using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>How a mesh's open boundary is subdivided.</summary>
public enum BoundaryInterpolation
{
    /// <summary>
    /// Boundary edges are sharp creases and a boundary vertex with a single
    /// face (the corner of a sheet) stays where it is (the default).
    /// </summary>
    EdgeAndCorner,
    /// <summary>Boundary edges are sharp creases; corners are rounded along the boundary curve.</summary>
    EdgeOnly,
}

/// <summary>The rule a vertex follows, from the sharp edges that meet at it (DeRose et al. 1998).</summary>
internal enum VertexRule
{
    /// <summary>No sharp edge, or one (a dart): the smooth mask.</summary>
    Smooth,
    /// <summary>Two sharp edges: the cubic B-spline curve through them.</summary>
    Crease,
    /// <summary>Three or more sharp edges, or a corner of the boundary: fixed.</summary>
    Corner,
}

/// <summary>
/// Adjacency of a <see cref="PolyMesh"/> for one level of subdivision: its
/// undirected edges with the faces on each side and their sharpness, and each
/// vertex's faces, edges and rule (PIANO-REVIEW.md Phase 2). Boundary edges,
/// and edges shared by more than two faces, are infinitely sharp. A vertex's
/// sharpness is the mean of its sharp edges'; between 0 and 1 its position
/// blends the smooth and the sharp rule (semi-sharp creases).
/// </summary>
internal sealed class SubdivisionTopology
{
    public struct Edge
    {
        public int A, B;
        public int Face0, Face1;
        public int FaceCount;
        public float Sharpness;
        public bool IsBoundary => FaceCount < 2;
    }

    public readonly Dictionary<long, Edge> Edges;
    public readonly List<int>[] VertexFaces;
    public readonly List<long>[] VertexEdges;
    public readonly VertexRule[] Rules;
    public readonly float[] VertexSharpness;
    /// <summary>The two neighbours along the sharp edges of a crease vertex.</summary>
    public readonly int[] CreaseA, CreaseB;

    public SubdivisionTopology(PolyMesh mesh, BoundaryInterpolation boundary)
    {
        int faceCount = mesh.FacePositions.Count;
        int posCount = mesh.Positions.Count;
        Edges = new Dictionary<long, Edge>(faceCount * 4);
        for (int fi = 0; fi < faceCount; fi++)
        {
            int[] f = mesh.FacePositions[fi];
            for (int k = 0; k < f.Length; k++)
            {
                int a = f[k], b = f[(k + 1) % f.Length];
                long key = EdgeKey(a, b);
                if (!Edges.TryGetValue(key, out var e))
                    e = new Edge { A = Math.Min(a, b), B = Math.Max(a, b), Face0 = -1, Face1 = -1 };
                if (e.FaceCount == 0) e.Face0 = fi; else if (e.FaceCount == 1) e.Face1 = fi;
                e.FaceCount++;
                Edges[key] = e;
            }
        }

        VertexFaces = new List<int>[posCount];
        VertexEdges = new List<long>[posCount];
        for (int i = 0; i < posCount; i++) { VertexFaces[i] = new List<int>(4); VertexEdges[i] = new List<long>(4); }
        for (int fi = 0; fi < faceCount; fi++)
            foreach (int v in mesh.FacePositions[fi]) VertexFaces[v].Add(fi);

        var keys = new List<long>(Edges.Keys);
        foreach (long key in keys)
        {
            var e = Edges[key];
            e.Sharpness = e.FaceCount != 2 ? float.PositiveInfinity
                        : mesh.Creases != null && mesh.Creases.TryGetValue(key, out float s) ? MathF.Max(0f, s) : 0f;
            Edges[key] = e;
            VertexEdges[e.A].Add(key);
            VertexEdges[e.B].Add(key);
        }

        Rules = new VertexRule[posCount];
        VertexSharpness = new float[posCount];
        CreaseA = new int[posCount];
        CreaseB = new int[posCount];
        for (int v = 0; v < posCount; v++)
        {
            int sharpCount = 0;
            float sum = 0f;
            int na = -1, nb = -1;
            bool onBoundary = false;
            foreach (long key in VertexEdges[v])
            {
                var e = Edges[key];
                if (e.Sharpness <= 0f) continue;
                sharpCount++;
                sum += e.Sharpness;
                onBoundary |= e.IsBoundary;
                int other = e.A == v ? e.B : e.A;
                if (na < 0) na = other; else if (nb < 0) nb = other;
            }
            CreaseA[v] = na; CreaseB[v] = nb;
            if (VertexFaces[v].Count == 0) { Rules[v] = VertexRule.Corner; VertexSharpness[v] = float.PositiveInfinity; }
            else if (sharpCount >= 3
                     || (onBoundary && boundary == BoundaryInterpolation.EdgeAndCorner && VertexFaces[v].Count == 1))
            { Rules[v] = VertexRule.Corner; VertexSharpness[v] = float.PositiveInfinity; }
            else if (sharpCount == 2) { Rules[v] = VertexRule.Crease; VertexSharpness[v] = sum / 2f; }
            else { Rules[v] = VertexRule.Smooth; VertexSharpness[v] = 0f; }
        }
    }

    /// <summary>
    /// The position a vertex moves to, from its smooth rule's position and
    /// its own rule: the curve (1/8, 6/8, 1/8) along a crease, fixed at a
    /// corner, a blend of the smooth and the sharp position for a
    /// sharpness below 1.
    /// </summary>
    public Vector3 VertexPoint(PolyMesh mesh, int v, Func<Vector3> smooth)
    {
        Vector3 s = mesh.Positions[v];
        Vector3 sharp;
        switch (Rules[v])
        {
            case VertexRule.Smooth: return smooth();
            case VertexRule.Crease:
                sharp = 0.75f * s + 0.125f * (mesh.Positions[CreaseA[v]] + mesh.Positions[CreaseB[v]]);
                break;
            default:
                sharp = s;
                break;
        }
        float sv = VertexSharpness[v];
        return sv >= 1f ? sharp : Vector3.Lerp(smooth(), sharp, sv);
    }

    /// <summary>
    /// The new point on an edge from its smooth rule's position: the
    /// midpoint on a sharp edge, a blend for a sharpness below 1.
    /// </summary>
    public static Vector3 EdgePoint(in Edge e, Vector3 mid, Func<Vector3> smooth)
        => e.Sharpness >= 1f ? mid : e.Sharpness <= 0f ? smooth() : Vector3.Lerp(smooth(), mid, e.Sharpness);

    /// <summary>
    /// Sharpness of the two halves of every crease edge after one level: one
    /// less (DeRose et al. 1998). Edges inside the old faces are smooth.
    /// </summary>
    public Dictionary<long, float>? ChildCreases(Func<long, int> edgePoint)
    {
        Dictionary<long, float>? child = null;
        foreach (var (key, e) in Edges)
        {
            // Boundary edges stay boundary edges, sharp at every level.
            if (e.FaceCount != 2 || e.Sharpness <= 1f) continue;
            float s = e.Sharpness - 1f;
            int m = edgePoint(key);
            child ??= new Dictionary<long, float>();
            child[EdgeKey(e.A, m)] = s;
            child[EdgeKey(m, e.B)] = s;
        }
        return child;
    }

    public static long EdgeKey(int a, int b)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }
}
