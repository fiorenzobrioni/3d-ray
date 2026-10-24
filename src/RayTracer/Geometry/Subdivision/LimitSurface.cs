using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Moves the vertices of a subdivided mesh onto the limit surface and gives
/// them its normals (PIANO-REVIEW.md Phase 2). After a few levels the control
/// mesh still lies off the surface the scheme converges to, by an amount the
/// next levels would remove; the limit masks (the left eigenvectors of the
/// subdivision matrix) give that surface's point and tangents directly:
///
/// <list type="bullet">
///   <item><description>Loop, smooth vertex of valence n:
///     <c>(ω·S + Σ eᵢ)/(ω + n)</c> with <c>ω = 3/(8β)</c>; tangents
///     <c>Σ cos(2πi/n) eᵢ</c> and <c>Σ sin(2πi/n) eᵢ</c> (Hoppe et al. 1994).</description></item>
///   <item><description>Catmull-Clark, smooth vertex of valence n:
///     <c>(n²·S + 4 Σ eᵢ + Σ dᵢ)/(n(n + 5))</c> with eᵢ the edge neighbours
///     and dᵢ the diagonal ones; tangents
///     <c>Σ Aₙ cos(2πi/n) eᵢ + (cos(2πi/n) + cos(2π(i+1)/n)) dᵢ</c> and the
///     same with sines, <c>Aₙ = 1 + cos(2π/n) + cos(π/n)·√(2(9 + cos(2π/n)))</c>
///     (Halstead, Kass and DeRose 1993).</description></item>
///   <item><description>Crease (both schemes): the cubic B-spline curve's
///     <c>(A + 4S + B)/6</c>.</description></item>
///   <item><description>Corner: the vertex itself.</description></item>
/// </list>
///
/// <para>The normal of a crease or corner vertex depends on the side: the
/// triangulation gives each face corner the normal of its own sector
/// (<see cref="SubdivisionEngine.Triangulate"/>). <see cref="PolyMesh.LimitNormals"/>
/// keeps one direction per vertex, the angle-weighted mean of all its faces,
/// which displacement moves the vertex along without opening the crease.
/// A crease still semi-sharp after the last level counts as sharp.</para>
/// </summary>
internal static class LimitSurface
{
    public static void Project(PolyMesh mesh, SubdivisionScheme scheme, BoundaryInterpolation boundary)
    {
        if (mesh.FaceCount == 0) return;
        var topo = new SubdivisionTopology(mesh, boundary);
        var directed = DirectedEdges(mesh);
        int count = mesh.Positions.Count;
        var limit = new Vector3[count];
        var normals = new Vector3[count];
        var control = AngleWeightedNormals(mesh);

        for (int v = 0; v < count; v++)
        {
            Vector3 s = mesh.Positions[v];
            switch (topo.Rules[v])
            {
                case VertexRule.Corner:
                    limit[v] = s;
                    continue;
                case VertexRule.Crease:
                    limit[v] = (mesh.Positions[topo.CreaseA[v]] + 4f * s + mesh.Positions[topo.CreaseB[v]]) / 6f;
                    continue;
            }

            var ring = OrderedFaces(mesh, v, topo, directed);
            bool quads = scheme == SubdivisionScheme.CatmullClark;
            if (ring == null || ring.Exists(c => mesh.FacePositions[c.Face].Length != (quads ? 4 : 3)))
            {
                limit[v] = s; // not a manifold ring of the scheme's faces: leave it
                normals[v] = control[v];
                continue;
            }

            int n = ring.Count;
            Vector3 t1 = Vector3.Zero, t2 = Vector3.Zero, sumE = Vector3.Zero, sumD = Vector3.Zero;
            float an = 1f + MathF.Cos(2f * MathF.PI / n) + MathF.Cos(MathF.PI / n) * MathF.Sqrt(2f * (9f + MathF.Cos(2f * MathF.PI / n)));
            for (int i = 0; i < n; i++)
            {
                int[] f = mesh.FacePositions[ring[i].Face];
                int k = ring[i].K;
                Vector3 e = mesh.Positions[f[(k + 1) % f.Length]];
                float c0 = MathF.Cos(2f * MathF.PI * i / n), s0 = MathF.Sin(2f * MathF.PI * i / n);
                sumE += e;
                if (quads)
                {
                    Vector3 d = mesh.Positions[f[(k + 2) % 4]];
                    sumD += d;
                    float c1 = MathF.Cos(2f * MathF.PI * (i + 1) / n), s1 = MathF.Sin(2f * MathF.PI * (i + 1) / n);
                    t1 += an * c0 * e + (c0 + c1) * d;
                    t2 += an * s0 * e + (s0 + s1) * d;
                }
                else
                {
                    t1 += c0 * e;
                    t2 += s0 * e;
                }
            }
            if (quads)
                limit[v] = (n * n * s + 4f * sumE + sumD) / (n * (n + 5f));
            else
            {
                float w = 3f / (8f * LoopSubdivider.Beta(n));
                limit[v] = (w * s + sumE) / (w + n);
            }

            Vector3 nrm = Vector3.Cross(t1, t2);
            float len = nrm.Length();
            if (len > 0f && float.IsFinite(len))
            {
                nrm /= len;
                normals[v] = Vector3.Dot(nrm, control[v]) < 0f ? -nrm : nrm;
            }
            else normals[v] = control[v];
        }

        for (int v = 0; v < count; v++) mesh.Positions[v] = limit[v];

        // Crease and corner vertices: the mean over all their faces, on the
        // limit positions.
        var after = AngleWeightedNormals(mesh);
        for (int v = 0; v < count; v++)
            if (topo.Rules[v] != VertexRule.Smooth || normals[v] == Vector3.Zero) normals[v] = after[v];
        mesh.LimitNormals = normals;
    }

