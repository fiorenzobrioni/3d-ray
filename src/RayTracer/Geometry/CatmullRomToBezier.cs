using System.Numerics;

namespace RayTracer.Geometry;

/// <summary>
/// Converts a centripetal Catmull-Rom control polygon (Yuksel et al. 2011,
/// "On the Parameterization of Catmull-Rom Curves") into a piecewise cubic
/// Bezier representation. The Bezier pieces can then share the same hit and
/// sampling code used for explicit Bezier input (<see cref="SplineSegment"/>).
///
/// Centripetal parameterisation (α = 1/2) is chosen because it is the only
/// canonical option that guarantees the curve neither self-intersects nor
/// develops cusps in regions where control points cluster — both failure
/// modes are common with the naive uniform parameterisation, and both would
/// cause the implicit grade-6 surface to collapse onto itself and produce
/// phantom intersections.
///
/// Endpoints use reflected phantom points (P_{-1} = 2 P_0 - P_1, etc.) so
/// the spline starts/ends with natural boundary tangents (no extrapolation).
/// </summary>
internal static class CatmullRomToBezier
{
    /// <summary>
    /// Converts <paramref name="points"/> into <c>N - 1</c> cubic Bezier
    /// segments. Each returned quadruple holds the four Bezier control
    /// points for segment <c>i → i+1</c>, in order.
    /// </summary>
    /// <param name="points">Control polygon, <c>N ≥ 4</c> required.</param>
    public static List<Vector2[]> Convert(IReadOnlyList<Vector2> points)
    {
        int n = points.Count;
        if (n < 4)
            throw new ArgumentException("Centripetal Catmull-Rom requires at least 4 points.", nameof(points));

        // Build the padded control polygon with reflected phantom endpoints.
        // Phantoms give zero-second-derivative (natural) tangents at P_0 and P_{N-1}.
        var padded = new Vector2[n + 2];
        padded[0]       = 2f * points[0] - points[1];
        padded[n + 1]   = 2f * points[n - 1] - points[n - 2];
        for (int i = 0; i < n; i++) padded[i + 1] = points[i];

        // Centripetal knot spacing: Δt_i = |P_{i+1} - P_i|^{1/2}.
        // Add a tiny epsilon so colinear coincident points don't divide by zero.
        var knots = new double[padded.Length];
        knots[0] = 0.0;
        for (int i = 1; i < padded.Length; i++)
        {
            double d = (padded[i] - padded[i - 1]).Length();
            knots[i] = knots[i - 1] + System.Math.Sqrt(System.Math.Max(d, 1e-12));
        }

        var result = new List<Vector2[]>(n - 1);

        // Each original segment (P_i, P_{i+1}) maps to padded indices (i+1, i+2).
        for (int i = 0; i < n - 1; i++)
        {
            int k = i + 1; // index into 'padded' corresponding to segment start
            Vector2 p0 = padded[k - 1];
            Vector2 p1 = padded[k];
            Vector2 p2 = padded[k + 1];
            Vector2 p3 = padded[k + 2];
            double t0 = knots[k - 1];
            double t1 = knots[k];
            double t2 = knots[k + 1];
            double t3 = knots[k + 2];

            // Evaluate the non-uniform Catmull-Rom (Barry-Goldman pyramid)
            // at u = 1/3 and u = 2/3 on the reparametrised [t1, t2] interval.
            // The cubic is uniquely determined by its values at 0, 1/3, 2/3, 1,
            // so we can recover the Bezier control polygon in closed form.
            Vector2 q0 = p1;
            Vector2 q1 = EvalBarryGoldman(p0, p1, p2, p3, t0, t1, t2, t3,
                                          Lerp(t1, t2, 1.0 / 3.0));
            Vector2 q2 = EvalBarryGoldman(p0, p1, p2, p3, t0, t1, t2, t3,
                                          Lerp(t1, t2, 2.0 / 3.0));
            Vector2 q3 = p2;

            // Closed-form Bezier interpolation through (q0, q1, q2, q3):
            //   q1 = (8 q0 + 12 b1 + 6 b2 + q3) / 27
            //   q2 = (q0 + 6 b1 + 12 b2 + 8 q3) / 27
            // Solving for b1, b2:
            //   b1 = (-5 q0 + 18 q1 -  9 q2 + 2 q3) / 6
            //   b2 = ( 2 q0 -  9 q1 + 18 q2 - 5 q3) / 6
            Vector2 b1 = (-5f * q0 + 18f * q1 - 9f * q2 + 2f * q3) / 6f;
            Vector2 b2 = ( 2f * q0 -  9f * q1 + 18f * q2 - 5f * q3) / 6f;

            result.Add(new[] { q0, b1, b2, q3 });
        }

        return result;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// Barry-Goldman pyramid evaluation of the non-uniform Catmull-Rom curve
    /// on the middle segment [t1, t2] given the four surrounding control
    /// points P0..P3 and knots t0 &lt; t1 &lt; t2 &lt; t3.
    /// </summary>
    private static Vector2 EvalBarryGoldman(
        Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        double t0, double t1, double t2, double t3,
        double t)
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
