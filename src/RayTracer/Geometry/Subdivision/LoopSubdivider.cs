using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Loop subdivision surface for triangle meshes, with sharp and semi-sharp
/// creases and boundaries (Hoppe et al. 1994, DeRose et al. 1998; vertex
/// rules from <see cref="SubdivisionTopology"/>).
///
/// <para>
/// Reference: Charles Loop, <i>Smooth subdivision surfaces based on
/// triangles</i>, MSc thesis, University of Utah, 1987. Boundary masks
/// follow Hoppe et al. 1994 <i>Piecewise smooth surface reconstruction</i>.
/// </para>
///
/// <para>Per iteration the algorithm rewrites the mesh in three steps:</para>
/// <list type="number">
///   <item><description>Build an undirected-edge adjacency to identify
///     boundary edges and opposite vertices in O(F) time.</description></item>
///   <item><description><b>Odd vertices</b> — one new vertex per edge:
///     <list type="bullet">
///       <item><description>Smooth: <c>3/8·(a+b) + 1/8·(c+d)</c>
///         with c,d the two opposite vertices.</description></item>
///       <item><description>Sharp (boundary or crease): <c>1/2·(a+b)</c>;
///         a blend for a sharpness below 1.</description></item>
///     </list></description></item>
///   <item><description><b>Even vertices</b> — relax existing vertices:
///     <list type="bullet">
///       <item><description>Smooth: <c>(1 − n·β)·v + β·Σ vᵢ</c> with
///         <c>β = (1/n)(5/8 − (3/8 + 1/4·cos(2π/n))²)</c> (Loop's original
///         formula).</description></item>
///       <item><description>Crease: <c>6/8·v + 1/8·(a + b)</c> with a, b the
///         neighbours along the two sharp edges.</description></item>
///       <item><description>Corner: retained.</description></item>
///     </list></description></item>
/// </list>
///
/// <para>
/// UVs and normals are carried with the same connectivity as positions
/// (vertex-varying channel in OpenSubdiv terminology). Edge-midpoint
/// attribute values are linear averages of the endpoints — the simple
/// scheme that matches OpenSubdiv's default for non-seamed channels.
/// </para>
/// </summary>
internal static class LoopSubdivider
{
    public static void Subdivide(PolyMesh mesh, int iterations,
        BoundaryInterpolation boundary = BoundaryInterpolation.EdgeAndCorner)
    {
        for (int i = 0; i < iterations; i++)
            SubdivideOnce(mesh, boundary);
    }

    /// <summary>Loop's β for an interior vertex of valence n.</summary>
    internal static float Beta(int n)
    {
        float c = 3f / 8f + 0.25f * MathF.Cos(2f * MathF.PI / n);
        return (1f / n) * (5f / 8f - c * c);
    }

    private static void SubdivideOnce(PolyMesh mesh, BoundaryInterpolation boundary)
    {
        int faceCount = mesh.FacePositions.Count;
        int posCount  = mesh.Positions.Count;
        foreach (var f in mesh.FacePositions)
            if (f.Length != 3)
                throw new InvalidOperationException("LoopSubdivider expects only triangles.");
        var topo = new SubdivisionTopology(mesh, boundary);

        // ── Odd vertices: one per edge ────────────────────────────────────
        var edgeMidIdx = new Dictionary<long, int>(topo.Edges.Count);
        var newPositions = new List<Vector3>(posCount + topo.Edges.Count);
        newPositions.AddRange(mesh.Positions);
        foreach (var (key, e) in topo.Edges)
        {
            Vector3 a = mesh.Positions[e.A];
            Vector3 b = mesh.Positions[e.B];
            var edge = e;
            Vector3 mid = SubdivisionTopology.EdgePoint(edge, 0.5f * (a + b), () =>
            {
                int c = Opposite(mesh.FacePositions[edge.Face0], edge.A, edge.B);
                int d = Opposite(mesh.FacePositions[edge.Face1], edge.A, edge.B);
                return (3f / 8f) * (a + b) + (1f / 8f) * (mesh.Positions[c] + mesh.Positions[d]);
            });
            edgeMidIdx[key] = newPositions.Count;
            newPositions.Add(mid);
        }

        // ── Even vertices ────────────────────────────────────────────────
        var relaxed = new Vector3[posCount];
        for (int v = 0; v < posCount; v++)
        {
            int vv = v;
            relaxed[v] = topo.VertexPoint(mesh, v, () =>
            {
                var incident = topo.VertexEdges[vv];
                int n = incident.Count;
                float beta = Beta(n);
                Vector3 sum = Vector3.Zero;
                foreach (long key in incident)
                {
                    var e = topo.Edges[key];
                    sum += mesh.Positions[e.A == vv ? e.B : e.A];
                }
                return (1f - n * beta) * mesh.Positions[vv] + beta * sum;
            });
        }
        for (int v = 0; v < posCount; v++) newPositions[v] = relaxed[v];

        // ── Child triangles ───────────────────────────────────────────────
        var newFaces = new List<int[]>(faceCount * 4);
        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            int v0 = f[0], v1 = f[1], v2 = f[2];
            int m01 = edgeMidIdx[EdgeKey(v0, v1)];
            int m12 = edgeMidIdx[EdgeKey(v1, v2)];
            int m20 = edgeMidIdx[EdgeKey(v2, v0)];

            newFaces.Add(new[] { v0,  m01, m20 });
            newFaces.Add(new[] { v1,  m12, m01 });
            newFaces.Add(new[] { v2,  m20, m12 });
            newFaces.Add(new[] { m01, m12, m20 });
        }
        List<int>? newMaterials = null;
        if (mesh.FaceMaterials != null)
        {
            newMaterials = new List<int>(faceCount * 4);
            foreach (int slot in mesh.FaceMaterials)
                for (int c = 0; c < 4; c++) newMaterials.Add(slot);
        }
        var childCreases = topo.ChildCreases(key => edgeMidIdx[key]);

