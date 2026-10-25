using System.Numerics;
using System.Runtime.InteropServices;
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
/// <c>--denoiser oidn</c>: the native library must load on every platform the
/// build ships it for (the CI matrix), and the learned filter must cut the
/// error of a low-spp render far below the noisy one and below NFOR, keeping
/// the image's energy.
/// </summary>
[Collection("SamplerExclusive")]
public class OidnDenoiserTests
{
    private const int Size = 64;

    /// <summary>The platforms whose OIDN binaries come with the build.</summary>
    private static bool Shipped =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && OperatingSystem.IsMacOS();

    private static bool Ready()
    {
        if (!Shipped) return false;
        Assert.True(Oidn.IsAvailable, $"OIDN failed to load: {Oidn.LoadError}");
        return true;
    }

    private static Renderer BuildRenderer(int spp)
    {
        var floorMat = new Lambertian(new CheckerTexture(2f,
            new Vector3(0.75f, 0.7f, 0.6f), new Vector3(0.2f, 0.25f, 0.3f)));
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, floorMat),
            new Sphere(new Vector3(-0.7f, 0.5f, 0f), 0.5f, new Lambertian(new Vector3(0.2f, 0.5f, 0.8f))),
            new Sphere(new Vector3(0.7f, 0.5f, 0f), 0.5f, new Metal(new Vector3(0.95f, 0.95f, 0.95f), fuzz: 0f)),
        });
        var light = new SphereLight(new Vector3(-1f, 3f, 2.5f), 0.4f,
            new Vector3(1f, 0.95f, 0.9f), 12f, shadowSamples: 1);
        var camera = new RayTracer.Camera.Camera(
            new Vector3(0f, 1.3f, 4f), new Vector3(0f, 0.5f, 0f), Vector3.UnitY,
            45f, 1f, 0f, 1f);
        return new Renderer(world, camera, new List<ILight> { light },
            new SkySettings(new Vector3(0.35f, 0.45f, 0.6f)), samplesPerPixel: spp, maxDepth: 6);
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
    public void Oidn_LoadsWhereShipped()
    {
        if (!Ready()) return;
        using var device = new Oidn.Device();
        Assert.NotEqual(IntPtr.Zero, device.Handle);
    }

    [Theory]
    [InlineData(DenoiseQuality.High)]
    [InlineData(DenoiseQuality.Fast)]
    public void Oidn_LowSppRender_BeatsNoisyAndNfor(DenoiseQuality quality)
    {
        if (!Ready()) return;
        Sampler.SetKind(SamplerKind.Sobol);

        var reference = BuildRenderer(1024).Render(Size, Size);
        var renderer = BuildRenderer(8);
        var noisy = renderer.Render(Size, Size, RenderCaptureOptions.Full);
        var opts = new DenoiserOptions { Kind = DenoiserKind.Oidn, Quality = quality };
        var oidn = Denoiser.Denoise(noisy.Buffers!, opts);
        var nfor = Denoiser.Denoise(noisy.Buffers!, new DenoiserOptions { Kind = DenoiserKind.Nfor, Quality = quality });

        double mseNoisy = Mse(noisy.Pixels, reference);
        double mseOidn = Mse(renderer.ToneMapToDisplay(oidn), reference);
        double mseNfor = Mse(renderer.ToneMapToDisplay(nfor), reference);
        // Measured: 0.47× (high), 0.55× (fast); the rest is the 8-spp
        // aliasing of the checker toward the horizon, which no denoiser can
        // undo at 64 × 64. NFOR measured 0.96× and 0.66×.
        Assert.True(mseOidn < 0.65 * mseNoisy, $"OIDN MSE {mseOidn:E3} not < 65% of noisy {mseNoisy:E3}");
        Assert.True(mseOidn < mseNfor, $"OIDN MSE {mseOidn:E3} not below NFOR {mseNfor:E3}");

        // Energy: the filtered image keeps the noisy mean (the learned filter
        // is not constrained to, so this guards against a scale error in the
        // binding, such as swapped channels or a wrong stride).
        double sumIn = 0, sumOut = 0;
        foreach (var v in noisy.Buffers!.Beauty.Data) sumIn += v;
        foreach (var v in oidn.Data) sumOut += v;
        Assert.Equal(1.0, sumOut / sumIn, 0.03);
    }

    [Fact]
    public void Oidn_KeepsChannelsAndOrientation()
    {
        if (!Ready()) return;
        // A noiseless image with a red left half and a blue right half, a
        // bright row on top: the filter must hand back the same picture.
        const int w = 40, h = 24;
        var b = new RenderBuffers(w, h, 16, captureAovs: true);
        int n = w * h;
        foreach (var (beauty, albedo, normal) in new[] { (b.Beauty, b.AlbedoA!, b.NormalA!),
                                                         (b.BeautyA, b.AlbedoB!, b.NormalB!),
                                                         (b.BeautyB, b.AlbedoA!, b.NormalA!) })
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var c = x < w / 2 ? new Vector3(0.8f, 0.1f, 0.05f) : new Vector3(0.05f, 0.1f, 0.8f);
                if (y < 4) c *= 3f;
                beauty.SetRgb(x, y, c);
                albedo.SetRgb(x, y, Vector3.Normalize(c) * 0.8f);
                normal.Data[n + i] = 1f;
            }
        }
        var output = Denoiser.Denoise(b, new DenoiserOptions { Kind = DenoiserKind.Oidn });
        for (int y = 6; y < h - 2; y++)
        {
            var left = output.GetRgb(4, y);
            var right = output.GetRgb(w - 5, y);
            Assert.True(left.X > 5 * left.Z, $"left pixel at row {y} is not red: {left}");
            Assert.True(right.Z > 5 * right.X, $"right pixel at row {y} is not blue: {right}");
        }
        Assert.True(output.GetRgb(4, 1).X > 2f * output.GetRgb(4, 12).X, "the bright top rows moved");
    }
}
