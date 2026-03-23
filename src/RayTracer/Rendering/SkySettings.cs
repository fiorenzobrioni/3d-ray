using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering;

/// <summary>
/// Encapsulates sky/environment rendering configuration.
///
/// Two modes:
///   Flat     — returns a single solid color (legacy behavior, full backward compat)
///   Gradient — vertical lerp between horizon and zenith, with optional ground color
///              and procedural sun disk with glow halo
///
/// The sky acts as the environment light source: rays that escape the scene
/// sample this to get their color contribution. A richer sky = richer GI.
/// </summary>
public class SkySettings
{
    // ── Mode ────────────────────────────────────────────────────────────────
    public bool IsGradient { get; }

    // ── Flat mode ───────────────────────────────────────────────────────────
    public Vector3 FlatColor { get; }

    // ── Gradient mode ───────────────────────────────────────────────────────
    public Vector3 ZenithColor { get; }
    public Vector3 HorizonColor { get; }
    public Vector3 GroundColor { get; }

    // ── Sun disk ────────────────────────────────────────────────────────────
    public bool HasSun { get; }
    public Vector3 SunDirection { get; }    // normalised, points TOWARD the sun
    public Vector3 SunColor { get; }
    public float SunIntensity { get; }
    public float SunCosAngle { get; }       // cos(half angular diameter)
    public float SunFalloff { get; }        // exponent for glow halo around disk

    // ─────────────────────────────────────────────────────────────────────────
    //  Constructors
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flat sky — identical to the legacy behavior where <c>background</c>
    /// was returned for every escaped ray regardless of direction.
    /// </summary>
    public SkySettings(Vector3 flatColor)
    {
        IsGradient = false;
        FlatColor = flatColor;
        // Gradient fields unused but initialised to safe defaults
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
        IsGradient = true;
        FlatColor = horizonColor; // fallback if somehow used in flat path

        ZenithColor = zenithColor;
        HorizonColor = horizonColor;
        GroundColor = groundColor;

        if (sunDirection.HasValue)
        {
            HasSun = true;
            // The YAML convention is "direction FROM which the sun shines"
            // (same as DirectionalLight). We negate to get the direction
            // TOWARD the sun for the dot product with the ray direction.
            SunDirection = Vector3.Normalize(-sunDirection.Value);
            SunColor = sunColor ?? Vector3.One;
            SunIntensity = sunIntensity;
            SunCosAngle = MathF.Cos(MathUtils.DegreesToRadians(sunSizeDeg * 0.5f));
            SunFalloff = sunFalloff;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Core method — called by Renderer.CalculateSkyColor()
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the sky radiance for a ray that escaped the scene.
    /// </summary>
    public Vector3 Sample(Ray ray)
    {
        if (!IsGradient)
            return FlatColor;

        Vector3 dir = Vector3.Normalize(ray.Direction);
        float y = dir.Y;

        // ── Sky gradient ────────────────────────────────────────────────
        //
        // Above horizon (y > 0): lerp horizon → zenith
        //   t=0 at horizon, t=1 at straight up
        //
        // Below horizon (y < 0): lerp horizon → ground
        //   t=0 at horizon, t=1 at straight down
        //
        // The pow(t, 0.5) on the sky side gives a wider horizon band
        // (the blue concentrates toward the zenith), which looks more
        // natural than a plain linear lerp.

        Vector3 skyColor;
        if (y >= 0f)
        {
            float t = MathF.Sqrt(MathF.Min(y, 1f)); // sqrt for wider horizon band
            skyColor = Vector3.Lerp(HorizonColor, ZenithColor, t);
        }
        else
        {
            float t = MathF.Min(-y * 4f, 1f); // quick falloff below horizon
            skyColor = Vector3.Lerp(HorizonColor, GroundColor, t);
        }

        // ── Sun disk + glow halo ────────────────────────────────────────
        //
        // The sun has two parts:
        //   1. Hard disk: when the ray is within the angular radius,
        //      return the full sun color × intensity (very bright).
        //   2. Glow halo: outside the disk, the intensity falls off as
        //      pow(cosAngle, falloff). This creates the warm glow around
        //      the sun that tints the surrounding sky.
        //
        // Both are ADDITIVE on top of the gradient, not replacing it.

        if (HasSun && y > -0.05f) // sun is only visible above (or near) horizon
        {
            float cosAngle = Vector3.Dot(dir, SunDirection);

            if (cosAngle > 0f)
            {
                if (cosAngle >= SunCosAngle)
                {
                    // Inside the hard disk — full brightness
                    skyColor += SunColor * SunIntensity;
                }
                else
                {
                    // Glow halo — smooth falloff
                    // Remap cosAngle to a 0..1 range relative to the disk edge
                    // then raise to the falloff exponent for a soft glow
                    float glow = MathF.Pow(cosAngle, SunFalloff);
                    skyColor += SunColor * SunIntensity * glow;
                }
            }
        }

        return skyColor;
    }
}
