using YamlDotNet.Serialization;

namespace RayTracer.Scene;

/// <summary>
/// POCO classes matching the YAML scene schema.
/// </summary>
public class SceneData
{
    [YamlMember(Alias = "world")]
    public WorldData? World { get; set; }

    /// <summary>
    /// Single-camera shorthand (legacy / backward-compatible).
    /// If both <c>camera</c> and <c>cameras</c> are present, <c>cameras</c> takes precedence.
    /// </summary>
    [YamlMember(Alias = "camera")]
    public CameraData? Camera { get; set; }

    /// <summary>
    /// Named camera list. Use <c>--camera &lt;name|index&gt;</c> on the CLI to select
    /// which camera to render. When more than one camera is defined and no selection
    /// is made, the first camera in the list is used and a warning is printed.
    /// </summary>
    [YamlMember(Alias = "cameras")]
    public List<CameraData>? Cameras { get; set; }

    [YamlMember(Alias = "materials")]
    public List<MaterialData>? Materials { get; set; }

    [YamlMember(Alias = "entities")]
    public List<EntityData>? Entities { get; set; }

    [YamlMember(Alias = "lights")]
    public List<LightData>? Lights { get; set; }

    // ── Scene imports ───────────────────────────────────────────────────────

    /// <summary>
    /// List of external YAML files to import. Imported files can contribute
    /// materials, entities, lights, and templates to the current scene.
    ///
    /// Paths are resolved relative to the importing file's directory.
    /// Imports are processed before local definitions, so local definitions
    /// with the same ID/name override imported ones.
    ///
    /// Nested imports are supported (an imported file can itself import
    /// other files). Circular import detection prevents infinite loops.
    /// </summary>
    [YamlMember(Alias = "imports")]
    public List<ImportData>? Imports { get; set; }

    // ── Templates ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reusable object templates (prototypes). Each template defines a group
    /// of children that can be instantiated multiple times via <c>type: "instance"</c>
    /// in the <c>entities:</c> section.
    ///
    /// Templates are NOT rendered directly — they serve as blueprints.
    /// Each instance creates its own copy of the geometry with independent
    /// material override, transform, and seed.
    ///
    /// Templates support transforms (scale/rotate/translate) as a "default pose"
    /// that composes with the instance's transform: child_local → template_transform → instance_transform.
    ///
    /// YAML example:
    /// <code>
    /// templates:
    ///   - name: "pedina"
    ///     material: "legno_noce"
    ///     children:
    ///       - type: "cylinder"
    ///         center: [0, 0, 0]
    ///         radius: 0.4
    ///         height: 0.15
    ///       - type: "sphere"
    ///         center: [0, 0.35, 0]
    ///         radius: 0.3
    ///
    /// entities:
    ///   - type: "instance"
    ///     template: "pedina"
    ///     translate: [0, 0, 0]
    ///   - type: "instance"
    ///     template: "pedina"
    ///     translate: [2, 0, 0]
    ///     material: "legno_acero"   # per-instance override
    /// </code>
    /// </summary>
    [YamlMember(Alias = "templates")]
    public List<EntityData>? Templates { get; set; }
}

/// <summary>
/// Describes an external YAML file to import into the current scene.
/// </summary>
public class ImportData
{
    /// <summary>
    /// File path to the YAML file to import.
    /// Resolved relative to the importing file's directory.
    /// </summary>
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }
}

public class WorldData
{
    [YamlMember(Alias = "ambient_light")]
    public List<float>? AmbientLight { get; set; }

    [YamlMember(Alias = "background")]
    public List<float>? Background { get; set; }

    [YamlMember(Alias = "ground")]
    public GroundData? Ground { get; set; }

    [YamlMember(Alias = "sky")]
    public SkyData? Sky { get; set; }

    [YamlMember(Alias = "medium")]
    public MediumData? Medium { get; set; }
}

public class GroundData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "material")]
    public string? Material { get; set; }

    [YamlMember(Alias = "y")]
    public float Y { get; set; } = 0f;
}

public class SkyData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;

    [YamlMember(Alias = "rotation")]
    public float Rotation { get; set; } = 0f;

    [YamlMember(Alias = "zenith_color")]
    public List<float>? ZenithColor { get; set; }

    [YamlMember(Alias = "horizon_color")]
    public List<float>? HorizonColor { get; set; }

    [YamlMember(Alias = "ground_color")]
    public List<float>? GroundColor { get; set; }

    [YamlMember(Alias = "sun")]
    public SunDiskData? Sun { get; set; }
}

public class SunDiskData
{
    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 10f;

    [YamlMember(Alias = "size")]
    public float Size { get; set; } = 3f;

    [YamlMember(Alias = "falloff")]
    public float Falloff { get; set; } = 32f;
}

