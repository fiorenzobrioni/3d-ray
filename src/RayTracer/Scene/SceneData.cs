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

public class WorldData
{
    [YamlMember(Alias = "sky")]
    public SkyData? Sky { get; set; }

    [YamlMember(Alias = "ground")]
    public GroundData? Ground { get; set; }

    /// <summary>The medium filling the scene (fog, atmosphere).</summary>
    [YamlMember(Alias = "medium")]
    public MediumData? Medium { get; set; }
}

/// <summary>
/// Per-ray-category visibility: <c>false</c> makes the surface invisible to
/// rays of that category, which go on as if it were not there.
/// </summary>
public class VisibilityData
{
    [YamlMember(Alias = "camera")] public bool Camera { get; set; } = true;
    [YamlMember(Alias = "diffuse")] public bool Diffuse { get; set; } = true;
    [YamlMember(Alias = "glossy")] public bool Glossy { get; set; } = true;
    [YamlMember(Alias = "transmission")] public bool Transmission { get; set; } = true;
    [YamlMember(Alias = "shadow")] public bool Shadow { get; set; } = true;
}

/// <summary>The ground: a shorthand for a plane, quad or disk under the scene.</summary>
[SchemaTypes("infinite_plane", "quad", "disk")]
public class GroundData
{
    /// <summary>Default <c>infinite_plane</c>.</summary>
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>
    /// Material id. Unset: a Lambertian of the sky's <c>ground_albedo</c>
    /// (physical skies) or <c>ground_color</c> (gradient), else grey 0.5.
    /// </summary>
    [YamlMember(Alias = "material")]
    public string? Material { get; set; }

    /// <summary>A point on the plane, or the centre of the quad / disk. Default the origin.</summary>
    [YamlMember(Alias = "point")]
    public List<float>? Point { get; set; }

    /// <summary>Surface normal, default <c>[0, 1, 0]</c>.</summary>
    [YamlMember(Alias = "normal")]
    public List<float>? Normal { get; set; }

    /// <summary>Half side of the quad, radius of the disk. Default 50.</summary>
    [YamlMember(Alias = "size")]
    [OnlyFor("quad", "disk")]
    public float Size { get; set; } = 50f;

    /// <summary>UV tiling <c>[u, v]</c> on top of the surface's own UVs.</summary>
    [YamlMember(Alias = "uv_scale")]
    public List<float>? UvScale { get; set; }

    [YamlMember(Alias = "uv_offset")]
    public List<float>? UvOffset { get; set; }

    /// <summary>UV rotation in degrees around (0.5, 0.5).</summary>
    [YamlMember(Alias = "uv_rotation")]
    public float UvRotation { get; set; }

    [YamlMember(Alias = "visibility")]
    public VisibilityData? Visibility { get; set; }
}

/// <summary>The environment: what a ray that leaves the scene sees.</summary>
[SchemaTypes("flat", "gradient", "hdri", "preetham", "hosek_wilkie", "nishita")]
public class SkyData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>Radiance of a flat sky (nits).</summary>
    [YamlMember(Alias = "color")]
    [OnlyFor("flat")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "zenith_color")]
    [OnlyFor("gradient")]
    public List<float>? ZenithColor { get; set; }

    [YamlMember(Alias = "horizon_color")]
    [OnlyFor("gradient")]
    public List<float>? HorizonColor { get; set; }

    /// <summary>Below the horizon (gradient), and the albedo of <c>world.ground</c> when it has no material.</summary>
    [YamlMember(Alias = "ground_color")]
    public List<float>? GroundColor { get; set; }

    /// <summary>Equirectangular image (<c>.hdr</c>, <c>.exr</c>), relative to the scene.</summary>
    [YamlMember(Alias = "path")]
    [OnlyFor("hdri")]
    public string? Path { get; set; }

    /// <summary>Brightness in stops (×2^exposure).</summary>
    [YamlMember(Alias = "exposure")]
    public float Exposure { get; set; }

    /// <summary>Rotation of the sky, Euler degrees as an entity's <c>rotate</c>.</summary>
    [YamlMember(Alias = "rotate")]
    public List<float>? Rotate { get; set; }

    [YamlMember(Alias = "rotate_order")]
    [SchemaEnum("xyz", "xzy", "yxz", "yzx", "zxy", "zyx")]
    public string? RotateOrder { get; set; }

    [YamlMember(Alias = "sun")]
    [OnlyFor("gradient", "hdri", "preetham", "hosek_wilkie", "nishita")]
    public SunData? Sun { get; set; }

    /// <summary>Atmospheric turbidity: 1 pristine, 3 clear (default), 5 haze, 10 smog.</summary>
    [YamlMember(Alias = "turbidity")]
    [OnlyFor("preetham", "hosek_wilkie", "nishita")]
    public float Turbidity { get; set; } = 3f;

    /// <summary>
    /// Albedo of the ground below the horizon of a physical sky (default 0.3),
    /// and of <c>world.ground</c> when it has no material.
    /// </summary>
    [YamlMember(Alias = "ground_albedo")]
    public List<float>? GroundAlbedo { get; set; }

    [YamlMember(Alias = "visibility")]
    public VisibilityData? Visibility { get; set; }

    /// <summary>A separate plate shown to camera rays only.</summary>
    [YamlMember(Alias = "background")]
    public SkyData? Background { get; set; }
}

