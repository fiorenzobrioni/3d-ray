using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Catmull-Clark subdivision surface — the algorithm used for quad meshes
/// by Pixar's OpenSubdiv (the basis of the original 1978 paper), Cycles,
/// Arnold and RenderMan.
///
/// <para>Reference: E. Catmull and J. Clark, <i>Recursively generated B-spline
/// surfaces on arbitrary topological meshes</i>, Computer-Aided Design 10(6),
/// 1978. Boundary handling per Hoppe et al. 1994 / DeRose et al. 1998
/// <i>Subdivision Surfaces in Character Animation</i>.</para>
///
/// <para>Per iteration:</para>
/// <list type="number">
///   <item><description><b>Face points</b>: average of every vertex of the
///     face (one new vertex per face).</description></item>
///   <item><description><b>Edge points</b>:
///     <list type="bullet">
///       <item><description>Interior: <c>(P1 + P2 + F1 + F2)/4</c> with P
///         the endpoints and F the face points of the two adjacent faces.</description></item>
///       <item><description>Boundary: <c>(P1 + P2)/2</c>.</description></item>
///     </list></description></item>
///   <item><description><b>Vertex points</b>:
///     <list type="bullet">
///       <item><description>Interior valence-n vertex:
///         <c>(Q + 2R + (n−3)S)/n</c> with
///         Q = avg of face points, R = avg of *edge midpoints* of incident
///         edges, S = original.</description></item>
///       <item><description>Boundary vertex with 2 boundary edges:
///         <c>(1/8)e_a + (6/8)S + (1/8)e_b</c> with e_a,e_b the boundary
///         neighbor positions.</description></item>
///       <item><description>Corner (1 face / single boundary): retained.</description></item>
///     </list></description></item>
///   <item><description>Each old face of arity m emits m new quads
///     <c>(face_pt, edge_pt_+, vertex_pt, edge_pt_−)</c>, one per corner.
///     After the first iteration the mesh is all-quads even if the input
///     contained triangles or n-gons.</description></item>
/// </list>
/// </summary>
internal static class CatmullClarkSubdivider
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

        // ── 1. Face points ────────────────────────────────────────────────
        var facePoints = new Vector3[faceCount];
        Vector2[]? faceUVCentroids   = mesh.HasUVs     ? new Vector2[faceCount] : null;
        Vector3[]? faceNormalCentr   = mesh.HasNormals ? new Vector3[faceCount] : null;
        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            Vector3 sum = Vector3.Zero;
            for (int k = 0; k < f.Length; k++) sum += mesh.Positions[f[k]];
            facePoints[fi] = sum / f.Length;

            if (faceUVCentroids != null)
            {
                int[] fu = mesh.FaceUVs![fi];
                Vector2 uvSum = Vector2.Zero;
                for (int k = 0; k < fu.Length; k++) uvSum += mesh.UVs![fu[k]];
                faceUVCentroids[fi] = uvSum / fu.Length;
            }
            if (faceNormalCentr != null)
            {
                int[] fn = mesh.FaceNormals![fi];
                Vector3 nSum = Vector3.Zero;
                for (int k = 0; k < fn.Length; k++) nSum += mesh.Normals![fn[k]];
                faceNormalCentr[fi] = nSum / fn.Length;
            }
        }

        // ── 2. Undirected-edge adjacency ──────────────────────────────────
        var edges = new Dictionary<long, CcEdge>(faceCount * 4);

        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            for (int k = 0; k < f.Length; k++)
            {
                int a = f[k];
                int b = f[(k + 1) % f.Length];
                long key = EdgeKey(a, b);
                if (!edges.TryGetValue(key, out var rec))
                {
                    rec = new CcEdge
                    {
                        A = Math.Min(a, b), B = Math.Max(a, b),
                        Face0 = -1, Face1 = -1,
                    };
                }
                if (rec.Face0 < 0) rec.Face0 = fi;
                else               rec.Face1 = fi;
                edges[key] = rec;
            }
        }

        // ── 3. Edge points ────────────────────────────────────────────────
        //
        // Interior: (P1 + P2 + F1 + F2)/4. Boundary: (P1 + P2)/2.
        // We need *both* expressions later when computing the vertex point's
        // R term (which uses *edge midpoints*, not edge points), so we cache
        // the midpoint separately.
        // ─────────────────────────────────────────────────────────────────
        var newPositions = new List<Vector3>(posCount + faceCount + edges.Count);
        newPositions.AddRange(mesh.Positions);

        int facePtBase = newPositions.Count;
        for (int fi = 0; fi < faceCount; fi++) newPositions.Add(facePoints[fi]);

        int edgePtBase = newPositions.Count;
        var edgePtIdx = new Dictionary<long, int>(edges.Count);
        var edgeMidPoint = new Dictionary<long, Vector3>(edges.Count);

        foreach (var kv in edges)
        {
            var rec = kv.Value;
            Vector3 a = mesh.Positions[rec.A];
            Vector3 b = mesh.Positions[rec.B];
            Vector3 mid = 0.5f * (a + b);
            edgeMidPoint[kv.Key] = mid;

            Vector3 edgePt = rec.Face1 < 0
                ? mid
                : 0.25f * (a + b + facePoints[rec.Face0] + facePoints[rec.Face1]);

            edgePtIdx[kv.Key] = newPositions.Count;
            newPositions.Add(edgePt);
        }

        // ── 4. Vertex points ──────────────────────────────────────────────
        //
        // For each existing vertex we need:
        //   • the list of incident faces  → average face point Q
        //   • the list of incident edges  → average edge midpoint R
        //   • whether it's a boundary vertex, and which two boundary
        //     neighbors form its boundary chain
        // ─────────────────────────────────────────────────────────────────
        var vertexFaces  = new List<int>[posCount];
        var vertexEdges  = new List<long>[posCount];
        var isBoundary   = new bool[posCount];
        var boundaryNbrs = new List<int>[posCount];

        for (int i = 0; i < posCount; i++)
        {
            vertexFaces[i]  = new List<int>(4);
            vertexEdges[i]  = new List<long>(4);
            boundaryNbrs[i] = new List<int>(2);
        }

        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            for (int k = 0; k < f.Length; k++)
                vertexFaces[f[k]].Add(fi);
        }

        foreach (var kv in edges)
        {
            var rec = kv.Value;
            vertexEdges[rec.A].Add(kv.Key);
            vertexEdges[rec.B].Add(kv.Key);

            if (rec.Face1 < 0)
            {
                isBoundary[rec.A] = true;
                isBoundary[rec.B] = true;
                if (!boundaryNbrs[rec.A].Contains(rec.B)) boundaryNbrs[rec.A].Add(rec.B);
                if (!boundaryNbrs[rec.B].Contains(rec.A)) boundaryNbrs[rec.B].Add(rec.A);
            }
        }

        var relaxed = new Vector3[posCount];
        for (int v = 0; v < posCount; v++)
        {
            if (isBoundary[v])
            {
                // Hoppe boundary mask: cubic B-spline curve in 1D.
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

            int n = vertexFaces[v].Count;
            if (n == 0) { relaxed[v] = mesh.Positions[v]; continue; }

            Vector3 Q = Vector3.Zero;
            for (int j = 0; j < n; j++) Q += facePoints[vertexFaces[v][j]];
            Q /= n;

            int m = vertexEdges[v].Count;
            Vector3 R = Vector3.Zero;
            for (int j = 0; j < m; j++) R += edgeMidPoint[vertexEdges[v][j]];
            R /= m;

            Vector3 S = mesh.Positions[v];
            relaxed[v] = (Q + 2f * R + (n - 3f) * S) / n;
        }
        for (int v = 0; v < posCount; v++) newPositions[v] = relaxed[v];

        // ── 5. Build new quads ───────────────────────────────────────────
        //
        // For each old face of arity m: emit m quads, one per corner
        //   (face_pt_fi, edge_pt(k,k+1), vertex_pt(k+1), edge_pt(k+1,k+2))
        // Wait — the canonical order is:
        //   corner k → quad ( vertex_k, edge_pt(k-1,k), face_pt, edge_pt(k,k+1) )
        // which preserves outward orientation.
        // ─────────────────────────────────────────────────────────────────
        var newFaces = new List<int[]>(faceCount * 4);
        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            int fp = facePtBase + fi;
            int m = f.Length;
            for (int k = 0; k < m; k++)
            {
                int vPrev = f[(k - 1 + m) % m];
                int vCurr = f[k];
                int vNext = f[(k + 1) % m];

                int ePrev = edgePtIdx[EdgeKey(vPrev, vCurr)];
                int eNext = edgePtIdx[EdgeKey(vCurr, vNext)];

                newFaces.Add(new[] { vCurr, eNext, fp, ePrev });
            }
        }

        // ── 6. Vertex-varying UVs and normals ────────────────────────────
        List<Vector2>? newUVs = null;
        List<int[]>? newFaceUVs = null;
        if (mesh.HasUVs)
        {
            newUVs = new List<Vector2>(mesh.UVs!);
            // Per-face centroid index
            int uvFacePtBase = newUVs.Count;
            for (int fi = 0; fi < faceCount; fi++) newUVs.Add(faceUVCentroids![fi]);

            // Per-face-corner edge midpoint UVs: a UV "seam" between two
            // faces yields different midpoint indices (we key on the UV
            // pair, not the position pair).
            var uvEdgeMid = new Dictionary<long, int>(edges.Count);
            newFaceUVs = new List<int[]>(faceCount * 4);

            // First pass: allocate UV vertex-point slots (same index as
            // position vertex-point — they share connectivity), recomputed
            // with the boundary/interior centroid mask.
            // We use the original UV indices in-place; the limit
            // approximation simply keeps endpoint UVs as their relaxed
            // positions are *positional*. The "vertex point" UV is the
            // original UV (we don't smooth UVs into space).
            //
            // Practically: we do NOT relax UVs here because doing so on a
            // boundary-sensitive 2D parametrization causes visible texture
            // creep at seams; OpenSubdiv's default is to keep UV vertex
            // points unchanged. Linear UV midpoint on edges already gives
            // the correct interpolation for the rendered surface.

            for (int fi = 0; fi < faceCount; fi++)
            {
                int[] fu = mesh.FaceUVs![fi];
                int m = fu.Length;
                int fpUV = uvFacePtBase + fi;
                for (int k = 0; k < m; k++)
                {
                    int uvPrev = fu[(k - 1 + m) % m];
                    int uvCurr = fu[k];
                    int uvNext = fu[(k + 1) % m];

                    int ePrev = GetOrCreateMid(newUVs, uvEdgeMid, mesh.UVs!, uvPrev, uvCurr);
                    int eNext = GetOrCreateMid(newUVs, uvEdgeMid, mesh.UVs!, uvCurr, uvNext);

                    newFaceUVs.Add(new[] { uvCurr, eNext, fpUV, ePrev });
                }
            }
        }

        List<Vector3>? newNormals = null;
        List<int[]>?   newFaceNormals = null;
        if (mesh.HasNormals)
        {
            newNormals = new List<Vector3>(mesh.Normals!);
            int nFacePtBase = newNormals.Count;
            for (int fi = 0; fi < faceCount; fi++) newNormals.Add(faceNormalCentr![fi]);

            var nEdgeMid = new Dictionary<long, int>(edges.Count);
            newFaceNormals = new List<int[]>(faceCount * 4);

            for (int fi = 0; fi < faceCount; fi++)
            {
                int[] fn = mesh.FaceNormals![fi];
                int m = fn.Length;
                int fpN = nFacePtBase + fi;
                for (int k = 0; k < m; k++)
                {
                    int nPrev = fn[(k - 1 + m) % m];
                    int nCurr = fn[k];
                    int nNext = fn[(k + 1) % m];

                    int ePrev = GetOrCreateMid3D(newNormals, nEdgeMid, mesh.Normals!, nPrev, nCurr);
                    int eNext = GetOrCreateMid3D(newNormals, nEdgeMid, mesh.Normals!, nCurr, nNext);

                    newFaceNormals.Add(new[] { nCurr, eNext, fpN, ePrev });
                }
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
        List<Vector2> values, Dictionary<long, int> cache,
        List<Vector2> original, int a, int b)
    {
        long key = EdgeKey(a, b);
        if (cache.TryGetValue(key, out int existing)) return existing;
        int idx = values.Count;
        values.Add(0.5f * (original[a] + original[b]));
        cache[key] = idx;
        return idx;
    }

    private static int GetOrCreateMid3D(
        List<Vector3> values, Dictionary<long, int> cache,
        List<Vector3> original, int a, int b)
    {
        long key = EdgeKey(a, b);
        if (cache.TryGetValue(key, out int existing)) return existing;
        int idx = values.Count;
        values.Add(0.5f * (original[a] + original[b]));
        cache[key] = idx;
        return idx;
    }

    private static long EdgeKey(int a, int b)
    {
        int lo = Math.Min(a, b);
        int hi = Math.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    private struct CcEdge
    {
        public int A, B;
        public int Face0, Face1;
    }
}