public class CameraData
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "position")]
    public List<float>? Position { get; set; }

    [YamlMember(Alias = "look_at")]
    public List<float>? LookAt { get; set; }

    [YamlMember(Alias = "vup")]
    public List<float>? Vup { get; set; }

    [YamlMember(Alias = "fov")]
    public float Fov { get; set; } = 60f;

    [YamlMember(Alias = "aperture")]
    public float Aperture { get; set; } = 0f;

    [YamlMember(Alias = "focal_dist")]
    public float FocalDist { get; set; } = 1f;
}

public class MaterialData
{
    [YamlMember(Alias = "id")]
    public string? Id { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "fuzz")]
    public float Fuzz { get; set; } = 0f;

    [YamlMember(Alias = "refraction_index")]
    public float RefractionIndex { get; set; } = 1.5f;

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;

    [YamlMember(Alias = "texture")]
    public TextureData? Texture { get; set; }

    [YamlMember(Alias = "normal_map")]
    public NormalMapData? NormalMap { get; set; }

    // ── Disney BSDF parameters ──────────────────────────────────────────────
    [YamlMember(Alias = "metallic")]
    public float Metallic { get; set; } = 0f;

    [YamlMember(Alias = "roughness")]
    public float Roughness { get; set; } = 0.5f;

    [YamlMember(Alias = "subsurface")]
    public float Subsurface { get; set; } = 0f;

    [YamlMember(Alias = "specular")]
    public float Specular { get; set; } = 0.5f;

    [YamlMember(Alias = "specular_tint")]
    public float SpecularTint { get; set; } = 0f;

    [YamlMember(Alias = "sheen")]
    public float Sheen { get; set; } = 0f;

    [YamlMember(Alias = "sheen_tint")]
    public float SheenTint { get; set; } = 0.5f;

    [YamlMember(Alias = "clearcoat")]
    public float Clearcoat { get; set; } = 0f;

    [YamlMember(Alias = "clearcoat_gloss")]
    public float ClearcoatGloss { get; set; } = 1f;

    [YamlMember(Alias = "spec_trans")]
    public float SpecTrans { get; set; } = 0f;

    [YamlMember(Alias = "ior")]
    public float DisneyIor { get; set; } = 1.5f;

    // ── Mix Material parameters ─────────────────────────────────────────────

    [YamlMember(Alias = "material_a")]
    public string? MaterialA { get; set; }

    [YamlMember(Alias = "material_b")]
    public string? MaterialB { get; set; }

    [YamlMember(Alias = "blend")]
    public float Blend { get; set; } = 0.5f;

    [YamlMember(Alias = "mask")]
    public TextureData? Mask { get; set; }
}

public class NormalMapData
{
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "strength")]
    public float Strength { get; set; } = 1f;

    [YamlMember(Alias = "uv_scale")]
    public List<float>? UvScale { get; set; }

    [YamlMember(Alias = "flip_y")]
    public bool FlipY { get; set; } = false;
}

public class TextureData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "uv_scale")]
    public List<float>? UvScale { get; set; }

    [YamlMember(Alias = "colors")]
    public List<List<float>>? Colors { get; set; }

    [YamlMember(Alias = "scale")]
    public float Scale { get; set; } = 1f;

    [YamlMember(Alias = "noise_strength")]
    public float? NoiseStrength { get; set; }

    [YamlMember(Alias = "offset")]
    public List<float>? Offset { get; set; }

    [YamlMember(Alias = "rotation")]
    public List<float>? Rotation { get; set; }

    [YamlMember(Alias = "randomize_offset")]
    public bool RandomizeOffset { get; set; }

    [YamlMember(Alias = "randomize_rotation")]
    public bool RandomizeRotation { get; set; }
}

