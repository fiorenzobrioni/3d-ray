using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

public class DirectionalLight : ILight
{
    public Vector3 Direction { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <inheritdoc/>
    public int ShadowSamples => 1;

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

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        Vector3 toLight = -Direction;

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, toLight);
        var rec = new HitRecord();
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, MathUtils.Infinity, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, toLight, MathUtils.Infinity);

        return (false, Color * Intensity, toLight, MathUtils.Infinity);
    }
}
