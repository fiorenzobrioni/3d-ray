using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// A rectangular area light emitter that produces physically-based soft shadows.
///
/// Defined by a corner position and two edge vectors (U, V) forming a parallelogram.
/// Each shadow test samples a random point on the light surface, and multiple samples
/// (ShadowSamples) are averaged in the renderer to produce penumbra gradients.
///
/// Illumination uses solid-angle weighting:
///   L = Intensity * area * cos(θ_light) / distance²
/// where θ_light is the angle between the surface-to-light direction and the light normal.
///
/// YAML example:
/// <code>
/// - type: area
///   corner: [-1.0, 4.9, -1.0]
///   u: [2.0, 0.0, 0.0]
///   v: [0.0, 0.0, 2.0]
///   color: [1.0, 0.95, 0.9]
///   intensity: 40.0
///   shadow_samples: 16
/// </code>
/// </summary>
public class AreaLight : ILight
{
    /// <summary>One corner of the rectangular light emitter.</summary>
    public Vector3 Corner { get; }

    /// <summary>First edge vector (defines width and one side direction).</summary>
    public Vector3 U { get; }

    /// <summary>Second edge vector (defines height and other side direction).</summary>
    public Vector3 V { get; }

    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    private readonly Vector3 _normal;  // Outward normal of the light surface
    private readonly float _area;      // Pre-computed surface area

    /// <param name="corner">Position of one corner of the light rectangle.</param>
    /// <param name="u">First edge vector (e.g. [2,0,0] for a 2-unit wide horizontal light).</param>
    /// <param name="v">Second edge vector (e.g. [0,0,2] for a 2-unit deep light).</param>
    /// <param name="color">Emitted light color.</param>
    /// <param name="intensity">Overall brightness scalar. Higher values = brighter light, longer reach.</param>
    /// <param name="shadowSamples">
    /// Number of random shadow rays per shading point. More samples = softer, less noisy penumbra.
    /// Recommended: 8 for preview, 16-32 for final render. Has a direct cost on render time.
    /// </param>
    public AreaLight(Vector3 corner, Vector3 u, Vector3 v, Vector3 color,
                     float intensity = 20f, int shadowSamples = 16)
    {
        Corner = corner;
        U = u;
        V = v;
        Color = color;
        Intensity = intensity;
        ShadowSamples = Math.Max(1, shadowSamples);

        Vector3 cross = Vector3.Cross(U, V);
        _area = cross.Length();
        _normal = cross / _area; // Normalized
    }

    /// <summary>
    /// Samples a uniformly random point on the light's rectangular surface.
    /// </summary>
    private Vector3 RandomSurfacePoint()
    {
        float ru = MathUtils.RandomFloat();
        float rv = MathUtils.RandomFloat();
        return Corner + ru * U + rv * V;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        Vector3 samplePoint = RandomSurfacePoint();
        return IlluminationFromPoint(hitPoint, samplePoint);
    }

    public bool IsInShadow(Vector3 hitPoint, IHittable world)
    {
        // Sample a point independently — NOTE: this is inconsistent with Illuminate.
        // Use IlluminateAndTest to guarantee the same sample point for both.
        Vector3 samplePoint = RandomSurfacePoint();
        return ShadowTestToPoint(hitPoint, samplePoint, world);
    }

    /// <summary>
    /// Atomically samples a random point on the light surface, performs the shadow ray test,
    /// AND computes the illumination contribution — all using the SAME sample point.
    /// This is the correct method to call from the renderer.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, IHittable world)
    {
        Vector3 samplePoint = RandomSurfacePoint();

        Vector3 toLight = samplePoint - hitPoint;
        float distance = toLight.Length();
        if (distance < MathUtils.Epsilon)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        Vector3 dirToLight = toLight / distance;

        // Shadow test using the same samplePoint
        var shadowRay = new Ray(hitPoint + dirToLight * MathUtils.Epsilon, dirToLight);
        var rec = new HitRecord();
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dirToLight, distance);

        var (color, dir, dist) = IlluminationFromPoint(hitPoint, samplePoint);
        return (false, color, dir, dist);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private (Vector3 Color, Vector3 DirectionToLight, float Distance) IlluminationFromPoint(
        Vector3 hitPoint, Vector3 samplePoint)
    {
        Vector3 toLight = samplePoint - hitPoint;
        float distSq = toLight.LengthSquared();
        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        // Cosine at the light surface (Lambert emitter — backlit faces emit nothing)
        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, _normal));

        // Solid-angle based attenuation: Intensity * area * cos(θ) / r²
        // Divided by ShadowSamples so the final averaged sum has correct energy.
        float attenuation = Intensity * _area * cosLight / (distSq * ShadowSamples);

        return (Color * attenuation, dirToLight, distance);
    }

    private static bool ShadowTestToPoint(Vector3 hitPoint, Vector3 samplePoint, IHittable world)
    {
        Vector3 toLight = samplePoint - hitPoint;
        float distance = toLight.Length();
        Vector3 dir = toLight / distance;
        var shadowRay = new Ray(hitPoint + dir * MathUtils.Epsilon, dir);
        var rec = new HitRecord();
        return world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);
    }
}
