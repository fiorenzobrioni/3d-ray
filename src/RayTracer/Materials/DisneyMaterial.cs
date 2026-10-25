using System.Numerics;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// The <c>disney</c> material, translated onto <see cref="OpenPbr"/>
/// (PIANO-REVIEW.md, Phase 5; owner's decision of 2026-10-21). The Disney
/// parameters keep their meaning; the light transport is OpenPBR's, which
/// conserves energy in every layer combination: the old implementation added
/// the sheen, clearcoat and transmission lobes on top of each other and
/// gained up to 39% of light (§3.17), weighted its delta lobes wrongly (§3.2)
/// and had no rough transmission in its BSDF (§3.6).
///
/// <list type="table">
/// <item><term>color, metallic, roughness</term><description>base_color, base_metalness, specular_roughness</description></item>
/// <item><term>specular (0.5)</term><description>specular_weight = 2 specular: Disney scales F0 by 2 specular</description></item>
/// <item><term>specular_tint</term><description>specular_color = lerp(1, hue of color, tint)</description></item>
/// <item><term>anisotropic, anisotropic_rotation</term><description>specular_roughness_anisotropy = 0.9 a (Disney's
/// α_y/α_x = 1 − 0.9 a), the rotation turns the tangent</description></item>
/// <item><term>sheen, sheen_tint, sheen_roughness</term><description>fuzz_weight, fuzz_color = lerp(1, hue, tint), fuzz_roughness</description></item>
/// <item><term>clearcoat, clearcoat_gloss</term><description>coat_weight; coat_roughness = √α with Disney's
/// α = lerp(0.1, 0.001, gloss), unless coat_roughness is written</description></item>
/// <item><term>spec_trans, ior</term><description>transmission_weight (subsurface_weight when the object has an
/// embedded subsurface medium), specular_ior</description></item>
/// <item><term>transmission_color, transmission_depth</term><description>with a depth: the same Beer-Lambert. Without:
/// Disney tinted every refraction, OpenPBR tints the entry only, so a solid object keeps its look with the colour
/// squared; with no colour the legacy tint √color per refraction becomes color</description></item>
/// <item><term>diff_trans</term><description>thin-walled subsurface of colour <c>color</c>, split with
/// g = 2 diff_trans − 1 so that diff_trans of the diffuse light goes through</description></item>
/// <item><term>thin_film_thickness (nm), thin_film_ior</term><description>thin_film_weight 1 when the thickness is
/// positive, thickness in micrometres</description></item>
/// </list>
///
/// The diffuse is Lambert (EON at roughness 0): Burley's retro-reflection gained
/// energy at grazing angles (§3.17).
/// </summary>
public static class DisneyMaterial
{
    private static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    private static Vector3 Hue(Vector3 c)
    {
        float l = Lum(c);
        return l > 0f ? c / l : Vector3.One;
    }

    private static FloatTexture Map(FloatTexture x, Func<float, float> f) => x.IsConstant
        ? new FloatTexture(f(x.ConstantValue))
        : new FloatTexture(new DerivedTexture(x.ToTexture(), c => new Vector3(f(c.X))));

    private static bool Is(FloatTexture? x, float v) => x == null ? v == 0f : x.IsConstant && x.ConstantValue == v;

    /// <summary>A tint towards the hue of <paramref name="baseColor"/>: lerp(1, hue, t).</summary>
    private static ITexture HueTint(ITexture baseColor, FloatTexture t)
    {
        if (t.IsConstant && t.ConstantValue <= 0f) return new SolidColor(Vector3.One);
        return new DerivedTexture(baseColor, t.ToTexture(),
            (b, w) => Vector3.Lerp(Vector3.One, Hue(Vector3.Max(b, Vector3.Zero)), Math.Clamp(w.X, 0f, 1f)));
    }

