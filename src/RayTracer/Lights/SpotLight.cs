using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// A spot light with position, direction, and cone angles for inner/outer falloff.
/// Combines inverse-square distance attenuation with angular cone attenuation.
///
/// <para><b>Multi-sample soft shadows (<c>shadowSamples &gt; 1</c>, requires <c>softRadius &gt; 0</c>):</b>
/// When both <c>softRadius</c> and <c>shadowSamples</c> are set, each shadow ray
/// jitters the source within a disc of radius <c>softRadius</c> perpendicular to
/// <c>Direction</c>, modelling the physical bulb extent. If <c>softRadius == 0</c>,
/// extra samples degenerate to the single-sample case (no effect on the result
/// beyond noise-averaging — avoid for efficiency).</para>
/// </summary>
public class SpotLight : ILight
{
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }
    public float CosInnerAngle { get; }
    public float CosOuterAngle { get; }

    /// <summary>
    /// Optional "virtual disc" radius used to soften the 1/d² singularity.
    /// See <see cref="PointLight.SoftRadius"/> for the rationale — same role
    /// here, particularly important for spotlights illuminating a scattering
    /// medium where shadow rays from medium events can land near the emitter.
    /// 0 = unclamped, original behaviour.
    /// </summary>
    public float SoftRadius { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    // Precomputed ONB for disc jittering when softRadius > 0 && shadowSamples > 1.
    private readonly Vector3 _tangentU;
    private readonly Vector3 _tangentV;
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

    public SpotLight(Vector3 position, Vector3 direction, Vector3 color,
                     float intensity = 1f, float innerAngleDeg = 15f, float outerAngleDeg = 30f,
                     float softRadius = 0f, int shadowSamples = 1)
    {
        Position = position;
        Direction = Vector3.Normalize(direction);
        Color = color;
        Intensity = intensity;
        CosInnerAngle = MathF.Cos(MathUtils.DegreesToRadians(innerAngleDeg));
        CosOuterAngle = MathF.Cos(MathUtils.DegreesToRadians(outerAngleDeg));
        SoftRadius = MathF.Max(0f, softRadius);
        ShadowSamples = Math.Max(1, shadowSamples);

        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
        _invSqrtSamples = 1f / _sqrtSamples;

        // Precompute an orthonormal basis perp to Direction for disc jittering.
        // Used when softRadius > 0 and shadowSamples > 1.
        if (MathF.Abs(Direction.Y) < 0.999f)
            _tangentU = Vector3.Normalize(Vector3.Cross(Direction, Vector3.UnitY));
        else
            _tangentU = Vector3.UnitX;
        _tangentV = Vector3.Cross(Direction, _tangentU);
    }

    // Finite cone emitter: Φ = I · Ω_eff where Ω_eff accounts for the smoothstep²
    // angular falloff from 1 at the inner half-angle to 0 at the outer.
    //   Ω_core   = 2π(1 − cosInner)              (full-intensity core)
    //   Ω_falloff = 2π(cosInner − cosOuter) · ⟨f²⟩   where ⟨f²⟩ = ∫₀¹ x² dx = 1/3
    // (the smoothstep ramps roughly linearly in cos θ over the ring, squared, so
    // its mean-squared contribution is ~1/3). This is an approximation but
    // matches the actual IlluminateAndTest() integral to within a small constant.
    public float ApproximatePower(AABB sceneBounds)
    {
        float coreSolidAngle = 2f * MathF.PI * (1f - CosInnerAngle);
        float falloffSolidAngle = 2f * MathF.PI * (CosInnerAngle - CosOuterAngle) / 3f;
        float omegaEff = coreSolidAngle + falloffSolidAngle;
        return MathUtils.Luminance(Color) * Intensity * omegaEff;
    }

    /// <summary>
    /// Fully inlined illumination + shadow test.
    /// Delegates to <see cref="IlluminateAndTestStratified"/> with sampleIndex = -1.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
        => IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1);

    /// <summary>
    /// Stratified variant.  When <c>softRadius &gt; 0</c> and
    /// <c>ShadowSamples &gt; 1</c> the source position is jittered within a disc
    /// of radius <c>softRadius</c> perp to <c>Direction</c>, modelling the
    /// physical bulb extent and enabling soft penumbra in fog.
    /// When <c>softRadius == 0</c> the jitter is zero and all shadow samples
    /// are identical — callers should keep <c>ShadowSamples = 1</c> in that case.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal,
                                    IHittable world, int sampleIndex)
    {
        // Source position: jitter on disc when softRadius > 0 and multi-sample.
        Vector3 sourcePos = Position;
        if (SoftRadius > 0f && ShadowSamples > 1 && sampleIndex >= 0)
        {
            int su = sampleIndex % _sqrtSamples;
            int sv = sampleIndex / _sqrtSamples;
            float xi1 = (su + MathUtils.RandomFloat()) * _invSqrtSamples;
            float xi2 = (sv + MathUtils.RandomFloat()) * _invSqrtSamples;

            // Uniform disc sampling: map [0,1)² to a disc of radius SoftRadius.
            float r = SoftRadius * MathF.Sqrt(xi1);
            float theta = 2f * MathF.PI * xi2;
            sourcePos = Position + r * (MathF.Cos(theta) * _tangentU + MathF.Sin(theta) * _tangentV);
        }

        Vector3 toLight = sourcePos - hitPoint;
        float distance = toLight.Length();
        Vector3 dirToLight = toLight / distance;

        // Early-out: outside outer cone — no light contribution at all
        float cosAngle = Vector3.Dot(-dirToLight, Direction);
        if (cosAngle < CosOuterAngle)
            return (true, Vector3.Zero, dirToLight, distance);

        // Shadow test with normal-based origin
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        var rec = new HitRecord();
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dirToLight, distance);

        // Compute illumination (only if not in shadow — avoids wasted math).
        // Soft-radius clamp: floors d² at SoftRadius² so the 1/d² term cannot
        // diverge when a medium-scattering event sits arbitrarily close to
        // the emitter. Geometric distance is still returned unchanged.
        float d2 = distance * distance;
        if (SoftRadius > 0f) d2 = MathF.Max(d2, SoftRadius * SoftRadius);
        float distanceAttenuation = Intensity / (d2 * ShadowSamples);

        float spotAttenuation = Math.Clamp(
            (cosAngle - CosOuterAngle) / (CosInnerAngle - CosOuterAngle),
            0f, 1f);
        spotAttenuation *= spotAttenuation;

        return (false, Color * distanceAttenuation * spotAttenuation, dirToLight, distance);
    }
}
