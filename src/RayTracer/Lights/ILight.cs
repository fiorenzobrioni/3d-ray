using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

public interface ILight
{
    /// <summary>
    /// Computes the illumination contribution at a surface point.
    /// Returns the light color/intensity, the direction TO the light, and the distance to the light.
    /// </summary>
    (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint);

    /// <summary>
    /// Checks if the point is in shadow from this light source.
    /// </summary>
    bool IsInShadow(Vector3 hitPoint, IHittable world);
}
