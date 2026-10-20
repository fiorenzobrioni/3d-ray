using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Accuracy of the polynomial solvers behind the curved primitives
/// (PIANO-REVIEW.md, Phase 1): roots of polynomials with known roots, and the
/// torus intersection against an independent double-precision reference
/// (dense march of the implicit function plus bisection).
/// </summary>
public class SolverPrecisionTests
{
    private readonly ITestOutputHelper _out;
    public SolverPrecisionTests(ITestOutputHelper output) => _out = output;

    private static double[] Expand(double[] r)
    {
        // Π (t − r_i) → coefficients, constant first.
        var c = new double[] { 1 };
        foreach (double root in r)
        {
            var n = new double[c.Length + 1];
            for (int i = 0; i < c.Length; i++) { n[i + 1] += c[i]; n[i] -= root * c[i]; }
            c = n;
        }
        return c;
    }

    [Fact]
    public void Quartic_FindsKnownRoots()
    {
        var rng = new Random(2);
        Span<double> roots = stackalloc double[4];
        double worst = 0;
        for (int k = 0; k < 20000; k++)
        {
            var r = Enumerable.Range(0, 4).Select(_ => rng.NextDouble() * 10 - 5).OrderBy(x => x).ToArray();
            // Keep roots apart: a double root is ill-conditioned for any solver
            // (its sensitivity is √ε); close pairs are tested separately below.
            if (r.Zip(r.Skip(1), (a, b) => b - a).Min() < 1e-2) continue;
            var c = Expand(r);
            int n = QuarticSolver.SolveQuartic(c[4], c[3], c[2], c[1], c[0], roots);
            Assert.Equal(4, n);
            for (int i = 0; i < 4; i++) worst = Math.Max(worst, Math.Abs(roots[i] - r[i]));
        }
        _out.WriteLine($"quartic, distinct roots: worst |root error| {worst:E2}");
        // Measured 1.5e-10 on roots in [-5, 5] (4e-8 before the cubic's
        // relative discriminant test): far below the float precision of a hit
        // distance (~1e-7 relative), which is what it feeds.
        Assert.True(worst < 1e-8, $"worst root error {worst:E2}");
    }

    [Theory]
    [InlineData(1e-2, 1e-7)]
    [InlineData(1e-3, 1e-6)]
    [InlineData(1e-4, 1e-6)]
    [InlineData(1e-5, 1e-5)]
    public void Quartic_CloseRoots_AreBothFound(double separation, double tolerance)
    {
        // A ray grazing a torus tube: two roots `separation` apart. Their
        // conditioning grows as ε / separation, hence the looser gate for the
        // closer pairs. Before the cubic's relative discriminant test the
        // resolvent was taken for a double root: pairs 1e-2 apart came back
        // 5e-4 apart, and pairs closer than 1e-4 were lost in 1-3% of cases.
        var rng = new Random(5);
        Span<double> roots = stackalloc double[4];
        double worst = 0;
        int missed = 0;
        for (int k = 0; k < 20000; k++)
        {
            double d = rng.NextDouble() * 10 - 5;
            double a = rng.NextDouble() * 10 - 5, b = rng.NextDouble() * 10 - 5;
            if (Math.Min(Math.Abs(a - d), Math.Abs(b - d)) < 0.1 || Math.Abs(a - b) < 1e-2) continue;
            var r = new[] { d, d + separation, a, b };
            var c = Expand(r);
            int n = QuarticSolver.SolveQuartic(c[4], c[3], c[2], c[1], c[0], roots);
            if (n < 4) { missed++; continue; }
            Array.Sort(r);
            for (int i = 0; i < 4; i++) worst = Math.Max(worst, Math.Abs(roots[i] - r[i]));
        }
        _out.WriteLine($"quartic, pair {separation:E0} apart: {missed} lost, worst |root error| {worst:E2}");
        Assert.Equal(0, missed);
        Assert.True(worst < tolerance, $"worst root error {worst:E2}");
    }

