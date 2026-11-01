using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: materials (see SceneData.cs).

/// <summary>
/// A material. Every colour or scalar input marked as a parameter takes a
/// number, a colour <c>[r, g, b]</c> or a texture block.
/// </summary>
[SchemaTypes("lambertian", "metal", "dielectric", "emissive", "openpbr", "mix")]
public class MaterialData
{
    [YamlMember(Alias = "id")]
    public string? Id { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    // ── Simple types ─────────────────────────────────────────────────────────

    /// <summary>
    /// lambertian: albedo (default 0.5). metal: reflectance at normal
    /// incidence (default 1). dielectric: tint of the transmitted light
    /// (default 1). emissive: colour of the emission (a texture keeps its
    /// values, a colour is a tint of luminance 1).
    /// </summary>
    [YamlMember(Alias = "color")]
    [OnlyFor("lambertian", "metal", "dielectric", "emissive")]
    public MaterialParam? Color { get; set; }

    /// <summary>metal, dielectric: GGX roughness, α = roughness² (default 0, smooth).</summary>
    [YamlMember(Alias = "roughness")]
    [OnlyFor("metal", "dielectric")]
    public float? Roughness { get; set; }

    /// <summary>metal: F82 tint, the colour near grazing incidence (default white).</summary>
    [YamlMember(Alias = "edge_color")]
    [OnlyFor("metal")]
    public List<float>? EdgeColor { get; set; }

    /// <summary>metal: measured complex IOR (gold, silver, copper, aluminium, ...), instead of the colours.</summary>
    [YamlMember(Alias = "preset")]
    [OnlyFor("metal")]
    public string? Preset { get; set; }

    /// <summary>metal: complex IOR, real part per channel (with <c>k</c>).</summary>
    [YamlMember(Alias = "eta")]
    [OnlyFor("metal")]
    public List<float>? Eta { get; set; }

    /// <summary>metal: complex IOR, extinction per channel.</summary>
    [YamlMember(Alias = "k")]
    [OnlyFor("metal")]
    public List<float>? K { get; set; }

    /// <summary>metal: Kulla-Conty multiple scattering (default on).</summary>
    [YamlMember(Alias = "multiple_scattering")]
    [OnlyFor("metal")]
    public bool MultipleScattering { get; set; } = true;

    /// <summary>dielectric: index of refraction (default 1.5).</summary>
    [YamlMember(Alias = "ior")]
    [OnlyFor("dielectric")]
    public float Ior { get; set; } = 1.5f;

    /// <summary>emissive: luminance in nits (default 1).</summary>
    [YamlMember(Alias = "luminance")]
    [OnlyFor("emissive")]
    public float Luminance { get; set; } = 1f;

    /// <summary>emissive: colour temperature in kelvin (black body), multiplying the colour.</summary>
    [YamlMember(Alias = "temperature")]
    [OnlyFor("emissive")]
    public float? Temperature { get; set; }

    // ── Shared surface modifiers ────────────────────────────────────────────

    [YamlMember(Alias = "normal_map")]
    public NormalMapData? NormalMap { get; set; }

    [YamlMember(Alias = "bump_map")]
    public BumpMapData? BumpMap { get; set; }

    /// <summary>Displacement of the meshes using the material (on a mix: <c>blend_with_mask</c>).</summary>
    [YamlMember(Alias = "displacement")]
    public DisplacementData? Displacement { get; set; }

    // ── OpenPBR Surface v1.1 (the specification's names and defaults) ────────

    [YamlMember(Alias = "base_weight")] [OnlyFor("openpbr")] public MaterialParam? BaseWeight { get; set; }
    [YamlMember(Alias = "base_color")] [OnlyFor("openpbr")] public MaterialParam? BaseColor { get; set; }
    [YamlMember(Alias = "base_metalness")] [OnlyFor("openpbr")] public MaterialParam? BaseMetalness { get; set; }
    [YamlMember(Alias = "base_diffuse_roughness")] [OnlyFor("openpbr")] public MaterialParam? BaseDiffuseRoughness { get; set; }
    [YamlMember(Alias = "specular_weight")] [OnlyFor("openpbr")] public MaterialParam? SpecularWeight { get; set; }
    [YamlMember(Alias = "specular_color")] [OnlyFor("openpbr")] public MaterialParam? SpecularColor { get; set; }
    [YamlMember(Alias = "specular_roughness")] [OnlyFor("openpbr")] public MaterialParam? SpecularRoughness { get; set; }
    [YamlMember(Alias = "specular_roughness_anisotropy")] [OnlyFor("openpbr")] public MaterialParam? SpecularRoughnessAnisotropy { get; set; }
    /// <summary>Rotation of the anisotropic highlight around the normal, degrees.</summary>
    [YamlMember(Alias = "specular_rotation")] [OnlyFor("openpbr")] public MaterialParam? SpecularRotation { get; set; }
    [YamlMember(Alias = "specular_ior")] [OnlyFor("openpbr")] public MaterialParam? SpecularIor { get; set; }
    [YamlMember(Alias = "transmission_weight")] [OnlyFor("openpbr")] public MaterialParam? TransmissionWeight { get; set; }
    [YamlMember(Alias = "transmission_color")] [OnlyFor("openpbr")] public MaterialParam? TransmissionColor { get; set; }
    [YamlMember(Alias = "transmission_depth")] [OnlyFor("openpbr")] public MaterialParam? TransmissionDepth { get; set; }
    [YamlMember(Alias = "transmission_scatter")] [OnlyFor("openpbr")] public MaterialParam? TransmissionScatter { get; set; }
    [YamlMember(Alias = "transmission_scatter_anisotropy")] [OnlyFor("openpbr")] public MaterialParam? TransmissionScatterAnisotropy { get; set; }
    [YamlMember(Alias = "subsurface_weight")] [OnlyFor("openpbr")] public MaterialParam? SubsurfaceWeight { get; set; }
    [YamlMember(Alias = "subsurface_color")] [OnlyFor("openpbr")] public MaterialParam? SubsurfaceColor { get; set; }
    /// <summary>Mean free path in scene units (default 1), per channel times <c>subsurface_radius_scale</c>.</summary>
    [YamlMember(Alias = "subsurface_radius")] [OnlyFor("openpbr")] public float? SubsurfaceRadius { get; set; }
    [YamlMember(Alias = "subsurface_radius_scale")] [OnlyFor("openpbr")] public MaterialParam? SubsurfaceRadiusScale { get; set; }
    [YamlMember(Alias = "subsurface_scatter_anisotropy")] [OnlyFor("openpbr")] public MaterialParam? SubsurfaceScatterAnisotropy { get; set; }
    [YamlMember(Alias = "coat_weight")] [OnlyFor("openpbr")] public MaterialParam? CoatWeight { get; set; }
    [YamlMember(Alias = "coat_color")] [OnlyFor("openpbr")] public MaterialParam? CoatColor { get; set; }
    [YamlMember(Alias = "coat_roughness")] [OnlyFor("openpbr")] public MaterialParam? CoatRoughness { get; set; }
    [YamlMember(Alias = "coat_roughness_anisotropy")] [OnlyFor("openpbr")] public MaterialParam? CoatRoughnessAnisotropy { get; set; }
    [YamlMember(Alias = "coat_ior")] [OnlyFor("openpbr")] public MaterialParam? CoatIor { get; set; }
    [YamlMember(Alias = "coat_darkening")] [OnlyFor("openpbr")] public MaterialParam? CoatDarkening { get; set; }
    [YamlMember(Alias = "coat_normal_map")] [OnlyFor("openpbr")] public NormalMapData? CoatNormalMap { get; set; }
    [YamlMember(Alias = "fuzz_weight")] [OnlyFor("openpbr")] public MaterialParam? FuzzWeight { get; set; }
    [YamlMember(Alias = "fuzz_color")] [OnlyFor("openpbr")] public MaterialParam? FuzzColor { get; set; }
    [YamlMember(Alias = "fuzz_roughness")] [OnlyFor("openpbr")] public MaterialParam? FuzzRoughness { get; set; }
    [YamlMember(Alias = "thin_film_weight")] [OnlyFor("openpbr")] public MaterialParam? ThinFilmWeight { get; set; }
    /// <summary>Film thickness in micrometres (default 0.5).</summary>
    [YamlMember(Alias = "thin_film_thickness")] [OnlyFor("openpbr")] public MaterialParam? ThinFilmThickness { get; set; }
    [YamlMember(Alias = "thin_film_ior")] [OnlyFor("openpbr")] public MaterialParam? ThinFilmIor { get; set; }
    [YamlMember(Alias = "emission_luminance")] [OnlyFor("openpbr")] public MaterialParam? EmissionLuminance { get; set; }
    [YamlMember(Alias = "emission_color")] [OnlyFor("openpbr")] public MaterialParam? EmissionColor { get; set; }
    [YamlMember(Alias = "geometry_opacity")] [OnlyFor("openpbr")] public MaterialParam? GeometryOpacity { get; set; }
    [YamlMember(Alias = "geometry_thin_walled")] [OnlyFor("openpbr")] public bool GeometryThinWalled { get; set; }

    // ── mix ──────────────────────────────────────────────────────────────────

    [YamlMember(Alias = "material_a")]
    [OnlyFor("mix")]
    public string? MaterialA { get; set; }

    [YamlMember(Alias = "material_b")]
    [OnlyFor("mix")]
    public string? MaterialB { get; set; }

    /// <summary>Share of <c>material_b</c>: a number (default 0.5) or a texture mask.</summary>
    [YamlMember(Alias = "blend")]
    [OnlyFor("mix")]
    public MaterialParam? Blend { get; set; }
}

public class NormalMapData
{
    /// <summary>Tangent-space normal map image, relative to the scene.</summary>
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "strength")]
    public float Strength { get; set; } = 1f;

    [YamlMember(Alias = "uv_scale")]
    public List<float>? UvScale { get; set; }

    /// <summary>Flip the green channel (DirectX-style maps).</summary>
    [YamlMember(Alias = "flip_y")]
    public bool FlipY { get; set; }
}

