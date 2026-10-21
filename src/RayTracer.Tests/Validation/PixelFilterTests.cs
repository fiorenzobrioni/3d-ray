using System.Numerics;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Pixel reconstruction filters (PIANO-REVIEW.md, Phase 3): the positions
/// drawn by filter importance sampling follow the filter's profile, and a
/// rendered sharp edge gives, column by column, the filter's step response.
/// </summary>
[Collection("SamplerExclusive")]
public class PixelFilterTests
{
    /// <summary>Fraction of the filter's mass at offsets below <paramref name="x"/>
    /// (from the pixel centre), by direct quadrature of the analytic profile.</summary>
    private static double MassBelow(PixelFilter f, double x)
    {
        const int n = 200_000;
        double r = f.Radius, h = 2 * r / n, below = 0, total = 0;
        for (int k = 0; k < n; k++)
        {
            double xi = -r + (k + 0.5) * h;
            double w = f.Evaluate(xi);
            total += w;
            if (xi < x) below += w;
        }
        return below / total;
    }

    [Theory]
    [InlineData(PixelFilterKind.Gaussian)]
    [InlineData(PixelFilterKind.BlackmanHarris)]
    public void SampledPositions_FollowTheProfile(PixelFilterKind kind)
    {
        var f = PixelFilter.Create(kind);
        const int bins = 40, n = 400_000;
        var counts = new double[bins];
        var rng = new Random(1);
        for (int i = 0; i < n; i++)
        {
            float p = f.SamplePosition((float)rng.NextDouble()) - 0.5f;   // offset from the centre
            Assert.InRange(p, -f.Radius, f.Radius);
            counts[Math.Clamp((int)((p + f.Radius) / (2 * f.Radius) * bins), 0, bins - 1)]++;
        }
        double chi2 = 0;
        int dof = 0;
        for (int b = 0; b < bins; b++)
        {
            double lo = -f.Radius + 2 * f.Radius * b / bins, hi = lo + 2 * f.Radius / bins;
            double e = n * (MassBelow(f, hi) - MassBelow(f, lo));
            if (e < 5) continue;
            chi2 += (counts[b] - e) * (counts[b] - e) / e;
            dof++;
        }
        double pValue = BsdfTestKit.RegularizedGammaQ((dof - 1) / 2.0, chi2 / 2.0);
        Assert.True(pValue > 1e-4, $"{kind}: chi2 {chi2:0.0} on {dof - 1} dof, p {pValue:E2}");
    }

    [Fact]
    public void Box_IsTheIdentityJitter()
    {
        for (float u = 0f; u < 1f; u += 0.0137f)
            Assert.Equal(u, PixelFilter.Box.SamplePosition(u));
    }

    [Theory]
    [InlineData(PixelFilterKind.Box)]
    [InlineData(PixelFilterKind.Gaussian)]
    [InlineData(PixelFilterKind.BlackmanHarris)]
    public void RenderedEdge_IsTheFilterStepResponse(PixelFilterKind kind)
    {
        // A white emitter covering the left half of the view, seen head-on:
        // the edge falls on the boundary between columns 15 and 16. Column i
        // is white where its sample lands left of x = 16, so its value is the
        // filter's mass below 16 - (i + 0.5).
        const int w = 32, h = 4;
        var filter = PixelFilter.Create(kind);
        var white = new Emissive(Vector3.One, 1f);
        var world = new HittableList(new IHittable[]
        {
            new Quad(new Vector3(-100, -10, 0), new Vector3(100, 0, 0), new Vector3(0, 20, 0), white),
        });
        var cam = RenderHarness.LookAt(new Vector3(0, 0, 10), Vector3.Zero, 20f, w, h);
        Sampler.SetKind(SamplerKind.Sobol);
        var img = RenderHarness.RenderLinear(world, new List<ILight>(), new SkySettings(Vector3.Zero),
                                             cam, w, h, 1024, 2, filter: filter);
        double worst = 0;
        for (int i = 0; i < w; i++)
        {
            double expected = MassBelow(filter, 16 - (i + 0.5));
            for (int y = 0; y < h; y++)
                worst = Math.Max(worst, Math.Abs(img[y, i].X - expected));
        }
        Assert.True(worst < 0.01, $"{kind}: step response off by {worst:0.0000}: " + string.Join(" ", Enumerable.Range(0, w).Select(i => img[1, i].X.ToString("0.00"))));
    }
}