/// <summary>
/// The sun of a sky. Gradient sky: a disc of <c>illuminance</c> lux. HDRI:
/// a sun extracted from the image. Physical skies: the direction only (the
/// disc follows from the atmosphere).
/// </summary>
public class SunData
{
    /// <summary>Direction towards the sun.</summary>
    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }

    /// <summary>Tint of the gradient sky's disc (luminance 1).</summary>
    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    /// <summary>Gradient sky: illuminance of the disc on a surface facing it, lux.</summary>
    [YamlMember(Alias = "illuminance")]
    public float? Illuminance { get; set; }

    /// <summary>Gradient sky: half-angle of the disc in degrees (default 1.5; the real sun 0.265).</summary>
    [YamlMember(Alias = "angular_radius")]
    public float? AngularRadius { get; set; }

    /// <summary>HDRI: extract the brightest peak as an analytic sun.</summary>
    [YamlMember(Alias = "extract_from_hdri")]
    public bool ExtractFromHdri { get; set; }

    /// <summary>HDRI: threshold of the extraction, multiple of the mean luminance (default 50).</summary>
    [YamlMember(Alias = "extract_threshold")]
    public float ExtractThreshold { get; set; } = 50f;

    [YamlMember(Alias = "shadow_samples")]
    public int ShadowSamples { get; set; } = 4;

    /// <summary>Only <c>camera</c> applies: <c>false</c> hides the disc from camera rays.</summary>
    [YamlMember(Alias = "visibility")]
    public VisibilityData? Visibility { get; set; }
}

/// <summary>
/// A thin-lens camera: a field of view, or the photographic
/// <c>focal_length</c> on a <c>sensor_width</c>; a lens <c>aperture</c> in
/// scene units, or an <c>f_stop</c>.
/// </summary>
public class CameraData
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "position")]
    public List<float>? Position { get; set; }

    [YamlMember(Alias = "look_at")]
    public List<float>? LookAt { get; set; }

    /// <summary>Up direction, default <c>[0, 1, 0]</c>.</summary>
    [YamlMember(Alias = "up")]
    public List<float>? Up { get; set; }

    /// <summary>Vertical field of view, degrees (default 60).</summary>
    [YamlMember(Alias = "fov")]
    public float Fov { get; set; } = 60f;

    /// <summary>Focal length in millimetres: with <c>sensor_width</c> it sets the field of view.</summary>
    [YamlMember(Alias = "focal_length")]
    public float? FocalLength { get; set; }

    /// <summary>Sensor width in millimetres, spanning the image width (default 36).</summary>
    [YamlMember(Alias = "sensor_width")]
    public float SensorWidth { get; set; } = 36f;

    /// <summary>Lens diameter in scene units (0 = pinhole).</summary>
    [YamlMember(Alias = "aperture")]
    public float Aperture { get; set; }

    /// <summary>f-number: the aperture is the focal length over N.</summary>
    [YamlMember(Alias = "f_stop")]
    public float? FStop { get; set; }

    /// <summary>Straight aperture blades (polygonal bokeh); 0 = round.</summary>
    [YamlMember(Alias = "aperture_blades")]
    public int ApertureBlades { get; set; }

    /// <summary>Rotation of the blade polygon, degrees.</summary>
    [YamlMember(Alias = "aperture_rotation")]
    public float ApertureRotation { get; set; }

    /// <summary>Aperture width / height (below 1: anamorphic oval bokeh).</summary>
    [YamlMember(Alias = "aperture_ratio")]
    public float ApertureRatio { get; set; } = 1f;

    /// <summary>Distance of the plane in focus, along the view axis (default 1).</summary>
    [YamlMember(Alias = "focus_distance")]
    public float FocusDistance { get; set; } = 1f;

    /// <summary>A point in focus: wins over <c>focus_distance</c>.</summary>
    [YamlMember(Alias = "focus_point")]
    public List<float>? FocusPoint { get; set; }

    /// <summary>ISO of the physical exposure (with <c>shutter_speed</c> and <c>f_stop</c>).</summary>
    [YamlMember(Alias = "iso")]
    public float? Iso { get; set; }

    /// <summary>Exposure time in seconds (0.008 = 1/125 s).</summary>
    [YamlMember(Alias = "shutter_speed")]
    public float? ShutterSpeed { get; set; }

    /// <summary>Exposure value at ISO 100 (15 sunny day, 7 lit interior), instead of ISO, shutter and f-number.</summary>
    [YamlMember(Alias = "ev100")]
    public float? Ev100 { get; set; }

    /// <summary>White balance, kelvin: a light of this colour temperature renders neutral.</summary>
    [YamlMember(Alias = "white_balance")]
    public float? WhiteBalance { get; set; }

    /// <summary>Motion-blur interval <c>[open, close]</c> inside the animation range [0, 1].</summary>
    [YamlMember(Alias = "shutter_interval")]
    public List<float>? ShutterInterval { get; set; }

    /// <summary>Camera keyframes; the base pose is the key at time 0.</summary>
    [YamlMember(Alias = "motion")]
    public List<CameraMotionKeyData>? Motion { get; set; }
}

public class CameraMotionKeyData
{
    [YamlMember(Alias = "time")] public float Time { get; set; }
    [YamlMember(Alias = "position")] public List<float>? Position { get; set; }
    [YamlMember(Alias = "look_at")] public List<float>? LookAt { get; set; }
    [YamlMember(Alias = "up")] public List<float>? Up { get; set; }
    [YamlMember(Alias = "fov")] public float? Fov { get; set; }
}

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

