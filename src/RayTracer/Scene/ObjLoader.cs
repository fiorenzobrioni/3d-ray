using System.Globalization;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Scene;

/// <summary>
/// Wavefront OBJ reader (PIANO-REVIEW.md Phase 2).
///
/// Reads <c>v</c>, <c>vn</c>, <c>vt</c> and <c>f</c> (any number of corners;
/// <c>v</c>, <c>v/vt</c>, <c>v/vt/vn</c>, <c>v//vn</c>; negative indices),
/// <c>mtllib</c> and <c>usemtl</c> (the faces' materials, from the MTL library,
/// <see cref="MtlLoader"/>) and <c>s</c> (smoothing groups: a face without
/// normals in a group is shaded with the angle-weighted normals of the
/// group's faces around each vertex; <c>s off</c> or <c>s 0</c> is flat).
/// <c>o</c>, <c>g</c>, lines and points are ignored. Polygons are triangulated
/// by <see cref="PolyMesh.TriangulateFace"/> (concave ones ear-clipped).
/// OBJ is right-handed with Y up, as the renderer.
/// </summary>
public static class ObjLoader
{
    /// <summary>
    /// Loads an OBJ file and returns a <see cref="Mesh"/> with an internal BVH.
    /// </summary>
    /// <param name="path">Path to the .obj file.</param>
    /// <param name="material">Material to apply to all faces.</param>
    /// <param name="warnings">Optional list to collect non-fatal parse warnings.</param>
    /// <returns>A Mesh containing all parsed triangles, or null if the file is empty/invalid.</returns>
    public static Mesh? Load(string path, IMaterial material, List<string>? warnings = null)
        => Load(path, material, SubdivisionOptions.Disabled,
                null, 0, warnings, out _, out _, out _);

    /// <summary>
    /// Loads an OBJ file and returns a <see cref="Mesh"/>, optionally running
    /// a subdivision pass (Loop / Catmull-Clark) before building the BVH.
    /// </summary>
    /// <param name="path">Path to the .obj file.</param>
    /// <param name="material">Material to apply to all faces.</param>
    /// <param name="subdivision">Subdivision parameters; when
    /// <see cref="SubdivisionOptions.IsActive"/> is false the legacy path
    /// (direct fan-triangulation) runs.</param>
    /// <param name="warnings">Optional list to collect non-fatal parse warnings.</param>
    /// <param name="appliedScheme">Receives the actually applied scheme after
    /// <see cref="SubdivisionScheme.Auto"/> resolution (or
    /// <see cref="SubdivisionScheme.None"/> when no subdivision ran).</param>
    /// <param name="appliedIterations">Receives the number of subdivision
    /// iterations actually performed.</param>
    public static Mesh? Load(string path, IMaterial material,
        SubdivisionOptions subdivision, List<string>? warnings,
        out SubdivisionScheme appliedScheme, out int appliedIterations)
        => Load(path, material, subdivision, null, 0,
                warnings, out appliedScheme, out appliedIterations, out _);

    /// <summary>
    /// Loads an OBJ file with subdivision and surface-displacement support
    /// (Arnold/RenderMan/Cycles parity): subdivision builds the micro-mesh,
    /// displacement pushes its vertices, then the renderer's BVH is built on
    /// the displaced triangles. The displacement descriptor lives on the
    /// material (<see cref="IMaterial.Displacement"/>) so a single displaced
    /// material drives every mesh that uses it; the caller passes it in here
    /// to let callers route the per-entity <c>displacement_enabled</c>
    /// override.
    /// </summary>
    /// <param name="path">Path to the .obj file.</param>
    /// <param name="material">Material to apply to all faces.</param>
    /// <param name="subdivision">Subdivision parameters.</param>
    /// <param name="displacement">Material-level displacement descriptor.
    /// <c>null</c> disables both the geometric displacement pass and the
    /// autobump derivation.</param>
    /// <param name="objectSeed">Entity seed forwarded to texture lookups —
    /// procedural displacement textures use it to break repetition across
    /// instances.</param>
    /// <param name="warnings">Optional list of non-fatal parse warnings.</param>
    /// <param name="appliedScheme">Receives the actually applied scheme.</param>
    /// <param name="appliedIterations">Receives the iteration count run.</param>
    /// <param name="maxDisplacement">Receives the maximum displacement
    /// amplitude observed across all vertices. Callers compare against
    /// <see cref="MaterialDisplacement.Bound"/> to detect under-bounded scenes.</param>
    public static Mesh? Load(string path, IMaterial material,
        SubdivisionOptions subdivision, MaterialDisplacement? displacement,
        int objectSeed, List<string>? warnings,
        out SubdivisionScheme appliedScheme, out int appliedIterations,
        out float maxDisplacement)
    {
        appliedScheme = SubdivisionScheme.None;
        appliedIterations = 0;
        maxDisplacement = 0f;
        var imported = Read(path, warnings);
        if (imported == null) return null;
        var result = MeshImporter.Build(imported, material, null, material, subdivision,
                                        displacement, objectSeed, warnings);
        if (result == null) return null;
        appliedScheme = result.Scheme;
        appliedIterations = result.Iterations;
        maxDisplacement = result.MaxDisplacement;
        return (Mesh)result.Hittable; // one material: one mesh
    }

