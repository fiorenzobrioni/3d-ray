using System.Numerics;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Material configurations the BSDF statistical kit is run on. White or
/// near-white colours so the white-furnace albedo has a known ceiling of 1.
/// Rough lobes only for the chi-square (α ≥ ~0.09): narrower lobes need a
/// finer grid than the test budget allows and are delta-sampled anyway.
/// </summary>
internal static class BsdfCatalog
{
    private static readonly Vector3 White = new(1f);
    private static readonly Vector3 Light = new(0.8f);

    private static DisneyBsdf Disney(
        Vector3 baseColor, float metallic = 0f, float roughness = 0.5f, float specular = 0.5f,
        float sheen = 0f, float clearcoat = 0f, float clearcoatGloss = 1f, float specTrans = 0f,
        float anisotropic = 0f, float diffTrans = 0f, float? coatRoughness = null)
        => new(new SolidColor(baseColor),
               metallic: metallic, roughness: roughness, specular: specular, specularTint: 0f,
               sheen: sheen, sheenTint: 0f, clearcoat: clearcoat, clearcoatGloss: clearcoatGloss,
               specTrans: specTrans, ior: 1.5f, anisotropic: anisotropic, diffTrans: diffTrans,
               coatRoughness: coatRoughness is { } cr ? new FloatTexture(cr) : null);

    public static IEnumerable<(string Name, IMaterial Material)> All()
    {
        yield return ("lambert-0.8", new Lambertian(Light));
        yield return ("metal-white-fuzz0.3", new Metal(White, 0.3f));
        yield return ("metal-white-fuzz0.6", new Metal(White, 0.6f));
        yield return ("metal-white-fuzz1.0", new Metal(White, 1.0f));
        yield return ("disney-plastic-r0.5", Disney(Light, roughness: 0.5f));
        yield return ("disney-plastic-r0.3", Disney(Light, roughness: 0.3f));
        yield return ("disney-plastic-r1.0", Disney(Light, roughness: 1.0f));
        yield return ("disney-metal-white-r0.3", Disney(White, metallic: 1f, roughness: 0.3f));
        yield return ("disney-metal-white-r0.6", Disney(White, metallic: 1f, roughness: 0.6f));
        yield return ("disney-metal-aniso-r0.5", Disney(White, metallic: 1f, roughness: 0.5f, anisotropic: 0.8f));
        yield return ("disney-sheen", Disney(Light, roughness: 0.6f, sheen: 1f));
        yield return ("disney-coat-rough", Disney(Light, roughness: 0.5f, clearcoat: 1f, coatRoughness: 0.4f));
        yield return ("disney-difftrans", Disney(Light, roughness: 0.6f, diffTrans: 0.5f));
        yield return ("disney-glass-rough0.4", Disney(White, roughness: 0.4f, specTrans: 1f));
        yield return ("disney-glass-smooth", Disney(White, roughness: 0f, specTrans: 1f));
        yield return ("disney-plastic-smooth", Disney(Light, roughness: 0f));
        yield return ("disney-coat-smooth", Disney(Light, roughness: 0.6f, clearcoat: 1f, clearcoatGloss: 1f));
        yield return ("mix-lambert-metal", new MixMaterial(new Lambertian(Light), new Metal(White, 0.5f), 0.5f));
    }

    public static readonly float[] Views = { 0.9f, 0.5f, 0.2f };
}