/// <summary>A texture: an image or a procedural pattern.</summary>
[SchemaTypes("image", "checker", "noise", "marble", "wood", "voronoi", "brick", "gradient", "coordinate")]
public class TextureData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>Image file, relative to the scene.</summary>
    [YamlMember(Alias = "path")]
    [OnlyFor("image")]
    public string? Path { get; set; }

    [YamlMember(Alias = "uv_scale")]
    [OnlyFor("image")]
    public List<float>? UvScale { get; set; }

    /// <summary>
    /// How the stored values relate to linear light: <c>srgb</c> or
    /// <c>linear</c>. Unset: sRGB where the texture is a colour, linear where
    /// it is data (roughness, weights, masks, heights).
    /// </summary>
    [YamlMember(Alias = "color_space")]
    [OnlyFor("image")]
    [SchemaEnum("srgb", "linear")]
    public string? ColorSpace { get; set; }

    /// <summary>The pattern's colours (two; brick three: brick, alternate brick, mortar).</summary>
    [YamlMember(Alias = "colors")]
    [OnlyFor("checker", "noise", "marble", "wood", "voronoi", "brick", "gradient")]
    public List<List<float>>? Colors { get; set; }

    /// <summary>Frequency: a number, or <c>[sx, sy, sz]</c> to stretch a solid pattern per axis.</summary>
    [YamlMember(Alias = "scale")]
    [OnlyFor("checker", "noise", "marble", "wood", "voronoi", "coordinate")]
    public object? Scale { get; set; }

    /// <summary>
    /// Parses <see cref="Scale"/> into <c>(scalar, ratio)</c>: the dominant
    /// frequency (it drives the octave clamp) and a per-axis ratio in [-1, 1]
    /// applied to the sample point. A number gives a ratio of 1.
    /// </summary>
    public (float Scalar, float Rx, float Ry, float Rz) ScaleScalarAndRatio()
    {
        if (Scale is null) return (1f, 1f, 1f, 1f);

        float x, y, z;
        if (Scale is System.Collections.IEnumerable seq and not string)
        {
            var vals = new List<float>();
            foreach (var o in seq)
            {
                if (float.TryParse(System.Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var f))
                    vals.Add(f);
            }
            if (vals.Count == 0) return (1f, 1f, 1f, 1f);
            if (vals.Count == 1) return (vals[0], 1f, 1f, 1f);
            x = vals[0];
            y = vals.Count > 1 ? vals[1] : vals[0];
            z = vals.Count > 2 ? vals[2] : vals[0];
        }
        else
        {
            float s = System.Convert.ToSingle(Scale, System.Globalization.CultureInfo.InvariantCulture);
            return (s, 1f, 1f, 1f);
        }

        float m = System.MathF.Max(System.MathF.Abs(x), System.MathF.Max(System.MathF.Abs(y), System.MathF.Abs(z)));
        if (m < 1e-8f) return (1f, 1f, 1f, 1f);
        return (m, x / m, y / m, z / m);
    }

    /// <summary>Amplitude of the turbulence (noise), of the vein distortion (marble), of the grain (wood).</summary>
    [YamlMember(Alias = "noise_strength")]
    [OnlyFor("noise", "marble", "wood")]
    public float? NoiseStrength { get; set; }

    [YamlMember(Alias = "offset")]
    [OnlyFor("noise", "marble", "wood", "voronoi", "brick", "gradient", "coordinate")]
    public List<float>? Offset { get; set; }

    /// <summary>Rotation of the pattern space, Euler degrees.</summary>
    [YamlMember(Alias = "rotation")]
    [OnlyFor("noise", "marble", "wood", "voronoi", "brick", "gradient", "coordinate")]
    public List<float>? Rotation { get; set; }

    /// <summary>A per-object random offset (from the entity's seed).</summary>
    [YamlMember(Alias = "randomize_offset")]
    [OnlyFor("noise", "marble", "wood", "voronoi", "brick", "gradient")]
    public bool RandomizeOffset { get; set; }

    [YamlMember(Alias = "randomize_rotation")]
    [OnlyFor("noise", "marble", "wood", "voronoi", "brick", "gradient")]
    public bool RandomizeRotation { get; set; }

    [YamlMember(Alias = "noise_type")]
    [OnlyFor("noise")]
    [SchemaEnum("auto", "perlin", "fbm", "turbulence", "ridged", "billow", "hetero_terrain", "hybrid_multifractal")]
    public string? NoiseTypeName { get; set; }

    [YamlMember(Alias = "octaves")]
    [OnlyFor("noise", "marble", "wood")]
    public int? Octaves { get; set; }

    [YamlMember(Alias = "lacunarity")]
    [OnlyFor("noise", "marble", "wood")]
    public float? Lacunarity { get; set; }

    [YamlMember(Alias = "gain")]
    [OnlyFor("noise", "marble", "wood")]
    public float? Gain { get; set; }

    [YamlMember(Alias = "distortion")]
    [OnlyFor("noise", "voronoi")]
    public float? Distortion { get; set; }

    [YamlMember(Alias = "fractal_increment")]
    [OnlyFor("noise")]
    public float? FractalIncrement { get; set; }

    [YamlMember(Alias = "fractal_offset")]
    [OnlyFor("noise")]
    public float? FractalOffset { get; set; }

    // ── marble ───────────────────────────────────────────────────────────────

    [YamlMember(Alias = "vein_axis")] [OnlyFor("marble")] public List<float>? VeinAxis { get; set; }
    [YamlMember(Alias = "warp_amplitude")] [OnlyFor("marble", "wood")] public float? WarpAmplitude { get; set; }
    [YamlMember(Alias = "warp_scale")] [OnlyFor("marble", "wood")] public float? WarpScale { get; set; }
    [YamlMember(Alias = "warp_iterations")] [OnlyFor("marble", "wood")] public int? WarpIterations { get; set; }
    [YamlMember(Alias = "fold_amplitude")] [OnlyFor("marble", "wood")] public List<float>? FoldAmplitude { get; set; }
    [YamlMember(Alias = "fold_scale")] [OnlyFor("marble", "wood")] public float? FoldScale { get; set; }
    [YamlMember(Alias = "vein_layers")] [OnlyFor("marble")] public int? VeinLayers { get; set; }
    [YamlMember(Alias = "vein_scale")] [OnlyFor("marble")] public List<float>? VeinScale { get; set; }
    [YamlMember(Alias = "vein_weight")] [OnlyFor("marble")] public List<float>? VeinWeight { get; set; }
    [YamlMember(Alias = "vein_thickness")] [OnlyFor("marble")] public float? VeinThickness { get; set; }
    [YamlMember(Alias = "vein_softness")] [OnlyFor("marble")] public float? VeinSoftness { get; set; }
    [YamlMember(Alias = "soft_max_sharpness")] [OnlyFor("marble")] public float? SoftMaxSharpness { get; set; }
    [YamlMember(Alias = "background_scale")] [OnlyFor("marble")] public float? BackgroundScale { get; set; }
    [YamlMember(Alias = "background_octaves")] [OnlyFor("marble")] public int? BackgroundOctaves { get; set; }
    [YamlMember(Alias = "impurities_density")] [OnlyFor("marble")] public float? ImpuritiesDensity { get; set; }
    [YamlMember(Alias = "impurities_scale")] [OnlyFor("marble")] public float? ImpuritiesScale { get; set; }
    [YamlMember(Alias = "impurity_weight")] [OnlyFor("marble")] public float? ImpurityWeight { get; set; }
    [YamlMember(Alias = "impurities_texture")] [OnlyFor("marble")] public TextureData? ImpuritiesTexture { get; set; }
    [YamlMember(Alias = "space_stretch")] [OnlyFor("marble", "wood")] public List<float>? SpaceStretch { get; set; }
    [YamlMember(Alias = "cracks_density")] [OnlyFor("marble")] public float? CracksDensity { get; set; }
    [YamlMember(Alias = "cracks_scale")] [OnlyFor("marble")] public float? CracksScale { get; set; }
    [YamlMember(Alias = "cracks_softness")] [OnlyFor("marble")] public float? CracksSoftness { get; set; }
    [YamlMember(Alias = "cracks_weight")] [OnlyFor("marble")] public float? CracksWeight { get; set; }

    // ── wood ─────────────────────────────────────────────────────────────────

    [YamlMember(Alias = "ring_axis")] [OnlyFor("wood")] public List<float>? RingAxis { get; set; }
    [YamlMember(Alias = "ring_sharpness")] [OnlyFor("wood")] public float? RingSharpness { get; set; }
    [YamlMember(Alias = "axial_grain")] [OnlyFor("wood")] public float? AxialGrain { get; set; }
    [YamlMember(Alias = "grain_scale")] [OnlyFor("wood")] public float? GrainScale { get; set; }
    [YamlMember(Alias = "figure_scale")] [OnlyFor("wood")] public float? FigureScale { get; set; }
    [YamlMember(Alias = "figure_strength")] [OnlyFor("wood")] public float? FigureStrength { get; set; }
    [YamlMember(Alias = "figure_aspect")] [OnlyFor("wood")] public float? FigureAspect { get; set; }
    [YamlMember(Alias = "radial_anisotropy")] [OnlyFor("wood")] public float? RadialAnisotropy { get; set; }
    [YamlMember(Alias = "knot_density")] [OnlyFor("wood")] public float? KnotDensity { get; set; }
    [YamlMember(Alias = "knot_scale")] [OnlyFor("wood")] public float? KnotScale { get; set; }
    [YamlMember(Alias = "latewood_width")] [OnlyFor("wood")] public float? LatewoodWidth { get; set; }
    [YamlMember(Alias = "earlywood_transition")] [OnlyFor("wood")] public float? EarlywoodTransition { get; set; }
    [YamlMember(Alias = "ring_color_variation")] [OnlyFor("wood")] public float? RingColorVariation { get; set; }
    [YamlMember(Alias = "ring_width_variation")] [OnlyFor("wood")] public float? RingWidthVariation { get; set; }
    [YamlMember(Alias = "pore_density")] [OnlyFor("wood")] public float? PoreDensity { get; set; }
    [YamlMember(Alias = "pore_scale")] [OnlyFor("wood")] public float? PoreScale { get; set; }
    [YamlMember(Alias = "pore_strength")] [OnlyFor("wood")] public float? PoreStrength { get; set; }
    [YamlMember(Alias = "pore_aspect")] [OnlyFor("wood")] public float? PoreAspect { get; set; }
    [YamlMember(Alias = "heartwood_radius")] [OnlyFor("wood")] public float? HeartwoodRadius { get; set; }
    [YamlMember(Alias = "heartwood_blend")] [OnlyFor("wood")] public float? HeartwoodBlend { get; set; }

    /// <summary>marble, wood: <c>color</c> or <c>mask</c>; voronoi: the feature it outputs.</summary>
    [YamlMember(Alias = "output")]
    [OnlyFor("marble", "wood", "voronoi")]
    [SchemaEnum("color", "mask", "f1", "f2", "f3", "f4", "f2_minus_f1", "f3_minus_f1", "f1_plus_f2", "cell", "random", "position")]
    public string? Output { get; set; }

    // ── voronoi ──────────────────────────────────────────────────────────────

    [YamlMember(Alias = "metric")]
    [OnlyFor("voronoi")]
    [SchemaEnum("euclidean", "euclidean_squared", "manhattan", "chebyshev")]
    public string? Metric { get; set; }

    [YamlMember(Alias = "randomness")] [OnlyFor("voronoi")] public float? Randomness { get; set; }
    [YamlMember(Alias = "smoothness")] [OnlyFor("voronoi")] public float? Smoothness { get; set; }

    // ── brick ────────────────────────────────────────────────────────────────

    [YamlMember(Alias = "brick_width")] [OnlyFor("brick")] public float? BrickWidth { get; set; }
    [YamlMember(Alias = "brick_height")] [OnlyFor("brick")] public float? BrickHeight { get; set; }
    [YamlMember(Alias = "mortar_size")] [OnlyFor("brick")] public float? MortarSize { get; set; }
    [YamlMember(Alias = "row_offset")] [OnlyFor("brick")] public float? RowOffset { get; set; }
    [YamlMember(Alias = "color_variation")] [OnlyFor("brick", "marble")] public float? ColorVariation { get; set; }
    [YamlMember(Alias = "noise_scale")] [OnlyFor("brick")] public float? NoiseScale { get; set; }

    // ── gradient, coordinate ─────────────────────────────────────────────────

    /// <summary>gradient: linear, quadratic, easing, spherical, radial. coordinate: object, uv, generated, world.</summary>
    [YamlMember(Alias = "mode")]
    [OnlyFor("gradient", "coordinate")]
    [SchemaEnum("linear", "quadratic", "easing", "spherical", "radial", "object", "uv", "generated", "world")]
    public string? Mode { get; set; }

    [YamlMember(Alias = "axis")] [OnlyFor("gradient")] public List<float>? Axis { get; set; }
    [YamlMember(Alias = "length")] [OnlyFor("gradient")] public float? Length { get; set; }

    /// <summary>Multi-stop colour ramp, replacing <c>colors</c>.</summary>
    [YamlMember(Alias = "color_ramp")]
    [OnlyFor("noise", "marble", "wood", "voronoi", "gradient")]
    public List<ColorRampStopData>? ColorRamp { get; set; }

    [YamlMember(Alias = "bounds_min")] [OnlyFor("coordinate")] public List<float>? BoundsMin { get; set; }
    [YamlMember(Alias = "bounds_max")] [OnlyFor("coordinate")] public List<float>? BoundsMax { get; set; }
}

