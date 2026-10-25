using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Every primitive reports ∂P/∂u and ∂P/∂v of its own texture mapping
/// (PIANO-REVIEW.md Phase 4): the texture footprint and the bump map turn a
/// step on the surface into a step in UV with them. Checked numerically: two
/// neighbouring hits must satisfy ΔP ≈ ∂P/∂u·Δu + ∂P/∂v·Δv, and the tangent
/// must point along ∂P/∂u.
/// </summary>
public class PrimitiveUvDerivativeTests
{
    private readonly ITestOutputHelper _out;
    public PrimitiveUvDerivativeTests(ITestOutputHelper output) => _out = output;

    private static readonly IMaterial M = new Lambertian(new Vector3(0.5f));

    public static IEnumerable<object[]> Primitives() => new[]
    {
        new object[] { "sphere" }, new object[] { "quad" }, new object[] { "box" },
        new object[] { "cylinder" }, new object[] { "cone" }, new object[] { "frustum" },
        new object[] { "disk" }, new object[] { "annulus" }, new object[] { "capsule" },
        new object[] { "torus" }, new object[] { "plane" }, new object[] { "triangle" },
        new object[] { "smooth-triangle" }, new object[] { "lathe" }, new object[] { "lathe-step" },
        new object[] { "lathe-spline" }, new object[] { "heightfield" },
    };

    private static IHittable Make(string name) => name switch
    {
        "sphere"   => new Sphere(Vector3.Zero, 1f, M),
        "quad"     => new Quad(new Vector3(-1, -1, 0.2f), new Vector3(2, 0.3f, 0), new Vector3(0.2f, 1.5f, 0.1f), M),
        "box"      => new Box(M),
        "cylinder" => new Cylinder(Vector3.Zero, 0.8f, 1.5f, M),
        "cone"     => new Cone(Vector3.Zero, 0.8f, 1.5f, M),
        "frustum"  => new Cone(Vector3.Zero, 0.8f, 0.3f, 1.5f, M),
        "disk"     => new Disk(Vector3.Zero, Vector3.Normalize(new Vector3(0.2f, 1, 0.3f)), 1f, M),
        "annulus"  => new Annulus(Vector3.Zero, Vector3.Normalize(new Vector3(0.2f, 1, 0.3f)), 1f, 0.4f, M),
        "capsule"  => new Capsule(Vector3.Zero, 0.5f, 1.5f, M),
        "torus"    => new Torus(1f, 0.35f, M),
        "plane"    => new InfinitePlane(Vector3.Zero, Vector3.Normalize(new Vector3(0.1f, 1, 0.2f)), M),
        "triangle" => new Triangle(new Vector3(-1, -1, 0), new Vector3(1.2f, -0.8f, 0.1f), new Vector3(0, 1, -0.2f), M),
        "smooth-triangle" => new SmoothTriangle(
            new Vector3(-1, -1, 0), new Vector3(1.2f, -0.8f, 0.1f), new Vector3(0, 1, -0.2f),
            Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
            new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.3f), new Vector2(0.4f, 0.8f), M),
        "lathe"    => new Lathe(new[] { new Vector2(0.3f, -0.7f), new Vector2(0.8f, 0f), new Vector2(0.5f, 0.7f) },
                                LatheMode.Linear, M),
        // A horizontal step (annular face) and the spline path, whose V is the
        // arc-length table's piecewise-linear fraction.
        "lathe-step" => new Lathe(new[] { new Vector2(0.3f, -0.7f), new Vector2(0.8f, 0f), new Vector2(0.5f, 0f),
                                          new Vector2(0.4f, 0.7f) }, LatheMode.Linear, M),
        "lathe-spline" => new Lathe(new[] { new Vector2(0.3f, -0.7f), new Vector2(0.8f, -0.2f), new Vector2(0.6f, 0.2f),
                                            new Vector2(0.5f, 0.7f) }, LatheMode.CatmullRom, M),
        "heightfield" => HeightFieldOf(),
        _ => throw new ArgumentException(name),
    };

    private static IHittable HeightFieldOf()
    {
        const int n = 9;
        var s = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                s[i + j * n] = 0.5f + 0.4f * MathF.Sin(1.3f * i) * MathF.Cos(0.9f * j);
        return new HeightField(-1.2f, -1.2f, 1.2f, 1.2f, s, n, n, 0.5f, M);
    }

    private static bool HitAt(IHittable h, Vector3 o, Vector3 d, out HitRecord rec)
    {
        rec = new HitRecord();
        return h.Hit(new Ray(o, d), 1e-4f, 1e4f, ref rec);
    }

    [Theory]
    [MemberData(nameof(Primitives))]
    public void DpDu_DpDv_MatchTheUvMapping(string name)
    {
        var prim = Make(name);
        var rng = new Random(7);
        int checkedHits = 0, bad = 0;
        string firstBad = "";
        const float eps = 2e-4f;

        for (int i = 0; i < 4000 && checkedHits < 300; i++)
        {
            // Rays from a sphere of radius 4 towards a jittered point near the origin.
            Vector3 o = 4f * Vector3.Normalize(new Vector3(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f));
            Vector3 target = 0.8f * new Vector3(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f);
            Vector3 d = Vector3.Normalize(target - o);
            if (!HitAt(prim, o, d, out var r0)) continue;

            Vector3 a = Vector3.Normalize(Vector3.Cross(d, MathF.Abs(d.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            Vector3 b = Vector3.Cross(d, a);
            bool ok = true;
            foreach (var off in new[] { a, b })
            {
                if (!HitAt(prim, o + eps * off, d, out var r1)) { ok = false; break; }
                float du = r1.U - r0.U, dv = r1.V - r0.V;
                Vector3 dp = r1.Point - r0.Point;
                // Skip seams, edges and jumps between faces.
                if (MathF.Abs(du) > 0.05f || MathF.Abs(dv) > 0.05f || Vector3.Dot(r0.Normal, r1.Normal) < 0.99f
                    || dp.Length() > 20f * eps) { ok = false; break; }
                Vector3 pred = r0.DpDu * du + r0.DpDv * dv;
                float err = (pred - dp).Length() / MathF.Max(dp.Length(), 1e-7f);
                if (err > 0.05f)
                {
                    bad++;
                    if (firstBad == "") firstBad = $"P={r0.Point} uv=({r0.U},{r0.V}) dpdu={r0.DpDu} dpdv={r0.DpDv} dp={dp} pred={pred} err={err:F3}";
                }
            }
            if (!ok) continue;
            checkedHits++;
            if (r0.DpDu.LengthSquared() > 0f && Vector3.Dot(Vector3.Normalize(r0.DpDu), r0.Tangent) < 0.99f)
            {
                bad++;
                if (firstBad == "") firstBad = $"tangent {r0.Tangent} not along dpdu {r0.DpDu} at {r0.Point}";
            }
        }
        _out.WriteLine($"{name}: {checkedHits} hits, {bad} bad. {firstBad}");
        Assert.True(checkedHits >= 100, $"{name}: only {checkedHits} usable hits");
        Assert.True(bad == 0, $"{name}: {bad} bad. {firstBad}");
    }
}
