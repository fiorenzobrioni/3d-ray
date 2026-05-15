using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Scalar surface displacement on a (sub)divided <see cref="PolyMesh"/>.
///
/// <para>
/// Pipeline position: runs <i>after</i> the Loop / Catmull-Clark subdivision
/// pass and <i>before</i> the final triangulation in
/// <see cref="SubdivisionEngine"/>. The displacement direction is the
/// angle-weighted average of incident face normals on the limit topology
/// (Max 1999 — the Blender/Maya/OpenSubdiv default for smooth shading), so
/// the surface inflates along the same "shading normal" the renderer would
/// otherwise see — which is the canonical Arnold/RenderMan/Cycles convention
/// for height-field displacement.
/// </para>
///
/// <para>
/// After moving the vertices in place the engine recomputes the per-vertex
/// shading normals from the displaced topology — the silhouette has changed,
/// and the BSDF must see the new normal field, not the pre-displacement one.
/// The follow-up <see cref="SubdivisionEngine.Triangulate"/> call does the
/// same angle-weighted recompute on its own; we still update the
/// <see cref="PolyMesh.Normals"/> channel because subsequent stages or tools
/// may inspect it.
/// </para>
///
/// <para>
/// The implementation is intentionally minimal: a single pass over the
/// triangulated face list to accumulate the directional normals, a single
/// pass over the vertices to sample the texture and offset them, and one
/// final renormalisation. No per-face state survives beyond the call, so
/// running the engine twice produces idempotent results when given the same
/// inputs.
/// </para>
/// </summary>
internal static class DisplacementEngine
{
    /// <summary>
    /// Applies the displacement options to <paramref name="mesh"/> in place.
    /// </summary>
    /// <param name="mesh">PolyMesh post-subdivision, pre-triangulation.</param>
    /// <param name="options">Displacement parameters (texture, scale, midlevel, bound).</param>
    /// <param name="objectSeed">Entity seed forwarded to <see cref="ITexture.Value"/>.</param>
    /// <returns>
    /// The maximum |scale·(h−midlevel)| observed across all vertices. Callers
    /// can compare this against <see cref="DisplacementOptions.Bound"/> to
    /// detect under-bounded scenes and warn the user.
    /// </returns>
    public static float Apply(PolyMesh mesh, DisplacementOptions options, int objectSeed)
    {
        if (!options.IsActive || mesh.FaceCount == 0 || options.Texture == null)
            return 0f;

        var triFaces = TriangulateForNormals(mesh);
        var smoothNormals = ComputeAngleWeightedNormals(mesh, triFaces);

        var positions = mesh.Positions;
        int vertexCount = positions.Count;

        // Per-vertex UV: when the mesh carries a vertex-varying UV channel
        // the first incident face corner picks up the texture coordinate.
        // Vertices with multiple distinct UVs (seams) get the lowest-index
        // corner — same convention OpenSubdiv uses for "vertex-varying"
        // sampling. The procedural textures that ignore UVs (noise, marble,
        // wood, voronoi) sample on the position itself and stay seam-free.
        var vertexUv = mesh.HasUVs
            ? BuildVertexUvLookup(mesh, vertexCount)
            : null;

        float scale = options.Scale;
        float midlevel = options.Midlevel;
        float uvScale = options.UvScale > 0f ? options.UvScale : 1f;
        var texture = options.Texture;

        float maxAbsDisp = 0f;

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 p = positions[i];
            Vector2 uv = vertexUv != null ? vertexUv[i] : new Vector2(0f, 0f);

            float h = MathUtils.Luminance(
                texture.Value(uv.X * uvScale, uv.Y * uvScale, p, objectSeed));

            float disp = scale * (h - midlevel);
            float absDisp = MathF.Abs(disp);
            if (absDisp > maxAbsDisp) maxAbsDisp = absDisp;

            positions[i] = p + smoothNormals[i] * disp;
        }

        if (mesh.Normals != null && mesh.FaceNormals != null)
        {
            // Recompute the vertex-varying normal channel from the
            // displaced topology. The triangulator overwrites these again,
            // but doing it here keeps the PolyMesh self-consistent so any
            // intermediate consumer (debug dump, future passes) sees the
            // post-displacement shading normals.
            RecomputeVertexNormalsInPlace(mesh, triFaces);
        }

