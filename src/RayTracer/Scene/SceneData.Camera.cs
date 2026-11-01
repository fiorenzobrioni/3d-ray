using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: camera (see SceneData.cs).

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
    public List<double>? Position { get; set; }

    [YamlMember(Alias = "look_at")]
    public List<double>? LookAt { get; set; }

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
    public List<double>? FocusPoint { get; set; }

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
    [YamlMember(Alias = "position")] public List<double>? Position { get; set; }
    [YamlMember(Alias = "look_at")] public List<double>? LookAt { get; set; }
    [YamlMember(Alias = "up")] public List<float>? Up { get; set; }
    [YamlMember(Alias = "fov")] public float? Fov { get; set; }
}