public class BumpMapData
{
    /// <summary>Height field; its luminance times <c>strength</c> is a height in scene units.</summary>
    [YamlMember(Alias = "texture")]
    public TextureData? Texture { get; set; }

    [YamlMember(Alias = "strength")]
    public float Strength { get; set; } = 1f;

    /// <summary>Frequency multiplier of the height field.</summary>
    [YamlMember(Alias = "scale")]
    public float Scale { get; set; } = 1f;
}

/// <summary>
/// Displacement of a mesh: scalar <c>v + scale (h − midlevel) n</c> along the
/// smooth normals, or vector <c>v + scale (rgb − midlevel)</c> in tangent or
/// object space.
/// </summary>
public class DisplacementData
{
    [YamlMember(Alias = "mode")]
    [SchemaEnum("scalar", "vector")]
    public string? Mode { get; set; }

    /// <summary>Vector mode: <c>tangent</c> (default) or <c>object</c>.</summary>
    [YamlMember(Alias = "space")]
    [SchemaEnum("tangent", "object")]
    public string? Space { get; set; }

    [YamlMember(Alias = "texture")]
    public TextureData? Texture { get; set; }

    /// <summary>Amplitude in scene units (default 0.1).</summary>
    [YamlMember(Alias = "scale")]
    public float Scale { get; set; } = 0.1f;

