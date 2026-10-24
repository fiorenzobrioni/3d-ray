using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Volumetrics;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Every light type against its integral (<see cref="LightTransportTests"/>),
/// rendered by bidirectional path tracing: the camera hitting the light, the
/// light sample, and light tracing into the camera, weighted by MIS, must add
/// up to the same direct lighting.
/// </summary>
[Collection("SamplerExclusive")]
public class BdptLightTransportTests : LightTransportTests
{
    public BdptLightTransportTests(ITestOutputHelper output) : base(output) { }
    protected override IntegratorKind Integrator => IntegratorKind.Bdpt;
}

/// <summary>
/// The bidirectional integrators (PIANO-REVIEW.md Phase 8b) on light that
/// only some of their strategies can carry.
/// </summary>
[Collection("SamplerExclusive")]
public class BidirectionalTests
{
    private readonly ITestOutputHelper _out;
    public BidirectionalTests(ITestOutputHelper output) => _out = output;

    // A diffuse floor at y = 0 under a point light, and a perfect mirror
    // facing down at y = 2: the floor sees the light and its mirror image.
    private const float Intensity = 3f, MirrorY = 2f, Albedo = 0.8f;
    private static readonly Vector3 LightPos = new(0.3f, 1.4f, 0.2f);

    private static Vector3 FloorRadiance(Vector3 p)
    {
        // Direct, and from the light's image behind the mirror.
        Vector3 image = new(LightPos.X, 2f * MirrorY - LightPos.Y, LightPos.Z);
        float e = 0f;
        foreach (Vector3 l in new[] { LightPos, image })
        {
            Vector3 d = l - p;
            float d2 = d.LengthSquared();
            e += Intensity * MathF.Max(0f, d.Y / MathF.Sqrt(d2)) / d2;
        }
        return new Vector3(Albedo / MathF.PI * e);
    }

