using System.Numerics;

namespace RayTracer.Rendering.Guiding;

/// <summary>
/// Directional distribution of one region of space (Müller, Gross, Novák 2017,
/// "Practical Path Guiding"): a quadtree over the unit square, which the
/// cylindrical equal-area map turns into the sphere of directions (z = cos θ,
/// φ), so a cell's share of the square is its share of solid angle. Each
/// node keeps the energy recorded in its four children; sampling descends
/// in proportion to them, and refinement splits the cells that hold more than
/// a fixed fraction of the total.
///
/// <para>Child k of a node covers the quadrant (k &amp; 1, k &gt;&gt; 1) of the node's
/// square. A child index of 0 marks a leaf (the root is node 0, never a
/// child).</para>
/// </summary>
public sealed class DTree
{
    /// <summary>Deepest level a cell is split to (cells of 4^-20 of the sphere).</summary>
    public const int MaxDepth = 20;

    private int[] _child;
    private float[] _sum;
    private int _nodes;

    /// <summary>Energy recorded in the whole tree.</summary>
    public float Total
    {
        get
        {
            return _sum[0] + _sum[1] + _sum[2] + _sum[3];
        }
    }

    /// <summary>Records splatted into this tree (zero-weight ones included).</summary>
    public long Records { get; private set; }

    /// <summary>Number of nodes (interior cells).</summary>
    public int NodeCount => _nodes;

    private DTree(int capacity)
    {
        _child = new int[4 * capacity];
        _sum = new float[4 * capacity];
        _nodes = 0;
    }

    /// <summary>A root with four empty leaves: the uniform distribution's shape, no energy.</summary>
    public static DTree Empty()
    {
        var t = new DTree(4);
        t.NewNode();
        return t;
    }

    private int NewNode()
    {
        if (4 * (_nodes + 1) > _child.Length)
        {
            Array.Resize(ref _child, _child.Length * 2);
            Array.Resize(ref _sum, _sum.Length * 2);
        }
        int n = _nodes++;
        for (int k = 0; k < 4; k++) { _child[4 * n + k] = 0; _sum[4 * n + k] = 0f; }
        return n;
    }

    /// <summary>A copy with the same structure and energy.</summary>
    public DTree Clone()
    {
        var t = new DTree(Math.Max(_nodes, 1))
        {
            _nodes = _nodes,
            Records = Records,
        };
        Array.Copy(_child, t._child, 4 * _nodes);
        Array.Copy(_sum, t._sum, 4 * _nodes);
        return t;
    }

    private static int Quadrant(ref Vector2 p)
    {
        int k = 0;
        if (p.X >= 0.5f) { k |= 1; p.X -= 0.5f; }
        if (p.Y >= 0.5f) { k |= 2; p.Y -= 0.5f; }
        p *= 2f;
        return k;
    }

    /// <summary>Adds <paramref name="weight"/> to every cell containing <paramref name="p"/>.</summary>
    public void Record(Vector2 p, float weight)
    {
        Records++;
        if (!(weight > 0f) || float.IsInfinity(weight)) return;
        p = Vector2.Clamp(p, Vector2.Zero, new Vector2(0.99999994f));
        int node = 0;
        for (int depth = 0; depth <= MaxDepth; depth++)
        {
            int k = Quadrant(ref p);
            _sum[4 * node + k] += weight;
            int c = _child[4 * node + k];
            if (c == 0) return;
            node = c;
        }
    }

    /// <summary>
    /// Warps <paramref name="u"/> (uniform on the square) to a point distributed
    /// as the tree's energy: one binary choice per axis and level, so a
    /// stratified input stays stratified. False when the tree is empty.
    /// </summary>
    public bool Sample(Vector2 u, out Vector2 p)
    {
        p = default;
        if (!(Total > 0f)) return false;
        Vector2 origin = Vector2.Zero;
        float size = 1f;
        int node = 0;
        for (int depth = 0; depth <= MaxDepth; depth++)
        {
            int b = 4 * node;
            float s0 = _sum[b], s1 = _sum[b + 1], s2 = _sum[b + 2], s3 = _sum[b + 3];
            float total = s0 + s1 + s2 + s3;
            if (!(total > 0f)) { p = InCell(origin, size, u); return true; }

            // Column (x) first, then the row within it.
            float left = (s0 + s2) / total;
            int x;
            if (u.X < left) { x = 0; u.X /= left; }
            else { x = 1; u.X = (u.X - left) / (1f - left); }
            float colLo = x == 0 ? s0 : s1, colHi = x == 0 ? s2 : s3;
            float lower = colLo / (colLo + colHi);
            int y;
            if (u.Y < lower) { y = 0; u.Y /= lower; }
            else { y = 1; u.Y = (u.Y - lower) / (1f - lower); }
            u = Vector2.Clamp(u, Vector2.Zero, new Vector2(0.99999994f));

            int k = x | (y << 1);
            size *= 0.5f;
            origin += new Vector2(x, y) * size;
            int c = _child[b + k];
            if (c == 0) { p = InCell(origin, size, u); return true; }
            node = c;
        }
        p = InCell(origin, size, u);
        return true;
    }

