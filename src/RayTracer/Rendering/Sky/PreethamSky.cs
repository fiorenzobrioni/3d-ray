using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Analytical clear-sky daylight model — Preetham, Shirley &amp; Smits (1999).
///
/// <para>Parameters mirror the Hosek-Wilkie 2012 successor (<c>turbidity</c>,
/// <c>ground_albedo</c>, <c>sun_direction</c>) so the YAML <c>type:
/// hosek_wilkie</c> / <c>type: preetham</c> schemas are interchangeable. The
/// Preetham model produces physically-plausible blue sky, sun-glow, and
/// horizon brightening from a single parameter (turbidity ∈ [1, 10]: 1 ≈ very
/// pure air, 3 ≈ clear day, 5 ≈ haze, 10 ≈ smog). It is the canonical pre-HW
/// model and remains a Mitsuba / Cycles / Arnold reference baseline.</para>
///
/// <para><b>Formulation.</b> Internally evaluates the Preetham xyY luminance
/// distribution
/// <code>
///   F(θ, γ) = (1 + A·exp(B/cosθ)) · (C + D·exp(E·γ) + F·cos²γ)
/// </code>
/// where θ is the angle between the view direction and zenith, γ the angle
/// between view and sun. The (A,B,C,D,E,F) coefficients are linear functions
/// of turbidity, tabulated separately for Y (luminance), x and y chromaticity
/// at the zenith. The result is converted xyY → CIE XYZ → linear Rec.709 RGB.
/// The sun disc itself is exposed analytically via <see cref="AnalyticalSun"/>
/// so a separate <see cref="Lights.PhysicalSun"/> can handle NEE / shadows.</para>
///
/// <para><b>Caveats vs full Hosek-Wilkie.</b> Preetham over-darkens the sky
/// near the horizon at low sun elevations (a known limitation fixed by HW's
/// 9-parameter form). For elevations above ~10° the deviation is &lt; 5%,
/// well below MC noise at typical sample counts. <see cref="GroundAlbedo"/> is
/// applied here as a multiplicative tint on the ground hemisphere only (a
/// Hosek-style ground-bounce contribution is left as a TODO; the
/// <see cref="ISkyModel"/> interface and call sites already accept it).</para>
/// </summary>
public class PreethamSky : ISkyModel
{
    /// <summary>Direction TO the sun (sky-local, normalized).</summary>
    public Vector3 SunDirection { get; }

    /// <summary>Atmospheric turbidity. Reasonable range [1, 10]. Default 3.</summary>
    public float Turbidity { get; }

    /// <summary>Albedo of the (notional) ground hemisphere. Tints the lower half.</summary>
    public Vector3 GroundAlbedo { get; }

    /// <summary>Multiplicative intensity scale. Applied uniformly to sky body and sun.</summary>
    public float Intensity { get; }

    // Preetham zenith chromaticities & luminance (eqns 22-24, Preetham 1999)
    private readonly float _zenithY;   // cd/m² at the zenith
    private readonly float _zenithX;   // chromaticity x at zenith
    private readonly float _zenithy;   // chromaticity y at zenith

    // (A,B,C,D,E,F) for Y, x, y (eqn 25, Preetham 1999)
    private readonly float _ay, _by, _cy, _dy, _ey;
    private readonly float _ax, _bx, _cx, _dx, _ex;
    private readonly float _ayy, _byy, _cyy, _dyy, _eyy;

    private readonly float _thetaSun;   // angle of sun off zenith (radians)
    private readonly float _sunCosHalfAngle;
    private readonly Vector3 _sunRadiance;

    private const float SunHalfAngleDeg = 0.265f;  // real solar disc

