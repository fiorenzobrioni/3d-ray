using System.Numerics;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Scenes with a closed-form answer, rendered in reference mode
/// (PIANO-REVIEW.md, Phase 0, "scene analitiche"). A failure here means the
/// estimator converges to the wrong value, not that it is noisy.
/// </summary>
[Collection("SamplerExclusive")]
public class AnalyticSceneTests
{
    private const int W = 24, H = 24;

    // Sphere of radius 1 at the origin filling the whole frame: from z = 4 it
    // subtends asin(1/4) ≈ 14.5°, well beyond the 10° frustum's diagonal.
    private static Camera.Camera FillCam() => RenderHarness.LookAt(new Vector3(0, 0, 4), Vector3.Zero, 10f, W, H);

    private static Vector3 Furnace(IMaterial material, int spp, int depth)
    {
        Sampler.SetKind(SamplerKind.Sobol);
        var world = new HittableList(new IHittable[] { new Sphere(Vector3.Zero, 1f, material) });
        // Uniform environment of radiance 1: a convex object's pixels equal its
        // directional albedo, so they read 1 for a lossless BSDF.
        var sky = new SkySettings(Vector3.One);
        return RenderHarness.Mean(RenderHarness.RenderLinear(world, new List<ILight>(), sky, FillCam(), W, H, spp, depth));
    }

    [Fact]
    public void Furnace_LambertianSphere_EqualsAlbedo()
    {
        // Convex Lambertian: every bounce ray escapes, so L = ρ · L_env exactly.
        var mean = Furnace(new Lambertian(new Vector3(0.5f)), spp: 64, depth: 4);
        Assert.InRange(mean.X, 0.495f, 0.505f);
    }

    [Fact]
    public void Furnace_SmoothDielectricSphere_IsLossless()
    {
        // Clear glass neither absorbs nor emits: Fresnel reflection plus
        // refraction (including total internal reflection inside) sums to 1.
        var mean = Furnace(new Dielectric(1.5f), spp: 256, depth: 64);
        Assert.InRange(mean.X, 0.98f, 1.02f);
    }

    [Fact]
    public void Furnace_PerfectMirror_IsLossless()
    {
        var mean = Furnace(new Metal(Vector3.One, fuzz: 0f), spp: 16, depth: 4);
        Assert.InRange(mean.X, 0.999f, 1.001f);
    }

    [Fact]
    public void PointLightOverPlane_MatchesInverseSquareLaw()
    {
        // Lambertian plane y = 0 (albedo ρ), point light of intensity I at
        // height h straight above the look-at point, black sky. Outgoing
        // radiance at the foot of the light: L = ρ/π · I / h². The 1° frustum
        // keeps cosθ / r² within 0.01% of that value across the frame.
        Sampler.SetKind(SamplerKind.Sobol);
        const float rho = 0.6f, intensity = 5f, h = 2f;
        var world = new HittableList(new IHittable[] { new InfinitePlane(Vector3.Zero, Vector3.UnitY, new Lambertian(new Vector3(rho))) });
        var lights = new List<ILight> { new PointLight(new Vector3(0, h, 0), Vector3.One, intensity) };
        var cam = new Camera.Camera(new Vector3(0, 3, 0.001f), Vector3.Zero, Vector3.UnitY, 1f, 1f, 0f, 1f);

        var mean = RenderHarness.Mean(RenderHarness.RenderLinear(world, lights, new SkySettings(Vector3.Zero), cam, W, H, 4, 4));

        float expected = rho / MathF.PI * intensity / (h * h);
        Assert.InRange(mean.X, expected * 0.995f, expected * 1.005f);
    }
}
