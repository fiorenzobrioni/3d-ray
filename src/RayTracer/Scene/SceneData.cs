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
    [YamlMember(Alias = "ground")]
    public GroundData? Ground { get; set; }

    [YamlMember(Alias = "sky")]
    public SkyData? Sky { get; set; }

    [YamlMember(Alias = "medium")]
    public MediumData? Medium { get; set; }
}

/// <summary>
/// Schema for the <c>world.ground</c> block — a richer shorthand than the legacy
/// "infinite plane at Y" model, with parity to Arnold's <c>floor</c> patterns,
/// Cycles' shadow-catcher floor and Mitsuba's <c>shape</c>-as-environment-base.
/// All fields are optional; sensible defaults reproduce the legacy behaviour.
///
/// <para>Dispatch by <see cref="Type"/>:
/// <c>infinite_plane</c> / <c>plane</c> (default), <c>quad</c>, <c>disk</c>,
/// <c>heightfield</c> (alias <c>terrain</c>). Geometry parameters that do not
/// apply to the selected type are ignored with an informational warning.</para>
/// </summary>
public class GroundData
{
    /// <summary>
    /// Ground geometry kind. Accepted values: <c>infinite_plane</c> / <c>plane</c>
    /// (default — infinite Y-up floor), <c>quad</c> (finite rectangular patch
    /// centred at <see cref="Point"/>), <c>disk</c> (finite circular patch),
    /// <c>heightfield</c> / <c>terrain</c> (full terrain primitive). Unknown
    /// values fall back to <c>infinite_plane</c> with a warning.
    /// </summary>
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>Material ID from the <c>materials:</c> list. Mutually exclusive with the inline shorthand (<see cref="Color"/>/<see cref="Roughness"/>/<see cref="Metallic"/>). When both are present <c>material</c> wins.</summary>
    [YamlMember(Alias = "material")]
    public string? Material { get; set; }

    // ── Position / orientation (universal) ────────────────────────────────────

    /// <summary>Shorthand for <c>point: [0, y, 0]</c>. Kept for backward compatibility with the legacy schema.</summary>
    [YamlMember(Alias = "y")]
    public float Y { get; set; } = 0f;

    /// <summary>Anchor point. For <c>infinite_plane</c>/<c>plane</c> this is a point lying on the plane (defaults to <c>[0, y, 0]</c>). For <c>quad</c>/<c>disk</c> this is the surface centre.</summary>
    [YamlMember(Alias = "point")]
    public List<float>? Point { get; set; }

    /// <summary>Surface normal. Defaults to <c>[0, 1, 0]</c> (Y-up). Lets the ground slope or tilt — useful for stylised setups and ramp geometry.</summary>
    [YamlMember(Alias = "normal")]
    public List<float>? Normal { get; set; }

    /// <summary>UV-frame orientation. Composes after <see cref="Normal"/>; affects how textured materials wrap the ground. Mirrors <c>sky.orientation</c>.</summary>
    [YamlMember(Alias = "orientation")]
    public OrientationData? Orientation { get; set; }

    // ── Finite-type geometry ──────────────────────────────────────────────────

    /// <summary>Half-extent (XZ) for <c>quad</c>, or radius for <c>disk</c>. Default 50 world units (100×100 m floor). Ignored by <c>infinite_plane</c>.</summary>
    [YamlMember(Alias = "size")]
    public float Size { get; set; } = 50f;

    // ── Heightfield-type geometry (mirrors EntityData heightfield fields) ────

    /// <summary><c>[xMin, zMin, xMax, zMax]</c> in world units. Required when <see cref="Type"/> is <c>heightfield</c>.</summary>
    [YamlMember(Alias = "bounds")]
    public List<float>? Bounds { get; set; }

    [YamlMember(Alias = "height_scale")]
    public float HeightScale { get; set; } = 1f;

    [YamlMember(Alias = "heightmap_path")]
    public string? HeightmapPath { get; set; }

    [YamlMember(Alias = "height_texture")]
    public TextureData? HeightTexture { get; set; }

    /// <summary>Procedural heightmap resolution when <see cref="HeightTexture"/> is used. Default 512.</summary>
    [YamlMember(Alias = "resolution")]
    public int Resolution { get; set; } = 0;

    [YamlMember(Alias = "sea_level")]
    public float? SeaLevel { get; set; }

    [YamlMember(Alias = "sea_material")]
    public string? SeaMaterial { get; set; }

    [YamlMember(Alias = "strata")]
    public List<StratumData>? Strata { get; set; }

    // ── Inline material shorthand (anonymous material) ────────────────────────

    /// <summary>Base colour. When set without <see cref="Material"/>, the loader builds an anonymous Disney BSDF with the supplied colour + <see cref="Roughness"/> + <see cref="Metallic"/>.</summary>
    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "roughness")]
    public float? Roughness { get; set; }

    [YamlMember(Alias = "metallic")]
    public float? Metallic { get; set; }

    // ── UV transform (applied on top of the primitive's native UVs) ──────────

    /// <summary>Per-axis tile factor — e.g. <c>[10, 10]</c> repeats a 1×1 texture 10 times across X and Z.</summary>
    [YamlMember(Alias = "uv_scale")]
    public List<float>? UvScale { get; set; }

    /// <summary>Per-axis UV offset applied after scale.</summary>
    [YamlMember(Alias = "uv_offset")]
    public List<float>? UvOffset { get; set; }

    /// <summary>UV-plane rotation in degrees, applied after offset around <c>(0.5, 0.5)</c>.</summary>
    [YamlMember(Alias = "uv_rotation")]
    public float UvRotation { get; set; } = 0f;

    // ── Visibility flags (parity with sky / Arnold / Cycles) ─────────────────

    /// <summary>Per-ray-category visibility flags. <c>null</c> = all categories visible (legacy default).</summary>
    [YamlMember(Alias = "visibility")]
    public GroundVisibilityData? Visibility { get; set; }
}

/// <summary>
/// Per-ray-category visibility flags for the ground surface, mirroring
/// <see cref="SkyVisibilityData"/> and Arnold's <c>polymesh.visibility.*</c> /
/// Cycles' "Ray Visibility" toggles. A <c>false</c> flag makes the ground
/// invisible to rays of that category — the ray continues past the surface as
/// if it weren't there.
/// </summary>
public class GroundVisibilityData
{
    [YamlMember(Alias = "camera")]
    public bool Camera { get; set; } = true;

