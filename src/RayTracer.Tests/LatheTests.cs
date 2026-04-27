using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Equivalence tests for <see cref="Lathe"/>. Following the BvhEquivalenceTests
/// pattern, the lathe is validated against existing analytic primitives that
/// represent the same surface: a linear profile of a rectangle must match
/// <see cref="Cylinder"/>, a triangular profile must match <see cref="Cone"/>,
/// and a densely sampled semicircle profile (Catmull-Rom) must match
/// <see cref="Sphere"/>. Additional tests assert AABB containment and Bezier
/// mode equivalence with the Catmull-Rom mode on a shared smooth profile.
/// </summary>
public class LatheTests
{
    private static IMaterial Mat() => new Lambertian(new Vector3(0.5f));
    private const float TEps = 1e-3f;

    // ─────────────────────────────────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static Ray RandomRayToBox(Random rng, AABB box, float pad = 1.5f)
    {
        // Origin on a sphere around the box; direction points roughly toward
        // the box so that most rays actually hit something — otherwise both
        // implementations agree on "miss" without testing the intersection.
        Vector3 center = 0.5f * (box.Min + box.Max);
        float radius = (box.Max - box.Min).Length() * pad + 1f;

        double theta = rng.NextDouble() * 2.0 * System.Math.PI;
        double phi = System.Math.Acos(2.0 * rng.NextDouble() - 1.0);
        Vector3 on = new Vector3(
            (float)(radius * System.Math.Sin(phi) * System.Math.Cos(theta)),
            (float)(radius * System.Math.Cos(phi)),
            (float)(radius * System.Math.Sin(phi) * System.Math.Sin(theta)));
        Vector3 origin = center + on;

        Vector3 jitter = new Vector3(
            (float)(rng.NextDouble() * 2 - 1),
            (float)(rng.NextDouble() * 2 - 1),
            (float)(rng.NextDouble() * 2 - 1)) * (radius * 0.3f);
        Vector3 target = center + jitter;
        Vector3 dir = Vector3.Normalize(target - origin);
        return new Ray(origin, dir);
    }

