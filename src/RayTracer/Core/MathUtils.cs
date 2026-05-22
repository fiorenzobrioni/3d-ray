using System.Numerics;
using RayTracer.Core.Sampling;

namespace RayTracer.Core;

public static class MathUtils
{
    // ─────────────────────────────────────────────────────────────────────────
    // Thread-local RNG: eliminates lock contention on Random.Shared that was
    // bottlenecking the Parallel.For render loop. Each thread now has its own
    // independent Random instance seeded from an atomic counter.
    // ─────────────────────────────────────────────────────────────────────────
    private static int _globalSeed = Environment.TickCount;

    private static readonly ThreadLocal<Random> _threadRng = new(
        () => new Random(Interlocked.Increment(ref _globalSeed)));

    public static Random Rng => _threadRng.Value!;

    public const float Epsilon = 1e-4f;
    public const float Infinity = float.MaxValue;
    public const float Pi = MathF.PI;
    public const float Inv4Pi = 0.0795774715f; // 1 / (4π)

    public static float DegreesToRadians(float degrees) => degrees * Pi / 180f;

    public static float RadiansToDegrees(float radians) => radians * 180f / Pi;

    /// <summary>
    /// Uniform [0, 1) draw routed through the active per-pixel sampler.
    /// When the Sobol sampler is installed and a per-pixel-sample
    /// context is open (see <see cref="Sampler"/>), draws are
    /// Owen-scrambled and successive calls walk independent dimensions
    /// of the low-discrepancy sequence — typically a 2-5× convergence
    /// improvement at fixed spp on path-traced scenes. When the PRNG
    /// sampler is active or the context is closed (e.g. tests, scene
    /// loader), falls through to the legacy thread-local
    /// <see cref="Random"/>.
    /// </summary>
    public static float RandomFloat() => Sampler.Sample1D();
    public static float RandomFloat(float min, float max) => min + (max - min) * RandomFloat();

    public static Vector3 RandomVector3() =>
        new(RandomFloat(), RandomFloat(), RandomFloat());

    public static Vector3 RandomVector3(float min, float max) =>
        new(RandomFloat(min, max), RandomFloat(min, max), RandomFloat(min, max));

    // ─────────────────────────────────────────────────────────────────────────
    // Direct (non-rejection) samplers.
    //
    // Rejection sampling is poison for low-discrepancy sequences: each
    // rejected attempt burns dimensions, so the dimension count consumed
    // by a single call becomes data-dependent. The Sobol sampler then
    // sees the same logical decision (e.g. "first diffuse scatter
    // direction") map to different dimensions across pixels and samples,
    // destroying the (0,1)-net stratification it relies on. The result
    // looks visibly noisier than plain PRNG, which is the failure mode
    // we hit on the Cornell box at -s 64 -d 8.
    //
    // Each helper below consumes a fixed number of Sample1D draws and
    // is uniform on its domain, so Sobol's per-dimension stratification
    // is preserved across every call.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uniform point on the unit sphere via inverse-CDF sampling.
    /// Consumes exactly two Sample1D draws.
    /// </summary>
    public static Vector3 RandomUnitVector()
    {
        float u = RandomFloat();
        float v = RandomFloat();
        float z = 1f - 2f * u;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        float phi = 2f * Pi * v;
        return new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
    }

    /// <summary>
    /// Uniform point inside the unit ball. Consumes exactly three
    /// Sample1D draws: two for the direction (inverse-CDF on the
    /// sphere) and one for the radius (cube-root for volumetric
    /// uniformity, since p(r) ∝ r²).
    /// </summary>
    public static Vector3 RandomInUnitSphere()
    {
        Vector3 dir = RandomUnitVector();
        float r = MathF.Cbrt(RandomFloat());
        return r * dir;
    }

    public static Vector3 RandomInHemisphere(Vector3 normal)
    {
        var p = RandomUnitVector();
        return Vector3.Dot(p, normal) > 0 ? p : -p;
    }

