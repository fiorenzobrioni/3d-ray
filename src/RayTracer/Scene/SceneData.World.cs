using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: world (see SceneData.cs).

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
/// The sun of a sky. Gradient sky: a disc of <c>luminance</c> nits. HDRI:
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

    /// <summary>Gradient sky: luminance of the disc, nits (default 10).</summary>
    [YamlMember(Alias = "luminance")]
    public float Luminance { get; set; } = 10f;

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