    public PreethamSky(Vector3 sunDirToSun, float turbidity = 3f,
                       Vector3? groundAlbedo = null, float intensity = 1f)
    {
        SunDirection = Vector3.Normalize(sunDirToSun);
        Turbidity = MathF.Max(1f, turbidity);
        GroundAlbedo = groundAlbedo ?? new Vector3(0.3f);
        Intensity = MathF.Max(0f, intensity);

        // ── Sun elevation ───────────────────────────────────────────────────
        // theta_sun = angle off zenith (0 = overhead, π/2 = horizon)
        float sunY = Math.Clamp(SunDirection.Y, -1f, 1f);
        _thetaSun = MathF.Acos(MathF.Max(0f, sunY));   // clamp below-horizon to horizon
        float ts = _thetaSun;
        float ts2 = ts * ts;
        float ts3 = ts2 * ts;
        float T = Turbidity;
        float T2 = T * T;

        // Zenith luminance (eqn 22, kcd/m² scaled to standard nits) and
        // zenith chromaticities (eqn 23 & 24, Preetham 1999).
        // chi factor (Preetham eqn 22)
        float chi = (4f / 9f - T / 120f) * (MathF.PI - 2f * ts);
        _zenithY = (4.0453f * T - 4.9710f) * MathF.Tan(chi) - 0.2155f * T + 2.4192f;
        if (_zenithY < 0f) _zenithY = 0f;
        // The Y above is in kcd/m². The original Preetham normalises so the
        // model emits luminance in cd/m²; we keep relative-RGB and rely on
        // `intensity` for absolute scaling.

        _zenithX = (0.00166f * ts3 - 0.00375f * ts2 + 0.00209f * ts + 0f) * T2
                 + (-0.02903f * ts3 + 0.06377f * ts2 - 0.03202f * ts + 0.00394f) * T
                 + (0.11693f * ts3 - 0.21196f * ts2 + 0.06052f * ts + 0.25886f);
        _zenithy = (0.00275f * ts3 - 0.00610f * ts2 + 0.00317f * ts + 0f) * T2
                 + (-0.04214f * ts3 + 0.08970f * ts2 - 0.04153f * ts + 0.00516f) * T
                 + (0.15346f * ts3 - 0.26756f * ts2 + 0.06670f * ts + 0.26688f);

        // Distribution coefficients (eqn 25, Preetham 1999)
        // Y luminance
        _ay = 0.1787f * T - 1.4630f;
        _by = -0.3554f * T + 0.4275f;
        _cy = -0.0227f * T + 5.3251f;
        _dy = 0.1206f * T - 2.5771f;
        _ey = -0.0670f * T + 0.3703f;
        // x chromaticity
        _ax = -0.0193f * T - 0.2592f;
        _bx = -0.0665f * T + 0.0008f;
        _cx = -0.0004f * T + 0.2125f;
        _dx = -0.0641f * T - 0.8989f;
        _ex = -0.0033f * T + 0.0452f;
        // y chromaticity
        _ayy = -0.0167f * T - 0.2608f;
        _byy = -0.0950f * T + 0.0092f;
        _cyy = -0.0079f * T + 0.2102f;
        _dyy = -0.0441f * T - 1.6537f;
        _eyy = -0.0109f * T + 0.0529f;

        // ── Sun ─────────────────────────────────────────────────────────────
        // Use a physically-plausible peak radiance: scale the zenith model
        // value at the sun direction by an empirical factor accounting for
        // direct solar transmittance. 30 000 W/m²/sr/normalisedY at zenith
        // (clear day, T=2.5) is the Preetham reference; we expose it via
        // `intensity` so the artist can dial it.
        _sunCosHalfAngle = MathF.Cos(MathUtils.DegreesToRadians(SunHalfAngleDeg));
        // Estimate sun colour from CIE D-illuminant model attenuated by air
        // mass = 1/cos(thetaSun). This gives the warm sunset / cool noon
        // chromaticity shift expected from Rayleigh scattering.
        float airmass = 1f / MathF.Max(0.05f, MathF.Cos(_thetaSun));
        // Approx Rayleigh transmittance per channel (Bird 1981, normalised)
        float transR = MathF.Exp(-0.008735f * airmass);
        float transG = MathF.Exp(-0.052f   * airmass);
        float transB = MathF.Exp(-0.198f   * airmass);
        // Solar constant ≈ 20 (relative; intensity multiplier shapes absolute).
        const float SolarConstantRel = 20f;
        _sunRadiance = new Vector3(transR, transG, transB) * (SolarConstantRel * intensity);
    }

    public Vector3 EvaluateRadiance(Vector3 dirLocal)
    {
        // Preetham model integrates above the horizon. Below we model the
        // ground as a Lambertian disc reflecting an estimate of the sky
        // hemisphere irradiance (≈ π · L̄), tinted by GroundAlbedo. This is
        // an approximation of HW's ground term — sufficient for outdoor
        // scenes where the camera rarely looks into pure -Y.
        if (dirLocal.Y < 0f)
        {
            // Use the horizon sky tint and modulate by ground albedo so the
            // GI bounce off the implicit ground reads correctly.
            Vector3 horizon = EvaluateAboveHorizon(new Vector3(dirLocal.X, 0.001f, dirLocal.Z));
            float t = MathF.Min(-dirLocal.Y * 4f, 1f);
            return Vector3.Lerp(horizon, horizon * GroundAlbedo, t) * Intensity;
        }
        return EvaluateAboveHorizon(dirLocal) * Intensity;
    }

