using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Rendering;

/// <summary>
/// Encapsulates sky/environment rendering configuration.
///
/// Three modes:
///   Flat     — returns a single solid color (for indoor/studio scenes)
///   Gradient — vertical lerp between horizon and zenith, with optional sun disk
///   HDRI     — samples an equirectangular HDR environment map for IBL
///
/// The sky acts as the environment light source: rays that escape the scene
/// sample this to get their color contribution. A richer sky = richer GI.
/// </summary>
public class SkySettings
{
    // ── Mode ────────────────────────────────────────────────────────────────
    public enum SkyMode { Flat, Gradient, Hdri }
    public SkyMode Mode { get; }

    // Convenience properties for Program.cs / logging
    public bool IsGradient => Mode == SkyMode.Gradient;
    public bool IsHdri => Mode == SkyMode.Hdri;

    // ── Flat mode ───────────────────────────────────────────────────────────
    public Vector3 FlatColor { get; }

    // ── Gradient mode ───────────────────────────────────────────────────────
    public Vector3 ZenithColor { get; }
    public Vector3 HorizonColor { get; }
    public Vector3 GroundColor { get; }

    // ── Sun disk (used by gradient mode) ────────────────────────────────────
    public bool HasSun { get; }
    public Vector3 SunDirection { get; }
    public Vector3 SunColor { get; }
    public float SunIntensity { get; }
    public float SunCosAngle { get; }
    public float SunFalloff { get; }

    // ── HDRI mode ───────────────────────────────────────────────────────────
    private readonly EnvironmentMap? _envMap;

    // ─────────────────────────────────────────────────────────────────────────
    //  Constructors
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flat sky — single solid color for all directions.
    /// </summary>
    public SkySettings(Vector3 flatColor)
    {
        Mode = SkyMode.Flat;
        FlatColor = flatColor;
        ZenithColor = flatColor;
        HorizonColor = flatColor;
        GroundColor = flatColor;
    }

    /// <summary>
    /// Gradient sky with optional sun disk.
    /// </summary>
    public SkySettings(
        Vector3 zenithColor,
        Vector3 horizonColor,
        Vector3 groundColor,
        Vector3? sunDirection = null,
        Vector3? sunColor = null,
        float sunIntensity = 10f,
        float sunSizeDeg = 3f,
        float sunFalloff = 32f)
    {
        Mode = SkyMode.Gradient;
        FlatColor = horizonColor;

        ZenithColor = zenithColor;
        HorizonColor = horizonColor;
        GroundColor = groundColor;

        if (sunDirection.HasValue)
        {
            HasSun = true;
            SunDirection = Vector3.Normalize(-sunDirection.Value);
            SunColor = sunColor ?? Vector3.One;
            SunIntensity = sunIntensity;
            SunCosAngle = MathF.Cos(MathUtils.DegreesToRadians(sunSizeDeg * 0.5f));
            SunFalloff = sunFalloff;
        }
    }

