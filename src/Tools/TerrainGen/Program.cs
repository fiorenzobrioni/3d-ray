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
/// Entry point. Generates a 3D-Ray terrain template (YAML + per-stratum OBJ
/// meshes) following the pipeline:
///   noise stack -> shape -> thermal erosion -> hydraulic erosion ->
///   sea + lakes + rivers -> style post -> classify + tessellate ->
///   write OBJs + YAML (and optional preview scene).
/// </summary>
internal static class Program
{
    private const string DefaultRelativeOutputDir = "scenes/libraries/terrain";

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
        Console.WriteLine("║      TerrainGen — 3D-Ray terrain builder     ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  name        : {cfg.Name}");
        Console.WriteLine($"  type        : {cfg.Type.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  include     : {DescribeFlags(cfg.Include)}");
        Console.WriteLine($"  season      : {cfg.Season.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  size        : {cfg.Size:0.###} units");
        Console.WriteLine($"  resolution  : {cfg.Resolution}");
        Console.WriteLine($"  seed        : {cfg.Seed}");
        Console.WriteLine($"  style       : {cfg.Style.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  erosion     : {(cfg.ErosionEnabled ? "on (" + cfg.ErosionIters + " drops)" : "off")}");
        Console.WriteLine($"  sea level   : {(cfg.SeaLevel < 0 ? "(no sea)" : cfg.SeaLevel.ToString("0.00"))}");
        Console.WriteLine($"  with cameras: {(cfg.WithCameras ? "yes" : "no")}");
        Console.WriteLine($"  output dir  : {cfg.OutputDir}");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();

        // 1. Noise stack -> heightmap
        Console.Write("  [1/6] shaping heightmap... ");
        var hm = TerrainShaper.Build(cfg);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");

        // 2. Thermal erosion (cheap smoothing of impossible slopes)
        long t0 = sw.ElapsedMilliseconds;
        if (cfg.ErosionEnabled)
        {
            Console.Write("  [2/6] thermal erosion...    ");
            ThermalErosion.Apply(hm, iterations: 8);
            hm.Normalize01();
            Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");
        }
        else Console.WriteLine("  [2/6] thermal erosion...    skipped");

        // 3. Hydraulic erosion (drops -> canyons + alluvial fans)
        t0 = sw.ElapsedMilliseconds;
        if (cfg.ErosionEnabled && cfg.ErosionIters > 0)
        {
            Console.Write("  [3/6] hydraulic erosion...  ");
            HydraulicErosion.Apply(hm, cfg.ErosionIters, cfg.Seed);
            hm.Normalize01();
            Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");
        }
        else Console.WriteLine("  [3/6] hydraulic erosion...  skipped");

        // 4. Hydrology: sea + lakes + rivers
        t0 = sw.ElapsedMilliseconds;
        Console.Write("  [4/6] hydrology...          ");
        var mask = new WaterMask(cfg.Resolution, cfg.SeaLevel);
        if (cfg.HasFlag(WaterFeatures.Mare) || cfg.HasFlag(WaterFeatures.Isole))
            SeaLevel.Apply(hm, mask);
        if (cfg.HasFlag(WaterFeatures.Fiumi))
            RiverCarver.Apply(hm, mask, cfg.Seed);
        if (cfg.HasFlag(WaterFeatures.Laghi))
            LakeFinder.Apply(hm, mask, cfg.Seed);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");