    [YamlMember(Alias = "diffuse")]
    public bool Diffuse { get; set; } = true;

    [YamlMember(Alias = "glossy")]
    public bool Glossy { get; set; } = true;

    [YamlMember(Alias = "transmission")]
    public bool Transmission { get; set; } = true;

    [YamlMember(Alias = "shadow")]
    public bool Shadow { get; set; } = true;
}

public class SkyData
{
    /// <summary>
    /// Sky model: <c>flat</c> (uniform colour), <c>gradient</c> (zenith/horizon/ground
    /// vertical lerp + optional analytical sun), <c>hdri</c> (equirect IBL), or
    /// <c>preetham</c> / <c>hosek_wilkie</c> (analytical physical sky). The
    /// <c>hosek_wilkie</c> alias currently routes to the Preetham implementation
    /// — see <c>docs/technical/sky-environment.md</c>.
    /// </summary>
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;

    /// <summary>Legacy Y-axis rotation in degrees; folded into <see cref="Orientation"/> when both are absent/present.</summary>
    [YamlMember(Alias = "rotation")]
    public float Rotation { get; set; } = 0f;

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "zenith_color")]
    public List<float>? ZenithColor { get; set; }

    [YamlMember(Alias = "horizon_color")]
    public List<float>? HorizonColor { get; set; }

    [YamlMember(Alias = "ground_color")]
    public List<float>? GroundColor { get; set; }

    [YamlMember(Alias = "sun")]
    public SunDiskData? Sun { get; set; }

    // ── Pro features (added with the sky/environment overhaul) ───────────────

    /// <summary>Atmospheric turbidity for <c>preetham</c> / <c>hosek_wilkie</c>. 1 = pristine, 3 ≈ clear, 5 = haze, 10 = smog. Default 3.</summary>
    [YamlMember(Alias = "turbidity")]
    public float Turbidity { get; set; } = 3f;

    /// <summary>RGB ground albedo for the physical sky's ground-bounce term. Default 0.3.</summary>
    [YamlMember(Alias = "ground_albedo")]
    public List<float>? GroundAlbedo { get; set; }

    /// <summary>Per-ray-category visibility flags. Optional; all true by default.</summary>
    [YamlMember(Alias = "visibility")]
    public SkyVisibilityData? Visibility { get; set; }

    /// <summary>Optional separate background plate shown to camera rays.</summary>
    [YamlMember(Alias = "background")]
    public SkyData? Background { get; set; }

    /// <summary>Sky-space orientation. Euler XYZ in degrees, or quaternion XYZW.</summary>
    [YamlMember(Alias = "orientation")]
    public OrientationData? Orientation { get; set; }
}

public class SunDiskData
{
    /// <summary>Direction TOWARDS the sun in world space. Note: this is the new convention (the legacy code internally inverted it).</summary>
    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 10f;

    /// <summary>Total angular diameter of the disc in degrees. Default 3°. Real Sun ≈ 0.53° (diameter).</summary>
    [YamlMember(Alias = "size")]
    public float Size { get; set; } = 3f;

    /// <summary>Half-angle in degrees. When set, overrides <see cref="Size"/>. Real Sun ≈ 0.265°.</summary>
    [YamlMember(Alias = "angular_radius")]
    public float AngularRadius { get; set; } = 0f;

    [YamlMember(Alias = "falloff")]
    public float Falloff { get; set; } = 32f;

    /// <summary>Apply Hestroffer V-band limb darkening to the disc.</summary>
    [YamlMember(Alias = "limb_darkening")]
    public bool LimbDarkening { get; set; } = true;

    /// <summary>When <c>type: hdri</c>, attempt automatic sun extraction from the HDRI peak.</summary>
    [YamlMember(Alias = "extract_from_hdri")]
    public bool ExtractFromHdri { get; set; } = false;

    /// <summary>Luminance threshold factor for sun extraction (multiple of HDRI mean). Default 50.</summary>
    [YamlMember(Alias = "extract_threshold")]
    public float ExtractThreshold { get; set; } = 50f;

    /// <summary>Number of stratified shadow samples for the paired PhysicalSun. Default 4.</summary>
    [YamlMember(Alias = "shadow_samples")]
    public int ShadowSamples { get; set; } = 4;

    /// <summary>When false, the sun disc is invisible to primary camera rays (still illuminates the scene).</summary>
    [YamlMember(Alias = "visible_to_camera")]
    public bool VisibleToCamera { get; set; } = true;
}

public class SkyVisibilityData
{
    [YamlMember(Alias = "camera")]
    public bool Camera { get; set; } = true;

    [YamlMember(Alias = "diffuse")]
    public bool Diffuse { get; set; } = true;

    [YamlMember(Alias = "glossy")]
    public bool Glossy { get; set; } = true;

    [YamlMember(Alias = "transmission")]
    public bool Transmission { get; set; } = true;

    [YamlMember(Alias = "shadow")]
    public bool Shadow { get; set; } = true;
}

public class OrientationData
{
    /// <summary>Euler XYZ angles in degrees, applied in that order (intrinsic).</summary>
    [YamlMember(Alias = "euler")]
    public List<float>? Euler { get; set; }

    /// <summary>Quaternion XYZW. Mutually exclusive with <see cref="Euler"/>; if both given, quaternion wins.</summary>
    [YamlMember(Alias = "quaternion")]
    public List<float>? Quaternion { get; set; }
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

    /// <summary>
    /// World-space focal point: alternative to <see cref="FocalDist"/>. When
    /// set, the loader computes the focus distance as the projection of
    /// <c>focal_pos − position</c> onto the optical axis
    /// <c>normalize(look_at − position)</c> — the standard "Focus Object/
    /// Point" workflow of Arnold, Cycles and RenderMan: the focus plane is
    /// perpendicular to the view direction passing through this point, so
    /// the value is a projection, not the Euclidean distance. When both
    /// <c>focal_pos</c> and <c>focal_dist</c> are specified, <c>focal_pos</c>
    /// wins and an info message is logged for transparency.
    /// </summary>
    [YamlMember(Alias = "focal_pos")]
    public List<float>? FocalPos { get; set; }
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

