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
    ///
    /// <paramref name="surfaceNormal"/> is used to compute a robust shadow origin:
    /// the hit point is offset along the geometric normal rather than along the
    /// shadow ray direction. This prevents self-intersection artefacts at grazing
    /// angles where the direction-based offset can fail.
    ///
    /// For area lights, both the shadow ray and illumination contribution reference
    /// the SAME random point on the light surface (critical for unbiased soft shadows).
    ///
    /// Returns InShadow=true and Color=Zero when the point is occluded.
    /// </summary>
    (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world);

    /// <summary>
    /// Computes the illumination from this light at a given point, without shadow testing.
    /// </summary>
    (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint);
}
