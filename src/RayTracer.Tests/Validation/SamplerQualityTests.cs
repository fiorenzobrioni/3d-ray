using RayTracer.Core.Sampling;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Measures the sample sequences the renderer draws (PIANO-REVIEW.md, Phase 1,
/// §3.10): for a pair of dimensions it integrates known 2D functions over many
/// independent pixels and fits how the RMS error falls with the sample count.
/// Independent random numbers fall as N^-0.5; a stratified 2D sequence falls
/// as N^-0.75 on a discontinuous integrand (a visibility edge) and faster on a
/// smooth one. The slope tells whether a pair of dimensions is jointly
/// stratified or only marginally.
/// </summary>
[Collection("SamplerExclusive")]
public class SamplerQualityTests
{
    private readonly ITestOutputHelper _out;
    public SamplerQualityTests(ITestOutputHelper output) => _out = output;

    private static readonly int[] Counts = { 16, 64, 256, 1024 };
    private const int Pixels = 512;

    // Smooth: a Gaussian bump. Discontinuous: a quarter-disc indicator (a
    // shadow edge in sample space).
    private static double Smooth(double u, double v) =>
        Math.Exp(-((u - 0.3) * (u - 0.3) + (v - 0.6) * (v - 0.6)) / 0.08);
    private static double Disc(double u, double v) => u * u + v * v < 0.6 ? 1.0 : 0.0;

    private static double Reference(Func<double, double, double> f)
    {
        const int n = 2000;
        double sum = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                sum += f((i + 0.5) / n, (j + 0.5) / n);
        return sum / ((double)n * n);
    }

    /// <summary>RMS error over <see cref="Pixels"/> pixels for each count, drawing
    /// the pair as the renderer would: <paramref name="skip"/> draws first, then
    /// the two coordinates through <paramref name="draw2D"/>.</summary>
    public static double[] Rmse(Func<double, double, double> f, double reference, int skip,
                                Func<(float u, float v)> draw2D)
    {
        var rmse = new double[Counts.Length];
        for (int c = 0; c < Counts.Length; c++)
        {
            int n = Counts[c];
            double se = 0;
            for (int p = 0; p < Pixels; p++)
            {
                uint pixelSeed = (uint)(p * 73856093) ^ 0x9e3779b9u;
                double sum = 0;
                for (int s = 0; s < n; s++)
                {
                    Sampler.BeginPixelSample(pixelSeed, (uint)s);
                    for (int k = 0; k < skip; k++) Sampler.Sample1D();
                    var (u, v) = draw2D();
                    sum += f(u, v);
                    Sampler.EndPixelSample();
                }
                double e = sum / n - reference;
                se += e * e;
            }
            rmse[c] = Math.Sqrt(se / Pixels);
        }
        return rmse;
    }

    public static double Slope(double[] rmse)
    {
        double mx = 0, my = 0;
        for (int i = 0; i < Counts.Length; i++) { mx += Math.Log(Counts[i]); my += Math.Log(rmse[i]); }
        mx /= Counts.Length; my /= Counts.Length;
        double num = 0, den = 0;
        for (int i = 0; i < Counts.Length; i++)
        {
            double dx = Math.Log(Counts[i]) - mx;
            num += dx * (Math.Log(rmse[i]) - my);
            den += dx * dx;
        }
        return num / den;
    }

    private static (float, float) TwoOneD() => (Sampler.Sample1D(), Sampler.Sample1D());

    private static T WithSobol<T>(Func<T> f)
    {
        var prev = Sampler.Kind;
        Sampler.SetKind(SamplerKind.Sobol);
        try { return f(); }
        finally { Sampler.SetKind(prev); }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]    // odd start: Sample2D must realign
    [InlineData(41)]
    public void Sample2D_IsJointlyStratified(int skip)
    {
        // A stratified 2D (0, 2)-sequence: about N^-1.4 on the smooth integrand
        // and N^-0.75 on the edge; independent numbers give N^-0.5.
        double rs = Reference(Smooth), rd = Reference(Disc);
        var (es, ed) = WithSobol(() => (Rmse(Smooth, rs, skip, Sampler.Sample2D), Rmse(Disc, rd, skip, Sampler.Sample2D)));
        Assert.True(Slope(es) < -1.2, $"smooth slope {Slope(es):0.00}");
        Assert.True(Slope(ed) < -0.68, $"edge slope {Slope(ed):0.00}");
    }

    [Fact]
    public void VertexBlocks_KeepADecisionOnTheSameDimensions()
    {
        // The vertex before consumes a different number of draws in every
        // sample (as lights, lobes and roulette do): the next vertex's 2D
        // decision must stay stratified.
        double rd = Reference(Disc);
        var ed = WithSobol(() => Rmse(Disc, rd, 0, () =>
        {
            Sampler.BeginVertex();
            int k = 1 + (int)(Sampler.Sample1D() * 7f);
            for (int i = 0; i < k; i++) Sampler.Sample1D();
            Sampler.BeginVertex();
            return Sampler.Sample2D();
        }));
        Assert.True(Slope(ed) < -0.68, $"edge slope {Slope(ed):0.00}");
    }

    [Fact]
    [Trait("Category", "Audit")]
    public void PrintReport()
    {
        if (Environment.GetEnvironmentVariable("RAYTRACER_AUDIT") != "1")
        {
            _out.WriteLine("Set RAYTRACER_AUDIT=1 to run the sampler audit.");
            return;
        }
        var prev = Sampler.Kind;
        try
        {
            Sampler.SetKind(SamplerKind.Sobol);
            double rs = Reference(Smooth), rd = Reference(Disc);
            foreach (var (label, skip) in new[] { ("dims 0-1", 0), ("dims 2-3", 2), ("dims 8-9", 8), ("dims 9-10", 9), ("dims 40-41", 40) })
            {
                var es = Rmse(Smooth, rs, skip, TwoOneD);
                var ed = Rmse(Disc, rd, skip, TwoOneD);
                _out.WriteLine($"{label,-11} 1D+1D  smooth slope {Slope(es):0.00} (rmse@1024 {es[^1]:0.0e0})   " +
                               $"edge slope {Slope(ed):0.00} (rmse@1024 {ed[^1]:0.0e0})");
            }
            {
                var rng = new Random(7);
                (float, float) Prng() => ((float)rng.NextDouble(), (float)rng.NextDouble());
                var es = Rmse(Smooth, rs, 0, Prng);
                var ed = Rmse(Disc, rd, 0, Prng);
                _out.WriteLine($"{"PRNG",-11} baseline smooth slope {Slope(es):0.00} (rmse@1024 {es[^1]:0.0e0})   " +
                               $"edge slope {Slope(ed):0.00} (rmse@1024 {ed[^1]:0.0e0})");
            }
            // Correlation between two dimensions of the same sample: should be ~0.
            foreach (var (a, b) in new[] { (0, 1), (2, 3), (8, 9), (1, 2), (8, 40) })
            {
                double sxy = 0, sx = 0, sy = 0, sxx = 0, syy = 0; int n = 0;
                for (int p = 0; p < 64; p++)
                    for (int s = 0; s < 1024; s++)
                    {
                        Sampler.BeginPixelSample((uint)(p * 2654435761u), (uint)s);
                        float x = 0, y = 0;
                        for (int d = 0; d <= Math.Max(a, b); d++)
                        {
                            float r = Sampler.Sample1D();
                            if (d == a) x = r;
                            if (d == b) y = r;
                        }
                        Sampler.EndPixelSample();
                        sxy += x * y; sx += x; sy += y; sxx += x * x; syy += y * y; n++;
                    }
                double cov = sxy / n - sx / n * sy / n;
                double corr = cov / Math.Sqrt((sxx / n - sx * sx / n / n) * (syy / n - sy * sy / n / n));
                _out.WriteLine($"correlation dims {a},{b}: {corr,8:F4}");
            }
            foreach (var (label, skip) in new[] { ("dims 2-3", 2), ("dims 9-10", 9), ("dims 40-41", 40) })
            {
                var es = Rmse(Smooth, rs, skip, Sampler.Sample2D);
                var ed = Rmse(Disc, rd, skip, Sampler.Sample2D);
                _out.WriteLine($"{label,-11} Sample2D smooth slope {Slope(es):0.00} (rmse@1024 {es[^1]:0.0e0})   " +
                               $"edge slope {Slope(ed):0.00} (rmse@1024 {ed[^1]:0.0e0})");
            }
        }
        finally
        {
            Sampler.SetKind(prev);
        }
    }
}
