using System.Diagnostics;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using RayTracer.Core.Sampling;
using RayTracer.Rendering;
using RayTracer.Scene;
using RayTracer.Volumetrics;

namespace RayTracer;

class Program
{
    static void Main(string[] args)
    {
        // Show help if no arguments or help requested
        if (args.Length == 0 || HasFlag(args, "--help", "-h"))
        {
            ShowHelp();
            return;
        }

        // Parse CLI arguments
        string? inputPath = GetArg(args, "--input", "-i");
        string? outputArg = GetArg(args, "--output", "-o");
        string outputPath;

        if (outputArg != null)
        {
            outputPath = outputArg;
        }
        else if (!string.IsNullOrEmpty(inputPath))
        {
            // Default to "renders/render-<scene>.png"
            string sceneName = Path.GetFileNameWithoutExtension(inputPath);
            outputPath = Path.Combine("renders", $"render-{sceneName}.png");
        }
        else
        {
            outputPath = Path.Combine("renders", "render.png");
        }

        bool wParsed = int.TryParse(GetArg(args, "--width",   "-w"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var width);
        bool hParsed = int.TryParse(GetArg(args, "--height",  "-H"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var height);
        bool sParsed = int.TryParse(GetArg(args, "--samples", "-s"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var samples);
        bool dParsed = int.TryParse(GetArg(args, "--depth",   "-d"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var depth);

        // Shadow samples CLI override (null = use per-light YAML values)
        int? shadowSamplesOverride = null;
        if (int.TryParse(GetArg(args, "--shadow-samples", "-S"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ssOverride) && ssOverride > 0)
            shadowSamplesOverride = ssOverride;

        // Firefly clamp CLI override (null = use Renderer.DefaultMaxSampleRadiance)
        float? clampOverride = null;
        if (float.TryParse(GetArg(args, "--clamp", "-C"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cOverride) && cOverride > 0f)
            clampOverride = cOverride;

        // Camera selector: name or zero-based index
        string? cameraSelector = GetArg(args, "--camera", "-c");

        // Sampler kind: defaults to Sobol (Burley 2020 hash-based Owen
        // scrambling) for the 2-5× per-spp convergence improvement; pass
        // `--sampler prng` to fall back to the legacy thread-local PRNG
        // for A/B comparisons or to reproduce pre-Sobol golden images.
        SamplerKind samplerKind = SamplerKind.Sobol;
        string? samplerArg = GetArg(args, "--sampler", null);
        if (samplerArg != null)
        {
            switch (samplerArg.ToLowerInvariant())
            {
                case "prng":
                case "random":
                    samplerKind = SamplerKind.Prng; break;
                case "sobol":
                case "owen":
                    samplerKind = SamplerKind.Sobol; break;
                default:
                    Console.WriteLine($"Error: Unknown --sampler '{samplerArg}'. Valid: prng, sobol.");
                    return;
            }
        }
        Sampler.SetKind(samplerKind);

        // MIS combination heuristic (balance / power). See Veach 1997 §9.2.
        MisHeuristic misHeuristic = MisHeuristic.Balance;
        string? misArg = GetArg(args, "--mis", null);
        if (misArg != null)
        {
            switch (misArg.ToLowerInvariant())
            {
                case "balance": misHeuristic = MisHeuristic.Balance; break;
                case "power":   misHeuristic = MisHeuristic.Power;   break;
                default:
                    Console.WriteLine($"Error: Unknown --mis '{misArg}'. Valid: balance, power.");
                    return;
            }
        }

        // Light selection strategy. See LightSamplingStrategy.
        LightSamplingStrategy lightSampling = LightSamplingStrategy.All;
        string? lightSamplingArg = GetArg(args, "--light-sampling", null);
        if (lightSamplingArg != null)
        {
            switch (lightSamplingArg.ToLowerInvariant())
            {
                case "all":     lightSampling = LightSamplingStrategy.All;     break;
                case "power":   lightSampling = LightSamplingStrategy.Power;   break;
                case "uniform": lightSampling = LightSamplingStrategy.Uniform; break;
                default:
                    Console.WriteLine($"Error: Unknown --light-sampling '{lightSamplingArg}'. Valid: all, power, uniform.");
                    return;
            }
        }

        // Indirect bounce clamp factor (Cycles/Arnold style depth-aware suppression).
        // Default 1.0 = no extra suppression (backward compat).
        float indirectClampFactor = Renderer.DefaultIndirectClampFactor;
        if (float.TryParse(GetArg(args, "--indirect-clamp-factor", null),
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture,
                           out var icf) && icf >= 0f)
            indirectClampFactor = icf;

        // Texture filtering — ray differentials drive analytic anti-aliasing
        // in filtered textures (Perlin/fBm octave clamp, Worley supersampling,
        // ImageTexture mipmap). Default 'auto' = on; 'off' disables differential
        // emission entirely for benchmark comparison against the point-sampled
        // baseline; 'on' is identical to auto and reserved for future heuristics.
        Renderer.TextureFilteringMode textureFiltering = Renderer.TextureFilteringMode.Auto;
        string? textureFilteringArg = GetArg(args, "--texture-filtering", null);
        if (textureFilteringArg != null)
        {
            switch (textureFilteringArg.ToLowerInvariant())
            {
                case "auto": textureFiltering = Renderer.TextureFilteringMode.Auto; break;
                case "on":   textureFiltering = Renderer.TextureFilteringMode.On;   break;
                case "off":  textureFiltering = Renderer.TextureFilteringMode.Off;  break;
                default:
                    Console.WriteLine($"Error: Unknown --texture-filtering '{textureFilteringArg}'. Valid: auto, on, off.");
                    return;
            }
        }

        // Verbose mode
        bool verbose = HasFlag(args, "--verbose", "-v");
        SceneLoader.SetVerbose(verbose);

        // Required argument check
        if (string.IsNullOrEmpty(inputPath))
        {
            Console.WriteLine("Error: Missing required argument --input (-i)");
            Console.WriteLine();
            ShowHelp();
            return;
        }

        // File existence check
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: Scene file not found: {inputPath}");
            return;
        }

        // --list-cameras: print available cameras and exit
        if (HasFlag(args, "--list-cameras", null))
        {
            SceneLoader.TryListCameras(inputPath);
            return;
        }

        // Default values and validation
        if (!wParsed || width  <= 0) width   = 1200;
        if (!hParsed || height <= 0) height  = 800;
        if (!sParsed || samples <= 0) samples = 16;
        if (!dParsed || depth  <= 0) depth   = 8;

        Console.WriteLine("╔═══════════════════════════════════════════╗");
        Console.WriteLine("║         RayTracer .NET 10 Engine          ║");
        Console.WriteLine("╚═══════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Scene:       {inputPath}");
        Console.WriteLine($"  Output:      {outputPath}");
        Console.WriteLine($"  Resolution:  {width} \u00d7 {height}");
        Console.WriteLine($"  Samples/px:  {samples}");
        Console.WriteLine($"  Max depth:   {depth}");
        if (shadowSamplesOverride.HasValue)
            Console.WriteLine($"  Shadow smp:  {shadowSamplesOverride.Value} (override)");
        if (clampOverride.HasValue)
            Console.WriteLine($"  Clamp:       {clampOverride.Value} (override)");
        if (indirectClampFactor != Renderer.DefaultIndirectClampFactor)
        {
            float baseClamp = clampOverride ?? Renderer.DefaultMaxSampleRadiance;
            Console.WriteLine($"  Indir.clamp: ×{indirectClampFactor:F2} ({baseClamp * indirectClampFactor:F1} effective)");
        }
        if (cameraSelector != null)
            Console.WriteLine($"  Camera:      {cameraSelector}");
        Console.WriteLine($"  Sampler:     {samplerKind.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  MIS:         {misHeuristic.ToString().ToLowerInvariant()} heuristic");
        if (lightSampling != LightSamplingStrategy.All)
            Console.WriteLine($"  Light pick:  {lightSampling.ToString().ToLowerInvariant()}");
        Console.WriteLine();

        // Load scene
        Console.Write("  Loading scene... ");
        var sw = Stopwatch.StartNew();
        try
        {
            var (world, camera, lights, sky, globalMedium) =
                SceneLoader.Load(inputPath, width, height, shadowSamplesOverride, cameraSelector);

            Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");
            SceneLoader.FlushMessages();
            Console.WriteLine($"  Lights:      {lights.Count}");
            string skyDesc = sky.Mode switch
            {
                SkySettings.SkyMode.Hdri     => "HDRI environment map",
                SkySettings.SkyMode.Gradient => "gradient" + (sky.HasSun ? " + sun disk" : ""),
                _                            => "flat"
            };
            Console.WriteLine($"  Sky:         {skyDesc}");

            // Render (constructor may print scene analysis info before the blank line)
            var renderer = new Renderer(world, camera, lights, sky, samples, depth, globalMedium, clampOverride, verbose, misHeuristic, lightSampling, indirectClampFactor, textureFiltering);
            Console.WriteLine();

            sw.Restart();
            var pixels = renderer.Render(width, height);
            var elapsed = sw.Elapsed;
            Console.WriteLine($"  Render time: {FormatElapsed(elapsed)}");

            // Save image
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            SaveImage(pixels, width, height, outputPath);
            Console.WriteLine();
            Console.WriteLine($"  \u2713 Saved: {Path.GetFullPath(outputPath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("failed!");
            Console.WriteLine($"Error loading or rendering scene: {ex.Message}");
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("RayTracer .NET 10 Engine");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/RayTracer/RayTracer.csproj -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>           Scene YAML file (required)");
        Console.WriteLine("  -o, --output <path>          Output image (default: renders/render-<scene>.png)");
        Console.WriteLine("  -w, --width <px>             Image width  (default: 1200)");
        Console.WriteLine("  -H, --height <px>            Image height (default: 800)");
        Console.WriteLine("  -s, --samples <n>            Samples per pixel (default: 16, see rendering profiles)");
        Console.WriteLine("  -d, --depth <n>              Max ray depth (default: 8, raise to 16+ for stacked glass)");
        Console.WriteLine("  -S, --shadow-samples <n>     Area light shadow samples override (default 4; perfect squares work best)");
        Console.WriteLine("  -C, --clamp <n>              Max per-sample radiance / firefly clamp (default: 100)");
        Console.WriteLine("      --indirect-clamp-factor  Clamp factor for indirect bounces (default: 1.0 = off; try 0.25)");
        Console.WriteLine("  -c, --camera <name|index>    Select camera by name or 0-based index");
        Console.WriteLine("      --sampler <prng|sobol>   Per-pixel sampler (default: sobol — Burley 2020)");
        Console.WriteLine("      --mis <balance|power>    MIS combination heuristic (default: balance)");
        Console.WriteLine("      --light-sampling <all|power|uniform>  NEE light strategy (default: all)");
        Console.WriteLine("      --texture-filtering <auto|on|off>     Analytic anti-aliasing via ray differentials (default: auto)");
        Console.WriteLine("      --list-cameras           List all cameras in the scene and exit");
        Console.WriteLine("  -v, --verbose                Show detailed loading and scene analysis info");
        Console.WriteLine("  -h, --help                   Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  from the root of the project: ");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml --list-cameras");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml -c top -o top.png");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml -c 2 -o cam2.png");
        Console.WriteLine("  from the bin/Debug/net10.0 folder: ");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml --list-cameras");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml -c top -o top.png");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml -c 2 -o cam2.png");
    }

    /// <summary>
    /// Formats a TimeSpan as a human-readable string:
    /// under 60s  → "42.18s"
    /// under 60m  → "5m 42.18s"
    /// 60m+       → "1h 05m 42s"
    /// </summary>
    static string FormatElapsed(TimeSpan t)
    {
        if (t.TotalMinutes < 1)
            return $"{t.TotalSeconds:F2}s";
        if (t.TotalHours < 1)
            return $"{(int)t.TotalMinutes}m {t.Seconds + t.Milliseconds / 1000.0:F2}s";
        return $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds}s";
    }

    static void SaveImage(Vector3[,] pixels, int width, int height, string path)
    {
        using var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y, x];
                // BUG-04 fix: cast via int with clamp avoids the silent byte-overflow
                // edge case that 255.99f could theoretically cause with Inf/NaN inputs.
                byte r = (byte)Math.Clamp((int)(Math.Clamp(c.X, 0f, 1f) * 256f), 0, 255);
                byte g = (byte)Math.Clamp((int)(Math.Clamp(c.Y, 0f, 1f) * 256f), 0, 255);
                byte b = (byte)Math.Clamp((int)(Math.Clamp(c.Z, 0f, 1f) * 256f), 0, 255);
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        // Determine format from extension
        string ext = Path.GetExtension(path).ToLowerInvariant();
        using var stream = File.Create(path);
        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                image.SaveAsJpeg(stream);
                break;
            case ".bmp":
                image.SaveAsBmp(stream);
                break;
            default:
                image.SaveAsPng(stream);
                break;
        }
    }

    /// <summary>
    /// Returns the value of a CLI argument (the token following the flag).
    /// </summary>
    static string? GetArg(string[] args, string longName, string? shortName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == longName || (shortName != null && args[i] == shortName))
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Returns true if a boolean flag is present (no value expected).
    /// </summary>
    static bool HasFlag(string[] args, string longName, string? shortName)
    {
        foreach (var arg in args)
        {
            if (arg == longName || (shortName != null && arg == shortName))
                return true;
        }
        return false;
    }
}
