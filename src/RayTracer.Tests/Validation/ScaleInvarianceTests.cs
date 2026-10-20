using System.Numerics;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Ray offsets must follow the scene (PIANO-REVIEW.md, Phase 1). A uniform
/// scale or a translation changes no radiance, so the image must not change.
/// With the former fixed 1e-4 offset a 10 µm scene rendered black and the
/// same scene 10 km from the origin lost 44% of its light to self-intersection.
/// </summary>
[Collection("SamplerExclusive")]
public class ScaleInvarianceTests
{
    private const int W = 24, H = 24;

    private static Vector3[,] Render(float scale, Vector3 offset)
    {
        Sampler.SetKind(SamplerKind.Sobol);
        Vector3 P(float x, float y, float z) => new Vector3(x, y, z) * scale + offset;
        Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * scale;
        var white = new Lambertian(new Vector3(0.7f));
        var panel = new Emissive(Vector3.One, 10f);
        // A closed-ish corner (floor, back wall, side wall) with a sphere and
        // an emissive panel facing down: contact shadows, interreflections.
        var floorQ = new Quad(P(-1, 0, 1), V(2, 0, 0), V(0, 0, -2), white);
        var back = new Quad(P(-1, 0, -1), V(2, 0, 0), V(0, 2, 0), white);
        var side = new Quad(P(-1, 0, 1), V(0, 0, -2), V(0, 2, 0), white);
        var ball = new Sphere(P(0.2f, 0.35f, -0.2f), 0.35f * scale, white);
        var light = new Quad(P(-0.3f, 1.8f, 0.3f), V(0, 0, -0.6f), V(0.6f, 0, 0), panel);
        var world = new HittableList(new IHittable[] { floorQ, back, side, ball, light });
        var lights = new List<ILight> { new GeometryLight(light, panel, shadowSamples: 1) };
        var cam = new Camera.Camera(P(0.4f, 1.0f, 3.2f), P(0, 0.7f, 0), Vector3.UnitY, 40f, 1f, 0f, 1f);
        return RenderHarness.RenderLinear(world, lights, new SkySettings(Vector3.Zero), cam, W, H, 32, 8);
    }

    [Theory]
    [InlineData(1e-5f, 0f)]
    [InlineData(1e3f, 0f)]
    [InlineData(1f, 1e4f)]
    public void Image_DoesNotDependOnSceneScaleOrPosition(float scale, float offset)
    {
        var reference = RenderHarness.Mean(Render(1f, Vector3.Zero));
        var scaled = RenderHarness.Mean(Render(scale, new Vector3(offset, offset * 0.5f, -offset)));
        float ratio = scaled.Y / reference.Y;
        Assert.InRange(ratio, 0.98f, 1.02f);
    }
}
