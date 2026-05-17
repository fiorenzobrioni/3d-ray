using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Surface displacement on a (sub)divided <see cref="PolyMesh"/>. Supports
/// both <see cref="DisplacementMode.Scalar"/> (height-field along the
/// smooth normal, DEVLOG step 3) and <see cref="DisplacementMode.Vector"/>
/// (RGB → XYZ offset, DEVLOG step 4).
///
/// <para>
/// Pipeline position: runs <i>after</i> the Loop / Catmull-Clark subdivision
/// pass and <i>before</i> the final triangulation in
/// <see cref="SubdivisionEngine"/>. For scalar mode the displacement
/// direction is the angle-weighted average of incident face normals on the
/// limit topology (Max 1999 — the Blender/Maya/OpenSubdiv default for
/// smooth shading), so the surface inflates along the same "shading normal"
/// the renderer would otherwise see — which is the canonical
/// Arnold/RenderMan/Cycles convention for height-field displacement.
/// </para>
///
/// <para>
/// For vector mode the engine additionally builds a per-vertex TBN basis
/// from UV gradients (Lengyel 2001 face-tangent formula, angle-weighted
/// aggregation, Gram-Schmidt orthonormalisation against the smooth normal,
/// MikkTSpace-style handedness preservation). The R/G/B channels of the
/// sampled texture are then interpreted as offsets along T/B/N (tangent
/// space) or as <c>(x, y, z)</c> directly (object space). This matches
/// Mudbox / Maya / ZBrush / Cycles / Arnold's tangent-space vector
/// displacement convention.
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
/// </summary>
internal static class DisplacementEngine
{
    /// <summary>
    /// Applies the displacement options to <paramref name="mesh"/> in place.
    /// </summary>
    /// <param name="mesh">PolyMesh post-subdivision, pre-triangulation.</param>
    /// <param name="options">Displacement parameters.</param>
    /// <param name="objectSeed">Entity seed forwarded to <see cref="ITexture.Value"/>.</param>
    /// <returns>
    /// The maximum displacement amplitude observed across all vertices.
    /// In scalar mode this is the maximum of <c>|scale·(h−midlevel)|</c>;
    /// in vector mode it is the maximum Euclidean length of the applied
    /// offset vector. Callers can compare this against
    /// <see cref="DisplacementOptions.Bound"/> to detect under-bounded
    /// scenes and warn the user.
    /// </returns>
    public static float Apply(PolyMesh mesh, DisplacementOptions options, int objectSeed)
    {
        if (!options.IsActive || mesh.FaceCount == 0 || options.Texture == null)
            return 0f;

        var triFaces = TriangulateForNormals(mesh);
        var smoothNormals = ComputeAngleWeightedNormals(mesh, triFaces);

        float maxDisp = options.Mode == DisplacementMode.Vector
            ? ApplyVector(mesh, options, triFaces, smoothNormals, objectSeed)
            : ApplyScalar(mesh, options, smoothNormals, objectSeed);

        if (mesh.Normals != null && mesh.FaceNormals != null)
        {
            // Recompute the vertex-varying normal channel from the
            // displaced topology. The triangulator overwrites these again,
            // but doing it here keeps the PolyMesh self-consistent so any
            // intermediate consumer (debug dump, future passes) sees the
            // post-displacement shading normals.
            RecomputeVertexNormalsInPlace(mesh, triFaces);
        }

        return maxDisp;
    }