    [YamlMember(Alias = "bump_map")]
    public BumpMapData? BumpMap { get; set; }

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

    // Estevez-Kulla 2017 "Charlie" sheen NDF roughness in (0, 1]. Lower
    // values give thin grazing-angle velvet; higher values broaden the lobe
    // toward dust/cloth. Defaults to 0.3, matching the Imageworks reference
    // and Arnold's standard_surface sheen_roughness default.
    [YamlMember(Alias = "sheen_roughness")]
    public float SheenRoughness { get; set; } = 0.3f;

    [YamlMember(Alias = "clearcoat")]
    public float Clearcoat { get; set; } = 0f;

    [YamlMember(Alias = "clearcoat_gloss")]
    public float ClearcoatGloss { get; set; } = 1f;

    [YamlMember(Alias = "spec_trans")]
    public float SpecTrans { get; set; } = 0f;

    [YamlMember(Alias = "ior")]
    public float DisneyIor { get; set; } = 1.5f;

    [YamlMember(Alias = "anisotropic")]
    public float Anisotropic { get; set; } = 0f;

    // Rotation of the anisotropic highlight around the surface normal, expressed
    // as a fraction of a full turn in [0, 1). Values outside the range are
    // wrapped at sample time.
    [YamlMember(Alias = "anisotropic_rotation")]
    public float AnisotropicRotation { get; set; } = 0f;

    // ── Volumetric interior (Beer-Lambert through glass) ────────────────────
    // transmission_color is the colour light takes on after travelling
    // transmission_depth units through the material. Internally converted to
    // a per-channel absorption σ_a = -ln(color) / depth and applied as
    // exp(-σ_a · t) to the ray segment inside the glass by the renderer's
    // interior-medium stack. transmission_depth = 0 disables Beer-Lambert
    // and falls back to a thin-approximation tint (equivalent to the pre-
    // stack baseColor-based attenuation).
    [YamlMember(Alias = "transmission_color")]
    public List<float>? TransmissionColor { get; set; }

    [YamlMember(Alias = "transmission_depth")]
    public float TransmissionDepth { get; set; } = 0f;

    // ── Disney 2015: subsurface / leaf-like thin-walled extensions ──────────
    // subsurface_color: tints the approximate subsurface lobe (used in place
    //   of base_color inside the Hanrahan-Krueger blend). Defaults to null,
    //   in which case base_color is used — matching the pre-Disney-2015
    //   single-colour convention.
    // subsurface_radius: per-channel mean free path (world units) for the
    //   future random-walk SSS implementation. Currently unused by the
    //   approximate SS lobe but persisted in the material so existing scene
    //   files authored for the full SSS pipeline can be loaded.
    // thin_walled: treats the surface as having no interior (leaves, paper,
    //   fabric). Disables refraction; enables diff_trans backface sampling.
    // diff_trans: fraction of the diffuse lobe that transmits through the
    //   surface — relevant when thin_walled or when modelling foliage.
    // flatness: blends the Lambert diffuse lobe with a flat (subsurface-
    //   approximation) lobe. 1.0 gives the classic HK "flat" look even for
    //   surfaces that don't use the full subsurface parameter.
    [YamlMember(Alias = "subsurface_color")]
    public List<float>? SubsurfaceColor { get; set; }

    [YamlMember(Alias = "subsurface_radius")]
    public List<float>? SubsurfaceRadius { get; set; }

    [YamlMember(Alias = "thin_walled")]
    public bool ThinWalled { get; set; } = false;

    [YamlMember(Alias = "diff_trans")]
    public float DiffTrans { get; set; } = 0f;

    [YamlMember(Alias = "flatness")]
    public float Flatness { get; set; } = 0f;

    // ── Disney BSDF parameter textures (optional; override the scalar) ──────
    // Each *_texture block is parsed as a full TextureData, so any existing
    // texture type (solid, image, checker, noise, marble, wood) works. When
    // present it replaces the corresponding scalar at lookup time; when null
    // the scalar value above is used unchanged.
    [YamlMember(Alias = "metallic_texture")]
    public TextureData? MetallicTexture { get; set; }

    [YamlMember(Alias = "roughness_texture")]
    public TextureData? RoughnessTexture { get; set; }

    [YamlMember(Alias = "subsurface_texture")]
    public TextureData? SubsurfaceTexture { get; set; }

    [YamlMember(Alias = "specular_texture")]
    public TextureData? SpecularTexture { get; set; }

    [YamlMember(Alias = "specular_tint_texture")]
    public TextureData? SpecularTintTexture { get; set; }

    [YamlMember(Alias = "sheen_texture")]
    public TextureData? SheenTexture { get; set; }

    [YamlMember(Alias = "sheen_tint_texture")]
    public TextureData? SheenTintTexture { get; set; }

    [YamlMember(Alias = "sheen_roughness_texture")]
    public TextureData? SheenRoughnessTexture { get; set; }

    [YamlMember(Alias = "clearcoat_texture")]
    public TextureData? ClearcoatTexture { get; set; }

    [YamlMember(Alias = "clearcoat_gloss_texture")]
    public TextureData? ClearcoatGlossTexture { get; set; }

    [YamlMember(Alias = "spec_trans_texture")]
    public TextureData? SpecTransTexture { get; set; }

    [YamlMember(Alias = "ior_texture")]
    public TextureData? IorTexture { get; set; }

    [YamlMember(Alias = "anisotropic_texture")]
    public TextureData? AnisotropicTexture { get; set; }

    [YamlMember(Alias = "anisotropic_rotation_texture")]
    public TextureData? AnisotropicRotationTexture { get; set; }

    [YamlMember(Alias = "transmission_color_texture")]
    public TextureData? TransmissionColorTexture { get; set; }

    [YamlMember(Alias = "transmission_depth_texture")]
    public TextureData? TransmissionDepthTexture { get; set; }

    [YamlMember(Alias = "subsurface_color_texture")]
    public TextureData? SubsurfaceColorTexture { get; set; }

    [YamlMember(Alias = "diff_trans_texture")]
    public TextureData? DiffTransTexture { get; set; }

