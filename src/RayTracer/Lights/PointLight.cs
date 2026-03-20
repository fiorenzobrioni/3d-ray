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

    public bool IsInShadow(Vector3 hitPoint, IHittable world)
    {
        Vector3 toLight = Position - hitPoint;
        float distance = toLight.Length();
        Vector3 dir = toLight / distance;
        var shadowRay = new Ray(hitPoint + dir * MathUtils.Epsilon, dir);
        var rec = new HitRecord();
        return world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, IHittable world)
    {
        var (color, dirToLight, distance) = Illuminate(hitPoint);
        bool inShadow = IsInShadow(hitPoint, world);
        return (inShadow, color, dirToLight, distance);
    }
}
