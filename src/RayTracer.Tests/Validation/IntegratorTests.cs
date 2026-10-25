using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The path loop (PIANO-REVIEW.md Phase 7): Russian roulette that stays
/// unbiased over long paths, the depth limits counted as documented, and
/// cut-outs outside the total.
/// </summary>
[Collection("SamplerExclusive")]
public class IntegratorTests
{
    private readonly ITestOutputHelper _out;
    public IntegratorTests(ITestOutputHelper o)
    {
        _out = o;
        Sampler.SetKind(SamplerKind.Sobol);
    }

    private const float Albedo = 0.8f, Intensity = 3f, Radius = 2f;

    /// <summary>
    /// Inside a diffuse sphere of albedo a lit by a point light at its centre,
    /// every wall point gets the same direct irradiance E = I/R², and the
    /// interreflections are uniform: a path of at most d vertices sees
    /// L_d = a E / π · (1 − a^d) / (1 − a); infinitely many, a E / (π (1 − a)).
    /// </summary>
    private float InsideSphere(int depth, PathDepths? depths = null, int spp = 64)
    {
        var world = new HittableList(new IHittable[] { new Sphere(Vector3.Zero, Radius, new Lambertian(new Vector3(Albedo))) });
        var lights = new List<ILight> { new PointLight(Vector3.Zero, Vector3.One, Intensity) };
        var cam = RenderHarness.LookAt(new Vector3(0, 0, 0.5f), new Vector3(0, 0, -1), 30f, 16, 16);
        var img = RenderHarness.RenderLinear(world, lights, new SkySettings(Vector3.Zero), cam, 16, 16, spp, depth, depths: depths);
        return RenderHarness.Mean(img).Y;
    }

    private static float Expected(int vertices)
    {
        float e = Intensity / (Radius * Radius);
        return Albedo * e / MathF.PI * (1f - MathF.Pow(Albedo, vertices)) / (1f - Albedo);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(200)]
    public void InsideDiffuseSphere_MatchesTheSeriesAtEveryDepth(int depth)
    {
        float got = InsideSphere(depth, spp: depth > 10 ? 256 : 64);
        float want = Expected(depth);
        _out.WriteLine($"depth {depth}: {got:0.0000} vs {want:0.0000} (ratio {got / want:0.0000})");
        // 200 vertices: the roulette ends almost every path long before, and
        // must still converge to the infinite sum (truncated at 0.8^200).
        Assert.InRange(got / want, 0.985f, 1.015f);
    }

    [Fact]
    public void DiffuseLimit_StopsAfterThatManyDiffuseBounces()
    {
        // One diffuse bounce allowed: the camera's vertex and the one after it.
        float got = InsideSphere(50, new PathDepths(1, 50, 50, 50, 16));
        float want = Expected(2);
        _out.WriteLine($"diffuse 1: {got:0.0000} vs {want:0.0000}");
        Assert.InRange(got / want, 0.99f, 1.01f);
    }

    /// <summary>
    /// Ten fully cut-out planes between the camera and a white sky: the ray
    /// crosses them without spending the total budget (one vertex suffices),
    /// but not beyond the cut-out limit.
    /// </summary>
    [Theory]
    [InlineData(16, 1f)]
    [InlineData(5, 0f)]
    public void CutOuts_DoNotCountAgainstTheTotal(int transparentLimit, float expected)
    {
        var cutOut = new OpenPbr(new OpenPbrParameters { BaseColor = new SolidColor(new Vector3(0.5f)), GeometryOpacity = 0f });
        var planes = new List<IHittable>();
        for (int i = 0; i < 10; i++)
            planes.Add(new Quad(new Vector3(-5, -5, -1 - i), new Vector3(10, 0, 0), new Vector3(0, 10, 0), cutOut));
        var cam = RenderHarness.LookAt(new Vector3(0, 0, 1), new Vector3(0, 0, -1), 20f, 8, 8);
        var img = RenderHarness.RenderLinear(new HittableList(planes), new List<ILight>(), new SkySettings(Vector3.One),
                                             cam, 8, 8, 4, 1, depths: new PathDepths(1, 1, 1, 1, transparentLimit));
        Assert.Equal(expected, RenderHarness.Mean(img).Y, 1e-3f);
    }

    /// <summary>
    /// A camera ray that passes a cut-out is still the camera's: behind a fully
    /// cut-out plane, a black plane hidden from the camera stays hidden, and
    /// the white sky shows. As a transmission ray it would see the black plane.
    /// </summary>
    [Fact]
    public void CutOuts_KeepTheRayCategory()
    {
        var cutOut = new OpenPbr(new OpenPbrParameters { BaseColor = new SolidColor(new Vector3(0.5f)), GeometryOpacity = 0f });
        var black = new Lambertian(Vector3.Zero);
        var world = new HittableList(new List<IHittable>
        {
            new Quad(new Vector3(-5, -5, -1), new Vector3(10, 0, 0), new Vector3(0, 10, 0), cutOut),
            new VisibilityFilteredHittable(
                new Quad(new Vector3(-5, -5, -2), new Vector3(10, 0, 0), new Vector3(0, 10, 0), black),
                HitVisibilityMask.Camera),
        });
        var cam = RenderHarness.LookAt(new Vector3(0, 0, 1), new Vector3(0, 0, -1), 20f, 8, 8);
        var img = RenderHarness.RenderLinear(world, new List<ILight>(), new SkySettings(Vector3.One), cam, 8, 8, 4, 4);
        Assert.Equal(1f, RenderHarness.Mean(img).Y, 1e-3f);
    }
}