    /// <summary>
    /// HDRI sky — environment map loaded from an HDR image file.
    /// </summary>
    public SkySettings(EnvironmentMap envMap)
    {
        Mode = SkyMode.Hdri;
        _envMap = envMap;
        FlatColor = new Vector3(0.5f);
        ZenithColor = FlatColor;
        HorizonColor = FlatColor;
        GroundColor = FlatColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Core method — called by Renderer.CalculateSkyColor()
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the sky radiance for a ray that escaped the scene.
    /// </summary>
    public Vector3 Sample(Ray ray)
    {
        return Mode switch
        {
            SkyMode.Flat     => FlatColor,
            SkyMode.Gradient => SampleGradient(ray),
            SkyMode.Hdri     => _envMap!.Sample(ray.Direction),
            _                => FlatColor
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Gradient sampling
    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 SampleGradient(Ray ray)
    {
        Vector3 dir = Vector3.Normalize(ray.Direction);
        float y = dir.Y;

        Vector3 skyColor;
        if (y >= 0f)
        {
            float t = MathF.Sqrt(MathF.Min(y, 1f));
            skyColor = Vector3.Lerp(HorizonColor, ZenithColor, t);
        }
        else
        {
            float t = MathF.Min(-y * 4f, 1f);
            skyColor = Vector3.Lerp(HorizonColor, GroundColor, t);
        }

        if (HasSun && y > -0.05f)
        {
            float cosAngle = Vector3.Dot(dir, SunDirection);
            if (cosAngle > 0f)
            {
                if (cosAngle >= SunCosAngle)
                {
                    skyColor += SunColor * SunIntensity;
                }
                else
                {
                    float glow = MathF.Pow(cosAngle, SunFalloff);
                    skyColor += SunColor * SunIntensity * glow;
                }
            }
        }

        return skyColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Direct Sampling (Next Event Estimation)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Threshold below which a flat sky's luminance is treated as "off" for NEE.
    /// Avoids registering a fully-black flat sky as a light and wasting shadow rays.
    /// </summary>
    private const float FlatSkyNeeLuminanceThreshold = 1e-6f;

    /// <summary>
    /// True when the sky can be importance-sampled as a direct light source via NEE:
    ///   - HDRI: importance-sampled by the environment map's CDF.
    ///   - Gradient with sun disk: cone-sampled inside the sun.
    ///   - Flat: uniform sphere sampling (matches Cycles/Arnold uniform world background).
    /// A pure gradient without a sun disk is intentionally excluded — its body has no
    /// concentrated radiance peaks, so BSDF importance sampling on the miss path is
    /// already optimal.
    /// </summary>
    public bool CanSampleDirectly =>
        IsHdri ||
        HasSun ||
        (Mode == SkyMode.Flat && MathUtils.Luminance(FlatColor) > FlatSkyNeeLuminanceThreshold);

    /// <summary>
    /// Deterministic estimate of average sky radiance, used by
    /// EnvironmentLight.ApproximatePower() for scene classification in the
    /// Renderer constructor (indirect-dominant detection).
    ///
    /// MUST NOT call MathUtils.RandomFloat() — the constructor runs single-threaded
    /// and must produce identical results across runs for consistent RR parameters.
    ///
    /// Gradient sky: weighted average of zenith/horizon/ground luminance plus the
    /// sun's contribution scaled by its solid angle.
    /// HDRI: delegates to EnvironmentMap.EstimatedAverageLuminance.
    /// Flat: luminance of the flat color (CanSampleDirectly=false, but provided for completeness).
    /// </summary>
    public float EstimatedAverageLuminance
    {
        get
        {
            if (IsHdri && _envMap != null)
                return _envMap.EstimatedAverageLuminance;
 
            if (HasSun)
            {
                // Weighted average over sky hemisphere zones.
                // Zenith covers ~25% of the upper hemisphere, horizon ~50%, ground ~25%.
                float zenithLum  = MathUtils.Luminance(ZenithColor);
                float horizonLum = MathUtils.Luminance(HorizonColor);
                float groundLum  = MathUtils.Luminance(GroundColor);
                float skyAvg = (zenithLum + horizonLum * 2f + groundLum) / 4f;
 
                // Sun contribution: peak radiance × solid angle of the disk.
                // 2π(1 − cosAngle) is the solid angle of a spherical cap.
                float sunSolidAngle = 2f * MathF.PI * (1f - SunCosAngle);
                float sunLum = MathUtils.Luminance(SunColor) * SunIntensity * sunSolidAngle;
 
                return skyAvg + sunLum;
            }
 
            // Flat mode — CanSampleDirectly is false here, but return a value anyway
            // so the method is always safe to call.
            return MathUtils.Luminance(FlatColor);
        }
    }

    /// <summary>
    /// Inverse of the unit sphere's solid angle (4π sr). Used as the PDF for
    /// uniform sphere sampling on a flat sky.
    /// </summary>
    private const float UniformSpherePdf = 1f / (4f * MathF.PI);

    /// <summary>
    /// Samples a direction over the sky for NEE.
    ///   - HDRI: importance-sampled by the environment map's CDF.
    ///   - Gradient with sun disk: cone-sampled inside the sun cap.
    ///   - Flat: uniform on the unit sphere (pdf = 1/(4π)).
    /// </summary>
    /// <returns>The sampled direction, the radiance at that direction, and the solid-angle PDF.</returns>
    public (Vector3 Direction, Vector3 Color, float Pdf) SampleDirectly()
    {
        if (IsHdri && _envMap != null)
        {
            var (dir, pdf) = _envMap.SampleDirection();
            return (dir, _envMap.Sample(dir), pdf);
        }

        if (HasSun)
        {
            // Sample uniformly within the sun's cone.
            float z = 1f - MathUtils.RandomFloat() * (1f - SunCosAngle);
            float sinTheta = MathF.Sqrt(1f - z * z);
            float phi = 2f * MathF.PI * MathUtils.RandomFloat();
            float x = MathF.Cos(phi) * sinTheta;
            float y = MathF.Sin(phi) * sinTheta;

            // Local basis around SunDirection (points FROM scene TO sun).
            Vector3 w = SunDirection;
            Vector3 u = Vector3.Normalize(Vector3.Cross(MathF.Abs(w.X) > 0.1f ? Vector3.UnitY : Vector3.UnitX, w));
            Vector3 v = Vector3.Cross(w, u);

            Vector3 dir = Vector3.Normalize(x * u + y * v + z * w);

            float solidAngle = 2f * MathF.PI * (1f - SunCosAngle);
            float pdf = solidAngle > 0f ? 1f / solidAngle : 1f;

            // Evaluate the full sky (gradient + sun) at the sampled direction so
            // the NEE estimator captures both the sun's peak and the gradient
            // body inside the cone.
            return (dir, SampleGradient(new Ray(Vector3.Zero, dir)), pdf);
        }

        // Flat sky: uniform on the unit sphere. Pairs with PdfSolidAngle below.
        // The shadow-test caller (EnvironmentLight) rejects directions in the
        // surface's lower hemisphere, so wasted samples cost only one Random pair.
        Vector3 randomDir = MathUtils.RandomUnitVector();
        return (randomDir, FlatColor, UniformSpherePdf);
    }

    /// <summary>
    /// Solid-angle PDF of <see cref="SampleDirectly"/> evaluated at the given
    /// direction. Mirrors the sampling strategy: HDRI uses the environment map's
    /// learned PDF, gradient-with-sun uses uniform-cone sampling inside the sun
    /// disk (and 0 elsewhere). Used for MIS balance heuristic when a BSDF-sampled
    /// ray escapes the scene.
    /// </summary>
    public float PdfSolidAngle(Vector3 direction)
    {
        if (!CanSampleDirectly)
            return 0f;

        if (IsHdri && _envMap != null)
            return _envMap.PdfDirection(direction);

        if (HasSun)
        {
            float cosAngle = Vector3.Dot(Vector3.Normalize(direction), SunDirection);
            if (cosAngle < SunCosAngle)
                return 0f;
            float solidAngle = 2f * MathF.PI * (1f - SunCosAngle);
            return solidAngle > 0f ? 1f / solidAngle : 0f;
        }

        // Flat sky: uniform on the unit sphere — same constant for any direction.
        return UniformSpherePdf;
    }
}
