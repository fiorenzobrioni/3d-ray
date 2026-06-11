using System.Numerics;
using RayTracer.Core.Sampling;
using RayTracer.Denoising;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// End-to-end denoiser regression: a small mixed scene (checker floor,
/// diffuse + mirror spheres, sphere light, flat sky) is rendered at low spp
/// and denoised; the display-space MSE against a converged high-spp reference
/// must drop well below the noisy baseline. NFOR must not lose to plain NLM.
///
/// How to update baselines: thresholds carry generous headroom over values
/// measured at implementation time (see DEVLOG); if filter parameters change
/// intentionally, re-measure and adjust.
/// </summary>
[Collection("SamplerExclusive")]
public class DenoiserRegressionTests
{
    private const int Size = 64;
    private const int NoisySpp = 8;
    private const int ReferenceSpp = 512;

    private static Renderer BuildRenderer(int spp)
    {
        var floorMat = new Lambertian(new CheckerTexture(2f,
            new Vector3(0.75f, 0.7f, 0.6f), new Vector3(0.2f, 0.25f, 0.3f)));
        var diffuseMat = new Lambertian(new Vector3(0.2f, 0.5f, 0.8f));
        var mirrorMat = new Metal(new Vector3(0.95f, 0.95f, 0.95f), fuzz: 0f);

        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, floorMat),
            new Sphere(new Vector3(-0.7f, 0.5f, 0f), 0.5f, diffuseMat),
            new Sphere(new Vector3(0.7f, 0.5f, 0f), 0.5f, mirrorMat),
        });

        var light = new SphereLight(
            center: new Vector3(-1f, 3f, 2.5f),
            radius: 0.4f,
            color: new Vector3(1f, 0.95f, 0.9f),
            intensity: 12f,
            shadowSamples: 1);

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1.3f, 4f),
            lookAt:      new Vector3(0f, 0.5f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     45f,
            aspectRatio: 1f,
            aperture:    0f,
            focusDist:   1f);

        return new Renderer(
            world, camera,
            new List<ILight> { light },
            new SkySettings(new Vector3(0.35f, 0.45f, 0.6f)),
            samplesPerPixel: spp,
            maxDepth: 6);
    }

    private static double Mse(Vector3[,] a, Vector3[,] b)
    {
        double sum = 0;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            sum += (a[y, x] - b[y, x]).LengthSquared();
        return sum / (Size * Size * 3);
    }

    [Fact]
    public void Denoise_LowSppRender_CutsMseAgainstConvergedReference()
    {
        // PRNG: independent A/B halves, the dual-buffer estimator's design
        // assumption (with Sobol the halves anti-correlate and the gains are
        // deliberately damped by the selection margin — see NforRegression).
        // Thresholds carry headroom over measured values (nfor ≈ 0.46×,
        // nlm ≈ 0.57×, fast ≈ 0.55× at implementation time) to tolerate the
        // PRNG run-to-run variation.
        Sampler.SetKind(SamplerKind.Prng);

        var reference = BuildRenderer(ReferenceSpp).Render(Size, Size);

        var renderer = BuildRenderer(NoisySpp);
        var noisy = renderer.Render(Size, Size, RenderCaptureOptions.Full);

        double mseNoisy = Mse(noisy.Pixels, reference);

        var nlm = NforDenoiser.Denoise(noisy.Buffers!,
            new DenoiserOptions { Kind = DenoiserKind.Nlm, Quality = DenoiseQuality.High });
        double mseNlm = Mse(renderer.ToneMapToDisplay(nlm), reference);

        var nfor = NforDenoiser.Denoise(noisy.Buffers!,
            new DenoiserOptions { Kind = DenoiserKind.Nfor, Quality = DenoiseQuality.High });
        double mseNfor = Mse(renderer.ToneMapToDisplay(nfor), reference);

        Assert.True(mseNlm < 0.80 * mseNoisy,
            $"NLM MSE {mseNlm:E3} not < 80% of noisy MSE {mseNoisy:E3}");
        Assert.True(mseNfor < 0.70 * mseNoisy,
            $"NFOR MSE {mseNfor:E3} not < 70% of noisy MSE {mseNoisy:E3}");

        // The regression backend must not lose to the plain weighted average
        // (tolerance for stochastic ties on this tiny scene).
        Assert.True(mseNfor <= mseNlm * 1.15,
            $"NFOR MSE {mseNfor:E3} worse than NLM {mseNlm:E3}");

        // Absolute quality gate, calibrated with headroom (display space,
        // values in [0,1]; measured ≈ 1.9e-4).
        Assert.True(mseNfor < 6e-4, $"NFOR MSE {mseNfor:E3} above absolute gate 6e-4");
    }

    [Fact]
    public void Denoise_FastQuality_StillReducesError()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var reference = BuildRenderer(ReferenceSpp).Render(Size, Size);
        var renderer = BuildRenderer(NoisySpp);
        var noisy = renderer.Render(Size, Size, RenderCaptureOptions.Full);

        var fast = NforDenoiser.Denoise(noisy.Buffers!,
            new DenoiserOptions { Kind = DenoiserKind.Nfor, Quality = DenoiseQuality.Fast });

        double mseNoisy = Mse(noisy.Pixels, reference);
        double mseFast = Mse(renderer.ToneMapToDisplay(fast), reference);
        Assert.True(mseFast < 0.8 * mseNoisy,
            $"fast NFOR MSE {mseFast:E3} not < 80% of noisy MSE {mseNoisy:E3}");
    }

    [Fact]
    public void Denoise_NearConvergedSobolRender_NeverRegresses()
    {
        // The Sobol selection margin must keep the denoiser from making an
        // already-clean low-discrepancy render worse (small tolerance for
        // per-pixel selection noise).
        Sampler.SetKind(SamplerKind.Sobol);

        var reference = BuildRenderer(ReferenceSpp).Render(Size, Size);
        var renderer = BuildRenderer(32);
        var noisy = renderer.Render(Size, Size, RenderCaptureOptions.Full);

        var nfor = NforDenoiser.Denoise(noisy.Buffers!,
            new DenoiserOptions { Kind = DenoiserKind.Nfor, Quality = DenoiseQuality.High });

        double mseNoisy = Mse(noisy.Pixels, reference);
        double mseNfor = Mse(renderer.ToneMapToDisplay(nfor), reference);
        Assert.True(mseNfor < 1.05 * mseNoisy,
            $"NFOR on near-converged Sobol regressed: {mseNfor:E3} vs noisy {mseNoisy:E3}");
    }

    [Fact]
    public void Denoise_WithoutAovCapture_Throws()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        var result = BuildRenderer(2).Render(8, 8,
            new RenderCaptureOptions { CaptureBeautyHalves = true, CaptureAovs = false });
        Assert.Throws<InvalidOperationException>(() =>
            NforDenoiser.Denoise(result.Buffers!, new DenoiserOptions()));
    }
}