public class ColorRampStopData
{
    [YamlMember(Alias = "position")]
    public float Position { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    /// <summary>Interpolation towards the next stop.</summary>
    [YamlMember(Alias = "interp")]
    [SchemaEnum("linear", "smoothstep", "constant", "ease")]
    public string? Interp { get; set; }
}

/// <summary>
/// One altitude/slope band of a heightfield: altitude normalised to [0, 1]
/// above <c>sea_level</c>, slope in degrees off the horizontal plane.
/// </summary>
public class StratumData
{
    [YamlMember(Alias = "min_altitude")] public float MinAltitude { get; set; }
    [YamlMember(Alias = "max_altitude")] public float MaxAltitude { get; set; } = 1f;
    [YamlMember(Alias = "min_slope")] public float MinSlope { get; set; }
    [YamlMember(Alias = "max_slope")] public float MaxSlope { get; set; } = 90f;
    [YamlMember(Alias = "blend_width")] public float BlendWidth { get; set; }
    [YamlMember(Alias = "material")] public string? Material { get; set; }
}

/// <summary>
/// An entity: a primitive, a model file, a CSG, a group or an instance of a
/// template. Every entity takes the same transform (<c>scale</c>, then
/// <c>rotate</c> in <c>rotate_order</c>, then <c>translate</c>; or a
/// <c>matrix</c>) and motion keys.
/// </summary>
[SchemaTypes("sphere", "box", "triangle", "quad", "cylinder", "cone", "torus", "capsule", "disk", "annulus",
             "infinite_plane", "lathe", "extrusion", "mesh", "heightfield", "csg", "group", "instance")]
public class EntityData
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>Seed of the randomised procedural textures (default from the entity's position in the file).</summary>
    [YamlMember(Alias = "seed")]
    public int? Seed { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "material")]
    public string? Material { get; set; }