    // origin + u·size can round up onto the cell's far edge, which belongs to
    // the next cell (perhaps an empty one); keep the point inside.
    private static Vector2 InCell(Vector2 origin, float size, Vector2 u)
    {
        Vector2 p = origin + u * size;
        return new Vector2(
            MathF.Min(p.X, MathF.BitDecrement(origin.X + size)),
            MathF.Min(p.Y, MathF.BitDecrement(origin.Y + size)));
    }

    /// <summary>Density of <see cref="Sample"/> on the unit square at <paramref name="p"/>.</summary>
    public float PdfSquare(Vector2 p)
    {
        if (!(Total > 0f)) return 0f;
        p = Vector2.Clamp(p, Vector2.Zero, new Vector2(0.99999994f));
        float pdf = 1f;
        int node = 0;
        for (int depth = 0; depth <= MaxDepth; depth++)
        {
            int b = 4 * node;
            float total = _sum[b] + _sum[b + 1] + _sum[b + 2] + _sum[b + 3];
            if (!(total > 0f)) return pdf;
            int k = Quadrant(ref p);
            float s = _sum[b + k];
            if (!(s > 0f)) return 0f;
            pdf *= 4f * s / total;
            int c = _child[b + k];
            if (c == 0) return pdf;
            node = c;
        }
        return pdf;
    }

    /// <summary>
    /// The structure for the next iteration: every cell holding more than
    /// <paramref name="threshold"/> of the total energy is split (a leaf as
    /// many times as its energy, spread evenly, stays above the threshold),
    /// the others become leaves. Energies start at zero. An empty tree keeps
    /// its structure.
    /// </summary>
    public DTree Refined(float threshold)
    {
        float total = Total;
        if (!(total > 0f))
        {
            var same = Clone();
            Array.Clear(same._sum, 0, 4 * same._nodes);
            same.Records = 0;
            return same;
        }

        var t = new DTree(Math.Max(_nodes, 4));
        int root = t.NewNode();
        BuildChildren(t, root, 0, 1, total, threshold);
        return t;
    }

    private void BuildChildren(DTree t, int newNode, int oldNode, int depth, float total, float threshold)
    {
        for (int k = 0; k < 4; k++)
        {
            float e = _sum[4 * oldNode + k];
            if (depth >= MaxDepth || !(e > threshold * total)) continue;
            int n = t.NewNode();
            t._child[4 * newNode + k] = n;
            int oldChild = _child[4 * oldNode + k];
            if (oldChild != 0)
                BuildChildren(t, n, oldChild, depth + 1, total, threshold);
            else
                SplitEvenly(t, n, e * 0.25f, depth + 1, total, threshold);
        }
    }

    private static void SplitEvenly(DTree t, int node, float childEnergy, int depth, float total, float threshold)
    {
        if (depth >= MaxDepth || !(childEnergy > threshold * total)) return;
        for (int k = 0; k < 4; k++)
        {
            int n = t.NewNode();
            t._child[4 * node + k] = n;
            SplitEvenly(t, n, childEnergy * 0.25f, depth + 1, total, threshold);
        }
    }

    // ── Direction ↔ square ──────────────────────────────────────────────────

    /// <summary>Cylindrical equal-area map: (cos θ + 1)/2, φ/2π, with θ from +Z.</summary>
    public static Vector2 ToSquare(Vector3 d)
    {
        float z = Math.Clamp(d.Z, -1f, 1f);
        float phi = MathF.Atan2(d.Y, d.X);
        if (phi < 0f) phi += 2f * MathF.PI;
        return new Vector2((z + 1f) * 0.5f, phi / (2f * MathF.PI));
    }

    public static Vector3 FromSquare(Vector2 p)
    {
        float z = 2f * p.X - 1f;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        float phi = 2f * MathF.PI * p.Y;
        return new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
    }

    /// <summary>The map preserves area: a density on the square is 4π times one in solid angle.</summary>
    public const float SquareToSolidAngle = 1f / (4f * MathF.PI);
}
