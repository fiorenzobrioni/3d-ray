using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Lights;

/// <summary>
/// An adapter that turns any ISamplable geometry with an Emissive material into an ILight.
/// This enables Direct Illumination (Next Event Estimation) for arbitrary emissive meshes,
/// including primitives wrapped in a Transform (translated, rotated, scaled emissives).
///
/// <para><b>Per-shape importance sampling.</b>
/// When the wrapped geometry also implements <see cref="ISolidAngleSamplable"/>
/// (currently: <see cref="Sphere"/>) this light switches to <b>solid-angle cone
/// sampling</b> — the same technique PBRT / Arnold / Cycles use for sphere lights.
/// Compared to uniform area sampling this eliminates wasted back-hemisphere samples,
/// makes <c>cos θ_light</c> roughly uniform across samples, and reduces variance
/// by an order of magnitude for small / distant emitters. Shapes that do NOT
/// implement the interface (triangles, quads, disks, boxes, cylinders, cones,
/// tori, capsules, annuli, meshes and any Transform-wrapped primitive) fall back
/// to the original uniform area-sampling estimator.
/// </para>
///
/// <para><b>Estimator equivalence.</b>
/// The two strategies are mathematically equivalent at convergence — they only
/// differ in variance. Both branches are unbiased Monte Carlo estimators of the
/// same direct-lighting integral; <see cref="ComputeAreaSample"/> uses pdf_A
/// = 1/area (integrating in area measure, hence the explicit
/// <c>cos θ_light / d²</c> factor), and <see cref="ComputeSolidAngleSample"/>
/// uses the shape's pdf_ω in solid-angle measure (which already subsumes the
/// geometric terms).
/// </para>
///
/// Changes from original:
///   - shadowSamples propagated from constructor (was hardcoded to 1).
///   - cosLight guard added to Illuminate() (was only in IlluminateAndTest).
///   - EmissionAt() used with the real sample UV and world point (removes the
///     former center-texel approximation bias for textured emissives).
///   - Deterministic Illuminate(): uses an AABB-based centre estimator instead
///     of a PRNG-driven surface sample, making renderer scene analysis reproducible.
///   - Stratified sampling: when ShadowSamples > 1, the surface is divided into a
///     √N × √N grid (matching AreaLight/SphereLight strategy) for lower noise.
///   - Solid-angle cone sampling dispatch for ISolidAngleSamplable geometry.
/// </summary>
public class GeometryLight : ILight
{
    public ISamplable Geometry { get; }
    public Emissive Material { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    // ── Stratified sampling grid ────────────────────────────────────────────
    private readonly int _sqrtSamples;

    // Optional importance-sampling strategy. When non-null the light uses
    // solid-angle cone sampling instead of uniform area sampling.
    private readonly ISolidAngleSamplable? _solidAngleSampler;

    // Deterministic representative data for Illuminate() — computed once from
    // the underlying IHittable's bounding box so scene analysis in Renderer is
    // reproducible regardless of the PRNG state.
    private readonly Vector3 _representativePoint;
    private readonly float   _representativeArea;

    // Default unified with AreaLight/SphereLight (16) so emissive geometry gets
    // comparable soft-shadow quality without extra YAML tuning.
    public const int DefaultShadowSamples = 16;

    public GeometryLight(ISamplable geometry, Emissive material, int shadowSamples = DefaultShadowSamples)
    {
        Geometry = geometry;
        Material = material;
        ShadowSamples = Math.Max(1, shadowSamples);
        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));

        // Opt-in to solid-angle cone sampling if the shape supports it.
        _solidAngleSampler = geometry as ISolidAngleSamplable;

        // Representative point = AABB centre (if the geometry is an IHittable).
        // Area = best deterministic estimate we have — one Sample() call at
        // construction time. This still consumes randomness exactly once per
        // light during scene construction (not during rendering), which is
        // stable across frames and tied to scene setup, not shading order.
        if (geometry is IHittable hittable)
        {
            var bbox = hittable.BoundingBox();
            _representativePoint = 0.5f * (bbox.Min + bbox.Max);
        }
        else
        {
            _representativePoint = Vector3.Zero;
        }