        // ── Vertex-varying UV channel ─────────────────────────────────────
        //
        // UV connectivity may differ from position connectivity (a single
        // position vertex can carry multiple UV indices when an artist
        // splits a seam). We compute a per-face UV edge-midpoint table:
        // edge (uvA, uvB) within face fi → its new UV index. Two faces that
        // share a position edge but different UVs (seam) get *different*
        // UV midpoints — the seam is preserved.
        // ─────────────────────────────────────────────────────────────────
        List<Vector2>? newUVs       = null;
        List<int[]>?    newFaceUVs   = null;
        if (mesh.HasUVs)
        {
            newUVs = new List<Vector2>(mesh.UVs!);
            // Map (faceIndex, cornerIndex) -> new uv midpoint index, keyed
            // by sorted-uv-pair-per-face. Faces meeting at a UV seam will
            // have different pairs and therefore different new indices.
            var uvMid = new Dictionary<long, int>(faceCount * 3);
            newFaceUVs = new List<int[]>(faceCount * 4);

            for (int fi = 0; fi < faceCount; fi++)
            {
                int[] fu = mesh.FaceUVs![fi];
                int uv0 = fu[0], uv1 = fu[1], uv2 = fu[2];

                int m01 = GetOrCreateMid(newUVs, uvMid, mesh.UVs!, uv0, uv1);
                int m12 = GetOrCreateMid(newUVs, uvMid, mesh.UVs!, uv1, uv2);
                int m20 = GetOrCreateMid(newUVs, uvMid, mesh.UVs!, uv2, uv0);

                newFaceUVs.Add(new[] { uv0, m01, m20 });
                newFaceUVs.Add(new[] { uv1, m12, m01 });
                newFaceUVs.Add(new[] { uv2, m20, m12 });
                newFaceUVs.Add(new[] { m01, m12, m20 });
            }
        }

        // ── Vertex-varying normal channel (3D analogue) ──────────────────
        //
        // We carry normals through the same way as UVs. After all
        // iterations the final triangles either keep these interpolated
        // normals (legacy meshes that supplied them) or have new ones
        // computed by the loader from the limit surface — the renderer
        // calls SmoothTriangle's constructor with either set.
        // ─────────────────────────────────────────────────────────────────
        List<Vector3>? newNormals    = null;
        List<int[]>?    newFaceNormals = null;
        if (mesh.HasNormals)
        {
            newNormals = new List<Vector3>(mesh.Normals!);
            var nMid = new Dictionary<long, int>(faceCount * 3);
            newFaceNormals = new List<int[]>(faceCount * 4);

            for (int fi = 0; fi < faceCount; fi++)
            {
                int[] fn = mesh.FaceNormals![fi];
                int n0 = fn[0], n1 = fn[1], n2 = fn[2];

                int m01 = GetOrCreateMid3D(newNormals, nMid, mesh.Normals!, n0, n1);
                int m12 = GetOrCreateMid3D(newNormals, nMid, mesh.Normals!, n1, n2);
                int m20 = GetOrCreateMid3D(newNormals, nMid, mesh.Normals!, n2, n0);

                newFaceNormals.Add(new[] { n0,  m01, m20 });
                newFaceNormals.Add(new[] { n1,  m12, m01 });
                newFaceNormals.Add(new[] { n2,  m20, m12 });
                newFaceNormals.Add(new[] { m01, m12, m20 });
            }
        }

        // ── Commit ────────────────────────────────────────────────────────
        mesh.Creases = childCreases;
        mesh.LimitNormals = null;
        mesh.FaceMaterials = newMaterials;
        mesh.Positions.Clear();
        mesh.Positions.AddRange(newPositions);
        mesh.FacePositions.Clear();
        mesh.FacePositions.AddRange(newFaces);

        if (newUVs != null)
        {
            mesh.UVs!.Clear();      mesh.UVs.AddRange(newUVs);
            mesh.FaceUVs!.Clear();  mesh.FaceUVs.AddRange(newFaceUVs!);
        }
        if (newNormals != null)
        {
            mesh.Normals!.Clear();     mesh.Normals.AddRange(newNormals);
            mesh.FaceNormals!.Clear(); mesh.FaceNormals.AddRange(newFaceNormals!);
        }
    }

    private static int GetOrCreateMid(
        List<Vector2> values, Dictionary<long, int> midCache,
        List<Vector2> original, int a, int b)
    {
        long key = EdgeKey(a, b);
        if (midCache.TryGetValue(key, out int existing)) return existing;
        int idx = values.Count;
        values.Add(0.5f * (original[a] + original[b]));
        midCache[key] = idx;
        return idx;
    }

    private static int GetOrCreateMid3D(
        List<Vector3> values, Dictionary<long, int> midCache,
        List<Vector3> original, int a, int b)
    {
        long key = EdgeKey(a, b);
        if (midCache.TryGetValue(key, out int existing)) return existing;
        int idx = values.Count;
        values.Add(0.5f * (original[a] + original[b]));
        midCache[key] = idx;
        return idx;
    }

    private static long EdgeKey(int a, int b) => SubdivisionTopology.EdgeKey(a, b);

    private static int Opposite(int[] f, int a, int b)
        => f[0] != a && f[0] != b ? f[0] : f[1] != a && f[1] != b ? f[1] : f[2];
}
