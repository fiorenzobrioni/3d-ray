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

    // [ThreadStatic] field rather than ThreadLocal<Random>: faster per-access on
    // the hot path. Lazily seeded per thread from an atomic counter (a
    // [ThreadStatic] initializer would only run on the defining thread).
    [ThreadStatic] private static Random? _threadRng;

    public static Random Rng => _threadRng ??= new Random(Interlocked.Increment(ref _globalSeed));

    /// <summary>Replaces the calling thread's PRNG with one seeded deterministically
    /// (used by <c>--seed</c> to make PRNG renders independent of each other).</summary>
    public static void ReseedThread(int seed) => _threadRng = new Random(seed);

    public const float Epsilon = 1e-4f;
    public const float Infinity = float.MaxValue;
    public const float Pi = MathF.PI;
    public const float Inv4Pi = 0.0795774715f; // 1 / (4π)

    public static float DegreesToRadians(float degrees) => degrees * Pi / 180f;

    public static float RadiansToDegrees(float radians) => radians * 180f / Pi;

    /// <summary>
    /// Shadow-terminator softening factor for perturbed shading normals
    /// (Conty &amp; Kulla 2019). When a normal/bump map tilts the shading normal
    /// <paramref name="ns"/> away from the geometric normal <paramref name="ng"/>,
    /// a direction <paramref name="w"/> can sit above the shading horizon while
    /// already below the geometric one, letting the clamped cosine fabricate
    /// energy — surfaces wash out at grazing incidence. This returns a scalar in
    /// [0, 1] that fades the contribution to zero across that band with a smooth
    /// cubic, leaving it untouched when <paramref name="w"/> is well inside both
    /// hemispheres. Identity (1) when the shading normal is unperturbed, so
    /// shading is unaffected wherever no map is present. Deterministic and
    /// allocation-free.
    /// </summary>
    public static float ShadowTerminatorTerm(Vector3 ng, Vector3 ns, Vector3 w)
    {
        float ngDotW = Vector3.Dot(ng, w);
        if (ngDotW <= 0f) return 0f;            // below the true surface horizon

        float nsDotW = Vector3.Dot(ns, w);
        if (nsDotW <= 0f) return 0f;            // below the shading horizon (already culled)

        float ngDotNs = Vector3.Dot(ng, ns);
        if (ngDotNs >= 0.9999f) return 1f;      // unperturbed → no correction

        float g = ngDotW / (nsDotW * MathF.Max(ngDotNs, 1e-4f));
        if (g >= 1f) return 1f;
        if (g <= 0f) return 0f;

        float g2 = g * g;
        return (-g2 * g) + g2 + g;              // smooth cubic ramp, zero-slope at g=1
    }

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

    /// <summary>
    /// Two coordinates of one 2D decision (a direction, a point on a light or
    /// a surface, a lens position). Under Sobol they are the two halves of an
    /// aligned padded block, jointly stratified in 2D; two separate
    /// <see cref="RandomFloat()"/> calls are only stratified one by one.
    /// </summary>
    public static (float U, float V) Random2D() => Sampler.Sample2D();

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
        var (u, v) = Random2D();
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
        var (ra, rb) = Random2D();
        float a = 2f * ra - 1f;
        float b = 2f * rb - 1f;
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

    /// <summary>
    /// Right-handed orthonormal basis (b1, b2, n) around the unit vector
    /// <paramref name="n"/>: Duff et al. 2017, "Building an Orthonormal Basis,
    /// Revisited". Branch-free and accurate to float precision everywhere.
    /// Frisvad's original form, which the engine used before, loses up to 4.5%
    /// of orthonormality near −Z and needed a guard whose fallback basis was
    /// not even orthogonal to n (PIANO-REVIEW.md, Phase 1).
    /// </summary>
    public static void OrthonormalBasis(Vector3 n, out Vector3 b1, out Vector3 b2)
    {
        float sign = MathF.CopySign(1f, n.Z);
        float a = -1f / (sign + n.Z);
        float b = n.X * n.Y * a;
        b1 = new Vector3(1f + sign * n.X * n.X * a, sign * b, -sign * n.X);
        b2 = new Vector3(b, sign + n.Y * n.Y * a, -n.Y);
    }

    /// <summary>Maps a vector from the frame with Z = <paramref name="n"/> to world space.</summary>
    public static Vector3 LocalToWorld(Vector3 local, Vector3 n)
    {
        OrthonormalBasis(n, out Vector3 b1, out Vector3 b2);
        return local.X * b1 + local.Y * b2 + local.Z * n;
    }

    public static Vector3 Reflect(Vector3 v, Vector3 n) =>
        v - 2f * Vector3.Dot(v, n) * n;

    /// <summary>
    /// Refracts the unit direction <paramref name="uv"/> through the normal
    /// <paramref name="n"/> (facing the incoming side) with η_i/η_t. Returns
    /// <see cref="Vector3.Zero"/> under total internal reflection, so callers
    /// must test it: it used to return a meaningless direction there
    /// (PIANO-REVIEW.md §3.32).
    /// </summary>
    public static Vector3 Refract(Vector3 uv, Vector3 n, float etaiOverEtat)
    {
        float cosTheta = MathF.Min(Vector3.Dot(-uv, n), 1f);
        Vector3 rOutPerp = etaiOverEtat * (uv + cosTheta * n);
        float k = 1f - rOutPerp.LengthSquared();
        if (k < 0f) return Vector3.Zero;
        return rOutPerp - MathF.Sqrt(k) * n;
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
    // Ray offsets (PIANO-REVIEW.md, Phase 1).
    //
    // A hit point carries a floating-point error proportional to the size of
    // its coordinates (and of the ray that found it), so the distance a new
    // ray must start off the surface is relative, not a fixed length. The old
    // fixed 1e-4 was larger than a 10 µm scene (black image) and smaller than
    // the float spacing of a scene 10 km from the origin (self-intersection,
    // -44% of the light). Now: RelativeOffset of the largest coordinate
    // (about 256 ulps, the margin of Wächter & Binder, Ray Tracing Gems ch. 6),
    // never less than RayEpsilon, a floor proportional to the scene radius that
    // covers the error of a hit near the origin reached by a ray from far away.
    // SceneScale is per thread: the renderer sets it on its workers and
    // clears it when they are done (pool threads outlive a render).
    // ─────────────────────────────────────────────────────────────────────────
    public const float RelativeOffset = 1f / 32768f;          // 2^-15
    private const float SceneFloorFactor = 1f / 524288f;      // 2^-19

    [ThreadStatic] private static float _sceneScale;

    /// <summary>Scene radius the offsets scale with (1 when unset; 0 unsets).</summary>
    public static float SceneScale
    {
        get => _sceneScale > 0f ? _sceneScale : 1f;
        set => _sceneScale = value;
    }

    /// <summary>Smallest ray offset and ray tMin for the current scene.</summary>
    public static float RayEpsilon => SceneScale * SceneFloorFactor;

    /// <summary>How far a ray spawned at <paramref name="point"/> starts off its surface.</summary>
    public static float OffsetDistance(Vector3 point)
    {
        Vector3 a = Vector3.Abs(point);
        float m = MathF.Max(a.X, MathF.Max(a.Y, a.Z));
        return MathF.Max(RayEpsilon, RelativeOffset * m);
    }

    /// <summary>
    /// Moves a hit point off its surface along <paramref name="normal"/> (the
    /// side the new ray leaves on) by <see cref="OffsetDistance"/>.
    /// </summary>
    public static Vector3 OffsetOrigin(Vector3 point, Vector3 normal)
        => point + normal * OffsetDistance(point);

    /// <summary>
    /// Origin of a ray leaving <paramref name="point"/> in direction
    /// <paramref name="dir"/>: offset along whichever side of the normal the
    /// ray goes to. A shadow ray toward a light behind a transmissive surface
    /// must start on the far side (PIANO-REVIEW.md, Phase 5).
    /// </summary>
    public static Vector3 OffsetToward(Vector3 point, Vector3 normal, Vector3 dir)
        => OffsetOrigin(point, Vector3.Dot(normal, dir) >= 0f ? normal : -normal);
}
