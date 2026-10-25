using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Scene;

/// <summary>
/// A model file read into a polygon mesh: positions, optional per-corner
/// normals and UVs, and a material slot per face with the materials the file
/// itself describes (MTL, glTF), each by name.
/// </summary>
public sealed class ImportedMesh
{
    internal PolyMesh Poly { get; } = new()
    {
        Normals = new List<Vector3>(),
        UVs = new List<Vector2>(),
        FaceNormals = new List<int[]>(),
        FaceUVs = new List<int[]>(),
        FaceMaterials = new List<int>(),
    };

    /// <summary>Name of each material slot (faces refer to slots by index).</summary>
    public List<string> SlotNames { get; } = new();

    /// <summary>The file's own material for each slot, null where it has none.</summary>
    public List<IMaterial?> SlotMaterials { get; } = new();

    /// <summary>The slot of the material called <paramref name="name"/>, added if new.</summary>
    public int Slot(string name, IMaterial? material = null)
    {
        int i = SlotNames.IndexOf(name);
        if (i >= 0) return i;
        SlotNames.Add(name);
        SlotMaterials.Add(material);
        return SlotNames.Count - 1;
    }

    public int FaceCount => Poly.FaceCount;
    public int VertexCount => Poly.Positions.Count;
}

/// <summary>What <see cref="MeshImporter.Build"/> made, for the scene's log.</summary>
public sealed record ImportResult(IHittable Hittable, int FaceCount, int VertexCount,
    SubdivisionScheme Scheme, int Iterations, float MaxDisplacement, int MaterialCount);

/// <summary>
/// Reads model files (Wavefront OBJ with MTL, glTF 2.0, PLY) and turns them
/// into renderable meshes through one pipeline (PIANO-REVIEW.md Phase 2):
/// subdivision onto the limit surface, displacement, triangulation (concave
/// polygons ear-clipped), MikkTSpace tangents, and one <see cref="Mesh"/> per
/// material, so each keeps its own area-light sampling.
/// </summary>
public static class MeshImporter
{
    /// <summary>The model formats read, by extension.</summary>
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".obj" or ".gltf" or ".glb" or ".ply";

    public static ImportedMesh? Read(string path, List<string>? warnings)
    {
        if (!File.Exists(path))
        {
            warnings?.Add($"model file not found: {path}");
            return null;
        }
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ObjLoader.Read(path, warnings),
            ".gltf" or ".glb" => GltfLoader.Read(path, warnings),
            ".ply" => PlyLoader.Read(path, warnings),
            var ext => Unsupported(ext),
        };

