using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The heightfield against its own definition (PIANO-REVIEW.md Phase 2): the
/// bilinear interpolation of the samples, intersected by scanning the ray
/// independently. Terrains with plateaus at the lowest level (where a ray
/// used to leave through the bottom of the bounding box without a hit),
/// spikes narrower than a cell, and grazing rays.
/// </summary>
public class HeightFieldAuditTests
{
    private readonly ITestOutputHelper _out;
    public HeightFieldAuditTests(ITestOutputHelper output) => _out = output;

    private const float X0 = -1f, Z0 = -1.5f, X1 = 1.5f, Z1 = 1f, Scale = 0.6f;
    private const int N = 17;

    private static float[] Samples(string kind)
    {
        var s = new float[N * N];
        var rng = new Random(4);
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float x = i / (N - 1f), z = j / (N - 1f);
                s[i + j * N] = kind switch
                {
                    // Waves clipped at 0: wide plateaus at the lowest level.
                    "plateaus" => MathF.Max(0f, MathF.Sin(7f * x) * MathF.Cos(5f * z)),
                    // Isolated one-sample spikes on a flat ground.
                    "spikes" => rng.NextSingle() < 0.08f ? 1f : 0.1f,
                    _ => 0.5f + 0.5f * MathF.Sin(9f * x + 3f * z) * MathF.Cos(4f * z),
                };
            }
        return s;
    }

    private static float Height(float[] s, float x, float z)
    {
        float u = (x - X0) / (X1 - X0) * (N - 1), v = (z - Z0) / (Z1 - Z0) * (N - 1);
        int i = Math.Clamp((int)MathF.Floor(u), 0, N - 2), j = Math.Clamp((int)MathF.Floor(v), 0, N - 2);
        float fu = u - i, fv = v - j;
        float a = s[i + j * N] + (s[i + 1 + j * N] - s[i + j * N]) * fu;
        float b = s[i + (j + 1) * N] + (s[i + 1 + (j + 1) * N] - s[i + (j + 1) * N]) * fu;
        return (a + (b - a) * fv) * Scale;
    }

    private static bool Inside(Vector3 p) => p.X >= X0 && p.X <= X1 && p.Z >= Z0 && p.Z <= Z1;

    /// <summary>First t where the ray goes from above the surface to on or below it, inside the bounds.</summary>
    private static float? Reference(float[] s, Ray ray, float tMax, out bool fromAbove)
    {
        fromAbove = true;
        const float step = 2e-4f;
        float? prevF = null;
        float prevT = 0f;
        for (float t = 1e-4f; t <= tMax; t += step)
        {
            Vector3 p = ray.At(t);
            if (!Inside(p)) { prevF = null; continue; }
            float f = p.Y - Height(s, p.X, p.Z);
            if (prevF is float pf && (pf > 0f) != (f > 0f))
            {
                fromAbove = pf > 0f;
                float a = prevT, b = t;
                for (int k = 0; k < 40; k++)
                {
                    float m = 0.5f * (a + b);
                    Vector3 q = ray.At(m);
                    float fm = q.Y - Height(s, q.X, q.Z);
                    if ((fm > 0f) == (pf > 0f)) a = m; else b = m;
                }
                return 0.5f * (a + b);
            }
            prevF = f; prevT = t;
        }
        return null;
    }

    [Theory]
    [InlineData("waves")]
    [InlineData("plateaus")]
    [InlineData("spikes")]
    public void Hit_MatchesTheBilinearSurface(string kind)
    {
        var s = Samples(kind);
        var hf = new HeightField(X0, Z0, X1, Z1, s, N, N, Scale, new Lambertian(new Vector3(0.5f)));
        var rng = new Random(kind.Length);
        int holes = 0, phantoms = 0, wrongT = 0, wrongNormal = 0, rays = 0;
        string first = "";
        for (int i = 0; i < 4000; i++)
        {
            // From above, toward a point of the terrain; a third of them grazing.
            var target = new Vector3(X0 + (X1 - X0) * rng.NextSingle(), 0f, Z0 + (Z1 - Z0) * rng.NextSingle());
            target.Y = Height(s, target.X, target.Z);
            float elev = i % 3 == 0 ? 0.02f + 0.1f * rng.NextSingle() : 0.2f + 1.2f * rng.NextSingle();
            float az = 2f * MathF.PI * rng.NextSingle();
            var dir = Vector3.Normalize(new Vector3(MathF.Cos(az), -MathF.Tan(elev), MathF.Sin(az)));
            var o = target - 3f * dir;
            var ray = new Ray(o, dir);
            float? tr = Reference(s, ray, 6f, out bool fromAbove);
            var rec = new HitRecord();
            bool hit = hf.Hit(ray, 1e-4f, 6f, ref rec);
            rays++;
            if (tr is float t && !hit) { holes++; if (first == "") first = $"hole: o={o} d={dir} tRef={t}"; continue; }
            // A ray running along the surface (within float noise of it) may
            // be counted as hitting anywhere on that stretch.
            bool AlongSurface(float ta, float tb)
            {
                for (float t = MathF.Min(ta, tb); t <= MathF.Max(ta, tb); t += 1e-4f)
                {
                    Vector3 p = ray.At(t);
                    if (!Inside(p) || MathF.Abs(p.Y - Height(s, p.X, p.Z)) > 3e-4f) return false;
                }
                return true;
            }
            if (tr == null && hit)
            {
                Vector3 p = rec.Point;
                if (MathF.Abs(p.Y - Height(s, p.X, p.Z)) > 3e-4f) { phantoms++; if (first == "") first = $"phantom: o={o} d={dir} t={rec.T}"; }
                continue;
            }
            if (!hit) continue;
            // An earlier hit on the surface is a touch the scan stepped over.
            bool touch = rec.T < tr!.Value && MathF.Abs(rec.Point.Y - Height(s, rec.Point.X, rec.Point.Z)) < 3e-4f;
            if (MathF.Abs(rec.T - tr.Value) > 1e-3f && !touch && !AlongSurface(rec.T, tr.Value))
            { wrongT++; if (first == "") first = $"t {rec.T} vs {tr}: o={o} d={dir}"; continue; }
            if (touch) continue;   // the side belongs to the later crossing the scan found
            // FrontFace when the ray arrives from above (a ray entering from the
            // side of the bounds can start under the surface), the shading
            // normal on the side it arrives from.
            if (rec.FrontFace != fromAbove || (rec.Normal.Y > 0f) != fromAbove) { wrongNormal++; if (first == "") first = $"normal {rec.Normal} FrontFace {rec.FrontFace} for d={dir}"; }
        }
        _out.WriteLine($"{kind}: {rays} rays, holes {holes}, phantoms {phantoms}, wrong t {wrongT}, wrong normal {wrongNormal}. {first}");
        Assert.True(holes + phantoms + wrongT + wrongNormal == 0, first);
    }
}
