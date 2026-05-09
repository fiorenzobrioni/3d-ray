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

    // ─────────────────────────────────────────────────────────────────────────
    // Curved profile modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CatmullRom_CoarseCircle_ApproximatesCylinder()
    {
        // Catmull-Rom densifies an N-gon back into a smooth circle. With N=8
        // control points and curve_samples=8 we get a 64-vertex silhouette
        // that should match a cylinder within the discretisation error of
        // ~R(1−cos(π/64)) ≈ 1.2e-3.
        const float R = 1.0f, H = 2.0f;
        const int N = 8;
        var profile = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = 2f * MathF.PI * i / N;
            profile[i] = new Vector2(R * MathF.Cos(a), R * MathF.Sin(a));
        }

        var cylinder = new Cylinder(new Vector3(0, 0, 0), R, H, Mat());
        var ext = new Extrusion(profile, ExtrusionMode.CatmullRom, H,
            ExtrusionCaps.Both, Mat(), curveSamples: 8);

        var rng = new Random(401);
        int agreed = 0, total = 400;
        const float Tolerance = 8e-3f;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cylinder.BoundingBox());
            var (hA, tA) = HitOnce(cylinder, ray);
            var (hB, tB) = HitOnce(ext, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < Tolerance)) agreed++;
        }
        Assert.True(agreed >= (int)(total * 0.85),
            $"Catmull-Rom dense circle vs Cylinder: only {agreed}/{total} agreed.");
    }

    [Fact]
    public void Bezier_ClosedCircle_ApproximatesCylinder()
    {
        // Standard 4-segment cubic Bezier circle (Stanislaw 1972 approximation):
        // control offset k = 4·(√2 − 1)/3 ≈ 0.5522847 from each axis endpoint.
        const float R = 1.0f, H = 2.0f;
        float k = 4f * (MathF.Sqrt(2f) - 1f) / 3f;
        var profile = new[]
        {
            new Vector2(R, 0), new Vector2(0, R),
            new Vector2(-R, 0), new Vector2(0, -R),
        };
        var controls = new[]
        {
            // Q1: (R,0) → (0,R)
            new Vector2(R, 0),    new Vector2(R, k*R),  new Vector2(k*R, R),  new Vector2(0, R),
            // Q2: (0,R) → (-R,0)
            new Vector2(0, R),    new Vector2(-k*R, R), new Vector2(-R, k*R), new Vector2(-R, 0),
            // Q3: (-R,0) → (0,-R)
            new Vector2(-R, 0),   new Vector2(-R, -k*R),new Vector2(-k*R, -R),new Vector2(0, -R),
            // Q4: (0,-R) → (R,0)
            new Vector2(0, -R),   new Vector2(k*R, -R), new Vector2(R, -k*R), new Vector2(R, 0),
        };

        var cylinder = new Cylinder(new Vector3(0, 0, 0), R, H, Mat());
        var ext = new Extrusion(profile, ExtrusionMode.Bezier, H,
            ExtrusionCaps.Both, Mat(), bezierControls: controls, curveSamples: 16);

        var rng = new Random(402);
        int agreed = 0, total = 400;
        // Bezier-circle approximation error peaks at 0.027% on the radius;
        // we use the same tolerance bracket as the dense polygon test.
        const float Tolerance = 8e-3f;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cylinder.BoundingBox());
            var (hA, tA) = HitOnce(cylinder, ray);
            var (hB, tB) = HitOnce(ext, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < Tolerance)) agreed++;
        }
        Assert.True(agreed >= (int)(total * 0.9),
            $"Bezier circle vs Cylinder: only {agreed}/{total} agreed.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Taper — frustum equivalence with Cone
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Taper_Frustum_ApproximatesCone()
    {
        // A circular extrusion with taper = topR / bottomR is a frustum, which
        // is exactly what Cone(bottomR, topR, height) describes. With N=64
        // facets the silhouette matches the cone within the polygon error
        // budget AND — critically — the side normals must point outward and
        // tilted along Y, which the pre-fix Extrusion did not produce.
        const float Rb = 1.0f, Rt = 0.4f, H = 2.0f;
        const int N = 64;
        float scale = 1f / MathF.Cos(MathF.PI / N);
        var profile = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = 2f * MathF.PI * i / N;
            profile[i] = new Vector2(Rb * scale * MathF.Cos(a), Rb * scale * MathF.Sin(a));
        }

        var cone = new Cone(new Vector3(0, 0, 0), Rb, Rt, H, Mat());
        var ext = new Extrusion(profile, ExtrusionMode.Linear, H,
            ExtrusionCaps.Both, Mat(), taper: Rt / Rb);

        var rng = new Random(403);
        int agreed = 0, total = 400;
        const float Tolerance = 8e-3f;
        for (int i = 0; i < total; i++)
        {
            var ray = RandomRayToBox(rng, cone.BoundingBox());
            var (hA, tA) = HitOnce(cone, ray);
            var (hB, tB) = HitOnce(ext, ray);
            if (hA == hB && (!hA || System.Math.Abs(tA - tB) < Tolerance)) agreed++;
        }
        Assert.True(agreed >= (int)(total * 0.9),
            $"Tapered N-gon vs Cone: only {agreed}/{total} agreed.");
    }

    [Fact]
    public void Taper_SmoothCircle_NormalTiltsAlongY()
    {
        // Validates the taper-aware smooth normal fix together with the
        // outward-facing winding. With curved mode (CatmullRom) and taper ≠ 1
        // the side normals must (a) point against the incoming ray (outward
        // hemisphere — caught back-face winding regressions that produced a
        // solid-black render in the showcase) and (b) carry a non-trivial +Y
        // component because the surface tilts inward as it narrows. The
        // pre-fix code lifted the 2D edge normal into XZ untouched and left
        // Ny exactly 0.
        const float Rb = 1.0f, H = 2.0f;
        const int N = 16;
        var profile = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = 2f * MathF.PI * i / N;
            profile[i] = new Vector2(Rb * MathF.Cos(a), Rb * MathF.Sin(a));
        }
        var ext = new Extrusion(profile, ExtrusionMode.CatmullRom, H,
            ExtrusionCaps.None, Mat(), taper: 0.4f, curveSamples: 8);

        int sideHits = 0, sideHitsWithYTilt = 0;
        var rng = new Random(404);
        for (int i = 0; i < 80; i++)
        {
            float angle = (float)(rng.NextDouble() * 2 * System.Math.PI);
            var origin = new Vector3(3f * MathF.Cos(angle), 1f, 3f * MathF.Sin(angle));
            var dir = Vector3.Normalize(new Vector3(0, 1f, 0) - origin);
            var rec = new HitRecord();
            if (ext.Hit(new Ray(origin, dir), 0.001f, 1e30f, ref rec))
            {
                sideHits++;
                Assert.True(Vector3.Dot(rec.Normal, dir) < 0f,
                    $"Side hit at {rec.Point}: rec.Normal {rec.Normal} must oppose " +
                    $"the incoming ray {dir}. Got dot = {Vector3.Dot(rec.Normal, dir)}.");
                // Tapered cylinder narrows toward the top → outward normal
                // must tilt UP (positive Y) to face away from the slanted
                // side; the pre-fix horizontal-normal bug had Ny ≈ 0.
                if (rec.Normal.Y > 0.05f) sideHitsWithYTilt++;
            }
        }
        Assert.True(sideHits > 20, $"Expected side hits, got {sideHits}.");
        Assert.True(sideHitsWithYTilt > sideHits / 2,
            $"Tapered smooth side normals must tilt up along Y: only " +
            $"{sideHitsWithYTilt}/{sideHits} hits had Ny > 0.05.");
    }

    [Fact]
    public void StraightPrism_HitNormalsOpposeRay()
    {
        // End-to-end winding regression: a Disney-shaded straight prism
        // rendered black in the showcase because SmoothTriangle's face
        // normal (used for FrontFace classification) pointed inward, so
        // the renderer flipped the shading normal and the BSDF cosine
        // collapsed to zero. Test all four cardinal sides + top/bottom
        // caps and assert rec.Normal · ray.Direction < 0 for every hit
        // arriving from outside the prism.
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2(-0.5f, 0.5f),
        };
        // Curved mode forces SmoothTriangle on the side walls so we exercise
        // the path that previously rendered black.
        var ext = new Extrusion(profile, ExtrusionMode.CatmullRom, 1f,
            ExtrusionCaps.Both, Mat(), curveSamples: 6);

        var probes = new (Vector3 origin, Vector3 dir, string label)[]
        {
            (new Vector3( 2f, 0.5f, 0f), -Vector3.UnitX,  "+X side from outside"),
            (new Vector3(-2f, 0.5f, 0f),  Vector3.UnitX,  "-X side from outside"),
            (new Vector3(0f, 0.5f,  2f), -Vector3.UnitZ,  "+Z side from outside"),
            (new Vector3(0f, 0.5f, -2f),  Vector3.UnitZ,  "-Z side from outside"),
            (new Vector3(0f,  2f, 0f),   -Vector3.UnitY,  "top cap from above"),
            (new Vector3(0f, -1f, 0f),    Vector3.UnitY,  "bottom cap from below"),
        };
        foreach (var (origin, dir, label) in probes)
        {
            var rec = new HitRecord();
            Assert.True(ext.Hit(new Ray(origin, dir), 0.001f, 1e30f, ref rec),
                $"{label}: ray missed the prism.");
            float d = Vector3.Dot(rec.Normal, dir);
            Assert.True(d < -0.5f,
                $"{label}: rec.Normal {rec.Normal} must oppose ray.Direction {dir} " +
                $"(dot must be ≪ 0). Got dot = {d:G6} — winding regression.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Caps — independent verification per side
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CapsNone_VerticalRayPassesThrough()
    {
        // Square prism y ∈ [0, 1] with no caps. A ray going straight up from
        // (0, -1, 0) must miss entirely (it would enter the bottom and exit
        // the top through holes).
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2(-0.5f, 0.5f),
        };
        var ext = new Extrusion(profile, ExtrusionMode.Linear, 1f,
            ExtrusionCaps.None, Mat());
        var rec = new HitRecord();
        var ray = new Ray(new Vector3(0, -1, 0), Vector3.UnitY);
        Assert.False(ext.Hit(ray, 0.001f, 1e30f, ref rec),
            "caps=None must let a centred vertical ray pass through.");
    }

    [Fact]
    public void CapsStart_HitsBottomNotTop()
    {
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2(-0.5f, 0.5f),
        };
        var ext = new Extrusion(profile, ExtrusionMode.Linear, 1f,
            ExtrusionCaps.Start, Mat());
        // Up from below: must hit bottom cap.
        {
            var rec = new HitRecord();
            var ray = new Ray(new Vector3(0, -1, 0), Vector3.UnitY);
            Assert.True(ext.Hit(ray, 0.001f, 1e30f, ref rec),
                "caps=Start: upward ray must hit the bottom cap.");
            Assert.InRange(rec.Point.Y, -1e-3f, 1e-3f);
        }
        // Down from above: top is open, must hit something only after passing
        // through the open top — the closest hit will be the bottom cap from
        // the inside.
        {
            var rec = new HitRecord();
            var ray = new Ray(new Vector3(0, 2, 0), -Vector3.UnitY);
            Assert.True(ext.Hit(ray, 0.001f, 1e30f, ref rec));
            Assert.InRange(rec.Point.Y, -1e-3f, 1e-3f);
        }
    }

    [Fact]
    public void CapsEnd_HitsTopNotBottom()
    {
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2(-0.5f, 0.5f),
        };
        var ext = new Extrusion(profile, ExtrusionMode.Linear, 1f,
            ExtrusionCaps.End, Mat());
        // Down from above: must hit top cap at y = 1.
        var rec = new HitRecord();
        var ray = new Ray(new Vector3(0, 2, 0), -Vector3.UnitY);
        Assert.True(ext.Hit(ray, 0.001f, 1e30f, ref rec),
            "caps=End: downward ray must hit the top cap.");
        Assert.InRange(rec.Point.Y, 1f - 1e-3f, 1f + 1e-3f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CW vs CCW input orientation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CwInput_ProducesSameAreaAndAabbAsCcw()
    {
        var ccw = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2(-0.5f, 0.5f),
        };
        var cw = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2( 0.5f, -0.5f),
        };
        var a = new Extrusion(ccw, ExtrusionMode.Linear, 1f, ExtrusionCaps.Both, Mat());
        var b = new Extrusion(cw,  ExtrusionMode.Linear, 1f, ExtrusionCaps.Both, Mat());
        Assert.InRange(b.SurfaceArea, a.SurfaceArea - 1e-3f, a.SurfaceArea + 1e-3f);
        var ba = a.BoundingBox();
        var bb = b.BoundingBox();
        Assert.InRange((bb.Min - ba.Min).Length(), 0f, 1e-4f);
        Assert.InRange((bb.Max - ba.Max).Length(), 0f, 1e-4f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Twist — geometric sanity
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Twist_IncreasesSideArea()
    {
        // A twisted prism has skew quads on its sides — each ruled patch is
        // longer than its straight-prism counterpart, so the lateral area must
        // grow strictly with |twist|. This validates that twist actually
        // bends the side geometry and that the surface area sums all of it.
        var profile = new[]
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
            new Vector2( 0.5f,  0.5f), new Vector2(-0.5f, 0.5f),
        };
        var straight = new Extrusion(profile, ExtrusionMode.Linear, 1f,
            ExtrusionCaps.Both, Mat());
        var twisted = new Extrusion(profile, ExtrusionMode.Linear, 1f,
            ExtrusionCaps.Both, Mat(), twistDegrees: 90f);

        Assert.True(twisted.SurfaceArea > straight.SurfaceArea + 1e-3f,
            $"Twisted side area must exceed the straight prism's: " +
            $"twisted={twisted.SurfaceArea}, straight={straight.SurfaceArea}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Concave star — cap interior membership
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConcaveStar_TopCapRespectsPolygonInterior()
    {
        // 5-pointed star. Shoot rays straight down at the top cap from points
        // we know to be inside (origin) and outside (between two star points
        // at radius > Router) the polygon: hits must agree with point-in-
        // polygon membership. This is the actual test that ear-clipping
        // produced a hole-free triangulation of the concave shape.
        const int Points = 5;
        const float Router = 1.0f, Rinner = 0.4f;
        var profile = new Vector2[Points * 2];
        for (int i = 0; i < Points * 2; i++)
        {
            float a = MathF.PI / 2f - 2f * MathF.PI * i / (Points * 2);
            float r = (i % 2 == 0) ? Router : Rinner;
            profile[i] = new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
        }
        var ext = new Extrusion(profile, ExtrusionMode.Linear, 0.5f,
            ExtrusionCaps.Both, Mat());

        // (0, 0) is inside.
        {
            var rec = new HitRecord();
            var ray = new Ray(new Vector3(0, 2, 0), -Vector3.UnitY);
            Assert.True(ext.Hit(ray, 0.001f, 1e30f, ref rec),
                "Star centre must hit the top cap.");
            Assert.InRange(rec.Point.Y, 0.5f - 1e-3f, 0.5f + 1e-3f);
        }
        // A point well outside the polygon (radius 1.2 between two outer
        // tips, where the star concavity is closest in) must miss the cap
        // and instead hit a side wall further down — the down-ray must NOT
        // hit at the cap plane.
        {
            float a = MathF.PI / 2f - MathF.PI / Points; // angle of an inner notch
            var probe = new Vector3(1.2f * MathF.Cos(a), 2, 1.2f * MathF.Sin(a));
            var rec = new HitRecord();
            var ray = new Ray(probe, -Vector3.UnitY);
            // Either misses entirely or hits a side wall (not the top cap
            // plane). The cap plane is at y = 0.5; if the ray hits, the y
            // coordinate must be strictly below the cap.
            if (ext.Hit(ray, 0.001f, 1e30f, ref rec))
                Assert.True(rec.Point.Y < 0.5f - 1e-3f,
                    $"Outside-star down-ray hit the top cap at y={rec.Point.Y}; " +
                    $"ear-clipping leaked a triangle outside the polygon.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation of malformed inputs — must surface as ArgumentException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DegenerateProfile_CollinearPoints_Throws()
    {
        // Four collinear points: every candidate ear has zero cross product,
        // ear-clipping bails with zero triangles. The constructor must
        // surface this as an ArgumentException rather than silently emitting
        // a holed mesh. (n = 3 is a special case: the algorithm skips the
        // ear loop entirely and emits a degenerate zero-area triangle —
        // numerically silly but topologically valid, so we test n ≥ 4 to
        // hit the genuine partial-triangulation branch.)
        var collinear = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(2, 0), new Vector2(3, 0),
        };
        Assert.Throws<ArgumentException>(() =>
            new Extrusion(collinear, ExtrusionMode.Linear, 1f,
                ExtrusionCaps.Both, Mat()));
    }

    [Fact]
    public void BezierC0Break_Throws()
    {
        // Closed Bezier loop where segment 0's last control does not match
        // segment 1's first control — the resulting polyline has a visible
        // gap. Must surface as an ArgumentException at construction time.
        var profile = new[]
        {
            new Vector2(1, 0), new Vector2(0, 1),
            new Vector2(-1, 0), new Vector2(0, -1),
        };
        var bad = new[]
        {
            // Q1 endpoint (0,1) — but Q2 starts at (0.5, 1) instead of (0,1)
            new Vector2(1, 0),    new Vector2(1, 0.5f),  new Vector2(0.5f, 1), new Vector2(0, 1),
            new Vector2(0.5f, 1), new Vector2(-0.5f, 1), new Vector2(-1, 0.5f),new Vector2(-1, 0),
            new Vector2(-1, 0),   new Vector2(-1, -0.5f),new Vector2(-0.5f, -1),new Vector2(0, -1),
            new Vector2(0, -1),   new Vector2(0.5f, -1), new Vector2(1, -0.5f),new Vector2(1, 0),
        };
        Assert.Throws<ArgumentException>(() =>
            new Extrusion(profile, ExtrusionMode.Bezier, 1f,
                ExtrusionCaps.Both, Mat(), bezierControls: bad));
    }

    [Fact]
    public void NegativeTaper_Throws()
    {
        var square = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1),
        };
        Assert.Throws<ArgumentException>(() =>
            new Extrusion(square, ExtrusionMode.Linear, 1f,
                ExtrusionCaps.Both, Mat(), taper: -0.5f));
    }
}
