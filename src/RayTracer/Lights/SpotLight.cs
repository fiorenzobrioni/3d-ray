using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// A spot light with position, direction, and cone angles for inner/outer falloff.
/// Combines inverse-square distance attenuation with angular cone attenuation.
/// </summary>
public class SpotLight : ILight
{
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }
    public float CosInnerAngle { get; }
    public float CosOuterAngle { get; }

    /// <param name="position">World-space position of the light.</param>
    /// <param name="direction">Direction the spot light points toward (will be normalized).</param>
    /// <param name="color">Light color (RGB, typically [0,1]).</param>
    /// <param name="intensity">Light intensity (controls brightness).</param>
    /// <param name="innerAngleDeg">Inner cone half-angle in degrees (full intensity).</param>
    /// <param name="outerAngleDeg">Outer cone half-angle in degrees (falloff to zero).</param>
    public SpotLight(Vector3 position, Vector3 direction, Vector3 color,
                     float intensity = 1f, float innerAngleDeg = 15f, float outerAngleDeg = 30f)
    {
        Position = position;
        Direction = Vector3.Normalize(direction);
        Color = color;
        Intensity = intensity;
        CosInnerAngle = MathF.Cos(MathUtils.DegreesToRadians(innerAngleDeg));
        CosOuterAngle = MathF.Cos(MathUtils.DegreesToRadians(outerAngleDeg));
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        Vector3 toLight = Position - hitPoint;
        float distance = toLight.Length();
        Vector3 dirToLight = toLight / distance;

        // Inverse-square distance attenuation
        float distanceAttenuation = Intensity / (distance * distance);

        // Angular attenuation: smooth falloff between inner and outer cone
        float cosAngle = Vector3.Dot(-dirToLight, Direction);
        float spotAttenuation = Math.Clamp(
            (cosAngle - CosOuterAngle) / (CosInnerAngle - CosOuterAngle),
            0f, 1f);

        // Smooth step for more natural falloff
        spotAttenuation *= spotAttenuation;

        return (Color * distanceAttenuation * spotAttenuation, dirToLight, distance);
    }

    public bool IsInShadow(Vector3 hitPoint, IHittable world)
    {
        Vector3 toLight = Position - hitPoint;
        float distance = toLight.Length();
        Vector3 dir = toLight / distance;

        // Check if point is outside outer cone — no light contribution, skip shadow test
        float cosAngle = Vector3.Dot(-dir, Direction);
        if (cosAngle < CosOuterAngle)
            return true; // Effectively in shadow (no light reaches here)

        var shadowRay = new Ray(hitPoint + dir * MathUtils.Epsilon, dir);
        var rec = new HitRecord();
        return world.Hit(shadowRay, MathUtils.Epsilon, distance, ref rec);
    }
}
