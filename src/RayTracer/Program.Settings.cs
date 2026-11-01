using System.Globalization;
using RayTracer.Core.Sampling;
using RayTracer.Denoising;
using RayTracer.Rendering;
using RayTracer.Rendering.Guiding;
using RayTracer.Scene;

namespace RayTracer;

// Program, part: the command line (and the scene's `render:` block) read into
// the settings of one run.
partial class Program
{
    /// <summary>Everything a run needs, from the scene's render block and the command line.</summary>
    sealed class RunSettings
    {
        public required string[] Cli;     // the command line as typed
        public required string[] Args;    // render block as flags, then the command line
        public required string InputPath;
        public string OutputPath = "";
        public QualityPreset? Quality = QualityPreset.DraftSmall;
        public bool QualityIsDefault;

        public int Width, Height, Samples, Depth;
        public bool DepthExplicit;
        public PathDepths Depths;
        public int? DepthDiffuse, DepthGlossy, DepthTransmission, DepthVolume, DepthTransparent;
        public int? ShadowSamplesOverride;
        public float? ClampOverride;
        public string? CameraSelector;

        public SamplerKind SamplerKind = SamplerKind.Sobol;
        public MisHeuristic MisHeuristic = MisHeuristic.Balance;
        public LightSamplingStrategy LightSampling;
        public float IndirectClampFactor;
        public float ExposureEv = Renderer.DefaultExposureEv;
        public Renderer.TextureFilteringMode TextureFiltering = Renderer.TextureFilteringMode.Auto;
        public SssMode SssMode;

        public bool Guiding;
        public int? GuidingTraining;
        public IntegratorKind Integrator = IntegratorKind.Path;
        public float? VcmRadius;
        public DenoiserKind DenoiserKind;
        public DenoiseQuality DenoiseQuality;
        public HashSet<string> Aovs = new(StringComparer.OrdinalIgnoreCase);
        public string? AovFormat;

        public bool Verbose;
        public bool ReferenceMode;
        public bool Adaptive;
        public float AdaptiveThreshold = AdaptiveSettings.DefaultThreshold;
        public int AdaptiveMinSpp;
        public bool TransparentShadows;

        public PixelFilterKind PixelFilterKind = PixelFilterKind.Gaussian;
        public ViewTransform View = ViewTransform.AgX;
        public ViewLook Look = ViewLook.None;
        public float? WhiteBalanceArg;
        public int BitDepth = 8;
        public bool Dither = true;
        public int Seed;

        public bool ExrOutput => Path.GetExtension(OutputPath).Equals(".exr", StringComparison.OrdinalIgnoreCase);

        /// <summary>A `.exr` output absorbs the AOVs as layers unless --aov-format forces files.</summary>
        public bool EmbedAovs => ExrOutput && AovFormat == null;

        public string? Arg(string longName, string? shortName) => GetArg(Args, longName, shortName);

        /// <summary>True when the user typed the flag (not the scene's render block).</summary>
        public bool Typed(string longName, string? shortName = null) => GetArg(Cli, longName, shortName) != null;
    }

    /// <summary>
    /// Reads the settings of a run: the scene's <c>render:</c> block as flags,
    /// then the command line (GetArg takes the last occurrence, so the command
    /// line wins). Returns null after printing the error of an invalid value.
    /// </summary>
    static RunSettings? ParseSettings(string[] cli, SceneLoader.ParsedScene scene, string inputPath)
    {
        // A -q given on the command line also wins over the scene's keys that
        // a preset sets.
        bool cliQuality = GetArg(cli, "--quality", "-q") != null;
        var s = new RunSettings
        {
            Cli = cli,
            Args = [.. RenderBlock.ToArgs(scene.Data.Render, cliQuality), .. cli],
            InputPath = inputPath,
        };
        if (!ParseOutputAndQuality(s)) return null;
        var size = ParseSizeFlags(s);
        if (!ParseSampling(s)) return null;
        if (!ParseTransport(s)) return null;
        if (!ParseAovs(s)) return null;
        s.Verbose = HasFlag(s.Args, "--verbose", "-v");
        if (!ParseReferenceAndAdaptive(s)) return null;
        if (!ParseDisplay(s)) return null;
        ResolveSize(s, scene, size);
        return s;
    }

