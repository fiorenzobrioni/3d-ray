using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

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
///   shadow_samples: 4
/// </code>
/// </summary>
public class AreaLight : ILight
{
    public Vector3 Corner { get; }
    public Vector3 U { get; }
    public Vector3 V { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <summary>
    /// Optional "virtual disc" radius used to soften the cosLight/d² singularity
    /// in the area-sampling estimator. When &gt; 0 the attenuation denominator
    /// is clamped: <c>distSq = max(distSq, softRadius²)</c>. This prevents
    /// unbounded variance when a stratified sample on the emitter falls nearly
    /// tangent to the receiver (common in dense volumetric media).
    /// 0 = unclamped, identical to pre-existing behaviour.
    /// See <see cref="PointLight.SoftRadius"/> for the same pattern on delta lights.
    /// </summary>
    public float SoftRadius { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    /// <inheritdoc/>
    public Emissive? ProxyMaterial { get; }

    private readonly Vector3 _normal;
    private readonly float _area;

    // ── Stratified sampling grid ────────────────────────────────────────────
    // Pre-compute the grid dimensions for stratified sampling on the light
    // surface. sqrt(N) × sqrt(N) gives the best noise reduction; the actual
    // sample count is rounded up to the nearest perfect square.
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

    public AreaLight(Vector3 corner, Vector3 u, Vector3 v, Vector3 color,
                     float intensity = 20f, int shadowSamples = 4, float softRadius = 0f,
                     Emissive? proxyMaterial = null)
    {
        Corner = corner;
        U = u;
        V = v;
        Color = color;
        Intensity = intensity;
        ShadowSamples = Math.Max(1, shadowSamples);
        SoftRadius = MathF.Max(0f, softRadius);
        ProxyMaterial = proxyMaterial;

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

    // Lambertian rectangular emitter: Φ = π · L · A, where L is the radiance
    // (the per-sample `Intensity` in our parameterisation — see IlluminateAndTest
    // which multiplies by area · cos / r²) and A is the surface area.
    public float ApproximatePower(AABB sceneBounds) =>
        MathF.PI * _area * MathUtils.Luminance(Color) * Intensity;

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

        // Cosine at the light surface (Lambert emitter — backlit faces emit
        // nothing). Cheap test, run BEFORE the shadow BVH walk so a receiver
        // sitting under the rect's plane doesn't fire ShadowSamples wasted
        // rays per shading point.
        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, _normal));
        if (cosLight <= 0f)
            return (false, Vector3.Zero, dirToLight, distance);

        // Shadow test with normal-based origin.
        // tMax is computed from shadowOrigin (not hitPoint) so the OffsetOrigin
        // shift does not cancel the Epsilon margin when dirToLight aligns with
        // the surface normal — otherwise the light's own geometry would register
        // as a self-intersection and produce a black halo under/around the emitter.
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        float shadowTMax = (samplePoint - shadowOrigin).Length() - MathUtils.Epsilon;
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, shadowTMax);

        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dirToLight, distance);

        // Soft-radius clamp: floors distSq at SoftRadius² so the cosLight/d²
        // term cannot diverge when a stratified sample falls nearly tangent to
        // the receiver (common in dense volumetric media near the emitter).
        // Geometric distance is returned unchanged — only the attenuation
        // denominator is clamped. Same pattern as PointLight.SoftRadius.
        float attenuationDistSq = distSq;
        if (SoftRadius > 0f) attenuationDistSq = MathF.Max(attenuationDistSq, SoftRadius * SoftRadius);

        // Solid-angle based attenuation: Intensity * area * cos(θ) / r²
        // Divided by ShadowSamples so the final summed result has correct energy.
        float attenuation = Intensity * _area * cosLight / (attenuationDistSq * ShadowSamples);

        return (false, Color * attenuation * trans, dirToLight, distance);
    }

    // ── MIS ─────────────────────────────────────────────────────────────────
    public bool IsDelta => false;

    /// <summary>
    /// Solid-angle PDF of uniform-area sampling on the rect light.
    /// Intersects the ray (hitPoint, wi) with the rect's plane and checks that
    /// the hit lies inside the parallelogram [0,1]×[0,1] in (U, V) space.
    /// pdf_ω = dist² / (area · cos θ_light) when visible, 0 otherwise.
    /// </summary>
    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        // Ray-plane intersection: rect plane has point Corner and normal _normal.
        float denom = Vector3.Dot(_normal, wi);
        if (MathF.Abs(denom) < 1e-7f)
            return 0f;

        float t = Vector3.Dot(Corner - hitPoint, _normal) / denom;
        if (t <= MathUtils.Epsilon)
            return 0f;

        Vector3 p = hitPoint + t * wi;
        Vector3 local = p - Corner;

        // Solve (u, v) such that local = u*U + v*V. Use the Gram-matrix formulation
        // (projection onto the non-orthogonal basis {U, V}).
        float uu = Vector3.Dot(U, U);
        float vv = Vector3.Dot(V, V);
        float uv = Vector3.Dot(U, V);
        float detG = uu * vv - uv * uv;
        if (detG < 1e-12f)
            return 0f;

        float lu = Vector3.Dot(local, U);
        float lv = Vector3.Dot(local, V);
        float u = (lu * vv - lv * uv) / detG;
        float v = (lv * uu - lu * uv) / detG;

        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return 0f;

        float cosLight = MathF.Abs(denom); // |-wi · n_light| = |wi · n|
        if (cosLight < 1e-6f)
            return 0f;

        // distance from hitPoint to the rect hit = t (since wi is unit).
        return (t * t) / (_area * cosLight);
    }

    /// <inheritdoc/>
    public bool TrySampleEmissivePoint(out Vector3 point, out Vector3 normal,
                                       out Vector3 emission, out float pdfArea)
    {
        float r1 = MathUtils.RandomFloat();
        float r2 = MathUtils.RandomFloat();
        point    = Corner + r1 * U + r2 * V;
        normal   = _normal;
        emission = Color * Intensity;
        pdfArea  = _area > 1e-12f ? 1f / _area : 0f;
        return pdfArea > 0f;
    }
}
