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

public class CameraData
{
    [YamlMember(Alias = "position")]
    public List<float>? Position { get; set; }

    [YamlMember(Alias = "look_at")]
    public List<float>? LookAt { get; set; }

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

    [YamlMember(Alias = "texture")]
    public TextureData? Texture { get; set; }
}

public class TextureData
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "colors")]
    public List<List<float>>? Colors { get; set; } // Per Checker, Marble, Wood

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

    // Box (not used anymore for unit box, but kept for compatibility if needed, though we will ignore it in loader)
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

    [YamlMember(Alias = "position")]
    public List<float>? Position { get; set; }

    [YamlMember(Alias = "direction")]
    public List<float>? Direction { get; set; }

    [YamlMember(Alias = "color")]
    public List<float>? Color { get; set; }

    [YamlMember(Alias = "intensity")]
    public float Intensity { get; set; } = 1f;
}
