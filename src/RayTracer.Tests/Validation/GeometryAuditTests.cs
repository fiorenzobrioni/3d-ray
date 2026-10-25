using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Every primitive against an independent description of its surface
/// (PIANO-REVIEW.md Phase 2):
/// <list type="bullet">
/// <item>the first hit along a ray, from outside and from inside, against the
///   first sign change of the shape's implicit function scanned along the ray
///   (or an analytic plane test for flat shapes): no hole, no phantom hit, the
///   closest one;</item>
/// <item>at every hit the point on the surface, the normal against the ray,
///   along the implicit's gradient, and <c>FrontFace</c> true exactly when the
///   ray arrives from outside;</item>
/// <item>the area, measured by the Cauchy-Crofton formula from the hits of
///   isotropic random lines (E[#hits] = A / (2πR²) inside a sphere of radius
///   R), against the closed form and the primitive's own <c>SurfaceArea</c>;</item>
/// <item>area sampling (<see cref="ISamplable"/>): the integral of a test
///   function estimated from the samples (with their reported area) equals the
///   Crofton integral of the same function; samples lie on the surface with
///   the outward normal and the UV the hit reports there.</item>
/// </list>
/// </summary>
[Collection("SamplerExclusive")]
public class GeometryAuditTests
{
    private readonly ITestOutputHelper _out;
    public GeometryAuditTests(ITestOutputHelper output)
    {
        _out = output;
        Sampler.SetKind(SamplerKind.Prng);
    }

    private static readonly IMaterial M = new Lambertian(new Vector3(0.5f));

    /// <summary>A primitive and what is known about it independently.</summary>
    private sealed record Spec(
        IHittable Shape,
        Vector3 Center,
        float Radius,                       // bounding sphere around Center
        Func<Vector3, float>? Implicit,     // < 0 inside, 0 on the surface (closed solids)
        Func<Ray, float?>? PlaneHit,        // analytic first hit (flat shapes)
        Func<Vector3, Vector3>? Outward,    // reference outward normal at a surface point
        double? Area);                      // closed form

    // ── Implicit functions (signed distances where exact) ───────────────────

