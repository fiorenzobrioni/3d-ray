using System.Numerics;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The dual-buffer variance estimate ((Ā − B̄)/2)² against the true variance
/// of the pixel mean, measured over independent seeds (PIANO-REVIEW.md
/// Phase 9).
/// </summary>
[Collection("SamplerExclusive")]
public class DualBufferVarianceTests(ITestOutputHelper output)
{
    private const int Size = 32;
    private const int Seeds = 24;

    private static Renderer Build(int spp, int seed)
    {
        var floor = new Lambertian(new CheckerTexture(2f,
            new Vector3(0.75f, 0.7f, 0.6f), new Vector3(0.2f, 0.25f, 0.3f)));
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, floor),
            new Sphere(new Vector3(-0.7f, 0.5f, 0f), 0.5f, new Lambertian(new Vector3(0.2f, 0.5f, 0.8f))),
            new Sphere(new Vector3(0.7f, 0.5f, 0f), 0.5f, new Metal(new Vector3(0.9f), fuzz: 0.3f)),
        });
        var light = new SphereLight(new Vector3(-1f, 3f, 2.5f), 0.4f, new Vector3(1f), 12f, shadowSamples: 1);
        var camera = new RayTracer.Camera.Camera(new Vector3(0f, 1.3f, 4f), new Vector3(0f, 0.5f, 0f),
            Vector3.UnitY, 45f, 1f, 0f, 1f);
        return new Renderer(world, camera, new List<ILight> { light },
            new SkySettings(new Vector3(0.35f, 0.45f, 0.6f)), samplesPerPixel: spp, maxDepth: 6, seed: seed);
    }

    /// <summary>Σ E[estimate] / Σ Var[mean] over the image (green channel).</summary>
    private double Ratio(SamplerKind kind, int spp)
    {
        Sampler.SetKind(kind);
        int n = Size * Size;
        var sum = new double[n];
        var sumSq = new double[n];
        var est = new double[n];
        for (int s = 1; s <= Seeds; s++)
        {
            var b = Build(spp, s).Render(Size, Size, new RenderCaptureOptions { CaptureBeautyHalves = true }).Buffers!;
            var raw = b.RawBeautyVariance().Data;
            var mean = b.Beauty.Data;
            for (int i = 0; i < n; i++)
            {
                double v = mean[n + i];
                sum[i] += v; sumSq[i] += v * v; est[i] += raw[n + i];
            }
        }
        double trueVar = 0, estVar = 0;
        for (int i = 0; i < n; i++)
        {
            double m = sum[i] / Seeds;
            trueVar += (sumSq[i] / Seeds - m * m) * Seeds / (Seeds - 1);
            estVar += est[i] / Seeds;
        }
        double r = estVar / trueVar;
        output.WriteLine($"{kind} {spp} spp: estimate / true variance = {r:F3}");
        return r;
    }

    /// <summary>
    /// Sobol: the halves are the first and second half of the pixel's
    /// sequence, each a stratified net, so the estimate is close to the truth
    /// and slightly conservative (measured 1.08 to 1.14). With the even and
    /// odd samples as halves it was 2.4 to 3.0: the two halves of one
    /// Owen-scrambled sequence fill each other's gaps, so they disagree far
    /// more than the full mean varies.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    public void Sobol_EstimateIsCloseAndConservative(int spp)
    {
        Assert.InRange(Ratio(SamplerKind.Sobol, spp), 0.95, 1.35);
    }

    /// <summary>
    /// PRNG: the jittered grid stratifies the pixel, and the even and odd
    /// cells (the halves) fill each other's gaps too: conservative by 1.24 at
    /// 16 spp and 1.44 at 64.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void Prng_EstimateIsConservative(int spp)
    {
        Assert.InRange(Ratio(SamplerKind.Prng, spp), 0.9, 1.7);
    }
}
