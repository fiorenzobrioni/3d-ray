using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: textures (see SceneData.cs).

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