    /// <summary>Texture value that means no displacement (default 0).</summary>
    [YamlMember(Alias = "midlevel")]
    public float Midlevel { get; set; }

    /// <summary>UV multiplier on top of the texture's <c>uv_scale</c>.</summary>
    [YamlMember(Alias = "uv_scale")]
    public float UvScale { get; set; } = 1f;

    /// <summary>Add the detail finer than the subdivision as a bump.</summary>
    [YamlMember(Alias = "autobump")]
    public bool Autobump { get; set; }

    [YamlMember(Alias = "autobump_strength")]
    public float AutobumpStrength { get; set; } = 1f;

    [YamlMember(Alias = "autobump_scale")]
    public float AutobumpScale { get; set; } = 1f;

    /// <summary>Largest displacement, scene units, padding the bounds (0: from <c>scale</c>).</summary>
    [YamlMember(Alias = "bound")]
    public float Bound { get; set; }

    /// <summary>On a mix: blend the two materials' displacements with the mix's blend.</summary>
    [YamlMember(Alias = "blend_with_mask")]
    public bool BlendWithMask { get; set; }

    /// <summary><c>both</c> (default), <c>displacement</c> (no autobump) or <c>bump_only</c>.</summary>
    [YamlMember(Alias = "method")]
    [SchemaEnum("both", "displacement", "bump_only")]
    public string? Method { get; set; }
}
