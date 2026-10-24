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
/// Volumes (PIANO-REVIEW.md Phase 8): every medium's free flight is an
/// unbiased estimator of the transport along a segment, channel by channel
/// and whatever throughput steers it; transmittance estimates are unbiased;
/// majorants bound the density; shadow rays see every medium they cross; a
/// bounded medium of albedo 1 in a white furnace returns exactly the
/// furnace's radiance.
/// </summary>
[Collection("SamplerExclusive")]
public class MediumTransportTests
{
    private readonly ITestOutputHelper _out;
    public MediumTransportTests(ITestOutputHelper o)
    {
        _out = o;
        Sampler.SetKind(SamplerKind.Prng);
    }

    // ── Media under test, with their σ_s and σ_t along the test ray ─────────

    private static readonly Vector3 SigA = new(0.3f, 0.1f, 0.6f), SigS = new(0.5f, 0.9f, 0.2f);

    /// <summary>A grid whose density rises linearly along x from 0.1 to 1 (trilinear: exact).</summary>
    private static GridMedium RampGrid(GridInterpolation interp = GridInterpolation.Trilinear, int n = 9)
    {
        var data = new float[n * n * n];
        for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    data[(z * n + y) * n + x] = 0.1f + 0.9f * x / (n - 1);
        return new GridMedium(SigA, SigS, new Vector3(0f, -1f, -1f), new Vector3(4f, 1f, 1f), n, n, n, data,
                              new IsotropicPhase(), interp);
    }

    private static float RampDensity(float x) => x < 0f || x > 4f ? 0f : 0.1f + 0.9f * x / 4f;

    public static TheoryData<string> Media() => new() { "homogeneous", "height_fog", "grid", "atmosphere" };

    /// <summary>The medium, a ray, its length, and σ_s(t), σ_t(t) along it.</summary>
    private static (IMedium M, Ray R, float T, Func<float, Vector3> S, Func<float, Vector3> E) Case(string name)
    {
        switch (name)
        {
            case "homogeneous":
                return (new HomogeneousMedium(SigA, SigS, new IsotropicPhase()), new Ray(Vector3.Zero, Vector3.UnitX), 3f,
                        _ => SigS, _ => SigA + SigS);
            case "height_fog":
            {
                // Downward, from 1 above y0 to 1.3 below it (H = 0.8).
                var dir = Vector3.Normalize(new Vector3(1f, -0.6f, 0.2f));
                var ray = new Ray(new Vector3(0f, 1f, 0f), dir);
                Func<float, float> f = t => MathF.Exp(-(1f + t * dir.Y) / 0.8f);
                return (new HeightFogMedium(SigA * 0.4f, SigS * 0.4f, 0f, 0.8f, new IsotropicPhase()), ray, 3f,
                        t => SigS * 0.4f * f(t), t => (SigA + SigS) * 0.4f * f(t));
            }
            case "grid":
                return (RampGrid(), new Ray(new Vector3(-0.5f, 0.1f, -0.2f), Vector3.UnitX), 5f,
                        t => SigS * RampDensity(t - 0.5f), t => (SigA + SigS) * RampDensity(t - 0.5f));
            case "atmosphere":
            {
                // Dense air and dust (scaled up) seen along a slanted ray.
                var atm = new NishitaAtmosphereMedium(airDensity: 3e4f, dustDensity: 3e4f, worldScale: 1f);
                var dir = Vector3.Normalize(new Vector3(1f, 0.05f, 0f));
                var ray = new Ray(new Vector3(0f, 200f, 0f), dir);
                return (atm, ray, 6f, t => atm.SigmaS_AtY(200f + t * dir.Y), t => atm.SigmaT_AtY(200f + t * dir.Y));
            }
        }
        throw new ArgumentException(name);
    }

