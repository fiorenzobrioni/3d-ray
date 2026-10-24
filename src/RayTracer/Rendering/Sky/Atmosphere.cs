using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Earth's clear atmosphere for the analytic skies (PIANO-REVIEW.md Phase 6):
/// Rayleigh scattering, Mie scattering and absorption by dust, and ozone
/// absorption, with the coefficients of Bruneton (2017, "A Qualitative and
/// Quantitative Evaluation of 8 Clear Sky Models") at 680 / 550 / 440 nm; a
/// transmittance table; and the sun disc that goes with it.
///
/// <para>
/// The sun and the sky come from the same light: the sun's illuminance at the
/// top of the atmosphere. Its disc shows that illuminance, attenuated along
/// the sun's path, spread over the disc's solid angle; the sky is the same
/// illuminance scattered. (Before, every sky model gave its disc the radiance
/// it fed the scattering as an irradiance, about 15 000 times too dim against
/// its own sky.)
/// </para>
/// </summary>
public sealed class Atmosphere
{
    public const float PlanetRadius = 6_360_000f;     // m
    public const float AtmosphereRadius = 6_420_000f; // m
    public const float RayleighScaleHeight = 8_000f;
    public const float MieScaleHeight = 1_200f;

    public static readonly Vector3 RayleighScattering = new(5.802e-6f, 1.358e-5f, 3.310e-5f);
    public static readonly Vector3 MieScattering = new(3.996e-6f);
    public static readonly Vector3 MieAbsorption = MieScattering * 0.11f;
    /// <summary>Ozone absorption at its densest (25 km), tent profile 15 km either side.</summary>
    public static readonly Vector3 OzoneAbsorption = new(0.650e-6f, 1.881e-6f, 0.085e-6f);
    public const float MieAsymmetry = 0.76f;

    /// <summary>Angular radius of the sun's disc, degrees.</summary>
    public const float SunHalfAngleDeg = 0.265f;

    /// <summary>Solid angle of the sun's disc (sr).</summary>
    public static readonly float SunSolidAngle =
        2f * MathF.PI * (1f - MathF.Cos(SunHalfAngleDeg * MathF.PI / 180f));

    /// <summary>
    /// Solar illuminance at the top of the atmosphere, lux (the solar
    /// illuminance constant of Darula, Kittler and Gueymard 2005).
    /// </summary>
    public const float SolarIlluminanceTop = 133_800f;

    public float AirDensity { get; }
    public float DustDensity { get; }
    public float OzoneDensity { get; }

    private const int LutHeights = 16, LutMus = 64;
    private readonly Vector3[] _lut = new Vector3[LutHeights * LutMus];

    public Atmosphere(float airDensity = 1f, float dustDensity = 1f, float ozoneDensity = 1f)
    {
        AirDensity = MathF.Max(0f, airDensity);
        DustDensity = MathF.Max(0f, dustDensity);
        OzoneDensity = MathF.Max(0f, ozoneDensity);
        for (int hi = 0; hi < LutHeights; hi++)
        {
            float h = hi / (float)(LutHeights - 1) * (AtmosphereRadius - PlanetRadius);
            for (int mi = 0; mi < LutMus; mi++)
            {
                float mu = -0.15f + mi / (float)(LutMus - 1) * 1.15f;
                _lut[hi * LutMus + mi] = ComputeTransmittance(h, mu);
            }
        }
    }

    /// <summary>
    /// Dust density for a Linke turbidity: the vertical optical depth of the
    /// clean, dry air times the turbidity is the whole atmosphere's, so the
    /// dust's Mie extinction (at 550 nm) is <c>T − 1</c> times the air's
    /// Rayleigh depth. T = 1 is air alone, 3 a clear day (aerosol depth about
    /// 0.2), 6 a hazy one. Dust 1 (the coefficients above as published) is
    /// T ≈ 1.05, an exceptionally clean sky.
    /// </summary>
    public static float DustFromTurbidity(float turbidity)
    {
        float rayleighDepth = RayleighScattering.Y * RayleighScaleHeight;
        float dustDepth = (MieScattering.Y + MieAbsorption.Y) * MieScaleHeight;
        return MathF.Max(0f, turbidity - 1f) * rayleighDepth / dustDepth;
    }

