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
///   - Deterministic area: uses ISamplable.SurfaceArea (closed-form) instead of
///     a PRNG-driven Sample() call at construction time, making scene-load
///     fully reproducible regardless of PRNG state.
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

    /// <summary>
    /// Optional "virtual disc" radius that softens the <c>area·cosLight/d²</c>
    /// singularity in the area-sampling estimator (<see cref="ComputeAreaSample"/>).
    /// When &gt; 0, the attenuation denominator is clamped:
    /// <c>distSq = max(distSq, softRadius²)</c>, preventing unbounded variance
    /// when a stratified sample nearly grazes the receiver in dense media.
    /// 0 = unclamped, identical to pre-existing behaviour.
    /// The solid-angle cone-sampling path (<see cref="ComputeSolidAngleSample"/>)
    /// does NOT apply this clamp — its pdf_ω estimator subsumes all geometric
    /// factors and is bounded by construction.
    /// </summary>
    public float SoftRadius { get; }

    // ── Stratified sampling grid ────────────────────────────────────────────
    private readonly int _sqrtSamples;

    // Optional importance-sampling strategy. When non-null the light uses
    // solid-angle cone sampling instead of uniform area sampling.
    private readonly ISolidAngleSamplable? _solidAngleSampler;

    // Deterministic representative data for ApproximatePower() — computed once
    // from the underlying IHittable's bounding box so scene classification in
    // Renderer is reproducible regardless of the PRNG state.
    private readonly Vector3 _representativePoint;
    private readonly float   _representativeArea;

    // Default unified with AreaLight/SphereLight (4) so emissive geometry gets
    // comparable soft-shadow quality without extra YAML tuning.
    public const int DefaultShadowSamples = 4;

    public GeometryLight(ISamplable geometry, Emissive material, int shadowSamples = DefaultShadowSamples, float softRadius = 0f)
    {
        Geometry = geometry;
        Material = material;
        ShadowSamples = Math.Max(1, shadowSamples);
        SoftRadius = MathF.Max(0f, softRadius);
        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));

        // Opt-in to solid-angle cone sampling if the shape supports it.
        _solidAngleSampler = geometry as ISolidAngleSamplable;

        // Representative point = AABB centre (if the geometry is an IHittable).
        // Area = deterministic closed-form surface area via ISamplable.SurfaceArea.
        // This replaces the former Sample() call which consumed the PRNG during
        // scene construction (non-deterministic across runs).
        if (geometry is IHittable hittable)
        {
            var bbox = hittable.BoundingBox();
            _representativePoint = 0.5f * (bbox.Min + bbox.Max);
        }
        else
        {
            _representativePoint = Vector3.Zero;
        }

        _representativeArea = geometry.SurfaceArea;
    }

    // Lambertian emissive surface: Φ = π · L · A.
    // L is sampled at (u=0.5, v=0.5, point=AABB centre) — exact for SolidColor
    // emission, representative for procedural or texture-based emission.
    public float ApproximatePower(AABB sceneBounds)
    {
        Vector3 emission = Material.EmissionAt(0.5f, 0.5f, _representativePoint);
        return MathF.PI * _representativeArea * MathUtils.Luminance(emission);
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
        // Compute tMax in shadow-ray parameter space (relative to shadowOrigin,
        // not hitPoint). Using `distance - Epsilon` as tMax would cancel the
        // OffsetOrigin shift whenever `dirToLight ≈ normal`, producing systematic
        // self-intersection with the light's own surface and a black hole
        // directly underneath small / close emitters.
        float shadowTMax = (samplePoint - shadowOrigin).Length() - MathUtils.Epsilon;
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, shadowTMax);
        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dirToLight, distance);

        Vector3 emissiveColor = Material.EmissionAt(uv.X, uv.Y, samplePoint);
        // Soft-radius clamp: floors distSq at SoftRadius² to prevent the
        // area·cosLight/d² estimator from diverging when a sample grazes the
        // receiver (particularly in dense volumetric media). The geometric
        // distance is returned unchanged — only the attenuation denominator.
        float attenuationDistSq = distSq;
        if (SoftRadius > 0f) attenuationDistSq = MathF.Max(attenuationDistSq, SoftRadius * SoftRadius);
        float attenuation = area * cosLight / (attenuationDistSq * ShadowSamples);
        return (false, emissiveColor * attenuation * trans, dirToLight, distance);
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
        // but the interior-observer fallback can land on the back hemisphere.
        // Aligned with AreaLight pattern: report zero contribution rather than
        // occlusion so MIS sees a consistent estimator across light types.
        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, lightNormal));
        if (cosLight <= 0f)
            return (false, Vector3.Zero, dirToLight, distance);

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        // See ComputeAreaSample for the derivation of this tMax — this bug bites
        // the cone-sampling path systematically: every sample of a floor point
        // directly under a floating sphere has dirToLight ≈ normal, so every
        // sample self-intersects the emitter under the naive `distance - Epsilon`.
        float shadowTMax = (samplePoint - shadowOrigin).Length() - MathUtils.Epsilon;
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, shadowTMax);
        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dirToLight, distance);

        Vector3 emissiveColor = Material.EmissionAt(uv.X, uv.Y, samplePoint);
        float attenuation = 1f / (pdf * ShadowSamples);
        return (false, emissiveColor * attenuation * trans, dirToLight, distance);
    }

    // ── MIS ─────────────────────────────────────────────────────────────────
    public bool IsDelta => false;

    /// <summary>
    /// Solid-angle PDF of sampling direction <paramref name="wi"/> from
    /// <paramref name="hitPoint"/> under whichever strategy this GeometryLight
    /// uses (solid-angle cone for spheres, uniform-area for everything else).
    /// Returns 0 when the direction does not reach the geometry.
    /// </summary>
    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        if (_solidAngleSampler != null)
            return _solidAngleSampler.SolidAnglePdf(hitPoint, wi);

        // Area-sample fallback: intersect wi with the geometry and convert
        // pdf_A = 1/area to pdf_ω via pdf_ω = dist² / (area · cos θ_light).
        if (Geometry is not IHittable hittable)
            return 0f;

        var ray = new Ray(hitPoint, wi);
        var rec = new HitRecord();
        if (!hittable.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec))
            return 0f;

        float cosLight = MathF.Abs(Vector3.Dot(-wi, rec.Normal));
        if (cosLight < 1e-6f)
            return 0f;

        float distSq = rec.T * rec.T;
        float area = _representativeArea;
        if (area < 1e-12f)
            return 0f;

        return distSq / (area * cosLight);
    }

    /// <inheritdoc/>
    public bool TrySampleEmissivePoint(out Vector3 point, out Vector3 normal,
                                       out Vector3 emission, out float pdfArea)
    {
        var (p, n, uv, area) = Geometry.Sample();
        point    = p;
        normal   = n;
        emission = Material.EmissionAt(uv.X, uv.Y, p);
        pdfArea  = area > 1e-12f ? 1f / area : 0f;
        return pdfArea > 0f;
    }
}
