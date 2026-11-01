using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;
using RayTracer.Textures;
using RayTracer.Rendering;
using RayTracer.Rendering.Sky;
using RayTracer.Volumetrics;

namespace RayTracer.Scene;
public partial class SceneLoader
{
    /// <summary>What a texture feeds: a colour (decoded from sRGB unless the
    /// block says otherwise) or data, used as stored (PIANO-REVIEW.md §3.3).</summary>
    private enum TextureRole { Color, Data }

    private static TextureColorSpace ResolveColorSpace(TextureData t, TextureRole role) => t.ColorSpace switch
    {
        "srgb"   => TextureColorSpace.Srgb,
        "linear" => TextureColorSpace.Linear,
        _        => role == TextureRole.Color ? TextureColorSpace.Srgb : TextureColorSpace.Linear,
    };

    private static ITexture CreateTexture(TextureData t, string sceneDir, TextureRole role = TextureRole.Color)
    {
        // `scale` may be a scalar (isotropic frequency) or a per-axis
        // [sx, sy, sz] vector (anisotropic stretch). Decompose into a dominant
        // scalar — fed to the procedural's frequency and its conservative
        // Nyquist octave clamp — plus a per-axis ratio applied to the sample
        // point. A scalar input yields ratio (1,1,1), i.e. byte-identical to
        // the legacy path.
        var (scaleScalar, srx, sry, srz) = t.ScaleScalarAndRatio();
        Vector3 scaleRatio = new Vector3(srx, sry, srz);

        ITexture tex = t.Type switch
        {
            "checker" => new CheckerTexture(scaleScalar,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.One  : Vector3.One,
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.Zero : Vector3.Zero),

            "noise" => new NoiseTexture(scaleScalar,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.Zero : Vector3.Zero,
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.One  : Vector3.One),

            "marble" => new MarbleTexture(scaleScalar,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.9f) : new Vector3(0.9f),
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.1f) : new Vector3(0.1f)),

