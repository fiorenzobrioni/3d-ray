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
///   shadow_samples: 4
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
                       float intensity = 20f, int shadowSamples = 4,
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
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, float time = 0f)
    {
        // Default call — random sample (backward compat)
        return IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1, time);
    }

    /// <summary>
    /// Stratified version: call with a specific sample index for optimal noise reduction.
    /// The visible cone is divided into a <c>√N × √N</c> grid in (cos θ, φ) space.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal,
                                    IHittable world, int sampleIndex, float time = 0f)
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

        Vector3 samplePoint;
        float oneMinusCosThetaMax;
        if (cosThetaMax > -1f)
        {
            // ── Outside the sphere: sample the visible cap geometrically ────
            // PBRT-v4 (Sphere::Sample): pick θ uniformly in solid angle inside
            // the cone, then place the point on the sphere through the angle α
            // it subtends at the centre, instead of intersecting a ray with the
            // sphere. The ray intersection loses the sphere near the cone's rim
            // in single precision (small or distant lights: 1-2% of the samples
            // missed and were counted as occluded, PIANO-REVIEW.md §3.19).
            // Below sin²θ_max ≈ sin²(1.5°) the cosines collapse to 1 in float,
            // so the small-angle forms are used.
            float sinThetaMax = MathF.Sqrt(sinThetaMaxSq);
            float cosTheta, sin2Theta;
            if (sinThetaMaxSq < SmallConeSin2)
            {
                sin2Theta = sinThetaMaxSq * xi1;
                cosTheta = MathF.Sqrt(1f - sin2Theta);
                oneMinusCosThetaMax = 0.5f * sinThetaMaxSq;
            }
            else
            {
                cosTheta = 1f - xi1 * (1f - cosThetaMax);
                sin2Theta = MathF.Max(0f, 1f - cosTheta * cosTheta);
                oneMinusCosThetaMax = 1f - cosThetaMax;
            }
            float cosAlpha = sin2Theta / sinThetaMax
                           + cosTheta * MathF.Sqrt(MathF.Max(0f, 1f - sin2Theta / sinThetaMaxSq));
            float sinAlpha = MathF.Sqrt(MathF.Max(0f, 1f - cosAlpha * cosAlpha));
            float phiA = 2f * MathF.PI * xi2;
            // α is measured from the centre toward the shading point (−wDir).
            Vector3 nLocal = new(sinAlpha * MathF.Cos(phiA), sinAlpha * MathF.Sin(phiA), cosAlpha);
            samplePoint = Center + Radius * LocalToWorld(nLocal, -wDir);
        }
        else
        {
            // ── Inside the sphere: uniform over the whole sphere of directions,
            // the surface point is the (only) forward intersection.
            float cosTheta = 1f - 2f * xi1;
            float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
            float phi = 2f * MathF.PI * xi2;
            Vector3 dirLocal = new(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
            Vector3 dirWorld = LocalToWorld(dirLocal, wDir);
            Vector3 oc = hitPoint - Center;
            float b = Vector3.Dot(oc, dirWorld);
            float c = oc.LengthSquared() - Radius * Radius;
            float t = -b + MathF.Sqrt(MathF.Max(0f, b * b - c));
            if (t < MathUtils.Epsilon)
                return (true, Vector3.Zero, dirWorld, 0f);
            samplePoint = hitPoint + t * dirWorld;
            oneMinusCosThetaMax = 2f;
        }

        Vector3 toSample = samplePoint - hitPoint;
        float sampleDist = toSample.Length();
        if (sampleDist < MathUtils.Epsilon)
            return (true, Vector3.Zero, wDir, 0f);
        Vector3 dirToLight = toSample / sampleDist;

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
        Vector3 trans = ShadowRay.TransmittanceToPoint(world, hitPoint, surfaceNormal, samplePoint, lightNormal, time);

        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dirToLight, sampleDist);

        // ── Energy computation ──────────────────────────────────────────────
        // Solid-angle sampling: each sample contributes
        //   L = Intensity × Ω / ShadowSamples
        // where Ω = 2π(1 − cos(θ_max)).
        //
        // This is the MC estimator: ∫ L_e dω ≈ (1/N) × L_e / pdf
        //   = (1/N) × Intensity × Ω
        //   = Intensity × Ω / N
        float solidAngle = 2f * MathF.PI * oneMinusCosThetaMax;
        float attenuation = Intensity * solidAngle / ShadowSamples;

        return (false, Color * attenuation * trans, dirToLight, sampleDist);
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

        // Same small-angle form as the sampler: a tiny light keeps a finite,
        // correct pdf instead of a zero that silently disabled its NEE weight.
        float oneMinusCosThetaMax = sinThetaMaxSq < SmallConeSin2
            ? 0.5f * sinThetaMaxSq
            : 1f - cosThetaMax;
        float solidAngle = 2f * MathF.PI * oneMinusCosThetaMax;
        return solidAngle > 0f ? 1f / solidAngle : 0f;
    }

    // sin²(1.5°): below it 1 − cos θ_max underflows in single precision and
    // the cone is handled with its small-angle expansion (PBRT-v4).
    private const float SmallConeSin2 = 0.00068523f;

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
