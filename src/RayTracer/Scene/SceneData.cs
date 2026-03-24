using YamlDotNet.Serialization;

namespace RayTracer.Scene;

/// <summary>
/// POCO classes matching the YAML scene schema.
/// </summary>
public class SceneData
{
    [YamlMember(Alias = "world")]
    public WorldData? World { get; set; }

    [YamlMember(Alias = "camera")]
    public CameraData? Camera { get; set; }

    [YamlMember(Alias = "materials")]
    public List<MaterialData>? Materials { get; set; }

    [YamlMember(Alias = "entities")]
    public List<EntityData>? Entities { get; set; }

    [YamlMember(Alias = "lights")]
    public List<LightData>? Lights { get; set; }
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
    /// <summary>
    /// Sky mode: "gradient" enables vertical color lerp + optional sun disk.
    /// Any other value (or absent) falls back to legacy flat background.
    /// </summary>
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }
 
    /// <summary>
    /// File path for HDRI environment maps (type: "hdri").
    /// Supports Radiance .hdr format. Resolved relative to the scene YAML directory.
    /// </summary>
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }
 
    /// <summary>
    /// Brightness multiplier for the HDRI map. Default 1.0 = original exposure.
    /// Increase to brighten the environment lighting, decrease to dim it.
    /// </summary>
    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;
 
    /// <summary>
    /// Y-axis rotation of the HDRI environment in degrees (0–360).
    /// Rotates the environment map around the vertical axis to align
    /// key lighting features (sun, windows) with the scene.
    /// </summary>
    [YamlMember(Alias = "rotation")]
    public float Rotation { get; set; } = 0f;

    /// <summary>Color at the zenith (straight up). Default: deep blue.</summary>
    [YamlMember(Alias = "zenith_color")]
    public List<float>? ZenithColor { get; set; }
 
    /// <summary>Color at the horizon line. Default: pale blue-white.</summary>
    [YamlMember(Alias = "horizon_color")]
    public List<float>? HorizonColor { get; set; }
 
    /// <summary>Color below the horizon (ground reflection). Default: brown-gray.</summary>
    [YamlMember(Alias = "ground_color")]
    public List<float>? GroundColor { get; set; }
 
    /// <summary>Optional sun disk configuration.</summary>
    [YamlMember(Alias = "sun")]
    public SunDiskData? Sun { get; set; }
}
 
public class SunDiskData
{
    /// <summary>
    /// Direction FROM which the sun shines (same convention as DirectionalLight).
    /// Gets negated internally to point TOWARD the sun.
    /// </summary>
    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }
 
    /// <summary>Sun disk color. Default: warm white.</summary>
    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }
 
    /// <summary>
    /// Brightness multiplier for the sun disk. Typical: 5–50.
    /// Higher values create stronger bloom through ACES tone mapping.
    /// </summary>
    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 10f;
 
    /// <summary>
    /// Angular diameter of the hard sun disk in degrees.
    /// Real sun ≈ 0.53°. Typical artistic values: 1–5°.
    /// </summary>
    [YamlMember(Alias = "size")]
    public float Size { get; set; } = 3f;
 
    /// <summary>
    /// Exponent for the glow halo falloff around the disk.
    /// Higher = tighter glow, lower = wider glow.
    /// Typical range: 8–128.
    /// </summary>
    [YamlMember(Alias = "falloff")]
    public float Falloff { get; set; } = 32f;
}

public class CameraData
{
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
}

public class NormalMapData
{
    /// <summary>
    /// File path for the normal map image.
    /// Resolved relative to the scene YAML file directory.
    /// </summary>
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }
 
    /// <summary>
    /// Perturbation strength. 1.0 = full effect, 0.5 = subtle, 2.0 = exaggerated.
    /// </summary>
    [YamlMember(Alias = "strength")]
    public float Strength { get; set; } = 1f;
 
    /// <summary>
    /// UV tiling scale for the normal map: [scaleU, scaleV].
    /// Should match the albedo texture tiling for correct alignment.
    /// </summary>
    [YamlMember(Alias = "uv_scale")]
    public List<float>? UvScale { get; set; }
 
    /// <summary>
    /// If true, inverts the Y (green) channel for DirectX-style normal maps.
    /// Default false = OpenGL convention (Y+ up). Set to true for maps
    /// exported from tools that use DirectX convention.
    /// </summary>
    [YamlMember(Alias = "flip_y")]
    public bool FlipY { get; set; } = false;
}

public class TextureData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>
    /// File path for image textures (type: "image").
    /// Resolved relative to the scene YAML file directory.
    /// </summary>
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }
 
    /// <summary>
    /// UV tiling scale for image textures: [scaleU, scaleV].
    /// Default [1, 1] = no tiling. [2, 2] = texture repeats 2× on each axis.
    /// </summary>
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

    // Box
    [YamlMember(Alias = "min")]
    public List<float>? Min { get; set; }

    [YamlMember(Alias = "max")]
    public List<float>? Max { get; set; }

    // Triangle
    [YamlMember(Alias = "v0")]
    public List<float>? V0 { get; set; }

    [YamlMember(Alias = "v1")]
    public List<float>? V1 { get; set; }

    [YamlMember(Alias = "v2")]
    public List<float>? V2 { get; set; }

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

    // Plane
    [YamlMember(Alias = "normal")]
    public List<float>? Normal { get; set; }

    [YamlMember(Alias = "point")]
    public List<float>? Point { get; set; }

    // Transformations
    [YamlMember(Alias = "translate")]
    public List<float>? Translate { get; set; }

    [YamlMember(Alias = "rotate")]
    public List<float>? Rotate { get; set; }

    [YamlMember(Alias = "scale")]
    public object? Scale { get; set; } // Can be float or List<float>
}

public class LightData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    // ── Point / Spot ──────────────────────────────────────────────
    [YamlMember(Alias = "position")]
    public List<float>? Position { get; set; }

    // ── Directional / Spot ────────────────────────────────────────
    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }

    // ── Shared ────────────────────────────────────────────────────
    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;

    // ── Spot-light cone angles ────────────────────────────────────
    [YamlMember(Alias = "inner_angle")]
    public float InnerAngle { get; set; } = 15f;

    [YamlMember(Alias = "outer_angle")]
    public float OuterAngle { get; set; } = 30f;

    // ── Area light ───────────────────────────────────────────────
    /// <summary>One corner of the rectangular emitter.</summary>
    [YamlMember(Alias = "corner")]
    public List<float>? Corner { get; set; }

    /// <summary>First edge vector of the rectangular emitter.</summary>
    [YamlMember(Alias = "u")]
    public List<float>? U { get; set; }

    /// <summary>Second edge vector of the rectangular emitter.</summary>
    [YamlMember(Alias = "v")]
    public List<float>? V { get; set; }

    /// <summary>
    /// Number of shadow samples for area lights (default 16).
    /// Higher = softer, smoother shadows, but proportionally more render time.
    /// </summary>
    [YamlMember(Alias = "shadow_samples")]
    public int ShadowSamples { get; set; } = 16;
}