        // 5. Style post (passthrough / quantize / decimate) + tessellation
        t0 = sw.ElapsedMilliseconds;
        Console.Write("  [5/6] tessellation...       ");
        var (postMap, flatShade) = StylePostprocessor.Apply(hm, cfg.Style);
        // If style decimated, the WaterMask has the OLD resolution. Resample it.
        var postMask = postMap.N == mask.N ? mask : ResampleMask(mask, postMap.N);
        float heightScale = cfg.Size * 0.25f; // map units 0..1 -> 0..size/4 (peaks ~size/4 tall)
        var bundle = MeshBuilder.Build(cfg, postMap, postMask, flatShade, heightScale);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds - t0} ms)");

        // 6. Output
        t0 = sw.ElapsedMilliseconds;
        Console.Write("  [6/6] writing files...      ");
        var written = WriteOutputs(cfg, repoRoot, bundle);
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

    private static List<string> WriteOutputs(GenerationConfig cfg, string repoRoot, TerrainMeshBundle bundle)
    {
        var written = new List<string>();
        Directory.CreateDirectory(cfg.OutputDir);

        // Mesh OBJs go into the output dir.
        foreach (var (stratum, mesh) in bundle.Land)
        {
            if (mesh.Faces.Count == 0) continue;
            string objName = $"{cfg.Name}-{stratum.Suffix()}.obj";
            string objPath = Path.Combine(cfg.OutputDir, objName);
            ObjWriter.Write(objPath, mesh, $"3D-Ray TerrainGen — {stratum} stratum, {mesh.Faces.Count} faces");
            written.Add(RelToRepo(repoRoot, objPath));
        }
        if (bundle.WaterSurface != null && bundle.WaterSurface.Faces.Count > 0)
        {
            string objName = $"{cfg.Name}-water.obj";
            string objPath = Path.Combine(cfg.OutputDir, objName);
            ObjWriter.Write(objPath, bundle.WaterSurface, $"3D-Ray TerrainGen — water surface, {bundle.WaterSurface.Faces.Count} faces");
            written.Add(RelToRepo(repoRoot, objPath));
        }

        // Path of OBJ files relative to scenes/ (= the engine's mesh-resolution root).
        // Compute it from the actual output dir so a custom --output still works
        // as long as it lives under scenes/.
        string objRelDirFromScenes = ComputeRelDirFromScenesRoot(repoRoot, cfg.OutputDir);

        string templateName = SanitiseId(cfg.Name);
        string materialPrefix = "terrain_" + templateName;

        // Template YAML.
        string yamlPath = Path.Combine(cfg.OutputDir, $"{cfg.Name}.yaml");
        string yamlText = YamlEmitter.Write(cfg, bundle,
            writtenObjs: written,
            templateName: templateName,
            materialPrefix: materialPrefix,
            objRelativeDirFromScenesRoot: objRelDirFromScenes);
        File.WriteAllText(yamlPath, yamlText);
        written.Add(RelToRepo(repoRoot, yamlPath));

        if (cfg.WithCameras)
        {
            string previewPath = Path.Combine(repoRoot, "scenes", $"{cfg.Name}-preview.yaml");
            string previewText = PreviewSceneEmitter.Write(cfg, bundle, templateName);
            File.WriteAllText(previewPath, previewText);
            written.Add(RelToRepo(repoRoot, previewPath));
        }

        return written;
    }

    private static WaterMask ResampleMask(WaterMask src, int dstN)
    {
        var dst = new WaterMask(dstN, src.SeaLevel);
        float scale = (float)(src.N - 1) / (dstN - 1);
        for (int z = 0; z < dstN; z++)
        for (int x = 0; x < dstN; x++)
        {
            int sx = (int)System.MathF.Round(x * scale);
            int sz = (int)System.MathF.Round(z * scale);
            sx = System.Math.Min(sx, src.N - 1);
            sz = System.Math.Min(sz, src.N - 1);
            int si = sz * src.N + sx;
            if (src.Kind[si] != WaterKind.None)
                dst.MarkWet(x, z, src.Kind[si], src.WaterLevel[si]);
        }
        return dst;
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

    private static string ComputeRelDirFromScenesRoot(string repoRoot, string absoluteOutputDir)
    {
        string scenesRoot = Path.GetFullPath(Path.Combine(repoRoot, "scenes"));
        string outFull = Path.GetFullPath(absoluteOutputDir);
        if (!outFull.StartsWith(scenesRoot, System.StringComparison.Ordinal))
        {
            // Outside scenes/: the user must add their own --output convention.
            // Emit the basename only and warn.
            Console.Error.WriteLine($"warning: output dir '{absoluteOutputDir}' is outside the 'scenes/' tree;");
            Console.Error.WriteLine("         mesh paths in the YAML will be just the file basename.");
            return string.Empty;
        }
        string rel = Path.GetRelativePath(scenesRoot, outFull).Replace(Path.DirectorySeparatorChar, '/');
        return rel == "." ? string.Empty : rel;
    }

    private static string RelToRepo(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
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