        return maxAbsDisp;
    }

    /// <summary>
    /// Fan-triangulates the face list into a flat array of <c>{v0, v1, v2}</c>
    /// triples for normal computation only. The mesh itself is left untouched
    /// — the proper triangulation happens later in
    /// <see cref="SubdivisionEngine.Triangulate"/>.
    /// </summary>
    private static int[][] TriangulateForNormals(PolyMesh mesh)
    {
        int count = 0;
        for (int fi = 0; fi < mesh.FaceCount; fi++)
            count += Math.Max(0, mesh.FacePositions[fi].Length - 2);

        var output = new int[count][];
        int o = 0;
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            for (int k = 1; k < f.Length - 1; k++)
                output[o++] = new[] { f[0], f[k], f[k + 1] };
        }
        return output;
    }

    /// <summary>
    /// Angle-weighted vertex normals (Max 1999) on a triangle list against
    /// the supplied positions. Allocates a fresh array sized to
    /// <c>mesh.Positions.Count</c>; entries are normalised in place and
    /// fall back to <see cref="Vector3.UnitY"/> when no incident triangle
    /// contributed (degenerate / orphan vertices).
    /// </summary>
    private static Vector3[] ComputeAngleWeightedNormals(PolyMesh mesh, int[][] triFaces)
    {
        var normals = new Vector3[mesh.Positions.Count];

        foreach (var t in triFaces)
        {
            Vector3 p0 = mesh.Positions[t[0]];
            Vector3 p1 = mesh.Positions[t[1]];
            Vector3 p2 = mesh.Positions[t[2]];

            Vector3 e01 = p1 - p0;
            Vector3 e12 = p2 - p1;
            Vector3 e20 = p0 - p2;

            Vector3 faceN = Vector3.Cross(e01, -e20);
            float len = faceN.Length();
            if (len < 1e-12f) continue;
            faceN /= len;

            float a0 = CornerAngle( e01, -e20);
            float a1 = CornerAngle(-e01,  e12);
            float a2 = CornerAngle(-e12,  e20);

            normals[t[0]] += faceN * a0;
            normals[t[1]] += faceN * a1;
            normals[t[2]] += faceN * a2;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            float len = normals[i].Length();
            normals[i] = len > 1e-12f ? normals[i] / len : Vector3.UnitY;
        }
        return normals;
    }

    /// <summary>
    /// Rebuilds the <see cref="PolyMesh.Normals"/> / <see cref="PolyMesh.FaceNormals"/>
    /// channels so they index the post-displacement shading normals. Used
    /// only when the mesh already carried a normals channel (i.e. the OBJ
    /// shipped explicit per-vertex normals or a previous pass populated it).
    /// </summary>
    private static void RecomputeVertexNormalsInPlace(PolyMesh mesh, int[][] triFaces)
    {
        var normals = ComputeAngleWeightedNormals(mesh, triFaces);

        mesh.Normals!.Clear();
        for (int i = 0; i < normals.Length; i++)
            mesh.Normals.Add(normals[i]);

        // Identity face-normal index = position index (vertex-varying).
        mesh.FaceNormals!.Clear();
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] fp = mesh.FacePositions[fi];
            int[] fn = new int[fp.Length];
            for (int k = 0; k < fp.Length; k++) fn[k] = fp[k];
            mesh.FaceNormals.Add(fn);
        }
    }

    /// <summary>
    /// Builds a per-position UV lookup, picking the first incident face
    /// corner's UV for each vertex. Faces that lack a UV channel (length 0
    /// from the OBJ parser's partial-channel filter) are skipped.
    /// </summary>
    private static Vector2[] BuildVertexUvLookup(PolyMesh mesh, int vertexCount)
    {
        var uvs = new Vector2[vertexCount];
        var seen = new bool[vertexCount];
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] fp = mesh.FacePositions[fi];
            int[] fu = mesh.FaceUVs![fi];
            if (fu.Length == 0) continue;
            for (int k = 0; k < fp.Length; k++)
            {
                int v = fp[k];
                if (seen[v]) continue;
                uvs[v] = mesh.UVs![fu[k]];
                seen[v] = true;
            }
        }
        return uvs;
    }

    private static float CornerAngle(Vector3 e1, Vector3 e2)
    {
        float len1 = e1.Length();
        float len2 = e2.Length();
        if (len1 < 1e-12f || len2 < 1e-12f) return 0f;
        float c = Vector3.Dot(e1, e2) / (len1 * len2);
        c = Math.Clamp(c, -1f, 1f);
        return MathF.Acos(c);
    }
}