    static bool ParseOutputAndQuality(RunSettings s)
    {
        // Default output: "renders/render-<scene>.png".
        s.OutputPath = s.Arg("--output", "-o")
            ?? Path.Combine("renders", $"render-{Path.GetFileNameWithoutExtension(s.InputPath)}.png");

        // Quality preset (draft/standard/pre-final/final ladder × tiny/small/full + ultra 4K).
        // Resolved before the per-flag parsing so individual flags (`-s`, `-d`,
        // `-w`, `-H`, `-S`) can override any of the preset's values.
        // When no `-q` is given the renderer falls back to the `draft-small`
        // preset: a fast, denoised composition check is a far better default
        // first-run experience than a slow, un-denoised 1200×800 / 16 spp pass.
        string? qualityArg = s.Arg("--quality", "-q");
        s.QualityIsDefault = qualityArg == null;
        if (qualityArg != null)
        {
            s.Quality = QualityPreset.Parse(qualityArg);
            if (s.Quality == null)
            {
                Console.WriteLine($"Error: Unknown --quality '{qualityArg}'. Valid: {QualityPreset.NamesCsv}.");
                return false;
            }
        }
        return true;
    }

    /// <summary>The size flags as typed (a value of 0 or less, or none, is unset).</summary>
    readonly record struct SizeFlags(int Width, bool WidthSet, int Height, bool HeightSet,
                                     int Samples, bool SamplesSet, int Depth, bool DepthSet);

