using System.Numerics;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Abstract sky model: evaluates radiance for a direction expressed in
/// <b>sky-local space</b> (Y = up, units = unit sphere). All world↔sky
/// orientation, visibility-flag masking, and sun-extractor bookkeeping live
/// one level above, in <see cref="SkyEnvironment"/>.
///
/// <para>Concrete implementations: <see cref="FlatSky"/>, <see cref="GradientSky"/>,
/// <see cref="HosekWilkieSky"/>, <see cref="NishitaSky"/>, <see cref="HdriSky"/>.</para>
///
/// <para><b>NEE contract.</b> A model that exposes a meaningful importance
/// sampler (HDRI CDF, gradient sun cone, Hosek/Nishita sun disc) sets
/// <see cref="HasImportanceSampling"/> = true and implements
/// <see cref="ImportanceSample"/> + <see cref="Pdf"/>. Otherwise NEE on the
/// environment falls back to uniform-sphere sampling at the
/// <see cref="SkyEnvironment"/> level.</para>
/// </summary>
public interface ISkyModel
{
    /// <summary>
    /// Linear HDR radiance emitted toward the camera from direction
    /// <paramref name="dirLocal"/> (already normalized, sky-space).
    /// <b>Excludes</b> the analytical sun cap when one exists — the sun is
    /// added by <see cref="SkyEnvironment.Sample"/> after MIS bookkeeping.
    /// </summary>
    Vector3 EvaluateRadiance(Vector3 dirLocal);

    /// <summary>
    /// Deterministic estimate of the spherical mean radiance, weighted by
    /// Rec.709 luminance. No PRNG — receiver-independent — finite. Used by
    /// <see cref="Lights.EnvironmentLight.ApproximatePower"/> for scene
    /// classification at renderer-construction time.
    /// </summary>
    float EstimatedAverageLuminance { get; }

    /// <summary>True when <see cref="ImportanceSample"/> draws a meaningful PDF.</summary>
    bool HasImportanceSampling { get; }

    /// <summary>
    /// Importance-sample a direction over the sphere. Returns the sampled
    /// direction (sky-local), the radiance evaluated along it (sun excluded —
    /// the sun is handled by <see cref="HasAnalyticalSun"/>), and the
    /// solid-angle PDF.
    /// </summary>
    (Vector3 Direction, Vector3 Radiance, float Pdf) ImportanceSample();

    /// <summary>
    /// Solid-angle PDF of <see cref="ImportanceSample"/> at the given
    /// direction. 0 outside the sampler's support, 0 when
    /// <see cref="HasImportanceSampling"/> is false.
    /// </summary>
    float Pdf(Vector3 dirLocal);

    /// <summary>True if the model exposes an analytical sun disc (Hosek/Nishita/Gradient with sun).</summary>
    bool HasAnalyticalSun { get; }

    /// <summary>
    /// Analytical sun parameters when <see cref="HasAnalyticalSun"/>:
    /// <list type="bullet">
    ///   <item><description><c>Direction</c>: <b>direction pointing TO the sun</b> from the scene, sky-local, unit length.</description></item>
    ///   <item><description><c>Radiance</c>: peak spectral radiance at the disc centre (linear, W/sr/m²·k).</description></item>
    ///   <item><description><c>CosHalfAngle</c>: cosine of the disc half-angle.</description></item>
    ///   <item><description><c>LimbDarkening</c>: when true the consumer should multiply by Hestroffer 1997 limb function.</description></item>
    /// </list>
    /// Returns the zero quadruple when <see cref="HasAnalyticalSun"/> is false.
    /// </summary>
    (Vector3 Direction, Vector3 Radiance, float CosHalfAngle, bool LimbDarkening) AnalyticalSun { get; }
}