    /// <summary>
    /// Mix-displacement path. Vector-blends the per-vertex offsets of two
    /// child <see cref="LeafDisplacement"/>s by the parent
    /// <see cref="MixMaterial"/>'s mask/blend factor evaluated AT the vertex.
    /// Matches Cycles' "Mix Shader → Displacement" socket: the same factor
    /// drives both the BSDF mix and the geometric displacement, producing a
    /// C0-continuous surface across material seams.
    ///
    /// <para>Pure-leaf children are required; nested MixDisplacement is
    /// rejected with a load-time warning at construction time so we don't
    /// have to handle recursion here. The blend math:
    /// <c>v' = v + (1−t)·offset_A(v) + t·offset_B(v)</c>, where each
    /// <c>offset_*(v)</c> is computed in that child's own basis (tangent /
    /// object / smooth-normal) and accumulated in object space.</para>
    /// </summary>
    public static float ApplyMix(PolyMesh mesh, MixDisplacement mix, int objectSeed)
    {
        if (mesh.FaceCount == 0) return 0f;

        var leafA = mix.A as LeafDisplacement;
        var leafB = mix.B as LeafDisplacement;
        if (leafA == null || leafB == null)
            return 0f; // nested mixes are rejected at load time

        var optsA = leafA.Options;
        var optsB = leafB.Options;
        var triFaces = TriangulateForNormals(mesh);
        var smoothNormals = ComputeAngleWeightedNormals(mesh, triFaces);

        // Vertex-varying UV lookup, shared between the two children.
        bool hasUv = mesh.HasUVs;
        var vertexUv = hasUv ? BuildVertexUvLookup(mesh, mesh.Positions.Count) : null;

        // Tangent-space children may need a TBN basis on the same topology.
        bool needsTangents =
            (leafA.RequestsGeometricDisplacement && optsA.Mode == DisplacementMode.Vector
                && optsA.Space == DisplacementSpace.Tangent && hasUv)
            ||
            (leafB.RequestsGeometricDisplacement && optsB.Mode == DisplacementMode.Vector
                && optsB.Space == DisplacementSpace.Tangent && hasUv);

        Vector3[]? tangents = null;
        Vector3[]? bitangents = null;
        if (needsTangents)
            (tangents, bitangents) = ComputeVertexTangents(
                mesh, triFaces, smoothNormals, vertexUv!);

        var positions = mesh.Positions;
        int vertexCount = positions.Count;
        var parent = mix.Parent;
        float maxLenSq = 0f;

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 p = positions[i];
            Vector2 uv = vertexUv != null ? vertexUv[i] : new Vector2(0f, 0f);

            Vector3 N = smoothNormals[i];
            Vector3 T = tangents     != null ? tangents[i]   : Vector3.Zero;
            Vector3 B = bitangents   != null ? bitangents[i] : Vector3.Zero;

            // Per-vertex blend factor — same evaluator the BSDF mix uses.
            float t = parent.EvaluateBlendFactor(uv.X, uv.Y, p, objectSeed);

            Vector3 offsetA = leafA.RequestsGeometricDisplacement
                ? ComputeOffset(optsA, p, uv, N, T, B, objectSeed)
                : Vector3.Zero;
            Vector3 offsetB = leafB.RequestsGeometricDisplacement
                ? ComputeOffset(optsB, p, uv, N, T, B, objectSeed)
                : Vector3.Zero;

            Vector3 offset = (1f - t) * offsetA + t * offsetB;
            float lenSq = offset.LengthSquared();
            if (lenSq > maxLenSq) maxLenSq = lenSq;

            positions[i] = p + offset;
        }

        if (mesh.Normals != null && mesh.FaceNormals != null)
            RecomputeVertexNormalsInPlace(mesh, triFaces);

