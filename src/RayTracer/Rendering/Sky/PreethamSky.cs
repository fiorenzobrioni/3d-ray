using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Analytic clear-sky daylight model of Preetham, Shirley and Smits (1999,
/// "A Practical Analytic Model for Daylight"). Luminance Y and chromaticity x,
/// y follow the Perez distribution
/// <code>
///   F(θ, γ) = (1 + A·e^(B/cosθ)) · (1 + C·e^(Dγ) + E·cos²γ)
///   Y(θ, γ) = Y_z · F(θ, γ) / F(0, θ_s)
/// </code>
/// (θ the view's zenith angle, γ its angle to the sun, θ_s the sun's zenith
/// angle), with the paper's zenith values and A-E coefficients linear in
/// turbidity; xyY is converted to linear Rec.709. Y_z is in kcd/m².
///
/// <para>The Perez function used to read <c>C + D·e^(Eγ) + E·cos²γ</c>, with
/// the coefficients shifted by one and E used twice (PIANO-REVIEW.md 3.1): the
/// circumsolar glow was off and the zenith no longer matched Y_z. The model is
/// kept as a baseline; <see cref="HosekWilkieSky"/> fits measured skies better,
/// low suns above all.</para>
/// </summary>
public sealed class PreethamSky : AnalyticSky
{
    /// <summary>Atmospheric turbidity, 1 (very clear) to 10 (hazy). Default 3.</summary>
    public float Turbidity { get; }

    private readonly float _zenithY, _zenithX, _zenithYy;
    private readonly Perez _pY, _px, _py;
    private readonly float _scale;

    private readonly record struct Perez(float A, float B, float C, float D, float E)
    {
        public float F(float cosTheta, float gamma, float cosGamma)
            => (1f + A * MathF.Exp(B / cosTheta)) * (1f + C * MathF.Exp(D * gamma) + E * cosGamma * cosGamma);
    }

    public PreethamSky(Vector3 sunDirToSun, float turbidity = 3f, Vector3? groundAlbedo = null,
                       float intensity = 1f, SkyUnits units = SkyUnits.Relative)
        : base(sunDirToSun, groundAlbedo ?? new Vector3(0.3f), intensity, units,
               new Atmosphere(dustDensity: Atmosphere.DustFromTurbidity(MathF.Max(1f, turbidity))))
    {
        Turbidity = MathF.Max(1f, turbidity);
        float T = Turbidity, T2 = T * T;
        // The model holds for the sun above the horizon; below it, the sky stays at sunset.
        float ts = MathF.Acos(Math.Clamp(SunDirection.Y, 0f, 1f));
        float ts2 = ts * ts, ts3 = ts2 * ts;

        float chi = (4f / 9f - T / 120f) * (MathF.PI - 2f * ts);
        _zenithY = MathF.Max(0f, (4.0453f * T - 4.9710f) * MathF.Tan(chi) - 0.2155f * T + 2.4192f);
        _zenithX = (0.00166f * ts3 - 0.00375f * ts2 + 0.00209f * ts) * T2
                 + (-0.02903f * ts3 + 0.06377f * ts2 - 0.03202f * ts + 0.00394f) * T
                 + (0.11693f * ts3 - 0.21196f * ts2 + 0.06052f * ts + 0.25886f);
        _zenithYy = (0.00275f * ts3 - 0.00610f * ts2 + 0.00317f * ts) * T2
                  + (-0.04214f * ts3 + 0.08970f * ts2 - 0.04153f * ts + 0.00516f) * T
                  + (0.15346f * ts3 - 0.26756f * ts2 + 0.06670f * ts + 0.26688f);

        _pY = new Perez(0.1787f * T - 1.4630f, -0.3554f * T + 0.4275f, -0.0227f * T + 5.3251f,
                        0.1206f * T - 2.5771f, -0.0670f * T + 0.3703f);
        _px = new Perez(-0.0193f * T - 0.2592f, -0.0665f * T + 0.0008f, -0.0004f * T + 0.2125f,
                        -0.0641f * T - 0.8989f, -0.0033f * T + 0.0452f);
        _py = new Perez(-0.0167f * T - 0.2608f, -0.0950f * T + 0.0092f, -0.0079f * T + 0.2102f,
                        -0.0441f * T - 1.6537f, -0.0109f * T + 0.0529f);

        // Normalise by F(0, θ_s): the zenith returns the zenith values.
        float cs = MathF.Cos(ts);
        _zenithY /= _pY.F(1f, ts, cs);
        _zenithX /= _px.F(1f, ts, cs);
        _zenithYy /= _py.F(1f, ts, cs);

        _scale = units == SkyUnits.Photometric ? 1000f : 1f;
        Finish(units == SkyUnits.Photometric ? Atmosphere.SolarIlluminanceTop : Atmosphere.SolarIlluminanceTop / 1000f);
    }

    /// <summary>Luminance toward <paramref name="dir"/>, kcd/m² (relative scale).</summary>
    internal float Luminance(Vector3 dir)
    {
        var (cosTheta, gamma, cosGamma) = Angles(dir);
        return _zenithY * _pY.F(cosTheta, gamma, cosGamma);
    }

    private (float CosTheta, float Gamma, float CosGamma) Angles(Vector3 dir)
    {
        // cosθ floored just above the horizon: the A·e^(B/cosθ) term is finite there.
        float cosTheta = MathF.Max(0.01f, dir.Y);
        float cosGamma = Math.Clamp(Vector3.Dot(dir, SunDirection), -1f, 1f);
        return (cosTheta, MathF.Acos(cosGamma), cosGamma);
    }

    protected override Vector3 Above(Vector3 dir)
    {
        var (cosTheta, gamma, cosGamma) = Angles(dir);
        float Y = _zenithY * _pY.F(cosTheta, gamma, cosGamma);
        float x = _zenithX * _px.F(cosTheta, gamma, cosGamma);
        float y = MathF.Max(1e-4f, _zenithYy * _py.F(cosTheta, gamma, cosGamma));

        float X = x / y * Y;
        float Z = (1f - x - y) / y * Y;
        float r = 3.2404542f * X - 1.5371385f * Y - 0.4985314f * Z;
        float g = -0.9692660f * X + 1.8760108f * Y + 0.0415560f * Z;
        float b = 0.0556434f * X - 0.2040259f * Y + 1.0572252f * Z;
        return new Vector3(MathF.Max(0f, r), MathF.Max(0f, g), MathF.Max(0f, b)) * _scale;
    }
}
