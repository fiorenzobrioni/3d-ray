using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

public class PointLight : ILight
{
    /// <summary>
    /// Radius of the virtual spherical bulb used when this point light drives an
    /// MNEE caustic. A mathematical point has zero emitter area, which the
    /// manifold estimator cannot integrate; a real "point" light is the
    /// idealization of a small bulb, so we model it as a uniform sphere of this
    /// radius (smaller = sharper but noisier caustic). Resolved from
    /// <see cref="SoftRadius"/>, falling back to this default when unset.
    /// </summary>
    public const float DefaultBulbRadius = 0.05f;

    public Vector3 Position { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <summary>
    /// Optional "virtual disc" radius used to soften the 1/d² singularity.
    /// When &gt; 0 the attenuation denominator is clamped to max(d², r²),
    /// which removes the unbounded variance at d → 0 (critical for fog +
    /// participating-media scenes where scattering events can land
    /// arbitrarily close to the emitter). 0 = unclamped, original behaviour.
    /// </summary>
    public float SoftRadius { get; }

    /// <inheritdoc/>
    public int ShadowSamples => 1;

    public PointLight(Vector3 position, Vector3 color, float intensity = 1f, float softRadius = 0f)
    {
        Position = position;
        Color = color;
        Intensity = intensity;
        SoftRadius = MathF.Max(0f, softRadius);
    }

    // Isotropic point emitter: Φ = 4π · I · Luminance(Color).
    // Integrating radiant intensity I (W/sr) over the full sphere.
    public float ApproximatePower(AABB sceneBounds) =>
        4f * MathF.PI * MathUtils.Luminance(Color) * Intensity;

    /// <summary>
    /// Inlined illumination + shadow test using normal-based shadow origin.
    /// Previous implementation called Illuminate() then IsInShadow() separately,
    /// redundantly computing the direction vector twice.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        Vector3 toLight = Position - hitPoint;
        float distance = toLight.Length();
        Vector3 dirToLight = toLight / distance;

        // Robust shadow origin: offset along the surface normal toward the light side.
        // This prevents self-intersection at grazing angles where the old method
        // (offset along ray direction) could fail.
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dirToLight);
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, distance - MathUtils.Epsilon);

        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dirToLight, distance);

        // Soft-radius clamp: floors d² at SoftRadius² so the 1/d² term cannot
        // diverge when a shading point (typically a medium-scattering event)
        // sits arbitrarily close to the emitter. Geometric distance is still
        // returned unchanged — only the attenuation denominator is clamped.
        float d2 = distance * distance;
        if (SoftRadius > 0f) d2 = MathF.Max(d2, SoftRadius * SoftRadius);
        float attenuation = Intensity / d2;
        return (false, Color * attenuation * trans, dirToLight, distance);
    }

    // ── MNEE caustic sampling ────────────────────────────────────────────────

    /// <summary>
    /// Samples a point on the virtual bulb (radius <see cref="SoftRadius"/>, or
    /// <see cref="DefaultBulbRadius"/> when unset) for the manifold caustic walk.
    ///
    /// <para>The bulb radiance is <c>Color·Intensity/(πr²)</c>: a uniform sphere
    /// of radius r and radiance L_e has radiant intensity I = L_e·πr², so matching
    /// this light's intensity <c>Color·Intensity</c> gives L_e = <c>Color·Intensity/(πr²)</c>.
    /// The trivial (no-caster) connection then reproduces <c>Intensity/d²</c>. The
    /// caustic contribution is independent of r — the <c>1/(πr²)</c> radiance
    /// cancels the <c>4πr²</c> from <c>1/pdf_A</c> — so r controls only sharpness
    /// and variance.</para>
    /// </summary>
    public bool TrySampleEmissivePoint(out Vector3 point, out Vector3 normal,
                                       out Vector3 emission, out float pdfArea)
    {
        float r = SoftRadius > 0f ? SoftRadius : DefaultBulbRadius;
        Vector3 dir = MathUtils.RandomUnitVector();
        point    = Position + r * dir;
        normal   = dir;
        emission = Color * Intensity / (MathF.PI * r * r);
        float area = 4f * MathF.PI * r * r;
        pdfArea  = 1f / area;
        return true;
    }
}
