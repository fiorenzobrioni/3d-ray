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
    /// <summary>
    /// Dispatches on <c>type</c> to build the appropriate <see cref="IMedium"/>.
    /// Used for both <c>world.medium</c> (global) and the <c>mediums:</c>
    /// library entries referenced by entity-level <c>interior_medium</c>.
    /// Returns null (and warns) on invalid or unknown configurations so the
    /// renderer falls back to vacuum on the affected binding instead of
    /// crashing. <paramref name="context"/> is the label used in diagnostics
    /// (e.g. <c>"world.medium"</c> or <c>"mediums['marble_int']"</c>).
    /// </summary>
    private static IMedium? BuildMedium(MediumData md, string sceneDir, string context = "medium")
    {
        string type = md.Type ?? "homogeneous";
        IPhaseFunction phase = BuildPhaseFunction(md);

        switch (type)
        {
            case "homogeneous":
                return BuildHomogeneous(md, phase);
            case "height_fog":
                return BuildHeightFog(md, phase);
            case "procedural":
                return BuildProcedural(md, phase);
            case "grid":
                return BuildGrid(md, phase, sceneDir);
            case "atmosphere":
                return BuildNishitaAtmosphere(md, phase);
            default:
                Warn($"{context}: unsupported medium type '{type}'. Supported: homogeneous, height_fog, procedural, grid, atmosphere. Ignoring.");
                return null;
        }
    }

    private static NishitaAtmosphereMedium BuildNishitaAtmosphere(MediumData md, IPhaseFunction phase)
    {
        // Air scatters with the Rayleigh phase function; `phase` sets the
        // dust's (Henyey-Greenstein g = 0.76 by default).
        IPhaseFunction? dustPhase = string.IsNullOrWhiteSpace(md.Phase) ? null : phase;
        Info($"Medium:      atmosphere (sea_level={md.Y0}, {_metersPerUnit} m per unit, " +
             $"air {md.AirDensity}, dust {md.DustDensity}, ozone {md.OzoneDensity}, dust phase {md.Phase ?? "hg(0.76)"})");
        return new NishitaAtmosphereMedium(dustPhase,
                                           airDensity: md.AirDensity,
                                           dustDensity: md.DustDensity,
                                           ozoneDensity: md.OzoneDensity,
                                           seaLevelY: md.Y0,
                                           worldScale: _metersPerUnit);
    }

    private static IPhaseFunction BuildPhaseFunction(MediumData md)
    {
        if (string.IsNullOrWhiteSpace(md.Phase)) return new IsotropicPhase();
        switch (md.Phase)
        {
            case "isotropic":
                return new IsotropicPhase();
            case "hg":
                return new HenyeyGreensteinPhase(md.G);
            case "rayleigh":
                return new RayleighPhase();
            case "schlick":
                return new SchlickPhase(md.G);
            case "double_hg":
                return new DoubleHenyeyGreensteinPhase(md.G1, md.G2, md.W);
            default:
                Warn($"Unknown phase function '{md.Phase}'. Supported: isotropic, hg, rayleigh, schlick, double_hg. Falling back to isotropic.");
                return new IsotropicPhase();
        }
    }

    private static (Vector3 A, Vector3 S) ParseSigmas(MediumData md, string context)
    {
        var a = ToVector3(md.SigmaA) ?? Vector3.Zero;
        var s = ToVector3(md.SigmaS) ?? Vector3.Zero;
        if (a.X < 0f || a.Y < 0f || a.Z < 0f || s.X < 0f || s.Y < 0f || s.Z < 0f)
        {
            Warn($"{context} has negative σ_a={a} or σ_s={s}. Clamping to zero.");
            a = Vector3.Max(a, Vector3.Zero);
            s = Vector3.Max(s, Vector3.Zero);
        }
        return (a, s);
    }

    private static IMedium BuildHomogeneous(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'homogeneous'");
        Info($"Medium:      homogeneous ({md.Phase ?? "isotropic"} phase)");
        Verbose($"Medium det:  \u03c3_a={sigmaA}, \u03c3_s={sigmaS}");
        return new HomogeneousMedium(sigmaA, sigmaS, phase);
    }

    private static IMedium BuildHeightFog(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'height_fog'");
        if (md.ScaleHeight <= 0f)
        {
            Warn($"Medium 'height_fog' has non-positive scale_height={md.ScaleHeight}. Using 1.");
        }
        float H = md.ScaleHeight > 0f ? md.ScaleHeight : 1f;
        Info($"Medium:      height_fog ({md.Phase ?? "isotropic"} phase)");
        Verbose($"Medium det:  \u03c3_a={sigmaA}, \u03c3_s={sigmaS}, y0={md.Y0}, H={H}");
        return new HeightFogMedium(sigmaA, sigmaS, md.Y0, H, phase);
    }

    private static IMedium BuildProcedural(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'procedural'");
        if (md.Frequency <= 0f) Warn($"Medium 'procedural' has non-positive frequency={md.Frequency}. Using 1.");
        float freq = md.Frequency > 0f ? md.Frequency : 1f;
        Info($"Medium:      procedural/fBm ({md.Phase ?? "isotropic"} phase)");
        Verbose($"Medium det:  \u03c3_base_a={sigmaA}, \u03c3_base_s={sigmaS}, freq={freq}, octaves={md.Octaves}");
        return new HeterogeneousProceduralMedium(
            sigmaA, sigmaS, freq, md.Octaves, md.Lacunarity, md.Gain, md.Seed, phase);
    }

    private static IMedium? BuildGrid(MediumData md, IPhaseFunction phase, string sceneDir)
    {
        var boundsMin = ToVector3(md.BoundsMin);
        var boundsMax = ToVector3(md.BoundsMax);
        if (boundsMin is null || boundsMax is null)
        {
            Warn("Medium 'grid' requires bounds_min and bounds_max. Ignoring.");
            return null;
        }

        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'grid'");

        float[]? data = null;
        int nx = md.Nx, ny = md.Ny, nz = md.Nz;

        if (!string.IsNullOrWhiteSpace(md.File))
        {
            // Binary .vol format:
            //   magic 'V','O','L','1' | nx int32 | ny int32 | nz int32
            //   boundsMin[3] float32  | boundsMax[3] float32
            //   data: nx*ny*nz float32 values in z-major order.
            try
            {
                string path = Path.IsPathRooted(md.File!)
                    ? md.File!
                    : Path.Combine(sceneDir, md.File!);
                (nx, ny, nz, data) = LoadVolFile(path);
                Info($"Medium:      grid {nx}\u00d7{ny}\u00d7{nz} from '{md.File}'");
            }
            catch (Exception ex)
            {
                Warn($"Medium 'grid' failed to load '{md.File}': {ex.Message}. Ignoring.");
                return null;
            }
        }
        else if (md.Data != null)
        {
            if (nx <= 0 || ny <= 0 || nz <= 0)
            {
                Warn("Medium 'grid' with inline 'data' requires positive nx, ny, nz. Ignoring.");
                return null;
            }
            if (md.Data.Count != nx * ny * nz)
            {
                Warn($"Medium 'grid' inline data length {md.Data.Count} does not match nx*ny*nz = {nx * ny * nz}. Ignoring.");
                return null;
            }
            data = md.Data.ToArray();
            Info($"Medium:      grid {nx}\u00d7{ny}\u00d7{nz} (inline)");
        }
        else
        {
            Warn("Medium 'grid' requires either 'file' or 'data'. Ignoring.");
            return null;
        }

        // GridMedium requires at least 2 voxels per axis for trilinear/tricubic
        // interpolation to be well-defined. Catch this here for a clear message.
        if (nx < 2 || ny < 2 || nz < 2)
        {
            Warn($"Medium 'grid' resolution {nx}×{ny}×{nz} is too small — each axis must be ≥ 2. Ignoring.");
            return null;
        }

        GridInterpolation interp = GridInterpolation.Trilinear;
        switch (md.Interpolation ?? "trilinear")
        {
            case "trilinear":
                interp = GridInterpolation.Trilinear;
                break;
            case "tricubic":
                interp = GridInterpolation.Tricubic;
                break;
            default:
                Warn($"Medium 'grid' unknown interpolation '{md.Interpolation}'. Using 'trilinear'.");
                break;
        }

        try
        {
            var medium = new GridMedium(sigmaA, sigmaS,
                                        boundsMin.Value, boundsMax.Value,
                                        nx, ny, nz, data!, phase, interp);
            Verbose($"Medium det:  interpolation = {interp.ToString().ToLowerInvariant()}");
            return medium;
        }
        catch (Exception ex)
        {
            Warn($"Medium 'grid' construction failed: {ex.Message}. Ignoring.");
            return null;
        }
    }

    private static (int Nx, int Ny, int Nz, float[] Data) LoadVolFile(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        using var br = new BinaryReader(fs);
        byte m0 = br.ReadByte(), m1 = br.ReadByte(), m2 = br.ReadByte(), m3 = br.ReadByte();
        if (m0 != (byte)'V' || m1 != (byte)'O' || m2 != (byte)'L' || m3 != (byte)'1')
            throw new InvalidDataException("Not a VOL1 file.");
        int nx = br.ReadInt32();
        int ny = br.ReadInt32();
        int nz = br.ReadInt32();
        // bounds are present in the file but already provided by YAML — skip 6 floats.
        for (int i = 0; i < 6; i++) br.ReadSingle();
        int count = nx * ny * nz;
        var data = new float[count];
        for (int i = 0; i < count; i++) data[i] = br.ReadSingle();
        return (nx, ny, nz, data);
    }
}