public class EntityData
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "seed")]
    public int? Seed { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "material")]
    public string? Material { get; set; }

    // Sphere & Cylinder
    [YamlMember(Alias = "center")]
    public List<float>? Center { get; set; }

    [YamlMember(Alias = "radius")]
    public float Radius { get; set; } = 1f;

    // Triangle
    [YamlMember(Alias = "v0")]
    public List<float>? V0 { get; set; }

    [YamlMember(Alias = "v1")]
    public List<float>? V1 { get; set; }

    [YamlMember(Alias = "v2")]
    public List<float>? V2 { get; set; }

    // SmoothTriangle — per-vertex normals
    [YamlMember(Alias = "n0")]
    public List<float>? N0 { get; set; }

    [YamlMember(Alias = "n1")]
    public List<float>? N1 { get; set; }

    [YamlMember(Alias = "n2")]
    public List<float>? N2 { get; set; }

    // SmoothTriangle — per-vertex texture coordinates
    [YamlMember(Alias = "uv0")]
    public List<float>? UV0 { get; set; }

    [YamlMember(Alias = "uv1")]
    public List<float>? UV1 { get; set; }

    [YamlMember(Alias = "uv2")]
    public List<float>? UV2 { get; set; }

    // Quad
    [YamlMember(Alias = "q")]
    public List<float>? Q { get; set; }

    [YamlMember(Alias = "u")]
    public List<float>? U { get; set; }

    [YamlMember(Alias = "v")]
    public List<float>? V { get; set; }

    // Cylinder
    [YamlMember(Alias = "height")]
    public float Height { get; set; } = 1f;

    // Cone (truncated cone / frustum)
    [YamlMember(Alias = "top_radius")]
    public float TopRadius { get; set; } = 0f;

    // Torus
    [YamlMember(Alias = "major_radius")]
    public float MajorRadius { get; set; } = 1f;

    [YamlMember(Alias = "minor_radius")]
    public float MinorRadius { get; set; } = 0.25f;

    // Annulus (ring disk)
    [YamlMember(Alias = "inner_radius")]
    public float InnerRadius { get; set; } = 0f;

    // Mesh (OBJ file)
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    // Plane
    [YamlMember(Alias = "normal")]
    public List<float>? Normal { get; set; }

    [YamlMember(Alias = "point")]
    public List<float>? Point { get; set; }

    // CSG (Constructive Solid Geometry)
    [YamlMember(Alias = "operation")]
    public string? Operation { get; set; }

    [YamlMember(Alias = "left")]
    public EntityData? Left { get; set; }

    [YamlMember(Alias = "right")]
    public EntityData? Right { get; set; }

    // ── Group (Scene Graph) ─────────────────────────────────────────────────

    /// <summary>
    /// Child entities for group nodes (type: "group") and template definitions.
    /// Each child can be any entity type, including other groups (arbitrary nesting).
    /// </summary>
    [YamlMember(Alias = "children")]
    public List<EntityData>? Children { get; set; }

    // ── Instance (Template reference) ───────────────────────────────────────

    /// <summary>
    /// Name of the template to instantiate. Only used when <c>type: "instance"</c>.
    /// The template must be defined in the <c>templates:</c> section (or imported).
    /// The instance inherits the template's children and default transform;
    /// its own transform composes on top: child_local → template_transform → instance_transform.
    /// Material can be overridden per-instance via the <c>material</c> field.
    /// </summary>
    [YamlMember(Alias = "template")]
    public string? Template { get; set; }

    // Transformations
    [YamlMember(Alias = "translate")]
    public List<float>? Translate { get; set; }

    [YamlMember(Alias = "rotate")]
    public List<float>? Rotate { get; set; }

    [YamlMember(Alias = "scale")]
    public object? Scale { get; set; }
}

public class LightData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "position")]
    public List<float>? Position { get; set; }

    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;

    [YamlMember(Alias = "inner_angle")]
    public float InnerAngle { get; set; } = 15f;

    [YamlMember(Alias = "outer_angle")]
    public float OuterAngle { get; set; } = 30f;

    [YamlMember(Alias = "corner")]
    public List<float>? Corner { get; set; }

    [YamlMember(Alias = "u")]
    public List<float>? U { get; set; }

    [YamlMember(Alias = "v")]
    public List<float>? V { get; set; }

    [YamlMember(Alias = "shadow_samples")]
    public int ShadowSamples { get; set; } = 16;

    [YamlMember(Alias = "radius")]
    public float Radius { get; set; } = 0.5f;
}

/// <summary>
/// Participating medium definition. Lives under <c>world.medium</c>.
/// When absent, the renderer runs in surface-only mode and produces
/// bit-identical output to pre-volumetric builds.
///
/// Supported <c>type</c> values:
///   - <c>homogeneous</c>: constant σ_a, σ_s everywhere (uses sigma_a, sigma_s)
///   - <c>height_fog</c>: exponential altitude falloff (sigma_a/sigma_s as density at <c>y0</c>; <c>scale_height</c>)
///   - <c>procedural</c>: Perlin-fBm noise density (frequency, octaves, lacunarity, gain, seed)
///   - <c>grid</c>: 3D voxel grid (bounds_min/max, nx/ny/nz, and either <c>data</c> inline or <c>file</c>)
///
/// Supported <c>phase</c> values:
///   - <c>isotropic</c> (default)
///   - <c>hg</c> / <c>henyey_greenstein</c>: needs <c>g</c>
///   - <c>rayleigh</c>: no parameters
///   - <c>schlick</c>: needs <c>g</c> (mapped to Schlick's k)
///   - <c>double_hg</c>: needs <c>g1</c>, <c>g2</c>, <c>w</c>
/// </summary>
public class MediumData
{
    /// <summary>"homogeneous", "height_fog", "procedural", "grid".</summary>
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>Absorption coefficient σ_a per RGB channel (units: 1/world-unit).</summary>
    [YamlMember(Alias = "sigma_a")]
    public List<float>? SigmaA { get; set; }

