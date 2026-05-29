using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using TerrainGen.Erosion;
using TerrainGen.Heightmap;
using TerrainGen.Hydrology;
using TerrainGen.Output;
using TerrainGen.Splatting;

namespace TerrainGen;

/// <summary>
/// Entry point. Generates a 3D-Ray heightfield template — a 16-bit grayscale
/// PNG heightmap plus a YAML template that references it as a
/// <c>type: heightfield</c> entity. Pipeline:
///   noise stack → thermal erosion → hydraulic erosion → hydrology (sea +
///   lakes + rivers) → write PNG-16 + YAML (+ optional preview scene).
///
/// No mesh tessellation, no OBJ output — the engine's <c>HeightField</c>
/// primitive intersects the heightmap directly via a min/max mipmap.
/// </summary>
internal static class Program
{
    private const string DefaultRelativeOutputDir = "scenes/assets/heightmaps";

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        string repoRoot = ResolveRepoRoot();
        string defaultOutput = Path.Combine(repoRoot, DefaultRelativeOutputDir);

        var parsed = Cli.Parse(args, defaultOutput);
        if (parsed.ShowHelp) { Cli.PrintHelp(Console.Out); return 0; }
        if (parsed.Error != null) { Console.Error.WriteLine($"error: {parsed.Error}"); return 1; }
        var cfg = parsed.Config!;