    [Fact]
    public void Sturm_FindsKnownRoots()
    {
        var rng = new Random(3);
        double worst = 0;
        Span<double> roots = stackalloc double[8];
        for (int k = 0; k < 5000; k++)
        {
            int deg = 2 + k % 5;
            var r = Enumerable.Range(0, deg).Select(_ => rng.NextDouble()).OrderBy(x => x).ToArray();
            if (r.Zip(r.Skip(1), (a, b) => b - a).Min() < 1e-2) continue;
            var c = Expand(r);
            int n = SturmSolver.FindRoots(c, 0.0, 1.0, roots[..deg], 1e-12);
            Assert.Equal(deg, n);
            for (int i = 0; i < deg; i++) worst = Math.Max(worst, Math.Abs(roots[i] - r[i]));
        }
        _out.WriteLine($"sturm, distinct roots in (0, 1]: worst |root error| {worst:E2}");
        // Measured 1.2e-6 (the tolerance is a residual, not a root accuracy):
        // the lathe solves for a segment parameter in [0, 1], so this is a
        // micron on a one-metre profile segment.
        Assert.True(worst < 5e-6, $"worst root error {worst:E2}");
    }

    [Fact]
    public void Torus_MatchesDoublePrecisionReference()
    {
        const double R = 1.0, r = 0.3;
        var torus = new Torus((float)R, (float)r, new Lambertian(new Vector3(0.5f)));
        var rng = new Random(4);
        int compared = 0, misses = 0, falseHits = 0;
        double worstT = 0;
        for (int k = 0; k < 20000; k++)
        {
            // Origin on a sphere of radius 4, aimed at a random point of the
            // torus' bounding region (many grazing rays at the silhouettes).
            double th = Math.Acos(rng.NextDouble() * 2 - 1), ph = rng.NextDouble() * 2 * Math.PI;
            var o = new Vector3((float)(4 * Math.Sin(th) * Math.Cos(ph)), (float)(4 * Math.Cos(th)), (float)(4 * Math.Sin(th) * Math.Sin(ph)));
            var target = new Vector3((float)(rng.NextDouble() * 2.6 - 1.3), (float)(rng.NextDouble() * 0.6 - 0.3), (float)(rng.NextDouble() * 2.6 - 1.3));
            var d = Vector3.Normalize(target - o);

            double F(double t)
            {
                double x = o.X + t * d.X, y = o.Y + t * d.Y, z = o.Z + t * d.Z;
                double s = x * x + y * y + z * z + R * R - r * r;
                return s * s - 4 * R * R * (x * x + z * z);
            }

            // Reference: first sign change on a fine march, refined by bisection.
            // A ray that only touches the surface (no sign change, |F| tiny at a
            // minimum) is ambiguous at any precision: skipped.
            double tRef = double.NaN, step = 2e-4, prev = F(0), minAbs = double.MaxValue;
            for (double t = step; t < 9; t += step)
            {
                double f = F(t);
                minAbs = Math.Min(minAbs, Math.Abs(f));
                if (Math.Sign(f) != Math.Sign(prev))
                {
                    double a = t - step, b = t;
                    for (int it = 0; it < 80; it++) { double m = 0.5 * (a + b); if (Math.Sign(F(m)) == Math.Sign(F(a))) a = m; else b = m; }
                    tRef = 0.5 * (a + b);
                    break;
                }
                prev = f;
            }
            if (double.IsNaN(tRef) && minAbs < 1e-6) continue;

            var rec = new HitRecord();
            bool hit = torus.Hit(new Ray(o, d), 1e-4f, 100f, ref rec);
            compared++;
            if (double.IsNaN(tRef)) { if (hit) falseHits++; continue; }
            if (!hit) { misses++; continue; }
            worstT = Math.Max(worstT, Math.Abs(rec.T - tRef));
        }
        _out.WriteLine($"torus: {compared} rays, {misses} misses, {falseHits} false hits, worst |t - t_ref| {worstT:E2}");
        Assert.Equal(0, falseHits);
        Assert.True(misses <= compared / 2000, $"{misses} missed hits out of {compared}");
        Assert.True(worstT < 1e-5, $"worst t error {worstT:E2}");
    }
}