    /// <summary>Reads an OBJ file (and its MTL libraries) into an <see cref="ImportedMesh"/>.</summary>
    public static ImportedMesh? Read(string path, List<string>? warnings)
    {
        if (!File.Exists(path))
        {
            warnings?.Add($"OBJ file not found: {path}");
            return null;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var imported = new ImportedMesh();
        var poly = imported.Poly;
        var library = new Dictionary<string, IMaterial>(StringComparer.Ordinal);
        var faceVerts = new List<FaceVertex>(8);
        var faceSmoothing = new List<int>();
        int currentSlot = -1;
        int smoothing = 0;
        int lineNum = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNum++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                if (TryParseVec3(line, 2, out var v)) poly.Positions.Add(v);
                else warnings?.Add($"OBJ line {lineNum}: malformed vertex position");
            }
            else if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                if (TryParseVec3(line, 3, out var n)) poly.Normals!.Add(n);
                else warnings?.Add($"OBJ line {lineNum}: malformed vertex normal");
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                if (TryParseVec2(line, 3, out var uv)) poly.UVs!.Add(uv);
                else warnings?.Add($"OBJ line {lineNum}: malformed texture coordinate");
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                faceVerts.Clear();
                var tokens = line.Substring(2).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    if (TryParseFaceVertex(token.AsSpan(),
                            poly.Positions.Count, poly.Normals!.Count, poly.UVs!.Count, out var fv))
                        faceVerts.Add(fv);
                    else
                        warnings?.Add($"OBJ line {lineNum}: malformed face vertex '{token}'");
                }
                if (faceVerts.Count < 3)
                {
                    warnings?.Add($"OBJ line {lineNum}: face has fewer than 3 vertices, skipping");
                    continue;
                }

