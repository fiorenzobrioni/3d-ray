using System.Numerics;
using RayTracer.Core.Sampling;
using RayTracer.Denoising;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Adaptive sampling (PIANO-REVIEW.md Phase 9): the convergence rule, the
/// halves it keeps, and a render that spends its samples where the noise is
/// while keeping the image's energy.
/// </summary>
[Collection("SamplerExclusive")]
public class AdaptiveSamplingTests
{
    private const int Size = 48;

    private static Renderer Build(int spp)
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
            new SkySettings(new Vector3(0.35f, 0.45f, 0.6f)), samplesPerPixel: spp, maxDepth: 6);
    }

    [Theory]
    [InlineData(0.01f, 0, 1024, 64)]    // 4/√0.01 = 40 → 64
    [InlineData(0.05f, 0, 1024, 32)]    // 17.9 → 32
    [InlineData(0.01f, 0, 16, 16)]      // never above the budget
    [InlineData(0.01f, 24, 1024, 24)]   // explicit minimum kept as is
    public void MinSamples_FromThreshold(float threshold, int min, int budget, int expected)
    {
        var s = new AdaptiveSettings { Threshold = threshold, MinSamples = min };
        Assert.Equal(expected, s.ResolveMinSamples(budget));
    }

    [Fact]
    public void Error_IsStandardErrorOverSquareRoot()
    {
        // Halves of 8 samples with means 1.1 and 0.9 on every channel: the
        // mean is 1, σ = 0.2·√64/16 = 0.1 per channel, error = 0.3/√3.
        var a = new Vector3(1.1f) * 8f;
        var b = new Vector3(0.9f) * 8f;
        float e = Renderer.AdaptiveError(a, 8, b, 8, exposure: 1f);
        Assert.Equal(0.3f / MathF.Sqrt(3f), e, 1e-4f);
        // Exposure k scales it by √k (noise on a display value).
        Assert.Equal(e * 2f, Renderer.AdaptiveError(a, 8, b, 8, exposure: 4f), 1e-4f);
        // One empty half: unknown, never converged.
        Assert.True(float.IsPositiveInfinity(Renderer.AdaptiveError(a, 8, Vector3.Zero, 0, 1f)));
    }

    [Fact]
    public void Convergence_ANoisyPixelKeepsItsNeighbours()
    {
        const int w = 7, h = 5;
        var active = Enumerable.Repeat(true, w * h).ToArray();
        var error = new float[w * h];
        error[2 * w + 3] = 1f;                  // one noisy pixel in the middle
        int remaining = Renderer.UpdateConvergence(active, error, w, h, threshold: 0.01f);
        Assert.Equal(9, remaining);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            Assert.Equal(Math.Abs(x - 3) <= 1 && Math.Abs(y - 2) <= 1, active[y * w + x]);

        // Stopped pixels count as converged: once the noisy one is below, all stop.
        error[2 * w + 3] = 0f;
        Assert.Equal(0, Renderer.UpdateConvergence(active, error, w, h, threshold: 0.01f));
    }

    [Fact]
    public void Render_SpendsSamplesOnNoiseAndKeepsEnergy()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        const int budget = 512;
        var reference = Build(4096).Render(Size, Size, new RenderCaptureOptions { CaptureBeautyHalves = true }).Buffers!.Beauty;

        var renderer = Build(budget);
        renderer.EnableAdaptive(new AdaptiveSettings { Threshold = 0.02f });
        var result = renderer.Render(Size, Size, RenderCaptureOptions.Full);
        var b = result.Buffers!;
        double mean = renderer.LastRenderMeanSamples;
        Assert.InRange(mean, 32, 0.8 * budget);

        // The flat sky converges at the first pass; the soft shadow under the
        // diffuse sphere takes more.
        Assert.Equal(32, b.SampleCount[2 * Size + 2]);
        Assert.True(b.SampleCount.Max() > 4 * 32, $"no pixel went past the first passes (max {b.SampleCount.Max()})");

        // Per-pixel halves: the first and second half of each pixel's samples.
        Assert.NotNull(b.HalfCountA);
        for (int i = 0; i < Size * Size; i++)
        {
            int n = b.SampleCount[i], nA = b.HalfCountA![i];
            Assert.True(nA == n / 2 || (n == budget && nA >= n / 2), $"pixel {i}: {nA} of {n} in half A");
        }
        var combined = b.CombineHalves(b.BeautyA, b.BeautyB);
        for (int i = 0; i < combined.Data.Length; i++)
            Assert.Equal(b.Beauty.Data[i], combined.Data[i], 1e-4f * (1f + b.Beauty.Data[i]));

        // Energy: unbiased up to the stopping rule's small selection effect.
        double sumAd = 0, sumRef = 0;
        for (int i = 0; i < b.Beauty.Data.Length; i++) { sumAd += b.Beauty.Data[i]; sumRef += reference.Data[i]; }
        Assert.Equal(1.0, sumAd / sumRef, 0.01);

        // The denoisers take the per-pixel counts as they are.
        foreach (var kind in new[] { DenoiserKind.Nfor, DenoiserKind.Oidn })
        {
            if (kind == DenoiserKind.Oidn && !Oidn.IsAvailable) continue;
            var den = Denoiser.Denoise(b, new DenoiserOptions { Kind = kind });
            double sumDen = 0;
            foreach (float v in den.Data) { Assert.True(float.IsFinite(v)); sumDen += v; }
            Assert.Equal(1.0, sumDen / sumRef, 0.02);
        }
    }

    [Fact]
    public void Render_ReferenceModeRefusesAdaptive()
    {
        var renderer = new Renderer(new HittableList(Array.Empty<IHittable>()),
            new RayTracer.Camera.Camera(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 45f, 1f, 0f, 1f),
            new List<ILight>(), new SkySettings(Vector3.One), samplesPerPixel: 4, maxDepth: 2, referenceMode: true);
        Assert.Throws<InvalidOperationException>(() => renderer.EnableAdaptive(new AdaptiveSettings()));
    }
}
