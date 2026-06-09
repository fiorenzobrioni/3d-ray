using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Lights;

public interface ILight
{
    /// <summary>
    /// Number of shadow samples to cast for this light.
    /// Point/Directional = 1. Area lights = 8-32 for soft shadows.
    /// </summary>
    int ShadowSamples { get; }

    /// <summary>
    /// Samples the light and performs the shadow test in a single, consistent operation.
    ///
    /// <paramref name="surfaceNormal"/> is used to compute a robust shadow origin:
    /// the hit point is offset along the geometric normal rather than along the
    /// shadow ray direction. This prevents self-intersection artefacts at grazing
    /// angles where the direction-based offset can fail.
    ///
    /// For area lights, both the shadow ray and illumination contribution reference
    /// the SAME random point on the light surface (critical for unbiased soft shadows).
    ///
    /// Returns InShadow=true and Color=Zero when the point is occluded.
    /// </summary>
    (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world);

    /// <summary>
    /// Stratified shadow-sample variant. Area/sphere/geometry/sun-disc lights and
    /// multi-sample spots override this to jitter the i-th of
    /// <see cref="ShadowSamples"/> samples; all other lights inherit this default,
    /// which ignores <paramref name="sampleIndex"/> and forwards to
    /// <see cref="IlluminateAndTest"/>. Routing every light through this one
    /// virtual method lets the renderer's NEE loop drop the per-sample type-switch
    /// it used to do (a type-ladder evaluated millions of times).
    /// </summary>
    (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
        => IlluminateAndTest(hitPoint, surfaceNormal, world);

    /// <summary>
    /// Approximate total radiant flux emitted by this light, in luminance-weighted
    /// units (Rec.709). Used by the renderer for scene classification
    /// (direct-dominant vs. indirect-dominant → Russian-Roulette tuning) and as
    /// a building block for future light-importance-sampling passes.
    ///
    /// <para><b>Contract.</b> Implementations MUST be:</para>
    /// <list type="bullet">
    ///   <item><description><b>Deterministic.</b> No PRNG; identical value across runs.</description></item>
    ///   <item><description><b>Receiver-independent.</b> Depends only on the light's own
    ///   parameters, never on a shading point.</description></item>
    ///   <item><description><b>Finite-valued.</b> Infinite-aperture lights
    ///   (<see cref="DirectionalLight"/>, <see cref="EnvironmentLight"/>) must use
    ///   <paramref name="sceneBounds"/> to bound the flux integral over the scene's
    ///   finite cross-section.</description></item>
    /// </list>
    ///
    /// <para>Units are consistent across light types up to the convention that each
    /// light's <c>Intensity</c>/<c>emission</c> represents the physical quantity
    /// natural to its formulation (radiant intensity W/sr for point/spot/sphere,
    /// radiance W/m²/sr for area/emissive surfaces, irradiance W/m² for
    /// directional and environment). All results are scaled by Rec.709 luminance
    /// of the tinting colour, giving a single scalar that the classifier sums.
    /// </para>
    /// </summary>
    /// <param name="sceneBounds">Finite AABB of the renderable scene (infinite
    /// planes clamped). Only consumed by lights whose flux depends on scene
    /// extent; finite lights may ignore it.</param>
    float ApproximatePower(AABB sceneBounds);

    // ── MIS support ─────────────────────────────────────────────────────────
    //
    // Multiple Importance Sampling combines NEE (sampling the light) with BSDF
    // sampling (sampling the material) using the balance heuristic. This
    // requires two things from each light:
    //
    //   1. IsDelta — whether the light is described by a Dirac distribution
    //      (point, directional, spot). Delta lights cannot be hit by a BSDF
    //      ray, so their NEE contribution is always taken at full weight and
    //      no BSDF-sampled emission is ever attributed to them.
    //
    //   2. PdfSolidAngle(hitPoint, wi) — the solid-angle PDF this light's
    //      sampler would assign to direction wi from hitPoint. Returns 0 when
    //      wi is outside the light's support cone (e.g. outside a sphere
    //      light's visible cap, or below a rect light's plane). Delta lights
    //      return 0 everywhere.
    //
    // Defaults match a delta light so existing implementations remain correct
    // without code changes; area-like lights override both.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True if the light is a Dirac-delta emitter (point, directional, spot).
    /// Delta lights: NEE is always taken at full MIS weight and BSDF samples
    /// never reach them.
    /// </summary>
    bool IsDelta => true;

    /// <summary>
    /// Evaluates the solid-angle PDF of sampling direction <paramref name="wi"/>
    /// from <paramref name="hitPoint"/>. Returns 0 for delta lights or for
    /// directions outside the light's sampling support.
    /// </summary>
    float PdfSolidAngle(Vector3 hitPoint, Vector3 wi) => 0f;

    /// <summary>
    /// When non-null, the renderer will treat a BSDF-hit emission from this
    /// material as having been sampled by this light, applying the MIS weight
    /// <c>p_bsdf / (p_bsdf + p_light)</c> via <see cref="PdfSolidAngle"/>.
    ///
    /// Sphere and area lights set this so a smooth specular surface (low-α
    /// glass, polished metal) does not produce a "dark hole where the light
    /// should reflect" — the BSDF sample reaches the proxy primitive in the
    /// world and contributes the same radiance the NEE estimator would,
    /// closing Veach's MIS estimator. Default = null (no visible proxy).
    /// </summary>
    Emissive? ProxyMaterial => null;
}