    /// <summary>Id of the medium inside the entity (from <c>mediums:</c>).</summary>
    [YamlMember(Alias = "interior_medium")]
    public string? InteriorMedium { get; set; }

    /// <summary>Id of the medium outside, when the loader cannot infer it.</summary>
    [YamlMember(Alias = "exterior_medium")]
    public string? ExteriorMedium { get; set; }

    [YamlMember(Alias = "visibility")]
    public VisibilityData? Visibility { get; set; }

    // ── Primitives ───────────────────────────────────────────────────────────

    [YamlMember(Alias = "center")]
    [OnlyFor("sphere", "cylinder", "cone", "torus", "capsule", "disk", "annulus", "lathe", "extrusion")]
    public List<float>? Center { get; set; }

    [YamlMember(Alias = "radius")]
    [OnlyFor("sphere", "cylinder", "cone", "capsule", "disk", "annulus")]
    public float Radius { get; set; } = 1f;

    [YamlMember(Alias = "v0")] [OnlyFor("triangle")] public List<float>? V0 { get; set; }
    [YamlMember(Alias = "v1")] [OnlyFor("triangle")] public List<float>? V1 { get; set; }
    [YamlMember(Alias = "v2")] [OnlyFor("triangle")] public List<float>? V2 { get; set; }
    /// <summary>Vertex normals: all three make the triangle smooth.</summary>
    [YamlMember(Alias = "n0")] [OnlyFor("triangle")] public List<float>? N0 { get; set; }
    [YamlMember(Alias = "n1")] [OnlyFor("triangle")] public List<float>? N1 { get; set; }
    [YamlMember(Alias = "n2")] [OnlyFor("triangle")] public List<float>? N2 { get; set; }
    /// <summary>Vertex UVs (with the normals).</summary>
    [YamlMember(Alias = "uv0")] [OnlyFor("triangle")] public List<float>? UV0 { get; set; }
    [YamlMember(Alias = "uv1")] [OnlyFor("triangle")] public List<float>? UV1 { get; set; }
    [YamlMember(Alias = "uv2")] [OnlyFor("triangle")] public List<float>? UV2 { get; set; }

    /// <summary>Quad: corner <c>q</c> and edges <c>u</c>, <c>v</c>.</summary>
    [YamlMember(Alias = "q")] [OnlyFor("quad")] public List<float>? Q { get; set; }
    [YamlMember(Alias = "u")] [OnlyFor("quad")] public List<float>? U { get; set; }
    [YamlMember(Alias = "v")] [OnlyFor("quad")] public List<float>? V { get; set; }

