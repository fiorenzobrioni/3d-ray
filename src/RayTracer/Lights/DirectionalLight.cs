using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

public class DirectionalLight : ILight
{
    public Vector3 Direction { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    public DirectionalLight(Vector3 direction, Vector3 color, float intensity = 1f)
    {
        Direction = Vector3.Normalize(direction);
        Color = color;
        Intensity = intensity;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        // Direction is "from light", so "to light" is negated
        return (Color * Intensity, -Direction, MathUtils.Infinity);
    }

    public bool IsInShadow(Vector3 hitPoint, IHittable world)
    {
        Vector3 toLight = -Direction;
        var shadowRay = new Ray(hitPoint + toLight * MathUtils.Epsilon, toLight);
        var rec = new HitRecord();
        return world.Hit(shadowRay, MathUtils.Epsilon, MathUtils.Infinity, ref rec);
    }
}