    /// <summary>Scattering coefficients (Rayleigh, Mie) at an altitude, per metre.</summary>
    public (Vector3 Rayleigh, Vector3 Mie) Scattering(float altitude)
    {
        float r = AirDensity * MathF.Exp(-altitude / RayleighScaleHeight);
        float m = DustDensity * MathF.Exp(-altitude / MieScaleHeight);
        return (RayleighScattering * r, MieScattering * m);
    }

    /// <summary>Extinction coefficient at an altitude, per metre.</summary>
    public Vector3 Extinction(float altitude)
    {
        float r = AirDensity * MathF.Exp(-altitude / RayleighScaleHeight);
        float m = DustDensity * MathF.Exp(-altitude / MieScaleHeight);
        float o = OzoneDensity * MathF.Max(0f, 1f - MathF.Abs(altitude - 25_000f) / 15_000f);
        return RayleighScattering * r + (MieScattering + MieAbsorption) * m + OzoneAbsorption * o;
    }

    /// <summary>Transmittance from an altitude to the top of the atmosphere along cos-zenith <paramref name="mu"/>.</summary>
    public Vector3 Transmittance(float altitude, float mu)
    {
        float fh = Math.Clamp(altitude / (AtmosphereRadius - PlanetRadius), 0f, 0.999f) * (LutHeights - 1);
        float fu = Math.Clamp((mu + 0.15f) / 1.15f, 0f, 0.999f) * (LutMus - 1);
        int hi = (int)fh, mi = (int)fu;
        float th = fh - hi, tm = fu - mi;
        int h1 = Math.Min(hi + 1, LutHeights - 1), m1 = Math.Min(mi + 1, LutMus - 1);
        Vector3 ab = Vector3.Lerp(_lut[hi * LutMus + mi], _lut[hi * LutMus + m1], tm);
        Vector3 cd = Vector3.Lerp(_lut[h1 * LutMus + mi], _lut[h1 * LutMus + m1], tm);
        return Vector3.Lerp(ab, cd, th);
    }

    /// <summary>
    /// Radiance of the sun's disc seen from sea level, for a top-of-atmosphere
    /// illuminance <paramref name="topIlluminance"/> (in the sky's units):
    /// that illuminance, attenuated along the sun's path, over the disc.
    /// Zero for a sun below the horizon.
    /// </summary>
    public Vector3 SunDiscRadiance(Vector3 dirToSun, float topIlluminance)
    {
        if (dirToSun.Y < -0.05f) return Vector3.Zero;
        return Transmittance(0f, MathF.Max(0f, dirToSun.Y)) * (topIlluminance / SunSolidAngle);
    }

    private Vector3 ComputeTransmittance(float h, float mu)
    {
        Vector3 origin = new(0f, PlanetRadius + h, 0f);
        Vector3 dir = new(MathF.Sqrt(MathF.Max(0f, 1f - mu * mu)), mu, 0f);
        float tTop = ExitDistance(origin, dir);
        if (tTop <= 0f) return Vector3.One;
        const int steps = 64;
        float dt = tTop / steps;
        Vector3 depth = Vector3.Zero;
        for (int i = 0; i < steps; i++)
        {
            Vector3 p = origin + dir * ((i + 0.5f) * dt);
            depth += Extinction(p.Length() - PlanetRadius);
        }
        depth *= dt;
        return new Vector3(MathF.Exp(-depth.X), MathF.Exp(-depth.Y), MathF.Exp(-depth.Z));
    }

    /// <summary>Distance along <paramref name="dir"/> from inside the atmosphere to its top.</summary>
    public static float ExitDistance(Vector3 origin, Vector3 dir)
    {
        float b = Vector3.Dot(origin, dir);
        float c = Vector3.Dot(origin, origin) - AtmosphereRadius * AtmosphereRadius;
        float disc = b * b - c;
        return disc < 0f ? 0f : -b + MathF.Sqrt(disc);
    }
}