    private static (IHittable World, List<ILight> Lights) MirrorScene()
    {
        var floor = new Quad(new Vector3(-50, 0, 50), new Vector3(100, 0, 0), new Vector3(0, 0, -100),
                             new Lambertian(new Vector3(Albedo)));
        var mirror = new Quad(new Vector3(-50, MirrorY, -50), new Vector3(100, 0, 0), new Vector3(0, 0, 100),
                              new Metal(new Vector3(1f), 0f));
        return (new HittableList(new List<IHittable> { floor, mirror }),
                new List<ILight> { new PointLight(LightPos, Vector3.One, Intensity) });
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.15f)]
    public void MirroredPointLight_ReachesTheFloorByLightTracing(float aperture)
    {
        // Light → mirror → floor → camera: a pinhole camera can reach the
        // reflected light only by tracing it from the light into the camera
        // (path tracing sees half the light). Checks the camera's importance
        // (film position, density, lens) against its own rays.
        var (world, lights) = MirrorScene();
        const int w = 16, h = 12;
        var cam = new Camera.Camera(new Vector3(0f, 1f, 1.6f), new Vector3(0f, 0f, 0f), Vector3.UnitY, 50f,
                                    (float)w / h, aperture, focusDist: 1.9f);

        // The mirror is perfect: the reflected light is all of the image's.
        var (mirrorRec, mirrorV) = (new HitRecord(), Vector3.Normalize(new Vector3(0.1f, -1f, 0.2f)));
        Assert.True(world.Hit(new Ray(new Vector3(0, 1, 0), -mirrorV), 1e-4f, 1e4f, ref mirrorRec));
        var s = mirrorRec.Material!.Sample(mirrorV, in mirrorRec);
        Assert.True(s is { IsDelta: true } && MathF.Abs(s.Value.F.X - 1f) < 1e-4f, "the mirror reflects everything");

        // Expected: the film's mean over the camera's own rays (box filter).
        Sampler.SetKind(SamplerKind.Prng);
        MathUtils.ReseedThread(3);
        double sum = 0;
        const int n = 512;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                Ray r = cam.GetRay((i + 0.5f) / n, (j + 0.5f) / n);
                Vector3 d = Vector3.Normalize(r.Direction);
                float t = -r.Origin.Y / d.Y;
                sum += t > 0f ? FloorRadiance(r.Origin + t * d).X : 0.0;
            }
        double expected = sum / (n * n);

        Sampler.SetKind(SamplerKind.Sobol);
        double Mean(IntegratorKind kind, int spp)
        {
            var img = RenderHarness.RenderLinear(world, lights, new SkySettings(Vector3.Zero), cam, w, h, spp, depth: 2,
                                                 integrator: kind);
            return RenderHarness.Mean(img).X;
        }
        double bdpt = Mean(IntegratorKind.Bdpt, 2048);
        double path = Mean(IntegratorKind.Path, 256);
        _out.WriteLine($"aperture {aperture}: bdpt {bdpt:F5}, path {path:F5}, expected {expected:F5} (ratio {bdpt / expected:F4})");
        Assert.InRange(bdpt / expected, 0.99, 1.01);
        // Path tracing misses the mirrored light: a check that the test needs light tracing.
        Assert.True(path < 0.8 * expected);
    }

    // ── Participating media ─────────────────────────────────────────────────

    public static TheoryData<string> FurnaceMedia() => MediumTransportTests.FurnaceMedia();

    [Theory]
    [MemberData(nameof(FurnaceMedia))]
    public void WhiteFurnace_ScatteringOnlyMedium_ReturnsTheFurnace(string name)
    {
        // The furnace of MediumTransportTests with the sky as a light: light
        // subpaths start from it, scatter in the medium, and connect to the
        // camera's vertices in it. Every pixel must still read exactly 1.
        Sampler.SetKind(SamplerKind.Sobol);
        var world = new HittableList(new IHittable[]
        {
            new MediumBoundHittable(new Sphere(Vector3.Zero, 1f, new Dielectric(1f)),
                                    new MediumInterface(MediumTransportTests.FurnaceMedium(name), null)),
        });
        var sky = new SkySettings(Vector3.One);
        var cam = RenderHarness.LookAt(new Vector3(0f, 0f, 3.2f), Vector3.Zero, 40f, 12, 12);
        var img = RenderHarness.RenderLinear(world, new List<ILight> { new EnvironmentLight(sky) }, sky, cam, 12, 12, 256, 256,
                                             integrator: IntegratorKind.Bdpt);
        Vector3 mean = RenderHarness.Mean(img);
        _out.WriteLine($"{name}: {mean}");
        foreach (float c in new[] { mean.X, mean.Y, mean.Z })
            Assert.InRange(c, 0.99f, 1.01f);
    }

    [Fact]
    public void SingleScattering_InFog_MatchesQuadrature()
    {
        // A point light in a coloured fog, depth 1: light sampled from the
        // medium vertices and traced from the light into the camera.
        Sampler.SetKind(SamplerKind.Sobol);
        var sigA = new Vector3(0.05f, 0.1f, 0.2f);
        var sigS = new Vector3(0.3f, 0.25f, 0.2f);
        var phase = new HenyeyGreensteinPhase(0.3f);
        var lightPos = new Vector3(0.3f, 0.2f, -2f);
        var world = new HittableList(new IHittable[] { new Sphere(new Vector3(0f, 0f, 50f), 0.1f, new Lambertian(new Vector3(0.5f))) });
        var lights = new List<ILight> { new PointLight(lightPos, Vector3.One, 1f) };
        const int W = 6, H = 6;
        var cam = RenderHarness.LookAt(Vector3.Zero, -Vector3.UnitZ, 0.05f, W, H);

        var dir = -Vector3.UnitZ;
        var st = sigA + sigS;
        Vector3 expected = Vector3.Zero;
        const int N = 400_000;
        for (int i = 0; i < N; i++)
        {
            double sv = (i + 0.5) / N, t = sv / (1 - sv), jac = 1 / ((1 - sv) * (1 - sv));
            Vector3 p = dir * (float)t;
            Vector3 toL = lightPos - p;
            float d = toL.Length();
            Vector3 tr = MediumMath.Exp(-st * (float)t) * MediumMath.Exp(-st * d);
            expected += sigS * tr * (phase.Evaluate(dir, toL / d) / (d * d) * (float)(jac / N));
        }

        var r = new Renderer(world, cam, lights, new SkySettings(Vector3.Zero), 256, 1,
                             globalMedium: new HomogeneousMedium(sigA, sigS, phase), referenceMode: true)
        { Integrator = IntegratorKind.Bdpt };
        var buf = r.Render(W, H, new RenderCaptureOptions { CaptureBeautyHalves = true }).Buffers!.Beauty;
        Vector3 sum = Vector3.Zero;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                sum += buf.GetRgb(x, y);
        Vector3 mean = sum / (W * H);
        _out.WriteLine($"expected {expected}; bdpt {mean}");
        Assert.True((mean - expected).Length() < 0.03f * expected.Length(), $"{mean} vs {expected}");
    }

    // ── Vertex merging ──────────────────────────────────────────────────────

    [Fact]
    public void Vcm_SeesTheCausticThroughTheGlass_AsPathTracingDoes()
    {
        // The floor under a glass ball, seen through the ball, lit through
        // it by a small panel: light → glass → floor → glass → camera has
        // no two vertices a connection can join, so only merging (or the
        // camera ray hitting the panel) carries it. VCM is consistent: its
        // mean agrees with path tracing's (which can still reach a panel
        // this size), and with fewer samples.
        Sampler.SetKind(SamplerKind.Sobol);
        var floor = new Quad(new Vector3(-5, 0, 5), new Vector3(10, 0, 0), new Vector3(0, 0, -10), new Lambertian(new Vector3(0.7f)));
        var ball = new Sphere(new Vector3(0f, 0.6f, 0f), 0.5f, new Dielectric(1.5f));
        var em = new Emissive(Vector3.One, 40f);
        var panel = new Quad(new Vector3(-0.2f, 3f, -0.2f), new Vector3(0.4f, 0, 0), new Vector3(0, 0, 0.4f), em);   // facing down
        var world = new HittableList(new List<IHittable> { floor, ball, panel });
        var lights = new List<ILight> { new GeometryLight(panel, em) };
        const int w = 12, h = 12;
        var cam = RenderHarness.LookAt(new Vector3(0f, 1.4f, 1.6f), new Vector3(0f, 0.55f, 0f), 18f, w, h);
        var black = new SkySettings(Vector3.Zero);
        Vector3 vcm = RenderHarness.Mean(RenderHarness.RenderLinear(world, lights, black, cam, w, h, 256, 8,
                                                                    integrator: IntegratorKind.Vcm));
        Vector3 path = RenderHarness.Mean(RenderHarness.RenderLinear(world, lights, black, cam, w, h, 4096, 8));
        _out.WriteLine($"vcm {vcm}, path {path}, ratio {vcm.Y / path.Y:F4}");
        Assert.InRange(vcm.Y / path.Y, 0.97, 1.03);
    }
}