    [YamlMember(Alias = "height")]
    [OnlyFor("cylinder", "cone", "capsule", "extrusion")]
    public float Height { get; set; } = 1f;

    [YamlMember(Alias = "top_radius")]
    [OnlyFor("cone")]
    public float TopRadius { get; set; }

    [YamlMember(Alias = "major_radius")]
    [OnlyFor("torus")]
    public float MajorRadius { get; set; } = 1f;

    [YamlMember(Alias = "minor_radius")]
    [OnlyFor("torus")]
    public float MinorRadius { get; set; } = 0.25f;

    [YamlMember(Alias = "inner_radius")]
    [OnlyFor("annulus")]
    public float InnerRadius { get; set; }

    [YamlMember(Alias = "normal")]
    [OnlyFor("disk", "annulus", "infinite_plane")]
    public List<float>? Normal { get; set; }

    [YamlMember(Alias = "point")]
    [OnlyFor("infinite_plane")]
    public List<float>? Point { get; set; }

    // ── mesh ─────────────────────────────────────────────────────────────────

    /// <summary>Model file (<c>.obj</c>, <c>.gltf</c>, <c>.glb</c>, <c>.ply</c>), relative to the scene.</summary>
    [YamlMember(Alias = "path")]
    [OnlyFor("mesh")]
    public string? Path { get; set; }

    /// <summary>Scene material ids for the file's own materials, by name (when <c>material</c> is unset).</summary>
    [YamlMember(Alias = "material_overrides")]
    [OnlyFor("mesh")]
    public Dictionary<string, string>? MaterialOverrides { get; set; }

    /// <summary><c>none</c> (default), <c>loop</c>, <c>catmull_clark</c>, <c>auto</c>.</summary>
    [YamlMember(Alias = "subdivision_scheme")]
    [OnlyFor("mesh")]
    [SchemaEnum("none", "loop", "catmull_clark", "auto")]
    public string? SubdivisionScheme { get; set; }

    [YamlMember(Alias = "subdivision_iterations")] [OnlyFor("mesh")] public int SubdivisionIterations { get; set; }
    /// <summary>Adaptive target: subdivide until the longest projected edge is below this many pixels.</summary>
    [YamlMember(Alias = "subdivision_pixel_error")] [OnlyFor("mesh")] public float SubdivisionPixelError { get; set; }
    [YamlMember(Alias = "subdivision_max_iterations")] [OnlyFor("mesh")] public int SubdivisionMaxIterations { get; set; } = 6;

    [YamlMember(Alias = "subdivision_boundary")]
    [OnlyFor("mesh")]
    [SchemaEnum("edge_and_corner", "edge_only")]
    public string? SubdivisionBoundary { get; set; }

    [YamlMember(Alias = "subdivision_crease_angle")] [OnlyFor("mesh")] public float SubdivisionCreaseAngle { get; set; }
    [YamlMember(Alias = "subdivision_crease_sharpness")] [OnlyFor("mesh")] public float? SubdivisionCreaseSharpness { get; set; }

    /// <summary>False: this entity skips its material's displacement.</summary>
    [YamlMember(Alias = "displacement_enabled")]
    [OnlyFor("mesh")]
    public bool DisplacementEnabled { get; set; } = true;

    // ── heightfield ──────────────────────────────────────────────────────────

    /// <summary><c>[x_min, z_min, x_max, z_max]</c>.</summary>
    [YamlMember(Alias = "bounds")] [OnlyFor("heightfield")] public List<float>? Bounds { get; set; }
    [YamlMember(Alias = "height_scale")] [OnlyFor("heightfield")] public float HeightScale { get; set; } = 1f;
    /// <summary>Procedural height (its luminance in [0, 1]).</summary>
    [YamlMember(Alias = "height_texture")] [OnlyFor("heightfield")] public TextureData? HeightTexture { get; set; }
    /// <summary>Baked height map (16-bit PNG preferred), relative to the scene.</summary>
    [YamlMember(Alias = "heightmap_path")] [OnlyFor("heightfield")] public string? HeightmapPath { get; set; }
    [YamlMember(Alias = "resolution")] [OnlyFor("heightfield")] public int Resolution { get; set; } = 512;
    [YamlMember(Alias = "sea_level")] [OnlyFor("heightfield")] public float? SeaLevel { get; set; }
    [YamlMember(Alias = "sea_material")] [OnlyFor("heightfield")] public string? SeaMaterial { get; set; }
    [YamlMember(Alias = "strata")] [OnlyFor("heightfield")] public List<StratumData>? Strata { get; set; }

    // ── csg, group, instance ─────────────────────────────────────────────────

    [YamlMember(Alias = "operation")]
    [OnlyFor("csg")]
    [SchemaEnum("union", "intersection", "subtraction")]
    public string? Operation { get; set; }

    [YamlMember(Alias = "left")] [OnlyFor("csg")] public EntityData? Left { get; set; }
    [YamlMember(Alias = "right")] [OnlyFor("csg")] public EntityData? Right { get; set; }

    [YamlMember(Alias = "children")]
    [OnlyFor("group")]
    public List<EntityData>? Children { get; set; }

    [YamlMember(Alias = "template")]
    [OnlyFor("instance")]
    public string? Template { get; set; }

    // ── lathe, extrusion ─────────────────────────────────────────────────────

    /// <summary>lathe: <c>[r, y]</c> points; extrusion: closed <c>[x, z]</c> polygon.</summary>
    [YamlMember(Alias = "profile")] [OnlyFor("lathe", "extrusion")] public List<List<float>>? Profile { get; set; }