    [YamlMember(Alias = "flatness_texture")]
    public TextureData? FlatnessTexture { get; set; }

    // ── Arnold standard_surface "coat" parameters ──────────────────────────
    // coat_ior:       index of refraction of the lacquer layer (default 1.5,
    //                 matching the legacy Disney F0 = 0.04). Higher values
    //                 brighten the coat highlight (1.7-2.4 for automotive
    //                 paint and diamond-clear coats).
    // coat_roughness: optional explicit roughness in [0, 1] for the coat
    //                 lobe; α = roughness² (matches the base specular
    //                 mapping). When omitted (null) the legacy
    //                 clearcoat_gloss slider is used so existing scenes
    //                 keep their look.
    // coat_normal_map: dedicated normal map perturbing the coat highlight
    //                 independently of the base NormalMap. Models scratches
    //                 or orange peel in the lacquer that sit on top of an
    //                 otherwise-different substrate normal.
    [YamlMember(Alias = "coat_ior")]
    public float CoatIor { get; set; } = 1.5f;

    // -1 sentinel matches the DisneyBsdf "use legacy gloss" path. Authors
    // should set the value in [0, 1] to take the explicit-roughness branch.
    [YamlMember(Alias = "coat_roughness")]
    public float CoatRoughness { get; set; } = -1f;

    [YamlMember(Alias = "coat_ior_texture")]
    public TextureData? CoatIorTexture { get; set; }

    [YamlMember(Alias = "coat_roughness_texture")]
    public TextureData? CoatRoughnessTexture { get; set; }

    [YamlMember(Alias = "coat_normal_map")]
    public NormalMapData? CoatNormalMap { get; set; }

    // ── Thin-film iridescence (Belcour-Barla 2017) ─────────────────────────
    // thin_film_thickness: film thickness in nanometres. 0 (default) leaves
    //   the BSDF on the plain Schlick Fresnel; values in 100-800 nm produce
    //   the visible-spectrum colour sweep characteristic of soap bubbles,
    //   beetle elytra and anti-reflection coatings.
    // thin_film_ior: refractive index of the film (default 1.5 — generic
    //   lacquer). Drives the contrast of the interference fringes; higher
    //   IOR with the same thickness yields more saturated colours.
    [YamlMember(Alias = "thin_film_thickness")]
    public float ThinFilmThickness { get; set; } = 0f;

    [YamlMember(Alias = "thin_film_ior")]
    public float ThinFilmIor { get; set; } = 1.5f;

    [YamlMember(Alias = "thin_film_thickness_texture")]
    public TextureData? ThinFilmThicknessTexture { get; set; }

    [YamlMember(Alias = "thin_film_ior_texture")]
    public TextureData? ThinFilmIorTexture { get; set; }

    // ── Mix Material parameters ─────────────────────────────────────────────

    [YamlMember(Alias = "material_a")]
    public string? MaterialA { get; set; }

    [YamlMember(Alias = "material_b")]
    public string? MaterialB { get; set; }

    [YamlMember(Alias = "blend")]
    public float Blend { get; set; } = 0.5f;

    [YamlMember(Alias = "mask")]
    public TextureData? Mask { get; set; }

    // ── Surface displacement (Cycles/RenderMan parity) ───────────────────────
    // Material-level displacement lets a single displaced material drive
    // multiple mesh entities without per-entity duplication. On a Mix
    // material the inner block can also enable `blend_with_mask: true` to
    // vector-blend the children's displacement at the vertex via the same
    // mask the BSDF blend uses (Cycles' "Mix Shader → Displacement" path).
    /// <summary>
    /// Material-level displacement block. When set and the entity using this
    /// material is a polygonal mesh, the loader deforms the (sub)divided
    /// limit topology before BVH construction. Non-mesh entities (analytic
    /// primitives, CSG, groups) emit a load warning and ignore the block.
    /// </summary>
    [YamlMember(Alias = "displacement")]
    public DisplacementData? Displacement { get; set; }
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

public class BumpMapData
{
    [YamlMember(Alias = "texture")]
    public TextureData? Texture { get; set; }

    [YamlMember(Alias = "strength")]
    public float Strength { get; set; } = 1f;

    [YamlMember(Alias = "scale")]
    public float Scale { get; set; } = 1f;
}

/// <summary>
/// YAML block for the surface displacement on a mesh entity
/// (Arnold/RenderMan/Cycles parity — DEVLOG steps 3/5 scalar, 4/5 vector).
///
/// <para>Scalar mode: <c>v' = v + scale · (luminance(texture) − midlevel) · n_smooth</c>.
/// Vector mode: <c>v' = v + scale · (rgb − midlevel) · basis</c>, where
/// <c>basis</c> is the per-vertex TBN frame (tangent space) or the identity
/// (object space).</para>
/// </summary>
public class DisplacementData
{
    /// <summary>
    /// <c>"scalar"</c> (default) or <c>"vector"</c>. Scalar reads the
    /// texture's Rec.709 luminance and offsets along the smooth normal;
    /// vector reads the full RGB triplet as a 3D offset (overhangs and
    /// crinkles a height map cannot represent).
    /// </summary>
    [YamlMember(Alias = "mode")]
    public string? Mode { get; set; }

    /// <summary>
    /// Vector-mode only: <c>"tangent"</c> (default) interprets RGB as a
    /// TBN-frame offset, <c>"object"</c> interprets it as a local-space
    /// <c>(x, y, z)</c> offset. Ignored when <see cref="Mode"/> is
    /// <c>"scalar"</c>.
    /// </summary>
    [YamlMember(Alias = "space")]
    public string? Space { get; set; }

    /// <summary>
    /// Inner displacement texture. Any <see cref="TextureData"/> works
    /// (procedurals or images) — luminance is read in scalar mode, the
    /// full RGB in vector mode.
    /// </summary>
    [YamlMember(Alias = "texture")]
    public TextureData? Texture { get; set; }

    /// <summary>
    /// Signed amplitude in world units. The applied offset is
    /// <c>scale · (h − midlevel)</c> in scalar mode and
    /// <c>scale · (rgb − midlevel)</c> in vector mode. 0 disables the
    /// displacement.
    /// </summary>
    [YamlMember(Alias = "scale")]
    public float Scale { get; set; } = 0.1f;