    /// <summary>Scattering coefficient σ_s per RGB channel (units: 1/world-unit).</summary>
    [YamlMember(Alias = "sigma_s")]
    public List<float>? SigmaS { get; set; }

    /// <summary>Phase function identifier. Default: "isotropic".</summary>
    [YamlMember(Alias = "phase")]
    public string? Phase { get; set; }

    /// <summary>HG / Schlick anisotropy parameter g ∈ (-1, 1). Ignored for isotropic and rayleigh.</summary>
    [YamlMember(Alias = "g")]
    public float G { get; set; } = 0f;

    // ── double_hg phase ──────────────────────────────────────────────────
    /// <summary>Double-HG forward lobe anisotropy.</summary>
    [YamlMember(Alias = "g1")]
    public float G1 { get; set; } = 0.85f;

    /// <summary>Double-HG backward lobe anisotropy.</summary>
    [YamlMember(Alias = "g2")]
    public float G2 { get; set; } = -0.3f;

    /// <summary>Double-HG weight of the forward lobe (0..1).</summary>
    [YamlMember(Alias = "w")]
    public float W { get; set; } = 0.5f;

    // ── height_fog ────────────────────────────────────────────────────────
    /// <summary>Reference altitude for <c>height_fog</c>: σ = sigma_a/sigma_s at y = y0.</summary>
    [YamlMember(Alias = "y0")]
    public float Y0 { get; set; } = 0f;

    /// <summary>Exponential scale height H for <c>height_fog</c> (density halves every H · ln 2).</summary>
    [YamlMember(Alias = "scale_height")]
    public float ScaleHeight { get; set; } = 1f;

    // ── procedural ────────────────────────────────────────────────────────
    /// <summary>Spatial frequency multiplier of the noise for <c>procedural</c>.</summary>
    [YamlMember(Alias = "frequency")]
    public float Frequency { get; set; } = 1f;

    /// <summary>Number of fBm octaves for <c>procedural</c> (1..8).</summary>
    [YamlMember(Alias = "octaves")]
    public int Octaves { get; set; } = 4;

    /// <summary>Frequency multiplier between octaves for <c>procedural</c>.</summary>
    [YamlMember(Alias = "lacunarity")]
    public float Lacunarity { get; set; } = 2f;

    /// <summary>Amplitude multiplier between octaves for <c>procedural</c>.</summary>
    [YamlMember(Alias = "gain")]
    public float Gain { get; set; } = 0.5f;

    /// <summary>Noise seed for <c>procedural</c>. Same seed → identical volume.</summary>
    [YamlMember(Alias = "seed")]
    public int Seed { get; set; } = 0;

    // ── grid ──────────────────────────────────────────────────────────────
    /// <summary>World-space AABB min corner for <c>grid</c>.</summary>
    [YamlMember(Alias = "bounds_min")]
    public List<float>? BoundsMin { get; set; }

    /// <summary>World-space AABB max corner for <c>grid</c>.</summary>
    [YamlMember(Alias = "bounds_max")]
    public List<float>? BoundsMax { get; set; }

    /// <summary>Grid resolution along X for <c>grid</c>.</summary>
    [YamlMember(Alias = "nx")]
    public int Nx { get; set; } = 0;

    /// <summary>Grid resolution along Y for <c>grid</c>.</summary>
    [YamlMember(Alias = "ny")]
    public int Ny { get; set; } = 0;

    /// <summary>Grid resolution along Z for <c>grid</c>.</summary>
    [YamlMember(Alias = "nz")]
    public int Nz { get; set; } = 0;

    /// <summary>Inline flat density array (length nx*ny*nz, z-major) for small <c>grid</c> volumes.</summary>
    [YamlMember(Alias = "data")]
    public List<float>? Data { get; set; }

    /// <summary>Path to a <c>.vol</c> binary file (relative to the scene YAML) for larger <c>grid</c> volumes.</summary>
    [YamlMember(Alias = "file")]
    public string? File { get; set; }

    /// <summary>
    /// Reconstruction filter used when sampling between voxels in a <c>grid</c>
    /// medium. Accepts <c>trilinear</c> (default, C⁰, 8 taps) or <c>tricubic</c>
    /// (C¹ Catmull-Rom, 64 taps — smoother at low resolutions, ~8× cost).
    /// </summary>
    [YamlMember(Alias = "interpolation")]
    public string Interpolation { get; set; } = "trilinear";
}
