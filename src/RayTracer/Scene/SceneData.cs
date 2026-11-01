using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// POCO classes of the YAML scene format, version 2 (PIANO-REVIEW.md Phase 10).
//
// The format has one name per concept: no aliases, no key kept for an older
// file. The validator (SceneValidator) reads these classes by reflection: an
// unknown key is an error, a key that has no effect on the block's `type`
// (OnlyFor) a warning, and the JSON Schema (SceneSchema) is generated from
// them. Every length is in scene units (`units.length` metres each), every
// angle in degrees, every literal colour linear Rec.709 (or "#rrggbb", sRGB).

/// <summary>A scene file.</summary>
public class SceneData
{
    /// <summary>Version of the scene format: 2. Required.</summary>
    [YamlMember(Alias = "format_version")]
    public int? FormatVersion { get; set; }

    [YamlMember(Alias = "units")]
    public UnitsData? Units { get; set; }

    /// <summary>How to render the scene; the CLI overrides it.</summary>
    [YamlMember(Alias = "render")]
    public RenderData? Render { get; set; }

    /// <summary>
    /// Scene files whose materials, mediums, lights, entities and templates
    /// join this scene's (paths relative to this file; local ids override
    /// imported ones; cycles are refused). <c>world</c>, <c>cameras</c>,
    /// <c>render</c> and <c>units</c> are not imported.
    /// </summary>
    [YamlMember(Alias = "imports")]
    public List<string>? Imports { get; set; }

    /// <summary>Cameras, picked with <c>--camera name|index</c> (default the first).</summary>
    [YamlMember(Alias = "cameras")]
    public List<CameraData>? Cameras { get; set; }

    [YamlMember(Alias = "world")]
    public WorldData? World { get; set; }

    [YamlMember(Alias = "materials")]
    public List<MaterialData>? Materials { get; set; }

    /// <summary>Participating media, bound to entities by <c>interior_medium</c> / <c>exterior_medium</c>.</summary>
    [YamlMember(Alias = "mediums")]
    public List<MediumData>? Mediums { get; set; }

    [YamlMember(Alias = "lights")]
    public List<LightData>? Lights { get; set; }

    [YamlMember(Alias = "entities")]
    public List<EntityData>? Entities { get; set; }

    /// <summary>
    /// Blueprints: no geometry until an entity of <c>type: instance</c>
    /// references one by <c>name</c>. A template's own transform composes
    /// under the instance's.
    /// </summary>
    [YamlMember(Alias = "templates")]
    public List<EntityData>? Templates { get; set; }

    /// <summary>Settings of the validation harness (<c>validation/run.py</c>); not read by the engine.</summary>
    [YamlMember(Alias = "validation")]
    [SchemaFreeForm]
    public Dictionary<string, object>? Validation { get; set; }
}

/// <summary>Physical scale of the scene.</summary>
public class UnitsData
{
    /// <summary>
    /// Length of one scene unit: metres as a number, or <c>m</c>, <c>cm</c>,
    /// <c>mm</c>, <c>km</c>, <c>in</c>, <c>ft</c>. Default 1 m. Used where a
    /// quantity carries metres: the aperture of <c>f_stop</c> (millimetres),
    /// candela and power of the lights, the atmosphere.
    /// </summary>
    [YamlMember(Alias = "length")]
    public string? Length { get; set; }
}

/// <summary>
/// How to render the scene. Each key is the CLI flag of the same name
/// (<c>light_sampling</c> = <c>--light-sampling</c>); a flag given on the
/// command line wins, and a <c>-q</c> preset given there wins over the keys it
/// sets. A preset with a <c>-tiny</c> / <c>-small</c> suffix scales the scene's
/// <c>width</c> × <c>height</c> to a quarter / a half.
/// </summary>
public class RenderData
{
    [YamlMember(Alias = "quality")]
    [SchemaEnum("draft-tiny", "draft-small", "draft", "standard-tiny", "standard-small", "standard",
                "pre-final-tiny", "pre-final-small", "pre-final", "final-tiny", "final-small", "final", "ultra")]
    public string? Quality { get; set; }