        var (_, _, _, area) = geometry.Sample();
        _representativeArea = area;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        // Deterministic power estimate using the AABB centre. This is only
        // used by Renderer's constructor for the indirect-dominant scene
        // classifier — it does not need to be physically rigorous, just
        // reproducible across runs.
        Vector3 toLight = _representativePoint - hitPoint;
        float distSq = toLight.LengthSquared();
        if (distSq < MathUtils.Epsilon * MathUtils.Epsilon)
            return (Vector3.Zero, Vector3.UnitY, 0f);

        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        // Use the solid-color average (u=0.5, v=0.5, point=centre) — exact for
        // SolidColor, representative for everything else. Good enough for a
        // "total power" heuristic; the correct, unbiased per-sample value is
        // used in IlluminateAndTest().
        Vector3 emissiveColor = Material.EmissionAt(0.5f, 0.5f, _representativePoint);

        // Hemispheric-average cos(θ) = 0.5 is a reasonable scalar proxy for
        // the projected-area factor of a Lambertian emitter seen from a
        // random direction.
        const float avgCosLight = 0.5f;
        float attenuation = _representativeArea * avgCosLight / distSq;

        return (emissiveColor * attenuation, dirToLight, distance);
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        return IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1);
    }

    /// <summary>
    /// Stratified variant. Dispatches to either the solid-angle or the area
    /// sampling estimator depending on whether the wrapped geometry implements
    /// <see cref="ISolidAngleSamplable"/>.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
    {
        return _solidAngleSampler != null
            ? ComputeSolidAngleSample(hitPoint, surfaceNormal, world, sampleIndex)
            : ComputeAreaSample(hitPoint, surfaceNormal, world, sampleIndex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Area-sampling estimator (fallback for triangles/quads/meshes/transforms)
    //
    //   L = L_e · area · cos θ_light / (d² · N)
    //
    // The caller multiplies by (BRDF · cos θ_surface). This is the standard
    // area-measure NEE estimator with pdf_A = 1/area.
    // ═════════════════════════════════════════════════════════════════════════
    private (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        ComputeAreaSample(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
    {
        var (samplePoint, lightNormal, uv, area) = sampleIndex >= 0
            ? Geometry.SampleStratified(sampleIndex, _sqrtSamples)
            : Geometry.Sample();

        Vector3 toLight = samplePoint - hitPoint;
        float distSq = toLight.LengthSquared();
        if (distSq < MathUtils.Epsilon * MathUtils.Epsilon)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        // Ensure we hit the front face of the light
        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, lightNormal));
        if (cosLight <= 0f)
            return (true, Vector3.Zero, dirToLight, distance);

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        var rec = new HitRecord();
        if (world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec))
            return (true, Vector3.Zero, dirToLight, distance);

        Vector3 emissiveColor = Material.EmissionAt(uv.X, uv.Y, samplePoint);
        float attenuation = area * cosLight / (distSq * ShadowSamples);
        return (false, emissiveColor * attenuation, dirToLight, distance);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Solid-angle cone-sampling estimator (preferred path for sphere emitters)
    //
    //   L = L_e / (pdf_ω · N)
    //
    // pdf_ω already contains the cos θ_light / d² factor, so it does NOT appear
    // explicitly here. This is the same estimator SphereLight uses internally.
    // For a sphere of radius R seen from distance d > R the cone solid angle is
    // Ω = 2π (1 − √(1 − R²/d²)), so 1/pdf_ω = Ω; at small R/d this tends to
    // πR²/d², the projected-area limit.
    // ═════════════════════════════════════════════════════════════════════════
    private (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        ComputeSolidAngleSample(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
    {
        var (samplePoint, lightNormal, uv, pdf) = sampleIndex >= 0
            ? _solidAngleSampler!.SampleSolidAngleStratified(hitPoint, sampleIndex, _sqrtSamples)
            : _solidAngleSampler!.SampleSolidAngle(hitPoint);

        if (pdf <= 0f)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        Vector3 toLight = samplePoint - hitPoint;
        float distSq = toLight.LengthSquared();
        if (distSq < MathUtils.Epsilon * MathUtils.Epsilon)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        // Cone sampling should produce only front-facing hits by construction,
        // but keep a guard for the interior-observer fallback (full-sphere uniform).
        float cosLight = Vector3.Dot(-dirToLight, lightNormal);
        if (cosLight <= 0f)
            return (true, Vector3.Zero, dirToLight, distance);

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        var rec = new HitRecord();
        if (world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec))
            return (true, Vector3.Zero, dirToLight, distance);

        Vector3 emissiveColor = Material.EmissionAt(uv.X, uv.Y, samplePoint);
        float attenuation = 1f / (pdf * ShadowSamples);
        return (false, emissiveColor * attenuation, dirToLight, distance);
    }
}