    [YamlMember(Alias = "profile_type")]
    [OnlyFor("lathe", "extrusion")]
    [SchemaEnum("linear", "catmull_rom", "bezier")]
    public string? ProfileType { get; set; }

    [YamlMember(Alias = "profile_bezier_controls")] [OnlyFor("lathe", "extrusion")] public List<List<float>>? ProfileBezierControls { get; set; }

    [YamlMember(Alias = "caps")]
    [OnlyFor("extrusion")]
    [SchemaEnum("both", "start", "end", "none")]
    public string? Caps { get; set; }

    [YamlMember(Alias = "twist")] [OnlyFor("extrusion")] public float Twist { get; set; }
    [YamlMember(Alias = "taper")] [OnlyFor("extrusion")] public float Taper { get; set; } = 1f;
    [YamlMember(Alias = "curve_samples")] [OnlyFor("extrusion")] public int CurveSamples { get; set; } = 16;
    [YamlMember(Alias = "crease_angle")] [OnlyFor("extrusion")] public float CreaseAngle { get; set; }

    // ── Transform ────────────────────────────────────────────────────────────

    [YamlMember(Alias = "translate")]
    public List<float>? Translate { get; set; }

    /// <summary>Euler degrees, applied in <c>rotate_order</c> (default xyz: X first).</summary>
    [YamlMember(Alias = "rotate")]
    public List<float>? Rotate { get; set; }

    [YamlMember(Alias = "rotate_order")]
    [SchemaEnum("xyz", "xzy", "yxz", "yzx", "zxy", "zyx")]
    public string? RotateOrder { get; set; }

    /// <summary>A number or <c>[sx, sy, sz]</c>.</summary>
    [YamlMember(Alias = "scale")]
    public object? Scale { get; set; }

    /// <summary>A 4×4 matrix, 16 numbers row by row acting on column vectors (translation in the last column); instead of translate / rotate / scale.</summary>
    [YamlMember(Alias = "matrix")]
    public List<float>? Matrix { get; set; }

    /// <summary>Motion keyframes; the base transform is the key at time 0.</summary>
    [YamlMember(Alias = "motion")]
    public List<MotionKeyData>? Motion { get; set; }
}

public class MotionKeyData
{
    [YamlMember(Alias = "time")] public float Time { get; set; }
    [YamlMember(Alias = "translate")] public List<float>? Translate { get; set; }
    [YamlMember(Alias = "rotate")] public List<float>? Rotate { get; set; }
    [YamlMember(Alias = "scale")] public object? Scale { get; set; }
}

/// <summary>
/// A light. Its amount is the photometric quantity of the type, the colour a
/// tint of luminance 1: <c>intensity</c> in candela (point, spot),
/// <c>luminance</c> in nits (area, sphere), <c>illuminance</c> in lux
/// (directional), or <c>power</c> for the finite lights.
/// </summary>
[SchemaTypes("point", "spot", "directional", "area", "sphere", "portal")]
public class LightData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "position")]
    [OnlyFor("point", "spot", "sphere")]
    public List<float>? Position { get; set; }

    /// <summary>spot: the axis; directional: towards where the light travels; point: the axis of an IES profile.</summary>
    [YamlMember(Alias = "direction")]
    [OnlyFor("point", "spot", "directional")]
    public List<float>? Direction { get; set; }

    [YamlMember(Alias = "color")]
    [OnlyFor("point", "spot", "directional", "area", "sphere")]
    public List<float>? Color { get; set; }

    /// <summary>Colour temperature in kelvin (black body), multiplying the colour.</summary>
    [YamlMember(Alias = "temperature")]
    [OnlyFor("point", "spot", "directional", "area", "sphere")]
    public float? Temperature { get; set; }

    /// <summary>Luminous intensity, candela (default 1).</summary>
    [YamlMember(Alias = "intensity")]
    [OnlyFor("point", "spot")]
    public float? Intensity { get; set; }

    /// <summary>Luminance of the emitting surface, nits (default 1).</summary>
    [YamlMember(Alias = "luminance")]
    [OnlyFor("area", "sphere")]
    public float? Luminance { get; set; }

    /// <summary>Illuminance on a surface facing the light, lux (default 1).</summary>
    [YamlMember(Alias = "illuminance")]
    [OnlyFor("directional")]
    public float? Illuminance { get; set; }

    /// <summary>Emitted flux in <c>power_unit</c>, instead of the quantity above.</summary>
    [YamlMember(Alias = "power")]
    [OnlyFor("point", "spot", "area", "sphere")]
    public float? Power { get; set; }

    /// <summary><c>watt</c> (default, 683 lm/W) or <c>lumen</c>.</summary>
    [YamlMember(Alias = "power_unit")]
    [OnlyFor("point", "spot", "area", "sphere")]
    [SchemaEnum("watt", "lumen")]
    public string? PowerUnit { get; set; }

    /// <summary>Scales the light by 2^exposure.</summary>
    [YamlMember(Alias = "exposure")]
    [OnlyFor("point", "spot", "directional", "area", "sphere")]
    public float Exposure { get; set; }

    /// <summary>Half-angle where the spot starts to fall off, degrees (default 15).</summary>
    [YamlMember(Alias = "inner_angle")]
    [OnlyFor("spot")]
    public float InnerAngle { get; set; } = 15f;

    /// <summary>Half-angle of the spot's cone, degrees (default 30).</summary>
    [YamlMember(Alias = "outer_angle")]
    [OnlyFor("spot")]
    public float OuterAngle { get; set; } = 30f;

    /// <summary>IES (LM-63) photometric file, relative to the scene: the light emits its candela distribution.</summary>
    [YamlMember(Alias = "ies")]
    [OnlyFor("point", "spot")]
    public string? Ies { get; set; }

    /// <summary>Rectangle: a corner and the two edges <c>u</c>, <c>v</c> (it emits on the side of u × v).</summary>
    [YamlMember(Alias = "corner")] [OnlyFor("area", "portal")] public List<float>? Corner { get; set; }
    [YamlMember(Alias = "u")] [OnlyFor("area", "portal")] public List<float>? U { get; set; }
    [YamlMember(Alias = "v")] [OnlyFor("area", "portal")] public List<float>? V { get; set; }

    [YamlMember(Alias = "radius")]
    [OnlyFor("sphere")]
    public float Radius { get; set; } = 0.5f;

    /// <summary>Floor of the 1/d² term near the light (scene units), for lights in dense media.</summary>
    [YamlMember(Alias = "soft_radius")]
    [OnlyFor("point", "spot", "area")]
    public float SoftRadius { get; set; }

    /// <summary>Half-angle of the light's disc, degrees (0 = sharp shadows; the sun 0.265).</summary>
    [YamlMember(Alias = "angular_radius")]
    [OnlyFor("directional")]
    public float AngularRadius { get; set; }

    [YamlMember(Alias = "shadow_samples")]
    [OnlyFor("spot", "area", "sphere", "portal")]
    public int ShadowSamples { get; set; } = 4;

    /// <summary>Visibility of the light's surface (area, sphere).</summary>
    [YamlMember(Alias = "visibility")]
    [OnlyFor("area", "sphere")]
    public VisibilityData? Visibility { get; set; }
}

