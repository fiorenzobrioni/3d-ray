using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

public class PointLight : ILight
{
    public Vector3 Position { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <inheritdoc/>
    public int ShadowSamples => 1;

    public PointLight(Vector3 position, Vector3 color, float intensity = 1f)
    {
        Position = position;
        Color = color;
        Intensity = intensity;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        Vector3 toLight = Position - hitPoint;
        float distance = toLight.Length();
        Vector3 direction = toLight / distance;
        float attenuation = Intensity / (distance * distance);
        return (Color * attenuation, direction, distance);
    }

    /// <summary>
    /// Inlined illumination + shadow test using normal-based shadow origin.
    /// Previous implementation called Illuminate() then IsInShadow() separately,
    /// redundantly computing the direction vector twice.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        Vector3 toLight = Position - hitPoint;
        float distance = toLight.Length();
        Vector3 dirToLight = toLight / distance;

        // Robust shadow origin: offset along the surface normal toward the light side.
        // This prevents self-intersection at grazing angles where the old method
        // (offset along ray direction) could fail.
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        var rec = new HitRecord();
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dirToLight, distance);

        float attenuation = Intensity / (distance * distance);
        return (false, Color * attenuation, dirToLight, distance);
    }
}
