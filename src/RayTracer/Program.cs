using System.Diagnostics;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using RayTracer.Core.Sampling;
using RayTracer.Denoising;
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

        // Quality preset (draft/standard/pre-final/final ladder × tiny/small/full + ultra 4K).
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

        // Light selection strategy. See LightSamplingStrategy. A quality preset
        // may set a default (standard → power); an explicit flag still wins.
        LightSamplingStrategy lightSampling = quality?.LightSampling ?? LightSamplingStrategy.All;
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

        // Indirect bounce clamp factor (Cycles/Arnold style depth-aware
        // suppression). A quality preset may set a default (standard → 0.5);
        // an explicit flag still wins.
        float indirectClampFactor = quality?.IndirectClampFactor ?? Renderer.DefaultIndirectClampFactor;
        string? indirectClampArg = GetArg(args, "--indirect-clamp-factor", null);
        if (float.TryParse(indirectClampArg,
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
        // A quality preset may force a mode (standard → off); an explicit
        // --sss-mode flag still wins.
        SssMode sssMode = quality?.SssModeOverride ?? SssMode.Auto;
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
        else if (quality != null && sssMode != SssMode.Off)
        {
            // Skipped when SSS is forced off (e.g. standard): the random-walk
            // config is unused, so don't load it or print a misleading
            // "SSS quality" line — the "SSS mode: off" line says it all.
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
        // ── Caustics ─────────────────────────────────────────────────────────
        // `--caustics on|off` toggles photon-mapped caustics; an explicit flag
        // overrides the quality-preset default (FINAL/ULTRA enable it). When on,
        // a photon pre-pass focuses light through every specular surface in the
        // scene — no per-object flags. `--caustic-photons N` overrides the budget.
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

        // Caustic photon budget: preset default (final/ultra raise it), global
        // fallback when on outside those presets, and an explicit --caustic-photons
        // override. Zero when caustics are off.
        int causticPhotons = 0;
        if (enableCaustics)
        {
            const int DefaultCausticPhotons = 2_000_000;
            int presetBudget = quality?.CausticPhotons ?? 0;
            causticPhotons = presetBudget > 0 ? presetBudget : DefaultCausticPhotons;
            if (int.TryParse(GetArg(args, "--caustic-photons", null), out var cpArg) && cpArg > 0)
                causticPhotons = cpArg;
        }

        // ── Denoiser ─────────────────────────────────────────────────────────
        // `--denoiser none|nlm|nfor` runs the feature-guided denoiser on the
        // linear HDR beauty before tone mapping. Quality presets supply a
        // default (draft/standard/pre-final → nfor); an explicit flag wins.
        DenoiserKind denoiserKind = quality?.Denoiser ?? DenoiserKind.None;
        string? denoiserArg = GetArg(args, "--denoiser", null);
        if (denoiserArg != null)
        {
            switch (denoiserArg.ToLowerInvariant())
            {
                case "none": denoiserKind = DenoiserKind.None; break;
                case "nlm":  denoiserKind = DenoiserKind.Nlm;  break;
                case "nfor": denoiserKind = DenoiserKind.Nfor; break;
                default:
                    Console.WriteLine($"Error: Unknown --denoiser '{denoiserArg}'. Valid: none, nlm, nfor.");
                    return;
            }
        }

        DenoiseQuality denoiseQuality = quality?.DenoiseQuality ?? DenoiseQuality.High;
        string? denoiseQualityArg = GetArg(args, "--denoise-quality", null);
        if (denoiseQualityArg != null)
        {
            switch (denoiseQualityArg.ToLowerInvariant())
            {
                case "fast": denoiseQuality = DenoiseQuality.Fast; break;
                case "high": denoiseQuality = DenoiseQuality.High; break;
                default:
                    Console.WriteLine($"Error: Unknown --denoise-quality '{denoiseQualityArg}'. Valid: fast, high.");
                    return;
            }
        }

        // ── AOV output (linear HDR, PFM/EXR) ─────────────────────────────────
        // `--aov albedo,normal,depth,beauty,variance` writes the requested
        // guide buffers next to the -o output. Any AOV request turns on full
        // capture (beauty halves + AOVs) in the renderer.
        var aovs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? aovArg = GetArg(args, "--aov", null);
        if (aovArg != null)
        {
            foreach (string raw in aovArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string name = raw.ToLowerInvariant();
                if (name is not ("albedo" or "normal" or "depth" or "beauty" or "variance"))
                {
                    Console.WriteLine($"Error: Unknown --aov '{raw}'. Valid: albedo, normal, depth, beauty, variance.");
                    return;
                }
                aovs.Add(name);
            }
        }

        // `--aov-format pfm|exr` forces one file per AOV in that format. When
        // omitted and -o is .exr, the AOVs are embedded as layers in the main
        // multilayer EXR instead; otherwise they default to separate PFM files.
        string? aovFormat = null;
        string? aovFormatArg = GetArg(args, "--aov-format", null);
        if (aovFormatArg != null)
        {
            switch (aovFormatArg.ToLowerInvariant())
            {
                case "pfm": aovFormat = "pfm"; break;
                case "exr": aovFormat = "exr"; break;
                default:
                    Console.WriteLine($"Error: Unknown --aov-format '{aovFormatArg}'. Valid: pfm, exr.");
                    return;
            }
        }

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
        if (denoiserKind != DenoiserKind.None)
            Console.WriteLine($"  Denoiser:    {denoiserKind.ToString().ToLowerInvariant()} ({denoiseQuality.ToString().ToLowerInvariant()})");

        // A `.exr` output switches the main image to scene-linear HDR (no
        // exposure/ACES/gamma) and, unless --aov-format forces separate
        // files, absorbs any requested AOVs as layers of that same file.
        bool exrOutput = Path.GetExtension(outputPath).Equals(".exr", StringComparison.OrdinalIgnoreCase);
        bool embedAovs = exrOutput && aovFormat == null;
        if (exrOutput)
            Console.WriteLine("  HDR output:  linear EXR (pre-tone-mapping)");
        if (aovs.Count > 0)
            Console.WriteLine($"  AOVs:        {string.Join(", ", aovs)} ({(embedAovs ? "EXR layers" : (aovFormat ?? "pfm") + " files")})");
        Console.WriteLine();

        // Load scene
        Console.Write("  Loading scene... ");
        var sw = Stopwatch.StartNew();
        try
        {
            var (world, camera, lights, sky, globalMedium, motionBlur) =
                SceneLoader.Load(inputPath, width, height, shadowSamplesOverride, cameraSelector);

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
            if (motionBlur.Active)
                Console.WriteLine($"  Motion blur: shutter [{motionBlur.ShutterOpen:0.##}, {motionBlur.ShutterClose:0.##}]");

            // Render (constructor may print scene analysis info before the blank line)
            var renderer = new Renderer(
                world, camera, lights, sky, samples, depth, globalMedium,
                clampOverride, verbose, misHeuristic, lightSampling,
                indirectClampFactor, textureFiltering, exposureEv,
                sssMode, walkConfig,
                enableCaustics: enableCaustics, causticPhotons: causticPhotons,
                motionBlur: motionBlur);
            Console.WriteLine();

            var captureOptions = denoiserKind != DenoiserKind.None || aovs.Count > 0
                ? RenderCaptureOptions.Full
                : exrOutput
                    ? new RenderCaptureOptions { CaptureBeautyHalves = true }
                    : RenderCaptureOptions.None;

            sw.Restart();
            var result = renderer.Render(width, height, captureOptions);
            var pixels = result.Pixels;
            var elapsed = sw.Elapsed;
            Console.WriteLine($"  Render time: {FormatElapsed(elapsed)}");

            // Denoise the linear HDR beauty, then re-apply the identical
            // display transform (exposure \u2192 ACES \u2192 gamma).
            FrameBuffer? denoisedBeauty = null;
            if (denoiserKind != DenoiserKind.None)
            {
                var denoiseOptions = new DenoiserOptions { Kind = denoiserKind, Quality = denoiseQuality };
                sw.Restart();
                denoisedBeauty = NforDenoiser.Denoise(result.Buffers!, denoiseOptions);
                pixels = renderer.ToneMapToDisplay(denoisedBeauty);
                Console.WriteLine($"  Denoise time: {FormatElapsed(sw.Elapsed)}");
            }

            // Save image
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            if (exrOutput)
            {
                var beauty = denoisedBeauty ?? result.Buffers!.Beauty;
                int embedded = SaveExrBeauty(outputPath, beauty, result.Buffers!, embedAovs ? aovs : null);
                Console.WriteLine();
                Console.WriteLine($"  \u2713 Saved: {Path.GetFullPath(outputPath)} " +
                                  $"(linear EXR{(embedded > 0 ? $", +{embedded} AOV layers" : "")})");
            }
            else
            {
                SaveImage(pixels, width, height, outputPath);
                Console.WriteLine();
                Console.WriteLine($"  \u2713 Saved: {Path.GetFullPath(outputPath)}");
            }

            if (aovs.Count > 0 && !embedAovs)
                SaveAovs(result.Buffers!, aovs, outputPath, aovFormat ?? "pfm", denoisedBeauty);
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
        Console.WriteLine("  -o, --output <path>          Output image (default: renders/render-<scene>.png).");
        Console.WriteLine("                                .png/.jpg/.bmp = tone-mapped display image; .exr = scene-linear");
        Console.WriteLine("                                HDR before exposure/tone-map (multilayer, half RGB + float Z, ZIP)");
        Console.WriteLine("  -q, --quality <preset>       Render-quality preset that fills -w/-H/-s/-d/-S in one shot.");
        Console.WriteLine("                                Any explicit flag below wins over the preset's value.");
        Console.WriteLine("                                Presets: draft-tiny, draft-small, draft,");
        Console.WriteLine("                                          standard-tiny, standard-small, standard,");
        Console.WriteLine("                                          pre-final-tiny, pre-final-small, pre-final,");
        Console.WriteLine("                                          final-tiny, final-small, final,");
        Console.WriteLine("                                          ultra (4K).");
        Console.WriteLine("                                standard = final-class quality for classic scenes");
        Console.WriteLine("                                (no caustics/SSS, 512 spp, power NEE, denoiser).");
        Console.WriteLine("                                pre-final = faithful preview of final (full feature");
        Console.WriteLine("                                set, 256 spp + denoiser, ~4-6x faster than final).");
        Console.WriteLine("  -w, --width <px>             Image width  (default: 1200)");
        Console.WriteLine("  -H, --height <px>            Image height (default: 800)");
        Console.WriteLine("  -s, --samples <n>            Samples per pixel (default: 16, see rendering profiles)");
        Console.WriteLine("  -d, --depth <n>              Max ray depth (default: 8, raise to 16+ for stacked glass)");
        Console.WriteLine("  -S, --shadow-samples <n>     Area light shadow samples override (default 4; perfect squares work best)");
        Console.WriteLine("  -C, --clamp <n>              Max per-sample radiance / firefly clamp (default: 10)");
        Console.WriteLine("      --indirect-clamp-factor  Clamp factor for indirect bounces (default: 0.25 = on; 1.0 = off)");
        Console.WriteLine("      --exposure <EV>          Photographic exposure compensation in stops applied pre-ACES");
        Console.WriteLine("                                (default: 0 = identity; -1 darkens 2×, +1 brightens 2×)");
        Console.WriteLine("  -c, --camera <name|index>    Select camera by name or 0-based index");
        Console.WriteLine("      --sampler <prng|sobol>   Per-pixel sampler (default: sobol — Burley 2020)");
        Console.WriteLine("      --mis <balance|power>    MIS combination heuristic (default: balance)");
        Console.WriteLine("      --light-sampling <all|power|uniform>  NEE light strategy (default: all)");
        Console.WriteLine("      --texture-filtering <auto|on|off>     Analytic anti-aliasing via ray differentials (default: auto)");
        Console.WriteLine("      --caustics <on|off>      Photon-mapped caustics (default: off, on for final/ultra)");
        Console.WriteLine("      --caustic-photons <n>    Caustic photon budget when --caustics on (default: 2-4M by preset)");
        Console.WriteLine("      --denoiser <none|nlm|nfor>  Feature-guided denoiser on the linear HDR beauty");
        Console.WriteLine("                                (default: none; draft/standard/pre-final presets use nfor)");
        Console.WriteLine("      --denoise-quality <fast|high>  Denoiser speed/quality trade-off (default: high)");
        Console.WriteLine("      --aov <list>             Comma list of albedo,normal,depth,beauty,variance —");
        Console.WriteLine("                                writes linear HDR buffers next to the -o output (as layers");
        Console.WriteLine("                                of the main file when -o is .exr, else as .pfm files)");
        Console.WriteLine("      --aov-format <pfm|exr>   Force one file per AOV in the given format, even when");
        Console.WriteLine("                                -o is .exr (default: embed in .exr output, else pfm)");
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
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q standard                # 1920×1080 quality review");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q final -o final.png     # 1920×1080 portfolio");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q ultra -o cover-4k.png  # 3840×2160 showcase");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -q final -d 16            # final preset, but bump depth to 16");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess --list-cameras");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess -c top -o top.png");
        Console.WriteLine("  from the bin/Debug/net10.0 folder: ");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess -q standard");
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

    /// <summary>
    /// Writes the requested AOV planes as one file each next to the main
    /// output: <c>renders/foo.png</c> → <c>renders/foo.albedo.pfm</c> (or
    /// <c>.exr</c>) etc. All values are linear, scene-referred (pre-exposure,
    /// pre-tonemap); the beauty AOV is the denoised buffer when a denoiser is
    /// active.
    /// </summary>
    static void SaveAovs(RenderBuffers buffers, HashSet<string> aovs, string outputPath,
                         string format, FrameBuffer? denoisedBeauty = null)
    {
        string stem = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? "",
            Path.GetFileNameWithoutExtension(outputPath));

        foreach (string aov in aovs)
        {
            string path = $"{stem}.{aov}.{format}";
            FrameBuffer fb = ResolveAov(buffers, aov, denoisedBeauty);
            if (format == "exr")
                ExrImage.Write(path, fb.Width, fb.Height, AovExrChannels(aov, fb, embedded: false));
            else
                fb.SavePfm(path);
            Console.WriteLine($"  ✓ Saved: {Path.GetFullPath(path)}");
        }
    }

    static FrameBuffer ResolveAov(RenderBuffers buffers, string aov, FrameBuffer? denoisedBeauty) =>
        aov switch
        {
            "beauty"   => denoisedBeauty ?? buffers.Beauty,
            "albedo"   => buffers.CombineHalves(buffers.AlbedoA!, buffers.AlbedoB!),
            "normal"   => buffers.CombineHalves(buffers.NormalA!, buffers.NormalB!),
            "depth"    => buffers.CombineDepthHalves(),
            "variance" => buffers.RawBeautyVariance(),
            _          => throw new InvalidOperationException($"unreachable AOV '{aov}'"),
        };

    /// <summary>
    /// Writes the scene-linear (pre-exposure, pre-tonemap) beauty as a
    /// half-float RGB EXR; when <paramref name="embedAovs"/> is non-null the
    /// requested AOVs become extra layers of the same multilayer file
    /// (<c>albedo.R…</c>, <c>normal.X…</c>, float <c>Z</c>,
    /// <c>variance.R…</c>). Returns the number of embedded AOV layers.
    /// </summary>
    static int SaveExrBeauty(string path, FrameBuffer beauty, RenderBuffers buffers,
                             HashSet<string>? embedAovs)
    {
        var channels = new List<ExrImage.Channel>(AovExrChannels("beauty", beauty, embedded: true));
        int embedded = 0;
        if (embedAovs != null)
        {
            foreach (string aov in embedAovs)
            {
                if (aov == "beauty")
                {
                    // The main R,G,B channels ARE the (denoised) beauty.
                    Console.WriteLine("  Note: --aov beauty is already the main EXR output — layer skipped.");
                    continue;
                }
                channels.AddRange(AovExrChannels(aov, ResolveAov(buffers, aov, null), embedded: true));
                embedded++;
            }
        }
        ExrImage.Write(path, beauty.Width, beauty.Height, channels);
        return embedded;
    }

    /// <summary>
    /// EXR channel layout for one AOV buffer. Embedded layers follow the
    /// <c>layer.channel</c> convention compositors group on (suffix R/G/B for
    /// colour data, X/Y/Z for vectors, a bare float <c>Z</c> for depth — the
    /// no-hit sentinel stays −1); standalone files use plain <c>R,G,B</c> so
    /// any viewer opens them without layer selection. Colour channels are
    /// half, depth is float32.
    /// </summary>
    static List<ExrImage.Channel> AovExrChannels(string aov, FrameBuffer fb, bool embedded)
    {
        int n = fb.Width * fb.Height;
        var data = fb.Data.AsMemory();
        if (aov == "depth")
            return new() { new ExrImage.Channel("Z", ExrPixelType.Float, data.Slice(0, n)) };

        string[] names = aov switch
        {
            "normal" when embedded => new[] { "normal.X", "normal.Y", "normal.Z" },
            "beauty"               => new[] { "R", "G", "B" },
            _ when embedded        => new[] { $"{aov}.R", $"{aov}.G", $"{aov}.B" },
            _                      => new[] { "R", "G", "B" },
        };
        return new()
        {
            new ExrImage.Channel(names[0], ExrPixelType.Half, data.Slice(0, n)),
            new ExrImage.Channel(names[1], ExrPixelType.Half, data.Slice(n, n)),
            new ExrImage.Channel(names[2], ExrPixelType.Half, data.Slice(2 * n, n)),
        };
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
        /// draft → preview (cheap), pre-final/final/ultra → high (standard
        /// forces SSS off, so its value is unused).</summary>
        public RandomWalkConfig WalkConfig { get; }
        public string SssQualityName { get; }
        /// <summary>Whether this tier turns caustics on by default. FINAL/ULTRA
        /// presets enable them: harmless on scenes without flagged entities (the
        /// caster registry is empty → zero cost), and exactly what a final render
        /// of a caustic-marked scene wants. An explicit <c>--caustics</c> overrides.</summary>
        public bool Caustics { get; }
        /// <summary>Caustic photon budget for this tier when caustics are on
        /// (0 → use the global default). FINAL/ULTRA raise it for cleaner maps.</summary>
        public int CausticPhotons { get; }
        /// <summary>When non-null, the tier forces a <c>--sss-mode</c> value
        /// (the FINAL-FAST tiers force <see cref="SssMode.Off"/>). An explicit
        /// <c>--sss-mode</c> flag still wins.</summary>
        public SssMode? SssModeOverride { get; }
        /// <summary>When non-null, the tier's default <c>--light-sampling</c>
        /// strategy (FINAL-FAST uses <see cref="LightSamplingStrategy.Power"/>,
        /// which scales better than the global <c>all</c> default). An explicit
        /// <c>--light-sampling</c> flag still wins.</summary>
        public LightSamplingStrategy? LightSampling { get; }
        /// <summary>When non-null, the tier's default <c>--indirect-clamp-factor</c>.
        /// An explicit flag still wins.</summary>
        public float? IndirectClampFactor { get; }
        /// <summary>The tier's default denoiser. DRAFT/STANDARD/PRE-FINAL run
        /// NFOR (low/mid spp is where denoising pays most; standard's 512 spp
        /// often leave faint residual noise that NFOR removes for a few extra
        /// seconds, and pre-final leans on it to cut the final budgets 4×).
        /// FINAL/ULTRA stay off — converged reference renders keep every
        /// unfiltered detail. An explicit <c>--denoiser</c> flag wins.</summary>
        public DenoiserKind Denoiser { get; }
        /// <summary>The tier's default <c>--denoise-quality</c>.</summary>
        public DenoiseQuality DenoiseQuality { get; }

        private QualityPreset(string name, int w, int h, int s, int d, int ss,
                              RandomWalkConfig walk, string walkName,
                              bool caustics = false, int causticPhotons = 0,
                              SssMode? sssModeOverride = null,
                              LightSamplingStrategy? lightSampling = null,
                              float? indirectClampFactor = null,
                              DenoiserKind denoiser = DenoiserKind.None,
                              DenoiseQuality denoiseQuality = DenoiseQuality.High)
        {
            Name = name; Width = w; Height = h; Samples = s; Depth = d; ShadowSamples = ss;
            WalkConfig = walk; SssQualityName = walkName;
            Caustics = caustics; CausticPhotons = causticPhotons;
            SssModeOverride = sssModeOverride;
            LightSampling = lightSampling;
            IndirectClampFactor = indirectClampFactor;
            Denoiser = denoiser;
            DenoiseQuality = denoiseQuality;
        }

        public static readonly QualityPreset DraftTiny   = new("draft-tiny",   480, 270,    16, 4, 1, RandomWalkConfig.Preview, "preview",
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.Fast);
        public static readonly QualityPreset DraftSmall  = new("draft-small",  960, 540,    16, 4, 1, RandomWalkConfig.Preview, "preview",
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.Fast);
        public static readonly QualityPreset Draft       = new("draft",       1920, 1080,   16, 4, 1, RandomWalkConfig.Preview, "preview",
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.Fast);

        // STANDARD: the day-to-day quality render. Final-class image quality
        // on a *classic* scene — Lambertian/Disney, non-nested glass,
        // procedural marble — with the expensive global-illumination extras
        // stripped:
        // photon caustics OFF, volumetric SSS OFF, single shadow sample
        // (512 spp already anti-aliases), power-weighted single-light NEE,
        // relaxed indirect clamp, and the NFOR denoiser absorbing the faint
        // residual grain 512 spp can leave.
        public static readonly QualityPreset StandardTiny  = new("standard-tiny",  480, 270,  512, 8, 1, RandomWalkConfig.High, "high",
            caustics: false, sssModeOverride: SssMode.Off, lightSampling: LightSamplingStrategy.Power, indirectClampFactor: 0.5f,
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.High);
        public static readonly QualityPreset StandardSmall = new("standard-small", 960, 540,  512, 8, 1, RandomWalkConfig.High, "high",
            caustics: false, sssModeOverride: SssMode.Off, lightSampling: LightSamplingStrategy.Power, indirectClampFactor: 0.5f,
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.High);
        public static readonly QualityPreset Standard      = new("standard",      1920, 1080, 512, 8, 1, RandomWalkConfig.High, "high",
            caustics: false, sssModeOverride: SssMode.Off, lightSampling: LightSamplingStrategy.Power, indirectClampFactor: 0.5f,
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.High);

        // PRE-FINAL: a faithful preview of `final` — the FULL final feature
        // set (caustics on, SSS high, depth 8, all-lights NEE, default
        // indirect clamp) with the sampling budgets cut where the denoiser
        // compensates best: ¼ of the pixel samples and a single shadow
        // sample (penumbra noise is exactly what feature-guided filtering
        // removes cleanest). Roughly 4-6× faster than `final`.
        public static readonly QualityPreset PreFinalTiny  = new("pre-final-tiny",  480, 270,  256, 8, 1, RandomWalkConfig.High, "high",
            caustics: true, causticPhotons: 2_000_000,
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.High);
        public static readonly QualityPreset PreFinalSmall = new("pre-final-small", 960, 540,  256, 8, 1, RandomWalkConfig.High, "high",
            caustics: true, causticPhotons: 2_000_000,
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.High);
        public static readonly QualityPreset PreFinal      = new("pre-final",      1920, 1080, 256, 8, 1, RandomWalkConfig.High, "high",
            caustics: true, causticPhotons: 2_000_000,
            denoiser: DenoiserKind.Nfor, denoiseQuality: DenoiseQuality.High);

        public static readonly QualityPreset FinalTiny   = new("final-tiny",   480, 270,  1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, causticPhotons: 2_000_000);
        public static readonly QualityPreset FinalSmall  = new("final-small",  960, 540,  1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, causticPhotons: 2_000_000);
        public static readonly QualityPreset Final       = new("final",       1920, 1080, 1024, 8, 4, RandomWalkConfig.High,    "high", caustics: true, causticPhotons: 3_000_000);
        public static readonly QualityPreset Ultra       = new("ultra",       3840, 2160,  512, 8, 4, RandomWalkConfig.High,    "high", caustics: true, causticPhotons: 4_000_000);

        public const string NamesCsv =
            "draft-tiny, draft-small, draft, standard-tiny, standard-small, standard, " +
            "pre-final-tiny, pre-final-small, pre-final, final-tiny, final-small, final, ultra";

        public static QualityPreset? Parse(string value) =>
            value.Trim().ToLowerInvariant() switch
            {
                "draft-tiny"      => DraftTiny,
                "draft-small"     => DraftSmall,
                "draft"           => Draft,
                "standard-tiny"   => StandardTiny,
                "standard-small"  => StandardSmall,
                "standard"        => Standard,
                "pre-final-tiny"  => PreFinalTiny,
                "pre-final-small" => PreFinalSmall,
                "pre-final"       => PreFinal,
                "final-tiny"      => FinalTiny,
                "final-small"     => FinalSmall,
                "final"           => Final,
                "ultra"           => Ultra,
                _                 => null,
            };
    }
}
