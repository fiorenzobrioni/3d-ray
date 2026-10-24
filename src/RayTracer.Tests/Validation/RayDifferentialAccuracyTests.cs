using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The texture footprint (PIANO-REVIEW.md, Phase 1): UV partials from the
/// tangent-plane projection of the camera ray differentials (FootprintMath)
/// against the finite difference obtained by actually intersecting the
/// auxiliary rays with the surface. Exact on a plane; on a sphere the tangent
/// plane is a first-order approximation, so the error must vanish with the
/// pixel size.
/// </summary>
public class RayDifferentialAccuracyTests
{
    private readonly ITestOutputHelper _out;
    public RayDifferentialAccuracyTests(ITestOutputHelper output) => _out = output;

    /// <summary>95th percentile of the relative footprint error, away from
    /// grazing silhouettes (|cos| &lt; 0.2) and from the sphere's poles, where the
    /// tangent plane and the u parameterisation are singular by nature.</summary>
    private static double WorstRelativeError(IHittable surface, Camera.Camera cam, float pixel, bool skipPoles = false, int samples = 4000)
    {
        var rng = new Random(6);
        var errors = new List<double>();
        for (int i = 0; i < samples; i++)
        {
            float s = 0.2f + 0.6f * (float)rng.NextDouble(), t = 0.2f + 0.6f * (float)rng.NextDouble();
            var ray = cam.GetRayWithDifferentials(s, t, pixel, pixel);
            var rec = new HitRecord();
            if (!surface.Hit(ray, 1e-6f, 1e9f, ref rec)) continue;
            var fp = FootprintMath.Compute(ray, rec.Point, rec.Normal, rec.DpDu, rec.DpDv);
            if (!fp.HasFootprint) continue;
            if (MathF.Abs(Vector3.Dot(Vector3.Normalize(ray.Direction), rec.Normal)) < 0.2f) continue;
            if (skipPoles && MathF.Abs(rec.Normal.Y) > 0.95f) continue;

            var d = ray.Differentials;
            var rx = new HitRecord(); var ry = new HitRecord();
            if (!surface.Hit(new Ray(d.OriginX, d.DirectionX), 1e-6f, 1e9f, ref rx)) continue;
            if (!surface.Hit(new Ray(d.OriginY, d.DirectionY), 1e-6f, 1e9f, ref ry)) continue;
            // Skip the u seam of the sphere.
            if (MathF.Abs(rx.U - rec.U) > 0.5f || MathF.Abs(ry.U - rec.U) > 0.5f) continue;

            double scale = Math.Max(Math.Abs(rx.U - rec.U) + Math.Abs(rx.V - rec.V)
                                    + Math.Abs(ry.U - rec.U) + Math.Abs(ry.V - rec.V), 1e-9);
            double err = Math.Abs(fp.DUdx - (rx.U - rec.U)) + Math.Abs(fp.DVdx - (rx.V - rec.V))
                       + Math.Abs(fp.DUdy - (ry.U - rec.U)) + Math.Abs(fp.DVdy - (ry.V - rec.V));
            errors.Add(err / scale);
        }
        errors.Sort();
        return errors[(int)(errors.Count * 0.95)];
    }

    private static Camera.Camera Cam() =>
        new(new Vector3(0.3f, 2.2f, 3.5f), new Vector3(0, 0.3f, 0), Vector3.UnitY, 40f, 1f, 0f, 1f);

    [Fact]
    public void Plane_FootprintIsExact()
    {
        var quad = new Quad(new Vector3(-3, 0, 3), new Vector3(6, 0, 0), new Vector3(0, 0, -6), new Lambertian(new Vector3(0.5f)));
        double e = WorstRelativeError(quad, Cam(), 1f / 512f);
        _out.WriteLine($"plane: p95 relative footprint error {e:E2}");
        Assert.True(e < 1e-2, $"plane footprint error {e:E2}");
    }

    [Fact]
    public void Sphere_FootprintConvergesWithPixelSize()
    {
        var sphere = new Sphere(new Vector3(0, 0.3f, 0), 1f, new Lambertian(new Vector3(0.5f)));
        double coarse = WorstRelativeError(sphere, Cam(), 1f / 64f, skipPoles: true);
        double fine = WorstRelativeError(sphere, Cam(), 1f / 512f, skipPoles: true);
        _out.WriteLine($"sphere: p95 relative footprint error {coarse:E2} at 1/64, {fine:E2} at 1/512");
        Assert.True(fine < 0.05, $"sphere footprint error {fine:E2}");
        // First order: an 8× smaller pixel must give a several times smaller error.
        Assert.True(fine < coarse / 4, "the tangent-plane error must shrink with the pixel");
    }
}