    /// <summary>
    /// Reference texture value treated as "no displacement". Defaults to 0
    /// (matches RenderMan's <c>dispMidpoint</c> and Arnold's
    /// <c>vector_zero_value</c>). Use 0.5 for 8-bit storage where the
    /// midpoint of <c>[0, 1]</c> means "flat" (both for greyscale height
    /// maps and for vector-displacement EXRs/PNGs baked from Mudbox/
    /// ZBrush with the standard unsigned remap).
    /// </summary>
    [YamlMember(Alias = "midlevel")]
    public float Midlevel { get; set; } = 0f;

    /// <summary>
    /// Uniform UV multiplier stacked on top of any per-texture <c>uv_scale</c>.
    /// </summary>
    [YamlMember(Alias = "uv_scale")]
    public float UvScale { get; set; } = 1f;

    /// <summary>
    /// "Autobump" toggle — when <c>true</c> the engine derives a residual
    /// bump map from the same displacement texture and attaches it to the
    /// mesh, so sub-pixel detail finer than the subdivision grid still
    /// shows up in the shading normal. Combines with any explicit
    /// <c>bump_map</c> on the material (Arnold composes them additively;
    /// our pipeline applies the material bump first, then the autobump).
    /// Disabled by default — when omitted the step-3/step-4 displacement
    /// behaviour is byte-identical to before. Mirrors Arnold's
    /// <c>autobump_visibility</c> flag on <c>polymesh</c> nodes (the
    /// engine-level equivalent of "use the high-frequency detail of the
    /// displacement texture as a bump").
    /// </summary>
    [YamlMember(Alias = "autobump")]
    public bool Autobump { get; set; } = false;

    /// <summary>
    /// Bump-strength multiplier passed to the derived
    /// <see cref="Textures.BumpMapTexture"/>. Defaults to the displacement
    /// scale so the autobump amplitude is in lockstep with the macro
    /// displacement (an authoring shortcut — same physical interpretation
    /// as Arnold's <c>autobump</c> auto-magnitude). Set explicitly to
    /// override.
    /// </summary>
    [YamlMember(Alias = "autobump_strength")]
    public float AutobumpStrength { get; set; } = 1f;

    /// <summary>
    /// UV-frequency multiplier for the autobump. Defaults to <c>1.0</c>
    /// (sample at the displacement's native frequency); raise it to tile
    /// the bump finer than the displacement and capture sub-grid detail
    /// (Arnold's <c>autobump</c> defaults to the same frequency; the
    /// extra dial mirrors the manual workflow of stacking a finer-tiled
    /// bump on top of the displacement texture).
    /// </summary>
    [YamlMember(Alias = "autobump_scale")]
    public float AutobumpScale { get; set; } = 1f;

    /// <summary>
    /// Maximum expected displacement amplitude in world units. Used to pad
    /// every BVH leaf AABB so shading-time bump perturbation stays inside the
    /// boxes the BVH was built with. Mirrors Arnold's <c>disp_padding</c> and
    /// RenderMan's <c>dispBound</c>. When 0 (default) the loader auto-derives
    /// it from <see cref="Scale"/>: <c>|scale|</c> in scalar mode,
    /// <c>|scale|·√3</c> in vector mode.
    /// </summary>
    [YamlMember(Alias = "bound")]
    public float Bound { get; set; } = 0f;

    /// <summary>
    /// On a Mix material's <c>displacement:</c> block: opt-in to vector-blend
    /// the two children's per-vertex displacement offsets using the Mix's own
    /// mask/blend factor (Cycles "Mix Shader → Displacement" parity). The
    /// other displacement fields above are ignored on the Mix; the Mix's
    /// displacement is purely a blend of the children's. Defaults to false
    /// (the Mix has no displacement of its own; if the user wants the Mix to
    /// displace the geometry they must set this to true).
    /// </summary>
    [YamlMember(Alias = "blend_with_mask")]
    public bool BlendWithMask { get; set; } = false;

    /// <summary>
    /// Tri-state Cycles-style mode: <c>"both"</c> (default) applies both
    /// geometric displacement and (when <see cref="Autobump"/> is true) a
    /// residual bump; <c>"displacement"</c> applies only the geometric
    /// displacement, never the autobump; <c>"bump_only"</c> skips the
    /// geometric displacement and turns the texture into a pure bump map.
    /// </summary>
    [YamlMember(Alias = "displacement_method")]
    public string? Method { get; set; }
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

    // --- Pro-grade procedural parameters (opt-in; back-compat preserved when unset) ---

    [YamlMember(Alias = "noise_type")]
    public string? NoiseTypeName { get; set; }

    [YamlMember(Alias = "octaves")]
    public int? Octaves { get; set; }

    [YamlMember(Alias = "lacunarity")]
    public float? Lacunarity { get; set; }

    [YamlMember(Alias = "gain")]
    public float? Gain { get; set; }

    [YamlMember(Alias = "distortion")]
    public float? Distortion { get; set; }

    // Musgrave multifractal — only used by noise_type: hetero_terrain | hybrid_multifractal.
    [YamlMember(Alias = "fractal_increment")]
    public float? FractalIncrement { get; set; }

    [YamlMember(Alias = "fractal_offset")]
    public float? FractalOffset { get; set; }

    [YamlMember(Alias = "vein_axis")]
    public List<float>? VeinAxis { get; set; }

    [YamlMember(Alias = "vein_frequency")]
    public float? VeinFrequency { get; set; }

    [YamlMember(Alias = "vein_sharpness")]
    public float? VeinSharpness { get; set; }

    // Marble — studio-quality secondary wave (step 5/7 VFX textures).
    [YamlMember(Alias = "secondary_wave")]
    public SecondaryWaveData? SecondaryWave { get; set; }

    [YamlMember(Alias = "ring_axis")]
    public List<float>? RingAxis { get; set; }

    [YamlMember(Alias = "ring_sharpness")]
    public float? RingSharpness { get; set; }

    [YamlMember(Alias = "axial_grain")]
    public float? AxialGrain { get; set; }

