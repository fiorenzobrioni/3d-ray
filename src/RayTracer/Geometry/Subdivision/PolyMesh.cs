using System.Numerics;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Polygon mesh — the data structure that flows through the subdivision
/// pipeline before the final triangulation. Faces can have any arity ≥ 3
/// (Loop subdivision wants triangles, Catmull-Clark wants quads, the OBJ
/// loader can hand us n-gons that the auto-scheme triangulates first).
///
/// <para>
/// We carry vertex positions plus two optional per-face-corner attribute
/// streams: normals and UVs. Subdivision treats both as linearly interpolated
/// scalar fields with the <i>same connectivity</i> as positions — this is the
/// "vertex varying" channel in OpenSubdiv terminology. The simpler
/// "face varying" channel (separate UV/normal connectivity with potential
/// seams) would let texture seams stay sharp across subdivision; we leave
/// that for a future cycle. Meshes without UV seams (the common case for
/// hand-authored OBJ models) reproduce identically with the simpler scheme.
/// </para>
///
/// <para>
/// Faces store indices into the <see cref="Positions"/> /
/// <see cref="Normals"/> / <see cref="UVs"/> arrays. The three index lists
/// for a face must have the same length (the face's arity); when normals
/// or UVs are absent the corresponding list-of-arrays is null.
/// </para>
/// </summary>
internal sealed class PolyMesh
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3>? Normals { get; set; }
    public List<Vector2>? UVs { get; set; }

    public List<int[]> FacePositions { get; } = new();
    public List<int[]>? FaceNormals { get; set; }
    public List<int[]>? FaceUVs { get; set; }

    public bool HasNormals => Normals is { Count: > 0 } && FaceNormals is { Count: > 0 };
    public bool HasUVs => UVs is { Count: > 0 } && FaceUVs is { Count: > 0 };

    public int FaceCount => FacePositions.Count;

    /// <summary>True iff every face has exactly 3 vertices.</summary>
    public bool IsAllTriangles()
    {
        foreach (var f in FacePositions)
            if (f.Length != 3) return false;
        return true;
    }

    /// <summary>True iff every face has exactly 4 vertices.</summary>
    public bool IsAllQuads()
    {
        foreach (var f in FacePositions)
            if (f.Length != 4) return false;
        return true;
    }

    /// <summary>
    /// Fan-triangulates every n-gon (n > 3) in place. Used before Loop
    /// subdivision when the input has a mix of arities.
    /// </summary>
    public void TriangulateInPlace()
    {
        var newPos = new List<int[]>(FacePositions.Count);
        var newNor = FaceNormals != null ? new List<int[]>(FacePositions.Count) : null;
        var newUv  = FaceUVs     != null ? new List<int[]>(FacePositions.Count) : null;

        for (int i = 0; i < FacePositions.Count; i++)
        {
            int[] f = FacePositions[i];
            int[]? fn = FaceNormals?[i];
            int[]? fu = FaceUVs?[i];

            if (f.Length == 3)
            {
                newPos.Add(f);
                if (newNor != null) newNor.Add(fn!);
                if (newUv  != null) newUv .Add(fu!);
                continue;
            }

            for (int k = 1; k < f.Length - 1; k++)
            {
                newPos.Add(new[] { f[0], f[k], f[k + 1] });
                if (newNor != null) newNor.Add(new[] { fn![0], fn[k], fn[k + 1] });
                if (newUv  != null) newUv .Add(new[] { fu![0], fu[k], fu[k + 1] });
            }
        }

        FacePositions.Clear();
        FacePositions.AddRange(newPos);
        if (newNor != null) { FaceNormals!.Clear(); FaceNormals.AddRange(newNor); }
        if (newUv  != null) { FaceUVs!    .Clear(); FaceUVs    .AddRange(newUv ); }
    }

    /// <summary>
    /// Computes the AABB of the position cloud, returning (Vector3.Zero,
    /// Vector3.Zero) on an empty mesh.
    /// </summary>
    public (Vector3 Min, Vector3 Max) BoundingBox()
    {
        if (Positions.Count == 0) return (Vector3.Zero, Vector3.Zero);
        var min = Positions[0];
        var max = Positions[0];
        for (int i = 1; i < Positions.Count; i++)
        {
            min = Vector3.Min(min, Positions[i]);
            max = Vector3.Max(max, Positions[i]);
        }
        return (min, max);
    }

    /// <summary>
    /// Total length of every edge in the mesh, summed once per directed
    /// edge (so each undirected edge is counted twice when shared between
    /// two faces). Used by the adaptive screen-space heuristic.
    /// </summary>
    public float TotalEdgeLength()
    {
        float total = 0f;
        foreach (var f in FacePositions)
        {
            for (int i = 0; i < f.Length; i++)
            {
                Vector3 a = Positions[f[i]];
                Vector3 b = Positions[f[(i + 1) % f.Length]];
                total += (b - a).Length();
            }
        }
        return total;
    }

    /// <summary>Total directed-edge count (faces summed by arity).</summary>
    public int DirectedEdgeCount()
    {
        int count = 0;
        foreach (var f in FacePositions) count += f.Length;
        return count;
    }
}
