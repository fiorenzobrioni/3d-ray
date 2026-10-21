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

    /// <summary>
    /// Camera at the centre of a large clear glass sphere in a uniform
    /// environment of radiance 1. Radiance divided by n² is what is conserved
    /// across an interface, so inside a lossless closed dielectric the
    /// equilibrium radiance is n² in every direction (PIANO-REVIEW.md §3.35).
    /// From the centre every ray meets the surface head-on: transmitted
    /// (1 - R) plus the internal reflections, (1 - R) / (1 - R) = 1, times n².
    /// </summary>
    private static Vector3 InsideGlass(IMaterial glass, int spp)
    {
        Sampler.SetKind(SamplerKind.Sobol);
        var world = new HittableList(new IHittable[] { new Sphere(Vector3.Zero, 10f, glass) });
        var cam = RenderHarness.LookAt(Vector3.Zero, new Vector3(0, 0, -1), 30f, W, H);
        return RenderHarness.Mean(RenderHarness.RenderLinear(world, new List<ILight>(), new SkySettings(Vector3.One),
                                                             cam, W, H, spp, 64));
    }

    public static TheoryData<string> Glasses => new() { "dielectric", "disney-smooth" };

    private static IMaterial Glass(string name, float ior) => name switch
    {
        "dielectric" => new Dielectric(ior),
        _ => DisneyMaterial.Create(new RayTracer.Textures.SolidColor(Vector3.One), metallic: 0f, roughness: 0f,
                            specular: 0.5f, specularTint: 0f, sheen: 0f, sheenTint: 0f, clearcoat: 0f,
                            clearcoatGloss: 1f, specTrans: 1f, ior: ior),
    };

    [Theory]
    [MemberData(nameof(Glasses))]
    public void InsideClearGlass_RadianceIsScaledByEtaSquared(string glass)
    {
        const float n = 1.33f;
        var mean = InsideGlass(Glass(glass, n), spp: 64);
        Assert.InRange(mean.X, 0.99f * n * n, 1.01f * n * n);
    }

    [Theory]
    [InlineData("dielectric")]
    [InlineData("disney-smooth")]   // gained 3% before the translation onto OpenPBR (§3.17)
    public void Furnace_GlassSphereFromOutside_StaysLossless(string glass)
    {
        // The n² gained on entering is given back on leaving.
        var mean = Furnace(Glass(glass, 1.5f), spp: 256, depth: 64);
        Assert.InRange(mean.X, 0.98f, 1.02f);
    }

    [Fact]
    public void Furnace_NestedDielectrics_StayLossless()
    {
        // A glass core (n 1.5) inside a water sphere (n 1.33): every η² factor
        // gained on entering a medium must be given back on leaving it, so the
        // lossless pair reads 1. (The relative IOR at the inner interface does
        // not show here, energy is conserved either way: it is checked against
        // Mitsuba with explicit interfaces, PIANO-REVIEW.md §4 Fase 5.)
        Sampler.SetKind(SamplerKind.Sobol);
        var world = new HittableList(new IHittable[]
        {
            new Sphere(Vector3.Zero, 1f, new Dielectric(1.33f)),
            new Sphere(Vector3.Zero, 0.5f, new Dielectric(1.5f)),
        });
        var mean = RenderHarness.Mean(RenderHarness.RenderLinear(world, new List<ILight>(), new SkySettings(Vector3.One),
                                                                  FillCam(), W, H, 256, 64));
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
