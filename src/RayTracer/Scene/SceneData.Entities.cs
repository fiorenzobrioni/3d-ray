using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: entities (see SceneData.cs).

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
