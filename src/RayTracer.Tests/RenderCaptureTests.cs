using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Guards the project invariant that the capture overload of
/// <see cref="Renderer.Render(int,int,RenderCaptureOptions)"/> never perturbs
/// the rendered image: capture adds side accumulators and draws no extra
/// randomness, so the tone-mapped pixels must be bit-identical with capture
/// off, on, or via the legacy two-argument overload. Also validates the
/// first-non-delta-hit AOV semantics and the dual-buffer variance estimator.
/// </summary>
[Collection("SamplerExclusive")]
public class RenderCaptureTests
{
    private const int Width = 32;
    private const int Height = 32;
    private const int Spp = 8;

    /// <summary>Small mixed scene: diffuse floor, diffuse + mirror spheres,
    /// a sphere light, flat sky — exercises every AOV commit path.</summary>
    private static Renderer BuildRenderer()
    {
        var floorMat  = new Lambertian(new Vector3(0.7f, 0.3f, 0.2f));
        var sphereMat = new Lambertian(new Vector3(0.2f, 0.5f, 0.8f));
        var mirrorMat = new Metal(new Vector3(0.95f, 0.95f, 0.95f), fuzz: 0f);

        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, floorMat),
            new Sphere(new Vector3(-0.7f, 0.5f, 0f), 0.5f, sphereMat),
            new Sphere(new Vector3(0.7f, 0.5f, 0f), 0.5f, mirrorMat),
        });

        var light = new SphereLight(
            center: new Vector3(0f, 3f, 2f),
            radius: 0.3f,
            color: Vector3.One,
            intensity: 10f,
            shadowSamples: 1);

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1.2f, 4f),
            lookAt:      new Vector3(0f, 0.5f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     45f,
            aspectRatio: (float)Width / Height,
            aperture:    0f,
            focusDist:   1f);

        return new Renderer(
            world, camera,
            new List<ILight> { light },
            new SkySettings(new Vector3(0.4f, 0.5f, 0.7f)),
            samplesPerPixel: Spp,
            maxDepth: 6);
    }

    [Fact]
    public void Capture_OnOrOff_PixelsAreBitIdentical()
    {
        // Sobol draws are a pure function of (pixel seed, sample index,
        // dimension), so renders are deterministic across runs and threads.
        Sampler.SetKind(SamplerKind.Sobol);

        var legacy   = BuildRenderer().Render(Width, Height);
        var none     = BuildRenderer().Render(Width, Height, RenderCaptureOptions.None);
        var full     = BuildRenderer().Render(Width, Height, RenderCaptureOptions.Full);

        Assert.Null(none.Buffers);
        Assert.NotNull(full.Buffers);

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            Assert.Equal(legacy[y, x], none.Pixels[y, x]);
            Assert.Equal(legacy[y, x], full.Pixels[y, x]);
        }
    }

    [Fact]
    public void ToneMapToDisplay_OnCapturedBeauty_MatchesInLoopPixels()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        var renderer = BuildRenderer();
        var result = renderer.Render(Width, Height, RenderCaptureOptions.Full);

        var remapped = renderer.ToneMapToDisplay(result.Buffers!.Beauty);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            Assert.Equal(result.Pixels[y, x], remapped[y, x]);
    }

    [Fact]
    public void BeautyHalves_Recombine_ToFullBeautyMean()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        var result = BuildRenderer().Render(Width, Height, RenderCaptureOptions.Full);
        var buffers = result.Buffers!;

        var combined = buffers.CombineHalves(buffers.BeautyA, buffers.BeautyB);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            Vector3 full = buffers.Beauty.GetRgb(x, y);
            Vector3 halves = combined.GetRgb(x, y);
            // Different float summation order — equal up to rounding.
            Assert.True((full - halves).Length() < 1e-4f + 1e-4f * full.Length(),
                $"halves recombination diverged at ({x},{y}): {full} vs {halves}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AOV semantics (first-non-delta-hit rule)
    // ─────────────────────────────────────────────────────────────────────────

    private static (RenderBuffers Buffers, int W, int H) RenderDownwardScene(IMaterial planeMat, Vector3 skyColor)
    {
        // Camera at (0, 5, 0) looking straight down at a plane at y = 0: the
        // centre pixel's depth is ~5 and its geometric normal is +Y.
        const int W = 16, H = 16;
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, planeMat),
        });
        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 5f, 0f),
            lookAt:      Vector3.Zero,
            vUp:         Vector3.UnitZ,
            vFovDeg:     40f,
            aspectRatio: 1f,
            aperture:    0f,
            focusDist:   1f);

        Sampler.SetKind(SamplerKind.Sobol);
        var renderer = new Renderer(
            world, camera, new List<ILight>(),
            new SkySettings(skyColor),
            samplesPerPixel: 16, maxDepth: 6);
        var result = renderer.Render(W, H, RenderCaptureOptions.Full);
        return (result.Buffers!, W, H);
    }

    [Fact]
    public void Aov_DiffuseSurface_RecordsAlbedoNormalDepth()
    {
        var albedo = new Vector3(0.7f, 0.3f, 0.2f);
        var (buffers, w, h) = RenderDownwardScene(new Lambertian(albedo), new Vector3(0.5f, 0.5f, 0.5f));

        int cx = w / 2, cy = h / 2;
        Vector3 aovAlbedo = buffers.CombineHalves(buffers.AlbedoA!, buffers.AlbedoB!).GetRgb(cx, cy);
        Vector3 aovNormal = buffers.CombineHalves(buffers.NormalA!, buffers.NormalB!).GetRgb(cx, cy);
        float aovDepth = buffers.CombineDepthHalves().Get(0, cx, cy);

        Assert.True((aovAlbedo - albedo).Length() < 0.02f, $"albedo AOV {aovAlbedo} != {albedo}");
        Assert.True(Vector3.Dot(Vector3.Normalize(aovNormal), Vector3.UnitY) > 0.99f,
            $"normal AOV {aovNormal} not ≈ +Y");
        Assert.InRange(aovDepth, 4.9f, 5.2f);
    }

    [Fact]
    public void Aov_MirrorSurface_FollowsSpecularChain()
    {
        // Perfect mirror floor under a flat sky: the first non-delta event is
        // the environment, so the pixel's albedo guide is tint × sky colour,
        // the normal is zero (sky commit), and depth is the mirror distance.
        var tint = new Vector3(0.9f, 0.8f, 0.7f);
        var sky = new Vector3(0.6f, 0.5f, 0.4f);
        var (buffers, w, h) = RenderDownwardScene(new Metal(tint, fuzz: 0f), sky);

        int cx = w / 2, cy = h / 2;
        Vector3 aovAlbedo = buffers.CombineHalves(buffers.AlbedoA!, buffers.AlbedoB!).GetRgb(cx, cy);
        Vector3 aovNormal = buffers.CombineHalves(buffers.NormalA!, buffers.NormalB!).GetRgb(cx, cy);
        float aovDepth = buffers.CombineDepthHalves().Get(0, cx, cy);

        Vector3 expected = tint * sky;
        Assert.True((aovAlbedo - expected).Length() < 0.02f,
            $"mirror albedo AOV {aovAlbedo} != tint×sky {expected}");
        Assert.True(aovNormal.Length() < 1e-3f, $"mirror normal AOV {aovNormal} != 0");
        Assert.InRange(aovDepth, 4.9f, 5.2f);
    }

    [Fact]
    public void Aov_SkyPixel_CommitsSkyColorAndDepthSentinel()
    {
        // Camera looking straight up: every sample misses.
        const int W = 8, H = 8;
        var sky = new Vector3(0.3f, 0.6f, 0.9f);
        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1f, 0f),
            lookAt:      new Vector3(0f, 2f, 0f),
            vUp:         Vector3.UnitZ,
            vFovDeg:     40f,
            aspectRatio: 1f,
            aperture:    0f,
            focusDist:   1f);

        Sampler.SetKind(SamplerKind.Sobol);
        var renderer = new Renderer(
            new HittableList(new IHittable[] { }), camera, new List<ILight>(),
            new SkySettings(sky), samplesPerPixel: 4, maxDepth: 4);
        var buffers = renderer.Render(W, H, RenderCaptureOptions.Full).Buffers!;

        int cx = W / 2, cy = H / 2;
        Vector3 aovAlbedo = buffers.CombineHalves(buffers.AlbedoA!, buffers.AlbedoB!).GetRgb(cx, cy);
        Vector3 aovNormal = buffers.CombineHalves(buffers.NormalA!, buffers.NormalB!).GetRgb(cx, cy);
        float aovDepth = buffers.CombineDepthHalves().Get(0, cx, cy);

        Assert.True((aovAlbedo - sky).Length() < 0.01f, $"sky albedo AOV {aovAlbedo} != {sky}");
        Assert.True(aovNormal.Length() < 1e-4f);
        Assert.Equal(-1f, aovDepth);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dual-buffer variance estimator
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DualBufferVariance_OnSyntheticGaussianNoise_MatchesSigmaSquaredOverN()
    {
        // Fill the A/B halves with means of synthetic N(mu, sigma²) samples,
        // exactly as the render loop would, and check that the raw dual-buffer
        // estimate ((Ā−B̄)/2)² averages to Var[mean] = σ²/n across many pixels.
        const int W = 128, H = 128, N = 16;
        const float Mu = 1.5f, Sigma = 0.8f;
        int nA = (N + 1) / 2, nB = N / 2;

        var buffers = new RenderBuffers(W, H, N, captureAovs: false);
        var rng = new Random(1234);
        // Box-Muller Gaussian.
        float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return Mu + Sigma * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            Vector3 sumA = default, sumB = default;
            for (int s = 0; s < N; s++)
            {
                var v = new Vector3(Gaussian(), Gaussian(), Gaussian());
                if ((s & 1) == 0) sumA += v; else sumB += v;
            }
            buffers.BeautyA.SetRgb(x, y, sumA / nA);
            buffers.BeautyB.SetRgb(x, y, sumB / nB);
        }

        var variance = buffers.RawBeautyVariance();
        double sum = 0;
        foreach (float v in variance.Data) sum += v;
        double meanVar = sum / variance.Data.Length;

        double expected = Sigma * Sigma / N;
        Assert.InRange(meanVar, expected * 0.9, expected * 1.1);
    }
}
