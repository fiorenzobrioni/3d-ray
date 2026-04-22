using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// A spot light with position, direction, and cone angles for inner/outer falloff.
/// Combines inverse-square distance attenuation with angular cone attenuation.
/// </summary>
public class SpotLight : ILight
{
    public Vector3 Position { get; }
    public Vector3 Direction { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }
    public float CosInnerAngle { get; }
    public float CosOuterAngle { get; }

    /// <inheritdoc/>
    public int ShadowSamples => 1;

    public SpotLight(Vector3 position, Vector3 direction, Vector3 color,
                     float intensity = 1f, float innerAngleDeg = 15f, float outerAngleDeg = 30f)
    {
        Position = position;
        Direction = Vector3.Normalize(direction);
        Color = color;
        Intensity = intensity;
        CosInnerAngle = MathF.Cos(MathUtils.DegreesToRadians(innerAngleDeg));
        CosOuterAngle = MathF.Cos(MathUtils.DegreesToRadians(outerAngleDeg));
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
    /// The previous implementation called Illuminate() then IsInShadow() separately,
    /// which computed the direction vector, distance, and cone angle redundantly.
    /// This version does it once and also uses normal-based shadow origin.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        Vector3 toLight = Position - hitPoint;
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

        // Compute illumination (only if not in shadow — avoids wasted math)
        float distanceAttenuation = Intensity / (distance * distance);

        float spotAttenuation = Math.Clamp(
            (cosAngle - CosOuterAngle) / (CosInnerAngle - CosOuterAngle),
            0f, 1f);
        spotAttenuation *= spotAttenuation;

        return (false, Color * distanceAttenuation * spotAttenuation, dirToLight, distance);
    }
}
