using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// A composite material that interpolates between two child materials using
/// a blend factor (constant scalar or spatially-varying texture mask).
///
/// <b>Scatter (indirect lighting)</b> — Stochastic lobe selection: at each hit,
/// a random number is compared against the blend factor to select material A or B.
/// This is the standard unbiased approach used by production renderers (Blender
/// Cycles Mix Shader, Mitsuba, PBRT). No re-weighting is needed because the
/// selection probability exactly equals the blend weight.
///
/// <b>EvaluateDirect (NEE)</b> — Deterministic weighted average of both materials'
/// BRDF responses. Lower variance than stochastic selection for direct lighting
/// because both lobes contribute to every sample.
///
/// <b>Emit</b> — Weighted blend of both materials' emission. Allows smooth
/// transitions between emissive and non-emissive regions (e.g. cooling lava).
///
/// <b>Normal map</b> — The MixMaterial accepts its own normal map via the
/// standard <c>normal_map:</c> YAML field. Child materials' individual normal
/// maps are NOT applied because the Renderer perturbs the normal once at the
/// top-level material before calling Scatter/EvaluateDirect.
///
/// In YAML:
///   # Constant blend (40% rust, 60% clean metal)
///   - id: "weathered_metal"
///     type: "mix"
///     material_a: "clean_metal"
///     material_b: "rust"
///     blend: 0.4
///
///   # Texture mask (Perlin noise drives the blend)
///   - id: "patchy_rust"
///     type: "mix"
///     material_a: "clean_metal"
///     material_b: "rust"
///     mask:
///       type: "noise"
///       scale: 3.0
///       noise_strength: 2.0
///
///   # Image mask (grayscale image controls the transition)
///   - id: "decal_blend"
///     type: "mix"
///     material_a: "base_paint"
///     material_b: "scratched_metal"
///     mask:
///       type: "image"
///       path: "textures/wear_mask.png"
/// </summary>
public class MixMaterial : IMaterial
{
    /// <summary>Child material selected when blend factor is low (t → 0).</summary>
    public IMaterial MaterialA { get; }

    /// <summary>Child material selected when blend factor is high (t → 1).</summary>
    public IMaterial MaterialB { get; }

    /// <summary>
    /// Constant blend factor in [0, 1]. Used when <see cref="Mask"/> is null.
    /// 0 = 100% material A, 1 = 100% material B.
    /// </summary>
    public float Blend { get; }

    /// <summary>
    /// Optional spatially-varying blend mask. When non-null, the mask's luminance
    /// at the hit point replaces the constant <see cref="Blend"/> factor.
    /// Any texture type is supported: image, noise, marble, checker, etc.
    /// </summary>
    public ITexture? Mask { get; }

    public MixMaterial(IMaterial materialA, IMaterial materialB, float blend, ITexture? mask = null)
    {
        MaterialA = materialA;
        MaterialB = materialB;
        Blend = Math.Clamp(blend, 0f, 1f);
        Mask = mask;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Direct lighting properties
    // ═════════════════════════════════════════════════════════════════════════

    // Take the maximum of both children to ensure the Renderer's
    // needsLightSampling check is satisfied if EITHER material needs it.
    // A weighted average could cause the check to fail when one material
    // is purely specular (DiffuseWeight=0) and the other is fully diffuse,
    // resulting in missing direct illumination on the diffuse portions.

    /// <inheritdoc/>
    public float DiffuseWeight => MathF.Max(MaterialA.DiffuseWeight, MaterialB.DiffuseWeight);

    /// <inheritdoc/>
    public float SpecularExponent => MathF.Max(MaterialA.SpecularExponent, MaterialB.SpecularExponent);

    /// <inheritdoc/>
    public float SpecularStrength => MathF.Max(MaterialA.SpecularStrength, MaterialB.SpecularStrength);

    /// <inheritdoc/>
    public NormalMapTexture? NormalMap { get; set; }

    // ═════════════════════════════════════════════════════════════════════════
    // Scatter (indirect lighting — stochastic lobe selection)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stochastically selects one of the two child materials based on the blend
    /// factor evaluated at the hit point.
    ///
    /// This is mathematically equivalent to:
    ///   E[color] = (1-t) × colorA + t × colorB
    ///
    /// because P(select A) = 1−t and P(select B) = t, so the expected value
    /// over many samples converges to the weighted blend. The stochastic
    /// approach is unbiased and works correctly with any material type
    /// (including Dielectric, Disney BSDF with transmission, nested Mix, etc.)
    /// without needing to combine incompatible BSDF lobes.
    /// </summary>
    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        float t = EvaluateBlendFactor(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);

        if (MathUtils.RandomFloat() < t)
            return MaterialB.Scatter(rayIn, rec, out attenuation, out scattered);
        else
            return MaterialA.Scatter(rayIn, rec, out attenuation, out scattered);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EvaluateDirect (NEE — deterministic weighted blend)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Weighted average of both materials' BRDF responses for direct lighting.
    ///
    /// Unlike Scatter, EvaluateDirect is called once per light sample and its
    /// result is multiplied by the light color directly. A deterministic blend
    /// produces lower variance than stochastic selection here because both
    /// materials contribute to every NEE sample.
    ///
    /// NOTE: The blend factor is evaluated at (0.5, 0.5) as a representative
    /// average because EvaluateDirect does not receive UV/hit-point coordinates.
    /// This matches the same approximation used by DisneyBsdf.EvaluateDirect
    /// for textured base colors. A future refactor passing HitRecord through
    /// IMaterial.EvaluateDirect would eliminate this approximation.
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal)
    {
        // Approximate blend factor — no UV available in this interface
        float t = Mask != null
            ? MathUtils.Luminance(Mask.Value(0.5f, 0.5f, Vector3.Zero, 0))
            : Blend;
        t = Math.Clamp(t, 0f, 1f);

        Vector3 brdfA = MaterialA.EvaluateDirect(toLight, toEye, normal);
        Vector3 brdfB = MaterialB.EvaluateDirect(toLight, toEye, normal);

        return Vector3.Lerp(brdfA, brdfB, t);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Emission
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Weighted blend of both materials' emission. Enables smooth transitions
    /// between emissive and non-emissive regions (e.g. lava cooling to rock,
    /// neon tube with partial damage).
    /// </summary>
    public Vector3 Emit(float u, float v, Vector3 point, int objectSeed, bool frontFace)
    {
        float t = EvaluateBlendFactor(u, v, point, objectSeed);

        Vector3 emitA = MaterialA.Emit(u, v, point, objectSeed, frontFace);
        Vector3 emitB = MaterialB.Emit(u, v, point, objectSeed, frontFace);

        return Vector3.Lerp(emitA, emitB, t);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Blend factor evaluation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluates the blend factor at the given surface point.
    ///
    /// When a mask texture is set, its RGB output is converted to a scalar
    /// via Rec.709 luminance. This allows both grayscale masks (where all
    /// channels are equal → luminance = channel value) and color textures
    /// (where the perceptual brightness drives the blend).
    ///
    /// The result is clamped to [0, 1] for safety.
    /// </summary>
    private float EvaluateBlendFactor(float u, float v, Vector3 point, int objectSeed)
    {
        if (Mask == null)
            return Blend;

        Vector3 maskColor = Mask.Value(u, v, point, objectSeed);
        float t = MathUtils.Luminance(maskColor);
        return Math.Clamp(t, 0f, 1f);
    }
}
