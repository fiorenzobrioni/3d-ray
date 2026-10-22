using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Physical atmospheric scattering sky (Nishita et al. 1993): single
/// scattering through <see cref="Atmosphere"/> (Rayleigh, Mie and ozone over a
/// 6360 km planet), integrated along the view ray: at each step the sun's
/// light, attenuated down to that point (transmittance table), scattered toward
/// the viewer (Rayleigh and Henyey-Greenstein phase), attenuated back.
///
/// <para>The sun's illuminance at the top of the atmosphere feeds both the
/// scattering and the disc (<see cref="AnalyticSky"/>): 20 on the relative
/// scale the model always had, 133 800 lux on the photometric one. The disc
/// used to show 20 as a radiance, about 15 000 times too dim against its own
/// sky (PIANO-REVIEW.md Phase 6).</para>
///
/// <para>References: Nishita, Sirai, Tadamura, Nakamae (1993) "Display of the
/// Earth Taking into Account Atmospheric Scattering"; Bruneton and Neyret
/// (2008) for the transmittance table.</para>
/// </summary>
public sealed class NishitaSky : AnalyticSky
{
    /// <summary>Top-of-atmosphere illuminance of the relative scale.</summary>
    public const float RelativeSolarIlluminance = 20f;

    /// <summary>
    /// The dust of the YAML <c>turbidity</c> knob: 0.5 at 1, 2.3 at 10, an
    /// exceptionally clean atmosphere at the default 3. Deliberately lighter
    /// than <see cref="Atmosphere.DustFromTurbidity"/> (the Linke turbidity
    /// of the analytic models): this model scatters once, and with the aerosol
    /// load of a real turbidity 3 its horizon goes dark where multiple
    /// scattering keeps a real one bright. Its sky is still darker than a
    /// measured one against the sun (about half, the missing multiple scattering).
    /// </summary>
    public static float DustFromTurbidity(float turbidity) => MathF.Max(0.1f, (turbidity - 1f) * 0.2f + 0.5f);

    public float AirDensity => Atmosphere.AirDensity;
    public float DustDensity => Atmosphere.DustDensity;

    private readonly float _top;

    public NishitaSky(Vector3 sunDirToSun, float airDensity = 1f, float dustDensity = 1f, float intensity = 1f,
                      Vector3? groundAlbedo = null, SkyUnits units = SkyUnits.Relative)
        : base(sunDirToSun, groundAlbedo ?? new Vector3(0.3f), intensity, units,
               new Atmosphere(airDensity, dustDensity))
    {
        _top = units == SkyUnits.Photometric ? Atmosphere.SolarIlluminanceTop : RelativeSolarIlluminance;
        Finish(_top);
    }

    protected override Vector3 Above(Vector3 dir)
    {
        dir = Vector3.Normalize(dir);
        Vector3 origin = new(0f, Atmosphere.PlanetRadius + 1f, 0f);
        float tMax = Atmosphere.ExitDistance(origin, dir);
        if (tMax <= 0f) return Vector3.Zero;

        float mu = Vector3.Dot(dir, SunDirection);
        float phaseR = 3f / (16f * MathF.PI) * (1f + mu * mu);
        const float g = Atmosphere.MieAsymmetry;
        float phaseM = (1f - g * g) / (4f * MathF.PI * MathF.Pow(MathF.Max(1e-4f, 1f + g * g - 2f * g * mu), 1.5f));

        const int Steps = 16;
        float dt = tMax / Steps;
        Vector3 sum = Vector3.Zero;
        Vector3 depth = Vector3.Zero;
        for (int i = 0; i < Steps; i++)
        {
            Vector3 p = origin + dir * ((i + 0.5f) * dt);
            float altitude = MathF.Max(0f, p.Length() - Atmosphere.PlanetRadius);

            // Optical depth to the middle of the step.
            Vector3 ext = Atmosphere.Extinction(altitude);
            Vector3 trView = Exp(-(depth + ext * (0.5f * dt)));
            depth += ext * dt;

            float muSun = Vector3.Dot(Vector3.Normalize(p), SunDirection);
            if (muSun < -0.05f) continue; // the planet shadows the sun
            Vector3 trSun = Atmosphere.Transmittance(altitude, muSun);

            var (rayleigh, mie) = Atmosphere.Scattering(altitude);
            sum += (rayleigh * phaseR + mie * phaseM) * trView * trSun * dt;
        }
        return sum * _top;
    }

    private static Vector3 Exp(Vector3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));
}
