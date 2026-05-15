using System.Globalization;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Scene;

/// <summary>
/// Wavefront OBJ file parser.
///
/// Supports:
///   • <c>v</c>  — vertex positions (x y z)
///   • <c>vn</c> — vertex normals (nx ny nz)
///   • <c>vt</c> — texture coordinates (u v)
///   • <c>f</c>  — faces (triangles and quads, auto-triangulated)
///   • Face index formats: <c>v</c>, <c>v/vt</c>, <c>v/vt/vn</c>, <c>v//vn</c>
///   • Negative indices (relative to current vertex count, per OBJ spec)
///   • <c>#</c> comments and blank lines
///   • <c>o</c> / <c>g</c> / <c>s</c> / <c>usemtl</c> / <c>mtllib</c> — parsed but ignored
///     (single-material mesh; MTL support can be added later)
///
/// When vertex normals are absent, flat face normals are computed automatically.
/// When texture coordinates are absent, barycentric UVs are assigned.
/// Polygons with more than 4 vertices are fan-triangulated from the first vertex.
///
/// <b>Performance:</b>
/// The parser is single-pass and allocation-conscious. Vertex/normal/UV lists
/// are pre-sized when possible (many OBJ exporters emit a comment like
/// "# N vertices" that we can use as a hint). The returned <see cref="Mesh"/>
/// wraps all triangles in an internal BVH for O(log N) intersection.
///
/// <b>Coordinate system:</b>
/// OBJ uses a right-handed coordinate system with Y up, same as our ray tracer.
/// No axis swapping is needed. If a model appears mirrored, the exporter may
/// have used Z-up (Blender default) — use Transform rotate to fix.
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
                DisplacementOptions.Disabled, 0, warnings, out _, out _, out _);

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
        => Load(path, material, subdivision, DisplacementOptions.Disabled, 0,
                warnings, out appliedScheme, out appliedIterations, out _);

    /// <summary>
    /// Loads an OBJ file with subdivision and scalar displacement support.
    /// Step 3 of the surface-displacement stack (Arnold/RenderMan/Cycles
    /// parity): subdivision builds the micro-mesh, displacement pushes its
    /// vertices along the limit-surface smooth normal, then the renderer's
    /// BVH is built on the displaced triangles.
    /// </summary>
    /// <param name="path">Path to the .obj file.</param>
    /// <param name="material">Material to apply to all faces.</param>
    /// <param name="subdivision">Subdivision parameters.</param>
    /// <param name="displacement">Scalar displacement parameters; when
    /// <see cref="DisplacementOptions.IsActive"/> is false the legacy
    /// (no-displacement) path runs.</param>
    /// <param name="objectSeed">Entity seed forwarded to texture lookups —
    /// procedural displacement textures use it to break repetition across
    /// instances.</param>
    /// <param name="warnings">Optional list of non-fatal parse warnings.</param>
    /// <param name="appliedScheme">Receives the actually applied scheme.</param>
    /// <param name="appliedIterations">Receives the iteration count run.</param>
    /// <param name="maxDisplacement">Receives the maximum |scale·(h−midlevel)|
    /// across all displaced vertices. Callers compare against
    /// <see cref="DisplacementOptions.Bound"/> to detect under-bounded scenes.</param>
    public static Mesh? Load(string path, IMaterial material,
        SubdivisionOptions subdivision, DisplacementOptions displacement,
        int objectSeed, List<string>? warnings,
        out SubdivisionScheme appliedScheme, out int appliedIterations,
        out float maxDisplacement)
    {
        appliedScheme = SubdivisionScheme.None;
        appliedIterations = 0;
        maxDisplacement = 0f;

        if (!File.Exists(path))
        {
            warnings?.Add($"OBJ file not found: {path}");
            return null;
        }

        // ── Parse OBJ into a PolyMesh (preserves face arity & attribute
        // indices). Even the legacy non-subdivision path runs through here
        // — we just skip the SubdivisionEngine.Apply call.
        // ─────────────────────────────────────────────────────────────────
        var poly = new PolyMesh
        {
            Normals    = new List<Vector3>(4096),
            UVs        = new List<Vector2>(4096),
            FaceNormals = new List<int[]>(8192),
            FaceUVs     = new List<int[]>(8192),
        };

        var faceVerts = new List<FaceVertex>(8);

        int lineNum = 0;
        bool anyNormalsInFile = false;
        bool anyUvsInFile     = false;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNum++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                if (TryParseVec3(line, 2, out var v))
                    poly.Positions.Add(v);
                else
                    warnings?.Add($"OBJ line {lineNum}: malformed vertex position");
            }
            else if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                if (TryParseVec3(line, 3, out var n))
                    poly.Normals!.Add(n);
                else
                    warnings?.Add($"OBJ line {lineNum}: malformed vertex normal");
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                if (TryParseVec2(line, 3, out var uv))
                    poly.UVs!.Add(uv);
                else
                    warnings?.Add($"OBJ line {lineNum}: malformed texture coordinate");
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                faceVerts.Clear();
                var tokens = line.Substring(2).Split(' ', StringSplitOptions.RemoveEmptyEntries);

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

                bool faceHasNormals = true;
                bool faceHasUvs = true;
                int[] fp = new int[faceVerts.Count];
                int[] fn = new int[faceVerts.Count];
                int[] fu = new int[faceVerts.Count];
                for (int i = 0; i < faceVerts.Count; i++)
                {
                    var v = faceVerts[i];
                    fp[i] = v.V;
                    fn[i] = v.VN;
                    fu[i] = v.VT;
                    if (v.VN < 0) faceHasNormals = false;
                    if (v.VT < 0) faceHasUvs    = false;
                }
                poly.FacePositions.Add(fp);
                if (faceHasNormals) { poly.FaceNormals!.Add(fn); anyNormalsInFile = true; }
                else                  poly.FaceNormals!.Add(Array.Empty<int>());
                if (faceHasUvs)     { poly.FaceUVs!    .Add(fu); anyUvsInFile     = true; }
                else                  poly.FaceUVs!    .Add(Array.Empty<int>());
            }
            // Silently ignore: o, g, s, usemtl, mtllib, and other directives
        }

        // Drop side-channel attributes the file didn't define on *every*
        // face — the subdivision engine wants a consistent channel or none.
        if (!anyNormalsInFile || HasPartialChannel(poly.FaceNormals!))
            { poly.Normals = null; poly.FaceNormals = null; }
        if (!anyUvsInFile || HasPartialChannel(poly.FaceUVs!))
            { poly.UVs = null; poly.FaceUVs = null; }

        if (poly.FaceCount == 0)
        {
            warnings?.Add("OBJ file produced zero faces.");
            return null;
        }

        int vertexCount = poly.Positions.Count;

        // ── Optional subdivision pass ────────────────────────────────────
        if (subdivision.IsActive)
        {
            var (iters, scheme) = SubdivisionEngine.Apply(poly, subdivision);
            appliedScheme = scheme;
            appliedIterations = iters;
        }

        // ── Optional scalar displacement pass ────────────────────────────
        // Runs after subdivision and before triangulation so the micro-mesh
        // built by Loop / Catmull-Clark is what gets displaced (the silhouette
        // resolution scales with subdivision_iterations, exactly as in
        // Arnold/RenderMan). Without prior subdivision a low-poly OBJ
        // displaces too coarsely to be visually useful — we still allow it
        // (matches the "displace without subdivide" sanity path used by tests
        // and minimal scenes) but expect authors to combine the two.
        if (displacement.IsActive)
        {
            maxDisplacement = DisplacementEngine.Apply(poly, displacement, objectSeed);
        }

        // ── Build SmoothTriangle list ────────────────────────────────────
        List<IHittable> triangles;
        if (appliedIterations > 0 || displacement.IsActive)
        {
            // After subdivision or displacement the per-vertex normals must
            // come from the (possibly displaced) limit topology — the
            // triangulator handles the angle-weighted recompute on its own,
            // so we always route through it once either pass has run.
            triangles = SubdivisionEngine.Triangulate(poly, material);
        }
        else
        {
            // Legacy direct path: triangulate using the face/normal/uv
            // indices straight from the OBJ. This preserves the historical
            // behaviour byte-for-byte when no subdivision is requested.
            triangles = BuildTrianglesDirect(poly, material);
        }

        if (triangles.Count == 0)
        {
            warnings?.Add("OBJ file produced zero triangles.");
            return null;
        }

        // displacement_bound: pad each BVH leaf AABB by the artist-supplied
        // safety margin. The displaced positions are already baked into the
        // triangles, so the padding is a defensive measure — it absorbs
        // shading-time bump perturbation and matches the contract surfaced
        // by Arnold's disp_padding / RenderMan's dispBound.
        float bound = displacement.IsActive ? MathF.Max(0f, displacement.Bound) : 0f;
        var mesh = new Mesh(triangles, material, vertexCount, bound);

        // Step 5 of the surface-displacement stack: optional "autobump"
        // residual bump map derived from the same displacement texture.
        // We compose it onto the mesh — not the material — so two meshes
        // sharing the same material can independently opt in. The strength
        // is tied to the displacement amplitude (Arnold's autobump magnitude
        // convention); authors dial AutobumpStrength to override the ratio.
        if (displacement.IsAutobumpActive)
        {
            float strength = displacement.AutobumpStrength * MathF.Abs(displacement.Scale);
            float uvScale  = MathF.Max(1e-6f,
                displacement.UvScale * displacement.AutobumpScale);
            mesh.AutoBump = new BumpMapTexture(displacement.Texture!, strength, uvScale);
        }

        return mesh;
    }

    /// <summary>Backwards-compatible 4-arg overload (no subdivision).</summary>
    public static Mesh? Load(string path, IMaterial material,
        SubdivisionOptions subdivision, List<string>? warnings = null)
        => Load(path, material, subdivision, warnings, out _, out _);

    /// <summary>
    /// Direct fan-triangulation of a <see cref="PolyMesh"/> using the
    /// per-face attribute indices captured during parsing. Reproduces the
    /// historical OBJ loader output bit-for-bit when subdivision is off.
    /// </summary>
    private static List<IHittable> BuildTrianglesDirect(PolyMesh poly, IMaterial material)
    {
        var output = new List<IHittable>(poly.FaceCount * 2);
        for (int fi = 0; fi < poly.FaceCount; fi++)
        {
            int[] f  = poly.FacePositions[fi];
            int[]? fn = poly.FaceNormals != null ? poly.FaceNormals[fi] : null;
            int[]? fu = poly.FaceUVs     != null ? poly.FaceUVs[fi]     : null;

            bool hasN = fn is { Length: > 0 };
            bool hasU = fu is { Length: > 0 };

            for (int k = 1; k < f.Length - 1; k++)
            {
                Vector3 v0 = poly.Positions[f[0]];
                Vector3 v1 = poly.Positions[f[k]];
                Vector3 v2 = poly.Positions[f[k + 1]];

                Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                if (cross.LengthSquared() < 1e-12f) continue;

                if (hasN && hasU)
                {
                    output.Add(new SmoothTriangle(
                        v0, v1, v2,
                        poly.Normals![fn![0]], poly.Normals[fn[k]], poly.Normals[fn[k + 1]],
                        poly.UVs![fu![0]],     poly.UVs[fu[k]],     poly.UVs[fu[k + 1]],
                        material));
                }
                else if (hasN)
                {
                    output.Add(new SmoothTriangle(
                        v0, v1, v2,
                        poly.Normals![fn![0]], poly.Normals[fn[k]], poly.Normals[fn[k + 1]],
                        material));
                }
                else
                {
                    output.Add(new Triangle(v0, v1, v2, material));
                }
            }
        }
        return output;
    }

    private static bool HasPartialChannel(List<int[]> faceChannel)
    {
        bool sawFull = false, sawEmpty = false;
        foreach (var f in faceChannel)
        {
            if (f.Length == 0) sawEmpty = true;
            else               sawFull  = true;
            if (sawFull && sawEmpty) return true;
        }
        return false;
    }

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
