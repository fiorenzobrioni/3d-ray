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
}
