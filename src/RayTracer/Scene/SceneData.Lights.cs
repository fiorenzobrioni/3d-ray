using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: lights (see SceneData.cs).

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
    public List<double>? Position { get; set; }

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
    [YamlMember(Alias = "corner")] [OnlyFor("area", "portal")] public List<double>? Corner { get; set; }
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