    // Wood — studio-quality controls (step 5/7 VFX textures).
    // `grain_strength` is a forward-compat alias for `noise_strength`.
    [YamlMember(Alias = "grain_strength")]
    public float? GrainStrength { get; set; }

    [YamlMember(Alias = "grain_scale")]
    public float? GrainScale { get; set; }

    [YamlMember(Alias = "figure_scale")]
    public float? FigureScale { get; set; }

    [YamlMember(Alias = "figure_strength")]
    public float? FigureStrength { get; set; }

    [YamlMember(Alias = "radial_anisotropy")]
    public float? RadialAnisotropy { get; set; }

    [YamlMember(Alias = "knot_density")]
    public float? KnotDensity { get; set; }

    // Voronoi
    [YamlMember(Alias = "metric")]
    public string? Metric { get; set; }

    [YamlMember(Alias = "output")]
    public string? Output { get; set; }

    [YamlMember(Alias = "randomness")]
    public float? Randomness { get; set; }

    // Smooth Voronoi (IQ): 0 = hard min (back-compat), ∈ (0,1] enables soft-min.
    [YamlMember(Alias = "smoothness")]
    public float? Smoothness { get; set; }

    // Brick
    [YamlMember(Alias = "brick_width")]
    public float? BrickWidth { get; set; }

    [YamlMember(Alias = "brick_height")]
    public float? BrickHeight { get; set; }

    [YamlMember(Alias = "mortar_size")]
    public float? MortarSize { get; set; }

    [YamlMember(Alias = "row_offset")]
    public float? RowOffset { get; set; }

    [YamlMember(Alias = "color_variation")]
    public float? ColorVariation { get; set; }

    [YamlMember(Alias = "noise_scale")]
    public float? NoiseScale { get; set; }

    // Gradient
    [YamlMember(Alias = "mode")]
    public string? Mode { get; set; }

    [YamlMember(Alias = "axis")]
    public List<float>? Axis { get; set; }

    [YamlMember(Alias = "length")]
    public float? Length { get; set; }

    // Multi-stop colour ramp. When set, overrides the two-colour lerp built
    // from `colors:` on every procedural texture (noise/marble/wood/voronoi/
    // gradient). Each stop carries the interpolation kind used on its
    // outgoing segment (toward the next stop), matching Blender's convention.
    [YamlMember(Alias = "color_ramp")]
    public List<ColorRampStopData>? ColorRamp { get; set; }

    // CoordinateTexture (DEVLOG VFX step 7): reference-space bounds used by
    // `mode: "generated"`. Default unit cube `[-1, 1]³` if omitted.
    [YamlMember(Alias = "bounds_min")]
    public List<float>? BoundsMin { get; set; }

    [YamlMember(Alias = "bounds_max")]
    public List<float>? BoundsMax { get; set; }
}

public class ColorRampStopData
{
    [YamlMember(Alias = "position")]
    public float Position { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "interp")]
    public string? Interp { get; set; }
}

/// <summary>
/// Optional secondary vein wave on <see cref="MarbleTexture"/>. When
/// <see cref="Strength"/> is omitted (or set to 0) the texture stays
/// single-axis legacy. Setting just <c>strength</c> with default axis/
/// frequency already produces visible cross-veining because the loader
/// orthogonalises the secondary axis against the primary at sample time.
/// </summary>
public class SecondaryWaveData
{
    [YamlMember(Alias = "axis")]
    public List<float>? Axis { get; set; }

    [YamlMember(Alias = "frequency")]
    public float? Frequency { get; set; }

    [YamlMember(Alias = "strength")]
    public float? Strength { get; set; }
}

/// <summary>
/// One altitude/slope band of a <c>heightfield</c> primitive. Altitude is
/// normalised to <c>[0, 1]</c> over the heightfield's world Y extent above
/// <c>sea_level</c>; slope is in degrees off vertical (0 = flat, 90 = cliff).
/// </summary>
public class StratumData
{
    [YamlMember(Alias = "min_altitude")]
    public float MinAltitude { get; set; } = 0f;

    [YamlMember(Alias = "max_altitude")]
    public float MaxAltitude { get; set; } = 1f;

    [YamlMember(Alias = "min_slope_deg")]
    public float MinSlopeDeg { get; set; } = 0f;

    [YamlMember(Alias = "max_slope_deg")]
    public float MaxSlopeDeg { get; set; } = 90f;

    /// <summary>
    /// Soft-edge width over which the band fades to 0 outside
    /// <c>[min_altitude, max_altitude]</c>. v1 stratum selection is winner
    /// takes all, so this currently only affects the band's effective
    /// dominance radius; future versions will lerp adjacent strata's
    /// materials over the fade region.
    /// </summary>
    [YamlMember(Alias = "blend_width")]
    public float BlendWidth { get; set; } = 0f;

    [YamlMember(Alias = "material")]
    public string? Material { get; set; }
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

    /// <summary>
    /// When <c>false</c>, this entity is invisible to primary camera rays
    /// while still receiving and casting indirect light, appearing in
    /// specular reflections/refractions and contributing to direct lighting
    /// (if emissive) through NEE. Mirrors Arnold's <c>camera</c> visibility
    /// flag and Cycles' "Ray Visibility → Camera". Default <c>true</c>.
    /// Applies uniformly to all entity types (primitive, csg, mesh, group,
    /// instance); on a group the flag propagates to every child via the
    /// outer wrapper.
    /// </summary>
    [YamlMember(Alias = "visible_to_camera")]
    public bool VisibleToCamera { get; set; } = true;

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

    // ── Mesh subdivision (Loop / Catmull-Clark) ─────────────────────────────

    /// <summary>
    /// Subdivision algorithm to apply to the loaded OBJ mesh before BVH
    /// construction. One of <c>none</c> (default), <c>loop</c>,
    /// <c>catmull_clark</c> (or <c>catmull-clark</c> / <c>cc</c>),
    /// <c>auto</c>. <c>auto</c> picks Catmull-Clark for all-quad input,
    /// Loop for all-triangle input, Catmull-Clark for mixed.
    /// </summary>
    [YamlMember(Alias = "subdivision_scheme")]
    public string? SubdivisionScheme { get; set; }

