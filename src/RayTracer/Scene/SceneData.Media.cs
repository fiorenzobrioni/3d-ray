using YamlDotNet.Serialization;

namespace RayTracer.Scene;

// Scene format 2 classes, part: media (see SceneData.cs).

/// <summary>
/// A participating medium: <c>world.medium</c>, or an entry of
/// <c>mediums:</c> with an <c>id</c>. Coefficients are per scene unit.
/// </summary>
[SchemaTypes("homogeneous", "height_fog", "procedural", "grid", "atmosphere")]
public class MediumData
{
    [YamlMember(Alias = "id")]
    public string? Id { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    /// <summary>Absorption coefficient per channel (height fog: at <c>y0</c>; procedural, grid: at density 1).</summary>
    [YamlMember(Alias = "sigma_a")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid")]
    public List<float>? SigmaA { get; set; }

    [YamlMember(Alias = "sigma_s")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid")]
    public List<float>? SigmaS { get; set; }

    /// <summary>Phase function (default isotropic; atmosphere: of the dust, default hg 0.76).</summary>
    [YamlMember(Alias = "phase")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")]
    [SchemaEnum("isotropic", "hg", "rayleigh", "schlick", "double_hg")]
    public string? Phase { get; set; }

    /// <summary>Anisotropy of <c>hg</c> and <c>schlick</c>.</summary>
    [YamlMember(Alias = "g")]
    [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")]
    public float G { get; set; }

    [YamlMember(Alias = "g1")] [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")] public float G1 { get; set; } = 0.85f;
    [YamlMember(Alias = "g2")] [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")] public float G2 { get; set; } = -0.3f;
    /// <summary>Weight of the forward lobe of <c>double_hg</c>.</summary>
    [YamlMember(Alias = "w")] [OnlyFor("homogeneous", "height_fog", "procedural", "grid", "atmosphere")] public float W { get; set; } = 0.5f;

    /// <summary>height_fog: altitude of the reference density; atmosphere: sea level.</summary>
    [YamlMember(Alias = "y0")] [OnlyFor("height_fog", "atmosphere")] public float Y0 { get; set; }
    /// <summary>Exponential scale height H: the density falls by e every H.</summary>
    [YamlMember(Alias = "scale_height")] [OnlyFor("height_fog")] public float ScaleHeight { get; set; } = 1f;

    [YamlMember(Alias = "frequency")] [OnlyFor("procedural")] public float Frequency { get; set; } = 1f;
    [YamlMember(Alias = "octaves")] [OnlyFor("procedural")] public int Octaves { get; set; } = 4;
    [YamlMember(Alias = "lacunarity")] [OnlyFor("procedural")] public float Lacunarity { get; set; } = 2f;
    [YamlMember(Alias = "gain")] [OnlyFor("procedural")] public float Gain { get; set; } = 0.5f;
    [YamlMember(Alias = "seed")] [OnlyFor("procedural")] public int Seed { get; set; }

    [YamlMember(Alias = "bounds_min")] [OnlyFor("grid")] public List<double>? BoundsMin { get; set; }
    [YamlMember(Alias = "bounds_max")] [OnlyFor("grid")] public List<double>? BoundsMax { get; set; }
    [YamlMember(Alias = "nx")] [OnlyFor("grid")] public int Nx { get; set; }
    [YamlMember(Alias = "ny")] [OnlyFor("grid")] public int Ny { get; set; }
    [YamlMember(Alias = "nz")] [OnlyFor("grid")] public int Nz { get; set; }
    /// <summary>Inline densities, nx·ny·nz values in z-major order (as a <c>.vol</c> file).</summary>
    [YamlMember(Alias = "data")] [OnlyFor("grid")] public List<float>? Data { get; set; }
    /// <summary>A <c>.vol</c> file, relative to the scene.</summary>
    [YamlMember(Alias = "file")] [OnlyFor("grid")] public string? File { get; set; }

    [YamlMember(Alias = "interpolation")]
    [OnlyFor("grid")]
    [SchemaEnum("trilinear", "tricubic")]
    public string? Interpolation { get; set; }

    /// <summary>Air (Rayleigh) density multiplier, 1 = the standard atmosphere.</summary>
    [YamlMember(Alias = "air_density")] [OnlyFor("atmosphere")] public float AirDensity { get; set; } = 1f;
    [YamlMember(Alias = "ozone_density")] [OnlyFor("atmosphere")] public float OzoneDensity { get; set; } = 1f;
    /// <summary>Mie dust density (0 pristine, 1 clean, above 1 polluted).</summary>
    [YamlMember(Alias = "dust_density")] [OnlyFor("atmosphere")] public float DustDensity { get; set; } = 1f;
}
