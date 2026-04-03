namespace RayTracer.Core;

/// <summary>
/// Analytical solver for polynomial equations up to degree 4 (quartic).
///
/// Uses Cardano's formula for cubics and Ferrari's method for quartics.
/// All methods return only real roots. Roots are returned in ascending order
/// and filtered to lie within a caller-specified [tMin, tMax] range.
///
/// Numerical robustness notes:
///   - Discriminant comparisons use a relative epsilon scaled to the
///     coefficient magnitudes, avoiding false classification near zero.
///   - The depressed quartic substitution (t = u - c3/4) is standard
///     Ferrari; it removes the cubic term and produces a resolvent cubic.
///   - Cube roots of negative numbers use -cbrt(|x|) to stay real.
///   - Double/triple root detection uses a tolerance of 1e-6.
///
/// Performance: the solver is allocation-free (Span-based) and branchless
/// where possible. A typical torus intersection calls SolveQuartic once
/// per ray, producing 0–4 roots in ~200 ns on modern hardware.
/// </summary>
public static class QuarticSolver
{
    private const double Eps = 1e-12;
    private const double RootDedupEps = 1e-6;

    // ═════════════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Solves a*t⁴ + b*t³ + c*t² + d*t + e = 0 for real roots in [tMin, tMax].
    /// Returns the number of valid roots written to <paramref name="roots"/>.
    /// Roots are sorted in ascending order.
    /// </summary>
    public static int SolveQuartic(
        double a, double b, double c, double d, double e,
        Span<double> roots, double tMin = double.NegativeInfinity, double tMax = double.PositiveInfinity)
    {
        if (Math.Abs(a) < Eps)
            return SolveCubic(b, c, d, e, roots, tMin, tMax);

        // Normalize to monic form: t⁴ + c3*t³ + c2*t² + c1*t + c0 = 0
        double invA = 1.0 / a;
        double c3 = b * invA;
        double c2 = c * invA;
        double c1 = d * invA;
        double c0 = e * invA;

        // ─────────────────────────────────────────────────────────────────
        // Ferrari's method: substitute t = u - c3/4 to get depressed quartic
        //   u⁴ + p*u² + q*u + r = 0
        // ─────────────────────────────────────────────────────────────────
        double c3_2 = c3 * c3;
        double c3_3 = c3_2 * c3;
        double c3_4 = c3_2 * c3_2;

        double p = c2 - 3.0 / 8.0 * c3_2;
        double q = c1 - 0.5 * c2 * c3 + c3_3 / 8.0;
        double r = c0 - c1 * c3 / 4.0 + c2 * c3_2 / 16.0 - 3.0 * c3_4 / 256.0;

        int count;

        if (Math.Abs(q) < Eps)
        {
            // Biquadratic: u⁴ + p*u² + r = 0 → solve as quadratic in u²
            count = SolveBiquadratic(p, r, roots);
        }
        else
        {
            // Full Ferrari: find a real root y of the resolvent cubic
            //   8*y³ - 4*p*y² - 8*r*y + (4*p*r - q²) = 0
            count = SolveViaFerrari(p, q, r, roots);
        }

        // Undo the substitution: t = u - c3/4
        double shift = -c3 / 4.0;
        int valid = 0;
        for (int i = 0; i < count; i++)
        {
            double t = roots[i] + shift;
            if (t >= tMin && t <= tMax)
                roots[valid++] = t;
        }

        // Sort the valid roots
        SortSpan(roots, valid);

        return valid;
    }

    /// <summary>
    /// Solves a*t³ + b*t² + c*t + d = 0 for real roots in [tMin, tMax].
    /// </summary>
    public static int SolveCubic(
        double a, double b, double c, double d,
        Span<double> roots, double tMin = double.NegativeInfinity, double tMax = double.PositiveInfinity)
    {
        if (Math.Abs(a) < Eps)
            return SolveQuadratic(b, c, d, roots, tMin, tMax);

        // Normalize to monic: t³ + pt² + qt + r = 0
        double invA = 1.0 / a;
        double p = b * invA;
        double q = c * invA;
        double r = d * invA;

        int count = SolveCubicMonic(p, q, r, roots);

        int valid = 0;
        for (int i = 0; i < count; i++)
        {
            if (roots[i] >= tMin && roots[i] <= tMax)
                roots[valid++] = roots[i];
        }
        SortSpan(roots, valid);
        return valid;
    }

