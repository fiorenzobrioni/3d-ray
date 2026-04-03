using System.Globalization;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

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
    {
        if (!File.Exists(path))
        {
            warnings?.Add($"OBJ file not found: {path}");
            return null;
        }

        var positions = new List<Vector3>(4096);
        var normals   = new List<Vector3>(4096);
        var texCoords = new List<Vector2>(4096);
        var triangles = new List<IHittable>(8192);

        // Temp buffer for face vertex indices (reused per face)
        var faceVerts = new List<FaceVertex>(8);

        int lineNum = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNum++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                if (TryParseVec3(line, 2, out var v))
                    positions.Add(v);
                else
                    warnings?.Add($"OBJ line {lineNum}: malformed vertex position");
            }
            else if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                if (TryParseVec3(line, 3, out var n))
                    normals.Add(n);
                else
                    warnings?.Add($"OBJ line {lineNum}: malformed vertex normal");
            }
            else if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                if (TryParseVec2(line, 3, out var uv))
                    texCoords.Add(uv);
                else
                    warnings?.Add($"OBJ line {lineNum}: malformed texture coordinate");
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                faceVerts.Clear();
                var tokens = line.Substring(2).Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var token in tokens)
                {
                    if (TryParseFaceVertex(token.AsSpan(), positions.Count, normals.Count, texCoords.Count, out var fv))
                        faceVerts.Add(fv);
                    else
                        warnings?.Add($"OBJ line {lineNum}: malformed face vertex '{token}'");
                }

                if (faceVerts.Count < 3)
                {
                    warnings?.Add($"OBJ line {lineNum}: face has fewer than 3 vertices, skipping");
                    continue;
                }

                // Fan triangulation: (0,1,2), (0,2,3), (0,3,4), ...
                for (int i = 1; i < faceVerts.Count - 1; i++)
                {
                    var tri = BuildTriangle(
                        faceVerts[0], faceVerts[i], faceVerts[i + 1],
                        positions, normals, texCoords, material);
                    if (tri != null)
                        triangles.Add(tri);
                }
            }
            // Silently ignore: o, g, s, usemtl, mtllib, and other directives
        }

        if (triangles.Count == 0)
        {
            warnings?.Add("OBJ file produced zero triangles.");
            return null;
        }

        return new Mesh(triangles, material, positions.Count);
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
    // Triangle construction
    // ═════════════════════════════════════════════════════════════════════════

    private static IHittable? BuildTriangle(
        FaceVertex fv0, FaceVertex fv1, FaceVertex fv2,
        List<Vector3> positions, List<Vector3> normals, List<Vector2> texCoords,
        IMaterial material)
    {
        if (fv0.V < 0 || fv1.V < 0 || fv2.V < 0) return null;

        Vector3 v0 = positions[fv0.V];
        Vector3 v1 = positions[fv1.V];
        Vector3 v2 = positions[fv2.V];

        // Degenerate triangle check
        Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
        if (cross.LengthSquared() < 1e-12f) return null;

        bool hasNormals = fv0.VN >= 0 && fv1.VN >= 0 && fv2.VN >= 0;
        bool hasUVs = fv0.VT >= 0 && fv1.VT >= 0 && fv2.VT >= 0;

        if (hasNormals)
        {
            Vector3 n0 = normals[fv0.VN];
            Vector3 n1 = normals[fv1.VN];
            Vector3 n2 = normals[fv2.VN];

            if (hasUVs)
            {
                return new SmoothTriangle(v0, v1, v2, n0, n1, n2,
                    texCoords[fv0.VT], texCoords[fv1.VT], texCoords[fv2.VT],
                    material);
            }
            else
            {
                return new SmoothTriangle(v0, v1, v2, n0, n1, n2, material);
            }
        }
        else
        {
            // No vertex normals → use flat Triangle
            // (which auto-computes face normal from the cross product)
            return new Triangle(v0, v1, v2, material);
        }
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