            "wood" => new WoodTexture(scaleScalar, t.NoiseStrength ?? 1.5f,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.85f, 0.65f, 0.40f) : new Vector3(0.85f, 0.65f, 0.40f),
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.45f, 0.28f, 0.14f) : new Vector3(0.45f, 0.28f, 0.14f)),

            "voronoi"
                => new VoronoiTexture(scaleScalar,
                    t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.Zero : Vector3.Zero,
                    t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.One  : Vector3.One),

            "brick"
                => new BrickTexture(
                    t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.65f, 0.27f, 0.20f) : new Vector3(0.65f, 0.27f, 0.20f),
                    t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.55f, 0.20f, 0.15f) : new Vector3(0.55f, 0.20f, 0.15f),
                    t.Colors is { Count: > 2 } ? ToVector3(t.Colors[2]) ?? new Vector3(0.85f, 0.83f, 0.78f) : new Vector3(0.85f, 0.83f, 0.78f)),

            "gradient"
                => new GradientTexture(
                    t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.Zero : Vector3.Zero,
                    t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.One  : Vector3.One),

            "coordinate"
                => new CoordinateTexture(),

            "image" => CreateImageTexture(t, sceneDir, role),

            _ => new SolidColor(Vector3.One)
        };

        ApplyTextureParams(tex, t, sceneDir);
        return tex;
    }

    /// <summary>
    /// Creates an ImageTexture from a file path specified in the YAML.
    /// The path is resolved relative to the scene file's directory.
    /// </summary>
    private static ITexture CreateImageTexture(TextureData t, string sceneDir, TextureRole role)
    {
        if (string.IsNullOrWhiteSpace(t.Path))
        {
            Warn("Image texture requires a 'path' field. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f)); // Magenta = missing texture
        }

        // Resolve relative to scene directory
        string imagePath = Path.IsPathRooted(t.Path)
            ? t.Path
            : Path.Combine(sceneDir, t.Path);

        if (!File.Exists(imagePath))
        {
            Warn($"Image texture file not found: {imagePath}. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f));
        }

        try
        {
            float scaleU = t.UvScale is { Count: > 0 } ? t.UvScale[0] : 1f;
            float scaleV = t.UvScale is { Count: > 1 } ? t.UvScale[1] : scaleU;
            return new ImageTexture(imagePath, scaleU, scaleV, ResolveColorSpace(t, role));
        }
        catch (Exception ex)
        {
            Warn($"Failed to load image texture '{imagePath}': {ex.Message}. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f));
        }
    }

    /// <summary>
    /// Applies YAML-specified transform and randomization parameters to a
    /// procedural texture after construction.
    /// Note: <c>noise_strength</c> is supported by <c>noise</c>, <c>marble</c>,
    /// and <c>wood</c> texture types. For <c>marble</c> and <c>wood</c> it
    /// overrides the amplitude already baked into the constructor; for <c>noise</c>
    /// it controls turbulence weight (0 = smooth Perlin, >0 = turbulent output).
    /// </summary>
    private static void ApplyTextureParams(ITexture tex, TextureData t, string sceneDir)
    {
        // Per-axis ratio from a vector `scale` (1,1,1 for a scalar). See
        // CreateTexture for the decomposition rationale.
        var (_, srx, sry, srz) = t.ScaleScalarAndRatio();
        Vector3 scaleRatio = new Vector3(srx, sry, srz);

        if (tex is NoiseTexture nt)
        {
            if (t.Offset   != null) nt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) nt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            nt.ScaleRatio        = scaleRatio;
            nt.RandomizeOffset   = t.RandomizeOffset;
            nt.RandomizeRotation = t.RandomizeRotation;
            if (t.NoiseStrength.HasValue) nt.NoiseStrength = t.NoiseStrength.Value;
            if (t.NoiseTypeName != null) nt.NoiseType = ParseNoiseKind(t.NoiseTypeName);
            if (t.Octaves.HasValue)      nt.Octaves    = Math.Clamp(t.Octaves.Value, 1, 16);
            if (t.Lacunarity.HasValue)   nt.Lacunarity = t.Lacunarity.Value;
            if (t.Gain.HasValue)         nt.Gain       = t.Gain.Value;
            if (t.Distortion.HasValue)   nt.Distortion = t.Distortion.Value;
            if (t.FractalIncrement.HasValue) nt.FractalIncrement = t.FractalIncrement.Value;
            if (t.FractalOffset.HasValue)    nt.FractalOffset    = t.FractalOffset.Value;
            nt.ColorRamp = BuildColorRamp(t.ColorRamp, "noise");
        }
        else if (tex is MarbleTexture mt)
        {
            if (t.Offset   != null) mt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) mt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            mt.ScaleRatio        = scaleRatio;
            mt.RandomizeOffset   = t.RandomizeOffset;
            mt.RandomizeRotation = t.RandomizeRotation;

            if (t.NoiseStrength.HasValue) mt.NoiseStrength = t.NoiseStrength.Value;
            if (t.VeinAxis != null)       mt.VeinAxis      = ToVector3(t.VeinAxis) ?? Vector3.UnitY;

            // Recursive IQ warp
            if (t.WarpAmplitude.HasValue)  mt.WarpAmplitude  = MathF.Max(t.WarpAmplitude.Value, 0f);
            if (t.WarpScale.HasValue)      mt.WarpScale      = MathF.Max(t.WarpScale.Value, 1e-4f);
            if (t.WarpIterations.HasValue) mt.WarpIterations = Math.Clamp(t.WarpIterations.Value, 0, 3);

            // Anisotropic geological fold
            if (t.FoldAmplitude != null)   mt.FoldAmplitude = ToVector3(t.FoldAmplitude) ?? mt.FoldAmplitude;
            if (t.FoldScale.HasValue)      mt.FoldScale     = MathF.Max(t.FoldScale.Value, 1e-4f);

            // Multi-scale ridged vein layers
            if (t.VeinLayers.HasValue)     mt.VeinLayers     = Math.Clamp(t.VeinLayers.Value, 1, 3);
            if (t.VeinScale  is { } vsc)   mt.VeinScales     = vsc.ToArray();
            if (t.VeinWeight is { } vwt)   mt.VeinWeights    = vwt.ToArray();
            ValidateMarbleLayers(mt);
            if (t.Octaves.HasValue)        mt.Octaves        = Math.Clamp(t.Octaves.Value, 1, 16);
            if (t.Lacunarity.HasValue)     mt.Lacunarity     = t.Lacunarity.Value;
            if (t.Gain.HasValue)           mt.Gain           = t.Gain.Value;
            if (t.SoftMaxSharpness.HasValue) mt.SoftMaxSharpness = MathF.Max(t.SoftMaxSharpness.Value, 0.1f);

            // Vein thickness remap
            if (t.VeinThickness.HasValue)  mt.VeinThickness  = Math.Clamp(t.VeinThickness.Value, 0f, 1f);
            if (t.VeinSoftness.HasValue)   mt.VeinSoftness   = Math.Clamp(t.VeinSoftness.Value, 1e-4f, 1f);

            // Background tonal variation
            if (t.BackgroundScale.HasValue)   mt.BackgroundScale   = MathF.Max(t.BackgroundScale.Value, 1e-4f);
            if (t.BackgroundOctaves.HasValue) mt.BackgroundOctaves = Math.Clamp(t.BackgroundOctaves.Value, 1, 16);
            if (t.ColorVariation.HasValue)    mt.ColorVariation    = Math.Clamp(t.ColorVariation.Value, 0f, 1f);

            // Impurities
            if (t.ImpuritiesDensity.HasValue) mt.ImpuritiesDensity = Math.Clamp(t.ImpuritiesDensity.Value, 0f, 1f);
            if (t.ImpuritiesScale.HasValue)   mt.ImpuritiesScale   = MathF.Max(t.ImpuritiesScale.Value, 1e-4f);
            if (t.ImpurityWeight.HasValue)    mt.ImpurityWeight    = t.ImpurityWeight.Value;
            if (t.ImpuritiesTexture is not null)
                mt.ImpuritiesTexture = CreateTexture(t.ImpuritiesTexture, sceneDir, TextureRole.Data);

            // Anisotropic space stretch (geological compression)
            if (t.SpaceStretch != null)
                mt.SpaceStretch = ToVector3(t.SpaceStretch) ?? Vector3.One;

            // Secondary linear cracks (Worley F2 − F1 overlay)
            if (t.CracksDensity.HasValue)  mt.CracksDensity  = Math.Clamp(t.CracksDensity.Value, 0f, 1f);
            if (t.CracksScale.HasValue)    mt.CracksScale    = MathF.Max(t.CracksScale.Value, 1e-4f);
            if (t.CracksSoftness.HasValue) mt.CracksSoftness = Math.Clamp(t.CracksSoftness.Value, 1e-4f, 1f);
            if (t.CracksWeight.HasValue)   mt.CracksWeight   = MathF.Max(t.CracksWeight.Value, 0f);

            // Output mode: `color` (default) or `mask` — packs vein scalar t as
            // (t,t,t) for FloatTexture-driven Disney parameters (roughness,
            // subsurface, sheen, etc.).
            if (t.Output != null) mt.Output = ParseMarbleOutput(t.Output);

            mt.ColorRamp = BuildColorRamp(t.ColorRamp, "marble");
        }
        else if (tex is WoodTexture wt)
        {
            if (t.Offset   != null) wt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) wt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            wt.ScaleRatio        = scaleRatio;
            wt.RandomizeOffset   = t.RandomizeOffset;
            wt.RandomizeRotation = t.RandomizeRotation;

            // Grain amplitude.
            if (t.NoiseStrength.HasValue)    wt.GrainStrength    = t.NoiseStrength.Value;

            // Geometry / ring layout
            if (t.RingAxis != null)          wt.RingAxis         = ToVector3(t.RingAxis) ?? Vector3.UnitY;
            if (t.SpaceStretch != null)      wt.SpaceStretch     = ToVector3(t.SpaceStretch) ?? Vector3.One;

            // Asymmetric ring profile
            if (t.RingSharpness.HasValue)    wt.RingSharpness    = MathF.Max(t.RingSharpness.Value, 0.05f);
            if (t.LatewoodWidth.HasValue)    wt.LatewoodWidth    = Math.Clamp(t.LatewoodWidth.Value, 0.02f, 0.95f);
            if (t.EarlywoodTransition.HasValue) wt.EarlywoodTransition = Math.Clamp(t.EarlywoodTransition.Value, 0.005f, 0.5f);

            // Per-ring variation
            if (t.RingColorVariation.HasValue) wt.RingColorVariation = Math.Clamp(t.RingColorVariation.Value, 0f, 1f);
            if (t.RingWidthVariation.HasValue) wt.RingWidthVariation = Math.Clamp(t.RingWidthVariation.Value, 0f, 0.5f);

            // Multi-band noise
            if (t.AxialGrain.HasValue)       wt.AxialGrain       = t.AxialGrain.Value;
            if (t.Octaves.HasValue)          wt.Octaves          = Math.Clamp(t.Octaves.Value, 1, 16);
            if (t.Lacunarity.HasValue)       wt.Lacunarity       = t.Lacunarity.Value;
            if (t.Gain.HasValue)             wt.Gain             = t.Gain.Value;
            if (t.GrainScale.HasValue)       wt.GrainScale       = MathF.Max(t.GrainScale.Value, 0f);
            if (t.FigureScale.HasValue)      wt.FigureScale      = MathF.Max(t.FigureScale.Value, 0f);
            if (t.FigureStrength.HasValue)   wt.FigureStrength   = MathF.Max(t.FigureStrength.Value, 0f);
            if (t.FigureAspect.HasValue)     wt.FigureAspect     = MathF.Max(t.FigureAspect.Value, 1e-3f);

            // Domain warp (recursive IQ + anisotropic fold).
            if (t.WarpAmplitude.HasValue)    wt.WarpAmplitude    = MathF.Max(t.WarpAmplitude.Value, 0f);
            if (t.WarpScale.HasValue)        wt.WarpScale        = MathF.Max(t.WarpScale.Value, 1e-4f);
            if (t.WarpIterations.HasValue)   wt.WarpIterations   = Math.Clamp(t.WarpIterations.Value, 0, 3);
            if (t.FoldAmplitude != null)     wt.FoldAmplitude    = ToVector3(t.FoldAmplitude) ?? wt.FoldAmplitude;
            if (t.FoldScale.HasValue)        wt.FoldScale        = MathF.Max(t.FoldScale.Value, 1e-4f);

            // Radial anisotropy
            if (t.RadialAnisotropy.HasValue) wt.RadialAnisotropy = MathF.Max(t.RadialAnisotropy.Value, 0f);

            // Knots
            if (t.KnotDensity.HasValue)      wt.KnotDensity      = Math.Clamp(t.KnotDensity.Value, 0f, 1f);
            if (t.KnotScale.HasValue)        wt.KnotScale        = MathF.Max(t.KnotScale.Value, 1e-3f);

            // Open-pore vessels
            if (t.PoreDensity.HasValue)      wt.PoreDensity      = Math.Clamp(t.PoreDensity.Value, 0f, 1f);
            if (t.PoreScale.HasValue)        wt.PoreScale        = MathF.Max(t.PoreScale.Value, 1e-4f);
            if (t.PoreStrength.HasValue)     wt.PoreStrength     = Math.Clamp(t.PoreStrength.Value, 0f, 1f);
            if (t.PoreAspect.HasValue)       wt.PoreAspect       = MathF.Max(t.PoreAspect.Value, 1e-3f);

            // Sapwood/heartwood gradient
            if (t.HeartwoodRadius.HasValue)  wt.HeartwoodRadius  = MathF.Max(t.HeartwoodRadius.Value, 0f);
            if (t.HeartwoodBlend.HasValue)   wt.HeartwoodBlend   = Math.Clamp(t.HeartwoodBlend.Value, -1f, 1f);

            // Output mode — `color` (default) or `mask` for FloatTexture-driven
            // Disney parameters (roughness, sheen, subsurface, ...).
            if (t.Output != null) wt.Output = ParseWoodOutput(t.Output);

            wt.ColorRamp = BuildColorRamp(t.ColorRamp, "wood");
        }
        else if (tex is VoronoiTexture vt)
        {
            if (t.Offset   != null) vt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) vt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            vt.ScaleRatio        = scaleRatio;
            vt.RandomizeOffset   = t.RandomizeOffset;
            vt.RandomizeRotation = t.RandomizeRotation;
            if (t.Metric != null)      vt.Metric     = ParseVoronoiMetric(t.Metric);
            if (t.Output != null)      vt.Output     = ParseVoronoiOutput(t.Output);
            if (t.Randomness.HasValue) vt.Randomness = Math.Clamp(t.Randomness.Value, 0f, 1f);
            if (t.Distortion.HasValue) vt.Distortion = t.Distortion.Value;
            if (t.Smoothness.HasValue) vt.Smoothness = Math.Clamp(t.Smoothness.Value, 0f, 1f);
            vt.ColorRamp = BuildColorRamp(t.ColorRamp, "voronoi");
        }
        else if (tex is BrickTexture bt)
        {
            if (t.Offset   != null) bt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) bt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            bt.RandomizeOffset   = t.RandomizeOffset;
            bt.RandomizeRotation = t.RandomizeRotation;
            if (t.BrickWidth.HasValue)     bt.BrickWidth     = t.BrickWidth.Value;
            if (t.BrickHeight.HasValue)    bt.BrickHeight    = t.BrickHeight.Value;
            if (t.MortarSize.HasValue)     bt.MortarSize     = t.MortarSize.Value;
            if (t.RowOffset.HasValue)      bt.RowOffset      = t.RowOffset.Value;
            if (t.ColorVariation.HasValue) bt.ColorVariation = Math.Clamp(t.ColorVariation.Value, 0f, 1f);
            if (t.NoiseScale.HasValue)     bt.NoiseScale     = t.NoiseScale.Value;
        }
        else if (tex is GradientTexture gt)
        {
            if (t.Offset   != null) gt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) gt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            gt.RandomizeOffset   = t.RandomizeOffset;
            gt.RandomizeRotation = t.RandomizeRotation;
            if (t.Mode != null)   gt.Mode   = ParseGradientMode(t.Mode);
            if (t.Axis != null)   gt.Axis   = ToVector3(t.Axis) ?? Vector3.UnitX;
            if (t.Length.HasValue) gt.Length = t.Length.Value;
            gt.ColorRamp = BuildColorRamp(t.ColorRamp, "gradient");
        }
        else if (tex is CoordinateTexture ct)
        {
            // CoordinateTexture: per-mode knobs. Defaults are already set on
            // the texture instance; only override when YAML supplies a value.
            if (t.Mode != null)         ct.Mode      = ParseCoordinateMode(t.Mode);
            // `scale` defaults to 1 (matching CoordinateTexture's own default).
            // CoordinateTexture is isotropic, so it uses only the dominant
            // component of a vector `scale`.
            if (t.Scale != null)        ct.Scale     = t.ScaleScalarAndRatio().Scalar;
            if (t.Offset   != null)     ct.Offset    = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null)     ct.Rotation  = ToVector3(t.Rotation) ?? Vector3.Zero;
            if (t.BoundsMin != null)    ct.BoundsMin = ToVector3(t.BoundsMin) ?? new Vector3(-1f);
            if (t.BoundsMax != null)    ct.BoundsMax = ToVector3(t.BoundsMax) ?? new Vector3( 1f);
        }
    }

    private static NoiseTexture.NoiseKind ParseNoiseKind(string s) =>
        s switch
        {
            "perlin"              => NoiseTexture.NoiseKind.Perlin,
            "fbm"                 => NoiseTexture.NoiseKind.Fbm,
            "turbulence"          => NoiseTexture.NoiseKind.Turbulence,
            "ridged"              => NoiseTexture.NoiseKind.Ridged,
            "billow"              => NoiseTexture.NoiseKind.Billow,
            "hetero_terrain"      => NoiseTexture.NoiseKind.HeteroTerrain,
            "hybrid_multifractal" => NoiseTexture.NoiseKind.HybridMultifractal,
            _                      => NoiseTexture.NoiseKind.Auto,
        };

    /// <summary>
    /// Validates that <see cref="MarbleTexture.VeinScales"/> and
    /// <see cref="MarbleTexture.VeinWeights"/> are coherent with
    /// <see cref="MarbleTexture.VeinLayers"/>. Mismatched lengths are
    /// reported through the deferred warning channel and the arrays are
    /// trimmed/extended to <c>VeinLayers</c> with sane defaults so the
    /// render still produces a valid image.
    /// </summary>
    private static void ValidateMarbleLayers(MarbleTexture mt)
    {
        int n = mt.VeinLayers;
        if (mt.VeinScales.Length != n)
        {
            if (mt.VeinScales.Length != 0)
                Warn($"Marble texture: vein_scale has {mt.VeinScales.Length} entries but vein_layers={n}. Padding/trimming with default 1.0.");
            mt.VeinScales = ResizeWith(mt.VeinScales, n, 1f);
        }
        if (mt.VeinWeights.Length != n)
        {
            if (mt.VeinWeights.Length != 0)
                Warn($"Marble texture: vein_weight has {mt.VeinWeights.Length} entries but vein_layers={n}. Padding/trimming with default 1.0.");
            mt.VeinWeights = ResizeWith(mt.VeinWeights, n, 1f);
        }
    }

    private static MarbleTexture.OutputMode ParseMarbleOutput(string s) =>
        s switch
        {
            "mask" => MarbleTexture.OutputMode.Mask,
            _      => MarbleTexture.OutputMode.Color,
        };

    private static WoodTexture.OutputMode ParseWoodOutput(string s) =>
        s switch
        {
            "mask" => WoodTexture.OutputMode.Mask,
            _      => WoodTexture.OutputMode.Color,
        };

    private static float[] ResizeWith(float[] src, int n, float fill)
    {
        if (src.Length == n) return src;
        var dst = new float[n];
        int copy = Math.Min(src.Length, n);
        Array.Copy(src, dst, copy);
        for (int i = copy; i < n; i++) dst[i] = fill;
        return dst;
    }

    private static WorleyNoise.Metric ParseVoronoiMetric(string s) =>
        s switch
        {
            "manhattan"         => WorleyNoise.Metric.Manhattan,
            "chebyshev"         => WorleyNoise.Metric.Chebyshev,
            "euclidean_squared" => WorleyNoise.Metric.EuclideanSquared,
            _ => WorleyNoise.Metric.Euclidean,
        };

    private static VoronoiTexture.OutputMode ParseVoronoiOutput(string s) =>
        s switch
        {
            "f1"            => VoronoiTexture.OutputMode.F1,
            "f2"            => VoronoiTexture.OutputMode.F2,
            "f3"            => VoronoiTexture.OutputMode.F3,
            "f4"            => VoronoiTexture.OutputMode.F4,
            "f2_minus_f1"   => VoronoiTexture.OutputMode.F2MinusF1,
            "f3_minus_f1"   => VoronoiTexture.OutputMode.F3MinusF1,
            "f1_plus_f2"    => VoronoiTexture.OutputMode.F1PlusF2,
            "cell"          => VoronoiTexture.OutputMode.Cell,
            "random"        => VoronoiTexture.OutputMode.Random,
            "position"      => VoronoiTexture.OutputMode.Position,
            _ => VoronoiTexture.OutputMode.F1,
        };

    private static GradientTexture.GradientMode ParseGradientMode(string s) =>
        s switch
        {
            "quadratic" => GradientTexture.GradientMode.Quadratic,
            "easing"    => GradientTexture.GradientMode.Easing,
            "spherical" => GradientTexture.GradientMode.Spherical,
            "radial"    => GradientTexture.GradientMode.Radial,
            _                                   => GradientTexture.GradientMode.Linear,
        };

    private static CoordinateTexture.CoordMode ParseCoordinateMode(string s) =>
        s switch
        {
            "uv"        => CoordinateTexture.CoordMode.UV,
            "generated" => CoordinateTexture.CoordMode.Generated,
            "world"     => CoordinateTexture.CoordMode.World,
            _                                           => CoordinateTexture.CoordMode.Object,
        };

    private static ColorRamp.Interp ParseRampInterp(string? s) =>
        s switch
        {
            "smoothstep" => ColorRamp.Interp.Smoothstep,
            "constant"   => ColorRamp.Interp.Constant,
            "ease"       => ColorRamp.Interp.Ease,
            _                                => ColorRamp.Interp.Linear,
        };

    /// <summary>
    /// Builds a <see cref="ColorRamp"/> from the YAML <c>color_ramp:</c>
    /// list. Returns <c>null</c> when the list is null/empty so the caller
    /// can fall back to the legacy two-colour lerp. Invalid stops (missing
    /// or short colour triplet) are skipped with a deferred warning; if no
    /// valid stops survive, the result is null.
    /// </summary>
    private static ColorRamp? BuildColorRamp(List<ColorRampStopData>? stops, string textureLabel)
    {
        if (stops is null || stops.Count == 0) return null;

        var built = new List<ColorRamp.Stop>(stops.Count);
        foreach (var s in stops)
        {
            Vector3? col = ToVector3(s.Color);
            if (!col.HasValue)
            {
                Warn($"color_ramp stop on '{textureLabel}' missing 3-component color, skipped.");
                continue;
            }
            built.Add(new ColorRamp.Stop(s.Position, col.Value, ParseRampInterp(s.Interp)));
        }
        if (built.Count == 0)
        {
            Warn($"color_ramp on '{textureLabel}' has no valid stops, falling back to colors:.");
            return null;
        }
        return new ColorRamp(built);
    }
}
