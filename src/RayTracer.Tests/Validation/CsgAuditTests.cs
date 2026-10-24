using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// CSG against an exact reference (PIANO-REVIEW.md Phase 2): the solids are
/// spheres and axis-aligned boxes, whose spans along a ray are known in
/// closed form, so the spans of a Boolean tree are the set operations of
/// those intervals. The first boundary of the result past tMin is the
/// reference hit, and the ray enters the result there iff the span starts
/// there. Coincident faces are the hard part (a box flush with another's
/// face, two boxes touching): in the reference two spans that touch are one
/// span, and a span of zero length is no span.
/// </summary>
public class CsgAuditTests
{
    private readonly ITestOutputHelper _out;
    public CsgAuditTests(ITestOutputHelper output) => _out = output;

    private static readonly IMaterial M = new Lambertian(new Vector3(0.5f));

    // ── Reference solids: a hittable and its exact spans along a ray ────────

    private abstract record Solid
    {
        public abstract IHittable Build();
        public abstract List<(double A, double B)> Spans(Vector3 o, Vector3 d);
    }

    private sealed record Ball(Vector3 C, float R) : Solid
    {
        public override IHittable Build() => new Sphere(C, R, M);
        public override List<(double, double)> Spans(Vector3 o, Vector3 d)
        {
            double ox = o.X - C.X, oy = o.Y - C.Y, oz = o.Z - C.Z;
            double a = (double)d.X * d.X + (double)d.Y * d.Y + (double)d.Z * d.Z;
            double hb = ox * d.X + oy * d.Y + oz * d.Z;
            double c = ox * ox + oy * oy + oz * oz - (double)R * R;
            double disc = hb * hb - a * c;
            if (disc <= 0) return new();
            double s = Math.Sqrt(disc);
            return new() { ((-hb - s) / a, (-hb + s) / a) };
        }
    }

    private sealed record Cuboid(Vector3 Min, Vector3 Max) : Solid
    {
        public override IHittable Build()
            => new Transform(new Box(M), Matrix4x4.CreateScale(Max - Min) * Matrix4x4.CreateTranslation(0.5f * (Min + Max)));
        public override List<(double, double)> Spans(Vector3 o, Vector3 d)
        {
            double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
            for (int k = 0; k < 3; k++)
            {
                double ok = k == 0 ? o.X : k == 1 ? o.Y : o.Z, dk = k == 0 ? d.X : k == 1 ? d.Y : d.Z;
                double lo = k == 0 ? Min.X : k == 1 ? Min.Y : Min.Z, hi = k == 0 ? Max.X : k == 1 ? Max.Y : Max.Z;
                if (dk == 0) { if (ok < lo || ok > hi) return new(); continue; }
                double a = (lo - ok) / dk, b = (hi - ok) / dk;
                if (a > b) (a, b) = (b, a);
                t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            }
            return t1 > t0 ? new() { (t0, t1) } : new();
        }
    }

    private sealed record Op(CsgOperation Kind, Solid L, Solid R) : Solid
    {
        public override IHittable Build() => new CsgObject(Kind, L.Build(), R.Build());
        public override List<(double, double)> Spans(Vector3 o, Vector3 d)
            => Combine(Kind, L.Spans(o, d), R.Spans(o, d));
    }

    private const double Tol = 1e-6;

    // Set operations on sorted span lists, by sweeping their end points.
    private static List<(double, double)> Combine(CsgOperation kind, List<(double A, double B)> a, List<(double A, double B)> b)
    {
        var points = new SortedSet<double>();
        foreach (var s in a) { points.Add(s.A); points.Add(s.B); }
        foreach (var s in b) { points.Add(s.A); points.Add(s.B); }
        var p = points.ToList();
        bool In(List<(double A, double B)> spans, double t) => spans.Any(s => t > s.A && t < s.B);
        var result = new List<(double A, double B)>();
        for (int i = 0; i + 1 < p.Count; i++)
        {
            double mid = 0.5 * (p[i] + p[i + 1]);
            bool ia = In(a, mid), ib = In(b, mid);
            bool inside = kind switch
            {
                CsgOperation.Union => ia || ib,
                CsgOperation.Intersection => ia && ib,
                _ => ia && !ib,
            };
            if (!inside || p[i + 1] - p[i] < Tol) continue;
            if (result.Count > 0 && Math.Abs(result[^1].B - p[i]) < Tol) result[^1] = (result[^1].A, p[i + 1]);
            else result.Add((p[i], p[i + 1]));
        }
        return result;
    }

    // ── Cases ────────────────────────────────────────────────────────────────

    public static TheoryData<string> Cases => new()
    {
        "union-spheres", "intersection-sphere-box", "subtraction-box-sphere",
        "subtraction-flush-pocket", "union-touching-boxes", "intersection-shared-face",
        "subtraction-touching-outside", "nested-flush", "subtraction-identical",
    };

