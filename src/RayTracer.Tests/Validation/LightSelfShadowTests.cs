using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Regression for PIANO-REVIEW.md §3.19: a light whose emitter is real geometry
/// in the scene (area-light proxy, sphere-light proxy, emissive quad) must not
/// shadow itself. The receiver sits on the floor and sees a small light 71.6°
/// away from the light's normal, where the old shadow ray (shifted origin,
/// unshifted direction) crossed the emitter before its end and every sample
/// came back occluded. Each light's NEE estimate must match the small-source
/// closed form L · A · cosθ_light / d² (L · πR² / d² for the sphere).
/// </summary>
public class LightSelfShadowTests
{
    private const float Radiance = 100f;
    private static readonly Vector3 Receiver = new(3f, 0f, 0f);
    private static readonly Vector3 LightCentre = new(0f, 1f, 0f);

    private static Vector3 MeanContribution(ILight light, IHittable world, int samples = 4000)
    {
        MathUtils.ReseedThread(11);
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            var (inShadow, color, _, _) = light.IlluminateAndTest(Receiver, Vector3.UnitY, world);
            Assert.False(inShadow, "the light shadowed itself");
            sum += color;
        }
        return sum / samples;
    }

    private static float ExpectedRect(float side)
    {
        Vector3 toLight = LightCentre - Receiver;
        float d2 = toLight.LengthSquared();
        float cosLight = Vector3.Dot(-Vector3.Normalize(toLight), -Vector3.UnitY);
        return Radiance * side * side * cosLight / d2;
    }

    [Fact]
    public void AreaLight_SeenFarFromItsNormal_IsNotSelfShadowed()
    {
        const float side = 0.1f;
        var corner = LightCentre - new Vector3(side / 2, 0, side / 2);
        // u × v points down: the light faces the floor.
        var u = new Vector3(side, 0, 0);
        var v = new Vector3(0, 0, side);
        var proxy = new Emissive(Vector3.One, Radiance);
        var light = new AreaLight(corner, u, v, Vector3.One, Radiance, shadowSamples: 1, proxyMaterial: proxy);
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, new Lambertian(new Vector3(0.5f))),
            new BackFaceCulledHittable(new Quad(corner, u, v, proxy)),
        });

        Vector3 e = MeanContribution(light, world);
        Assert.InRange(e.X, ExpectedRect(side) * 0.98f, ExpectedRect(side) * 1.02f);
    }

    [Fact]
    public void EmissiveQuad_SeenFarFromItsNormal_IsNotSelfShadowed()
    {
        const float side = 0.1f;
        var corner = LightCentre - new Vector3(side / 2, 0, side / 2);
        var emissive = new Emissive(Vector3.One, Radiance);
        var quad = new Quad(corner, new Vector3(side, 0, 0), new Vector3(0, 0, side), emissive);
        var light = new GeometryLight(quad, emissive, shadowSamples: 1);
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, new Lambertian(new Vector3(0.5f))),
            quad,
        });

        Vector3 e = MeanContribution(light, world);
        Assert.InRange(e.X, ExpectedRect(side) * 0.98f, ExpectedRect(side) * 1.02f);
    }

    [Theory]
    [InlineData(0.05f)]
    // 0.01 at 3.16: Ω ≈ 3e-5 sr. The old 1 − cos θ_max underflowed and its
    // "pdf ≤ Epsilon → 0" guard left such an emitter with no NEE at all.
    [InlineData(0.01f)]
    public void EmissiveSphere_SmallAndDistant_GetsAllItsDirectLight(float radius)
    {
        var emissive = new Emissive(Vector3.One, Radiance);
        var sphere = new Sphere(LightCentre, radius, emissive);
        var light = new GeometryLight(sphere, emissive, shadowSamples: 1);
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, new Lambertian(new Vector3(0.5f))),
            sphere,
        });

        Vector3 e = MeanContribution(light, world);
        float expected = Radiance * MathF.PI * radius * radius / (LightCentre - Receiver).LengthSquared();
        Assert.InRange(e.X, expected * 0.98f, expected * 1.02f);
    }

    [Fact]
    public void SphereLight_SeenFromAfar_IsNotSelfShadowed()
    {
        const float radius = 0.05f;
        var proxy = new Emissive(Vector3.One, Radiance);
        var light = new SphereLight(LightCentre, radius, Vector3.One, Radiance, shadowSamples: 1, proxyMaterial: proxy);
        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, new Lambertian(new Vector3(0.5f))),
            new Sphere(LightCentre, radius, proxy),
        });

        Vector3 e = MeanContribution(light, world);
        float expected = Radiance * MathF.PI * radius * radius / (LightCentre - Receiver).LengthSquared();
        Assert.InRange(e.X, expected * 0.98f, expected * 1.02f);
    }
}
