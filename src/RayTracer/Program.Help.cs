using System.Diagnostics;
using System.Numerics;
using RayTracer.Imaging;
using RayTracer.Core.Sampling;
using RayTracer.Denoising;
using RayTracer.Rendering;
using RayTracer.Rendering.Guiding;
using RayTracer.Scene;
using RayTracer.Volumetrics;

namespace RayTracer;

// Program, part: The command-line help and the quality presets.
partial class Program
{
    static void ShowHelp()
    {
        Console.WriteLine("3D-Ray RayTracer Engine");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/RayTracer/RayTracer.csproj -- [options]");
        Console.WriteLine();
        Console.WriteLine("The scene's 'render:' block sets any of the long options below (same name,");
        Console.WriteLine("underscores: light_sampling = --light-sampling); the command line wins, and a -q");
        Console.WriteLine("given here wins over the scene's keys that a preset sets. A scene resolution is");
        Console.WriteLine("scaled by the preset (-tiny 1/4, -small 1/2, ultra 2) instead of replaced.");
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
        Console.WriteLine("                                (no SSS, 512 spp, denoiser).");
        Console.WriteLine("                                pre-final = faithful preview of final (full feature");
        Console.WriteLine("                                set, 256 spp + denoiser, ~4-6x faster than final).");
        Console.WriteLine("  -w, --width <px>             Image width  (default: 960 via draft-small preset)");
        Console.WriteLine("  -H, --height <px>            Image height (default: 540 via draft-small preset)");
        Console.WriteLine("  -s, --samples <n>            Samples per pixel (default: 16 via draft-small preset)");
        Console.WriteLine("  -d, --depth <n>              Max path vertices (default: 8 via draft-small; 12 standard, 16 final, 32 ultra).");
        Console.WriteLine("                                Explicit -d lifts the per-kind limits below to the same value");
        Console.WriteLine("      --depth-diffuse <n>      Max diffuse bounces (presets: 3 draft, 4 standard, 8 final, 16 ultra)");
        Console.WriteLine("      --depth-glossy <n>       Max glossy/specular reflections (4, 6, 8, 16)");
        Console.WriteLine("      --depth-transmission <n> Max refractions/transmissions (8, 12, 16, 32)");
        Console.WriteLine("      --depth-volume <n>       Max scatters in a medium (3, 4, 8, 16)");
        Console.WriteLine("      --depth-transparent <n>  Max cut-outs crossed, outside the total (16; 32 ultra)");
        Console.WriteLine("  -S, --shadow-samples <n>     Area light shadow samples override (default 4; perfect squares work best)");
        Console.WriteLine("  -C, --clamp <n>              Firefly clamp: max per-sample radiance (default: off).");
        Console.WriteLine("                                Biased: cuts every sample brighter than n (try 10)");
        Console.WriteLine("      --indirect-clamp-factor  Indirect clamp = -C x factor, only with -C (default: 0.25; 1.0 = off)");
        Console.WriteLine("      --exposure <EV>          Exposure compensation in stops, applied before the view transform");
        Console.WriteLine("                                (default: 0 = identity; -1 darkens 2×, +1 brightens 2×)");
        Console.WriteLine("  -c, --camera <name|index>    Select camera by name or 0-based index");
        Console.WriteLine("      --sampler <prng|sobol>   Per-pixel sampler (default: sobol — Burley 2020)");
        Console.WriteLine("      --mis <balance|power>    MIS combination heuristic (default: balance)");
        Console.WriteLine("      --light-sampling <auto|all|power|uniform|bvh>  NEE light strategy (default: auto = all up to 16 lights, bvh above)");
        Console.WriteLine("      --texture-filtering <auto|on|off>     Analytic anti-aliasing via ray differentials (default: auto)");
        Console.WriteLine("      --integrator <path|bdpt|vcm>  Light transport: path tracing (default), bidirectional path tracing,");
        Console.WriteLine("                               or vertex connection and merging (bdpt + merging: caustics through glass/water)");
        Console.WriteLine("      --vcm-radius <f>         VCM merging radius at the first iteration, fraction of the scene radius (default: 0.003)");
        Console.WriteLine("      --guiding <on|off>       Path guiding: learn the incident light in training passes,");
        Console.WriteLine("                                then sample bounces from it too (unbiased; default: off)");
        Console.WriteLine("      --guiding-training <n>   Training spp out of -s, passes of 1, 2, 4, ... that join the image (default: -s / 2)");
        Console.WriteLine("      --denoiser <none|nlm|nfor|oidn>  Denoiser on the linear HDR beauty (oidn: Intel Open Image Denoise)");
        Console.WriteLine("                                (default: none; draft/standard/pre-final presets use oidn)");
        Console.WriteLine("      --denoise-quality <fast|high>  Denoiser speed/quality trade-off (default: high)");
        Console.WriteLine("      --adaptive <on|off>      Adaptive sampling: pixels stop once their noise is below the threshold");
        Console.WriteLine("                                (path tracing with sobol, not with --guiding or --reference)");
        Console.WriteLine("      --adaptive-threshold <f> Noise threshold, about the error of a display value (default: 0.01)");
        Console.WriteLine("      --adaptive-min-spp <n>   Samples before a pixel may stop (default: 4/sqrt(threshold), power of two)");
        Console.WriteLine("      --aov <list>             Comma list of albedo,normal,depth,beauty,variance —");
        Console.WriteLine("                                writes linear HDR buffers next to the -o output (as layers");
        Console.WriteLine("                                of the main file when -o is .exr, else as .pfm files)");
        Console.WriteLine("      --aov-format <pfm|exr>   Force one file per AOV in the given format, even when");
        Console.WriteLine("                                -o is .exr (default: embed in .exr output, else pfm)");
        Console.WriteLine("      --sss-mode <auto|off>    Subsurface-scattering dispatch (default: auto = follow scene)");
        Console.WriteLine("      --transparent-shadows <on|off>  Glass lets shadow rays through, tinted, instead of");
        Console.WriteLine("                                casting its physical shadow and caustic (default: off)");
        Console.WriteLine("      --filter <box|gaussian|blackman-harris>  Pixel reconstruction filter (default: gaussian,");
        Console.WriteLine("                                sigma 0.5 px; blackman-harris is a little sharper, box the sharpest)");
        Console.WriteLine("      --view <agx|aces|standard>  View transform from scene-linear to display");
        Console.WriteLine("                                (default: agx; aces = ACES 1.x SDR; standard = sRGB curve,");
        Console.WriteLine("                                no tone mapping)");
        Console.WriteLine("      --look <none|punchy|golden>  Creative look for the agx view (default: none)");
        Console.WriteLine("      --white-balance <K>      White balance in kelvin: a light of this temperature renders");
        Console.WriteLine("                                neutral (default: the camera's white_balance, else none)");
        Console.WriteLine("      --bit-depth <8|16>       Bits per channel of a .png output (default: 8)");
        Console.WriteLine("      --dither <on|off>        Dither 8-bit output against banding (default: on)");
        Console.WriteLine("      --reference              Unbiased reference estimator for validation: no clamps,");
        Console.WriteLine("                                no transparent shadows, uniform depths, no denoiser");
        Console.WriteLine("      --seed <n>               Independent sample sequence (default 0 = standard sequence)");
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
        /// <summary>The tier's split of <see cref="Depth"/> by kind of bounce.</summary>
        public PathDepths? Depths { get; }
        public int ShadowSamples { get; }
        /// <summary>When non-null, the tier forces a <c>--sss-mode</c> value
        /// (the FINAL-FAST tiers force <see cref="SssMode.Off"/>). An explicit
        /// <c>--sss-mode</c> flag still wins.</summary>
        public SssMode? SssModeOverride { get; }
        /// <summary>When non-null, the tier's default <c>--light-sampling</c>
        /// strategy (none sets one today: <c>auto</c> fits every tier). An
        /// explicit <c>--light-sampling</c> flag still wins.</summary>
        public LightSamplingStrategy? LightSampling { get; }
        /// <summary>When non-null, the tier's default <c>--indirect-clamp-factor</c>.
        /// An explicit flag still wins.</summary>
        public float? IndirectClampFactor { get; }
        /// <summary>The tier's default denoiser. DRAFT/STANDARD/PRE-FINAL run
        /// OIDN (NFOR where it does not load): low/mid spp is where denoising
        /// pays most, standard's 512 spp often leave faint residual noise, and
        /// pre-final leans on it to cut the final budgets 4×.
        /// FINAL/ULTRA stay off: converged reference renders keep every
        /// unfiltered detail. An explicit <c>--denoiser</c> flag wins.</summary>
        public DenoiserKind Denoiser { get; }
        /// <summary>The tier's default <c>--denoise-quality</c>.</summary>
        public DenoiseQuality DenoiseQuality { get; }
        /// <summary>The tier's default <c>--adaptive</c>. An explicit flag wins.</summary>
        public bool Adaptive { get; }

        private QualityPreset(string name, int w, int h, int s, int d, int ss,
                              SssMode? sssModeOverride = null,
                              LightSamplingStrategy? lightSampling = null,
                              float? indirectClampFactor = null,
                              DenoiserKind denoiser = DenoiserKind.None,
                              DenoiseQuality denoiseQuality = DenoiseQuality.High,
                              PathDepths? depths = null,
                              bool adaptive = false)
        {
            Name = name; Width = w; Height = h; Samples = s; Depth = d; ShadowSamples = ss;
            Depths = depths;
            SssModeOverride = sssModeOverride;
            LightSampling = lightSampling;
            IndirectClampFactor = indirectClampFactor;
            Denoiser = denoiser;
            DenoiseQuality = denoiseQuality;
            Adaptive = adaptive;
        }

        // Bounce budgets by kind (total, then diffuse, glossy, transmission,
        // volume, cut-outs): glass needs many transmission bounces, diffuse
        // interreflection few. Russian roulette ends most paths far earlier.
        private static readonly PathDepths DraftDepths    = new(3, 4, 8, 3, 16);
        private static readonly PathDepths StandardDepths = new(4, 6, 12, 4, 16);
        private static readonly PathDepths FinalDepths    = new(8, 8, 16, 8, 16);
        private static readonly PathDepths UltraDepths    = new(16, 16, 32, 16, 32);

        // Adaptive sampling (threshold 0.01) from standard up: the pixels that
        // converge early stop, the budget is the cap (PIANO-REVIEW.md Phase 9:
        // up to 60% fewer samples, 2-27% less time at the same error; the
        // pixels that stop first are the cheap ones). The draft's 16 spp are
        // below its first pass (64), so it would change nothing there.

        public static readonly QualityPreset DraftTiny   = new("draft-tiny",   480, 270,    16, 8, 1,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.Fast, depths: DraftDepths);
        public static readonly QualityPreset DraftSmall  = new("draft-small",  960, 540,    16, 8, 1,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.Fast, depths: DraftDepths);
        public static readonly QualityPreset Draft       = new("draft",       1920, 1080,   16, 8, 1,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.Fast, depths: DraftDepths);

        // STANDARD: the day-to-day quality render. Final-class image quality
        // on a *classic* scene — Lambertian/Disney, non-nested glass,
        // procedural marble — with the expensive global-illumination extras
        // stripped:
        // volumetric SSS OFF, single shadow sample
        // (512 spp already anti-aliases), relaxed indirect clamp, and the
        // denoiser absorbing the faint residual grain 512 spp can leave.
        public static readonly QualityPreset StandardTiny  = new("standard-tiny",  480, 270,  512, 12, 1,
            sssModeOverride: SssMode.Off, indirectClampFactor: 0.5f,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.High, depths: StandardDepths, adaptive: true);
        public static readonly QualityPreset StandardSmall = new("standard-small", 960, 540,  512, 12, 1,
            sssModeOverride: SssMode.Off, indirectClampFactor: 0.5f,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.High, depths: StandardDepths, adaptive: true);
        public static readonly QualityPreset Standard      = new("standard",      1920, 1080, 512, 12, 1,
            sssModeOverride: SssMode.Off, indirectClampFactor: 0.5f,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.High, depths: StandardDepths, adaptive: true);

        // PRE-FINAL: a faithful preview of `final` — the FULL final feature
        // set (SSS high, final bounce budgets, default
        // indirect clamp) with the sampling budgets cut where the denoiser
        // compensates best: ¼ of the pixel samples and a single shadow
        // sample (penumbra noise is exactly what feature-guided filtering
        // removes cleanest). Roughly 4-6× faster than `final`.
        public static readonly QualityPreset PreFinalTiny  = new("pre-final-tiny",  480, 270,  256, 16, 1,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.High, depths: FinalDepths, adaptive: true);
        public static readonly QualityPreset PreFinalSmall = new("pre-final-small", 960, 540,  256, 16, 1,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.High, depths: FinalDepths, adaptive: true);
        public static readonly QualityPreset PreFinal      = new("pre-final",      1920, 1080, 256, 16, 1,
            denoiser: DenoiserKind.Oidn, denoiseQuality: DenoiseQuality.High, depths: FinalDepths, adaptive: true);

        public static readonly QualityPreset FinalTiny   = new("final-tiny",   480, 270,  1024, 16, 4, depths: FinalDepths, adaptive: true);
        public static readonly QualityPreset FinalSmall  = new("final-small",  960, 540,  1024, 16, 4, depths: FinalDepths, adaptive: true);
        public static readonly QualityPreset Final       = new("final",       1920, 1080, 1024, 16, 4, depths: FinalDepths, adaptive: true);
        public static readonly QualityPreset Ultra       = new("ultra",       3840, 2160,  512, 32, 4, depths: UltraDepths, adaptive: true);

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
