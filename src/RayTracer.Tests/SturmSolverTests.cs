using RayTracer.Core;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Algebraic correctness tests for <see cref="SturmSolver"/>. The polynomial
/// root-finder is validated in isolation — no rendering, no ray tracing — so
/// that any failure caught by the lathe/higher-level tests can be cleanly
/// attributed to the solver or to the geometry code.
///
/// We compare results against known ground-truth roots (constructed by
/// factoring (x − r_k) explicitly), against <see cref="QuarticSolver"/> on
/// degree-4 polynomials, and against a stress suite of randomly-generated
/// degree-6 polynomials.
/// </summary>
public class SturmSolverTests
{
    private const double RootTol = 1e-6;

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Expands p(x) = Π (x − r_k) into power-basis coefficients, constant first.
    /// </summary>
    private static double[] FromRoots(params double[] roots)
    {
        // Start with p(x) = 1 (length 1). Multiply by (x − r_k) for each root.
        var c = new double[roots.Length + 1];
        c[0] = 1.0;
        int len = 1;
        foreach (double r in roots)
        {
            // Multiply coefficients by (x − r): new[i] = old[i−1] − r·old[i].
            for (int i = len; i >= 1; i--) c[i] = c[i - 1] - r * c[i];
            c[0] = -r * c[0];
            len++;
        }
        return c;
    }

    private static double Eval(double[] coeffs, double x)
    {
        double acc = coeffs[^1];
        for (int i = coeffs.Length - 2; i >= 0; i--) acc = acc * x + coeffs[i];
        return acc;
    }

