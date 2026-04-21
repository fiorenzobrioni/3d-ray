using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

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
    /// Computes the illumination from this light at a given point, without shadow testing.
    /// </summary>
    (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint);

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
}