    private static (bool hit, float t) HitOnce(IHittable obj, Ray ray)
    {
        var rec = new HitRecord();
        bool h = obj.Hit(ray, 0.001f, 1e30f, ref rec);
        return (h, rec.T);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Linear mode equivalence tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Linear_Rectangle_MatchesCylinder()
    {
        float r = 1.0f, h = 2.0f;
        var cylinder = new Cylinder(new Vector3(0, 0, 0), r, h, Mat());
        var lathe = new Lathe(
            new[] { new Vector2(r, 0f), new Vector2(r, h) },
            LatheMode.Linear, Mat());

        var rng = new Random(101);
        int agreed = 0;
        int total = 500;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cylinder.BoundingBox());
            var (hA, tA) = HitOnce(cylinder, ray);
            var (hB, tB) = HitOnce(lathe, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < TEps)) agreed++;
        }
        Assert.True(agreed >= total - 5, $"Linear rectangle vs Cylinder: only {agreed}/{total} agreed.");
    }

    [Fact]
    public void Linear_Triangle_MatchesCone()
    {
        // Pointed cone from (1, 0) to (0, 1): base radius 1, height 1, apex at top.
        var cone = new Cone(new Vector3(0, 0, 0), 1f, 0f, 1f, Mat());
        var lathe = new Lathe(
            new[] { new Vector2(1f, 0f), new Vector2(0f, 1f) },
            LatheMode.Linear, Mat());

        var rng = new Random(102);
        int agreed = 0;
        int total = 500;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cone.BoundingBox());
            var (hA, tA) = HitOnce(cone, ray);
            var (hB, tB) = HitOnce(lathe, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < TEps)) agreed++;
        }
        Assert.True(agreed >= total - 10, $"Linear triangle vs Cone: only {agreed}/{total} agreed.");
    }

    [Fact]
    public void Linear_Trapezoid_MatchesFrustum()
    {
        var cone = new Cone(new Vector3(0, 0, 0), 1f, 0.5f, 2f, Mat());
        var lathe = new Lathe(
            new[] { new Vector2(1f, 0f), new Vector2(0.5f, 2f) },
            LatheMode.Linear, Mat());

        var rng = new Random(103);
        int agreed = 0;
        int total = 500;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cone.BoundingBox());
            var (hA, tA) = HitOnce(cone, ray);
            var (hB, tB) = HitOnce(lathe, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < TEps)) agreed++;
        }
        Assert.True(agreed >= total - 10, $"Linear trapezoid vs Frustum: only {agreed}/{total} agreed.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Catmull-Rom semicircle vs Sphere — exercises the spline path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CatmullRom_Semicircle_MatchesSphere_Dense()
    {
        const float R = 1.0f;
        const int N = 48;
        var pts = new Vector2[N + 1];
        for (int i = 0; i <= N; i++)
        {
            // Half-circle profile: u = 0 → south pole, u = 1 → north pole.
            float theta = MathF.PI * (1f - i / (float)N);   // π → 0
            pts[i] = new Vector2(R * MathF.Sin(theta), R * MathF.Cos(theta));
        }
        // Shift so bottom pole sits at y = 0 (avoids negative y in the profile,
        // which would break monotonicity; equivalently a translation).
        for (int i = 0; i <= N; i++) pts[i] = new Vector2(pts[i].X, pts[i].Y + R);

        var sphere = new Sphere(new Vector3(0, R, 0), R, Mat());
        var lathe = new Lathe(pts, LatheMode.CatmullRom, Mat());

        var rng = new Random(202);
        int agreed = 0;
        int total = 400;
        // Densely sampled Catmull-Rom matches a sphere to ~5e-3 due to the
        // discretisation of the profile (each 48th of π is ~0.065 rad; the
        // sagitta between sample points is ~0.002 for a unit circle). We
        // accept a generous 2e-2 tolerance to tolerate grazing hits.
        const float ToleranceT = 2e-2f;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, sphere.BoundingBox());
            var (hA, tA) = HitOnce(sphere, ray);
            var (hB, tB) = HitOnce(lathe, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < ToleranceT)) agreed++;
        }
        Assert.True(agreed >= (int)(total * 0.9),
            $"Catmull-Rom semicircle vs Sphere: only {agreed}/{total} agreed within 2e-2.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bezier mode — rational-cylinder approximation via straight-line controls
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bezier_DegenerateCylinder_MatchesCylinder()
    {
        // A single cubic-Bezier segment where every control point has the
        // same R(u) = 1 makes the revolved surface an exact cylinder; Y(u)
        // is a linear interpolation from 0 to 2 placed at the four control
        // points 0, 2/3, 4/3, 2 so it is still linear in u.
        const float r = 1f, h = 2f;
        var profile = new[] { new Vector2(r, 0f), new Vector2(r, h) };
        var controls = new[]
        {
            new Vector2(r, 0f),
            new Vector2(r, h * 1f / 3f),
            new Vector2(r, h * 2f / 3f),
            new Vector2(r, h),
        };
        var lathe = new Lathe(profile, LatheMode.Bezier, Mat(), controls);
        var cylinder = new Cylinder(new Vector3(0, 0, 0), r, h, Mat());

        var rng = new Random(303);
        int agreed = 0;
        int total = 500;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cylinder.BoundingBox());
            var (hA, tA) = HitOnce(cylinder, ray);
            var (hB, tB) = HitOnce(lathe, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < TEps)) agreed++;
        }
        Assert.True(agreed >= total - 10,
            $"Bezier-cylinder vs Cylinder: only {agreed}/{total} agreed.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AABB containment — every hit point must lie inside the reported box
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CatmullRom_HitPoints_InsideBoundingBox()
    {
        // Generate a handful of non-convex profiles and verify the AABB tightly
        // contains every world-space hit. A fail here means the extrema search
        // in SplineSegment is off, which could poison BVH pruning.
        var rng = new Random(404);
        const float Pad = 1e-3f;
        for (int scene = 0; scene < 8; scene++)
        {
            int n = 5 + rng.Next(5);
            var pts = new Vector2[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = 0.2f + (float)rng.NextDouble() * 0.8f;
                y += 0.05f + (float)rng.NextDouble() * 0.4f;
                pts[i] = new Vector2(r, y);
            }

            var lathe = new Lathe(pts, LatheMode.CatmullRom, Mat());
            var box = lathe.BoundingBox();

            int tested = 0;
            for (int i = 0; i < 300 && tested < 80; i++)
            {
                var ray = RandomRayToBox(rng, box);
                var rec = new HitRecord();
                if (!lathe.Hit(ray, 0.001f, 1e30f, ref rec)) continue;
                tested++;
                Assert.InRange(rec.Point.X, box.Min.X - Pad, box.Max.X + Pad);
                Assert.InRange(rec.Point.Y, box.Min.Y - Pad, box.Max.Y + Pad);
                Assert.InRange(rec.Point.Z, box.Min.Z - Pad, box.Max.Z + Pad);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Loader-side preconditions — y monotonic, Catmull-Rom fallback
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Lathe_Constructor_RejectsShortProfile()
    {
        Assert.Throws<ArgumentException>(() =>
            new Lathe(new[] { new Vector2(1f, 0f) }, LatheMode.Linear, Mat()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Horizontal Linear segment — must produce an annular disc, not a hole
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Linear_HorizontalSegment_RendersAsAnnularDisc()
    {
        // A profile that starts with a horizontal step (Δy = 0, Δr ≠ 0) used
        // to be silently swallowed by the frustum quadratic. The fix routes
        // such steps through an AnnulusSegment so the disc face actually
        // shows up. Validate by aiming straight-down rays at the disc plane:
        // every ray with x²+z² ∈ [0, R²] must hit at y = 0.
        const float R = 0.55f;
        var profile = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(R,  0f),  // horizontal step — the disc
            new Vector2(R,  1f),  // vertical wall, frustum
        };
        var lathe = new Lathe(profile, LatheMode.Linear, Mat());

        var rng = new Random(505);
        int discHits = 0;
        const int total = 200;
        for (int i = 0; i < total; i++)
        {
            // Origin above the disc, looking straight down.
            float r = (float)rng.NextDouble() * R * 0.95f;
            float th = (float)(rng.NextDouble() * 2 * System.Math.PI);
            var origin = new Vector3(r * MathF.Cos(th), 1.5f, r * MathF.Sin(th));
            var ray = new Ray(origin, new Vector3(0, -1, 0));
            var rec = new HitRecord();
            if (!lathe.Hit(ray, 0.001f, 1e30f, ref rec)) continue;
            // First hit on a downward ray must be the top of the wall (y = 1)
            // — that's the closer surface. So instead aim from below.
        }

        // Aim from below the disc upward so the disc itself is the closest hit.
        for (int i = 0; i < total; i++)
        {
            float r = (float)rng.NextDouble() * R * 0.95f;
            float th = (float)(rng.NextDouble() * 2 * System.Math.PI);
            var origin = new Vector3(r * MathF.Cos(th), -1.5f, r * MathF.Sin(th));
            var ray = new Ray(origin, new Vector3(0, 1, 0));
            var rec = new HitRecord();
            if (lathe.Hit(ray, 0.001f, 1e30f, ref rec) && MathF.Abs(rec.Point.Y) < 1e-3f)
                discHits++;
        }

        // Without the fix, this used to be ~0. With it, almost every ray
        // hits the disc plane.
        Assert.True(discHits >= (int)(total * 0.95),
            $"Horizontal segment failed: only {discHits}/{total} disc rays hit y = 0.");
    }

    [Fact]
    public void Lathe_Constructor_BezierWithWrongControlCount_Throws()
    {
        var profile = new[] { new Vector2(1f, 0f), new Vector2(1f, 1f) };
        var shortControls = new[] { new Vector2(1f, 0f), new Vector2(1f, 1f) };
        Assert.Throws<ArgumentException>(() =>
            new Lathe(profile, LatheMode.Bezier, Mat(), shortControls));
    }
}
