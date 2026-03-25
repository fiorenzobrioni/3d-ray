using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Lights;

/// <summary>
/// An adapter that turns any ISamplable geometry with an Emissive material into an ILight.
/// This enables Direct Illumination (Next Event Estimation) for arbitrary emissive meshes.
/// </summary>
public class GeometryLight : ILight
{
    public ISamplable Geometry { get; }
    public Emissive Material { get; }

    // How many times to sample this mesh light during NEE. Can be customized in the future.
    public int ShadowSamples { get; } = 1;

    public GeometryLight(ISamplable geometry, Emissive material)
    {
        Geometry = geometry;
        Material = material;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        var (point, normal, area) = Geometry.Sample();
        Vector3 toLight = point - hitPoint;
        float distSq = toLight.LengthSquared();
        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, normal));
        
        // Emissive materials define Intensity internally, often multiplied by Albedo/Texture
        // For Illuminate(), we just get the base raw emission. We pass dummy UVs since we might not have them easily here,
        // but for now, we evaluate the emission at the sample center, or just use solid white * intensity.
        // A proper way is to have Emissive expose the average color or evaluate it.
        // Since many emissive materials use solid colors, we sample at (0,0) for now.
        Vector3 emissiveColor = Material.Emit(0f, 0f, point, 0, true);

        float attenuation = area * cosLight / (distSq * ShadowSamples);

        return (emissiveColor * attenuation, dirToLight, distance);
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance) IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        var (samplePoint, lightNormal, area) = Geometry.Sample();
        
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
        
        // We test shadow ray up to (distance - epsilon) to avoid self-intersection with the light itself
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dirToLight, distance);

        // Same as Illuminate(), we need to evaluate the emissive color. We pass dummy params for UV/Seed.
        Vector3 emissiveColor = Material.Emit(0f, 0f, samplePoint, 0, true);

        float attenuation = area * cosLight / (distSq * ShadowSamples);

        return (false, emissiveColor * attenuation, dirToLight, distance);
    }
}
