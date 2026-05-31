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

        // Quality preset (industry-style draft/medium/final × small/full + ultra 4K).
        // Resolved before the per-flag parsing so individual flags (`-s`, `-d`,
        // `-w`, `-H`, `-S`) can override any of the preset's values.
        string? qualityArg = GetArg(args, "--quality", "-q");
        QualityPreset? quality = null;
        if (qualityArg != null)
        {
            quality = QualityPreset.Parse(qualityArg);
            if (quality == null)
            {
                Console.WriteLine($"Error: Unknown --quality '{qualityArg}'. Valid: {QualityPreset.NamesCsv}.");
                return;
            }
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

        // Photographic exposure compensation, in stops (EV). Default 0 EV =
        // identity. Negative EV darkens (good for over-bright lighting setups
        // that ACES would otherwise squash into a near-saturated plateau);
        // positive EV brightens. The linear gain `2^EV` is applied to each
        // pixel BEFORE the ACES tonemap so the artist slides the scene into
        // the curve's linear sweet-spot. Mirrors Arnold `exposure`,
        // Cycles "Film → Exposure", RenderMan display-filter `exposure`.
        float exposureEv = Renderer.DefaultExposureEv;
        if (float.TryParse(GetArg(args, "--exposure", null),
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture,
                           out var ev))
            exposureEv = ev;

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

        // SSS dispatch toggle. `auto` (default) routes refractions into scattering
        // interior_medium bindings through the random-walk integrator; `off`
        // declasses pushed media to absorption-only so the legacy Beer-Lambert
        // path handles them (preview / A/B comparison knob — see Renderer.SssMode).
        SssMode sssMode = SssMode.Auto;
        string? sssModeArg = GetArg(args, "--sss-mode", null);
        if (sssModeArg != null)
        {
            switch (sssModeArg.ToLowerInvariant())
            {
                case "auto": sssMode = SssMode.Auto; break;
                case "off":  sssMode = SssMode.Off;  break;
                default:
                    Console.WriteLine($"Error: Unknown --sss-mode '{sssModeArg}'. Valid: auto, off.");
                    return;
            }
        }

        // Random-walk quality preset (Preview/Normal/High). Default is `normal`
        // unless a top-level `--quality` preset implies a different SSS tier
        // (draft* → preview, final*/ultra → high). Explicit `--sss-quality`
        // always wins. Each preset configures MaxVolumeBounces, RrStartBounce
        // and NeeInsideWalk in lockstep — see RandomWalkConfig.
        RandomWalkConfig? walkConfig = null;
        string? sssQualityArg = GetArg(args, "--sss-quality", null);
        string? sssQualityLabel = null;
        if (sssQualityArg != null)
        {
            switch (sssQualityArg.ToLowerInvariant())
            {
                case "preview": walkConfig = RandomWalkConfig.Preview; sssQualityLabel = "preview"; break;
                case "normal":  walkConfig = RandomWalkConfig.Normal;  sssQualityLabel = "normal";  break;
                case "high":    walkConfig = RandomWalkConfig.High;    sssQualityLabel = "high";    break;
                default:
                    Console.WriteLine($"Error: Unknown --sss-quality '{sssQualityArg}'. Valid: preview, normal, high.");
                    return;
            }
        }
        else if (quality != null)
        {
            walkConfig       = quality.WalkConfig;
            sssQualityLabel  = quality.SssQualityName;
        }

        // Volume-bounce hard cap (defaults inherited from the resolved SSS quality
        // preset; this flag is the per-render escape hatch over the preset value).
        int? maxVolumeBouncesOverride = null;
        if (int.TryParse(GetArg(args, "--max-volume-bounces", null),
                         System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture,
                         out var mvb) && mvb > 0)
            maxVolumeBouncesOverride = mvb;
        if (maxVolumeBouncesOverride.HasValue)
        {
            var baseCfg = walkConfig ?? RandomWalkConfig.Normal;
            walkConfig = new RandomWalkConfig(
                maxVolumeBounces: maxVolumeBouncesOverride.Value,
                rrStartBounce:    baseCfg.RrStartBounce,
                neeInsideWalk:    baseCfg.NeeInsideWalk,
                neeMaxBounce:     baseCfg.NeeMaxBounce);
        }

        // Verbose mode
        // ── Caustics (Manifold NEE Phase 2 + Specular Manifold Sampling 2b) ──
        // Opt-in: entities must additionally be flagged caustic_caster /
        // caustic_receiver in YAML. `--caustics on` activates the manifold walk;
        // an explicit flag overrides the quality-preset default (FINAL/ULTRA turn
        // caustics on — harmless on scenes without flagged entities). When unset,
        // caustics stay off so every scene is bit-identical to pre-caustics output.
        bool? causticsExplicit = null;
        string? causticsArg = GetArg(args, "--caustics", null);
        if (causticsArg != null)
        {
            switch (causticsArg.ToLowerInvariant())
            {
                case "on":  causticsExplicit = true;  break;
                case "off": causticsExplicit = false; break;
                default:
                    Console.WriteLine($"Error: Unknown --caustics '{causticsArg}'. Valid: on, off.");
                    return;
            }
        }
        bool enableCaustics = causticsExplicit ?? (quality?.Caustics ?? false);

        // SMS (Phase-2b) trials per rough caster connection. Preset default
        // (FINAL/ULTRA → 8) unless explicitly overridden with --sms-samples.
        int smsSamples = quality?.SmsSamples ?? 4;
        if (int.TryParse(GetArg(args, "--sms-samples", null), out var smsArg) && smsArg > 0)
            smsSamples = smsArg;

        // MNEE emitter samples per receiver per light. Default 1 (smooth area/
        // sphere casters are low-variance); raise for the finite virtual bulb of
        // point/spot lights, whose smaller emitter is noisier.
        int mneeSamples = 1;
        if (int.TryParse(GetArg(args, "--mnee-samples", null), out var mneeArg) && mneeArg > 0)
            mneeSamples = mneeArg;

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

        // File existence check — accept the path as-is, or append .yaml/.yml
        // when the user omitted the extension (e.g. `-i scenes/chess`).
        if (!File.Exists(inputPath))
        {
            if (File.Exists(inputPath + ".yaml"))     inputPath += ".yaml";
            else if (File.Exists(inputPath + ".yml")) inputPath += ".yml";
            else
            {
                Console.WriteLine($"Error: Scene file not found: {inputPath}");
                return;
            }
        }

        // --list-cameras: print available cameras and exit
        if (HasFlag(args, "--list-cameras", null))
        {
            SceneLoader.TryListCameras(inputPath);
            return;
        }

        // Default values and validation. A quality preset (if any) supplies the
        // baseline for every value the user did NOT override with an explicit flag.
        if (!wParsed || width  <= 0) width   = quality?.Width   ?? 1200;
        if (!hParsed || height <= 0) height  = quality?.Height  ?? 800;
        if (!sParsed || samples <= 0) samples = quality?.Samples ?? 16;
        if (!dParsed || depth  <= 0) depth   = quality?.Depth   ?? 8;
        if (!shadowSamplesOverride.HasValue && quality != null)
            shadowSamplesOverride = quality.ShadowSamples;

        Console.WriteLine("╔═══════════════════════════════════════════╗");
        Console.WriteLine("║          3D-Ray RayTracer Engine          ║");
        Console.WriteLine("╚═══════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Scene:       {inputPath}");
        Console.WriteLine($"  Output:      {outputPath}");
        if (quality != null)
            Console.WriteLine($"  Quality:     {quality.Name}");
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
        if (exposureEv != Renderer.DefaultExposureEv)
            Console.WriteLine($"  Exposure:    {exposureEv:+0.0;-0.0;0.0} EV (×{MathF.Pow(2f, exposureEv):F2})");
        if (cameraSelector != null)
            Console.WriteLine($"  Camera:      {cameraSelector}");
        Console.WriteLine($"  Sampler:     {samplerKind.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  MIS:         {misHeuristic.ToString().ToLowerInvariant()} heuristic");
        if (lightSampling != LightSamplingStrategy.All)
            Console.WriteLine($"  Light pick:  {lightSampling.ToString().ToLowerInvariant()}");
        if (sssMode != SssMode.Auto)
            Console.WriteLine($"  SSS mode:    {sssMode.ToString().ToLowerInvariant()}");
        if (sssQualityLabel != null || maxVolumeBouncesOverride.HasValue)
        {
            var effective = walkConfig ?? RandomWalkConfig.Normal;
            string label = sssQualityLabel ?? "normal";
            Console.WriteLine(
                $"  SSS quality: {label} (vol-bounces={effective.MaxVolumeBounces}, " +
                $"rr-start={effective.RrStartBounce}, nee-in-walk={(effective.NeeInsideWalk ? "on" : "off")})");
        }
        Console.WriteLine();

        // Load scene
        Console.Write("  Loading scene... ");
        var sw = Stopwatch.StartNew();
        try
        {
            var (world, camera, lights, sky, globalMedium) =
                SceneLoader.Load(inputPath, width, height, shadowSamplesOverride, cameraSelector,
                                 enableCaustics);
            var causticCasters = SceneLoader.LastCausticCasters;

            Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");
            SceneLoader.FlushMessages();
            Console.WriteLine($"  Lights:      {lights.Count}");
            string skyDesc = sky.Mode switch
            {
                SkySettings.SkyMode.Hdri     => "HDRI environment map" + (sky.HasSun ? " + extracted sun" : ""),
                SkySettings.SkyMode.Gradient => "gradient" + (sky.HasSun ? " + sun disk" : ""),
                SkySettings.SkyMode.Physical => sky.LightingModel switch
                {
                    RayTracer.Rendering.Sky.NishitaSky  => "physical (nishita)" + (sky.HasSun ? " + sun disk" : ""),
                    RayTracer.Rendering.Sky.PreethamSky => "physical (preetham)" + (sky.HasSun ? " + sun disk" : ""),
                    _                                   => "physical" + (sky.HasSun ? " + sun disk" : "")
                },
                _                            => "flat"
            };
            Console.WriteLine($"  Sky:         {skyDesc}");

            // Render (constructor may print scene analysis info before the blank line)
            var renderer = new Renderer(
                world, camera, lights, sky, samples, depth, globalMedium,
                clampOverride, verbose, misHeuristic, lightSampling,
                indirectClampFactor, textureFiltering, exposureEv,
                sssMode, walkConfig,
                enableCaustics: enableCaustics, causticCasters: causticCasters,
                mneeSamples: mneeSamples, smsSamples: smsSamples);
            if (enableCaustics)
                Console.WriteLine($"  Caustics:    MNEE + SMS on ({causticCasters.Count} caster"
                                  + (causticCasters.Count == 1 ? "" : "s")
                                  + $", mnee-samples {mneeSamples}, sms-samples {smsSamples})");
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
        Console.WriteLine("3D-Ray RayTracer Engine");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/RayTracer/RayTracer.csproj -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>           Scene YAML file (required; .yaml/.yml extension is optional)");
        Console.WriteLine("  -o, --output <path>          Output image (default: renders/render-<scene>.png)");
        Console.WriteLine("  -q, --quality <preset>       Render-quality preset that fills -w/-H/-s/-d/-S in one shot.");
        Console.WriteLine("                                Any explicit flag below wins over the preset's value.");
        Console.WriteLine("                                Presets: draft-tiny, draft-small, draft,");
        Console.WriteLine("                                          medium-tiny, medium-small, medium,");
        Console.WriteLine("                                          final-tiny, final-small, final, ultra (4K).");
        Console.WriteLine("  -w, --width <px>             Image width  (default: 1200)");
        Console.WriteLine("  -H, --height <px>            Image height (default: 800)");
        Console.WriteLine("  -s, --samples <n>            Samples per pixel (default: 16, see rendering profiles)");
        Console.WriteLine("  -d, --depth <n>              Max ray depth (default: 8, raise to 16+ for stacked glass)");
        Console.WriteLine("  -S, --shadow-samples <n>     Area light shadow samples override (default 4; perfect squares work best)");
        Console.WriteLine("  -C, --clamp <n>              Max per-sample radiance / firefly clamp (default: 100)");
        Console.WriteLine("      --indirect-clamp-factor  Clamp factor for indirect bounces (default: 1.0 = off; try 0.25)");
        Console.WriteLine("      --exposure <EV>          Photographic exposure compensation in stops applied pre-ACES");
        Console.WriteLine("                                (default: 0 = identity; -1 darkens 2×, +1 brightens 2×)");
        Console.WriteLine("  -c, --camera <name|index>    Select camera by name or 0-based index");
        Console.WriteLine("      --sampler <prng|sobol>   Per-pixel sampler (default: sobol — Burley 2020)");
        Console.WriteLine("      --mis <balance|power>    MIS combination heuristic (default: balance)");
        Console.WriteLine("      --light-sampling <all|power|uniform>  NEE light strategy (default: all)");
        Console.WriteLine("      --texture-filtering <auto|on|off>     Analytic anti-aliasing via ray differentials (default: auto)");
        Console.WriteLine("      --caustics <on|off>      Focused caustics through caustic_caster glass/metal onto");
        Console.WriteLine("                               caustic_receiver surfaces — smooth via MNEE, rough/frosted via");
        Console.WriteLine("                               Specular Manifold Sampling (default: off, on for final/ultra;");
        Console.WriteLine("                               opt-in per-entity in YAML)");
        Console.WriteLine("      --sms-samples <n>        Stochastic SMS trials per rough caster connection (default: 4,");
        Console.WriteLine("                               8 for final/ultra). Higher = smoother frosted caustics, slower");
        Console.WriteLine("      --mnee-samples <n>       Emitter samples per receiver per light for MNEE caustics (default: 1).");
        Console.WriteLine("                               Raise to clean point/spot caustics (finite virtual bulb is noisier)");
        Console.WriteLine("      --sss-mode <auto|off>    Subsurface-scattering dispatch (default: auto = follow scene)");
        Console.WriteLine("      --sss-quality <preview|normal|high>   Random-walk preset; inherits from -q when omitted");
        Console.WriteLine("      --max-volume-bounces <n> Hard cap on random-walk bounces inside one entity");
        Console.WriteLine("      --list-cameras           List all cameras in the scene and exit");
        Console.WriteLine("  -v, --verbose                Show detailed loading and scene analysis info");
        Console.WriteLine("  -h, --help                   Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  (the .yaml extension on -i is optional: 'scenes/chess' resolves to 'scenes/chess.yaml')");
        Console.WriteLine("  from the root of the project: ");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q draft-tiny              # instant 480×270 sanity check");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q draft-small             # super-fast 960×540 composition check");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q medium                  # 1920×1080 review");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q final -o final.png     # 1920×1080 portfolio");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q ultra -o cover-4k.png  # 3840×2160 showcase");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q final -d 16            # final preset, but bump depth to 16");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess --list-cameras");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -c top -o top.png");
        Console.WriteLine("  from the bin/Debug/net10.0 folder: ");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess -q medium");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess --list-cameras");
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

    /// <summary>
    /// CLI quality preset (`--quality` / `-q`). Each preset fills the five
    /// quality knobs <c>-w</c>, <c>-H</c>, <c>-s</c>, <c>-d</c>, <c>-S</c> at
    /// once; any of those flags passed explicitly on the command line wins.
    /// Tiers follow the Preview/Standard/Final convention shared by Arnold,
    /// Cycles and RenderMan, with `-small` variants at half resolution, `-tiny`
    /// variants at quarter resolution (half of small) for instant checks, and
    /// an `ultra` 4K showcase tier. See `docs/reference/rendering-profiles.md`.
    /// </summary>
    sealed class QualityPreset
    {
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public int Samples { get; }
        public int Depth { get; }
        public int ShadowSamples { get; }
        /// <summary>SSS random-walk preset matched to this quality tier.
        /// draft → preview (cheap), medium → normal, final/ultra → high.</summary>
        public RandomWalkConfig WalkConfig { get; }
        public string SssQualityName { get; }
        /// <summary>Whether this tier turns caustics on by default. FINAL/ULTRA
        /// presets enable them: harmless on scenes without flagged entities (the
        /// caster registry is empty → zero cost), and exactly what a final render
        /// of a caustic-marked scene wants. An explicit <c>--caustics</c> overrides.</summary>
        public bool Caustics { get; }
        /// <summary>SMS (Phase-2b) trials per rough caster connection for this tier.</summary>
        public int SmsSamples { get; }

        private QualityPreset(string name, int w, int h, int s, int d, int ss,
                              RandomWalkConfig walk, string walkName,
                              bool caustics = false, int smsSamples = 4)
        {
            Name = name; Width = w; Height = h; Samples = s; Depth = d; ShadowSamples = ss;
            WalkConfig = walk; SssQualityName = walkName;
            Caustics = caustics; SmsSamples = smsSamples;
        }

        public static readonly QualityPreset DraftTiny   = new("draft-tiny",   480, 270,    16, 4, 1, RandomWalkConfig.Preview, "preview");
        public static readonly QualityPreset DraftSmall  = new("draft-small",  960, 540,    16, 4, 1, RandomWalkConfig.Preview, "preview");
        public static readonly QualityPreset Draft       = new("draft",       1920, 1080,   16, 4, 1, RandomWalkConfig.Preview, "preview");
        public static readonly QualityPreset MediumTiny  = new("medium-tiny",  480, 270,   128, 6, 1, RandomWalkConfig.Normal,  "normal");
        public static readonly QualityPreset MediumSmall = new("medium-small", 960, 540,   128, 6, 1, RandomWalkConfig.Normal,  "normal");
        public static readonly QualityPreset Medium      = new("medium",      1920, 1080,  128, 6, 1, RandomWalkConfig.Normal,  "normal");
        public static readonly QualityPreset FinalTiny   = new("final-tiny",   480, 270,  1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, smsSamples: 8);
        public static readonly QualityPreset FinalSmall  = new("final-small",  960, 540,  1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, smsSamples: 8);
        public static readonly QualityPreset Final       = new("final",       1920, 1080, 1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, smsSamples: 8);
        public static readonly QualityPreset Ultra       = new("ultra",       3840, 2160, 1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, smsSamples: 8);

        public const string NamesCsv =
            "draft-tiny, draft-small, draft, medium-tiny, medium-small, medium, final-tiny, final-small, final, ultra";

        public static QualityPreset? Parse(string value) =>
            value.Trim().ToLowerInvariant() switch
            {
                "draft-tiny"   => DraftTiny,
                "draft-small"  => DraftSmall,
                "draft"        => Draft,
                "medium-tiny"  => MediumTiny,
                "medium-small" => MediumSmall,
                "medium"       => Medium,
                "final-tiny"   => FinalTiny,
                "final-small"  => FinalSmall,
                "final"        => Final,
                "ultra"        => Ultra,
                _              => null,
            };
    }
}