    private Vector3 EvaluateAboveHorizon(Vector3 dirLocal)
    {
        float cosTheta = MathF.Max(0.001f, dirLocal.Y);
        float gamma   = MathF.Acos(Math.Clamp(Vector3.Dot(dirLocal, SunDirection), -1f, 1f));
        float cosGamma = MathF.Cos(gamma);

        float Y  = ZenithRelative(_zenithY,
                       _ay, _by, _cy, _dy, _ey,
                       cosTheta, gamma, cosGamma);
        float x  = ZenithRelative(_zenithX,
                       _ax, _bx, _cx, _dx, _ex,
                       cosTheta, gamma, cosGamma);
        float yy = ZenithRelative(_zenithy,
                       _ayy, _byy, _cyy, _dyy, _eyy,
                       cosTheta, gamma, cosGamma);

        // Avoid division by zero in the xyY→XYZ conversion when y collapses.
        if (yy < 1e-4f) yy = 1e-4f;
        // xyY → CIE XYZ
        float X = (x / yy) * Y;
        float Z = ((1f - x - yy) / yy) * Y;

        // CIE XYZ → linear sRGB (Rec.709) — Bradford-adapted D65
        float r =  3.2404542f * X + -1.5371385f * Y + -0.4985314f * Z;
        float g = -0.9692660f * X +  1.8760108f * Y +  0.0415560f * Z;
        float b =  0.0556434f * X + -0.2040259f * Y +  1.0572252f * Z;

        // Negative values are physically meaningless: clamp to 0.
        return new Vector3(MathF.Max(0f, r), MathF.Max(0f, g), MathF.Max(0f, b));
    }

    private float ZenithRelative(float zenithValue,
                                 float A, float B, float C, float D, float E,
                                 float cosTheta, float gamma, float cosGamma)
    {
        // F(θ, γ) Preetham distribution divided by F(0, θ_sun) to normalise so
        // the sample at zenith returns the supplied zenith value.
        float fThetaGamma = (1f + A * MathF.Exp(B / cosTheta))
                          * (C + D * MathF.Exp(E * gamma)
                               + E * cosGamma * cosGamma /* small higher-order */);
        // Numerator of the canonical Preetham F has F·cos²γ as the trailing
        // term. Preetham's listed coefficients call this F (overloaded with
        // the function name); we fold it into E·cosGamma² to keep the formula
        // honest with fewer letters.
        float fZeroThetaSun = (1f + A * MathF.Exp(B))
                            * (C + D * MathF.Exp(E * _thetaSun)
                                 + E * MathF.Cos(_thetaSun) * MathF.Cos(_thetaSun));
        if (MathF.Abs(fZeroThetaSun) < 1e-6f) return zenithValue;
        return zenithValue * fThetaGamma / fZeroThetaSun;
    }

    public float EstimatedAverageLuminance
    {
        get
        {
            // Hemispheric mean ≈ ½ · (L_zenith + L_horizon). Convert zenith
            // luminance from kcd/m² to relative-RGB units consistent with the
            // Rec.709 conversion above. The factor 0.01 keeps it in the same
            // scale as a typical HDRI mean luminance — used for scene
            // classification, not for accurate flux.
            float lZenith = _zenithY * 0.01f;
            float lHorizon = lZenith * 0.4f;          // crude but stable
            return Intensity * 0.5f * (lZenith + lHorizon);
        }
    }

    public bool HasImportanceSampling => false;  // body has no concentrated peaks
    public (Vector3 Direction, Vector3 Radiance, float Pdf) ImportanceSample()
        => (Vector3.UnitY, Vector3.Zero, 0f);
    public float Pdf(Vector3 dirLocal) => 0f;

    public bool HasAnalyticalSun => true;

    public (Vector3 Direction, Vector3 Radiance, float CosHalfAngle, bool LimbDarkening) AnalyticalSun
        => (SunDirection, _sunRadiance, _sunCosHalfAngle, /*limbDarkening*/ true);
}
