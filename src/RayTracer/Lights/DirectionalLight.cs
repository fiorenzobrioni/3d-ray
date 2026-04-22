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

    // Directional emitter: irradiance I (W/m²) on a plane perpendicular to the
    // direction, integrated over the scene's projected cross-section.
    //   Φ = I · π · R²   where R = scene bounding-sphere radius.
    // This produces a flux that scales with scene size the same way the other
    // lights do, so the Renderer's normalised-irradiance classifier behaves
    // consistently whether the scene is lit by a sun, a sky, or finite emitters.
    public float ApproximatePower(AABB sceneBounds)
    {
        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        float radius = 0.5f * extent.Length();
        float crossSection = MathF.PI * radius * radius;
        return MathUtils.Luminance(Color) * Intensity * crossSection;
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
