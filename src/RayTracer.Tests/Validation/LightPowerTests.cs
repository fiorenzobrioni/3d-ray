using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// <c>ApproximatePower</c> of every finite light equals the flux it actually
/// emits (PIANO-REVIEW.md Phase 6): the power-weighted light picking divides
/// the samples by it. The flux is measured as the irradiance its own samples
/// deliver over a large sphere around it.
/// </summary>
public class LightPowerTests
{
    private static float MeasuredFlux(ILight light, Vector3 centre, float radius = 25f, int points = 40000)
    {
        var empty = new HittableList();
        var rng = new Random(3);
        double sum = 0;
        for (int i = 0; i < points; i++)
        {
            // Uniform point on the sphere, normal facing the centre.
            float z = 2f * rng.NextSingle() - 1f, phi = 2f * MathF.PI * rng.NextSingle();
            float r = MathF.Sqrt(1f - z * z);
            Vector3 n = new(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
            Vector3 p = centre + radius * n;
            for (int s = 0; s < light.ShadowSamples; s++)
            {
                var (shadow, c, dir, _) = light.IlluminateAndTestStratified(p, -n, empty, s);
                if (!shadow) sum += MathUtils.Luminance(c) * MathF.Max(0f, Vector3.Dot(-n, dir));
            }
        }
        return (float)(sum / points * 4 * Math.PI * radius * radius);
    }

    public static IEnumerable<object[]> Lights()
    {
        var bounds = new AABB(new Vector3(-30), new Vector3(30));
        yield return new object[] { "point", new PointLight(new Vector3(0.3f, 0.1f, 0f), new Vector3(1, 0.8f, 0.6f), 7f) };
        yield return new object[] { "spot", new SpotLight(Vector3.Zero, new Vector3(0.2f, -1, 0.1f), Vector3.One, 7f, 20f, 40f) };
        yield return new object[] { "area", new AreaLight(new Vector3(-1, 0, -0.5f), new Vector3(2, 0, 0), new Vector3(0, 0, 1), Vector3.One, 3f, 4) };
        yield return new object[] { "sphere r=0.2", new SphereLight(Vector3.Zero, 0.2f, Vector3.One, 3f, 4) };
        yield return new object[] { "sphere r=1.5", new SphereLight(Vector3.Zero, 1.5f, Vector3.One, 3f, 4) };
        var em = new Emissive(Vector3.One, 3f);
        yield return new object[] { "emissive quad", new GeometryLight(new Quad(new Vector3(-1, 0, -0.5f), new Vector3(2, 0, 0), new Vector3(0, 0, 1), em), em, 4) };
        yield return new object[] { "emissive sphere", new GeometryLight(new Sphere(Vector3.Zero, 0.7f, em), em, 4) };
    }

    [Theory, MemberData(nameof(Lights))]
    public void ApproximatePower_IsTheEmittedFlux(string name, ILight light)
    {
        float declared = light.ApproximatePower(new AABB(new Vector3(-30), new Vector3(30)));
        float measured = MeasuredFlux(light, Vector3.Zero);
        Assert.True(MathF.Abs(measured / declared - 1f) < 0.03f, $"{name}: declared {declared:F3}, measured {measured:F3}");
    }
}
