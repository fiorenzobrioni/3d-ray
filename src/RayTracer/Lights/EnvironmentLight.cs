using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Rendering;

namespace RayTracer.Lights;

/// <summary>
/// Wraps SkySettings to provide direct lighting (Next Event Estimation)
/// from an HDRI environment map or a gradient sky with a sun disk.
/// Uses importance sampling to dramatically reduce noise vs purely indirect gathering.
///
/// Lifecycle:
///   - ApproximatePower()  → called once at Renderer construction for scene
///                           classification. Returns a DETERMINISTIC flux estimate
///                           scaled by the scene's cross-section (no PRNG).
///   - IlluminateAndTest() → called at every surface hit during rendering.
///                           Uses importance sampling (PRNG) for accurate NEE.
/// </summary>
public class EnvironmentLight : ILight
{
    private readonly SkySettings _sky;

    // FIX #9: removed redundant `= 1` inline initialiser — value always comes from constructor.
    /// <inheritdoc/>
    public int ShadowSamples { get; }

    public EnvironmentLight(SkySettings sky, int shadowSamples = 1)
    {
        _sky = sky;
        ShadowSamples = Math.Max(1, shadowSamples);
    }

    /// <summary>
    /// Approximate flux received by the scene from the environment.
    ///
    ///   • Hemispheric irradiance onto a diffuse point: E = π · L̄  where L̄ is the
    ///     scene-averaged sky radiance (<see cref="SkySettings.EstimatedAverageLuminance"/>).
    ///   • Flux through the scene's cross-section: Φ = E · π · R².
    ///
    /// Returns 0 when the sky has no direct-sampling support (flat ambient fills),
    /// since NEE cannot draw samples from it and the classifier must treat such
    /// scenes as indirect-only.
    ///
    /// Fully deterministic: reads <see cref="SkySettings.EstimatedAverageLuminance"/>
    /// which itself avoids PRNG (gradient hemisphere weighted mean or cached HDRI
    /// average), ensuring identical classification across runs.
    /// </summary>
    public float ApproximatePower(AABB sceneBounds)
    {
        if (!_sky.CanSampleDirectly)
            return 0f;

        // Cauchy's formula: the average projected area of a convex body is
        // (surface area) / 4. For an axis-aligned box of extent (x, y, z) the
        // surface area is 2(xy + yz + xz), so the directionally-averaged
        // cross-section is (xy + yz + xz) / 2. This replaces the previous
        // bounding-sphere upper bound (πR² with R = ½‖extent‖) which over-
        // estimated elongated scenes by up to 8× and skewed the power-weighted
        // light picking toward the environment in mixed lighting setups.
        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        float crossSection = 0.5f * (extent.X * extent.Y +
                                      extent.Y * extent.Z +
                                      extent.X * extent.Z);

        float irradiance = MathF.PI * _sky.EstimatedAverageLuminance;
        return irradiance * crossSection;
    }

    /// <inheritdoc/>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, float time = 0f)
    {
        if (!_sky.CanSampleDirectly)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        var (dir, color, pdf) = _sky.SampleDirectly();
        if (pdf <= 0f)
            return (true, Vector3.Zero, dir, 0f);

        float distance = MathUtils.Infinity;

        // For surface hits, discard samples below the geometric horizon.
        // For volumetric scattering points the caller passes Vector3.Zero as the
        // normal (no surface exists); in that case all directions are valid and
        // the phase function handles the directional weight instead.
        bool hasSurfaceNormal = surfaceNormal.LengthSquared() > 0f;
        if (hasSurfaceNormal)
        {
            float nDotL = Vector3.Dot(surfaceNormal, dir);
            if (nDotL <= 0f)
                return (true, Vector3.Zero, dir, distance);
        }

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dir, time);

        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, MathUtils.Infinity);
        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dir, distance);

        // L / (pdf × ShadowSamples): each sample contributes 1/ShadowSamples of total energy.
        // The N·L factor is applied by ComputeDirectLighting in Renderer (via EvaluateDirect).
        Vector3 attenuation = color * trans / (pdf * ShadowSamples);
        return (false, attenuation, dir, distance);
    }

    // ── MIS ─────────────────────────────────────────────────────────────────
    public bool IsDelta => false;

    /// <inheritdoc/>
    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        if (!_sky.CanSampleDirectly)
            return 0f;
        return _sky.PdfSolidAngle(wi);
    }

    /// <summary>
    /// Exposes the wrapped sky so the renderer can query environment radiance
    /// when a BSDF ray escapes the scene and apply the MIS weight.
    /// </summary>
    public SkySettings Sky => _sky;
}