        return MathF.Sqrt(maxLenSq);
    }

    /// <summary>
    /// Per-vertex displacement offset for a single <see cref="LeafDisplacement"/>.
    /// Identical math to the scalar / vector paths above, extracted so the
    /// Mix path can reuse it without duplicating the basis logic. Tangent /
    /// bitangent are read only in vector-tangent mode; the caller passes
    /// <see cref="Vector3.Zero"/> otherwise.
    /// </summary>
    private static Vector3 ComputeOffset(DisplacementOptions options,
        Vector3 p, Vector2 uv, Vector3 N, Vector3 T, Vector3 B, int objectSeed)
    {
        float uvScale = options.UvScale > 0f ? options.UvScale : 1f;
        float scale   = options.Scale;
        float midlevel = options.Midlevel;
        var texture   = options.Texture!;

        if (options.Mode == DisplacementMode.Scalar)
        {
            float h = MathUtils.Luminance(
                texture.Value(uv.X * uvScale, uv.Y * uvScale, p, objectSeed));
            return N * (scale * (h - midlevel));
        }

        Vector3 rgb = texture.Value(uv.X * uvScale, uv.Y * uvScale, p, objectSeed);
        Vector3 raw = new(rgb.X - midlevel, rgb.Y - midlevel, rgb.Z - midlevel);
        bool tangentSpace = options.Space == DisplacementSpace.Tangent
                            && T != Vector3.Zero;
        return tangentSpace
            ? scale * (T * raw.X + B * raw.Y + N * raw.Z)
            : scale * raw;
    }

    /// <summary>
    /// Scalar height-field path. Identical to step 3: every vertex moves by
    /// <c>scale · (luminance(texture) − midlevel)</c> along its smooth
    /// normal. Returns the maximum absolute scalar offset.
    /// </summary>
    private static float ApplyScalar(PolyMesh mesh, DisplacementOptions options,
        Vector3[] smoothNormals, int objectSeed)
    {
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
        var texture = options.Texture!;

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

        return maxAbsDisp;
    }

    /// <summary>
    /// Vector-displacement path. RGB → XYZ offset; the basis the offset is
    /// expressed in is either the per-vertex TBN (tangent space) or the
    /// identity (object space). Returns the maximum Euclidean length of
    /// the applied offset — this is what <c>displacement_bound</c>
    /// (Arnold's <c>disp_padding</c>) is meant to enclose.
    /// </summary>
    private static float ApplyVector(PolyMesh mesh, DisplacementOptions options,
        int[][] triFaces, Vector3[] smoothNormals, int objectSeed)
    {
        var positions = mesh.Positions;
        int vertexCount = positions.Count;

        bool tangentSpace = options.Space == DisplacementSpace.Tangent;
        bool hasUv = mesh.HasUVs;

        // Tangent-space mode without UVs cannot define a basis: the loader
        // already warns when the YAML asks for it, but the engine is a
        // library entry-point too so we silently fall back to object space
        // — the safe production-renderer behaviour (Arnold prints a
        // <i>"no UVs, falling back to object space"</i> diagnostic and
        // continues rendering).
        if (tangentSpace && !hasUv)
            tangentSpace = false;

        var vertexUv = hasUv
            ? BuildVertexUvLookup(mesh, vertexCount)
            : null;

        // Build the per-vertex tangent / bitangent only when needed: the
        // computation is non-trivial and the object-space path skips it
        // entirely.
        Vector3[]? tangents = null;
        Vector3[]? bitangents = null;
        if (tangentSpace)
            (tangents, bitangents) = ComputeVertexTangents(
                mesh, triFaces, smoothNormals, vertexUv!);

        float scale = options.Scale;
        float midlevel = options.Midlevel;
        float uvScale = options.UvScale > 0f ? options.UvScale : 1f;
        var texture = options.Texture!;

        float maxLenSq = 0f;

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 p = positions[i];
            Vector2 uv = vertexUv != null ? vertexUv[i] : new Vector2(0f, 0f);

            Vector3 rgb = texture.Value(uv.X * uvScale, uv.Y * uvScale, p, objectSeed);
            Vector3 raw = new(rgb.X - midlevel, rgb.Y - midlevel, rgb.Z - midlevel);

            Vector3 offset;
            if (tangentSpace)
            {
                // R → T, G → B, B → N — the standard Mudbox / Maya / Cycles
                // tangent-space vector-displacement convention.
                offset = scale * (tangents![i] * raw.X
                                + bitangents![i] * raw.Y
                                + smoothNormals[i]  * raw.Z);
            }
            else
            {
                // Object space: RGB is the offset triplet directly.
                offset = scale * raw;
            }

            float lenSq = offset.LengthSquared();
            if (lenSq > maxLenSq) maxLenSq = lenSq;

            positions[i] = p + offset;
        }

        return MathF.Sqrt(maxLenSq);
    }

    /// <summary>
    /// Builds the per-vertex tangent and bitangent fields from UV gradients
    /// on the displacement-time triangulation. Lengyel-style face tangents
    /// are accumulated angle-weighted, then orthonormalised against the
    /// smooth normal via Gram-Schmidt. Handedness is preserved from the
    /// accumulated bitangent so mirrored UV charts still produce a
    /// consistent TBN frame (MikkTSpace's convention).
    /// </summary>
    private static (Vector3[] T, Vector3[] B) ComputeVertexTangents(
        PolyMesh mesh, int[][] triFaces, Vector3[] smoothNormals, Vector2[] vertexUv)
    {
        int vertexCount = mesh.Positions.Count;
        var tAcc = new Vector3[vertexCount];
        var bAcc = new Vector3[vertexCount];

        foreach (var t in triFaces)
        {
            int i0 = t[0], i1 = t[1], i2 = t[2];
            Vector3 p0 = mesh.Positions[i0];
            Vector3 p1 = mesh.Positions[i1];
            Vector3 p2 = mesh.Positions[i2];

            Vector2 uv0 = vertexUv[i0];
            Vector2 uv1 = vertexUv[i1];
            Vector2 uv2 = vertexUv[i2];

            Vector3 e1 = p1 - p0;
            Vector3 e2 = p2 - p0;
            Vector2 du1 = uv1 - uv0;
            Vector2 du2 = uv2 - uv0;

            float denom = du1.X * du2.Y - du2.X * du1.Y;
            if (MathF.Abs(denom) < 1e-8f) continue; // Degenerate UV chart for this face
            float r = 1f / denom;

            Vector3 tFace = (e1 * du2.Y - e2 * du1.Y) * r;
            Vector3 bFace = (e2 * du1.X - e1 * du2.X) * r;

            // Skip face if the position triangle is degenerate too — the
            // angle weighting below would blow up.
            Vector3 e01 = p1 - p0;
            Vector3 e12 = p2 - p1;
            Vector3 e20 = p0 - p2;
            float faceCrossLen = Vector3.Cross(e01, -e20).Length();
            if (faceCrossLen < 1e-12f) continue;

            float a0 = CornerAngle( e01, -e20);
            float a1 = CornerAngle(-e01,  e12);
            float a2 = CornerAngle(-e12,  e20);

            tAcc[i0] += tFace * a0; bAcc[i0] += bFace * a0;
            tAcc[i1] += tFace * a1; bAcc[i1] += bFace * a1;
            tAcc[i2] += tFace * a2; bAcc[i2] += bFace * a2;
        }

        var tOut = new Vector3[vertexCount];
        var bOut = new Vector3[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 N = smoothNormals[i];
            Vector3 T = tAcc[i];
            Vector3 B = bAcc[i];

            // Gram-Schmidt: project T onto the plane perpendicular to N.
            Vector3 tOrt = T - Vector3.Dot(T, N) * N;
            float tLen = tOrt.Length();
            if (tLen > 1e-8f)
            {
                tOrt /= tLen;
            }
            else
            {
                // Vertex without a usable UV gradient: pick any orthogonal
                // axis to N. Mirrors GLM's tangent fallback (cross with the
                // smaller-component basis axis to avoid degeneracy).
                Vector3 fallback = MathF.Abs(N.X) < 0.9f
                    ? new Vector3(1, 0, 0)
                    : new Vector3(0, 1, 0);
                tOrt = Vector3.Normalize(fallback - Vector3.Dot(fallback, N) * N);
            }

            // Reconstruct the bitangent as N × T, but flip its sign to match
            // the accumulated B's handedness (preserves mirrored-UV charts).
            Vector3 nCrossT = Vector3.Cross(N, tOrt);
            if (Vector3.Dot(B, nCrossT) < 0f)
                nCrossT = -nCrossT;

            tOut[i] = tOrt;
            bOut[i] = nCrossT;
        }

        return (tOut, bOut);
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
