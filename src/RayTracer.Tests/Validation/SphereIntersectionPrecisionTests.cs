using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Single-precision accuracy of <see cref="Sphere.Hit"/> for small, distant
/// spheres (PIANO-REVIEW.md §3.19): the textbook b² − ac discriminant cancels
/// catastrophically there, and the error was large enough for a small sphere
/// light to shadow itself. The hit distance must stay within a few float ulps
/// of the double-precision answer.
/// </summary>
public class SphereIntersectionPrecisionTests
{
    [Theory]
    [InlineData(0.01f, 3f)]
    [InlineData(0.01f, 30f)]
    [InlineData(0.5f, 200f)]
    public void Hit_SmallDistantSphere_MatchesDoublePrecision(float radius, float distance)
    {
        var centre = new Vector3(0.3f, 1.1f, -0.7f);
        var sphere = new Sphere(centre, radius, new Lambertian(new Vector3(0.5f)));
        var rng = new Random(5);
        double worst = 0;
        for (int i = 0; i < 2000; i++)
        {
            // Origin far away, aimed at a random point of the visible disc.
            var origin = centre + new Vector3(distance, 0.2f * distance, 0.1f * distance);
            double ox = origin.X, oy = origin.Y, oz = origin.Z;
            var aim = centre + radius * 0.999f * new Vector3(0f,
                (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * 0.7f;
            var dir = Vector3.Normalize(aim - origin);

            var rec = new HitRecord();
            if (!sphere.Hit(new Ray(origin, dir), MathUtils.Epsilon, float.MaxValue, ref rec))
                continue;

            // Double-precision reference on the same (float) ray.
            double fx = ox - centre.X, fy = oy - centre.Y, fz = oz - centre.Z;
            double dx = dir.X, dy = dir.Y, dz = dir.Z;
            double a = dx * dx + dy * dy + dz * dz;
            double hb = fx * dx + fy * dy + fz * dz;
            double c = fx * fx + fy * fy + fz * fz - (double)radius * radius;
            double t = (-hb - Math.Sqrt(hb * hb - a * c)) / a;

            worst = Math.Max(worst, Math.Abs(rec.T - t));
        }
        // A few ulps of the hit distance, far below the 1e-4 ray epsilon.
        Assert.True(worst < 4e-6 * distance, $"max |t - t_double| = {worst:E2}");
    }
}