    /// <summary>
    /// Number of uniform subdivision iterations. Each iteration multiplies
    /// face count by 4 and roughly halves edge length. Clamped to
    /// <c>[0, subdivision_max_iterations]</c>. Default 0 (no subdivision).
    /// </summary>
    [YamlMember(Alias = "subdivision_iterations")]
    public int SubdivisionIterations { get; set; } = 0;

    /// <summary>
    /// Adaptive screen-space target: the loader picks the iteration count
    /// that brings the mesh's longest projected edge below this many pixels.
    /// Combined with the static <see cref="SubdivisionIterations"/> by
    /// taking the max. 0 = disabled. Default 0.
    /// </summary>
    [YamlMember(Alias = "subdivision_pixel_error")]
    public float SubdivisionPixelError { get; set; } = 0f;

    /// <summary>
    /// Upper bound for the iteration count, including the adaptive
    /// estimate. Default 6 (4 096× face explosion ceiling).
    /// </summary>
    [YamlMember(Alias = "subdivision_max_iterations")]
    public int SubdivisionMaxIterations { get; set; } = 6;

    // ── Surface displacement (material-level — Cycles/RenderMan parity) ────
    //
    // Displacement is now a property of the material, not the entity. This
    // mirrors Cycles' "Material Output → Displacement" socket and RenderMan's
    // bxdf shader network: a single displaced material drives every mesh that
    // uses it, with no per-entity duplication. The two legacy entity-level
    // fields (`displacement` and `displacement_bound`) are deserialized into
    // sentinels below so the loader can raise a clear migration error rather
    // than silently ignoring them.

    /// <summary>
    /// When false, suppresses the resolved material's displacement for this
    /// single entity (the material itself is still shared). Defaults to
    /// true. Useful for per-instance overrides, e.g. a "low detail" copy of
    /// a displaced rock that uses the same material but skips the
    /// subdivision/displacement cost.
    /// </summary>
    [YamlMember(Alias = "displacement_enabled")]
    public bool DisplacementEnabled { get; set; } = true;

    /// <summary>
    /// Legacy field — the entity-level <c>displacement:</c> block has moved
    /// to the material. The loader detects a non-null value here and raises
    /// a hard error pointing at the material-level migration. Do not use.
    /// </summary>
    [YamlMember(Alias = "displacement")]
    public DisplacementData? LegacyEntityDisplacement { get; set; }

    /// <summary>
    /// Legacy field — the entity-level <c>displacement_bound</c> override has
    /// moved to <c>material.displacement.bound</c>. Detected and reported as
    /// a load error. Do not use.
    /// </summary>
    [YamlMember(Alias = "displacement_bound")]
    public float LegacyEntityDisplacementBound { get; set; } = 0f;

    // Plane
    [YamlMember(Alias = "normal")]
    public List<float>? Normal { get; set; }

    [YamlMember(Alias = "point")]
    public List<float>? Point { get; set; }

    // ── HeightField (Mitsuba-style procedural/baked terrain) ────────────────

    /// <summary>
    /// XZ extents of the heightfield rectangle as <c>[xMin, zMin, xMax, zMax]</c>.
    /// </summary>
    [YamlMember(Alias = "bounds")]
    public List<float>? Bounds { get; set; }

    /// <summary>
    /// Maximum world-space height the heightfield may reach. Drives the
    /// primitive's AABB and is independent of <c>height_scale</c> (the AABB
    /// remains a safe upper bound even when the actual peak under-shoots).
    /// </summary>
    [YamlMember(Alias = "max_height")]
    public float MaxHeight { get; set; } = 25f;

    /// <summary>
    /// Multiplicative scale applied to the unit-range height samples before
    /// they are stored in the primitive (so e.g. a normalised PNG-16 value of
    /// <c>1.0</c> becomes <c>height_scale</c> world units in Y).
    /// </summary>
    [YamlMember(Alias = "height_scale")]
    public float HeightScale { get; set; } = 1f;

    /// <summary>
    /// Procedural height definition. Mutually exclusive with
    /// <see cref="HeightmapPath"/> — when both are set, the path wins and a
    /// deferred warning is emitted.
    /// </summary>
    [YamlMember(Alias = "height_texture")]
    public TextureData? HeightTexture { get; set; }

    /// <summary>
    /// Path to a PNG (16-bit preferred) heightmap, resolved relative to the
    /// scene file. The luminance / red channel is read as the height in
    /// <c>[0, 1]</c>; values are scaled by <c>height_scale</c>.
    /// </summary>
    [YamlMember(Alias = "heightmap_path")]
    public string? HeightmapPath { get; set; }

    /// <summary>
    /// For procedural mode: side length of the pre-sampling grid feeding the
    /// min/max pyramid. Final visual quality is set by the bilinear patch +
    /// bisection; this only governs the acceleration structure's tightness.
    /// </summary>
    [YamlMember(Alias = "resolution")]
    public int Resolution { get; set; } = 512;

    /// <summary>
    /// World-space Y of an optional water plane clipped to the heightfield
    /// footprint. When set, the plane is treated as an extra surface;
    /// <see cref="SeaMaterial"/> drives its shading.
    /// </summary>
    [YamlMember(Alias = "sea_level")]
    public float? SeaLevel { get; set; }

    /// <summary>
    /// Material ID for the water surface (see <see cref="SeaLevel"/>). When
    /// null or empty no water is rendered.
    /// </summary>
    [YamlMember(Alias = "sea_material")]
    public string? SeaMaterial { get; set; }

    /// <summary>
    /// Altitude/slope-driven material bands layered on top of the fallback
    /// <see cref="EntityData.Material"/>. Order is insignificant — at each
    /// shading point the band with the highest combined alt×slope weight wins.
    /// </summary>
    [YamlMember(Alias = "strata")]
    public List<StratumData>? Strata { get; set; }

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

    // ── Lathe (Surface of Revolution) ───────────────────────────────────────

    /// <summary>
    /// Profile points for a <c>type: "lathe"</c> entity. Each entry is a
    /// <c>[r, y]</c> pair in lathe-local coordinates (r ≥ 0, y monotonically
    /// non-decreasing). Must contain at least two points.
    /// </summary>
    [YamlMember(Alias = "profile")]
    public List<List<float>>? Profile { get; set; }

