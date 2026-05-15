using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Loop subdivision surface — the production-grade algorithm Pixar's
/// OpenSubdiv, Cycles and Arnold use for triangle meshes.
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
///       <item><description>Interior: <c>3/8·(a+b) + 1/8·(c+d)</c>
///         with c,d the two opposite vertices.</description></item>
///       <item><description>Boundary: <c>1/2·(a+b)</c>.</description></item>
///     </list></description></item>
///   <item><description><b>Even vertices</b> — relax existing vertices:
///     <list type="bullet">
///       <item><description>Interior: <c>(1 − n·β)·v + β·Σ vᵢ</c> with
///         <c>β = (1/n)(5/8 − (3/8 + 1/4·cos(2π/n))²)</c> (Loop's original
///         formula).</description></item>
///       <item><description>Boundary: <c>6/8·v + 1/8·v_prev + 1/8·v_next</c>
///         using the two boundary neighbors only.</description></item>
///       <item><description>Corner (1 or ≥3 boundary neighbors): retained.</description></item>
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
    public static void Subdivide(PolyMesh mesh, int iterations)
    {
        for (int i = 0; i < iterations; i++)
            SubdivideOnce(mesh);
    }

    private static void SubdivideOnce(PolyMesh mesh)
    {
        int faceCount = mesh.FacePositions.Count;
        int posCount  = mesh.Positions.Count;
        int uvCount   = mesh.HasUVs     ? mesh.UVs!.Count     : 0;
        int nrCount   = mesh.HasNormals ? mesh.Normals!.Count : 0;

        // ── Undirected-edge adjacency for positions ───────────────────────
        var edges = new Dictionary<long, EdgeRecord>(faceCount * 3);

        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            if (f.Length != 3)
                throw new InvalidOperationException("LoopSubdivider expects only triangles.");

            for (int k = 0; k < 3; k++)
            {
                int a = f[k];
                int b = f[(k + 1) % 3];
                long key = EdgeKey(a, b);
                if (!edges.TryGetValue(key, out var rec))
                {
                    rec = new EdgeRecord
                    {
                        A = Math.Min(a, b),
                        B = Math.Max(a, b),
                        Face0 = -1, Face1 = -1,
                        Corner0 = -1, Corner1 = -1,
                    };
                }
                if (rec.Face0 < 0) { rec.Face0 = fi; rec.Corner0 = k; }
                else               { rec.Face1 = fi; rec.Corner1 = k; }
                edges[key] = rec;
            }
        }

        // ── Vertex adjacency (interior + boundary lists) ─────────────────
        var neighbors    = new List<int>[posCount];
        var isBoundary   = new bool[posCount];
        var boundaryNbrs = new List<int>[posCount];

        for (int i = 0; i < posCount; i++)
        {
            neighbors[i]    = new List<int>(6);
            boundaryNbrs[i] = new List<int>(2);
        }

        foreach (var rec in edges.Values)
        {
            if (!neighbors[rec.A].Contains(rec.B)) neighbors[rec.A].Add(rec.B);
            if (!neighbors[rec.B].Contains(rec.A)) neighbors[rec.B].Add(rec.A);

            if (rec.Face1 < 0)
            {
                isBoundary[rec.A] = true;
                isBoundary[rec.B] = true;
                if (!boundaryNbrs[rec.A].Contains(rec.B)) boundaryNbrs[rec.A].Add(rec.B);
                if (!boundaryNbrs[rec.B].Contains(rec.A)) boundaryNbrs[rec.B].Add(rec.A);
            }
        }

        // ── Allocate edge-midpoint slot indices (one per undirected edge) ─
        var edgeMidIdx = new Dictionary<long, int>(edges.Count);
        var newPositions = new List<Vector3>(posCount + edges.Count);
        newPositions.AddRange(mesh.Positions);

        foreach (var kv in edges)
        {
            var rec = kv.Value;
            Vector3 a = mesh.Positions[rec.A];
            Vector3 b = mesh.Positions[rec.B];
            Vector3 mid;
            if (rec.Face1 < 0)
            {
                mid = 0.5f * (a + b);
            }
            else
            {
                int c = OppositeVertex(mesh.FacePositions[rec.Face0], rec.Corner0);
                int d = OppositeVertex(mesh.FacePositions[rec.Face1], rec.Corner1);
                mid = (3f / 8f) * (a + b) +
                      (1f / 8f) * (mesh.Positions[c] + mesh.Positions[d]);
            }
            edgeMidIdx[kv.Key] = newPositions.Count;
            newPositions.Add(mid);
        }

        // ── Relax existing vertices ──────────────────────────────────────
        var relaxed = new Vector3[posCount];
        for (int v = 0; v < posCount; v++)
        {
            if (isBoundary[v])
            {
                if (boundaryNbrs[v].Count == 2)
                {
                    Vector3 a = mesh.Positions[boundaryNbrs[v][0]];
                    Vector3 b = mesh.Positions[boundaryNbrs[v][1]];
                    relaxed[v] = (6f / 8f) * mesh.Positions[v] + (1f / 8f) * (a + b);
                }
                else
                {
                    relaxed[v] = mesh.Positions[v]; // corner / non-manifold
                }
                continue;
            }

            int n = neighbors[v].Count;
            if (n == 0) { relaxed[v] = mesh.Positions[v]; continue; }

            float c = 3f / 8f + 0.25f * MathF.Cos(2f * MathF.PI / n);
            float beta = (1f / n) * (5f / 8f - c * c);

            Vector3 sum = Vector3.Zero;
            for (int j = 0; j < n; j++) sum += mesh.Positions[neighbors[v][j]];

            relaxed[v] = (1f - n * beta) * mesh.Positions[v] + beta * sum;
        }
        for (int v = 0; v < posCount; v++) newPositions[v] = relaxed[v];

        // ── Build child triangles ─────────────────────────────────────────
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

    private static long EdgeKey(int a, int b)
    {
        int lo = Math.Min(a, b);
        int hi = Math.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    private static int OppositeVertex(int[] f, int k) => f[(k + 2) % 3];

    private struct EdgeRecord
    {
        public int A, B;
        public int Face0, Face1;
        public int Corner0, Corner1;
    }
}
