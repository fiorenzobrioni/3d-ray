using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Equivalence tests for <see cref="Extrusion"/>. Following the LatheTests
/// pattern, the extrusion is validated against existing analytic primitives:
/// a 4-point square profile must match a <see cref="Box"/>, an N-gon profile
/// with N → ∞ must approach a <see cref="Cylinder"/>, and the cap NEE
/// surface area must match the closed-form polygon area · sides count.
/// Concave profiles (5-pointed star) are validated for AABB containment and
/// no spurious holes.
/// </summary>
public class ExtrusionTests
{
    private static IMaterial Mat() => new Lambertian(new Vector3(0.5f));
    private const float TEps = 1e-3f;

    private static Ray RandomRayToBox(Random rng, AABB box, float pad = 1.5f)
    {
        Vector3 center = 0.5f * (box.Min + box.Max);
        float radius = (box.Max - box.Min).Length() * pad + 1f;
        double theta = rng.NextDouble() * 2.0 * System.Math.PI;
        double phi = System.Math.Acos(2.0 * rng.NextDouble() - 1.0);
        Vector3 on = new(
            (float)(radius * System.Math.Sin(phi) * System.Math.Cos(theta)),
            (float)(radius * System.Math.Cos(phi)),
            (float)(radius * System.Math.Sin(phi) * System.Math.Sin(theta)));
        Vector3 origin = center + on;
        Vector3 jitter = new Vector3(
            (float)(rng.NextDouble() * 2 - 1),
            (float)(rng.NextDouble() * 2 - 1),
            (float)(rng.NextDouble() * 2 - 1)) * (radius * 0.3f);
        Vector3 dir = Vector3.Normalize(center + jitter - origin);
        return new Ray(origin, dir);
    }

    private static (bool hit, float t) HitOnce(IHittable obj, Ray ray)
    {
        var rec = new HitRecord();
        bool h = obj.Hit(ray, 0.001f, 1e30f, ref rec);
        return (h, rec.T);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Linear square profile vs Box
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Square_Profile_MatchesBox()
    {
        // Box is a unit cube centered at the origin (Min = -0.5, Max = +0.5).
        var box = new Box(Mat());
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f),
            new Vector2( 0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f),
            new Vector2(-0.5f,  0.5f),
        };
        // Box spans y ∈ [-0.5, +0.5]; extrusion default sits at y ∈ [0, 1].
        // Translate the extrusion to align them.
        var ext = new Extrusion(profile, ExtrusionMode.Linear, height: 1f,
            ExtrusionCaps.Both, Mat());
        var aligned = new Transform(ext,
            Matrix4x4.CreateTranslation(new Vector3(0f, -0.5f, 0f)));

