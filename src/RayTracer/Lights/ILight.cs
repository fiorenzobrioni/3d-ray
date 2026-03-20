using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

public interface ILight
{
    /// <summary>
    /// Number of shadow samples to cast for this light.
    /// Point/Directional = 1. Area lights = 8-32 for soft shadows.
    /// </summary>
    int ShadowSamples { get; }

    /// <summary>
    /// Samples the light and performs the shadow test in a single, consistent operation.
    /// This is critical for area lights: both the shadow ray and the illumination
    /// contribution must reference the SAME random point on the light surface.
    /// Returns InShadow=true and Color=Zero when the point is occluded.
    /// </summary>
    (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, IHittable world);

    /// <summary>
    /// Legacy single-call illumination (used internally). Use IlluminateAndTest in the renderer.
    /// </summary>
    (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint);

    /// <summary>
    /// Legacy shadow test. Prefer IlluminateAndTest for correctness.
    /// </summary>
    bool IsInShadow(Vector3 hitPoint, IHittable world);
}