    /// <summary>
    /// ∫₀ᵀ Tr(t) σ_s(t) g(t) dt + Tr(T) L_b by quadrature, with g(t) = 1 + t
    /// standing in for the light a scattering event gathers.
    /// </summary>
    private static Vector3 Expected(Func<float, Vector3> sigS, Func<float, Vector3> sigT, float tMax, Vector3 background)
    {
        const int N = 20000;
        double dt = tMax / (double)N;
        var tau = new double[3];
        var sum = new double[3];
        for (int i = 0; i < N; i++)
        {
            float tm = (float)((i + 0.5) * dt);
            Vector3 e = sigT(tm), s = sigS(tm);
            for (int c = 0; c < 3; c++)
            {
                double ec = c == 0 ? e.X : c == 1 ? e.Y : e.Z, sc = c == 0 ? s.X : c == 1 ? s.Y : s.Z;
                // Midpoint transmittance of the cell.
                double tr = Math.Exp(-(tau[c] + 0.5 * ec * dt));
                sum[c] += tr * sc * (1 + tm) * dt;
                tau[c] += ec * dt;
            }
        }
        return new Vector3(
            (float)(sum[0] + Math.Exp(-tau[0]) * background.X),
            (float)(sum[1] + Math.Exp(-tau[1]) * background.Y),
            (float)(sum[2] + Math.Exp(-tau[2]) * background.Z));
    }

    [Theory]
    [MemberData(nameof(Media))]
    public void FreeFlight_IsUnbiased_PerChannel_WhateverTheThroughput(string name)
    {
        var (m, ray, tMax, sigS, sigT) = Case(name);
        var background = new Vector3(0.8f, 1.2f, 0.5f);
        Vector3 expected = Expected(sigS, sigT, tMax, background);
        foreach (var throughput in new[] { Vector3.One, new Vector3(1f, 0.2f, 0.02f), new Vector3(0f, 0f, 1f) })
        {
            MathUtils.ReseedThread(17);
            const int N = 400_000;
            Vector3 sum = Vector3.Zero, sum2 = Vector3.Zero;
            for (int i = 0; i < N; i++)
            {
                MediumEvent ev = m.Sample(ray, tMax, throughput);
                Vector3 x = ev.Scattered ? ev.Weight * (1f + ev.T) : ev.Weight * background;
                sum += x;
                sum2 += x * x;
            }
            Vector3 mean = sum / N, sd = Vector3.SquareRoot(Vector3.Max(sum2 / N - mean * mean, Vector3.Zero) / N);
            _out.WriteLine($"{name} h={throughput}: {mean} vs {expected} (±{sd})");
            for (int c = 0; c < 3; c++)
            {
                float hc = c == 0 ? throughput.X : c == 1 ? throughput.Y : throughput.Z;
                if (hc <= 0f) continue;   // a channel that carries nothing need not be estimated
                float mu = c == 0 ? mean.X : c == 1 ? mean.Y : mean.Z;
                float ex = c == 0 ? expected.X : c == 1 ? expected.Y : expected.Z;
                float s = c == 0 ? sd.X : c == 1 ? sd.Y : sd.Z;
                Assert.True(MathF.Abs(mu - ex) < 5f * s + 2e-3f * ex, $"{name} channel {c}: {mu} vs {ex} (σ {s})");
            }
        }
    }