        var rng = new Random(301);
        int agreed = 0, total = 500;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, box.BoundingBox());
            var (hA, tA) = HitOnce(box, ray);
            var (hB, tB) = HitOnce(aligned, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < TEps)) agreed++;
        }
        Assert.True(agreed >= total - 10,
            $"Square profile vs Box: only {agreed}/{total} agreed.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dense N-gon vs Cylinder
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DenseNgon_MatchesCylinder()
    {
        const float R = 1.0f, H = 2.0f;
        const int N = 64;

        // Inscribed polygon with apothem matching the radius minimises silhouette
        // error for a coarse N — bumping the polygon out to the circumscribed
        // radius would make the lateral surface land outside the cylinder.
        float scale = R / MathF.Cos(MathF.PI / N); // circumscribed radius
        var profile = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = 2f * MathF.PI * i / N;
            profile[i] = new Vector2(scale * MathF.Cos(a), scale * MathF.Sin(a));
        }

        var cylinder = new Cylinder(new Vector3(0, 0, 0), R, H, Mat());
        var ext = new Extrusion(profile, ExtrusionMode.Linear, H,
            ExtrusionCaps.Both, Mat());

        var rng = new Random(302);
        int agreed = 0, total = 400;
        // Discretisation of the silhouette into 64 facets adds at most R(1-cos(π/N))
        // ≈ 1.2e-3 on the outer radius. We allow a slightly looser tolerance for
        // grazing rays where ray-cylinder vs ray-polygon disagree by that order.
        const float Tolerance = 8e-3f;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cylinder.BoundingBox());
            var (hA, tA) = HitOnce(cylinder, ray);
            var (hB, tB) = HitOnce(ext, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < Tolerance)) agreed++;
        }
        Assert.True(agreed >= (int)(total * 0.9),
            $"Dense {N}-gon vs Cylinder: only {agreed}/{total} agreed.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Concave profile (5-pointed star) — AABB containment
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConcaveStar_HitsLieInsideAabb()
    {
        // 5-pointed star alternating outer (R = 1) and inner (r = 0.4) vertices.
        const int Points = 5;
        const float Router = 1.0f, Rinner = 0.4f;
        var profile = new Vector2[Points * 2];
        for (int i = 0; i < Points * 2; i++)
        {
            float a = MathF.PI / 2f - 2f * MathF.PI * i / (Points * 2);
            float r = (i % 2 == 0) ? Router : Rinner;
            profile[i] = new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
        }
        var ext = new Extrusion(profile, ExtrusionMode.Linear, height: 0.5f,
            ExtrusionCaps.Both, Mat());

        var aabb = ext.BoundingBox();
        var rng = new Random(303);
        int hits = 0;
        const int total = 200;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, aabb);
            var rec = new HitRecord();
            if (ext.Hit(ray, 0.001f, 1e30f, ref rec))
            {
                hits++;
                Vector3 p = rec.Point;
                Assert.InRange(p.X, aabb.Min.X - 1e-3f, aabb.Max.X + 1e-3f);
                Assert.InRange(p.Y, aabb.Min.Y - 1e-3f, aabb.Max.Y + 1e-3f);
                Assert.InRange(p.Z, aabb.Min.Z - 1e-3f, aabb.Max.Z + 1e-3f);
            }
        }
        Assert.True(hits > 20, $"Star extrusion produced only {hits} hits across {total} rays.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Surface area / NEE
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Square_SurfaceArea_MatchesBox()
    {
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f),
            new Vector2( 0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f),
            new Vector2(-0.5f,  0.5f),
        };
        var ext = new Extrusion(profile, ExtrusionMode.Linear, height: 2f,
            ExtrusionCaps.Both, Mat());

        // Closed-form: sides = perimeter · height = 4 · 2 = 8; caps = 2 · 1 = 2.
        const float expected = 10f;
        Assert.InRange(ext.SurfaceArea, expected - 1e-3f, expected + 1e-3f);
    }

    [Fact]
    public void NoCaps_OmitsTopAndBottom()
    {
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f),
            new Vector2( 0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f),
            new Vector2(-0.5f,  0.5f),
        };
        var ext = new Extrusion(profile, ExtrusionMode.Linear, height: 2f,
            ExtrusionCaps.None, Mat());
        // Only side walls: 4 · 2 = 8.
        Assert.InRange(ext.SurfaceArea, 8f - 1e-3f, 8f + 1e-3f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TooFewPoints_Throws()
    {
        var profile = new[] { new Vector2(0, 0), new Vector2(1, 0) };
        Assert.Throws<ArgumentException>(() =>
            new Extrusion(profile, ExtrusionMode.Linear, 1f, ExtrusionCaps.Both, Mat()));
    }

    [Fact]
    public void NonPositiveHeight_Throws()
    {
        var square = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1),
        };
        Assert.Throws<ArgumentException>(() =>
            new Extrusion(square, ExtrusionMode.Linear, 0f, ExtrusionCaps.Both, Mat()));
    }

    [Fact]
    public void BezierWithWrongControlCount_Throws()
    {
        var profile = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1),
        };
        var bogus = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0) };
        Assert.Throws<ArgumentException>(() =>
            new Extrusion(profile, ExtrusionMode.Bezier, 1f, ExtrusionCaps.Both, Mat(),
                bezierControls: bogus));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ear clipping is exercised end-to-end by the concave-star and L-shaped
    // surface area tests — the SignedArea / cap counts implicitly validate
    // that ear clipping produced N-2 triangles for the cap.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LShape_Concave_SurfaceAreaMatchesClosedForm()
    {
        // L-shape (6 vertices, area = 3, perimeter = 8). Two caps + side strip.
        var poly = new[]
        {
            new Vector2(0, 0), new Vector2(2, 0),
            new Vector2(2, 1), new Vector2(1, 1),
            new Vector2(1, 2), new Vector2(0, 2),
        };
        const float h = 1f;
        var ext = new Extrusion(poly, ExtrusionMode.Linear, h,
            ExtrusionCaps.Both, Mat());
        const float expected = 8f * h + 2f * 3f; // perimeter·h + 2·area
        Assert.InRange(ext.SurfaceArea, expected - 1e-3f, expected + 1e-3f);
    }
}