        ImportedMesh? Unsupported(string ext)
        {
            warnings?.Add($"unsupported model format '{ext}' (expected .obj, .gltf, .glb or .ply)");
            return null;
        }
    }

    /// <summary>
    /// Builds the renderable mesh. Materials: <paramref name="entityMaterial"/>,
    /// when given, covers every face; otherwise each slot takes the scene
    /// material its name maps to in <paramref name="overrides"/>, else the
    /// file's own, else <paramref name="fallback"/>.
    /// </summary>
    public static ImportResult? Build(ImportedMesh imported, IMaterial? entityMaterial,
        IReadOnlyDictionary<string, IMaterial>? overrides, IMaterial fallback,
        SubdivisionOptions subdivision, MaterialDisplacement? displacement, int objectSeed,
        List<string>? warnings)
    {
        var poly = imported.Poly;
        if (poly.FaceCount == 0)
        {
            warnings?.Add("the model has no faces.");
            return null;
        }

        int slotCount = Math.Max(1, imported.SlotNames.Count);
        var materials = new IMaterial[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            string? name = i < imported.SlotNames.Count ? imported.SlotNames[i] : null;
            materials[i] = entityMaterial
                ?? (name != null && overrides != null && overrides.TryGetValue(name, out var o) ? o : null)
                ?? (i < imported.SlotMaterials.Count ? imported.SlotMaterials[i] : null)
                ?? fallback;
        }
        if (poly.FaceMaterials is { Count: > 0 } fm && fm.Count != poly.FaceCount) poly.FaceMaterials = null;
        if (poly.FaceMaterials is { Count: 0 }) poly.FaceMaterials = null;

        int vertexCount = poly.Positions.Count;
        bool wantsDisplace = displacement?.RequestsGeometricDisplacement == true;

        // Subdivision and displacement build their own normals and need the
        // UVs on every face or on none.
        if (subdivision.IsActive || wantsDisplace)
        {
            poly.Normals = null;
            poly.FaceNormals = null;
            if (poly.FaceUVs != null && Partial(poly.FaceUVs))
            {
                warnings?.Add("texture coordinates are missing on some faces: dropped for subdivision/displacement.");
                poly.UVs = null;
                poly.FaceUVs = null;
            }
        }
        if (poly.FaceNormals != null && !poly.FaceNormals.Exists(f => f.Length > 0)) { poly.Normals = null; poly.FaceNormals = null; }
        if (poly.FaceUVs != null && !poly.FaceUVs.Exists(f => f.Length > 0)) { poly.UVs = null; poly.FaceUVs = null; }

        var scheme = SubdivisionScheme.None;
        int iterations = 0;
        if (subdivision.IsActive)
            (iterations, scheme) = SubdivisionEngine.Apply(poly, subdivision);

        float maxDisplacement = 0f;
        if (wantsDisplace)
        {
            maxDisplacement = displacement switch
            {
                LeafDisplacement leaf => DisplacementEngine.Apply(poly, leaf.Options, objectSeed),
                MixDisplacement mix => DisplacementEngine.ApplyMix(poly, mix, objectSeed),
                _ => 0f,
            };
        }

        vertexCount = poly.Positions.Count; // after subdivision
        var triangles = iterations > 0 || wantsDisplace
            ? SubdivisionEngine.Triangulate(poly, materials)
            : BuildTrianglesDirect(poly, materials);
        if (triangles.Count == 0)
        {
            warnings?.Add("the model produced no triangles.");
            return null;
        }

        // Per-vertex tangents in the frame normal-map bakers use (MikkTSpace).
        MeshTangents.Generate(triangles);

        // One mesh per material: each is sampled as a light on its own.
        float bound = wantsDisplace ? MathF.Max(0f, displacement!.Bound) : 0f;
        IBumpMap? autobump = displacement is { RequestsAutobump: true } ? ObjLoader.BuildAutobump(displacement) : null;
        var byMaterial = new Dictionary<IMaterial, List<IHittable>>(ReferenceEqualityComparer.Instance);
        var order = new List<IMaterial>();
        foreach (var t in triangles)
        {
            var m = t is SmoothTriangle st ? st.Material : ((Triangle)t).Material;
            if (!byMaterial.TryGetValue(m, out var list)) { byMaterial[m] = list = new List<IHittable>(); order.Add(m); }
            list.Add(t);
        }
        var meshes = new List<IHittable>(order.Count);
        foreach (var m in order)
        {
            var mesh = new Mesh(byMaterial[m], m, vertexCount, bound);
            if (autobump != null) mesh.AutoBump = autobump;
            meshes.Add(mesh);
        }
        IHittable result = meshes.Count == 1 ? meshes[0] : new Group(meshes);
        return new ImportResult(result, triangles.Count, vertexCount, scheme, iterations, maxDisplacement, order.Count);
    }

    private static bool Partial(List<int[]> channel)
    {
        bool full = false, empty = false;
        foreach (var f in channel)
        {
            if (f.Length == 0) empty = true; else full = true;
            if (full && empty) return true;
        }
        return false;
    }

    /// <summary>
    /// Triangles straight from the file's polygons, with its own normals and
    /// UVs face by face: shaded with the given normals where a face has them,
    /// flat where it has none (its UVs kept either way).
    /// </summary>
    internal static List<IHittable> BuildTrianglesDirect(PolyMesh poly, IReadOnlyList<IMaterial> materials)
    {
        var output = new List<IHittable>(poly.FaceCount * 2);
        for (int fi = 0; fi < poly.FaceCount; fi++)
        {
            int[] f = poly.FacePositions[fi];
            int[]? fn = poly.FaceNormals?[fi];
            int[]? fu = poly.FaceUVs?[fi];
            bool hasN = fn is { Length: > 0 };
            bool hasU = fu is { Length: > 0 };
            IMaterial material = materials[poly.FaceMaterials?[fi] ?? 0];

            foreach (var (a, b, c) in poly.TriangulateFace(f))
            {
                Vector3 v0 = poly.Positions[f[a]], v1 = poly.Positions[f[b]], v2 = poly.Positions[f[c]];
                Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                if (cross == Vector3.Zero) continue;

                if (hasN || hasU)
                {
                    Vector3 face = Vector3.Normalize(cross);
                    Vector3 n0 = hasN ? poly.Normals![fn![a]] : face;
                    Vector3 n1 = hasN ? poly.Normals![fn![b]] : face;
                    Vector3 n2 = hasN ? poly.Normals![fn![c]] : face;
                    // A zero normal in the file: the face's own.
                    if (n0 == Vector3.Zero) n0 = face;
                    if (n1 == Vector3.Zero) n1 = face;
                    if (n2 == Vector3.Zero) n2 = face;
                    output.Add(hasU
                        ? new SmoothTriangle(v0, v1, v2, n0, n1, n2, poly.UVs![fu![a]], poly.UVs[fu[b]], poly.UVs[fu[c]], material)
                        : new SmoothTriangle(v0, v1, v2, n0, n1, n2, material));
                }
                else output.Add(new Triangle(v0, v1, v2, material));
            }
        }
        return output;
    }
}