                bool faceHasNormals = true, faceHasUvs = true;
                int[] fp = new int[faceVerts.Count], fn = new int[faceVerts.Count], fu = new int[faceVerts.Count];
                for (int i = 0; i < faceVerts.Count; i++)
                {
                    var v = faceVerts[i];
                    fp[i] = v.V; fn[i] = v.VN; fu[i] = v.VT;
                    if (v.VN < 0) faceHasNormals = false;
                    if (v.VT < 0) faceHasUvs = false;
                }
                poly.FacePositions.Add(fp);
                poly.FaceNormals!.Add(faceHasNormals ? fn : Array.Empty<int>());
                poly.FaceUVs!.Add(faceHasUvs ? fu : Array.Empty<int>());
                if (currentSlot < 0) currentSlot = imported.Slot("");
                poly.FaceMaterials!.Add(currentSlot);
                faceSmoothing.Add(smoothing);
            }
            else if (line.StartsWith("usemtl", StringComparison.Ordinal))
            {
                string name = line.Length > 6 ? line[6..].Trim() : "";
                library.TryGetValue(name, out var m);
                if (m == null && name.Length > 0 && library.Count > 0)
                    warnings?.Add($"OBJ line {lineNum}: material '{name}' is not in the MTL libraries");
                currentSlot = imported.Slot(name, m);
            }
            else if (line.StartsWith("mtllib", StringComparison.Ordinal))
            {
                // One or more libraries; a file name with spaces is taken whole
                // when it exists as such.
                string rest = line.Length > 6 ? line[6..].Trim() : "";
                var names = File.Exists(Path.Combine(dir, rest))
                    ? new[] { rest }
                    : rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                foreach (var lib in names)
                    foreach (var (name, m) in MtlLoader.Load(Path.Combine(dir, lib), warnings))
                        library[name] = m;
            }
            else if (line.StartsWith("s ", StringComparison.Ordinal) || line == "s")
            {
                string arg = line.Length > 1 ? line[1..].Trim() : "off";
                smoothing = arg == "off" || !int.TryParse(arg, out int g) ? 0 : g;
            }
            // o, g, l, p and other statements: ignored.
        }

        if (poly.FaceCount == 0)
        {
            warnings?.Add("OBJ file produced zero faces.");
            return null;
        }
        // A slot named "" with no MTL material is the scene's fallback; drop
        // the slot list altogether when there is only that one.
        if (imported.SlotNames.Count == 1 && imported.SlotMaterials[0] == null) poly.FaceMaterials = null;

        SmoothingGroupNormals(poly, faceSmoothing);
        return imported;
    }

    /// <summary>
    /// Normals for the faces that have none and belong to a smoothing group:
    /// at each vertex, the angle-weighted mean of the normals of the faces of
    /// the same group around it.
    /// </summary>
    private static void SmoothingGroupNormals(PolyMesh poly, List<int> groups)
    {
        var sums = new Dictionary<(int Vertex, int Group), Vector3>();
        for (int fi = 0; fi < poly.FaceCount; fi++)
        {
            if (groups[fi] == 0 || poly.FaceNormals![fi].Length > 0) continue;
            int[] f = poly.FacePositions[fi];
            Vector3 n = LimitSurface.FaceNormal(poly, f);
            if (n == Vector3.Zero) continue;
            for (int k = 0; k < f.Length; k++)
            {
                var key = (f[k], groups[fi]);
                sums.TryGetValue(key, out var acc);
                sums[key] = acc + LimitSurface.CornerAngle(poly, f, k) * n;
            }
        }
        if (sums.Count == 0) return;
        var index = new Dictionary<(int, int), int>(sums.Count);
        foreach (var (key, sum) in sums)
        {
            index[key] = poly.Normals!.Count;
            float len = sum.Length();
            poly.Normals.Add(len > 0f ? sum / len : Vector3.UnitY);
        }
        for (int fi = 0; fi < poly.FaceCount; fi++)
        {
            if (groups[fi] == 0 || poly.FaceNormals![fi].Length > 0) continue;
            int[] f = poly.FacePositions[fi];
            var fn = new int[f.Length];
            bool all = true;
            for (int k = 0; k < f.Length; k++)
                if (index.TryGetValue((f[k], groups[fi]), out int ni)) fn[k] = ni; else all = false;
            if (all) poly.FaceNormals[fi] = fn;
        }
    }

    /// <summary>
    /// Recursively builds the <see cref="IBumpMap"/> used by
    /// <see cref="Mesh.AutoBump"/> from a material-level displacement
    /// descriptor. Returns <c>null</c> when no child contributes an autobump
    /// (the caller checks <see cref="MaterialDisplacement.RequestsAutobump"/>
    /// first so this is normally non-null).
    /// </summary>
    internal static IBumpMap? BuildAutobump(MaterialDisplacement disp)
    {
        if (disp is LeafDisplacement leaf)
        {
            if (!leaf.RequestsAutobump || leaf.Options.Texture == null)
                return null;
            // Bump amplitude is tied to the displacement amplitude (Arnold's
            // autobump magnitude convention) so the bump scale tracks the
            // macro relief without manual re-balancing.
            float strength = leaf.Options.AutobumpStrength
                             * MathF.Abs(leaf.Options.Scale);
            float uvScale  = MathF.Max(1e-6f,
                leaf.Options.UvScale * leaf.Options.AutobumpScale);
            return new BumpMapTexture(leaf.Options.Texture!, strength, uvScale);
        }
        if (disp is MixDisplacement mix)
        {
            var a = BuildAutobump(mix.A);
            var b = BuildAutobump(mix.B);
            if (a == null && b == null) return null;
            // When one side has no autobump we fall back to a flat "identity"
            // bump on that side so the mix still produces the active side's
            // perturbation, attenuated by the blend factor — matching Cycles'
            // behaviour when one shader's Displacement socket is unconnected.
            a ??= IdentityBump.Instance;
            b ??= IdentityBump.Instance;
            return new MixBumpMapTexture(a, b, mix.Parent.Mask, mix.Parent.Blend);
        }
        return null;
    }

    /// <summary>
    /// "No perturbation" bump: returns the +Z tangent-space normal at every
    /// sample. Used as the neutral element of the Mix blend when only one
    /// child of a <see cref="MixDisplacement"/> contributes an autobump.
    /// </summary>
    private sealed class IdentityBump : IBumpMap
    {
        public static readonly IdentityBump Instance = new();
        public System.Numerics.Vector3 SampleTangentNormal(float u, float v,
            System.Numerics.Vector3 p, in BumpFrame frame, int seed) => System.Numerics.Vector3.UnitZ;
    }

    /// <summary>Backwards-compatible 4-arg overload (no subdivision).</summary>
    public static Mesh? Load(string path, IMaterial material,
        SubdivisionOptions subdivision, List<string>? warnings = null)
        => Load(path, material, subdivision, warnings, out _, out _);

    // ═════════════════════════════════════════════════════════════════════════
    // Face vertex parsing
    // ═════════════════════════════════════════════════════════════════════════

    private struct FaceVertex
    {
        public int V;   // 0-based position index (-1 = missing)
        public int VT;  // 0-based texcoord index (-1 = missing)
        public int VN;  // 0-based normal index (-1 = missing)
    }

    /// <summary>
    /// Parses a face vertex token in one of the OBJ formats:
    ///   v, v/vt, v/vt/vn, v//vn
    /// Handles negative indices (relative to current list size).
    /// </summary>
    private static bool TryParseFaceVertex(ReadOnlySpan<char> token,
        int posCount, int normCount, int texCount, out FaceVertex fv)
    {
        fv = new FaceVertex { V = -1, VT = -1, VN = -1 };

        // Split on '/'
        Span<Range> parts = stackalloc Range[3];
        int count = token.Split(parts, '/', StringSplitOptions.None);

        if (count < 1) return false;

        // Position index (required)
        if (!TryParseIndex(token[parts[0]], posCount, out fv.V)) return false;

        // Texture coordinate index (optional)
        if (count >= 2 && parts[1].End.Value > parts[1].Start.Value)
            TryParseIndex(token[parts[1]], texCount, out fv.VT);

        // Normal index (optional)
        if (count >= 3 && parts[2].End.Value > parts[2].Start.Value)
            TryParseIndex(token[parts[2]], normCount, out fv.VN);

        return fv.V >= 0;
    }

    /// <summary>
    /// Parses a 1-based OBJ index (possibly negative) into a 0-based index.
    /// </summary>
    private static bool TryParseIndex(ReadOnlySpan<char> s, int listSize, out int index)
    {
        index = -1;
        if (s.IsEmpty) return false;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw))
            return false;
        if (raw > 0)
            index = raw - 1;         // OBJ is 1-based
        else if (raw < 0)
            index = listSize + raw;  // Negative = relative to end
        else
            return false;            // 0 is invalid in OBJ
        return index >= 0 && index < listSize;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Parsing helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static bool TryParseVec3(string line, int prefixLen, out Vector3 result)
    {
        result = Vector3.Zero;
        var span = line.AsSpan(prefixLen).Trim();
        Span<Range> parts = stackalloc Range[4];
        int count = span.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries);
        if (count < 3) return false;
        if (!float.TryParse(span[parts[0]], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
        if (!float.TryParse(span[parts[1]], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
        if (!float.TryParse(span[parts[2]], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
        result = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseVec2(string line, int prefixLen, out Vector2 result)
    {
        result = Vector2.Zero;
        var span = line.AsSpan(prefixLen).Trim();
        Span<Range> parts = stackalloc Range[3];
        int count = span.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries);
        if (count < 2) return false;
        if (!float.TryParse(span[parts[0]], NumberStyles.Float, CultureInfo.InvariantCulture, out float u)) return false;
        if (!float.TryParse(span[parts[1]], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return false;
        result = new Vector2(u, v);
        return true;
    }

}
