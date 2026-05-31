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

    // ── MNEE caustic sampling ────────────────────────────────────────────────
    //
    /// <summary>
    /// Draws one sample of an emissive surface point for Manifold Next Event
    /// Estimation (<see cref="Rendering.ManifoldWalker"/>): a world-space point
    /// on the light, its surface normal, the emitted radiance there, and the
    /// area-measure PDF. Returns false for lights MNEE does not yet drive
    /// (delta point/spot/directional, environment) — those fall through to the
    /// ordinary path. The manifold walk needs the raw area sample (not the
    /// solid-angle-folded value <see cref="IlluminateAndTest"/> returns) because
    /// it computes the generalized geometric term <c>dΩ_x/dA_y</c> itself by
    /// perturbing the sampled point across the light surface.
    /// </summary>
    bool TrySampleEmissivePoint(out Vector3 point, out Vector3 normal,
                                out Vector3 emission, out float pdfArea)
    {
        point = default; normal = default; emission = default; pdfArea = 0f;
        return false;
    }

    /// <summary>
    /// Per-channel scale applied to the radiance an MNEE caustic connection
    /// carries off this light along <paramref name="emitDir"/> — the unit
    /// direction from the sampled emitter point toward the specular chain
    /// (<c>normalize(lastVertex − y)</c>, i.e. outward along the beam).
    ///
    /// <para>Default <see cref="Vector3.One"/>: an isotropic emitter (point,
    /// sphere, area, geometry) radiates the same in every direction, so the
    /// caustic estimator is unchanged. <see cref="SpotLight"/> overrides this to
    /// apply its cone falloff, the one anisotropy in the emitter's profile that
    /// <see cref="TrySampleEmissivePoint"/> cannot encode (it is chain-independent
    /// and runs before the manifold solve produces the exit direction).</para>
    /// </summary>
    Vector3 DirectionalEmissionScale(Vector3 emitDir) => Vector3.One;
}
