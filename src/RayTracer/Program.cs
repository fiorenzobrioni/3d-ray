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

partial class Program
{
    static void Main(string[] cli)
    {
        // Show help if no arguments or help requested
        if (cli.Length == 0 || HasFlag(cli, "--help", "-h"))
        {
            ShowHelp();
            return;
        }
        if (ResolveInput(cli) is not string inputPath) return;
        SceneLoader.SetVerbose(HasFlag(cli, "--verbose", "-v"));

        // The scene is read (and validated) first: its `render:` block is the
        // base of every setting, the command line overrides it.
        SceneLoader.ParsedScene scene;
        try
        {
            scene = SceneLoader.Parse(inputPath);
        }
        catch (SceneFormatException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        // --list-cameras: print available cameras and exit
        if (HasFlag(cli, "--list-cameras", null))
        {
            SceneLoader.TryListCameras(inputPath);
            return;
        }

        if (ParseSettings(cli, scene, inputPath) is not RunSettings settings) return;
        PrintSettings(settings);
        RenderAndSave(settings, scene);
    }

    /// <summary>
    /// The scene file of `-i`, as given or with `.yaml`/`.yml` appended when
    /// the extension was left out (e.g. `-i scenes/chess`); null after the error.
    /// </summary>
    static string? ResolveInput(string[] cli)
    {
        string? inputPath = GetArg(cli, "--input", "-i");
        if (string.IsNullOrEmpty(inputPath))
        {
            Console.WriteLine("Error: Missing required argument --input (-i)");
            Console.WriteLine();
            ShowHelp();
            Environment.ExitCode = 1;
            return null;
        }
        if (File.Exists(inputPath)) return inputPath;
        if (File.Exists(inputPath + ".yaml")) return inputPath + ".yaml";
        if (File.Exists(inputPath + ".yml")) return inputPath + ".yml";
        Console.WriteLine($"Error: Scene file not found: {inputPath}");
        Environment.ExitCode = 1;
        return null;
    }

    static void PrintSettings(RunSettings s)
    {
        Console.WriteLine("╔═══════════════════════════════════════════╗");
        Console.WriteLine("║          3D-Ray RayTracer Engine          ║");
        Console.WriteLine("╚═══════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Scene:       {s.InputPath}");
        Console.WriteLine($"  Output:      {s.OutputPath}");
        if (s.Quality != null)
            Console.WriteLine($"  Quality:     {s.Quality.Name}{(s.QualityIsDefault ? " (default)" : "")}");
        Console.WriteLine($"  Resolution:  {s.Width} \u00d7 {s.Height}");
        Console.WriteLine($"  Samples/px:  {s.Samples}");
        Console.WriteLine($"  Max depth:   {s.Depth} (diffuse {s.Depths.Diffuse}, glossy {s.Depths.Glossy}, " +
                          $"transmission {s.Depths.Transmission}, volume {s.Depths.Volume}, cut-outs {s.Depths.Transparent})");
        if (s.ShadowSamplesOverride.HasValue)
            Console.WriteLine($"  Shadow smp:  {s.ShadowSamplesOverride.Value} (override)");
        if (s.ReferenceMode)
            Console.WriteLine("  Reference:   on (no clamps, no transparent shadows, uniform depths, no denoiser)");
        if (s.Seed != 0)
            Console.WriteLine($"  Seed:        {s.Seed}");
        if (s.Integrator != IntegratorKind.Path)
            Console.WriteLine($"  Integrator:  {s.Integrator.ToString().ToLowerInvariant()}" +
                              (s.Integrator == IntegratorKind.Vcm ? $" (radius {s.VcmRadius ?? 0.003f:0.####} of the scene radius)" : ""));
        if (s.Adaptive)
            Console.WriteLine($"  Adaptive:    threshold {s.AdaptiveThreshold:0.####}, " +
                              $"at least {new AdaptiveSettings { Threshold = s.AdaptiveThreshold, MinSamples = s.AdaptiveMinSpp }.ResolveMinSamples(s.Samples)} spp");
        if (s.Guiding)
            Console.WriteLine($"  Guiding:     on (training {s.GuidingTraining ?? GuidingSettings.DefaultTraining(s.Samples)} of the {s.Samples} spp)");
        if (s.TransparentShadows)
            Console.WriteLine("  Shadows:     transparent through glass (approximation, refracted caustics off)");
        if (s.ClampOverride.HasValue)
        {
            Console.WriteLine($"  Clamp:       {s.ClampOverride.Value} (biased: caps every brighter sample)");
            Console.WriteLine($"  Indir.clamp: ×{s.IndirectClampFactor:F2} ({s.ClampOverride.Value * s.IndirectClampFactor:F1} effective)");
        }
        if (s.ExposureEv != Renderer.DefaultExposureEv)
            Console.WriteLine($"  Exposure:    {s.ExposureEv:+0.0;-0.0;0.0} EV (×{MathF.Pow(2f, s.ExposureEv):F2})");
        if (s.CameraSelector != null)
            Console.WriteLine($"  Camera:      {s.CameraSelector}");
        Console.WriteLine($"  Sampler:     {s.SamplerKind.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  MIS:         {s.MisHeuristic.ToString().ToLowerInvariant()} heuristic");
        if (s.LightSampling is not (LightSamplingStrategy.All or LightSamplingStrategy.Auto))
            Console.WriteLine($"  Light pick:  {s.LightSampling.ToString().ToLowerInvariant()}");
        if (s.SssMode != SssMode.Auto)
            Console.WriteLine($"  SSS mode:    {s.SssMode.ToString().ToLowerInvariant()}");
        if (s.DenoiserKind != DenoiserKind.None)
            Console.WriteLine($"  Denoiser:    {s.DenoiserKind.ToString().ToLowerInvariant()} ({s.DenoiseQuality.ToString().ToLowerInvariant()})");
        // A `.exr` output switches the main image to scene-linear HDR (no
        // exposure, view transform or sRGB) and absorbs the AOVs as layers.
        if (s.ExrOutput)
            Console.WriteLine("  HDR output:  linear EXR (pre-tone-mapping)");
        if (s.Aovs.Count > 0)
            Console.WriteLine($"  AOVs:        {string.Join(", ", s.Aovs)} ({(s.EmbedAovs ? "EXR layers" : (s.AovFormat ?? "pfm") + " files")})");
        Console.WriteLine();
    }

    /// <summary>Loads the scene, renders it, denoises it and writes the outputs.</summary>
    static void RenderAndSave(RunSettings s, SceneLoader.ParsedScene scene)
    {
        Console.Write("  Loading scene... ");
        var sw = Stopwatch.StartNew();
        try
        {
            var (world, camera, lights, sky, globalMedium, motionBlur) =
                SceneLoader.Load(scene, s.Width, s.Height, s.ShadowSamplesOverride, s.CameraSelector);

            Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");
            SceneLoader.FlushMessages();
            Console.WriteLine($"  Lights:      {lights.Count}");
            Console.WriteLine($"  Sky:         {DescribeSky(sky)}");
            if (motionBlur.Active)
                Console.WriteLine($"  Motion blur: shutter [{motionBlur.ShutterOpen:0.##}, {motionBlur.ShutterClose:0.##}]");
            float? whiteBalanceKelvin = s.WhiteBalanceArg ?? camera.WhiteBalanceKelvin;
            float exposureEv = s.ExposureEv + (camera.PhysicalExposureEv ?? 0f);
            Console.WriteLine($"  View:        {s.View.ToString().ToLowerInvariant()}{(s.Look != ViewLook.None ? $" ({s.Look.ToString().ToLowerInvariant()})" : "")}, filter {s.PixelFilterKind.ToString().ToLowerInvariant()}" +
                              (whiteBalanceKelvin is float k ? $", white balance {k:0} K" : ""));

            var renderer = new Renderer(
                world, camera, lights, sky, s.Samples, s.Depth, globalMedium,
                s.ClampOverride, s.Verbose, s.MisHeuristic, s.LightSampling,
                s.IndirectClampFactor, s.TextureFiltering, exposureEv,
                s.SssMode,
                motionBlur: motionBlur,
                referenceMode: s.ReferenceMode, seed: s.Seed,
                transparentShadows: s.TransparentShadows,
                pixelFilter: PixelFilter.Create(s.PixelFilterKind),
                displayTransform: new DisplayTransform(s.View, s.Look,
                    whiteBalanceKelvin is float wbk ? WhiteBalance.Matrix(wbk) : null),
                depths: s.Depths);
            renderer.Integrator = s.Integrator;
            if (s.VcmRadius is float vr) renderer.VcmRadiusFactor = vr;
            if (s.Adaptive)
                renderer.EnableAdaptive(new AdaptiveSettings { Threshold = s.AdaptiveThreshold, MinSamples = s.AdaptiveMinSpp });
            if (s.Guiding)
                renderer.EnableGuiding(new GuidingSettings
                {
                    TrainingSpp = s.GuidingTraining ?? GuidingSettings.DefaultTraining(s.Samples),
                });
            Console.WriteLine();

            // Linear capture: everything for the denoiser and the AOVs, the
            // beauty and its halves for an EXR output, nothing otherwise
            // (the tone-mapped pixels are identical either way).
            var captureOptions = s.DenoiserKind != DenoiserKind.None || s.Aovs.Count > 0
                ? RenderCaptureOptions.Full
                : s.ExrOutput
                    ? new RenderCaptureOptions { CaptureBeautyHalves = true }
                    : RenderCaptureOptions.None;

            sw.Restart();
            var result = renderer.Render(s.Width, s.Height, captureOptions);
            var pixels = result.Pixels;
            Console.WriteLine($"  Render time: {FormatElapsed(sw.Elapsed)}");
            if (renderer.LastRenderMeanSamples > 0)
                Console.WriteLine($"  Samples:     {renderer.LastRenderMeanSamples:F1} spp on average (adaptive, budget {s.Samples})");
            if (renderer.LastRenderNonFiniteCount > 0)
                Console.WriteLine($"  Warning:     {renderer.LastRenderNonFiniteCount:N0} NaN/Inf radiance values were zeroed (engine bug)");

            // The denoiser works on the linear beauty; the display pixels are
            // re-derived from its output with the same view transform.
            FrameBuffer? denoisedBeauty = null;
            if (s.DenoiserKind != DenoiserKind.None)
            {
                var denoiseOptions = new DenoiserOptions { Kind = s.DenoiserKind, Quality = s.DenoiseQuality };
                sw.Restart();
                denoisedBeauty = Denoiser.Denoise(result.Buffers!, denoiseOptions);
                pixels = renderer.ToneMapToDisplay(denoisedBeauty);
                Console.WriteLine($"  Denoise time: {FormatElapsed(sw.Elapsed)}");
            }
            SaveOutputs(s, result, pixels, denoisedBeauty);
        }
        catch (Exception ex)
        {
            Console.WriteLine("failed!");
            Console.WriteLine($"Error loading or rendering scene: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    static string DescribeSky(SkySettings sky) => sky.Mode switch
    {
        SkySettings.SkyMode.Hdri     => "HDRI environment map" + (sky.HasSun ? " + extracted sun" : ""),
        SkySettings.SkyMode.Gradient => "gradient" + (sky.HasSun ? " + sun disk" : ""),
        SkySettings.SkyMode.Physical => sky.LightingModel switch
        {
            RayTracer.Rendering.Sky.NishitaSky  => "physical (nishita)" + (sky.HasSun ? " + sun disk" : ""),
            RayTracer.Rendering.Sky.PreethamSky => "physical (preetham)" + (sky.HasSun ? " + sun disk" : ""),
            RayTracer.Rendering.Sky.HosekWilkieSky => "physical (hosek-wilkie)" + (sky.HasSun ? " + sun disk" : ""),
            _                                   => "physical" + (sky.HasSun ? " + sun disk" : "")
        },
        _                            => "flat"
    };

    /// <summary>
    /// Returns the value of a CLI argument (the token following the flag).
    /// </summary>
    /// <summary>The value of a flag; the last occurrence wins (the command
    /// line comes after the scene's render block).</summary>
    static string? GetArg(string[] args, string longName, string? shortName)
    {
        string? value = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == longName || (shortName != null && args[i] == shortName))
                value = args[i + 1];
        }
        return value;
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