    /// <summary>
    /// Interpolation mode for the <c>profile</c>. One of <c>linear</c>
    /// (default — polyline / faceted frustum stack), <c>catmull_rom</c>
    /// (centripetal, smooth, passes through all points) or <c>bezier</c>
    /// (explicit control points via <c>profile_bezier_controls</c>).
    /// </summary>
    [YamlMember(Alias = "profile_type")]
    public string? ProfileType { get; set; }

    /// <summary>
    /// Explicit cubic-Bezier control points for <c>profile_type: bezier</c>.
    /// Layout: four <c>[r, y]</c> entries per segment, concatenated for every
    /// segment of the profile (length must equal <c>4 · (profile.Count − 1)</c>
    /// for a lathe, or <c>4 · profile.Count</c> for a closed extrusion).
    /// Ignored for the linear and Catmull-Rom modes.
    /// </summary>
    [YamlMember(Alias = "profile_bezier_controls")]
    public List<List<float>>? ProfileBezierControls { get; set; }

    // ── Extrusion (Linear extrusion of a 2D profile) ────────────────────────

    /// <summary>
    /// Which ends of an <c>extrusion</c> are closed by a triangulated cap.
    /// One of <c>both</c> (default), <c>start</c>, <c>end</c> or <c>none</c>.
    /// </summary>
    [YamlMember(Alias = "caps")]
    public string? Caps { get; set; }

    /// <summary>
    /// Total rotation of the top profile around the Y axis for an
    /// <c>extrusion</c>, in degrees. Default 0 (straight prism).
    /// </summary>
    [YamlMember(Alias = "twist_degrees")]
    public float TwistDegrees { get; set; } = 0f;

    /// <summary>
    /// Uniform XZ scale of the top profile relative to the bottom for an
    /// <c>extrusion</c>. 1 = straight prism, &lt; 1 narrows toward the top
    /// (pyramid-like), &gt; 1 flares outward. Must be &gt; 0.
    /// </summary>
    [YamlMember(Alias = "taper")]
    public float Taper { get; set; } = 1f;

    /// <summary>
    /// For curved <c>extrusion</c> profile types (catmull_rom, bezier), how
    /// many polyline samples to emit per input segment. Higher values produce
    /// smoother silhouettes at the cost of more triangles in the internal
    /// BVH. Default 16.
    /// </summary>
    [YamlMember(Alias = "curve_samples")]
    public int CurveSamples { get; set; } = 16;

    /// <summary>
    /// For <c>profile_type: linear</c> only: dihedral threshold (degrees)
    /// for auto-smoothing across adjacent profile segments. Pairs of side
    /// walls whose face normals make an angle below this value share a
    /// blended vertex normal (smooth shading) while pairs above it stay
    /// hard. Set to 0 to disable smoothing entirely (faceted look — the
    /// historical default for polyline profiles). 30° is a sensible
    /// default that softens polyline-approximated curves while preserving
    /// 90° corners on letters, gears and engineered profiles.
    /// </summary>
    [YamlMember(Alias = "crease_angle")]
    public float CreaseAngle { get; set; } = 0f;

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

    /// <summary>Alias for <see cref="Corner"/> used in portal-light YAML.</summary>
    [YamlMember(Alias = "anchor")]
    public List<float>? Anchor { get; set; }

    [YamlMember(Alias = "u")]
    public List<float>? U { get; set; }

    [YamlMember(Alias = "v")]
    public List<float>? V { get; set; }

    [YamlMember(Alias = "shadow_samples")]
    public int ShadowSamples { get; set; } = 4;

    [YamlMember(Alias = "radius")]
    public float Radius { get; set; } = 0.5f;

    /// <summary>
    /// Optional virtual-disc radius for <c>point</c>/<c>spot</c>/<c>area</c>
    /// lights that softens the 1/d² (or cosLight/d²) singularity. 0 =
    /// unclamped, identical to pre-existing behaviour. Recommended values
    /// approximate the physical emitter size (e.g. 0.05–0.2 for a streetlamp,
    /// 0.5–1.0 for a large area panel in dense fog).
    /// <para>
    /// Sphere lights deliberately ignore this knob: their solid-angle
    /// estimator is bounded by construction and needs no floor.
    /// </para>
    /// </summary>
    [YamlMember(Alias = "soft_radius")]
    public float SoftRadius { get; set; } = 0f;

    /// <summary>
    /// Optional angular radius in degrees for <c>directional</c>/<c>sun</c>
    /// lights. Produces a soft penumbra. 0 = hard shadows (default).
    /// The real Sun subtends ~0.27°.
    /// </summary>
    [YamlMember(Alias = "angular_radius")]
    public float AngularRadius { get; set; } = 0f;

    /// <summary>
    /// When <c>false</c>, the light's visible proxy (the <c>Sphere</c> for
    /// <c>type: sphere</c>, the <c>Quad</c> for <c>type: area</c>) is hidden
    /// from primary camera rays — the light still illuminates the scene via
    /// NEE and remains visible in specular reflections/refractions and to
    /// indirect bounces. Mirrors Arnold's <c>camera</c> visibility flag and
    /// Cycles' "Ray Visibility → Camera". Default <c>true</c>.
    /// <para>
    /// Has no observable effect on delta lights (<c>point</c>,
    /// <c>directional</c>, <c>spot</c>) which carry no proxy geometry.
    /// </para>
    /// </summary>
    [YamlMember(Alias = "visible_to_camera")]
    public bool VisibleToCamera { get; set; } = true;
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

    // ── atmosphere (Nishita aerial perspective) ─────────────────────────────
    /// <summary>RGB air-density multiplier (grey by default). Tints Rayleigh scattering.</summary>
    [YamlMember(Alias = "air_density")]
    public List<float>? AirDensity { get; set; }

    /// <summary>Mie dust density (0 = pristine, 1 = clean, &gt;1 = polluted). Default 1.</summary>
    [YamlMember(Alias = "dust_density")]
    public float DustDensity { get; set; } = 1f;

    /// <summary>Metres of real atmosphere per world unit. 1000 = 1 wu : 1 km. Default 1000.</summary>
    [YamlMember(Alias = "world_scale")]
    public float WorldScale { get; set; } = 1000f;
}
