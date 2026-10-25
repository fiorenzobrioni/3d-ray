using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Guards the <c>--reference</c> mode and <c>--seed</c> used by the engine
/// review (PIANO-REVIEW.md, Phase 0): reference mode must remove every
/// deliberate bias, a zero seed must leave the default render untouched, and a
/// non-zero seed must give an independent sample sequence.
/// </summary>
[Collection("SamplerExclusive")]
public class ReferenceModeTests
{
    private const int W = 16, H = 16;

    private static Camera.Camera Cam() => RenderHarness.LookAt(new Vector3(0, 0, 4), Vector3.Zero, 10f, W, H);

    [Fact]
    public void FireflyClamp_IsOptIn_AndReferenceModeOverridesIt()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        // An emitter of radiance 100 filling the frame. No clamp by default
        // (PIANO-REVIEW.md §3.20); an explicit clamp of 10 caps it; reference
        // mode ignores even an explicit clamp.
        var world = new HittableList(new IHittable[] { new Sphere(Vector3.Zero, 1f, new Emissive(Vector3.One, 100f)) });
        var sky = new SkySettings(Vector3.Zero);
        var none = new List<ILight>();

        var byDefault = RenderHarness.Mean(RenderHarness.RenderLinear(world, none, sky, Cam(), W, H, 4, 4, reference: false));
        var clamped = RenderHarness.Mean(RenderHarness.RenderLinear(world, none, sky, Cam(), W, H, 4, 4, reference: false, clamp: 10f));
        var reference = RenderHarness.Mean(RenderHarness.RenderLinear(world, none, sky, Cam(), W, H, 4, 4, reference: true, clamp: 10f));

        Assert.InRange(byDefault.X, 99.9f, 100.1f);
        Assert.InRange(clamped.X, 9.9f, 10.1f);
        Assert.InRange(reference.X, 99.9f, 100.1f);
    }

    [Fact]
    public void Reference_ShadowRaysAreOpaqueToGlass()
    {
        var world = new HittableList(new IHittable[] { new Sphere(Vector3.Zero, 1f, new Dielectric(1.5f)) });
        var ray = new Ray(new Vector3(0, 0, 5), -Vector3.UnitZ);

        bool previous = ShadowRay.BlockAllTransmission;
        try
        {
            ShadowRay.BlockAllTransmission = false;
            Vector3 straightThrough = ShadowRay.Transmittance(world, ray, MathUtils.Epsilon, 10f);
            ShadowRay.BlockAllTransmission = true;
            Vector3 blocked = ShadowRay.Transmittance(world, ray, MathUtils.Epsilon, 10f);

            Assert.True(straightThrough.X > 0.8f, $"default transparent shadow expected, got {straightThrough}");
            Assert.Equal(Vector3.Zero, blocked);
        }
        finally
        {
            ShadowRay.BlockAllTransmission = previous;
        }
    }

    [Fact]
    public void Seed_ZeroIsBitIdentical_NonZeroIsIndependent()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(new Vector3(0, -1, 0), Vector3.UnitY, new Lambertian(new Vector3(0.6f))),
            new Sphere(Vector3.Zero, 1f, new Lambertian(new Vector3(0.5f))),
        });
        var sky = new SkySettings(new Vector3(1f));
        var cam = RenderHarness.LookAt(new Vector3(0, 1, 5), Vector3.Zero, 40f, W, H);

        var plain = RenderHarness.RenderLinear(world, new List<ILight>(), sky, cam, W, H, 4, 4, reference: false);
        var seed0 = RenderHarness.RenderLinear(world, new List<ILight>(), sky, cam, W, H, 4, 4, reference: false, seed: 0);
        var seed1 = RenderHarness.RenderLinear(world, new List<ILight>(), sky, cam, W, H, 4, 4, reference: false, seed: 1);

        int differing = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Assert.Equal(plain[y, x], seed0[y, x]);
                if (plain[y, x] != seed1[y, x]) differing++;
            }
        Assert.True(differing > W * H / 2, $"seed 1 should change most pixels, changed {differing}");
    }
}
