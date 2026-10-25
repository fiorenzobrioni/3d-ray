using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Catmull-Clark subdivision surface (E. Catmull and J. Clark, 1978) with
/// sharp and semi-sharp creases and boundaries (DeRose, Kass and Truong 1998,
/// "Subdivision Surfaces in Character Animation"; the rules OpenSubdiv
/// implements). Vertex rules come from <see cref="SubdivisionTopology"/>.
///
/// <para>Per iteration:</para>
/// <list type="number">
///   <item><description><b>Face points</b>: the average of the face's vertices.</description></item>
///   <item><description><b>Edge points</b>: smooth <c>(P1 + P2 + F1 + F2)/4</c>;
///     on a sharp edge (boundary or crease) the midpoint; a blend for a
///     sharpness below 1.</description></item>
///   <item><description><b>Vertex points</b>: smooth (valence n)
///     <c>(Q + 2R + (n−3)S)/n</c> with Q the mean of the face points, R the
///     mean of the edge midpoints; crease <c>(6S + A + B)/8</c> with A, B the
///     neighbours along the two sharp edges; corner fixed.</description></item>
///   <item><description>Each face of arity m becomes m quads; crease edges
///     pass sharpness − 1 to their two halves.</description></item>
/// </list>
/// </summary>
internal static class CatmullClarkSubdivider
{
    public static void Subdivide(PolyMesh mesh, int iterations,
        BoundaryInterpolation boundary = BoundaryInterpolation.EdgeAndCorner)
    {
        for (int i = 0; i < iterations; i++)
            SubdivideOnce(mesh, boundary);
    }

    private static void SubdivideOnce(PolyMesh mesh, BoundaryInterpolation boundary)
    {
        int faceCount = mesh.FacePositions.Count;
        int posCount  = mesh.Positions.Count;
        var topo = new SubdivisionTopology(mesh, boundary);

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

        // ── 2. Edge points ────────────────────────────────────────────────
        var newPositions = new List<Vector3>(posCount + faceCount + topo.Edges.Count);
        newPositions.AddRange(mesh.Positions);

        int facePtBase = newPositions.Count;
        for (int fi = 0; fi < faceCount; fi++) newPositions.Add(facePoints[fi]);

        var edgePtIdx = new Dictionary<long, int>(topo.Edges.Count);
        var edgeMidPoint = new Dictionary<long, Vector3>(topo.Edges.Count);
        foreach (var (key, e) in topo.Edges)
        {
            Vector3 a = mesh.Positions[e.A];
            Vector3 b = mesh.Positions[e.B];
            Vector3 mid = 0.5f * (a + b);
            edgeMidPoint[key] = mid;
            var edge = e;
            Vector3 edgePt = SubdivisionTopology.EdgePoint(edge, mid,
                () => 0.25f * (a + b + facePoints[edge.Face0] + facePoints[edge.Face1]));
            edgePtIdx[key] = newPositions.Count;
            newPositions.Add(edgePt);
        }

        // ── 3. Vertex points ──────────────────────────────────────────────
        var relaxed = new Vector3[posCount];
        for (int v = 0; v < posCount; v++)
        {
            int vv = v;
            relaxed[v] = topo.VertexPoint(mesh, v, () =>
            {
                var faces = topo.VertexFaces[vv];
                int n = faces.Count;
                Vector3 q = Vector3.Zero;
                foreach (int fi in faces) q += facePoints[fi];
                q /= n;
                var incident = topo.VertexEdges[vv];
                Vector3 r = Vector3.Zero;
                foreach (long key in incident) r += edgeMidPoint[key];
                r /= incident.Count;
                return (q + 2f * r + (n - 3f) * mesh.Positions[vv]) / n;
            });
        }
        for (int v = 0; v < posCount; v++) newPositions[v] = relaxed[v];

        // ── 4. New quads: corner k → (vertex_k, edge_pt(k,k+1), face_pt, edge_pt(k-1,k)),
        //       the winding of the old face.
        var newFaces = new List<int[]>(faceCount * 4);
        var newMaterials = mesh.FaceMaterials != null ? new List<int>(faceCount * 4) : null;
        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            int fp = facePtBase + fi;
            int m = f.Length;
            for (int k = 0; k < m; k++)
            {
                newMaterials?.Add(mesh.FaceMaterials![fi]);
                int vPrev = f[(k - 1 + m) % m];
                int vCurr = f[k];
                int vNext = f[(k + 1) % m];
                int ePrev = edgePtIdx[SubdivisionTopology.EdgeKey(vPrev, vCurr)];
                int eNext = edgePtIdx[SubdivisionTopology.EdgeKey(vCurr, vNext)];
                newFaces.Add(new[] { vCurr, eNext, fp, ePrev });
            }
        }

        // ── 5. Face-varying UVs and normals, interpolated linearly: the
        //       old values stay at the old vertices, the midpoints and the
        //       face centroids fill in (a UV seam keeps its two sides).
        List<Vector2>? newUVs = null;
        List<int[]>? newFaceUVs = null;
        if (mesh.HasUVs)
        {
            newUVs = new List<Vector2>(mesh.UVs!);
            int uvFacePtBase = newUVs.Count;
            for (int fi = 0; fi < faceCount; fi++) newUVs.Add(faceUVCentroids![fi]);
            var uvEdgeMid = new Dictionary<long, int>(topo.Edges.Count);
            newFaceUVs = new List<int[]>(faceCount * 4);
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
            var nEdgeMid = new Dictionary<long, int>(topo.Edges.Count);
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
        mesh.Creases = topo.ChildCreases(key => edgePtIdx[key]);
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
        List<Vector2> values, Dictionary<long, int> cache,
        List<Vector2> original, int a, int b)
    {
        long key = SubdivisionTopology.EdgeKey(a, b);
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
        long key = SubdivisionTopology.EdgeKey(a, b);
        if (cache.TryGetValue(key, out int existing)) return existing;
        int idx = values.Count;
        values.Add(0.5f * (original[a] + original[b]));
        cache[key] = idx;
        return idx;
    }
}