    [Fact]
    public void RatioTracking_IsUnbiased_AndBounded()
    {
        var m = RampGrid();
        var ray = new Ray(new Vector3(-0.5f, 0.1f, -0.2f), Vector3.UnitX);
        // ∫ σ_t over the ramp: (0.1 + 1)/2 · 4 = 2.2 density-units.
        Vector3 expected = MediumMath.Exp(-(SigA + SigS) * 2.2f);
        MathUtils.ReseedThread(5);
        const int N = 200_000;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < N; i++)
        {
            Vector3 tr = m.Transmittance(ray, 10f);
            Assert.True(tr.X <= 20f && tr.Y <= 20f && tr.Z <= 20f, $"{tr}");
            sum += tr;
        }
        Vector3 mean = sum / N;
        _out.WriteLine($"ratio tracking {mean} vs {expected}");
        Assert.True((mean - expected).Length() < 0.01f * expected.Length() + 1e-3f, $"{mean} vs {expected}");
    }

    [Fact]
    public void TricubicGrid_NeverExceedsItsMajorants()
    {
        // Random data: the Catmull-Rom overshoots between voxels, and the
        // majorant grid must still bound it.
        var rng = new Random(2);
        const int n = 12;
        var data = new float[n * n * n];
        for (int i = 0; i < data.Length; i++) data[i] = rng.NextDouble() < 0.3 ? (float)rng.NextDouble() : 0f;
        var m = new GridMedium(Vector3.One, Vector3.One, -Vector3.One, Vector3.One, n, n, n, data,
                               new IsotropicPhase(), GridInterpolation.Tricubic);
        long before = TrackedMedium.Violations;
        MathUtils.ReseedThread(3);
        for (int i = 0; i < 20000; i++)
        {
            var o = new Vector3((float)rng.NextDouble() * 4 - 2, (float)rng.NextDouble() * 4 - 2, -3f);
            var d = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, 1f));
            m.Sample(new Ray(o, d), 10f, Vector3.One);
            m.Transmittance(new Ray(o, d), 10f);
        }
        Assert.Equal(before, TrackedMedium.Violations);
    }

    [Fact]
    public void HeightFog_FarBelowItsReference_StaysFinite()
    {
        // §3.59: the float form overflowed to ∞ · 0 a few dozen scale heights
        // below y0 (the lake of showcases/ground-alpine-lake.yaml).
        var fog = new HeightFogMedium(new Vector3(0.01f), new Vector3(0.02f), 50f, 0.5f, new IsotropicPhase());
        MathUtils.ReseedThread(1);
        foreach (var dir in new[] { Vector3.UnitX, Vector3.Normalize(new Vector3(1, -1, 0)), Vector3.Normalize(new Vector3(1, 1, 0)) })
        {
            var ray = new Ray(new Vector3(0f, 0f, 0f), dir);
            for (int i = 0; i < 2000; i++)
            {
                MediumEvent ev = fog.Sample(ray, 1e30f, new Vector3(1f, 0.5f, 0.2f));
                Assert.True(float.IsFinite(ev.Weight.X + ev.Weight.Y + ev.Weight.Z), $"{dir}: {ev}");
                Vector3 tr = fog.Transmittance(ray, 10f);
                Assert.True(float.IsFinite(tr.X + tr.Y + tr.Z));
            }
        }
    }

    [Fact]
    public void Atmosphere_AtSunset_HasBoundedWeights()
    {
        // §3.60: the old medium recoloured its events by exp(τ̄ − τ_c), which
        // grows without bound along a long, low ray (sunset): fireflies.
        var atm = new NishitaAtmosphereMedium(worldScale: 1000f);
        var ray = new Ray(new Vector3(0f, 0.05f, 0f), Vector3.Normalize(new Vector3(1f, 0.002f, 0f)));
        MathUtils.ReseedThread(8);
        float worst = 0f;
        for (int i = 0; i < 20000; i++)
        {
            MediumEvent ev = atm.Sample(ray, 400f, new Vector3(1f, 0.6f, 0.3f));
            worst = MathF.Max(worst, MediumMath.Max(ev.Weight));
        }
        // Spectral MIS bounds every weight by the albedo over the smallest
        // channel probability: here 1 / (0.3 / 1.9).
        _out.WriteLine($"largest weight: {worst}");
        Assert.True(worst < 1.9f / 0.3f, $"{worst}");
    }

    // ── Shadow rays through media ───────────────────────────────────────────

    /// <summary>An index-matched box of medium, bound like the loader does.</summary>
    private static IHittable FogBox(Vector3 min, Vector3 max, IMedium medium)
    {
        var box = new Transform(new Box(new Dielectric(1f)),
            Matrix4x4.CreateScale(max - min) * Matrix4x4.CreateTranslation((min + max) * 0.5f));
        return new MediumBoundHittable(box, new MediumInterface(medium, null));
    }

    [Fact]
    public void ShadowRay_IsAttenuatedByTheMediaItCrosses()
    {
        var fog = new HomogeneousMedium(new Vector3(0.4f, 0.2f, 0.1f), new Vector3(0.3f), new IsotropicPhase());
        var global = new HomogeneousMedium(new Vector3(0.05f), Vector3.Zero, new IsotropicPhase());
        var world = new HittableList(new IHittable[] { FogBox(new Vector3(1f, -1f, -1f), new Vector3(2.5f, 1f, 1f), fog) });
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);

        // Without a media context the walk sees index-matched glass only.
        Assert.Equal(Vector3.One, ShadowRay.Transmittance(world, ray, MathUtils.RayEpsilon, 4f));

        ShadowRay.BeginMedia(new MediumStack(), global);
        try
        {
            Vector3 tr = ShadowRay.Transmittance(world, ray, MathUtils.RayEpsilon, 4f);
            // 1.5 in the box (its own medium replaces the global one), 2.5 outside.
            Vector3 expected = MediumMath.Exp(-fog.SigmaTotal * 1.5f) * MathF.Exp(-0.05f * 2.5f);
            _out.WriteLine($"{tr} vs {expected}");
            Assert.True((tr - expected).Length() < 2e-3f, $"{tr} vs {expected}");
        }
        finally
        {
            ShadowRay.EndMedia();
        }

        // From inside the box (its medium on the stack), out through its wall.
        var inside = new MediumStack();
        inside.Push(fog, null);
        ShadowRay.BeginMedia(inside, global);
        try
        {
            Vector3 tr = ShadowRay.Transmittance(world, new Ray(new Vector3(2f, 0f, 0f), Vector3.UnitX), MathUtils.RayEpsilon, 2f);
            Vector3 expected = MediumMath.Exp(-fog.SigmaTotal * 0.5f) * MathF.Exp(-0.05f * 1.5f);
            Assert.True((tr - expected).Length() < 2e-3f, $"{tr} vs {expected}");
        }
        finally
        {
            ShadowRay.EndMedia();
        }
    }

    // ── White furnace ───────────────────────────────────────────────────────

    public static TheoryData<string> FurnaceMedia() => new() { "grid-tricubic", "procedural", "height_fog", "homogeneous-walk" };

    internal static IMedium FurnaceMedium(string name) => name switch
    {
        // Albedo 1 in every channel, chromatic extinction.
        "grid-tricubic" => new GridMedium(Vector3.Zero, new Vector3(2f, 1f, 0.5f), new Vector3(-1f), new Vector3(1f), 6, 6, 6,
                                          Enumerable.Range(0, 216).Select(i => (float)((i * 7919 % 101) / 100.0)).ToArray(),
                                          new HenyeyGreensteinPhase(0.5f), GridInterpolation.Tricubic),
        "procedural" => new HeterogeneousProceduralMedium(Vector3.Zero, new Vector3(1.5f, 0.8f, 3f), 2f, 3, 2f, 0.5f, 4,
                                                          new HenyeyGreensteinPhase(-0.3f)),
        "height_fog" => new HeightFogMedium(Vector3.Zero, new Vector3(1f, 2f, 0.5f), 0f, 0.7f, new RayleighPhase()),
        _ => new HomogeneousMedium(Vector3.Zero, new Vector3(1f, 2f, 0.6f), new HenyeyGreensteinPhase(0.4f)),
    };

    [Theory]
    [MemberData(nameof(FurnaceMedia))]
    public void WhiteFurnace_ScatteringOnlyMedium_ReturnsTheFurnace(string name)
    {
        // A sphere of index-matched glass filled with a medium that scatters
        // and never absorbs, in a uniform white sky: every path ends in the
        // sky, so every pixel must read exactly 1.
        Sampler.SetKind(SamplerKind.Sobol);
        var world = new HittableList(new IHittable[]
        {
            new MediumBoundHittable(new Sphere(Vector3.Zero, 1f, new Dielectric(1f)), new MediumInterface(FurnaceMedium(name), null)),
        });
        var cam = RenderHarness.LookAt(new Vector3(0f, 0f, 3.2f), Vector3.Zero, 40f, 12, 12);
        var img = RenderHarness.RenderLinear(world, new List<ILight>(), new SkySettings(Vector3.One), cam, 12, 12, 256, 256);
        Vector3 mean = RenderHarness.Mean(img);
        _out.WriteLine($"{name}: {mean}");
        foreach (float c in new[] { mean.X, mean.Y, mean.Z })
            Assert.InRange(c, 0.99f, 1.01f);
    }
    // ── Equiangular sampling ────────────────────────────────────────────────

    [Fact]
    public void Equiangular_SingleScattering_MatchesQuadrature_WithLessNoise()
    {
        // A point light 0.36 off the camera ray in a coloured fog, depth 1
        // (single scattering): the radiance along the ray has a 1D integral.
        Sampler.SetKind(SamplerKind.Sobol);
        var sigA = new Vector3(0.05f, 0.1f, 0.2f);
        var sigS = new Vector3(0.3f, 0.25f, 0.2f);
        var phase = new HenyeyGreensteinPhase(0.3f);
        var lightPos = new Vector3(0.3f, 0.2f, -2f);
        var world = new HittableList(new IHittable[] { new Sphere(new Vector3(0f, 0f, 50f), 0.1f, new Lambertian(new Vector3(0.5f))) });
        var lights = new List<ILight> { new PointLight(lightPos, Vector3.One, 1f) };
        const int W = 6, H = 6;
        var cam = RenderHarness.LookAt(Vector3.Zero, -Vector3.UnitZ, 0.05f, W, H);

        // Quadrature along -Z (the substitution t = s/(1−s) maps [0, ∞) to [0, 1)).
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

        (Vector3 Mean, float Spread) Run(bool eq)
        {
            var r = new Renderer(world, cam, lights, new SkySettings(Vector3.Zero), 64, 1,
                                 globalMedium: new HomogeneousMedium(sigA, sigS, phase), referenceMode: true) { Equiangular = eq };
            var buf = r.Render(W, H, new RenderCaptureOptions { CaptureBeautyHalves = true }).Buffers!.Beauty;
            Vector3 sum = Vector3.Zero; double sumY2 = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector3 v = buf.GetRgb(x, y);
                    sum += v;
                    sumY2 += v.Y * v.Y;
                }
            Vector3 mean = sum / (W * H);
            return (mean, (float)Math.Sqrt(Math.Max(0, sumY2 / (W * H) - mean.Y * mean.Y)));
        }

        var on = Run(true);
        var off = Run(false);
        _out.WriteLine($"expected {expected}; equiangular {on.Mean} (pixel spread {on.Spread:0.000e0}); " +
                       $"free flight only {off.Mean} (spread {off.Spread:0.000e0})");
        foreach (var m in new[] { on, off })
            Assert.True((m.Mean - expected).Length() < 0.03f * expected.Length(), $"{m.Mean} vs {expected}");
        Assert.True(on.Spread < 0.5f * off.Spread, $"{on.Spread} vs {off.Spread}");
    }
    // ── Subsurface parametrisation ──────────────────────────────────────────

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    public void Subsurface_ThickSlab_ShowsItsColour(float g)
    {
        // The OpenPBR parametrisation: subsurface_color is the albedo a thick
        // slab shows under uniform light (its bi-hemispherical albedo). A deep
        // index-matched slab of the medium under a white sky, seen from every
        // angle and averaged by cos θ, must read that colour (the fit of
        // Chiang et al. 2016; seen from above alone it reads darker, from the
        // horizon brighter).
        Sampler.SetKind(SamplerKind.Sobol);
        var color = new Vector3(0.2f, 0.5f, 0.85f);
        var medium = RayTracer.Scene.SceneLoader.SubsurfaceMedium("slab", color, new Vector3(0.05f), g);
        var slab = new Transform(new Box(new Dielectric(1f)),
            Matrix4x4.CreateScale(200f, 20f, 200f) * Matrix4x4.CreateTranslation(0f, -10f, 0f));
        var world = new HittableList(new IHittable[] { new MediumBoundHittable(slab, new MediumInterface(medium, null)) });
        Vector3 sum = Vector3.Zero;
        float wsum = 0f;
        for (int k = 0; k < 9; k++)
        {
            float th = (5f + 10f * k) * MathF.PI / 180f;
            var cam = new Camera.Camera(new Vector3(MathF.Sin(th), MathF.Cos(th), 0f), Vector3.Zero, Vector3.UnitY, 2f, 1f, 0f, 1f);
            Vector3 m = RenderHarness.Mean(RenderHarness.RenderLinear(world, new List<ILight>(), new SkySettings(Vector3.One),
                                                                      cam, 4, 4, 512, 16));
            float w = MathF.Cos(th) * MathF.Sin(th);
            sum += w * m;
            wsum += w;
        }
        Vector3 albedo = sum / wsum;
        _out.WriteLine($"g {g}: {albedo} for {color}");
        const float tol = 0.02f;
        Assert.InRange(albedo.X, color.X * (1 - tol), color.X * (1 + tol));
        Assert.InRange(albedo.Y, color.Y * (1 - tol), color.Y * (1 + tol));
        Assert.InRange(albedo.Z, color.Z * (1 - tol), color.Z * (1 + tol));
    }
}
