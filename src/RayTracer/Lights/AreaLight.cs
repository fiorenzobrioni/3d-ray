using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// A rectangular area light emitter that produces physically-based soft shadows.
///
/// Defined by a corner position and two edge vectors (U, V) forming a parallelogram.
/// Uses stratified sampling on the light surface: the rectangle is divided into a
/// grid of sub-cells, and each shadow sample picks a random point within its assigned
/// cell. This produces dramatically lower noise than pure random sampling at the same
/// sample count, with smooth penumbra gradients.
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
    public Vector3 Corner { get; }
    public Vector3 U { get; }
    public Vector3 V { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    private readonly Vector3 _normal;
    private readonly float _area;

    // ── Stratified sampling grid ────────────────────────────────────────────
    // Pre-compute the grid dimensions for stratified sampling on the light
    // surface. sqrt(N) × sqrt(N) gives the best noise reduction; the actual
    // sample count is rounded up to the nearest perfect square.
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

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
        _normal = cross / _area;

        // Pre-compute stratification grid
        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
        _invSqrtSamples = 1f / _sqrtSamples;
    }

    /// <summary>
    /// Samples a point on the light surface using stratified sampling.
    /// The light rectangle is divided into a grid of <c>_sqrtSamples × _sqrtSamples</c>
    /// cells. Given a sample index, the method identifies the cell and picks a jittered
    /// random point within it. This is far superior to pure random sampling for noise
    /// reduction in soft shadows.
    /// </summary>
    /// <param name="sampleIndex">Index of the current sample (0..ShadowSamples-1).</param>
    private Vector3 StratifiedSurfacePoint(int sampleIndex)
    {
        int su = sampleIndex % _sqrtSamples;
        int sv = sampleIndex / _sqrtSamples;

        float ru = (su + MathUtils.RandomFloat()) * _invSqrtSamples;
        float rv = (sv + MathUtils.RandomFloat()) * _invSqrtSamples;

        return Corner + ru * U + rv * V;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        // For the standalone Illuminate call, use a pure random sample
        float ru = MathUtils.RandomFloat();
        float rv = MathUtils.RandomFloat();
        Vector3 samplePoint = Corner + ru * U + rv * V;
        return IlluminationFromPoint(hitPoint, samplePoint);
    }

    /// <summary>
    /// Performs stratified shadow test + illumination for sample <paramref name="sampleIndex"/>.
    /// Both the shadow ray and the illumination contribution reference the SAME
    /// stratified sample point on the light surface (critical for unbiased results).
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        // Default call without sample index — uses random sample (backward compat)
        return IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1);
    }

    /// <summary>
    /// Stratified version: call with a specific sample index for optimal noise reduction.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
    {
        Vector3 samplePoint = sampleIndex >= 0
            ? StratifiedSurfacePoint(sampleIndex)
            : Corner + MathUtils.RandomFloat() * U + MathUtils.RandomFloat() * V;

        Vector3 toLight = samplePoint - hitPoint;
        float distSq = toLight.LengthSquared();
        if (distSq < MathUtils.Epsilon * MathUtils.Epsilon)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        // Shadow test with normal-based origin
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        var rec = new HitRecord();
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dirToLight, distance);

        // Compute illumination from this sample point
        // Cosine at the light surface (Lambert emitter — backlit faces emit nothing)
        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, _normal));

        // Solid-angle based attenuation: Intensity * area * cos(θ) / r²
        // Divided by ShadowSamples so the final summed result has correct energy.
        float attenuation = Intensity * _area * cosLight / (distSq * ShadowSamples);

        return (false, Color * attenuation, dirToLight, distance);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private (Vector3 Color, Vector3 DirectionToLight, float Distance) IlluminationFromPoint(
        Vector3 hitPoint, Vector3 samplePoint)
    {
        Vector3 toLight = samplePoint - hitPoint;
        float distSq = toLight.LengthSquared();
        float distance = MathF.Sqrt(distSq);
        Vector3 dirToLight = toLight / distance;

        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, _normal));
        float attenuation = Intensity * _area * cosLight / (distSq * ShadowSamples);

        return (Color * attenuation, dirToLight, distance);
    }
}
