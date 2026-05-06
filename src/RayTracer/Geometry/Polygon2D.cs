using System.Numerics;

namespace RayTracer.Geometry;

/// <summary>
/// Static helpers for 2D simple polygons used by the <see cref="Extrusion"/>
/// primitive: signed area / orientation, ear-clipping triangulation of
/// concave-but-simple polygons and tessellation of closed Catmull-Rom and
/// cubic Bezier profile loops.
///
/// Conventions:
/// <list type="bullet">
///   <item>The polygon is given as a closed loop of <see cref="Vector2"/>
///         points (no implicit duplicate of the first point at the end).</item>
///   <item>Counter-clockwise winding (positive shoelace area) means the
///         interior lies to the left of each edge — extrusion sidewall
///         outward normals are computed under that assumption.</item>
/// </list>
/// </summary>
internal static class Polygon2D
{
    /// <summary>
    /// Signed area via the shoelace formula. Positive when the vertices are
    /// counter-clockwise, negative when clockwise.
    /// </summary>
    public static float SignedArea(IReadOnlyList<Vector2> poly)
    {
        int n = poly.Count;
        if (n < 3) return 0f;
        float s = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            s += a.X * b.Y - b.X * a.Y;
        }
        return 0.5f * s;
    }

    /// <summary>
    /// Triangulates a simple (non self-intersecting) polygon using the
    /// classical O(n²) ear-clipping algorithm. Returns triangle index triples
    /// into the input list, with counter-clockwise winding when the polygon
    /// is CCW. Concave vertices and acute notches are handled correctly; the
    /// algorithm fails (returns an empty list) only on degenerate polygons
    /// (self-intersecting, collinear, near-zero area).
    /// </summary>
    public static List<(int A, int B, int C)> EarClip(IReadOnlyList<Vector2> poly)
    {
        var result = new List<(int, int, int)>();
        int n = poly.Count;
        if (n < 3) return result;

        // Work on a CCW copy so the ear test can use a single sign.
        bool ccw = SignedArea(poly) >= 0f;
        var indices = new List<int>(n);
        if (ccw)
            for (int i = 0; i < n; i++) indices.Add(i);
        else
            for (int i = n - 1; i >= 0; i--) indices.Add(i);

        // Defensive cap on iterations: a well-formed simple polygon needs
        // exactly n-2 ears, but a numerically degenerate polygon can stall.
        int guard = 3 * n;
        while (indices.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];

                Vector2 a = poly[prev];
                Vector2 b = poly[curr];
                Vector2 c = poly[next];

                // Convex test (left turn ⇒ ear candidate in CCW polygon).
                if (Cross(b - a, c - b) <= 0f) continue;

                // No other vertex of the remaining polygon lies inside the
                // candidate triangle.
                bool empty = true;
                for (int j = 0; j < indices.Count; j++)
                {
                    int k = indices[j];
                    if (k == prev || k == curr || k == next) continue;
                    if (PointInTriangle(poly[k], a, b, c)) { empty = false; break; }
                }
                if (!empty) continue;

                result.Add((prev, curr, next));
                indices.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break; // numerical stall — bail gracefully
        }

        if (indices.Count == 3)
            result.Add((indices[0], indices[1], indices[2]));

        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // Barycentric sign test. A point is inside (or on the edge of) a CCW
        // triangle iff all three edge crosses are non-negative.
        float c1 = Cross(b - a, p - a);
        float c2 = Cross(c - b, p - b);
        float c3 = Cross(a - c, p - c);
        bool nonNeg = c1 >= 0f && c2 >= 0f && c3 >= 0f;
        bool nonPos = c1 <= 0f && c2 <= 0f && c3 <= 0f;
        return nonNeg || nonPos;
    }

    /// <summary>
    /// Densifies a closed Catmull-Rom control polygon by sampling each edge
    /// at <paramref name="samplesPerEdge"/> internal points using centripetal
    /// (α = 0.5) parameterisation — the same choice as
    /// <see cref="CatmullRomToBezier"/> for the lathe, picked because it
    /// never self-intersects and never develops cusps in clustered regions.
    /// Returns a new closed polyline (no duplicated last point).
    /// </summary>
    public static List<Vector2> TessellateClosedCatmullRom(
        IReadOnlyList<Vector2> points, int samplesPerEdge)
    {
        int n = points.Count;
        var dense = new List<Vector2>(n * Math.Max(1, samplesPerEdge));
        if (n < 3) { dense.AddRange(points); return dense; }
        if (samplesPerEdge < 1) samplesPerEdge = 1;

        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = points[(i - 1 + n) % n];
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % n];
            Vector2 p3 = points[(i + 2) % n];

            // Centripetal knot spacing.
            double t0 = 0.0;
            double t1 = t0 + Math.Sqrt(Math.Max((p1 - p0).Length(), 1e-12));
            double t2 = t1 + Math.Sqrt(Math.Max((p2 - p1).Length(), 1e-12));
            double t3 = t2 + Math.Sqrt(Math.Max((p3 - p2).Length(), 1e-12));

            // Emit p1 at u=0 and `samplesPerEdge - 1` interior samples; the
            // u=1 endpoint is the next iteration's p1, so we don't duplicate.
            for (int k = 0; k < samplesPerEdge; k++)
            {
                double u = (double)k / samplesPerEdge;
                double t = t1 + u * (t2 - t1);
                dense.Add(EvalBarryGoldman(p0, p1, p2, p3, t0, t1, t2, t3, t));
            }
        }
        return dense;
    }

    /// <summary>
    /// Densifies a closed sequence of cubic Bezier segments. The Bezier
    /// control list must contain <c>4 · N</c> entries — one quadruple per
    /// segment — and the <c>last</c> control of segment <c>i</c> must equal
    /// the <c>first</c> of segment <c>i+1</c> (loop closure with C⁰
    /// continuity); this matches the layout used by the lathe's Bezier mode.
    /// </summary>
    public static List<Vector2> TessellateClosedBezier(
        IReadOnlyList<Vector2> bezierControls, int samplesPerSegment)
    {
        var dense = new List<Vector2>();
        if (bezierControls.Count < 4 || bezierControls.Count % 4 != 0) return dense;
        int segCount = bezierControls.Count / 4;
        if (samplesPerSegment < 1) samplesPerSegment = 1;

        for (int s = 0; s < segCount; s++)
        {
            int k = s * 4;
            Vector2 b0 = bezierControls[k];
            Vector2 b1 = bezierControls[k + 1];
            Vector2 b2 = bezierControls[k + 2];
            Vector2 b3 = bezierControls[k + 3];
            for (int i = 0; i < samplesPerSegment; i++)
            {
                float u = (float)i / samplesPerSegment;
                dense.Add(EvalCubic(b0, b1, b2, b3, u));
            }
        }
        return dense;
    }

    private static Vector2 EvalCubic(Vector2 b0, Vector2 b1, Vector2 b2, Vector2 b3, float u)
    {
        float v = 1f - u;
        float c0 = v * v * v;
        float c1 = 3f * v * v * u;
        float c2 = 3f * v * u * u;
        float c3 = u * u * u;
        return c0 * b0 + c1 * b1 + c2 * b2 + c3 * b3;
    }

    private static Vector2 EvalBarryGoldman(
        Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        double t0, double t1, double t2, double t3, double t)
    {
        Vector2 a1 = Blend(p0, p1, t0, t1, t);
        Vector2 a2 = Blend(p1, p2, t1, t2, t);
        Vector2 a3 = Blend(p2, p3, t2, t3, t);
        Vector2 b1 = Blend(a1, a2, t0, t2, t);
        Vector2 b2 = Blend(a2, a3, t1, t3, t);
        return Blend(b1, b2, t1, t2, t);
    }

    private static Vector2 Blend(Vector2 lo, Vector2 hi, double tLo, double tHi, double t)
    {
        double denom = tHi - tLo;
        if (denom < 1e-12) return lo;
        float w = (float)((t - tLo) / denom);
        return lo * (1f - w) + hi * w;
    }
}