    /// <summary>Directed edge (a → b) of a face → that face.</summary>
    internal static Dictionary<long, int> DirectedEdges(PolyMesh mesh)
    {
        var directed = new Dictionary<long, int>(mesh.FaceCount * 4);
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            for (int k = 0; k < f.Length; k++)
                directed[Directed(f[k], f[(k + 1) % f.Length])] = fi;
        }
        return directed;
    }

    private static long Directed(int a, int b) => ((long)a << 32) | (uint)b;

    /// <summary>
    /// The faces around an interior vertex in order, each with the vertex's
    /// index in it: in face i the vertex is followed by eᵢ (and, in a quad,
    /// the diagonal dᵢ), and preceded by eᵢ₊₁. Null if the faces do not close
    /// a single fan (a boundary or a non-manifold vertex).
    /// </summary>
    internal static List<(int Face, int K)>? OrderedFaces(PolyMesh mesh, int v, SubdivisionTopology topo,
                                                          Dictionary<long, int> directed)
    {
        var faces = topo.VertexFaces[v];
        if (faces.Count == 0) return null;
        var ring = new List<(int Face, int K)>(faces.Count);
        int face = faces[0];
        for (int step = 0; step < faces.Count; step++)
        {
            int[] f = mesh.FacePositions[face];
            int k = Array.IndexOf(f, v);
            if (k < 0) return null;
            ring.Add((face, k));
            int prev = f[(k - 1 + f.Length) % f.Length];
            if (!directed.TryGetValue(Directed(v, prev), out face)) return null;
        }
        return face == faces[0] ? ring : null;
    }

    /// <summary>
    /// Per-vertex normals as the mean of the incident faces' normals, each
    /// weighted by the face's angle at the vertex (Thürmer and Wüthrich 1998).
    /// </summary>
    internal static Vector3[] AngleWeightedNormals(PolyMesh mesh)
    {
        var normals = new Vector3[mesh.Positions.Count];
        for (int fi = 0; fi < mesh.FaceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            Vector3 fn = FaceNormal(mesh, f);
            if (fn == Vector3.Zero) continue;
            for (int k = 0; k < f.Length; k++)
                normals[f[k]] += CornerAngle(mesh, f, k) * fn;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            float len = normals[i].Length();
            normals[i] = len > 1e-20f ? normals[i] / len : Vector3.UnitY;
        }
        return normals;
    }

    /// <summary>Unit normal of a polygon (Newell's method), zero when degenerate.</summary>
    internal static Vector3 FaceNormal(PolyMesh mesh, int[] f)
    {
        Vector3 n = Vector3.Zero;
        for (int k = 0; k < f.Length; k++)
        {
            Vector3 a = mesh.Positions[f[k]], b = mesh.Positions[f[(k + 1) % f.Length]];
            n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        float len = n.Length();
        return len > 1e-30f ? n / len : Vector3.Zero;
    }

    /// <summary>The polygon's interior angle at its k-th vertex.</summary>
    internal static float CornerAngle(PolyMesh mesh, int[] f, int k)
    {
        Vector3 p = mesh.Positions[f[k]];
        Vector3 a = mesh.Positions[f[(k + 1) % f.Length]] - p;
        Vector3 b = mesh.Positions[f[(k - 1 + f.Length) % f.Length]] - p;
        float la = a.Length(), lb = b.Length();
        if (la <= 0f || lb <= 0f) return 0f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1f, 1f));
    }
}