    /// <summary>Builds the OpenPBR material for a set of Disney parameters
    /// (same names and defaults as the <c>disney</c> YAML type).</summary>
    /// <param name="subsurfaceMedium">The object has an embedded subsurface medium
    /// (<c>subsurface_radius</c>): <c>spec_trans</c> is then the share of light that
    /// enters it.</param>
    public static OpenPbr Create(
        ITexture baseColor,
        FloatTexture? metallic = null,
        FloatTexture? roughness = null,
        FloatTexture? specular = null,
        FloatTexture? specularTint = null,
        FloatTexture? sheen = null,
        FloatTexture? sheenTint = null,
        FloatTexture? sheenRoughness = null,
        FloatTexture? clearcoat = null,
        FloatTexture? clearcoatGloss = null,
        FloatTexture? specTrans = null,
        FloatTexture? ior = null,
        FloatTexture? anisotropic = null,
        FloatTexture? anisotropicRotation = null,
        ITexture? transmissionColor = null,
        FloatTexture? transmissionDepth = null,
        FloatTexture? diffTrans = null,
        bool thinWalled = false,
        FloatTexture? coatIor = null,
        FloatTexture? coatRoughness = null,
        FloatTexture? thinFilmThickness = null,
        FloatTexture? thinFilmIor = null,
        bool subsurfaceMedium = false,
        NormalMapTexture? coatNormal = null)
    {
        metallic ??= 0f;
        specTrans ??= 0f;
        transmissionDepth ??= 0f;
        bool fullMetal = metallic.IsConstant && metallic.ConstantValue >= 1f;
        bool hasDiffTrans = !Is(diffTrans, 0f);
        bool thin = thinWalled || hasDiffTrans;

        // A metal's grazing colour stays white (Disney's Schlick metal) and
        // its reflectance is not scaled by 'specular'.
        FloatTexture specWeight = fullMetal ? 1f : Map(specular ?? 0.5f, s => 2f * Math.Clamp(s, 0f, 2f));
        ITexture specColor = fullMetal ? new SolidColor(Vector3.One) : HueTint(baseColor, specularTint ?? 0f);

        ITexture transColor;
        FloatTexture depth = transmissionDepth;
        if (thin)
        {
            transColor = transmissionColor ?? new DerivedTexture(baseColor, c => Vector3.SquareRoot(Vector3.Max(c, Vector3.Zero)));
        }
        else if (transmissionColor == null)
        {
            transColor = baseColor;
            depth = 0f;
        }
        else if (Is(transmissionDepth, 0f))
        {
            transColor = new DerivedTexture(transmissionColor, c => c * c);
        }
        else
        {
            transColor = transmissionColor;
        }

        var p = new OpenPbrParameters
        {
            BaseColor = baseColor,
            BaseMetalness = metallic,
            SpecularWeight = specWeight,
            SpecularColor = specColor,
            SpecularRoughness = roughness ?? 0.5f,
            SpecularRoughnessAnisotropy = Map(anisotropic ?? 0f, a => 0.9f * Math.Clamp(a, 0f, 1f)),
            SpecularRotation = anisotropicRotation ?? 0f,
            SpecularIor = ior ?? 1.5f,
            TransmissionWeight = subsurfaceMedium ? 0f : specTrans,
            TransmissionColor = transColor,
            TransmissionDepth = depth,
            SubsurfaceWeight = subsurfaceMedium && !thin ? specTrans : hasDiffTrans ? 1f : 0f,
            SubsurfaceColor = baseColor,
            SubsurfaceScatterAnisotropy = hasDiffTrans
                ? Map(diffTrans!, d => 2f * Math.Clamp(d, 0f, 1f) - 1f)
                : 0f,
            CoatWeight = clearcoat ?? 0f,
            CoatRoughness = coatRoughness
                ?? Map(clearcoatGloss ?? 1f, g => MathF.Sqrt(0.1f + (0.001f - 0.1f) * Math.Clamp(g, 0f, 1f))),
            CoatIor = coatIor ?? 1.5f,
            FuzzWeight = sheen ?? 0f,
            FuzzColor = HueTint(baseColor, sheenTint ?? 0.5f),
            FuzzRoughness = sheenRoughness ?? 0.3f,
            ThinFilmWeight = Map(thinFilmThickness ?? 0f, t => t > 0f ? 1f : 0f),
            ThinFilmThickness = Map(thinFilmThickness ?? 0f, t => MathF.Max(t, 0f) / 1000f),
            ThinFilmIor = thinFilmIor ?? 1.5f,
            ThinWalled = thin,
            CoatNormal = coatNormal,
        };
        return new OpenPbr(p) { InteriorIsMedium = subsurfaceMedium && !thin };
    }
}