    private static float SdBox(Vector3 p, Vector3 half)
    {
        Vector3 q = Vector3.Abs(p) - half;
        return Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f);
    }

    private static float SdCylinder(Vector3 p, Vector3 baseCenter, float r, float h)
    {
        Vector3 q = p - baseCenter - new Vector3(0, h / 2, 0);
        float dx = new Vector2(q.X, q.Z).Length() - r, dy = MathF.Abs(q.Y) - h / 2;
        return MathF.Min(MathF.Max(dx, dy), 0f) + new Vector2(MathF.Max(dx, 0f), MathF.Max(dy, 0f)).Length();
    }

    private static float InCone(Vector3 p, Vector3 baseCenter, float r1, float r2, float h)
    {
        // Not a distance: the larger of the (scaled) violations of the three
        // half-spaces, negative exactly inside, zero on the surface.
        Vector3 q = p - baseCenter;
        float y = q.Y;
        float r = r1 + (r2 - r1) * y / h;
        float slant = MathF.Sqrt(h * h + (r1 - r2) * (r1 - r2)) / h;
        float side = (new Vector2(q.X, q.Z).Length() - r) / slant;
        return MathF.Max(side, MathF.Max(-y, y - h));
    }

    private static float SdCapsule(Vector3 p, Vector3 a, float r, float h)
    {
        float y = Math.Clamp(p.Y - a.Y, 0f, h);
        return (p - (a + new Vector3(0, y, 0))).Length() - r;
    }

    private static float SdTorus(Vector3 p, float R, float r)
        => new Vector2(new Vector2(p.X, p.Z).Length() - R, p.Y).Length() - r;

    private static Vector3 Gradient(Func<Vector3, float> f, Vector3 p, float h = 1e-4f)
    {
        var g = new Vector3(
            f(p + new Vector3(h, 0, 0)) - f(p - new Vector3(h, 0, 0)),
            f(p + new Vector3(0, h, 0)) - f(p - new Vector3(0, h, 0)),
            f(p + new Vector3(0, 0, h)) - f(p - new Vector3(0, 0, h)));
        return g / (2 * h);
    }

    private static Func<Vector3, Vector3> Grad(Func<Vector3, float> f) => p => Vector3.Normalize(Gradient(f, p));

    // ── Specs ───────────────────────────────────────────────────────────────

    public static TheoryData<string> Closed() => new()
    {
        "sphere", "box", "cylinder", "cone", "frustum", "capsule", "torus",
        "ellipsoid", "box-sheared", "box-mirrored", "torus-scaled",
    };

    public static TheoryData<string> Flat() => new() { "quad", "disk", "annulus", "triangle" };

    public static TheoryData<string> All() => new()
    {
        "sphere", "box", "cylinder", "cone", "frustum", "capsule", "torus",
        "ellipsoid", "box-sheared", "box-mirrored", "torus-scaled",
        "quad", "disk", "annulus", "triangle", "lathe", "lathe-spline", "extrusion", "mesh",
    };

    private static Spec Make(string name)
    {
        switch (name)
        {
            case "sphere":
            {
                var c = new Vector3(0.2f, -0.1f, 0.3f);
                Func<Vector3, float> f = p => (p - c).Length() - 0.8f;
                return new Spec(new Sphere(c, 0.8f, M), c, 0.9f, f, null, Grad(f), 4 * Math.PI * 0.64);
            }
            case "box":
            {
                Func<Vector3, float> f = p => SdBox(p, new Vector3(0.5f));
                return new Spec(new Box(M), Vector3.Zero, 0.9f, f, null, Grad(f), 6);
            }
            case "cylinder":
            {
                var c = new Vector3(0.1f, -0.7f, 0f);
                Func<Vector3, float> f = p => SdCylinder(p, c, 0.6f, 1.4f);
                return new Spec(new Cylinder(c, 0.6f, 1.4f, M), c + new Vector3(0, 0.7f, 0), 1f, f, null, Grad(f),
                                2 * Math.PI * 0.6 * 1.4 + 2 * Math.PI * 0.36);
            }
            case "cone":
            case "frustum":
            {
                var c = new Vector3(0f, -0.6f, 0.1f);
                float r1 = 0.7f, r2 = name == "cone" ? 0f : 0.3f, h = 1.2f;
                Func<Vector3, float> f = p => InCone(p, c, r1, r2, h);
                double s = Math.Sqrt(h * h + (r1 - r2) * (r1 - r2));
                var shape = name == "cone" ? new Cone(c, r1, h, M) : new Cone(c, r1, r2, h, M);
                return new Spec(shape, c + new Vector3(0, 0.6f, 0), 1f, f, null, Grad(f),
                                Math.PI * (r1 + r2) * s + Math.PI * (r1 * r1 + r2 * r2));
            }
            case "capsule":
            {
                var c = new Vector3(0f, -0.5f, 0f);
                Func<Vector3, float> f = p => SdCapsule(p, c, 0.4f, 1f);
                return new Spec(new Capsule(c, 0.4f, 1f, M), c + new Vector3(0, 0.5f, 0), 1f, f, null, Grad(f),
                                2 * Math.PI * 0.4 * 1 + 4 * Math.PI * 0.16);
            }
            case "torus":
            {
                Func<Vector3, float> f = p => SdTorus(p, 0.7f, 0.25f);
                return new Spec(new Torus(0.7f, 0.25f, M), Vector3.Zero, 1f, f, null, Grad(f),
                                4 * Math.PI * Math.PI * 0.7 * 0.25);
            }
            case "ellipsoid":
                return Transformed(new Sphere(Vector3.Zero, 1f, M), p => p.Length() - 1f,
                                   Matrix4x4.CreateScale(0.4f, 0.7f, 1.1f) * Matrix4x4.CreateRotationZ(0.5f)
                                   * Matrix4x4.CreateRotationX(0.3f) * Matrix4x4.CreateTranslation(0.1f, 0f, -0.1f));
            case "box-sheared":
                // Rotation between two non-uniform scales: a sheared box.
                return Transformed(new Box(M), p => SdBox(p, new Vector3(0.5f)),
                                   Matrix4x4.CreateScale(1.2f, 0.5f, 0.8f) * Matrix4x4.CreateRotationY(0.6f)
                                   * Matrix4x4.CreateScale(1f, 1.4f, 0.6f));
            case "box-mirrored":
                return Transformed(new Box(M), p => SdBox(p, new Vector3(0.5f)),
                                   Matrix4x4.CreateScale(-1.1f, 0.9f, 0.7f) * Matrix4x4.CreateRotationX(0.4f));
            case "torus-scaled":
                return Transformed(new Torus(0.7f, 0.25f, M), p => SdTorus(p, 0.7f, 0.25f),
                                   Matrix4x4.CreateScale(1f, 2f, 0.8f) * Matrix4x4.CreateRotationX(1f));
            case "quad":
            {
                Vector3 q = new(-0.6f, -0.5f, 0.1f), u = new(1.2f, 0.2f, 0f), v = new(0.1f, 0.9f, 0.3f);
                Vector3 n = Vector3.Normalize(Vector3.Cross(u, v));
                return new Spec(new Quad(q, u, v, M), q + 0.5f * (u + v), 0.95f, null,
                                r => PlaneHit(r, q, n, p => InParallelogram(p - q, u, v)), _ => n,
                                Vector3.Cross(u, v).Length());
            }
            case "disk":
            case "annulus":
            {
                Vector3 c = new(0.1f, 0f, -0.1f), n = Vector3.Normalize(new Vector3(0.3f, 1f, -0.2f));
                float ri = name == "disk" ? 0f : 0.35f;
                IHittable shape = name == "disk" ? new Disk(c, n, 0.8f, M) : new Annulus(c, n, 0.8f, ri, M);
                return new Spec(shape, c, 0.85f, null,
                                r => PlaneHit(r, c, n, p => { float d = (p - c).Length(); return d <= 0.8f && d >= ri; }),
                                _ => n, Math.PI * (0.64 - ri * ri));
            }
            case "triangle":
            {
                Vector3 a = new(-0.7f, -0.5f, 0f), b = new(0.8f, -0.4f, 0.2f), c = new(0f, 0.7f, -0.3f);
                Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                return new Spec(new Triangle(a, b, c, M), (a + b + c) / 3, 0.95f, null,
                                r => PlaneHit(r, a, n, p => InTriangle(p, a, b, c)), _ => n,
                                0.5 * Vector3.Cross(b - a, c - a).Length());
            }
            case "lathe":
            {
                var prof = new[] { new Vector2(0f, -0.6f), new Vector2(0.5f, -0.5f), new Vector2(0.7f, 0.1f), new Vector2(0.3f, 0.6f) };
                // Pappus: lateral area of each frustum, plus the top cap (the profile leaves the axis there).
                double a = 0;
                for (int i = 0; i + 1 < prof.Length; i++)
                    a += Math.PI * (prof[i].X + prof[i + 1].X) * Vector2.Distance(prof[i], prof[i + 1]);
                a += Math.PI * 0.3 * 0.3;
                return new Spec(new Lathe(prof, LatheMode.Linear, M), Vector3.Zero, 0.95f, null, null, null, a);
            }
            case "lathe-spline":
            {
                var prof = new[] { new Vector2(0.3f, -0.7f), new Vector2(0.8f, -0.2f), new Vector2(0.6f, 0.2f), new Vector2(0.5f, 0.7f) };
                return new Spec(new Lathe(prof, LatheMode.CatmullRom, M), Vector3.Zero, 1.1f, null, null, null, null);
            }
            case "extrusion":
            {
                // An L-shaped section, capped: perimeter × height + 2 × polygon area.
                var prof = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, -0.2f),
                                   new Vector2(-0.2f, -0.2f), new Vector2(-0.2f, 0.5f), new Vector2(-0.5f, 0.5f) };
                double per = 0, area2 = 0;
                for (int i = 0; i < prof.Length; i++)
                {
                    var p0 = prof[i]; var p1 = prof[(i + 1) % prof.Length];
                    per += Vector2.Distance(p0, p1);
                    area2 += p0.X * p1.Y - p1.X * p0.Y;
                }
                double h = 0.8;
                var shape = new Extrusion(prof, ExtrusionMode.Linear, (float)h, ExtrusionCaps.Both, M);
                return new Spec(shape, new Vector3(0, 0.4f, 0), 0.95f, null, null, null, per * h + Math.Abs(area2));
            }
            case "mesh":
            {
                var tris = Icosphere(0.8f, 2, out double area);
                return new Spec(new Mesh(tris.Cast<IHittable>().ToList(), M), Vector3.Zero, 0.85f,
                                null, null, null, area);
            }
        }
        throw new ArgumentException(name);
    }

    /// <summary>A transformed shape: implicit pulled back through the matrix, area by quadrature of the Jacobian.</summary>
    private static Spec Transformed(IHittable inner, Func<Vector3, float> objImplicit, Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inv);
        var normalMatrix = Matrix4x4.Transpose(inv);
        Func<Vector3, float> f = p => objImplicit(Vector3.Transform(p, inv));
        Func<Vector3, Vector3> outward = p =>
            Vector3.Normalize(Vector3.TransformNormal(Gradient(objImplicit, Vector3.Transform(p, inv)), normalMatrix));
        // World area = ∫ |det M| |M⁻ᵀ n| dA over the object's surface, by its own uniform samples.
        var samplable = (ISamplable)inner;
        MathUtils.ReseedThread(99);
        float det = MathF.Abs(m.GetDeterminant());
        double sum = 0;
        const int n = 400_000;
        for (int i = 0; i < n; i++)
        {
            var (_, nrm, _, a) = samplable.Sample();
            sum += a * det * Vector3.TransformNormal(nrm, normalMatrix).Length();
        }
        var box = Transform.ComputeWorldBox(inner.BoundingBox(), m);
        Vector3 c = 0.5f * (box.Min + box.Max);
        float r = 0.5f * (box.Max - box.Min).Length() * 1.02f;
        return new Spec(new Transform(inner, m), c, r, f, null, outward, sum / n);
    }

    private static float? PlaneHit(Ray ray, Vector3 p0, Vector3 n, Func<Vector3, bool> inside)
    {
        double denom = Vector3.Dot(n, ray.Direction);
        if (Math.Abs(denom) < 1e-12) return null;
        double t = Vector3.Dot(n, p0 - ray.Origin) / denom;
        if (t <= 1e-4) return null;
        return inside(ray.At((float)t)) ? (float)t : null;
    }

    private static bool InParallelogram(Vector3 w, Vector3 u, Vector3 v)
    {
        Vector3 n = Vector3.Cross(u, v);
        float nn = n.LengthSquared();
        float a = Vector3.Dot(Vector3.Cross(w, v), n) / nn, b = Vector3.Dot(Vector3.Cross(u, w), n) / nn;
        return a >= 0 && a <= 1 && b >= 0 && b <= 1;
    }

    private static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        return Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= 0 && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= 0
            && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= 0;
    }

    internal static List<Triangle> Icosphere(float radius, int subdivisions, out double area)
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6),
            (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10),
            (8, 6, 7), (9, 8, 1),
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (mid.TryGetValue(key, out int i)) return i;
                v.Add((v[a] + v[b]) / 2);
                return mid[key] = v.Count - 1;
            }
            var nf = new List<(int, int, int)>();
            foreach (var (a, b, c) in f)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.Add((a, ab, ca)); nf.Add((b, bc, ab)); nf.Add((c, ca, bc)); nf.Add((ab, bc, ca));
            }
            f = nf;
        }
        var pts = v.Select(p => radius * Vector3.Normalize(p)).ToArray();
        area = 0;
        var tris = new List<Triangle>();
        foreach (var (a, b, c) in f)
        {
            area += 0.5 * Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]).Length();
            tris.Add(new Triangle(pts[a], pts[b], pts[c], M));
        }
        return tris;
    }

    // ── Reference first hit ─────────────────────────────────────────────────

    /// <summary>
    /// First sign change of <paramref name="f"/> along the ray, by scanning and
    /// bisection, or null; <paramref name="minAbs"/> is the smallest |f| met
    /// (a grazing ray touches the surface without a sign change).
    /// </summary>
    private static float? ScanFirstRoot(Func<Vector3, float> f, Ray ray, float tMax, out float minAbs)
    {
        const float step = 2e-3f;
        float t0 = 1e-4f, f0 = f(ray.At(t0));
        minAbs = MathF.Abs(f0);
        for (float t1 = t0 + step; t1 <= tMax; t1 += step)
        {
            float f1 = f(ray.At(t1));
            minAbs = MathF.Min(minAbs, MathF.Abs(f1));
            if ((f0 < 0f) != (f1 < 0f))
            {
                float a = t0, b = t1, fa = f0;
                for (int k = 0; k < 40; k++)
                {
                    float m = 0.5f * (a + b), fm = f(ray.At(m));
                    if ((fa < 0f) == (fm < 0f)) { a = m; fa = fm; } else b = m;
                }
                return 0.5f * (a + b);
            }
            t0 = t1; f0 = f1;
        }
        return null;
    }

    private static Vector3 RandomDir(Random rng)
    {
        float z = 2f * rng.NextSingle() - 1f, phi = 2f * MathF.PI * rng.NextSingle();
        float s = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(s * MathF.Cos(phi), s * MathF.Sin(phi), z);
    }

    [Theory]
    [MemberData(nameof(Closed))]
    [MemberData(nameof(Flat))]
    public void FirstHit_MatchesTheReference_FromOutsideAndInside(string name)
    {
        var s = Make(name);
        var rng = new Random(name.GetHashCode() & 0x7fffffff);
        int rays = 0, holes = 0, phantoms = 0, wrongT = 0, wrongFace = 0, wrongNormal = 0, offSurface = 0;
        string first = "";
        void Fail(string what) { if (first == "") first = what; }

        for (int i = 0; i < 3000; i++)
        {
            Vector3 o, d;
            bool inside = s.Implicit != null && i % 3 == 2;
            if (inside)
            {
                // A point inside the solid, a random direction.
                do o = s.Center + s.Radius * new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1);
                while (s.Implicit!(o) >= -1e-3f);
                d = RandomDir(rng);
            }
            else
            {
                o = s.Center + 2.5f * s.Radius * RandomDir(rng);
                Vector3 target = s.Center + 0.9f * s.Radius * new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1);
                d = Vector3.Normalize(target - o);
            }
            var ray = new Ray(o, d);
            float tMax = 6f * s.Radius;
            float minAbs = 0f;
            float? tRef = s.Implicit != null ? ScanFirstRoot(s.Implicit, ray, tMax, out minAbs) : s.PlaneHit!(ray);
            var rec = new HitRecord();
            bool hit = s.Shape.Hit(ray, 1e-4f, tMax, ref rec);
            rays++;
            float tol = 2e-3f * s.Radius;
            if (tRef is float tr && !hit)
            {
                // A ray that only grazes the surface may be missed.
                bool grazing = s.Implicit != null && ScanDepth(s.Implicit, ray, tr) < 1e-3f * s.Radius;
                if (!grazing) { holes++; Fail($"hole: o={o} d={d} tRef={tr}"); }
                continue;
            }
            if (tRef == null && hit)
            {
                bool grazing = s.Implicit != null && minAbs < 1e-3f * s.Radius;
                if (!grazing && !(s.PlaneHit != null && MathF.Abs(Vector3.Dot(s.Outward!(rec.Point), d)) < 1e-3f))
                { phantoms++; Fail($"phantom: o={o} d={d} t={rec.T}"); }
                continue;
            }
            if (!hit) continue;
            if (MathF.Abs(rec.T - tRef!.Value) > tol) { wrongT++; Fail($"t {rec.T} vs {tRef}: o={o} d={d}"); continue; }
            if ((rec.Point - ray.At(rec.T)).Length() > 1e-4f * s.Radius) { offSurface++; Fail($"point off the ray at {rec.Point}"); }
            if (s.Implicit != null && MathF.Abs(s.Implicit(rec.Point)) > 1e-3f * s.Radius) { offSurface++; Fail($"F(P)={s.Implicit(rec.Point)} at {rec.Point}"); }
            Vector3 outward = s.Outward!(rec.Point);
            // Skip edges and corners, where the outward normal is not defined.
            if (s.Implicit != null && !Smooth(s, rec.Point)) continue;
            bool fromOutside = Vector3.Dot(outward, d) < 0f;
            if (MathF.Abs(Vector3.Dot(outward, d)) < 1e-3f) continue;   // grazing: either side
            if (rec.FrontFace != fromOutside) { wrongFace++; Fail($"FrontFace {rec.FrontFace} with outward {outward} d={d} at {rec.Point}"); }
            if (Vector3.Dot(rec.Normal, d) > 1e-4f) { wrongNormal++; Fail($"normal {rec.Normal} along the ray {d} at {rec.Point}"); }
            if (MathF.Abs(Vector3.Dot(rec.Normal, outward)) < 0.999f)
            { wrongNormal++; Fail($"normal {rec.Normal} vs outward {outward} at {rec.Point}"); }
        }
        _out.WriteLine($"{name}: {rays} rays, holes {holes}, phantoms {phantoms}, wrong t {wrongT}, off surface {offSurface}, " +
                       $"wrong FrontFace {wrongFace}, wrong normal {wrongNormal}. {first}");
        Assert.True(holes + phantoms + wrongT + offSurface + wrongFace + wrongNormal == 0, first);
    }

    /// <summary>Away from edges: the implicit's gradient keeps its direction around the point.</summary>
    private static bool Smooth(Spec s, Vector3 p)
    {
        Vector3 n = s.Outward!(p);
        float h = 4e-3f * s.Radius;
        foreach (var e in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ })
            if (Vector3.Dot(s.Outward!(p + h * e), n) < 0.995f) return false;
        return true;
    }

    /// <summary>How deep the ray goes below the surface around a root (0 for a tangent touch).</summary>
    private static float ScanDepth(Func<Vector3, float> f, Ray ray, float tRoot)
    {
        float deepest = 0f;
        for (float t = tRoot; t < tRoot + 0.2f; t += 1e-3f) deepest = MathF.Min(deepest, f(ray.At(t)));
        return -deepest;
    }

    // ── Area and area sampling ──────────────────────────────────────────────

    /// <summary>A smooth test function over the surface (relative to the spec's frame).</summary>
    private static double TestF(Spec s, Vector3 p)
    {
        Vector3 q = (p - s.Center) / s.Radius;
        return 1.0 + 0.5 * Math.Sin(3 * q.X) + 0.3 * q.Y * q.Y + 0.4 * Math.Exp(q.Z);
    }

    /// <summary>
    /// Cauchy-Crofton estimates over isotropic uniform lines through the
    /// bounding sphere: E[#hits] = A / (2πR²), E[Σ f(hits)] = ∫f dA / (2πR²).
    /// Returns (area, its standard error, ∫f dA, its standard error).
    /// </summary>
    private static (double A, double SeA, double If, double SeIf) Crofton(Spec s, int lines, int seed)
    {
        var rng = new Random(seed);
        double sumN = 0, sumN2 = 0, sumF = 0, sumF2 = 0;
        float R = s.Radius;
        for (int i = 0; i < lines; i++)
        {
            Vector3 d = RandomDir(rng);
            MathUtils.OrthonormalBasis(d, out Vector3 t, out Vector3 b);
            float r = R * MathF.Sqrt(rng.NextSingle()), phi = 2f * MathF.PI * rng.NextSingle();
            Vector3 o = s.Center + r * (MathF.Cos(phi) * t + MathF.Sin(phi) * b) - 2f * R * d;
            var ray = new Ray(o, d);
            int n = 0;
            double fsum = 0;
            float tMin = 1e-5f;
            for (int k = 0; k < 64; k++)
            {
                var rec = new HitRecord();
                if (!s.Shape.Hit(ray, tMin, 4f * R, ref rec)) break;
                n++;
                fsum += TestF(s, rec.Point);
                tMin = rec.T + 1e-5f * R;
            }
            sumN += n; sumN2 += n * n; sumF += fsum; sumF2 += fsum * fsum;
        }
        double k2 = 2 * Math.PI * R * R;
        double mN = sumN / lines, mF = sumF / lines;
        double seN = Math.Sqrt(Math.Max(0, sumN2 / lines - mN * mN) / lines);
        double seF = Math.Sqrt(Math.Max(0, sumF2 / lines - mF * mF) / lines);
        return (k2 * mN, k2 * seN, k2 * mF, k2 * seF);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Area_ByCrofton_MatchesTheClosedFormAndSurfaceArea(string name)
    {
        var s = Make(name);
        var (a, se, _, _) = Crofton(s, 200_000, 17);
        var samplable = (ISamplable)s.Shape;
        float own = samplable.SurfaceArea;
        double reference = s.Area ?? own;
        _out.WriteLine($"{name}: Crofton {a:F4} ± {se:F4}, closed form {s.Area?.ToString("F4") ?? "-"}, SurfaceArea {own:F4}" +
                       (samplable.UniformArea ? "" : " (not uniform: approximate)"));
        Assert.True(Math.Abs(a - reference) < 5 * se + 2e-3 * reference, $"Crofton {a} ± {se} vs {reference}");
        // A sampler that is not uniform by area reports its density per point
        // (AreaPdf); its SurfaceArea only weighs lights by power.
        if (samplable.UniformArea)
            Assert.True(Math.Abs(own - reference) < 2e-3 * reference, $"SurfaceArea {own} vs {reference}");
    }

    [Theory]
    [MemberData(nameof(All))]
    public void AreaSampling_IntegratesLikeCrofton_OnTheSurface(string name)
    {
        // ∫ f dA from the samples (each weighted by the area it reports, the
        // inverse of its density) against Crofton's: a non-uniform sampler
        // that reports a uniform area, or samples off the surface, shows here.
        var s = Make(name);
        var samplable = (ISamplable)s.Shape;
        var (_, _, ic, seC) = Crofton(s, 200_000, 23);
        MathUtils.ReseedThread(5);
        const int n = 200_000;
        double sum = 0, sum2 = 0, sumStrat = 0;
        int off = 0, badNormal = 0, badUv = 0, badPdf = 0;
        string first = "";
        for (int i = 0; i < n; i++)
        {
            var (p, nrm, uv, area) = samplable.Sample();
            double x = area * TestF(s, p);
            sum += x; sum2 += x * x;
            var (ps, _, _, areaS) = samplable.SampleStratified(i % 16, 16);
            sumStrat += areaS * TestF(s, ps);
            if (i % 50 != 0) continue;
            // The density the sampler reports for this point is the inverse of its area.
            float pdf = samplable.AreaPdf(p, nrm);
            if (MathF.Abs(pdf * area - 1f) > 1e-3f) { badPdf++; if (first == "") first = $"AreaPdf {pdf} vs 1/area {1f / area} at {p}"; }
            // On the surface, outward normal, the UV a hit there reports.
            if (s.Implicit != null && MathF.Abs(s.Implicit(p)) > 1e-3f * s.Radius) { off++; if (first == "") first = $"F={s.Implicit(p)} at {p}"; }
            if (s.Outward != null && s.Implicit != null && Smooth(s, p) && Vector3.Dot(nrm, s.Outward(p)) < 0.999f)
            { badNormal++; if (first == "") first = $"normal {nrm} vs {s.Outward(p)} at {p}"; }
            var rec = new HitRecord();
            Vector3 o = p + 1e-2f * s.Radius * nrm;
            if (!s.Shape.Hit(new Ray(o, -nrm), 1e-5f, 2e-2f * s.Radius, ref rec) || (rec.Point - p).Length() > 2e-3f * s.Radius)
            { off++; if (first == "") first = $"no surface under the sample {p} (normal {nrm})"; continue; }
            float du = MathF.Abs(rec.U - uv.X); du = MathF.Min(du, 1f - du);
            if (Smooth2(s, p) && (du > 2e-3f || MathF.Abs(rec.V - uv.Y) > 2e-3f))
            { badUv++; if (first == "") first = $"uv {uv} vs hit ({rec.U}, {rec.V}) at {p}"; }
        }
        double mean = sum / n, se = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean) / n), meanStrat = sumStrat / n;
        _out.WriteLine($"{name}: ∫f samples {mean:F4} ± {se:F4}, stratified {meanStrat:F4}, Crofton {ic:F4} ± {seC:F4}; " +
                       $"off surface {off}, normals {badNormal}, uv {badUv}, pdf {badPdf}. {first}");
        Assert.True(Math.Abs(mean - ic) < 5 * Math.Sqrt(se * se + seC * seC) + 2e-3 * ic, $"samples {mean} vs Crofton {ic}");
        Assert.True(Math.Abs(meanStrat - ic) < 5 * Math.Sqrt(se * se + seC * seC) + 2e-3 * ic, $"stratified {meanStrat} vs Crofton {ic}");
        Assert.True(off == 0 && badNormal == 0 && badUv == 0 && badPdf == 0, first);
    }

    /// <summary>Away from edges for the UV check (flat shapes have none inside).</summary>
    private static bool Smooth2(Spec s, Vector3 p) => s.Implicit == null || Smooth(s, p);
}