/// <summary>
/// A participating medium: <c>world.medium</c>, or an entry of
/// <c>mediums:</c> with an <c>id</c>. Coefficients are per scene unit.
/// </summary>
[SchemaTypes("homogeneous", "height_fog", "procedural", "grid", "atmosphere")]
public class MediumData
{
    [YamlMember(Alias = "id")]
    public string? Id { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>Absorption coefficient per channel (height fog: at <c>y0</c>; procedural, grid: at density 1).</summary>
    [YamlMember(Alias = "sigma_a")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid")]
    public List<float>? SigmaA { get; set; }

    [YamlMember(Alias = "sigma_s")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid")]
    public List<float>? SigmaS { get; set; }

    /// <summary>Phase function (default isotropic; atmosphere: of the dust, default hg 0.76).</summary>
    [YamlMember(Alias = "phase")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")]
    [SchemaEnum("isotropic", "hg", "rayleigh", "schlick", "double_hg")]
    public string? Phase { get; set; }

    /// <summary>Anisotropy of <c>hg</c> and <c>schlick</c>.</summary>
    [YamlMember(Alias = "g")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")]
    public float G { get; set; }

    [YamlMember(Alias = "g1")] [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")] public float G1 { get; set; } = 0.85f;
    [YamlMember(Alias = "g2")] [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")] public float G2 { get; set; } = -0.3f;
    /// <summary>Weight of the forward lobe of <c>double_hg</c>.</summary>
    [YamlMember(Alias = "w")] [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")] public float W { get; set; } = 0.5f;

    /// <summary>height_fog: altitude of the reference density; atmosphere: sea level.</summary>
    [YamlMember(Alias = "y0")] [OnlyFor("height_fog", "atmosphere")] public float Y0 { get; set; }
    /// <summary>Exponential scale height H: the density falls by e every H.</summary>
    [YamlMember(Alias = "scale_height")] [OnlyFor("height_fog")] public float ScaleHeight { get; set; } = 1f;

    [YamlMember(Alias = "frequency")] [OnlyFor("procedural")] public float Frequency { get; set; } = 1f;
    [YamlMember(Alias = "octaves")] [OnlyFor("procedural")] public int Octaves { get; set; } = 4;
    [YamlMember(Alias = "lacunarity")] [OnlyFor("procedural")] public float Lacunarity { get; set; } = 2f;
    [YamlMember(Alias = "gain")] [OnlyFor("procedural")] public float Gain { get; set; } = 0.5f;
    [YamlMember(Alias = "seed")] [OnlyFor("procedural")] public int Seed { get; set; }

    [YamlMember(Alias = "bounds_min")] [OnlyFor("grid")] public List<float>? BoundsMin { get; set; }
    [YamlMember(Alias = "bounds_max")] [OnlyFor("grid")] public List<float>? BoundsMax { get; set; }
    [YamlMember(Alias = "nx")] [OnlyFor("grid")] public int Nx { get; set; }
    [YamlMember(Alias = "ny")] [OnlyFor("grid")] public int Ny { get; set; }
    [YamlMember(Alias = "nz")] [OnlyFor("grid")] public int Nz { get; set; }
    /// <summary>Inline densities, nx·ny·nz values in z-major order (as a <c>.vol</c> file).</summary>
    [YamlMember(Alias = "data")] [OnlyFor("grid")] public List<float>? Data { get; set; }
    /// <summary>A <c>.vol</c> file, relative to the scene.</summary>
    [YamlMember(Alias = "file")] [OnlyFor("grid")] public string? File { get; set; }

    [YamlMember(Alias = "interpolation")]
    [OnlyFor("grid")]
    [SchemaEnum("trilinear", "tricubic")]
    public string? Interpolation { get; set; }

    /// <summary>Air (Rayleigh) density multiplier, 1 = the standard atmosphere.</summary>
    [YamlMember(Alias = "air_density")] [OnlyFor("atmosphere")] public float AirDensity { get; set; } = 1f;
    [YamlMember(Alias = "ozone_density")] [OnlyFor("atmosphere")] public float OzoneDensity { get; set; } = 1f;
    /// <summary>Mie dust density (0 pristine, 1 clean, above 1 polluted).</summary>
    [YamlMember(Alias = "dust_density")] [OnlyFor("atmosphere")] public float DustDensity { get; set; } = 1f;
}
