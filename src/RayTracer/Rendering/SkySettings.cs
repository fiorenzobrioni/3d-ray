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

    public bool CanSampleDirectly => IsHdri || HasSun;

    /// <summary>
    /// Samples a random direction towards a bright part of the sky (HDRI or Sun).
    /// </summary>
    /// <returns>The sampled direction, the unbounded radiance of that direction (Color/Intensity), and the PDF.</returns>
    public (Vector3 Direction, Vector3 Color, float Pdf) SampleDirectly()
    {
        if (IsHdri && _envMap != null)
        {
            var (dir, pdf) = _envMap.SampleDirection();
            return (dir, _envMap.Sample(dir), pdf);
        }
        else if (HasSun)
        {
            // Sample uniformly within the sun's cone
            float z = 1f - MathUtils.RandomFloat() * (1f - SunCosAngle);
            float sinTheta = MathF.Sqrt(1f - z * z);
            float phi = 2f * MathF.PI * MathUtils.RandomFloat();
            float x = MathF.Cos(phi) * sinTheta;
            float y = MathF.Sin(phi) * sinTheta;

            // Local basis around SunDirection (points FROM scene TO sun)
            Vector3 w = SunDirection; // Already a normalized vector pointing to the sun
            Vector3 u = Vector3.Normalize(Vector3.Cross(MathF.Abs(w.X) > 0.1f ? Vector3.UnitY : Vector3.UnitX, w));
            Vector3 v = Vector3.Cross(w, u);
            
            Vector3 dir = Vector3.Normalize(x * u + y * v + z * w);

            float solidAngle = 2f * MathF.PI * (1f - SunCosAngle);
            float pdf = solidAngle > 0f ? 1f / solidAngle : 1f;

            // Wait, we also need to account for atmospheric scattering / gradient color at that direction
            // but for a small sun, SunColor * SunIntensity is 99% of the radiance.
            // Let's just evaluate the full sky at that direction!
            return (dir, SampleGradient(new Ray(Vector3.Zero, dir)), pdf);
        }

        // Fallback (Uniform sampling of hemisphere) - actually unused if CanSampleDirectly is checked
        Vector3 randomDir = MathUtils.RandomUnitVector();
        if (randomDir.Y < 0) randomDir.Y = -randomDir.Y;
        return (randomDir, FlatColor, 1f / (2f * MathF.PI));
    }
}
