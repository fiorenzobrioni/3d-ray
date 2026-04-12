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
///   - AverageEmission() used instead of Emit(0,0,...) for UV-independent evaluation.
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

    public GeometryLight(ISamplable geometry, Emissive material, int shadowSamples = 1)
    {
        Geometry = geometry;
        Material = material;
        ShadowSamples = Math.Max(1, shadowSamples);
        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        // Sample() gives us the total area (deterministic) and a surface point
        // (random, but acceptable for the distance estimate).
        var (point, _, area) = Geometry.Sample();
 
        Vector3 toLight = point - hitPoint;
        float distSq = toLight.LengthSquared();
        if (distSq < MathUtils.Epsilon * MathUtils.Epsilon)
            return (Vector3.Zero, Vector3.UnitY, 0f);
 
        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;
 
        Vector3 emissiveColor = Material.AverageEmission();
 
        // Hemispheric-average cos(θ) = 0.5:  a Lambertian emitter seen from a
        // random direction has an expected projected-area factor of 1/2.
        // This eliminates the 50% zero-contribution problem of the old code.
        const float avgCosLight = 0.5f;
 
        // Full power estimate — NOT divided by ShadowSamples.
        float attenuation = area * avgCosLight / distSq;
 
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
        var (samplePoint, lightNormal, area) = sampleIndex >= 0
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

        // FIX #11: use AverageEmission() — same approximation as Illuminate().
        Vector3 emissiveColor = Material.AverageEmission();

        float attenuation = area * cosLight / (distSq * ShadowSamples);
        return (false, emissiveColor * attenuation, dirToLight, distance);
    }
}
