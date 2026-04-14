using System.Numerics;

namespace RayTracer.Geometry;

/// <summary>
/// Companion interface to <see cref="ISamplable"/> for geometries that admit an
/// efficient solid-angle importance-sampling strategy with respect to an external
/// reference point (typically a surface being shaded).
///
/// <para>
/// Whereas <see cref="ISamplable"/> samples uniformly over the surface area
/// (simple and general, but wastes roughly half the samples on the back-facing
/// hemisphere for convex shapes and has high variance when the observer is close),
/// a solid-angle sampler restricts sampling to the cone of directions that actually
/// hit the geometry. For a sphere this reduces variance by an order of magnitude
/// for small or distant emitters — the same technique PBRT, Arnold, Cycles and
/// Mitsuba use for sphere lights.
/// </para>
///
/// <para><b>Estimator convention.</b>
/// The returned <c>SolidAnglePdf</c> is in <b>solid-angle measure</b> with respect
/// to <c>from</c>. The Monte Carlo estimator for direct lighting is therefore:
/// <code>
///   L_o = (1/N) Σ f_r(ω) · L_e(x) · cos θ_surface / pdf_ω(ω)
/// </code>
/// Notice the absence of the <c>cos θ_light / d²</c> factor that appears in area
/// sampling — the solid-angle pdf already subsumes it. Callers must be aware of
/// which PDF domain they are working in and must NOT multiply by the area-sampling
/// geometric term.
/// </para>
///
/// <para><b>MIS readiness.</b>
/// <see cref="SolidAnglePdf"/> evaluates the pdf for an arbitrary direction,
/// enabling Multiple Importance Sampling (balance / power heuristic) between
/// light sampling and BSDF sampling in a future renderer pass. Implementations
/// must return <c>0</c> for directions outside the sample set (e.g. outside the
/// visibility cone) to keep the MIS weight well-defined.
/// </para>
///
/// <para><b>Implementations.</b>
/// Only primitives with a closed-form cone / projected-shape distribution need
/// to implement this — currently <see cref="Sphere"/>. Triangles, quads, disks,
/// meshes and transformed primitives fall back automatically to the area-sampling
/// path in <see cref="Lights.GeometryLight"/>.
/// </para>
/// </summary>
public interface ISolidAngleSamplable
{
    /// <summary>
    /// Samples a surface point using the shape's solid-angle distribution as
    /// seen from <paramref name="from"/>.
    /// </summary>
    /// <returns>
    /// World-space <c>Point</c>, outward surface <c>Normal</c>, texture-space
    /// <c>Uv</c>, and the <c>SolidAnglePdf</c> in solid-angle measure w.r.t.
    /// <paramref name="from"/>. A non-positive pdf signals that no valid sample
    /// could be produced (degenerate configuration — caller should skip).
    /// </returns>
    (Vector3 Point, Vector3 Normal, Vector2 Uv, float SolidAnglePdf)
        SampleSolidAngle(Vector3 from);

    /// <summary>
    /// Stratified variant: maps <paramref name="sampleIndex"/> ∈ [0, N) to a
    /// jittered position inside its assigned cell on a <paramref name="sqrtSamples"/>
    /// × <paramref name="sqrtSamples"/> grid laid out in (cos θ, φ) space on the
    /// visibility cone.
    /// </summary>
    (Vector3 Point, Vector3 Normal, Vector2 Uv, float SolidAnglePdf)
        SampleSolidAngleStratified(Vector3 from, int sampleIndex, int sqrtSamples)
        => SampleSolidAngle(from);

    /// <summary>
    /// Evaluates the solid-angle pdf of sampling direction <paramref name="wi"/>
    /// from reference point <paramref name="from"/>. Returns <c>0</c> when
    /// <paramref name="wi"/> is outside the cone / visibility region.
    /// Used by future MIS implementations to weight BSDF-sampled directions that
    /// happen to hit the emitter.
    /// </summary>
    float SolidAnglePdf(Vector3 from, Vector3 wi);
}
