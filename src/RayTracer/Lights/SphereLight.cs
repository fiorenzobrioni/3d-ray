using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Lights;

/// <summary>
/// A spherical area light with solid-angle sampling of the visible cap.
///
/// Instead of sampling uniformly over the entire sphere surface (which wastes
/// ~50% of samples on the invisible back hemisphere), this light samples
/// directions uniformly within the cone subtended by the sphere as seen from
/// the shading point.
///
/// <b>Solid-angle sampling algorithm (PBRT §6.2.3):</b>
///   1. Compute the half-angle of the visible cone:
///      <c>cos(θ_max) = √(1 − R²/d²)</c> where d = distance to sphere center.
///   2. Sample a direction uniformly inside that cone (uniform in solid angle):
///      <c>cos(θ) = 1 − ξ₁(1 − cos(θ_max))</c>, <c>φ = 2πξ₂</c>
///   3. Intersect the sampled ray with the sphere to find the exact surface point.
///   4. PDF w.r.t. solid angle: <c>1 / (2π(1 − cos(θ_max)))</c>
///
/// <b>Energy formula:</b>
///   <c>L = Intensity × 2π(1 − cos(θ_max)) / ShadowSamples</c>
///
/// At large distances this converges to <c>Intensity × πR²/d²</c> (equivalent to
/// a disk of area πR² facing the observer), giving intuitive intensity scaling.
///
/// Uses stratified sampling: the cone's (θ, φ) domain is divided into a
/// <c>√N × √N</c> grid and each sample is jittered within its assigned cell,
/// matching the AreaLight's stratification strategy for low-noise penumbrae.
///
/// <b>Comparison with emissive Sphere + GeometryLight:</b>
/// GeometryLight uses uniform surface sampling (wastes half the samples on
/// the back hemisphere and has 1/r² variance). SphereLight concentrates
/// all samples on the visible cap with 1/Ω variance — typically 2–10× lower
/// noise at the same sample count for small/distant spheres.
///
/// YAML example:
/// <code>
/// - type: sphere
///   position: [0, 5, 0]
///   radius: 0.5
///   color: [1.0, 0.95, 0.9]
///   intensity: 30.0
///   shadow_samples: 16
/// </code>
/// </summary>
public class SphereLight : ILight
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    /// <inheritdoc/>
    public Emissive? ProxyMaterial { get; }

    // ── Stratified sampling grid ────────────────────────────────────────────
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

    // Note: this light deliberately exposes no `SoftRadius` knob. Solid-angle
    // cone sampling produces an `L = Intensity × Ω / N` estimator that is
    // bounded above by `4π · Intensity` even when the receiver is inside the
    // emitter, so the 1/d² (or cosLight/d²) floor that PointLight/SpotLight/
    // AreaLight use to tame variance in dense media is unnecessary here.
    public SphereLight(Vector3 center, float radius, Vector3 color,
                       float intensity = 20f, int shadowSamples = 16,
                       Emissive? proxyMaterial = null)
    {
        Center = center;
        Radius = MathF.Max(radius, MathUtils.Epsilon);
        Color = color;
        Intensity = intensity;
        ShadowSamples = Math.Max(1, shadowSamples);
        ProxyMaterial = proxyMaterial;

        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
        _invSqrtSamples = 1f / _sqrtSamples;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ILight — ApproximatePower (deterministic, used for scene classification)
    // ═════════════════════════════════════════════════════════════════════════

    // Isotropic spherical emitter with radiant intensity I (W/sr) — the
    // IlluminateAndTest formula is L = I · Ω/N, so I is per-steradian and the
    // total flux integrated over 4π sr is 4π · I.
    public float ApproximatePower(AABB sceneBounds) =>
        4f * MathF.PI * MathUtils.Luminance(Color) * Intensity;

    // ═════════════════════════════════════════════════════════════════════════
    //  ILight — IlluminateAndTest (stochastic, called per-hit during render)
    // ═════════════════════════════════════════════════════════════════════════

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        // Default call — random sample (backward compat)
        return IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1);
    }

    /// <summary>
    /// Stratified version: call with a specific sample index for optimal noise reduction.
    /// The visible cone is divided into a <c>√N × √N</c> grid in (cos θ, φ) space.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal,
                                    IHittable world, int sampleIndex)
    {
        Vector3 toCenter = Center - hitPoint;
        float distSq = toCenter.LengthSquared();

        if (distSq < MathUtils.Epsilon * MathUtils.Epsilon)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        float dist = MathF.Sqrt(distSq);
        Vector3 wDir = toCenter / dist; // direction toward sphere center

        // ── Cone half-angle ─────────────────────────────────────────────────
        float sinThetaMaxSq = Radius * Radius / distSq;
        float cosThetaMax;

        if (sinThetaMaxSq >= 1f)
        {
            // Shading point is inside the sphere — entire sphere is visible
            cosThetaMax = -1f; // full sphere (4π solid angle)
        }
        else
        {
            cosThetaMax = MathF.Sqrt(1f - sinThetaMaxSq);
        }

        // ── Sample direction within the visible cone ────────────────────────
        float xi1, xi2;
        if (sampleIndex >= 0)
        {
            // Stratified jitter within the assigned grid cell
            int su = sampleIndex % _sqrtSamples;
            int sv = sampleIndex / _sqrtSamples;
            xi1 = (su + MathUtils.RandomFloat()) * _invSqrtSamples;
            xi2 = (sv + MathUtils.RandomFloat()) * _invSqrtSamples;
        }
        else
        {
            xi1 = MathUtils.RandomFloat();
            xi2 = MathUtils.RandomFloat();
        }

        // Uniform sampling within a cone of half-angle θ_max:
        //   cos(θ) = 1 - ξ₁(1 - cos(θ_max))
        //   φ      = 2πξ₂
        float cosTheta = 1f - xi1 * (1f - cosThetaMax);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * xi2;

        // Local-to-world: build an ONB with wDir as the Z axis
        Vector3 sampleDirLocal = new(
            sinTheta * MathF.Cos(phi),
            sinTheta * MathF.Sin(phi),
            cosTheta
        );
        Vector3 sampleDirWorld = LocalToWorld(sampleDirLocal, wDir);

        // ── Ray-sphere intersection to find exact surface point ─────────────
        // Ray: hitPoint + t * sampleDirWorld
        // Sphere: |X - Center|² = R²
        Vector3 oc = hitPoint - Center;
        float b = Vector3.Dot(oc, sampleDirWorld);
        float c = oc.LengthSquared() - Radius * Radius;
        float discriminant = b * b - c;

        if (discriminant < 0f)
        {
            // Numerical miss — shouldn't happen with correct cone sampling.
            // Fall back to closest point on the sphere along the direction.
            Vector3 closestPoint = Center + Vector3.Normalize(sampleDirWorld) * Radius;
            float fallbackDist = (closestPoint - hitPoint).Length();
            return (true, Vector3.Zero, sampleDirWorld, fallbackDist);
        }

        float sqrtD = MathF.Sqrt(discriminant);
        float t = -b - sqrtD; // near intersection
        if (t < MathUtils.Epsilon)
            t = -b + sqrtD; // far intersection (inside sphere case)
        if (t < MathUtils.Epsilon)
            return (true, Vector3.Zero, sampleDirWorld, 0f);

        Vector3 samplePoint = hitPoint + t * sampleDirWorld;
        float sampleDist = t;
        Vector3 dirToLight = sampleDirWorld;

        // Light-surface normal at the sample point. By construction the cone
        // sampler only produces front-facing intersections; this check guards
        // against numerical drift at grazing angles and the inside-sphere
        // fallback (cosThetaMax = -1, full uniform sphere). Aligned with
        // AreaLight: a back-face direction is treated as zero contribution
        // rather than as occlusion, so MIS sees a consistent estimator across
        // light types.
        Vector3 lightNormal = (samplePoint - Center) / Radius;
        float cosLight = MathF.Max(0f, Vector3.Dot(-dirToLight, lightNormal));
        if (cosLight <= 0f)
            return (false, Vector3.Zero, dirToLight, sampleDist);

        // ── Shadow test ─────────────────────────────────────────────────────
        // tMax is computed in shadow-ray-parameter space (relative to
        // shadowOrigin, not hitPoint). Using sampleDist directly — which is
        // measured from hitPoint — would cancel the OffsetOrigin shift when
        // dirToLight aligns with the surface normal, so the sphere's own
        // surface would self-intersect the shadow ray at t == tMax and the
        // strict `t > tMax` reject in Sphere.Hit would miss it, producing a
        // black disc directly under the light.
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        var rec = new HitRecord();
        float shadowTMax = (samplePoint - shadowOrigin).Length() - MathUtils.Epsilon;
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, shadowTMax, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dirToLight, sampleDist);

        // ── Energy computation ──────────────────────────────────────────────
        // Solid-angle sampling: each sample contributes
        //   L = Intensity × Ω / ShadowSamples
        // where Ω = 2π(1 − cos(θ_max)).
        //
        // This is the MC estimator: ∫ L_e dω ≈ (1/N) × L_e / pdf
        //   = (1/N) × Intensity × Ω
        //   = Intensity × Ω / N
        float solidAngle = 2f * MathF.PI * (1f - cosThetaMax);
        float attenuation = Intensity * solidAngle / ShadowSamples;

        return (false, Color * attenuation, dirToLight, sampleDist);
    }

    // ── MIS ─────────────────────────────────────────────────────────────────
    public bool IsDelta => false;

    /// <summary>
    /// Solid-angle PDF of uniform cone sampling toward the visible cap.
    /// Returns 1/Ω for directions inside the cone, 0 otherwise.
    /// </summary>
    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        Vector3 toCenter = Center - hitPoint;
        float distSq = toCenter.LengthSquared();
        float rSq = Radius * Radius;

        if (distSq <= rSq)
            return 1f / (4f * MathF.PI); // observer inside — full sphere

        float sinThetaMaxSq = rSq / distSq;
        float cosThetaMax = MathF.Sqrt(MathF.Max(0f, 1f - sinThetaMaxSq));

        float wiLen = wi.Length();
        if (wiLen < MathUtils.Epsilon)
            return 0f;

        float dist = MathF.Sqrt(distSq);
        Vector3 wDir = toCenter / dist;
        float cosTheta = Vector3.Dot(wi, wDir) / wiLen;
        if (cosTheta < cosThetaMax)
            return 0f;

        float solidAngle = 2f * MathF.PI * (1f - cosThetaMax);
        return solidAngle > MathUtils.Epsilon ? 1f / solidAngle : 0f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds an orthonormal basis with <paramref name="w"/> as the Z axis
    /// and transforms <paramref name="local"/> from that local frame to world space.
    ///
    /// Uses Frisvad's method (same as DisneyBsdf.TangentToWorld) with a widened
    /// singularity guard for numerical safety near the south pole.
    /// </summary>
    private static Vector3 LocalToWorld(Vector3 local, Vector3 w)
    {
        Vector3 u, v;

        if (w.Z < -0.999f)
        {
            // Near south pole — use safe fallback basis
            u = new Vector3(0f, -1f, 0f);
            v = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + w.Z);
            float b = -w.X * w.Y * a;
            u = new Vector3(1f - w.X * w.X * a, b, -w.X);
            v = new Vector3(b, 1f - w.Y * w.Y * a, -w.Y);
        }

        Vector3 result = local.X * u + local.Y * v + local.Z * w;
        float lenSq = result.LengthSquared();
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : w;
    }
}
