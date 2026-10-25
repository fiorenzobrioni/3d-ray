using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// Physically-grounded sun: a directional emitter with a finite angular
/// radius (the real sun subtends 0.265° half-angle), MIS-correct cone
/// sampling, and optional limb darkening.
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
/// <para><b>Limb darkening.</b> When enabled, the disc is brighter at its
/// centre and darker at its rim (<see cref="LimbFactor"/>), with the same
/// mean.</para>
/// </summary>
public class PhysicalSun : ILight
{
    /// <summary>Unit vector pointing FROM the scene TO the sun, sky-space (Y up).</summary>
    public Vector3 Direction { get; }

    /// <summary>Mean radiance over the disc (limb darkening redistributes it, <see cref="LimbFactor"/>).</summary>
    public Vector3 Color { get; }

    /// <summary>Multiplicative intensity scale; folded into <see cref="Color"/> at NEE time.</summary>
    public float Intensity { get; }

    /// <summary>Disc half-angle in degrees. 0.265° = real Sun.</summary>
    public float AngularRadiusDeg { get; }

    /// <summary>Darken the disc toward its rim (<see cref="LimbFactor"/>).</summary>
    public bool LimbDarkening { get; }

    private readonly float _cosHalfAngle;
    private readonly float _solidAngle;

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
            (xi1, xi2) = MathUtils.Stratum2D(sampleIndex, ShadowSamples);
        }
        else
        {
            (xi1, xi2) = MathUtils.Random2D();
        }

        float cosTheta = 1f - xi1 * (1f - _cosHalfAngle);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * xi2;

        Vector3 local = new(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
        Vector3 toLight = LocalToWorld(local, Direction);

        // Optional surface-normal early-out
        bool hasNormal = surfaceNormal.LengthSquared() > 0f;
        if (hasNormal && !ShadowRay.ReceiverTransmits && Vector3.Dot(surfaceNormal, toLight) <= 0f)
            return (true, Vector3.Zero, toLight, MathUtils.Infinity);

        Vector3 shadowOrigin = MathUtils.OffsetToward(hitPoint, surfaceNormal, toLight);
        var shadowRay = new Ray(shadowOrigin, toLight, time);
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.RayEpsilon, MathUtils.Infinity);
        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, toLight, MathUtils.Infinity);

        Vector3 sampleRadiance = Color * Intensity;
        if (LimbDarkening)
            sampleRadiance *= LimbFactor(cosTheta, _cosHalfAngle);

        // Per-sample contribution: L * Ω / N (radiance × disc solid angle,
        // divided by sample count). The cone PDF is 1/Ω, so dividing by Ω
        // would cancel — but we sample within the cone with PDF 1/Ω, and
        // the integrator wants  L / pdf  =  L · Ω. Then × (1/N) for the
        // mean across N stratified samples.
        Vector3 contribution = sampleRadiance * _solidAngle * trans / ShadowSamples;
        return (false, contribution, toLight, MathUtils.Infinity);
    }

    /// <summary>
    /// Radiance the sun shows in direction <paramref name="dir"/>, zero off its
    /// disc: what a BSDF ray that reaches the sun collects, the same function
    /// the light samples.
    /// </summary>
    public Vector3 RadianceToward(Vector3 dir)
    {
        float len = dir.Length();
        if (len < MathUtils.Epsilon) return Vector3.Zero;
        float cosTheta = Vector3.Dot(dir / len, Direction);
        if (cosTheta < _cosHalfAngle) return Vector3.Zero;
        Vector3 radiance = Color * Intensity;
        return LimbDarkening ? radiance * LimbFactor(cosTheta, _cosHalfAngle) : radiance;
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
    /// Limb darkening of the solar disc, normalised so that its mean over the
    /// disc is 1: the disc delivers the same irradiance with it or without it,
    /// <see cref="Color"/> being the disc's mean radiance. Linear law
    /// I(μ)/I(1) = 1 − u(1 − μ) with u = 0.6 (visible band), μ the cosine of
    /// the angle on the solar surface, √(1 − (r/R)²) at a fraction r/R of the
    /// disc's radius; the mean of μ over the disc is 2/3.
    ///
    /// <para>The law used to take μ as the cosine of the angle in the sky from
    /// the disc's centre, which never drops below cos 0.265° on the real sun:
    /// the darkening did nothing.</para>
    /// </summary>
    public static float LimbFactor(float cosAngle, float cosHalfAngle)
    {
        const float u = 0.6f;
        float r2 = Math.Clamp((1f - cosAngle) / MathF.Max(1e-12f, 1f - cosHalfAngle), 0f, 1f);
        float mu = MathF.Sqrt(1f - r2);
        return (1f - u * (1f - mu)) / (1f - u / 3f);
    }

    private static Vector3 LocalToWorld(Vector3 local, Vector3 w)
    {
        Vector3 result = MathUtils.LocalToWorld(local, w);
        float lenSq = result.LengthSquared();
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : w;
    }

    // ── Emission (bidirectional integrators) ────────────────────────────────

    public bool SampleEmission(in EmissionContext ctx, Vector2 uPos, Vector2 uDir, out LightEmission e)
    {
        Vector3 toLight = EmissionMath.UniformCone(Direction, _cosHalfAngle, uDir);
        var (origin, pdfPos) = EmissionMath.InfiniteOrigin(ctx, toLight, uPos);
        e = new LightEmission
        {
            Position = origin, Direction = -toLight, Normal = Vector3.Zero, Radiance = RadianceToward(toLight),
            PdfPos = pdfPos, PdfDir = 1f / _solidAngle, Cos = 1f, Infinite = true,
        };
        return e.Radiance != Vector3.Zero;
    }

    public float EmissionPdfOverCos(in EmissionContext ctx, Vector3 point, Vector3 dirFromLight)
        => EmissionMath.InfiniteDiscPdf(ctx) * PdfSolidAngle(point, -dirFromLight);
}
