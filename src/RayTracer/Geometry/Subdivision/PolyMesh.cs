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

    /// <summary>
    /// Sharpness of the crease edges (key: <see cref="SubdivisionTopology.EdgeKey"/>
    /// of the two position indices). An edge with sharpness s is sharp for s
    /// levels of subdivision, then smooth (DeRose et al. 1998); infinity keeps
    /// it sharp. Boundary edges are always sharp and need no entry.
    /// </summary>
    public Dictionary<long, float>? Creases { get; set; }

    /// <summary>
    /// Normals of the limit surface at each position, set when the positions
    /// are moved onto the limit surface (<see cref="LimitSurface"/>): exact at
    /// smooth vertices, the angle-weighted mean of the faces around a crease
    /// or corner vertex. Null once anything moves the positions again.
    /// </summary>
    public Vector3[]? LimitNormals { get; set; }
    public List<int[]>? FaceNormals { get; set; }
    public List<int[]>? FaceUVs { get; set; }

    /// <summary>
    /// Material slot of each face (an index into the importer's material
    /// list), or null when the whole mesh has one material. Subdivision
    /// gives every child face its parent's slot.
    /// </summary>
    public List<int>? FaceMaterials { get; set; }

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
    /// Triangulates every polygon (n > 3) in place (<see cref="TriangulateFace"/>).
    /// Used before Loop subdivision when the input has a mix of arities.
    /// </summary>
    public void TriangulateInPlace()
    {
        var newPos = new List<int[]>(FacePositions.Count);
        var newNor = FaceNormals != null ? new List<int[]>(FacePositions.Count) : null;
        var newUv  = FaceUVs     != null ? new List<int[]>(FacePositions.Count) : null;
        var newMat = FaceMaterials != null ? new List<int>(FacePositions.Count) : null;

        for (int i = 0; i < FacePositions.Count; i++)
        {
            int[] f = FacePositions[i];
            int[]? fn = FaceNormals?[i];
            int[]? fu = FaceUVs?[i];
            foreach (var (a, b, c) in TriangulateFace(f))
            {
                newPos.Add(new[] { f[a], f[b], f[c] });
                if (newNor != null) newNor.Add(fn!.Length > 0 ? new[] { fn[a], fn[b], fn[c] } : fn);
                if (newUv  != null) newUv .Add(fu!.Length > 0 ? new[] { fu[a], fu[b], fu[c] } : fu);
                newMat?.Add(FaceMaterials![i]);
            }
        }

        FacePositions.Clear();
        FacePositions.AddRange(newPos);
        if (newNor != null) { FaceNormals!.Clear(); FaceNormals.AddRange(newNor); }
        if (newUv  != null) { FaceUVs!    .Clear(); FaceUVs    .AddRange(newUv ); }
        if (newMat != null) { FaceMaterials = newMat; }
    }

    /// <summary>
    /// Splits a polygon into triangles, as triples of its corner indices with
    /// the polygon's winding (PIANO-REVIEW.md Phase 2). A triangle stays as it
    /// is; a quad is cut along its shorter diagonal, or along the one through
    /// its reflex corner when it is concave; a larger polygon is ear-clipped in
    /// the plane of its (Newell) normal, so a concave n-gon is not covered by
    /// a fan reaching outside it. A polygon ear clipping cannot handle (self
    /// intersecting, degenerate) falls back to the fan.
    /// </summary>
    public List<(int A, int B, int C)> TriangulateFace(int[] f)
    {
        int n = f.Length;
        var tris = new List<(int, int, int)>(Math.Max(1, n - 2));
        if (n == 3) { tris.Add((0, 1, 2)); return tris; }

        Vector3 normal = Vector3.Zero;
        for (int k = 0; k < n; k++)
        {
            Vector3 a = Positions[f[k]], b = Positions[f[(k + 1) % n]];
            normal += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }

        if (n == 4)
        {
            // Diagonal 0-2 is valid iff corners 1 and 3 lie on opposite sides
            // of it (both triangles keep the polygon's orientation); same for 1-3.
            Vector3 p0 = Positions[f[0]], p1 = Positions[f[1]], p2 = Positions[f[2]], p3 = Positions[f[3]];
            bool d02 = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) > 0f
                    && Vector3.Dot(Vector3.Cross(p2 - p0, p3 - p0), normal) > 0f;
            bool d13 = Vector3.Dot(Vector3.Cross(p2 - p1, p3 - p1), normal) > 0f
                    && Vector3.Dot(Vector3.Cross(p3 - p1, p0 - p1), normal) > 0f;
            bool use13 = d13 && (!d02 || (p3 - p1).LengthSquared() < (p2 - p0).LengthSquared());
            if (use13) { tris.Add((0, 1, 3)); tris.Add((1, 2, 3)); }
            else { tris.Add((0, 1, 2)); tris.Add((0, 2, 3)); }
            return tris;
        }

        float len = normal.Length();
        if (len > 0f)
        {
            // A frame with the normal as +Z: the polygon is counter-clockwise
            // in it, and so are the ears.
            normal /= len;
            Vector3 helper = MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 ax = Vector3.Normalize(Vector3.Cross(helper, normal));
            Vector3 ay = Vector3.Cross(normal, ax);
            var flat = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                Vector3 p = Positions[f[k]];
                flat[k] = new Vector2(Vector3.Dot(p, ax), Vector3.Dot(p, ay));
            }
            var ears = Polygon2D.EarClip(flat, out bool complete);
            if (complete)
            {
                foreach (var (a, b, c) in ears) tris.Add((a, b, c));
                return tris;
            }
        }
        for (int k = 1; k < n - 1; k++) tris.Add((0, k, k + 1));
        return tris;
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