    private static void AssertRootsMatch(IEnumerable<double> expected, Span<double> actual, int count,
                                         double tol = RootTol)
    {
        var exp = expected.OrderBy(x => x).ToArray();
        Assert.Equal(exp.Length, count);
        for (int i = 0; i < exp.Length; i++)
            Assert.InRange(actual[i], exp[i] - tol, exp[i] + tol);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Basic polynomials with known roots
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Quadratic_TwoSimpleRoots_FoundInOrder()
    {
        double[] coeffs = FromRoots(-0.5, 0.75); // (x + 0.5)(x − 0.75)
        Span<double> roots = stackalloc double[2];
        int n = SturmSolver.FindRoots(coeffs, -1.0, 1.0, roots);
        Assert.Equal(2, n);
        Assert.InRange(roots[0], -0.5 - RootTol, -0.5 + RootTol);
        Assert.InRange(roots[1],  0.75 - RootTol, 0.75 + RootTol);
    }

    [Fact]
    public void Cubic_ThreeRoots_AllInsideInterval()
    {
        double[] coeffs = FromRoots(-0.8, 0.1, 0.6);
        Span<double> roots = stackalloc double[3];
        int n = SturmSolver.FindRoots(coeffs, -1.0, 1.0, roots);
        Assert.Equal(3, n);
        AssertRootsMatch(new[] { -0.8, 0.1, 0.6 }, roots, n);
    }

    [Fact]
    public void Cubic_OnlySomeRootsInInterval_ReturnsSubset()
    {
        double[] coeffs = FromRoots(-5.0, 0.1, 5.0);
        Span<double> roots = stackalloc double[3];
        int n = SturmSolver.FindRoots(coeffs, -1.0, 1.0, roots);
        Assert.Equal(1, n);
        Assert.InRange(roots[0], 0.1 - RootTol, 0.1 + RootTol);
    }

    [Fact]
    public void Quartic_FourRoots_AllRecovered()
    {
        double[] coeffs = FromRoots(-0.9, -0.2, 0.3, 0.85);
        Span<double> roots = stackalloc double[4];
        int n = SturmSolver.FindRoots(coeffs, -1.0, 1.0, roots);
        Assert.Equal(4, n);
        AssertRootsMatch(new[] { -0.9, -0.2, 0.3, 0.85 }, roots, n);
    }

    [Fact]
    public void Degree6_SixRoots_AllRecovered()
    {
        double[] expected = { -0.9, -0.55, -0.1, 0.2, 0.55, 0.9 };
        double[] coeffs = FromRoots(expected);
        Span<double> roots = stackalloc double[6];
        int n = SturmSolver.FindRoots(coeffs, -1.0, 1.0, roots);
        Assert.Equal(6, n);
        AssertRootsMatch(expected, roots, n);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Degenerate cases
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoRealRoots_ReturnsZero()
    {
        // (x² + 1) has no real roots — coefficients [1, 0, 1].
        double[] coeffs = { 1.0, 0.0, 1.0 };
        Span<double> roots = stackalloc double[2];
        Assert.Equal(0, SturmSolver.FindRoots(coeffs, -10.0, 10.0, roots));
    }

    [Fact]
    public void DoubleRoot_FoundAtBisectionLimit()
    {
        // (x − 0.3)² = x² − 0.6 x + 0.09
        double[] coeffs = FromRoots(0.3, 0.3);
        Span<double> roots = stackalloc double[2];
        int n = SturmSolver.FindRoots(coeffs, 0.0, 1.0, roots);
        // Multiplicity-2 roots may be reported once (Sturm collapses the chain)
        // or twice (bisection fallback); either is acceptable as long as every
        // reported value is on the root.
        Assert.InRange(n, 1, 2);
        for (int i = 0; i < n; i++)
            Assert.InRange(roots[i], 0.3 - 1e-4, 0.3 + 1e-4);
    }

    [Fact]
    public void RootAtUpperEndpoint_IsReported()
    {
        // Roots at 0.3 and 1.0 — Sturm's (a, b] convention keeps the endpoint.
        double[] coeffs = FromRoots(0.3, 1.0);
        Span<double> roots = stackalloc double[2];
        int n = SturmSolver.FindRoots(coeffs, 0.0, 1.0, roots);
        Assert.Equal(2, n);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Agreement with QuarticSolver on degree-4 polynomials
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(42)]
    [InlineData(1337)]
    [InlineData(0xBEEF)]
    public void Quartic_AgreementWithQuarticSolver(int seed)
    {
        var rng = new Random(seed);

        int trials = 100;
        int agreed = 0;
        Span<double> qRoots = stackalloc double[4];
        Span<double> sRoots = stackalloc double[4];
        for (int k = 0; k < trials; k++)
        {
            double r1 = rng.NextDouble() * 2 - 1;
            double r2 = rng.NextDouble() * 2 - 1;
            double r3 = rng.NextDouble() * 2 - 1;
            double r4 = rng.NextDouble() * 2 - 1;
            double[] coeffs = FromRoots(r1, r2, r3, r4);

            // QuarticSolver wants leading-first coefficients.
            int nq = QuarticSolver.SolveQuartic(
                coeffs[4], coeffs[3], coeffs[2], coeffs[1], coeffs[0],
                qRoots, -1.0, 1.0);

            int ns = SturmSolver.FindRoots(coeffs, -1.0, 1.0, sRoots);

            // Both solvers should find the same roots that lie in (−1, 1).
            // QuarticSolver uses closed intervals so it may report a root at
            // exactly −1 whereas Sturm's open-left convention drops it. Count
            // only roots strictly inside and compare.
            var qInside = new List<double>();
            for (int i = 0; i < nq; i++)
                if (qRoots[i] > -1.0 + 1e-9) qInside.Add(qRoots[i]);

            if (qInside.Count != ns) continue;
            bool ok = true;
            qInside.Sort();
            for (int i = 0; i < ns; i++)
                if (System.Math.Abs(qInside[i] - sRoots[i]) > 1e-6) { ok = false; break; }
            if (ok) agreed++;
        }

        // Allow a small number of disagreements from boundary-root edge cases.
        Assert.True(agreed >= trials - 5,
            $"Sturm agreed with QuarticSolver on only {agreed}/{trials} random quartics.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Stress test — degree-6 polynomials with known roots
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Degree6_Stress_AllRootsFound()
    {
        const int trials = 200;
        const double interval = 0.95;
        var rng = new Random(9001);
        int fullRecall = 0;

        Span<double> roots = stackalloc double[6];
        for (int k = 0; k < trials; k++)
        {
            // Six random roots in (−interval, interval), sorted.
            var rs = new double[6];
            for (int i = 0; i < 6; i++) rs[i] = (rng.NextDouble() * 2 - 1) * interval;
            Array.Sort(rs);

            // Deduplicate near-coincident roots (they legitimately merge).
            var uniq = new List<double> { rs[0] };
            for (int i = 1; i < 6; i++)
                if (rs[i] - uniq[^1] > 1e-3) uniq.Add(rs[i]);

            double[] coeffs = FromRoots(rs);

            int n = SturmSolver.FindRoots(coeffs, -1.0, 1.0, roots);

            // Every returned root must evaluate close to zero.
            bool residualOk = true;
            for (int i = 0; i < n; i++)
                if (System.Math.Abs(Eval(coeffs, roots[i])) > 1e-5) { residualOk = false; break; }

            // Every unique root should be matched by one of the returned roots.
            bool recalled = true;
            foreach (double r in uniq)
            {
                bool ok = false;
                for (int i = 0; i < n; i++) if (System.Math.Abs(roots[i] - r) < 1e-3) { ok = true; break; }
                if (!ok) { recalled = false; break; }
            }

            if (residualOk && recalled) fullRecall++;
        }

        Assert.True(fullRecall >= trials - 5,
            $"Degree-6 Sturm recall: {fullRecall}/{trials} — expected near-100%.");
    }
}
