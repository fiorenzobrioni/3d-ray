using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Tests for Phase-2b caustics — Specular Manifold Sampling (SMS,
/// <see cref="ManifoldWalker.ConnectRough"/>). Two tiers, mirroring the
/// MNEE suite: analytic contract tests on the rough solver (the rough path
/// must reduce to the smooth MNEE solution as roughness → 0, and produce
/// physically admissible throughput/geometric terms), plus an end-to-end
/// render regression that a frosted-glass lens focuses a soft caustic.
/// All stochastic; PRNG-seeded for determinism.
/// </summary>
public class SmsCausticTests
{
    private static CausticCasterRegistry.Caster Sphere(Vector3 center, float radius)
    {
        // The Hittable material is irrelevant to ConnectRough (it uses the
        // surface for the manifold walk and the passed CausticInterface for the
        // physics); a Dielectric keeps the seed-ray intersection well-defined.
        var s = new Sphere(center, radius, new Dielectric(1.5f));
        return new CausticCasterRegistry.Caster(s, new AnalyticManifoldCaster(s, s), s.BoundingBox());
    }

    private static CausticInterface RoughGlass(float roughness, float ior = 1.5f)
    {
        float a = MathF.Max(roughness * roughness, 1e-3f);
        return new CausticInterface(isTransmissive: true, ior: ior, tint: Vector3.One,
                                    absorption: Vector3.Zero, alphaX: a, alphaY: a, roughness: roughness);
    }

    private static CausticInterface RoughMirror(float roughness)
    {
        float a = MathF.Max(roughness * roughness, 1e-3f);
        return new CausticInterface(isTransmissive: false, ior: 1.0001f, tint: new Vector3(0.95f),
                                    absorption: Vector3.Zero, alphaX: a, alphaY: a, roughness: roughness);
    }

    // Opens a deterministic Sobol sample stream for the analytic tests. The PRNG
    // fallback (MathUtils.Rng) is seeded from Environment.TickCount and is NOT
    // reproducible across runs/platforms, which makes per-trial SMS assertions
    // flaky; Owen-Sobol is pure integer hashing and identical everywhere.
    private static void BeginDeterministic()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        Sampler.BeginPixelSample(pixelSeed: 0x5A17C0DEu, sampleIndex: 0u);
    }

    [Fact]
    public void RoughGlass_NearSmoothLimit_ApproachesMneeSolution()
    {
        // With a barely-rough interface the sampled microfacet hugs the geometric
        // normal, so the SMS trials cluster on the smooth on-axis solid-glass
        // solution (vertices at ±R, caustic direction +Z). We assert on the
        // *average* over converged trials so a single wide GGX-tail sample can't
        // flip the test. Deterministic Sobol stream → reproducible across runs.
        BeginDeterministic();
        var caster = Sphere(Vector3.Zero, 1f);
        var ci = RoughGlass(roughness: 0.05f);

        Vector3 x = new(0f, 0f, -5f);
        Vector3 y = new(0f, 0f, 5f);
        Vector3 yN = new(0f, 0f, -1f);

        int converged = 0;
        Vector3 frontSum = Vector3.Zero, backSum = Vector3.Zero;
        float wiZSum = 0f;
        for (int t = 0; t < 64; t++)
        {
            if (!ManifoldWalker.ConnectRough(caster, ci, x, y, yN,
                                             ManifoldWalker.DefaultMaxIterations, out var conn))
                continue;
            converged++;
            frontSum += conn.FirstVertex;
            backSum  += conn.LastVertex;
            wiZSum   += conn.WiAtReceiver.Z;
            // Hard invariants that hold for any valid connection (seed-independent).
            Assert.InRange(conn.Throughput.X, 0f, 1.0001f);
            Assert.True(conn.G > 0f && !float.IsNaN(conn.G) && !float.IsInfinity(conn.G));
        }

        Assert.True(converged >= 24, $"too few SMS trials converged near the smooth limit ({converged}/64)");
        Vector3 frontMean = frontSum / converged, backMean = backSum / converged;
        Assert.True((frontMean - new Vector3(0f, 0f, -1f)).Length() < 0.1f,
                    $"mean front vertex {frontMean} not near the smooth solution (0,0,-1)");
        Assert.True((backMean - new Vector3(0f, 0f, 1f)).Length() < 0.1f,
                    $"mean back vertex {backMean} not near the smooth solution (0,0,1)");
        Assert.True(wiZSum / converged > 0.95f, $"mean caustic direction Z {wiZSum / converged:F3} not ~+1");
        Sampler.EndPixelSample();
    }

    [Fact]
    public void RoughGlass_OffAxis_ProducesAdmissibleConnections()
    {
        // Off-axis frosted lens: no closed form, but every converged trial must
        // carry a physical (finite, non-negative, ≤1) throughput and a positive
        // geometric term — the firefly guard the render regression relies on.
        BeginDeterministic();
        var caster = Sphere(Vector3.Zero, 1f);
        var ci = RoughGlass(roughness: 0.2f);

        Vector3 x = new(0.4f, -0.3f, -4f);
        Vector3 y = new(-0.2f, 0.1f, 4f);
        Vector3 yN = Vector3.Normalize(new Vector3(0f, 0f, -1f));

        int converged = 0;
        for (int t = 0; t < 64; t++)
        {
            if (!ManifoldWalker.ConnectRough(caster, ci, x, y, yN,
                                             ManifoldWalker.DefaultMaxIterations, out var conn))
                continue;
            converged++;
            Assert.InRange(conn.Throughput.X, 0f, 1.0001f);
            Assert.True(conn.Throughput.X >= 0f && !float.IsNaN(conn.Throughput.X));
            Assert.True(conn.G > 0f && !float.IsNaN(conn.G) && !float.IsInfinity(conn.G));
            Assert.True(MathF.Abs(conn.WiAtReceiver.LengthSquared() - 1f) < 1e-3f,
                        "caustic direction is not unit length");
        }

        Assert.True(converged >= 4, $"frosted off-axis lens produced too few connections ({converged}/64)");
        Sampler.EndPixelSample();
    }

    [Fact]
    public void RoughMirror_Reflection_Converges()
    {
        // A rough metallic mirror reflects x → caster → y on a single outward
        // interface; both endpoints sit on the reflective hemisphere.
        BeginDeterministic();
        var caster = Sphere(Vector3.Zero, 1f);
        var ci = RoughMirror(roughness: 0.15f);

        Vector3 x = new(-2f, 0f, 3f);
        Vector3 y = new(2f, 0f, 3f);
        Vector3 yN = Vector3.Normalize(new Vector3(0f, 0f, -1f));

        int converged = 0;
        for (int t = 0; t < 64; t++)
        {
            if (!ManifoldWalker.ConnectRough(caster, ci, x, y, yN,
                                             ManifoldWalker.DefaultMaxIterations, out var conn))
                continue;
            converged++;
            // Conductor Schlick (F0 = 0.95) ⇒ high reflectance.
            Assert.InRange(conn.Throughput.X, 0.5f, 1.0001f);
            Assert.True(conn.G > 0f && !float.IsNaN(conn.G));
        }

        Assert.True(converged >= 4, $"rough mirror produced too few reflective connections ({converged}/64)");
        Sampler.EndPixelSample();
    }

    // ── End-to-end render regression ────────────────────────────────────────

    private const int Width  = 200;
    private const int Height = 150;
    private const int Spp    = 48;
    private const int Depth  = 6;

    private const string SceneYaml = @"
camera:
  position: [0, 4.5, 6]
  look_at: [0, 0, 0]
  fov: 40
world:
  sky:
    type: ""flat""
    color: [0.0, 0.0, 0.0]
  ground:
    type: ""infinite_plane""
    material: ""floor""
    y: 0
    caustic_receiver: true
entities:
  - name: ""frosted_ball""
    type: ""sphere""
    center: [0, 1.4, 0]
    radius: 1.0
    material: ""frosted_glass""
    caustic_caster: true
lights:
  - type: area
    corner: [-0.4, 6.0, -0.4]
    u: [0.8, 0.0, 0.0]
    v: [0.0, 0.0, 0.8]
    color: [1.0, 1.0, 1.0]
    intensity: 90.0
    shadow_samples: 4
materials:
  - id: ""floor""
    type: ""lambertian""
    color: [0.7, 0.7, 0.7]
  - id: ""frosted_glass""
    type: ""disney""
    color: [1.0, 1.0, 1.0]
    spec_trans: 1.0
    roughness: 0.08
    ior: 1.5
";

    private static (float peak, float mean, int spikes) RenderStats(bool enableCaustics)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, SceneYaml);
        try
        {
            // Sobol is deterministic and platform-independent (the Renderer drives
            // BeginPixelSample per pixel); the PRNG fallback is time-seeded and
            // would make the peak-margin assertion flaky on CI.
            Sampler.SetKind(SamplerKind.Sobol);
            var (world, camera, lights, sky, globalMedium) =
                SceneLoader.Load(path, Width, Height, enableCaustics: enableCaustics);
            var casters = SceneLoader.LastCausticCasters;

            var renderer = new Renderer(
                world, camera, lights, sky, Spp, Depth, globalMedium,
                enableCaustics: enableCaustics, causticCasters: casters, smsSamples: 4);

            var px = renderer.Render(Width, Height);

            float peak = 0f, sum = 0f;
            int spikes = 0, count = 0;
            for (int y = (int)(Height * 0.42f); y < (int)(Height * 0.62f); y++)
            for (int x = (int)(Width * 0.40f); x < (int)(Width * 0.60f); x++)
            {
                float lum = MathUtils.Luminance(px[y, x]);
                peak = System.MathF.Max(peak, lum);
                sum += lum;
                count++;
                if (lum > 10f) spikes++;
            }
            return (peak, sum / count, spikes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FrostedGlassSphere_SoftCaustic_EmergesAndStaysBounded()
    {
        var off = RenderStats(enableCaustics: false);
        var on  = RenderStats(enableCaustics: true);

        // The frosted lens must brighten the floor under the sphere…
        Assert.True(on.peak > off.peak + 0.05f,
            $"Expected a soft frosted caustic: peak on={on.peak:F3} should exceed off={off.peak:F3}.");
        // …without introducing fireflies (the biased SMS estimator + per-trial
        // averaging must keep spikes out — the key correctness guard).
        Assert.True(on.spikes == 0, $"SMS introduced {on.spikes} firefly spikes under the lens.");
        Assert.True(!float.IsNaN(on.mean) && !float.IsInfinity(on.mean) && on.mean > 0f,
            $"caustic region mean radiance is not finite/positive ({on.mean}).");
    }
}