    /// <summary>
    /// Uniform point inside the unit disk via Shirley's concentric
    /// mapping. Consumes exactly two Sample1D draws.
    /// </summary>
    public static Vector3 RandomInUnitDisk()
    {
        float a = 2f * RandomFloat() - 1f;
        float b = 2f * RandomFloat() - 1f;
        if (a == 0f && b == 0f) return Vector3.Zero;

        float r, phi;
        if (a * a > b * b)
        {
            r = a;
            phi = (Pi / 4f) * (b / a);
        }
        else
        {
            r = b;
            phi = (Pi / 2f) - (Pi / 4f) * (a / b);
        }
        return new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), 0f);
    }

    public static Vector3 Reflect(Vector3 v, Vector3 n) =>
        v - 2f * Vector3.Dot(v, n) * n;

    public static Vector3 Refract(Vector3 uv, Vector3 n, float etaiOverEtat)
    {
        float cosTheta = MathF.Min(Vector3.Dot(-uv, n), 1f);
        Vector3 rOutPerp = etaiOverEtat * (uv + cosTheta * n);
        Vector3 rOutParallel = -MathF.Sqrt(MathF.Abs(1f - rOutPerp.LengthSquared())) * n;
        return rOutPerp + rOutParallel;
    }

    public static float Schlick(float cosine, float refractionIndex)
    {
        float r0 = (1f - refractionIndex) / (1f + refractionIndex);
        r0 *= r0;
        // (1-c)^5 unrolled — MathF.Pow with a constant exponent isn't constant-
        // folded by the JIT, and a transcendental call costs ~5-10× the four
        // multiplies it replaces. Schlick fires on every dielectric/metal hit.
        float x = 1f - cosine;
        float x2 = x * x;
        return r0 + (1f - r0) * x2 * x2 * x;
    }

    /// <summary>
    /// Exact unpolarised Fresnel reflectance for a dielectric interface.
    /// <paramref name="cosThetaI"/> is the incident cosine (|V·N|, always ≥ 0);
    /// <paramref name="eta"/> is ηi/ηt — the ratio of the incident-side IOR to the
    /// transmitted-side IOR. Returns 1 on total internal reflection.
    ///
    /// Schlick's approximation is exposed separately and is fine for metals/
    /// paint/coat, but dielectric glass at grazing angles diverges from Schlick
    /// by several percent — enough to bias a 1024-spp render noticeably — so we
    /// use the full Fresnel equations for the transmission lobe.
    /// </summary>
    public static float FresnelDielectric(float cosThetaI, float eta)
    {
        cosThetaI = Math.Clamp(cosThetaI, 0f, 1f);
        float sin2ThetaI = MathF.Max(0f, 1f - cosThetaI * cosThetaI);
        float sin2ThetaT = eta * eta * sin2ThetaI;
        if (sin2ThetaT >= 1f) return 1f; // TIR
        float cosThetaT = MathF.Sqrt(1f - sin2ThetaT);
        float rParl = (cosThetaI - eta * cosThetaT) / (cosThetaI + eta * cosThetaT);
        float rPerp = (eta * cosThetaI - cosThetaT) / (eta * cosThetaI + cosThetaT);
        return 0.5f * (rParl * rParl + rPerp * rPerp);
    }

    public static bool NearZero(Vector3 v)
    {
        const float s = 1e-8f;
        return MathF.Abs(v.X) < s && MathF.Abs(v.Y) < s && MathF.Abs(v.Z) < s;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Luminance (Rec.709) — used for Russian Roulette survival probability
    // and for weighting specular energy.
    // ─────────────────────────────────────────────────────────────────────────
    public static float Luminance(Vector3 color)
        => 0.2126f * color.X + 0.7152f * color.Y + 0.0722f * color.Z;

    // ─────────────────────────────────────────────────────────────────────────
    // Robust shadow origin: offsets the hit point along the geometric normal
    // to prevent self-intersection. This is more robust than offsetting along
    // the shadow ray direction, especially at grazing angles.
    // ─────────────────────────────────────────────────────────────────────────
    public static Vector3 OffsetOrigin(Vector3 point, Vector3 normal)
        => point + normal * Epsilon;
}