    [YamlMember(Alias = "width")] public int? Width { get; set; }
    [YamlMember(Alias = "height")] public int? Height { get; set; }
    [YamlMember(Alias = "samples")] public int? Samples { get; set; }
    [YamlMember(Alias = "depth")] public int? Depth { get; set; }
    [YamlMember(Alias = "depth_diffuse")] public int? DepthDiffuse { get; set; }
    [YamlMember(Alias = "depth_glossy")] public int? DepthGlossy { get; set; }
    [YamlMember(Alias = "depth_transmission")] public int? DepthTransmission { get; set; }
    [YamlMember(Alias = "depth_volume")] public int? DepthVolume { get; set; }
    [YamlMember(Alias = "depth_transparent")] public int? DepthTransparent { get; set; }
    [YamlMember(Alias = "shadow_samples")] public int? ShadowSamples { get; set; }

    [YamlMember(Alias = "integrator")]
    [SchemaEnum("path", "bdpt", "vcm")]
    public string? Integrator { get; set; }

    [YamlMember(Alias = "vcm_radius")] public float? VcmRadius { get; set; }

    [YamlMember(Alias = "sampler")]
    [SchemaEnum("sobol", "prng")]
    public string? Sampler { get; set; }

    [YamlMember(Alias = "mis")]
    [SchemaEnum("balance", "power")]
    public string? Mis { get; set; }

    [YamlMember(Alias = "light_sampling")]
    [SchemaEnum("auto", "all", "power", "uniform", "bvh")]
    public string? LightSampling { get; set; }

    [YamlMember(Alias = "guiding")] public bool? Guiding { get; set; }
    [YamlMember(Alias = "guiding_training")] public int? GuidingTraining { get; set; }

    [YamlMember(Alias = "adaptive")] public bool? Adaptive { get; set; }
    [YamlMember(Alias = "adaptive_threshold")] public float? AdaptiveThreshold { get; set; }
    [YamlMember(Alias = "adaptive_min_spp")] public int? AdaptiveMinSpp { get; set; }

    [YamlMember(Alias = "denoiser")]
    [SchemaEnum("none", "nlm", "nfor", "oidn")]
    public string? Denoiser { get; set; }

    [YamlMember(Alias = "denoise_quality")]
    [SchemaEnum("fast", "high")]
    public string? DenoiseQuality { get; set; }

    [YamlMember(Alias = "clamp")] public float? Clamp { get; set; }
    [YamlMember(Alias = "indirect_clamp_factor")] public float? IndirectClampFactor { get; set; }
    [YamlMember(Alias = "transparent_shadows")] public bool? TransparentShadows { get; set; }

    [YamlMember(Alias = "texture_filtering")]
    [SchemaEnum("auto", "on", "off")]
    public string? TextureFiltering { get; set; }

    [YamlMember(Alias = "sss_mode")]
    [SchemaEnum("auto", "off")]
    public string? SssMode { get; set; }

    [YamlMember(Alias = "filter")]
    [SchemaEnum("gaussian", "blackman-harris", "box")]
    public string? Filter { get; set; }

    [YamlMember(Alias = "view")]
    [SchemaEnum("agx", "aces", "standard")]
    public string? View { get; set; }

    [YamlMember(Alias = "look")]
    [SchemaEnum("none", "punchy", "golden")]
    public string? Look { get; set; }

    /// <summary>Exposure in stops, added to the camera's physical exposure.</summary>
    [YamlMember(Alias = "exposure")] public float? Exposure { get; set; }

    [YamlMember(Alias = "white_balance")] public float? WhiteBalance { get; set; }
    [YamlMember(Alias = "bit_depth")] public int? BitDepth { get; set; }
    [YamlMember(Alias = "dither")] public bool? Dither { get; set; }
    [YamlMember(Alias = "seed")] public int? Seed { get; set; }

    /// <summary>The camera to render, by name or index.</summary>
    [YamlMember(Alias = "camera")] public string? Camera { get; set; }
}