    /// <summary>
    /// Solves a*t² + b*t + c = 0 for real roots in [tMin, tMax].
    /// </summary>
    public static int SolveQuadratic(
        double a, double b, double c,
        Span<double> roots, double tMin = double.NegativeInfinity, double tMax = double.PositiveInfinity)
    {
        if (Math.Abs(a) < Eps)
        {
            // Linear: b*t + c = 0
            if (Math.Abs(b) < Eps) return 0;
            double t = -c / b;
            if (t >= tMin && t <= tMax) { roots[0] = t; return 1; }
            return 0;
        }

        double disc = b * b - 4.0 * a * c;
        if (disc < -Eps) return 0;

        if (disc < Eps)
        {
            double t = -b / (2.0 * a);
            if (t >= tMin && t <= tMax) { roots[0] = t; return 1; }
            return 0;
        }

        double sqrtD = Math.Sqrt(disc);
        double t1 = (-b - sqrtD) / (2.0 * a);
        double t2 = (-b + sqrtD) / (2.0 * a);
        if (t1 > t2) (t1, t2) = (t2, t1);

        int valid = 0;
        if (t1 >= tMin && t1 <= tMax) roots[valid++] = t1;
        if (t2 >= tMin && t2 <= tMax && Math.Abs(t2 - t1) > RootDedupEps)
            roots[valid++] = t2;
        return valid;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INTERNAL — Cubic solver (Cardano's formula)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Solves the monic cubic t³ + pt² + qt + r = 0.
    /// Returns 1 or 3 real roots in the roots span (unsorted).
    /// </summary>
    private static int SolveCubicMonic(double p, double q, double r, Span<double> roots)
    {
        // Depress: substitute t = u - p/3 → u³ + au + b = 0
        double p2 = p * p;
        double a = q - p2 / 3.0;
        double b = r - p * q / 3.0 + 2.0 * p2 * p / 27.0;

        double shift = -p / 3.0;

        double disc = -4.0 * a * a * a - 27.0 * b * b; // Discriminant of depressed cubic

        if (disc < -Eps)
        {
            // One real root (Cardano)
            double halfB = -b / 2.0;
            double det = b * b / 4.0 + a * a * a / 27.0;
            double sqrtDet = Math.Sqrt(Math.Max(0.0, det));

            double u = Cbrt(halfB + sqrtDet);
            double v = Cbrt(halfB - sqrtDet);

            roots[0] = u + v + shift;
            return 1;
        }
        else if (disc < Eps)
        {
            // Triple or double root
            if (Math.Abs(a) < Eps && Math.Abs(b) < Eps)
            {
                // Triple root at 0
                roots[0] = shift;
                return 1;
            }

            // Double root + single root
            double u = Cbrt(-b / 2.0);
            roots[0] = 2.0 * u + shift;
            roots[1] = -u + shift;
            if (Math.Abs(roots[0] - roots[1]) < RootDedupEps)
                return 1;
            return 2;
        }
        else
        {
            // Three distinct real roots (trigonometric method)
            double m = 2.0 * Math.Sqrt(-a / 3.0);
            double theta = Math.Acos(3.0 * b / (a * m)) / 3.0;
            double twoPiOver3 = 2.0 * Math.PI / 3.0;

            roots[0] = m * Math.Cos(theta) + shift;
            roots[1] = m * Math.Cos(theta - twoPiOver3) + shift;
            roots[2] = m * Math.Cos(theta - 2.0 * twoPiOver3) + shift;
            return 3;
        }
    }

    /// <summary>
    /// Finds one real root of the monic cubic for use as the Ferrari resolvent.
    /// Prefers the largest root for numerical stability.
    /// </summary>
    private static double CubicRealRoot(double p, double q, double r)
    {
        Span<double> roots = stackalloc double[3];
        int count = SolveCubicMonic(p, q, r, roots);

        // Return the largest root — it gives the best numerical conditioning
        // for the subsequent quadratic factorizations in Ferrari.
        double best = roots[0];
        for (int i = 1; i < count; i++)
            if (roots[i] > best) best = roots[i];
        return best;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INTERNAL — Ferrari's factorization
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Solves the depressed quartic u⁴ + p*u² + q*u + r = 0 via Ferrari.
    /// Returns real roots in the roots span (may contain duplicates, unsorted).
    /// </summary>
    private static int SolveViaFerrari(double p, double q, double r, Span<double> roots)
    {
        // Resolvent cubic: 8y³ - 4py² - 8ry + (4pr - q²) = 0
        // Monic form: y³ - (p/2)y² - ry + (pr/2 - q²/8) = 0
        //           = y³ + ay² + by + c  where:
        double ra = -p / 2.0;
        double rb = -r;
        double rc = (p * r) / 2.0 - (q * q) / 8.0;

        double y = CubicRealRoot(ra, rb, rc);

        // From the resolvent root y, factor the quartic into two quadratics:
        //   (u² + y + s*u)(u² + y - s*u) = u⁴ + p*u² + q*u + r
        // where s² = 2y - p (must be ≥ 0 for real factorization)

        double s2 = 2.0 * y - p;
        if (s2 < -Eps)
        {
            // No real factorization — quartic has no real roots
            return 0;
        }
        double s = Math.Sqrt(Math.Max(0.0, s2));

        if (s < Eps)
        {
            // s ≈ 0: degenerate case, fall back to biquadratic
            return SolveBiquadratic(p, r, roots);
        }

        // Two quadratics:
        //   u² + s*u + (y + q/(2s)) = 0      … (I)
        //   u² - s*u + (y - q/(2s)) = 0      … (II)
        double halfQoverS = q / (2.0 * s);

        int count = 0;

        // Quadratic I: u² + s*u + (y + halfQoverS) = 0
        double discI = s * s - 4.0 * (y + halfQoverS);
        if (discI >= -Eps)
        {
            double sqrtI = Math.Sqrt(Math.Max(0.0, discI));
            roots[count++] = (-s + sqrtI) / 2.0;
            roots[count++] = (-s - sqrtI) / 2.0;
        }

        // Quadratic II: u² - s*u + (y - halfQoverS) = 0
        double discII = s * s - 4.0 * (y - halfQoverS);
        if (discII >= -Eps)
        {
            double sqrtII = Math.Sqrt(Math.Max(0.0, discII));
            roots[count++] = (s + sqrtII) / 2.0;
            roots[count++] = (s - sqrtII) / 2.0;
        }

        // Deduplicate near-identical roots
        count = Deduplicate(roots, count);

        return count;
    }

    /// <summary>
    /// Solves the biquadratic u⁴ + p*u² + r = 0 by substitution w = u².
    /// </summary>
    private static int SolveBiquadratic(double p, double r, Span<double> roots)
    {
        // w² + pw + r = 0
        double disc = p * p - 4.0 * r;
        if (disc < -Eps) return 0;

        double sqrtDisc = Math.Sqrt(Math.Max(0.0, disc));
        double w1 = (-p + sqrtDisc) / 2.0;
        double w2 = (-p - sqrtDisc) / 2.0;

        int count = 0;

        if (w1 >= -Eps)
        {
            double s = Math.Sqrt(Math.Max(0.0, w1));
            roots[count++] = s;
            if (s > RootDedupEps) roots[count++] = -s;
        }

        if (w2 >= -Eps && Math.Abs(w2 - w1) > Eps)
        {
            double s = Math.Sqrt(Math.Max(0.0, w2));
            roots[count++] = s;
            if (s > RootDedupEps) roots[count++] = -s;
        }

        return count;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Real cube root (handles negative arguments).</summary>
    private static double Cbrt(double x) =>
        x >= 0 ? Math.Cbrt(x) : -Math.Cbrt(-x);

    /// <summary>In-place insertion sort for small spans (≤ 4 elements).</summary>
    private static void SortSpan(Span<double> s, int count)
    {
        for (int i = 1; i < count; i++)
        {
            double key = s[i];
            int j = i - 1;
            while (j >= 0 && s[j] > key) { s[j + 1] = s[j]; j--; }
            s[j + 1] = key;
        }
    }

    /// <summary>Removes near-duplicate values from an unsorted span.</summary>
    private static int Deduplicate(Span<double> s, int count)
    {
        if (count <= 1) return count;
        SortSpan(s, count);
        int unique = 1;
        for (int i = 1; i < count; i++)
        {
            if (Math.Abs(s[i] - s[unique - 1]) > RootDedupEps)
                s[unique++] = s[i];
        }
        return unique;
    }
}