        return Run(cfg, repoRoot);
    }

    private static int Run(GenerationConfig cfg, string repoRoot)
    {
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║      TerrainGen — 3D-Ray heightfield gen     ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  name        : {cfg.Name}");
        Console.WriteLine($"  type        : {cfg.Type.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  include     : {DescribeFlags(cfg.Include)}");
        Console.WriteLine($"  season      : {cfg.Season.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  size        : {cfg.Size:0.###} units");
        Console.WriteLine($"  resolution  : {cfg.Resolution}");
        Console.WriteLine($"  seed        : {cfg.Seed}");
        Console.WriteLine($"  erosion     : {(cfg.ErosionEnabled ? "on (" + cfg.ErosionIters + " drops)" : "off")}");
        Console.WriteLine($"  sea level   : {(cfg.SeaLevel < 0 ? "(no sea)" : cfg.SeaLevel.ToString("0.00"))}");
        Console.WriteLine($"  with cameras: {(cfg.WithCameras ? "yes" : "no")}");
        Console.WriteLine($"  output dir  : {cfg.OutputDir}");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();

        // 1. Noise stack -> heightmap
        Console.Write("  [1/5] shaping heightmap... ");
        var hm = TerrainShaper.Build(cfg);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");

        // 2. Thermal erosion (cheap smoothing of impossible slopes)
        long t0 = sw.ElapsedMilliseconds;
        if (cfg.ErosionEnabled)
        {
            Console.Write("  [2/5] thermal erosion...    ");
            ThermalErosion.Apply(hm, iterations: 8);
            hm.Normalize01();
            Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");
        }
        else Console.WriteLine("  [2/5] thermal erosion...    skipped");

        // 3. Hydraulic erosion (drops -> canyons + alluvial fans)
        t0 = sw.ElapsedMilliseconds;
        if (cfg.ErosionEnabled && cfg.ErosionIters > 0)
        {
            Console.Write("  [3/5] hydraulic erosion...  ");
            HydraulicErosion.Apply(hm, cfg.ErosionIters, cfg.Seed);
            hm.Normalize01();
            Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");
        }
        else Console.WriteLine("  [3/5] hydraulic erosion...  skipped");

        // 4. Hydrology: sea + lakes + rivers carve the heightmap in place.
        t0 = sw.ElapsedMilliseconds;
        Console.Write("  [4/5] hydrology...          ");
        var mask = new WaterMask(cfg.Resolution, cfg.SeaLevel);
        if (cfg.HasFlag(WaterFeatures.Mare) || cfg.HasFlag(WaterFeatures.Isole))
            SeaLevel.Apply(hm, mask);
        if (cfg.HasFlag(WaterFeatures.Fiumi))
            RiverCarver.Apply(hm, mask, cfg.Seed);
        if (cfg.HasFlag(WaterFeatures.Laghi))
            LakeFinder.Apply(hm, mask, cfg.Seed);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");

        // 5. Output: PNG-16 heightmap + YAML template + optional preview scene.
        t0 = sw.ElapsedMilliseconds;
        Console.Write("  [5/5] writing files...      ");
        var written = WriteOutputs(cfg, repoRoot, hm, mask);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");

        Console.WriteLine();
        Console.WriteLine($"  Total time: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine();
        Console.WriteLine("Generated files:");
        foreach (var f in written) Console.WriteLine($"  • {f}");
        if (cfg.WithCameras)
        {
            string previewName = $"{cfg.Name}-preview.yaml";
            Console.WriteLine();
            Console.WriteLine($"Render preview:");
            Console.WriteLine($"  dotnet run --project src/RayTracer/RayTracer.csproj -c Release -- \\");
            Console.WriteLine($"    -i scenes/{previewName} -o renders/preview-{cfg.Name}.png \\");
            Console.WriteLine($"    -w 1280 -H 720 -s 256 -d 6 -S 2 -c wide");
        }
        return 0;
    }

    private static List<string> WriteOutputs(GenerationConfig cfg, string repoRoot,
                                             Heightmap2D hm, WaterMask mask)
    {
        var written = new List<string>();
        Directory.CreateDirectory(cfg.OutputDir);

        // The world peaks at size×0.25 in Y — same calibration the old mesh
        // pipeline used, kept so existing preview cameras keep framing.
        float heightScale = cfg.Size * 0.25f;
        bool waterPresent = mask.HasAnyWater || cfg.HasFlag(WaterFeatures.Mare);

        // PNG-16 heightmap — written to the output dir. The engine resolves
        // <c>heightmap_path</c> relative to the *master* scene being loaded,
        // not the imported template; we therefore embed the same scenes/-root
        // relative directory the legacy mesh pipeline used so a preview
        // scene living under scenes/ keeps finding it.
        string pngFileName = $"{cfg.Name}-height.png";
        string pngPath = Path.Combine(cfg.OutputDir, pngFileName);
        PngHeightmapWriter.Write(pngPath, hm);
        written.Add(RelToRepo(repoRoot, pngPath));

        string scenesRelDir = ComputeRelDirFromScenesRoot(repoRoot, cfg.OutputDir);
        string heightmapEmittedPath = string.IsNullOrEmpty(scenesRelDir)
            ? pngFileName
            : $"{scenesRelDir}/{pngFileName}";

        // Template YAML.
        string templateName = SanitiseId(cfg.Name);
        string materialPrefix = "terrain_" + templateName;
        var bands = StratumClassifier.Build(cfg);
        string yamlPath = Path.Combine(cfg.OutputDir, $"{cfg.Name}.yaml");
        string yamlText = YamlEmitter.Write(
            cfg, bands, waterPresent, heightScale,
            templateName: templateName,
            materialPrefix: materialPrefix,
            heightmapFileName: heightmapEmittedPath);
        File.WriteAllText(yamlPath, yamlText);
        written.Add(RelToRepo(repoRoot, yamlPath));

        if (cfg.WithCameras)
        {
            var (_, hMaxNorm, hAvgNorm) = hm.Stats();
            string previewPath = Path.Combine(repoRoot, "scenes", $"{cfg.Name}-preview.yaml");
            string previewText = PreviewSceneEmitter.Write(
                cfg, templateName,
                hMax: hMaxNorm * heightScale,
                hAvg: hAvgNorm * heightScale);
            File.WriteAllText(previewPath, previewText);
            written.Add(RelToRepo(repoRoot, previewPath));
        }

        return written;
    }

    /// <summary>
    /// Walks up from the running binary until we find the directory that
    /// contains a <c>scenes/</c> folder (the repo root). Throws otherwise.
    /// Same convention as <c>src/Tools/ChessGen/Program.cs:ResolveOutputPath</c>.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "scenes")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException(
                "Could not locate repo root — no 'scenes/' directory found above the binary.");
        return dir.FullName;
    }

    private static string RelToRepo(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Computes the heightmap PNG's directory relative to <c>scenes/</c> so the
    /// emitted <c>heightmap_path</c> resolves correctly when the template is
    /// imported by a master scene that lives in <c>scenes/</c>. Returns an
    /// empty string when the output directory is outside the scenes/ tree —
    /// the caller falls back to the bare filename in that case (and the user
    /// is expected to author paths manually).
    /// </summary>
    private static string ComputeRelDirFromScenesRoot(string repoRoot, string absoluteOutputDir)
    {
        string scenesRoot = Path.GetFullPath(Path.Combine(repoRoot, "scenes"));
        string outFull = Path.GetFullPath(absoluteOutputDir);
        if (!outFull.StartsWith(scenesRoot, System.StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"warning: output dir '{absoluteOutputDir}' is outside the 'scenes/' tree;");
            Console.Error.WriteLine("         heightmap_path in the YAML will be just the file basename.");
            return string.Empty;
        }
        string rel = Path.GetRelativePath(scenesRoot, outFull).Replace(Path.DirectorySeparatorChar, '/');
        return rel == "." ? string.Empty : rel;
    }

    private static string DescribeFlags(WaterFeatures f)
    {
        if (f == WaterFeatures.None) return "(none)";
        var parts = new List<string>();
        if ((f & WaterFeatures.Fiumi) != 0) parts.Add("fiumi");
        if ((f & WaterFeatures.Laghi) != 0) parts.Add("laghi");
        if ((f & WaterFeatures.Mare)  != 0) parts.Add("mare");
        if ((f & WaterFeatures.Isole) != 0) parts.Add("isole");
        return string.Join(",", parts);
    }

    /// <summary>Convert a CLI name into a YAML-friendly template id.</summary>
    private static string SanitiseId(string name)
    {
        var chars = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            chars[i] = (char.IsLetterOrDigit(c) || c == '_') ? c : '_';
        }
        return new string(chars);
    }
}