    private static Solid Case(string name) => name switch
    {
        "union-spheres" => new Op(CsgOperation.Union, new Ball(new(-0.4f, 0, 0), 0.7f), new Ball(new(0.4f, 0.1f, 0), 0.6f)),
        "intersection-sphere-box" => new Op(CsgOperation.Intersection, new Ball(Vector3.Zero, 1f), new Cuboid(new(-0.7f), new(0.7f))),
        "subtraction-box-sphere" => new Op(CsgOperation.Subtraction, new Cuboid(new(-0.7f), new(0.7f)), new Ball(Vector3.Zero, 0.9f)),
        // A pocket open at the top: B's top face lies in A's top face.
        "subtraction-flush-pocket" => new Op(CsgOperation.Subtraction, new Cuboid(new(-1f), new(1f)),
                                             new Cuboid(new(-0.5f, 0f, -0.5f), new(0.5f, 1f, 0.5f))),
        // Two boxes sharing the face x = 0: the shared face is inside the union.
        "union-touching-boxes" => new Op(CsgOperation.Union, new Cuboid(new(-1f), new(0f, 1f, 1f)),
                                         new Cuboid(new(0f, -0.5f, -0.5f), new(1f, 0.5f, 0.5f))),
        // B's bottom face lies in A's bottom face.
        "intersection-shared-face" => new Op(CsgOperation.Intersection, new Cuboid(new(-1f), new(1f)),
                                             new Cuboid(new(-0.5f, -1f, -0.5f), new(0.5f, 0.3f, 0.5f))),
        // B touches A from outside: A's face x = 0 stays.
        "subtraction-touching-outside" => new Op(CsgOperation.Subtraction, new Cuboid(new(-1f), new(0f, 1f, 1f)),
                                                 new Cuboid(new(0f, -0.5f, -0.5f), new(1f, 0.5f, 0.5f))),
        "nested-flush" => new Op(CsgOperation.Subtraction,
                                 new Op(CsgOperation.Union, new Cuboid(new(-1f), new(1f)), new Ball(new(0, 1f, 0), 0.6f)),
                                 new Cuboid(new(-0.3f, -1f, -0.3f), new(0.3f, 1.8f, 0.3f))),
        "subtraction-identical" => new Op(CsgOperation.Subtraction, new Cuboid(new(-1f), new(1f)), new Cuboid(new(-1f), new(1f))),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FirstHit_MatchesTheExactSpans(string name)
    {
        // A stable seed per case (string.GetHashCode changes per process).
        for (int seed = 0; seed < 8; seed++)
            Check(name, name.Aggregate(seed * 7919, (h, c) => h * 31 + c) & 0x7fffffff);
    }

    private void Check(string name, int seed)
    {
        Solid solid = Case(name);
        IHittable h = solid.Build();
        var rng = new Random(seed);
        int rays = 0, wrongHit = 0, wrongT = 0, wrongFace = 0, wrongNormal = 0, skipped = 0;
        string first = "";
        const float tMin = 1e-4f;
        for (int i = 0; i < 20000; i++)
        {
            Vector3 R() => new(rng.NextSingle() * 4 - 2, rng.NextSingle() * 4 - 2, rng.NextSingle() * 4 - 2);
            Vector3 o = i % 2 == 0 ? 2f * R() : 0.6f * R();
            // Aim at a point on a coordinate plane of the boxes half the time,
            // so many rays cross the coincident faces and the edges.
            Vector3 target = 0.6f * R();
            if (i % 4 == 1) target.Y = rng.Next(3) - 1f;
            if (i % 4 == 3) target.X = 0f;
            Vector3 d = Vector3.Normalize(target - o);
            // A ray within a degree of the faces of the boxes crosses them at
            // distances float arithmetic cannot pin down: features thinner
            // than the CSG's grouping tolerance across the surface (about
            // 1e-5 here) are merged there, by design.
            Vector3 ad = Vector3.Abs(d);
            if (MathF.Min(ad.X, MathF.Min(ad.Y, ad.Z)) < 0.02f) { skipped++; continue; }

            var spans = solid.Spans(o, d);
            double? tRef = null; bool entering = false;
            foreach (var s in spans)
            {
                if (s.A > tMin) { tRef = s.A; entering = true; break; }
                if (s.B > tMin) { tRef = s.B; entering = false; break; }
            }
            // Leave out a ray whose reference sits within a tolerance of tMin,
            // or with a span shorter than 1e-3 (a sliver at an edge, merged
            // when it is thinner than the grouping tolerance across the face).
            if (tRef is double tr && (Math.Abs(tr - tMin) < 1e-4 || spans.Any(s => s.B - s.A < 1e-3)))
            { skipped++; continue; }

            rays++;
            var rec = new HitRecord();
            bool hit = h.Hit(new Ray(o, d), tMin, 100f, ref rec);
            void Fail(string what) { if (first == "") first = $"{what}: o={o}, d={d}, ref={tRef}, got={(hit ? rec.T : float.NaN)}"; }
            if (hit != tRef.HasValue) { wrongHit++; Fail("hit"); continue; }
            if (!hit) continue;
            if (Math.Abs(rec.T - tRef!.Value) > 1e-4 * (1 + tRef.Value)) { wrongT++; Fail("t"); continue; }
            if (rec.FrontFace != entering) { wrongFace++; Fail("front face"); }
            if (Vector3.Dot(rec.Normal, d) >= 0f) { wrongNormal++; Fail("normal"); }
        }
        _out.WriteLine($"{name} (seed {seed}): {rays} rays ({skipped} left out): {wrongHit} hit/miss, {wrongT} distance, {wrongFace} front face, {wrongNormal} normal side wrong. {first}");
        Assert.True(rays > 15000);
        Assert.Equal(0, wrongHit + wrongT + wrongFace + wrongNormal);
    }
}
