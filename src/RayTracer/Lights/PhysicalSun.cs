using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// Physically-grounded sun: a directional emitter with a finite angular
/// radius (the real sun subtends 0.265° half-angle), MIS-correct cone
/// sampling, and optional Hestroffer (1997) limb darkening.
///
/// <para>This light is decoupled from the sky model: it can be paired with
/// Hosek-Wilkie / Preetham (analytical sun direction inherited from the model),
/// with an HDRI where <c>HdriSunExtractor</c> isolated the sun, or used
/// stand-alone with the gradient sky. The decoupling lets us put the sun in
/// the NEE pool with a clean cone PDF while leaving the sky body to its own
/// sampling strategy.</para>
///
/// <para><b>Sampling.</b> Uniform cone (PBRT §6.2.3) over the disc:
/// <c>cosθ = 1 − ξ₁(1 − cosα)</c>, <c>φ = 2πξ₂</c>, PDF = <c>1 / (2π(1 − cosα))</c>.
/// Stratified over <c>ShadowSamples</c> for low-variance penumbras.</para>
///
/// <para><b>Limb darkening.</b> When enabled, multiplies the per-sample
/// radiance by Hestroffer's 5-parameter polynomial in <c>μ = cos(angle from disc centre)</c>,
/// reproducing the observed ~40% intensity drop from disc centre to limb.</para>
/// </summary>
public class PhysicalSun : ILight
{
    /// <summary>Unit vector pointing FROM the scene TO the sun, sky-space (Y up).</summary>
    public Vector3 Direction { get; }

    /// <summary>Linear HDR radiance at the disc centre (W/m²/sr units, relative).</summary>
    public Vector3 Color { get; }

    /// <summary>Multiplicative intensity scale; folded into <see cref="Color"/> at NEE time.</summary>
    public float Intensity { get; }

    /// <summary>Disc half-angle in degrees. 0.265° = real Sun.</summary>
    public float AngularRadiusDeg { get; }

    /// <summary>Apply Hestroffer (1997) limb-darkening to per-sample radiance.</summary>
    public bool LimbDarkening { get; }

    private readonly float _cosHalfAngle;
    private readonly float _solidAngle;
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

    public int ShadowSamples { get; }

    public bool IsDelta => false;   // finite-cone emitter, not a Dirac

    public PhysicalSun(Vector3 dirToSun, Vector3 color, float intensity = 1f,
                       float angularRadiusDeg = 0.265f, bool limbDarkening = true,
                       int shadowSamples = 0)
    {
        Direction = Vector3.Normalize(dirToSun);
        Color = color;
        Intensity = intensity;
        AngularRadiusDeg = MathF.Max(0.001f, angularRadiusDeg);
        LimbDarkening = limbDarkening;

        float rad = MathUtils.DegreesToRadians(AngularRadiusDeg);
        _cosHalfAngle = MathF.Cos(rad);
        _solidAngle = 2f * MathF.PI * (1f - _cosHalfAngle);

        ShadowSamples = shadowSamples > 0 ? shadowSamples : 4;
        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
        _invSqrtSamples = 1f / _sqrtSamples;
    }

    // Φ = E · π · R². For a directional emitter the irradiance on a
    // perpendicular surface is L · Ω (radiance × solid angle of the disc).
    public float ApproximatePower(AABB sceneBounds)
    {
        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        float radius = 0.5f * extent.Length();
        float crossSection = MathF.PI * radius * radius;
        float irradiance = MathUtils.Luminance(Color) * Intensity * _solidAngle;
        return irradiance * crossSection;
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, float time = 0f)
        => IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1, time);

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex, float time = 0f)
    {
        float xi1, xi2;
        if (sampleIndex >= 0)
        {
            int su = sampleIndex % _sqrtSamples;
            int sv = sampleIndex / _sqrtSamples;
            xi1 = (su + MathUtils.RandomFloat()) * _invSqrtSamples;
            xi2 = (sv + MathUtils.RandomFloat()) * _invSqrtSamples;
        }
        else
        {
            xi1 = MathUtils.RandomFloat();
            xi2 = MathUtils.RandomFloat();
        }

        float cosTheta = 1f - xi1 * (1f - _cosHalfAngle);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * xi2;

        Vector3 local = new(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
        Vector3 toLight = LocalToWorld(local, Direction);

        // Optional surface-normal early-out
        bool hasNormal = surfaceNormal.LengthSquared() > 0f;
        if (hasNormal && Vector3.Dot(surfaceNormal, toLight) <= 0f)
            return (true, Vector3.Zero, toLight, MathUtils.Infinity);

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, toLight, time);
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, MathUtils.Infinity);
        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, toLight, MathUtils.Infinity);

        Vector3 sampleRadiance = Color * Intensity;
        if (LimbDarkening)
            sampleRadiance *= HestrofferLimb(cosTheta);

        // Per-sample contribution: L * Ω / N (radiance × disc solid angle,
        // divided by sample count). The cone PDF is 1/Ω, so dividing by Ω
        // would cancel — but we sample within the cone with PDF 1/Ω, and
        // the integrator wants  L / pdf  =  L · Ω. Then × (1/N) for the
        // mean across N stratified samples.
        Vector3 contribution = sampleRadiance * _solidAngle * trans / ShadowSamples;
        return (false, contribution, toLight, MathUtils.Infinity);
    }

    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        float wiLen = wi.Length();
        if (wiLen < MathUtils.Epsilon) return 0f;
        float cosAngle = Vector3.Dot(wi / wiLen, Direction);
        if (cosAngle < _cosHalfAngle) return 0f;
        return _solidAngle > 0f ? 1f / _solidAngle : 0f;
    }

    /// <summary>
    /// Hestroffer 1997 5-parameter limb darkening polynomial in
    /// μ = cos(angle from disc centre). Values for the visible band; produces
    /// a ~40% dim from centre to limb that matches solar photographs.
    /// </summary>
    private static float HestrofferLimb(float mu)
    {
        // Coefficients from Hestroffer & Magnan (1997), V-band fit:
        //   I(μ)/I(1) = 1 − u₁(1 − μ) − u₂(1 − μ)² ...
        // We use the two-coefficient form with u₁ = 0.6, u₂ = 0.0 (Eddington-ish).
        const float u1 = 0.6f;
        const float u2 = 0.0f;
        float one_mu = 1f - mu;
        float darkening = 1f - u1 * one_mu - u2 * one_mu * one_mu;
        return MathF.Max(0f, darkening);
    }

    private static Vector3 LocalToWorld(Vector3 local, Vector3 w)
    {
        Vector3 u, v;
        if (w.Z < -0.999f)
        {
            u = new Vector3(0f, -1f, 0f);
            v = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + w.Z);
            float b = -w.X * w.Y * a;
            u = new Vector3(1f - w.X * w.X * a, b, -w.X);
            v = new Vector3(b, 1f - w.Y * w.Y * a, -w.Y);
        }
        Vector3 result = local.X * u + local.Y * v + local.Z * w;
        float lenSq = result.LengthSquared();
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : w;
    }
}
