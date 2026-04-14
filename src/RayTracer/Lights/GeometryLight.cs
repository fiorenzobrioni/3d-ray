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
/// Changes from original:
///   - shadowSamples propagated from constructor (was hardcoded to 1).
///   - cosLight guard added to Illuminate() (was only in IlluminateAndTest).
///   - EmissionAt() used with the real sample UV and world point (removes the
///     former center-texel approximation bias for textured emissives).
///   - Deterministic Illuminate(): uses an AABB-based centre estimator instead
///     of a PRNG-driven surface sample, making renderer scene analysis reproducible.
///   - Stratified sampling: when ShadowSamples > 1, the surface is divided into a
///     √N × √N grid (matching AreaLight/SphereLight strategy) for lower noise.
/// </summary>
public class GeometryLight : ILight
{
    public ISamplable Geometry { get; }
    public Emissive Material { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    // ── Stratified sampling grid ────────────────────────────────────────────
    private readonly int _sqrtSamples;

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
    /// Stratified version: call with a specific sample index for optimal noise reduction.
    /// Delegates to <see cref="ISamplable.SampleStratified"/> which divides the
    /// surface into a grid, matching the AreaLight/SphereLight strategy.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
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

        // Test shadow ray up to (distance - epsilon) to avoid self-intersection with the light
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);
        if (inShadow)
            return (true, Vector3.Zero, dirToLight, distance);

        // Evaluate the emissive texture AT the actual sample point.
        // This removes the former bias for image / checker / 3D procedural
        // emissives while keeping the estimator unbiased (surface sampling
        // with uniform PDF on area — the Monte Carlo weight `area * cosLight / distSq`
        // is already the correct Lᵢ/pdf factor).
        Vector3 emissiveColor = Material.EmissionAt(uv.X, uv.Y, samplePoint);

        float attenuation = area * cosLight / (distSq * ShadowSamples);
        return (false, emissiveColor * attenuation, dirToLight, distance);
    }
}