    static SizeFlags ParseSizeFlags(RunSettings s)
    {
        static bool Int(string? text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        bool w = Int(s.Arg("--width", "-w"), out int width);
        bool h = Int(s.Arg("--height", "-H"), out int height);
        bool sp = Int(s.Arg("--samples", "-s"), out int samples);
        bool d = Int(s.Arg("--depth", "-d"), out int depth);

        // Per-kind bounce limits (PIANO-REVIEW.md Phase 7). Unset = the preset's,
        // or the total when -d is explicit or in reference mode.
        int? DepthArg(string name) => Int(s.Arg(name, null), out var v) && v >= 0 ? v : null;
        s.DepthDiffuse = DepthArg("--depth-diffuse");
        s.DepthGlossy = DepthArg("--depth-glossy");
        s.DepthTransmission = DepthArg("--depth-transmission");
        s.DepthVolume = DepthArg("--depth-volume");
        s.DepthTransparent = DepthArg("--depth-transparent");

        // Shadow samples override (null = the per-light values of the scene).
        if (Int(s.Arg("--shadow-samples", "-S"), out var shadow) && shadow > 0)
            s.ShadowSamplesOverride = shadow;

        // Firefly clamp, opt-in (null = no clamp; see Renderer.SuggestedMaxSampleRadiance).
        if (float.TryParse(s.Arg("--clamp", "-C"), NumberStyles.Float, CultureInfo.InvariantCulture, out var clamp) && clamp > 0f)
            s.ClampOverride = clamp;

        // Camera selector: name or zero-based index.
        s.CameraSelector = s.Arg("--camera", "-c");
        return new SizeFlags(width, w, height, h, samples, sp, depth, d);
    }

    /// <summary>
    /// A keyword flag: the value's case-insensitive match in <paramref name="choices"/>,
    /// the default when the flag is absent, false (after the error) for anything else.
    /// </summary>
    static bool Choice<T>(RunSettings s, string flag, string valid, (string Name, T Value)[] choices, ref T value)
    {
        string? arg = s.Arg(flag, null);
        if (arg == null) return true;
        foreach (var (name, v) in choices)
            if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        Console.WriteLine($"Error: Unknown {flag} '{arg}'. Valid: {valid}.");
        return false;
    }

    static readonly (string, bool)[] OnOff = [("on", true), ("off", false)];

    static bool ParseSampling(RunSettings s)
    {
        // Sampler: Sobol (Burley 2020 hash-based Owen scrambling) by default;
        // `--sampler prng` is the legacy thread-local PRNG, for A/B comparisons.
        if (!Choice(s, "--sampler", "prng, sobol", [("prng", SamplerKind.Prng), ("sobol", SamplerKind.Sobol)], ref s.SamplerKind))
            return false;
        Sampler.SetKind(s.SamplerKind);

        // MIS combination heuristic (balance / power). See Veach 1997 §9.2.
        if (!Choice(s, "--mis", "balance, power",
                    [("balance", MisHeuristic.Balance), ("power", MisHeuristic.Power)], ref s.MisHeuristic))
            return false;

        // Light selection strategy. A quality preset may set a default; an
        // explicit flag still wins.
        s.LightSampling = s.Quality?.LightSampling ?? LightSamplingStrategy.Auto;
        if (!Choice(s, "--light-sampling", "auto, all, power, uniform, bvh",
                    [("all", LightSamplingStrategy.All), ("power", LightSamplingStrategy.Power),
                     ("uniform", LightSamplingStrategy.Uniform), ("bvh", LightSamplingStrategy.Bvh),
                     ("auto", LightSamplingStrategy.Auto)], ref s.LightSampling))
            return false;

        // Indirect clamp factor (only with -C). A preset may set a default.
        s.IndirectClampFactor = s.Quality?.IndirectClampFactor ?? Renderer.DefaultIndirectClampFactor;
        if (float.TryParse(s.Arg("--indirect-clamp-factor", null), NumberStyles.Float, CultureInfo.InvariantCulture,
                           out var icf) && icf >= 0f)
            s.IndirectClampFactor = icf;

        // Exposure compensation in stops, applied before the view transform:
        // the linear gain 2^EV slides the scene along the tone curve.
        if (float.TryParse(s.Arg("--exposure", null), NumberStyles.Float, CultureInfo.InvariantCulture, out var ev))
            s.ExposureEv = ev;

        // Texture filtering: ray differentials drive the analytic anti-aliasing
        // of filtered textures (octave clamp, mipmaps). 'off' disables the
        // differentials, for comparison against point sampling.
        if (!Choice(s, "--texture-filtering", "auto, on, off",
                    [("auto", Renderer.TextureFilteringMode.Auto), ("on", Renderer.TextureFilteringMode.On),
                     ("off", Renderer.TextureFilteringMode.Off)], ref s.TextureFiltering))
            return false;

        // SSS dispatch: `auto` walks the scattering interiors with the random
        // walk, `off` reduces them to absorption. A preset may force a mode.
        s.SssMode = s.Quality?.SssModeOverride ?? SssMode.Auto;
        return Choice(s, "--sss-mode", "auto, off", [("auto", SssMode.Auto), ("off", SssMode.Off)], ref s.SssMode);
    }

    static bool ParseTransport(RunSettings s)
    {
        // Path guiding: learn where the light comes from in training passes,
        // then sample bounces from that as well as from the BSDF (unbiased).
        // `--guiding-training N` sets the training budget, out of -s.
        if (!Choice(s, "--guiding", "on, off", OnOff, ref s.Guiding)) return false;
        s.GuidingTraining = int.TryParse(s.Arg("--guiding-training", null), out var gt) && gt > 0 ? gt : null;

        // Integrator: path tracing (default), bidirectional path tracing, or
        // vertex connection and merging (for caustics seen through glass and water).
        if (!Choice(s, "--integrator", "path, bdpt, vcm",
                    [("path", IntegratorKind.Path), ("bdpt", IntegratorKind.Bdpt), ("vcm", IntegratorKind.Vcm)],
                    ref s.Integrator))
            return false;
        s.VcmRadius = float.TryParse(s.Arg("--vcm-radius", null), NumberStyles.Float, CultureInfo.InvariantCulture,
                                     out var vr) && vr > 0f ? vr : null;
        if (s.Integrator != IntegratorKind.Path && s.Guiding)
        {
            Console.WriteLine("Error: --guiding is a path tracing option (not with --integrator bdpt|vcm).");
            return false;
        }

        // Denoiser on the linear HDR beauty before tone mapping. Quality
        // presets supply a default; an explicit flag wins.
        s.DenoiserKind = s.Quality?.Denoiser ?? DenoiserKind.None;
        if (!Choice(s, "--denoiser", "none, nlm, nfor, oidn",
                    [("none", DenoiserKind.None), ("nlm", DenoiserKind.Nlm), ("nfor", DenoiserKind.Nfor),
                     ("oidn", DenoiserKind.Oidn)], ref s.DenoiserKind))
            return false;
        // A preset's OIDN falls back to NFOR where the native library does not
        // load; an explicit `--denoiser oidn` is an error there.
        if (s.DenoiserKind == DenoiserKind.Oidn && !Oidn.IsAvailable)
        {
            if (s.Typed("--denoiser"))
            {
                Console.WriteLine($"Error: --denoiser oidn: Open Image Denoise did not load ({Oidn.LoadError}).");
                return false;
            }
            Console.WriteLine($"Note: Open Image Denoise did not load ({Oidn.LoadError}): the preset uses nfor.");
            s.DenoiserKind = DenoiserKind.Nfor;
        }
        s.DenoiseQuality = s.Quality?.DenoiseQuality ?? DenoiseQuality.High;
        return Choice(s, "--denoise-quality", "fast, high",
                      [("fast", DenoiseQuality.Fast), ("high", DenoiseQuality.High)], ref s.DenoiseQuality);
    }

    static bool ParseAovs(RunSettings s)
    {
        // `--aov albedo,normal,depth,beauty,variance` writes the guide buffers
        // next to the -o output; any AOV turns on full capture in the renderer.
        string? aovArg = s.Arg("--aov", null);
        if (aovArg != null)
        {
            foreach (string raw in aovArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string name = raw.ToLowerInvariant();
                if (name is not ("albedo" or "normal" or "depth" or "beauty" or "variance"))
                {
                    Console.WriteLine($"Error: Unknown --aov '{raw}'. Valid: albedo, normal, depth, beauty, variance.");
                    return false;
                }
                s.Aovs.Add(name);
            }
        }
        // `--aov-format pfm|exr` forces one file per AOV in that format; by
        // default a .exr output embeds them as layers, otherwise PFM files.
        string? format = null;
        if (!Choice(s, "--aov-format", "pfm, exr", [("pfm", (string?)"pfm"), ("exr", "exr")], ref format))
            return false;
        s.AovFormat = format;
        return true;
    }

    static bool ParseReferenceAndAdaptive(RunSettings s)
    {
        // `--reference`: the pure, unbiased estimator used to validate the
        // engine (PIANO-REVIEW.md, Phase 0): no clamps, no straight-through
        // transparent shadows, one depth for every kind of bounce. The denoiser
        // and vertex merging are biased by construction, so they are refused.
        s.ReferenceMode = HasFlag(s.Args, "--reference", null);
        if (s.ReferenceMode)
        {
            if (s.Integrator == IntegratorKind.Vcm)
            {
                Console.WriteLine("Error: --integrator vcm is biased (it merges nearby vertices): use bdpt with --reference.");
                return false;
            }
            if (s.Typed("--denoiser") && s.DenoiserKind != DenoiserKind.None)
                Console.WriteLine("Warning: --denoiser ignored in --reference mode.");
            if (s.Typed("--clamp", "-C") || s.Typed("--indirect-clamp-factor"))
                Console.WriteLine("Warning: --clamp / --indirect-clamp-factor ignored in --reference mode.");
            s.DenoiserKind = DenoiserKind.None;
            s.ClampOverride = null;
            s.IndirectClampFactor = Renderer.DefaultIndirectClampFactor;
        }

        // Adaptive sampling (Phase 9): on in the standard presets and above.
        s.Adaptive = s.Quality?.Adaptive ?? false;
        if (!Choice(s, "--adaptive", "on, off", OnOff, ref s.Adaptive)) return false;
        string? thresholdArg = s.Arg("--adaptive-threshold", null);
        if (thresholdArg != null
            && (!float.TryParse(thresholdArg, NumberStyles.Float, CultureInfo.InvariantCulture, out s.AdaptiveThreshold)
                || !(s.AdaptiveThreshold > 0f)))
        {
            Console.WriteLine($"Error: --adaptive-threshold '{thresholdArg}' must be a positive number.");
            return false;
        }
        s.AdaptiveMinSpp = int.TryParse(s.Arg("--adaptive-min-spp", null), out var am) && am > 0 ? am : 0;
        if (s.Adaptive)
        {
            string? why = s.ReferenceMode ? "--reference (stopping on a pixel's own samples is biased)"
                : s.Integrator != IntegratorKind.Path ? "--integrator bdpt|vcm (light paths reach every pixel)"
                : s.Guiding ? "--guiding"
                : s.SamplerKind != SamplerKind.Sobol ? "--sampler prng"
                : null;
            if (why != null)
            {
                if (s.Typed("--adaptive"))
                {
                    Console.WriteLine($"Error: --adaptive is not available with {why}.");
                    return false;
                }
                s.Adaptive = false;   // the preset's default yields to the explicit choice
            }
        }

        // `--transparent-shadows on`: straight-through tinted shadows through
        // all glass, an approximation (default off: physical shadows).
        if (!Choice(s, "--transparent-shadows", "on, off", OnOff, ref s.TransparentShadows)) return false;
        if (s.ReferenceMode && s.TransparentShadows)
        {
            if (s.Typed("--transparent-shadows"))
                Console.WriteLine("Warning: --transparent-shadows on ignored in --reference mode.");
            s.TransparentShadows = false;
        }
        return true;
    }

    static bool ParseDisplay(RunSettings s)
    {
        string? filterArg = s.Arg("--filter", null);
        if (filterArg != null && !PixelFilter.TryParse(filterArg, out s.PixelFilterKind))
        {
            Console.WriteLine($"Error: Unknown --filter '{filterArg}'. Valid: box, gaussian, blackman-harris.");
            return false;
        }

        string? viewArg = s.Arg("--view", null);
        if (viewArg != null && !DisplayTransform.TryParseView(viewArg, out s.View))
        {
            Console.WriteLine($"Error: Unknown --view '{viewArg}'. Valid: agx, aces, standard.");
            return false;
        }
        string? lookArg = s.Arg("--look", null);
        if (lookArg != null && !DisplayTransform.TryParseLook(lookArg, out s.Look))
        {
            Console.WriteLine($"Error: Unknown --look '{lookArg}'. Valid: none, punchy, golden.");
            return false;
        }
        if (s.Look != ViewLook.None && s.View != ViewTransform.AgX)
            Console.WriteLine($"Warning: --look {lookArg} applies to the agx view only; ignored.");

        string? wbArg = s.Arg("--white-balance", null);
        if (wbArg != null)
        {
            if (!float.TryParse(wbArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var wbK)
                || wbK < WhiteBalance.MinKelvin || wbK > WhiteBalance.MaxKelvin)
            {
                Console.WriteLine($"Error: --white-balance '{wbArg}' must be a temperature in {WhiteBalance.MinKelvin}-{WhiteBalance.MaxKelvin} K.");
                return false;
            }
            s.WhiteBalanceArg = wbK;
        }

        // `--bit-depth <8|16>` (16 for PNG only) and `--dither <on|off>` for 8-bit.
        string? bitDepthArg = s.Arg("--bit-depth", null);
        if (bitDepthArg != null)
        {
            if (bitDepthArg is not ("8" or "16"))
            {
                Console.WriteLine($"Error: Unknown --bit-depth '{bitDepthArg}'. Valid: 8, 16.");
                return false;
            }
            s.BitDepth = int.Parse(bitDepthArg);
        }
        if (!Choice(s, "--dither", "on, off", OnOff, ref s.Dither)) return false;

        // `--seed <n>`: independent sample sequences (0 = the default, bit-identical).
        string? seedArg = s.Arg("--seed", null);
        if (seedArg != null && !int.TryParse(seedArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.Seed))
        {
            Console.WriteLine($"Error: Invalid --seed '{seedArg}' (integer expected).");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Resolution, samples and depths: the flags, else the scene's render
    /// block (scaled by a -tiny/-small preset), else the preset.
    /// </summary>
    static void ResolveSize(RunSettings s, SceneLoader.ParsedScene scene, SizeFlags f)
    {
        int width = f.Width, height = f.Height, samples = f.Samples, depth = f.Depth;
        bool wSet = f.WidthSet, hSet = f.HeightSet;
        // A size in the scene's render block is the full-resolution frame: a
        // -tiny/-small preset scales it as it scales its own 1920×1080.
        if (scene.Data.Render is { Width: int sceneW, Height: int sceneH } && sceneW > 0 && sceneH > 0)
        {
            float presetScale = (s.Quality?.Width ?? 1920) / 1920f;
            if (!wSet || width <= 0) { width = Math.Max(1, (int)MathF.Round(sceneW * presetScale)); wSet = true; }
            if (!hSet || height <= 0) { height = Math.Max(1, (int)MathF.Round(sceneH * presetScale)); hSet = true; }
        }
        if (!wSet || width <= 0) width = s.Quality?.Width ?? 1200;
        if (!hSet || height <= 0) height = s.Quality?.Height ?? 800;
        if (!f.SamplesSet || samples <= 0) samples = s.Quality?.Samples ?? 16;
        s.DepthExplicit = f.DepthSet && depth > 0;
        if (!s.DepthExplicit) depth = s.Quality?.Depth ?? 8;
        if (!s.ShadowSamplesOverride.HasValue && s.Quality != null)
            s.ShadowSamplesOverride = s.Quality.ShadowSamples;
        (s.Width, s.Height, s.Samples, s.Depth) = (width, height, samples, depth);

        PathDepths baseDepths = !s.DepthExplicit && !s.ReferenceMode && s.Quality?.Depths is PathDepths qd
            ? qd
            : PathDepths.Uniform(depth);
        s.Depths = new PathDepths(
            Math.Min(s.DepthDiffuse ?? baseDepths.Diffuse, depth),
            Math.Min(s.DepthGlossy ?? baseDepths.Glossy, depth),
            Math.Min(s.DepthTransmission ?? baseDepths.Transmission, depth),
            Math.Min(s.DepthVolume ?? baseDepths.Volume, depth),
            s.DepthTransparent ?? baseDepths.Transparent);
    }
}
