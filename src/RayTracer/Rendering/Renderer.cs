using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

/// <summary>
/// MIS combination strategy used by the renderer. <see cref="Balance"/> is
/// the unbiased one-sample estimator from Veach 1997 §9.2.2: w(p) = p/(p+q).
/// <see cref="Power"/> uses the β=2 power heuristic (§9.2.4) which
/// suppresses the contribution of the worse-matched sampler more
/// aggressively — typically reduces variance for highly asymmetric pairings
/// (small specular light + rough diffuse material, or vice-versa) at no
/// extra cost.
/// </summary>
public enum MisHeuristic
{
    Balance,
    Power
}

/// <summary>
/// NEE light selection strategy.
///
/// <list type="bullet">
///   <item><description><b>All</b> — iterate over every light and sum contributions
///   (original behaviour, default for backward compatibility). O(N·S) per shading point.</description></item>
///   <item><description><b>Power</b> — sample one light per NEE event with probability
///   proportional to its <see cref="ILight.ApproximatePower"/>. Reduces variance
///   significantly in scenes with many lights of mixed brightnesses (PBRT §16.3.2).
///   O(S) per shading point after O(N) construction.</description></item>
///   <item><description><b>Uniform</b> — sample one light uniformly at random. Useful
///   as a baseline/debug comparison against <c>Power</c>.</description></item>
///   <item><description><b>Bvh</b> — sample one light per NEE event through a light
///   BVH (<see cref="LightBvh"/>): the probability follows what each light can
///   deliver at the shading point (power, distance, orientation). O(log N);
///   the strategy for scenes with many lights.</description></item>
///   <item><description><b>Auto</b> — <b>All</b> up to <see cref="Renderer.AutoBvhLightCount"/>
///   lights, <b>Bvh</b> beyond.</description></item>
/// </list>
/// </summary>
public enum LightSamplingStrategy
{
    All,
    Power,
    Uniform,
    Bvh,
    Auto,
}

/// <summary>
/// SSS dispatch mode. Drives whether refraction events into geometry bound to
/// a scattering <see cref="HomogeneousMedium"/> activate the random-walk
/// integrator (<see cref="Auto"/>) or fall through to the existing Beer-Lambert
/// volumetric path (<see cref="Off"/>).
/// <para><see cref="Auto"/> is the default — SSS is correctness, not an extra
/// optional. <see cref="Off"/> is offered as a preview / A/B comparison knob
/// (Phase 4 CLI: <c>--sss-mode</c>).</para>
/// </summary>
public enum SssMode
{
    Auto,
    Off,
}


public partial class Renderer
{
    private readonly IHittable _world;
    private readonly Camera.Camera _camera;
    private readonly List<ILight> _lights;
    private readonly SkySettings _sky;
    private readonly int _maxDepth;
    private readonly int _samplesPerPixel;
    private readonly LightMisTable _mis;
    private readonly bool _hasEquiangularLights;
    private readonly EnvironmentLight? _envLight;
    private readonly IMedium? _globalMedium;
    private readonly bool _verbose;
    private readonly MisHeuristic _misHeuristic;
    private readonly LightSamplingStrategy _lightSamplingStrategy;
    private readonly LightDistribution? _lightDist; // non-null for power / uniform

    /// <summary>Above this many lights, <see cref="LightSamplingStrategy.Auto"/> uses the light BVH.</summary>
    public const int AutoBvhLightCount = 16;

    // ── Caustics (photon mapping) ────────────────────────────────────────────
    // Built once in the constructor from a photon-emission pre-pass when
    // --caustics is on. Null (and _causticsActive false) otherwise, so every
    // non-caustic scene pays exactly zero overhead and is bit-identical to off.
    private readonly PhotonMap? _photonMap;
    private readonly bool  _causticsActive;
    private readonly float _causticGatherRadius;   // gather radius cap (grid cell = half of this)
    private const    int   CausticGatherK = 40;     // k nearest photons per density estimate
    // Cone-filter constant k_f (PBRT §16.2): photons are weighted 1 at the gather
    // point and fall to 0 at the kernel edge, so a lone photon reads as a soft
    // glow instead of a hard-edged flat disc. CausticConeNorm = 1 − 2/(3·k_f) is
    // the matching density normalisation that keeps the estimate energy-consistent
    // with the flat kernel in the dense (focused-caustic) limit.
    private const    float CausticConeK    = 1f;
    private const    float CausticConeNorm = 1f - 2f / (3f * CausticConeK);  // = 1/3

    // Caustic-chain state threaded through the eye path so the photon gather and
    // the BSDF-sampled emission are counted exactly once. None: no non-delta
    // bounce yet (camera / pure-specular-from-camera). Diffuse: last bounce was
    // non-delta. Suppress: a specular chain followed a diffuse vertex — that
    // L S+ D transport is owned by the photon gather, so its mirror BSDF path
    // (D S+ L emission) is suppressed here.
    private const int CausticNone = 0, CausticDiffuse = 1, CausticSuppress = 2;
    // Bit on top of the state: every event of the specular chain since the
    // last diffuse vertex was a transmission a shadow ray also crosses (straight
    // transmission always, any transmission with transparent shadows). Such a
    // chain ending on a light is exactly what the light sample already
    // delivered through the same surfaces, so its emission is dropped.
    private const int CausticStateMask = 3, ChainShadowPassable = 4;

    // ── Transparent shadows (opt-in, PIANO-REVIEW.md §3.21) ─────────────────
    // Off: surfaces are opaque to NEE shadow rays, except straight transmission
    // (IOR-1 shells, thin sheets: IMaterial.TransmitsStraight), and light
    // through glass arrives only along the BSDF paths that actually cross it
    // (the unbiased, physically correct default; caustics included, noisy).
    // On: shadow rays
    // pass straight through glass, tinted by its Fresnel transmittance, a
    // cheap and noise-free stand-in for the refracted light. To keep that
    // approximation from adding to the real transport (the old default,
    // which wiped out the shadow of a glass sphere), the BSDF paths
    // "diffuse → through glass → light" are suppressed and a light sample
    // that crossed glass takes the full MIS weight: one estimator per
    // transport, as production renderers do with caustics disabled.
    private readonly bool _transparentShadows;

    // Scene radius the ray offsets scale with (MathUtils.SceneScale), set on
    // every worker thread before it traces.
    private readonly float _sceneScale;

    // ── Path depths ─────────────────────────────────────────────────────────
    // A path may have at most _maxDepth vertices, and at most so many
    // bounces of each kind (PathDepths): glass needs many transmission
    // bounces, diffuse interreflection few. Cut-outs do not count against
    // the total, only against their own limit. Russian roulette (unbiased)
    // ends most paths long before any limit (SurviveRoulette).
    private readonly PathDepths _depths;

    // ── Reference mode (--reference) ────────────────────────────────────────
    // The "pure" estimator used to validate the engine against an external
    // reference renderer: every deliberate bias is switched off (per-sample
    // and indirect clamps, straight-through transparent shadow rays, the
    // prefiltered HDRI for BSDF rays). Russian roulette stays on: it is
    // unbiased, it only trades variance for speed. The CLI gives a reference
    // render one depth for every kind of bounce and refuses the denoiser and
    // photon caustics.
    private readonly bool _referenceMode;

    // Extra seed mixed into every pixel's sample sequence. 0 keeps the
    // historical sequences (bit-identical output); any other value gives a
    // statistically independent render of the same scene, which is what the
    // unbiasedness test averages.
    private readonly uint _seedMix;

    // Non-finite (NaN/Inf) radiance values met and zeroed by SanitizeRadiance.
    // Zeroing hides the symptom; the count keeps the bug visible. Static
    // because SanitizeRadiance is: renders running concurrently in the same
    // process (unit tests) share it, which is fine for a diagnostic.
    private static long s_nonFiniteCount;

    /// <summary>NaN/Inf radiance values zeroed during the last <see cref="Render(int,int,RenderCaptureOptions)"/>
    /// (process-wide: includes any render running at the same time).</summary>
    public long LastRenderNonFiniteCount { get; private set; }

    // ── Firefly suppression (opt-in) ────────────────────────────────────────
    // Maximum per-sample radiance (before tone mapping), enabled only when a
    // value is passed (CLI flag --clamp/-C). Off by default: the threshold is
    // an absolute radiance, so it does not single out outliers but cuts every
    // sample brighter than it, and a scene whose lights are brighter than the
    // threshold loses real energy (Cornell box with a radiance-20 panel: -22%,
    // PIANO-REVIEW.md §3.20). Production renderers ship it disabled too.
    // SuggestedMaxSampleRadiance is the value the docs propose as a starting
    // point when fireflies must be traded for bias.
    public const float SuggestedMaxSampleRadiance = 10f;
    private readonly float _maxSampleRadiance;

    // ── Depth-aware indirect firefly clamp ──────────────────────────────────
    // A second (typically tighter) clamp is applied to the indirect (bounce ≥ 1)
    // contribution (what arrives through the first vertex's bounce), mirroring the
    // Cycles/Arnold "indirect clamp" feature. It scales the primary clamp, so
    // it is active only together with it: 0.25 → indirect clamp = 2.5 with
    // --clamp 10, targeting caustic / specular-chain fireflies that survive
    // the primary clamp. Set to 1.0 to disable the extra suppression.
    //
    // CLI: --indirect-clamp-factor <f>   (e.g. 1.0 = same as primary clamp)
    public const float DefaultIndirectClampFactor = 0.25f;
    private readonly float _indirectMaxSampleRadiance;

    // Clamp used on the world AABB when no finite geometry exists. InfinitePlane
    // reports a ±1e6 AABB (its "fake finite box" for BVH compatibility); the
    // scene radius (ray offsets, caustic gather radius, light power of the
    // directional and environment lights) would follow it. ±1e3 keeps a
    // realistic finite extent.
    private const float SceneBoundsClamp = 1.0e3f;

    /// <summary>
    /// Texture filtering modes — picks whether the renderer emits ray
    /// differentials on primary camera rays so analytic-filtering textures
    /// (Perlin/fBm octave clamp, Worley supersampling, ImageTexture mipmap)
    /// can pre-integrate over their footprint.
    /// </summary>
    public enum TextureFilteringMode { Auto, On, Off }

    private readonly bool _emitRayDifferentials;

    /// <summary>Smallest share of the pixel a camera ray's differentials cover (PBRT-v4).</summary>
    internal const float DifferentialFloor = 1f / 8f;

    // ── Exposure (photographic stops, EV) ──────────────────────────────────
    // Linear multiplier applied to each pixel BEFORE the view transform.
    // Matches the "Exposure" / "Camera exposure" knob found in Arnold
    // (`exposure` attribute), Cycles ("Film → Exposure") and RenderMan
    // (`exposure` on the display filter). Without this control, scenes lit
    // above ~1.0 irradiance land on the shoulder of the tone curve where
    // procedural textures look uniformly bright; with it, the artist slides
    // the linear gain back to where textural variation reads properly.
    //
    // EV semantics: factor = 2^EV. EV = 0 (default) is identity; negative EV
    // darkens, positive brightens. Typical studio compensation for an
    // over-exposed setup is EV = -1 to -2.
    public const float DefaultExposureEv = 0f;
    private readonly float _exposureScale;

    // Pixel reconstruction filter, applied by filter importance sampling: it
    // only moves each camera sample inside the filter's footprint.
    private readonly PixelFilter _pixelFilter;

    // Exposure-scaled linear radiance → display-encoded pixel (view transform).
    private readonly DisplayTransform _display;

    // ── Random-walk subsurface scattering ──────────────────────────────────
    // Dispatched from the path loop when an entry refraction lands on an
    // entity bound to a HomogeneousMedium with σ_s > 0. Off mode lets the
    // refraction fall through to the existing Beer-Lambert path (the medium
    // still tracks absorption, but does not scatter — useful for A/B testing
    // the lit appearance and for fast previews of scene composition before
    // the full walk cost). See RandomWalkSss.cs for the integrator.
    private readonly SssMode _sssMode;

    // ── Motion blur ─────────────────────────────────────────────────────────
    // When active, each camera sample draws ONE extra sampler dimension (right
    // after the pixel jitter, before the lens) mapped to [shutterOpen,
    // shutterClose]; the whole path then samples the scene at that frozen
    // instant. When inactive no dimension is drawn — output stays bit-identical
    // to a build without motion blur (same invariant as volumetrics/AOV capture).
    private readonly bool _motionActive;
    private readonly float _shutterOpen;
    private readonly float _shutterClose;

    public Renderer(
        IHittable world,
        Camera.Camera camera,
        List<ILight> lights,
        SkySettings sky,
        int samplesPerPixel,
        int maxDepth,
        IMedium? globalMedium = null,
        float? maxSampleRadiance = null,
        bool verbose = false,
        MisHeuristic misHeuristic = MisHeuristic.Balance,
        LightSamplingStrategy lightSamplingStrategy = LightSamplingStrategy.All,
        float indirectClampFactor = DefaultIndirectClampFactor,
        TextureFilteringMode textureFiltering = TextureFilteringMode.Auto,
        float exposureEv = DefaultExposureEv,
        SssMode sssMode = SssMode.Auto,
        bool enableCaustics = false,
        int causticPhotons = 0,
        MotionBlurSettings motionBlur = default,
        bool referenceMode = false,
        int seed = 0,
        bool transparentShadows = false,
        PixelFilter? pixelFilter = null,
        DisplayTransform? displayTransform = null,
        PathDepths? depths = null)
    {
        _depths = depths ?? PathDepths.Uniform(maxDepth);
        _pixelFilter = pixelFilter ?? PixelFilter.Box;
        _display = displayTransform ?? DisplayTransform.Legacy;
        _referenceMode = referenceMode;
        _transparentShadows = transparentShadows && !referenceMode;
        _seedMix = seed == 0 ? 0u : MixSeed((uint)seed);
        _world = world;
        _motionActive = motionBlur.Active;
        _shutterOpen = motionBlur.ShutterOpen;
        _shutterClose = motionBlur.ShutterClose;
        _camera = camera;
        _lights = lights;
        _sky = sky;
        _samplesPerPixel = samplesPerPixel;
        _maxDepth = maxDepth;
        _globalMedium = globalMedium;
        _maxSampleRadiance = referenceMode
            ? float.PositiveInfinity
            : maxSampleRadiance ?? float.PositiveInfinity;
        // Without a primary clamp there is no indirect one either (and ∞ · 0
        // would be NaN).
        _indirectMaxSampleRadiance = float.IsPositiveInfinity(_maxSampleRadiance)
            ? float.PositiveInfinity
            : _maxSampleRadiance * MathF.Max(0f, indirectClampFactor);
        _exposureScale = MathF.Pow(2f, exposureEv);
        _sssMode = sssMode;
        _verbose = verbose;
        _misHeuristic = misHeuristic;
        _lightSamplingStrategy = lightSamplingStrategy;

        // Texture-filtering / ray-differential toggle. Auto = on (the analytic
        // filter is back-compat with every texture that doesn't override the
        // footprint-aware ITexture.Value overload, so there's no reason to
        // ship "auto" as anything other than on by default).
        _emitRayDifferentials = textureFiltering switch
        {
            TextureFilteringMode.Off => false,
            _                        => true,
        };

        // The finite part of the scene (an infinite plane's sentinel box left
        // out): its radius scales the ray offsets and the caustic gather.
        AABB sceneBounds = ComputeFiniteSceneBounds(world);
        _finiteSceneBounds = sceneBounds;
        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        float sceneRadius = MathF.Max(0.5f * extent.Length(), 1e-3f);
        _sceneScale = sceneRadius;

        // ── Caustics: emit photons and build the caustic map ─────────────────
        // The gather radius and grid cell scale with the scene so the estimate
        // is resolution-independent. Build returns null when no caustic photon
        // is deposited (no specular caster in the scene) — then the gather is a
        // no-op and the render is identical to --caustics off.
        if (enableCaustics && causticPhotons > 0 && lights.Count > 0)
        {
            // The cap (~6% of the scene radius) bounds the search; the
            // *effective* kernel radius is adaptive (the k-th nearest photon),
            // so the focal spot stays sharp and bright. The grid cell is half
            // the cap: the shell search probes more (cheap, mostly-empty)
            // buckets in sparse regions, but inside a dense focal cluster the
            // nearest-first traversal + cell pruning touch ~an order of
            // magnitude fewer photons than radius-sized cells would.
            _causticGatherRadius = MathF.Max(sceneRadius * 0.06f, 1e-4f);
            // With motion blur active the photon map is a static snapshot at
            // the shutter midpoint (see CausticPhotonTracer.Build docs).
            float photonTime = _motionActive ? motionBlur.MidTime : 0f;
            if (_motionActive)
                Console.WriteLine("  Caustics:    photons traced at shutter midpoint (motion blur active)");
            _photonMap = CausticPhotonTracer.Build(world, lights, sceneBounds, causticPhotons,
                                                   _causticGatherRadius * 0.5f, photonTime);
            _causticsActive = _photonMap != null;
            if (_causticsActive)
                Console.WriteLine($"  Caustics:    photon map ({_photonMap!.Count:N0} caustic photons)");
            else
                Console.WriteLine("  Caustics:    on (no caustic photons deposited — no specular casters)");
        }

        _envLight = lights.OfType<EnvironmentLight>().FirstOrDefault();

        // Light importance sampling: build CDF for power/uniform strategies.
        // The 'all' strategy skips this — it sums every light directly.
        if (lightSamplingStrategy == LightSamplingStrategy.Auto)
            lightSamplingStrategy = lights.Count > AutoBvhLightCount ? LightSamplingStrategy.Bvh : LightSamplingStrategy.All;
        LightBvh? lightBvh = null;
        if (lightSamplingStrategy is LightSamplingStrategy.Power or LightSamplingStrategy.Uniform && lights.Count > 0)
        {
            bool forceUniform = lightSamplingStrategy == LightSamplingStrategy.Uniform;
            _lightDist = new LightDistribution(lights, sceneBounds, forceUniform);
        }
        else if (lightSamplingStrategy == LightSamplingStrategy.Bvh && lights.Count > 0)
            lightBvh = new LightBvh(lights, sceneBounds);
        _mis = new LightMisTable(lights, _lightDist, lightBvh);
        _hasEquiangularLights = lights.Any(l => EquiangularLight(l, out _));

        // MIS compensation of the environment map assumes every light sample
        // shares its weight with a BSDF sample; straight-through transparent
        // shadows let light samples own the transport behind glass.
        if (sky.LightingModel is Sky.HdriSky hdri)
            hdri.Map.UseMisCompensation = !_transparentShadows;
    }

    // Per-axis extent above which an AABB is treated as an "infinite" sentinel
    // and excluded from the finite-scene-bounds union. InfinitePlane reports
    // ±1e6 per-axis (a BVH-compatibility dummy); real geometry never
    // approaches this in practice.
    private const float InfiniteExtentSentinel = 1.0e5f;

    /// <summary>
    /// Computes the AABB of the *finite* portion of the world for scene-scale
    /// normalisation. Walks the top-level children of a <see cref="HittableList"/>
    /// and excludes any whose bounding box has an axis extent above
    /// <see cref="InfiniteExtentSentinel"/> — this filters out
    /// <see cref="InfinitePlane"/> and its wrappers without peeking inside
    /// BVH-acceleration structures, whose union bbox is already bounded by
    /// their finite contents.
    ///
    /// Fallback: if no finite child exists (pure infinite-plane scenes or
    /// unit-test stubs), returns the raw world bbox clamped to
    /// <see cref="SceneBoundsClamp"/> along each axis. This keeps the
    /// normalisation denominator finite and produces a realistic R for
    /// environment/directional flux.
    /// </summary>
    private static AABB ComputeFiniteSceneBounds(IHittable world)
    {
        if (world is HittableList list && list.Objects.Count > 0)
        {
            AABB acc = AABB.Empty;
            bool any = false;
            for (int i = 0; i < list.Objects.Count; i++)
            {
                var b = list.Objects[i].BoundingBox();
                Vector3 d = b.Max - b.Min;
                if (d.X < InfiniteExtentSentinel &&
                    d.Y < InfiniteExtentSentinel &&
                    d.Z < InfiniteExtentSentinel)
                {
                    acc = any ? AABB.SurroundingBox(acc, b) : b;
                    any = true;
                }
            }
            if (any) return acc;
        }

        var raw = world.BoundingBox();
        Vector3 v = new(SceneBoundsClamp);
        return new AABB(
            Vector3.Max(raw.Min, -v),
            Vector3.Min(raw.Max,  v));
    }

    /// <summary>
    /// Renders the scene using stratified (jittered) sampling for superior anti-aliasing.
    /// Parallel over 16×16 tiles for multi-core rendering.
    /// </summary>
    public Vector3[,] Render(int width, int height)
        => Render(width, height, RenderCaptureOptions.None).Pixels;

    /// <summary>
    /// Render overload that can additionally capture linear-HDR beauty (with
    /// even/odd half-sample splits) and first-non-delta-hit AOVs
    /// (albedo/normal/depth) for the denoiser and PFM output.
    ///
    /// Invariant: the tone-mapped <see cref="RenderResult.Pixels"/> are
    /// bit-identical to the legacy overload regardless of
    /// <paramref name="options"/> — the capture adds accumulators next to the
    /// existing ones (same summation order, no extra RNG draws) and only ever
    /// writes to the side buffers.
    /// </summary>
    public RenderResult Render(int width, int height, RenderCaptureOptions options)
    {
        var pixels = new Vector3[height, width];
        // Path guiding learns before the render, on its own sample sequences.
        if (_guide is { CanSample: false })
            TrainGuide(width, height);
        _captureAovs = options.CaptureAovs;
        Interlocked.Exchange(ref s_nonFiniteCount, 0);

        // Tile-based work partitioning: 16×16 tiles balance load far better than
        // whole scanlines (sky rows are cheap, geometry-dense centre rows
        // expensive) and keep each worker on a cache-local pixel block.
        const int TileSize = 16;
        int tilesX = (width  + TileSize - 1) / TileSize;
        int tilesY = (height + TileSize - 1) / TileSize;
        int totalTiles = tilesX * tilesY;
        int completedTiles = 0;

        // Two pixel-sampling strategies share the same TracePath core:
        //
        //   • PRNG  — needs external sqrt(spp) × sqrt(spp) jittered
        //              stratification to claw back anti-aliasing the
        //              independent random draws don't give for free.
        //
        //   • Sobol — dim 0/1 already form a (0,2,2)-net at every
        //              power-of-two prefix, which natively places the spp
        //              points in a perfectly stratified sqrt(spp)²-cell
        //              grid on the pixel. Stacking the external sx/sy
        //              stratification on top of that double-stratifies
        //              the same pixel: it crams every Sobol point into a
        //              cell whose index is unrelated to the cell the
        //              point would have landed in naturally, destroying
        //              the joint 2D stratification structure. Empirically
        //              this leaks ~20% of a stop's worth of variance on
        //              the Cornell box at -s 64 — Sobol ends up noisier
        //              than PRNG, the exact opposite of what shipping the
        //              sampler is supposed to buy.
        bool useSobol = Sampler.Kind == SamplerKind.Sobol;
        // With path guiding, the training passes are part of the budget.
        int renderSpp = Math.Max(1, _samplesPerPixel - _guideTrainCount);
        int sqrtSpp = (int)MathF.Ceiling(MathF.Sqrt(renderSpp));
        float invSqrtSpp = 1f / sqrtSpp;
        int actualSamples = useSobol ? renderSpp : sqrtSpp * sqrtSpp;

        // Path-guiding training passes are unbiased estimates of the same
        // pixels: they join the render's (CombineGuidedPasses), with their own
        // sample indices first. The render's own sums are kept per pixel for that.
        int trained = _guideTrainCount;
        RenderBuffers? buffers = options.Any
            ? new RenderBuffers(width, height, actualSamples + trained, options.CaptureAovs)
            : null;
        GuidedPassSums? finalPass = trained > 0 ? new GuidedPassSums(width, height) : null;
        bool captureAovs = _captureAovs;

        // Progress is printed by a single dedicated reporter thread, so no worker
        // ever touches the Console lock (which used to serialise the render
        // threads at every scanline boundary).
        // Only re-print when the tile count actually changed: on a TTY the \r
        // overwrite hides repeats, but on redirected output (CI logs, GitHub
        // Actions) every Write becomes a new line, so an unchanged counter
        // would flood the log with identical lines.
        using var renderDone = new ManualResetEventSlim(false);
        int lastReported = -1;
        var reporter = Task.Run(() =>
        {
            while (!renderDone.Wait(100))
            {
                int done = Volatile.Read(ref completedTiles);
                if (done == lastReported) continue;
                lastReported = done;
                Console.Write($"\r  Rendering: {100f * done / totalTiles:F1}% ({done}/{totalTiles} tiles)   ");
            }
        });

        Parallel.For(0, totalTiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, tile =>
        {
            // Per-worker switches, set before any ray and every tile so they
            // never leak across renders: with photon caustics active, smooth
            // specular transmission is opaque to NEE shadow rays (the photon
            // map owns that refracted light); otherwise shadow rays are opaque
            // to glass unless transparent shadows were asked for, so light
            // through glass arrives only along BSDF-sampled paths, the unbiased
            // estimator (rough glass stays a transparent shadow under caustics).
            PrepareWorker(tile, useSobol);

            int tileX = tile % tilesX;
            int tileY = tile / tilesX;
            int x0 = tileX * TileSize;
            int y0 = tileY * TileSize;
            int x1 = Math.Min(x0 + TileSize, width);
            int y1 = Math.Min(y0 + TileSize, height);

            for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
            {
                Vector3 cumulativeColor = Vector3.Zero;
                // Side accumulators for the optional capture (dual-buffer
                // halves + AOVs). The legacy cumulativeColor sum above stays
                // untouched so the default output is bit-identical.
                CaptureAccumulator capAcc = default;

                // Per-pixel scramble seed: combine pixel coordinates so each
                // pixel walks an independent Owen-scrambled sequence. Without
                // this, every pixel would start from the same Sobol prefix
                // and the resulting Moiré would be more visible than plain
                // PRNG noise — the very failure mode Owen scrambling exists
                // to fix.
                uint pixelSeed = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ _seedMix;

                // Pixel-size deltas used to build the differential rays.
                // Scaled by 1/sqrt(spp): the samples of one pixel already
                // average over its area, so each needs only its share of the
                // footprint. Floored at 1/8 (PBRT-v4): below that the filter
                // would drop under an eighth of a pixel, where the texture
                // aliases between samples and the finest mip level thrashes
                // the cache for no visible gain.
                float diffShrink = MathF.Max(DifferentialFloor, 1f / sqrtSpp);
                float dsdx = diffShrink / width;
                float dtdy = diffShrink / height;

                if (useSobol)
                {
                    for (int s = 0; s < actualSamples; s++)
                    {
                        // After the path-guiding training passes, if any (they
                        // took the sequence's first samples).
                        Sampler.BeginPixelSample(pixelSeed, (uint)(trained + s));

                        // Sobol(2D) places the camera-jitter point natively:
                        // the (0,2,2)-net property covers the pixel as a
                        // stratified grid at every power-of-two prefix, and the
                        // filter maps each axis monotonically. The shutter time
                        // (motion blur only) comes after the jitter and before
                        // the lens, so dims 0-1 keep their pairing.
                        var ray = CameraSample(i, j, width, height, dsdx, dtdy);
                        if (captureAovs) (t_aovProbe ??= new AovProbe()).Reset();
                        Vector3 sample = TracePath(ray);

                        Sampler.EndPixelSample();

                        sample = ClampRadiance(sample);
                        cumulativeColor += sample;
                        if (buffers != null) capAcc.Add(sample, trained + s, captureAovs);
                        finalPass?.Add(i, j, trained + s, sample);
                    }
                }
                else
                {
                    // Stratified sampling: divide pixel into sqrtSpp x sqrtSpp grid.
                    for (int sy = 0; sy < sqrtSpp; sy++)
                    {
                        for (int sx = 0; sx < sqrtSpp; sx++)
                        {
                            float jitterU = (sx + MathUtils.RandomFloat()) * invSqrtSpp;
                            float jitterV = (sy + MathUtils.RandomFloat()) * invSqrtSpp;

                            // Same shutter-time draw as the Sobol path: only
                            // when motion is active (bit-identical otherwise).
                            float rayTime = _motionActive
                                ? _shutterOpen + (_shutterClose - _shutterOpen) * MathUtils.RandomFloat()
                                : 0f;

                            float u = (i + _pixelFilter.SamplePosition(jitterU)) / width;
                            float v = (height - j - 1 + _pixelFilter.SamplePosition(jitterV)) / height;

                            var ray = _emitRayDifferentials
                                ? _camera.GetRayWithDifferentials(u, v, dsdx, dtdy, rayTime)
                                : _camera.GetRay(u, v, rayTime);
                            if (captureAovs) (t_aovProbe ??= new AovProbe()).Reset();
                            Vector3 sample = TracePath(ray);

                            sample = ClampRadiance(sample);
                            cumulativeColor += sample;
                            if (buffers != null) capAcc.Add(sample, trained + sy * sqrtSpp + sx, captureAovs);
                            finalPass?.Add(i, j, trained + sy * sqrtSpp + sx, sample);
                        }
                    }
                }

                Vector3 linearColor = cumulativeColor / actualSamples;
                // Apply the photographic exposure (linear gain = 2^EV) BEFORE
                // the view transform, so the artist's EV slides the scene
                // along the tone curve instead of rescaling display values.
                pixels[j, i] = _display.Apply(linearColor * _exposureScale);
                if (buffers != null)
                    capAcc.Store(buffers, i, j, linearColor, actualSamples + trained);
            }

            // Pool threads outlive the render: never leave reference mode or
            // this scene's ray offsets on for whatever else runs here next.
            ReleaseWorker();
            Interlocked.Increment(ref completedTiles);
        });

        renderDone.Set();
        reporter.Wait();
        if (lastReported != totalTiles)
            Console.Write($"\r  Rendering: 100.0% ({totalTiles}/{totalTiles} tiles)   ");
        Console.WriteLine();
        if (finalPass != null)
            CombineGuidedPasses(finalPass, actualSamples, trained, pixels, buffers);
        _captureAovs = false;
        LastRenderNonFiniteCount = Interlocked.Read(ref s_nonFiniteCount);
        return new RenderResult { Pixels = pixels, Buffers = buffers };
    }

    /// <summary>
    /// Applies the renderer's display transform (exposure gain → white balance
    /// → view transform → display encoding) to a linear-HDR beauty buffer. Uses the exact same per-pixel
    /// math as the in-loop path, so tone-mapping a captured (or denoised)
    /// beauty buffer reproduces the legacy output for unchanged radiance.
    /// </summary>
    public Vector3[,] ToneMapToDisplay(FrameBuffer linearBeauty)
    {
        int w = linearBeauty.Width, h = linearBeauty.Height;
        var pixels = new Vector3[h, w];
        Parallel.For(0, h, j =>
        {
            for (int i = 0; i < w; i++)
                pixels[j, i] = _display.Apply(linearBeauty.GetRgb(i, j) * _exposureScale);
        });
        return pixels;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // AOV CAPTURE (denoiser guide buffers)
    // ═════════════════════════════════════════════════════════════════════════

    // Whether the current Render call captures AOVs. Every per-hit hook is a
    // single test on this bool when capture is off, and no hook ever draws
    // from the sampler — the path tracer's random stream (and therefore the
    // image) is identical with capture on or off.
    private bool _captureAovs;

    // Per-thread probe for the camera path currently being traced. A path
    // stays on one thread, so thread-static state is safe; the probe
    // object is allocated once per worker and reset per camera sample.
    [ThreadStatic] private static AovProbe? t_aovProbe;

    /// <summary>
    /// Records the denoiser guide features of one camera path, following the
    /// first-non-delta-hit convention: depth at the first surface hit of the
    /// camera ray; albedo/normal at the first non-delta (rough/diffuse)
    /// surface, with perfect-specular chains accumulating their tint into
    /// <see cref="AlbedoWeight"/> (a mirror pixel's albedo is the mirror tint
    /// times what it reflects). Environment misses commit the sky colour with
    /// zero normal; medium scatter events commit featureless white.
    /// </summary>
    private sealed class AovProbe
    {
        public bool Pending;
        public bool HaveDepth;
        public float Depth;
        public Vector3 AlbedoWeight;
        public Vector3 TentAlbedo;
        public Vector3 TentNormal;
        public Vector3 Albedo;
        public Vector3 Normal;

        public void Reset()
        {
            Pending = true;
            HaveDepth = false;
            Depth = -1f;
            AlbedoWeight = Vector3.One;
            TentAlbedo = Vector3.One;
            TentNormal = Vector3.Zero;
            Albedo = Vector3.Zero;
            Normal = Vector3.Zero;
        }

        /// <summary>Commit at (or falling back to) the last observed surface.</summary>
        public void Commit()
        {
            Albedo = AlbedoWeight * TentAlbedo;
            Normal = TentNormal;
            Pending = false;
        }

        /// <summary>Commit with explicit features (sky / medium events).</summary>
        public void CommitAs(Vector3 albedo, Vector3 normal)
        {
            Albedo = AlbedoWeight * albedo;
            Normal = normal;
            Pending = false;
        }
    }

    /// <summary>
    /// Per-pixel side accumulators for the optional capture: dual-buffer
    /// (even/odd sample) beauty halves plus AOV halves. Lives on the worker's
    /// stack next to the legacy accumulator; zero allocations.
    /// </summary>
    private struct CaptureAccumulator
    {
        private Vector3 _sumA, _sumB;
        private Vector3 _albA, _albB, _nrmA, _nrmB;
        private float _depA, _depB;
        private int _depCntA, _depCntB;
        private int _aovCntA, _aovCntB;

        public void Add(Vector3 sample, int sampleIndex, bool aovs)
        {
            bool isA = (sampleIndex & 1) == 0;
            if (isA) _sumA += sample; else _sumB += sample;
            if (!aovs) return;
            if (isA) _aovCntA++; else _aovCntB++;

            var p = t_aovProbe!;
            // Paths that ended without a non-delta vertex (RR kill, emissive
            // hit, depth exhausted, absorbed) commit their tentative surface.
            if (p.Pending) p.Commit();
            if (isA)
            {
                _albA += p.Albedo;
                _nrmA += p.Normal;
                if (p.HaveDepth) { _depA += p.Depth; _depCntA++; }
            }
            else
            {
                _albB += p.Albedo;
                _nrmB += p.Normal;
                if (p.HaveDepth) { _depB += p.Depth; _depCntB++; }
            }
        }

        /// <summary>
        /// Divides the half sums into means and writes one pixel of every
        /// captured plane. With a single sample (nB == 0) the B half mirrors
        /// A so downstream variance estimates read as zero.
        /// </summary>
        public readonly void Store(RenderBuffers buffers, int x, int y,
                                   Vector3 linearColor, int samples)
        {
            int nA = buffers.SamplesA, nB = buffers.SamplesB;
            buffers.Beauty.SetRgb(x, y, linearColor);
            Vector3 meanA = _sumA / nA;
            Vector3 meanB = nB > 0 ? _sumB / nB : meanA;
            buffers.BeautyA.SetRgb(x, y, meanA);
            buffers.BeautyB.SetRgb(x, y, meanB);
            buffers.SampleCount[y * buffers.Width + x] = samples;

            if (!buffers.HasAovs) return;
            // The guides come from the render's own samples only (training
            // samples carry no guides), so they divide by their own counts.
            int aA = Math.Max(_aovCntA, 1), aB = _aovCntB;
            Vector3 albA = _albA / aA;
            Vector3 albB = aB > 0 ? _albB / aB : albA;
            Vector3 nrmA = _nrmA / aA;
            Vector3 nrmB = aB > 0 ? _nrmB / aB : nrmA;
            float depA = _depCntA > 0 ? _depA / _depCntA : -1f;
            float depB = aB > 0 ? (_depCntB > 0 ? _depB / _depCntB : -1f) : depA;
            buffers.AlbedoA!.SetRgb(x, y, albA);
            buffers.AlbedoB!.SetRgb(x, y, albB);
            buffers.NormalA!.SetRgb(x, y, nrmA);
            buffers.NormalB!.SetRgb(x, y, nrmB);
            buffers.DepthA!.Set(0, x, y, depA);
            buffers.DepthB!.Set(0, x, y, depB);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 Clamp01(Vector3 v) => Vector3.Clamp(v, Vector3.Zero, Vector3.One);

    /// <summary>
    /// Replaces NaN/Inf components with zero and floors negatives at zero.
    /// Idempotent — applying it more than once is a no-op — so it is safe to
    /// run on every contribution to stop a non-finite value from poisoning the
    /// path's sums or the pixel accumulator,
    /// without introducing the bias a repeated *magnitude* clamp would.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 SanitizeRadiance(Vector3 color)
    {
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z))
        {
            Interlocked.Increment(ref s_nonFiniteCount);
            if (!float.IsFinite(color.X)) color.X = 0f;
            if (!float.IsFinite(color.Y)) color.Y = 0f;
            if (!float.IsFinite(color.Z)) color.Z = 0f;
        }
        return Vector3.Max(color, Vector3.Zero);
    }

    /// <summary>Murmur3 finaliser: spreads a small user seed over all 32 bits.</summary>
    private static uint MixSeed(uint x)
    {
        x ^= x >> 16; x *= 0x85ebca6bu;
        x ^= x >> 13; x *= 0xc2b2ae35u;
        x ^= x >> 16;
        return x == 0 ? 1u : x;
    }

    /// <summary>
    /// Clamps a radiance sample to suppress firefly artifacts.
    /// Also replaces NaN/Inf values with black to prevent corruption
    /// from propagating into the pixel accumulator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampRadiance(Vector3 color)
    {
        color = SanitizeRadiance(color);

        // Luminance-preserving clamp — scales the entire vector down
        // to prevent Hue-shifting heavily saturated bright highlights.
        float lum = MathUtils.Luminance(color);
        if (lum > _maxSampleRadiance)
        {
            color *= _maxSampleRadiance / lum;
        }

        return color;
    }

    /// <summary>
    /// Clamps the indirect contribution using the depth-aware secondary clamp
    /// threshold (<c>_indirectMaxSampleRadiance</c>). When
    /// <c>--indirect-clamp-factor</c> is 1.0 this matches <see cref="ClampRadiance"/>;
    /// below 1 it suppresses caustics / deep-specular fireflies more
    /// aggressively than the primary pixel clamp.
    ///
    /// <para>Applied <b>once per camera path</b>, at the primary surface
    /// (<c>depth == _maxDepth</c>), to the full post-BSDF indirect contribution
    /// <c>attenuation · L_indirect</c>. This is the camera-relative clamp the
    /// firefly suppression is meant to be — applying it at every returning
    /// recursion frame (the previous behaviour) compounded into a clamp-of-clamp
    /// that lost energy proportional to path length, and clamping the bare
    /// returned radiance instead of the throughput-weighted contribution made
    /// the effective threshold depth-dependent.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampRadianceIndirect(Vector3 color)
    {
        color = SanitizeRadiance(color);

        float lum = MathUtils.Luminance(color);
        if (lum > _indirectMaxSampleRadiance && lum > 0f)
            color *= _indirectMaxSampleRadiance / lum;

        return color;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CORE PATH TRACER
    // ═════════════════════════════════════════════════════════════════════════

    // ═════════════════════════════════════════════════════════════════════════
    // Multiple Importance Sampling (Veach, balance heuristic)
    //
    // Each bounce carries forward (PrevPdf, PrevIsDelta) to the next surface
    // hit or sky miss of the path loop. This encodes two cases:
    //
    //   prevIsDelta = true                 Emission is shown at full weight.
    //                                      Used for camera rays and for pure-
    //                                      delta BSDF samples (mirror / perfect
    //                                      refraction) that cannot participate
    //                                      in MIS with area lights.
    //
    //   prevIsDelta = false, pdf > 0       Proper MIS balance-heuristic weight
    //                                      on any emission. w_bsdf = pdf /
    //                                      (pdf + p_light). Used for materials
    //                                      return a non-delta sample. Emitters no
    //                                      light sampler can reach (not
    //                                      registered) keep their full weight.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps a <see cref="RayCategory"/> to the matching
    /// <see cref="HitVisibilityMask"/> bit. Used by the visibility-skip loop
    /// of <see cref="FindVisibleHit"/> and by
    /// <see cref="Geometry.ShadowRay.Transmittance"/> to translate "the ray
    /// the caller is tracing" into "the bit that, if set on a hit, means
    /// skip this surface".
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HitVisibilityMask CategoryToMask(RayCategory cat) => cat switch
    {
        RayCategory.Camera       => HitVisibilityMask.Camera,
        RayCategory.Diffuse      => HitVisibilityMask.Diffuse,
        RayCategory.Glossy       => HitVisibilityMask.Glossy,
        RayCategory.Transmission => HitVisibilityMask.Transmission,
        RayCategory.Shadow       => HitVisibilityMask.Shadow,
        _                        => HitVisibilityMask.None,
    };

    /// <summary>
    /// Picks a <see cref="RayCategory"/> for a recursive bounce given the
    /// direction it scattered into and whether the BSDF lobe was a delta
    /// (mirror / perfect refraction). Used by the indirect bounce paths to
    /// label the next ray of the path so the
    /// <see cref="HitVisibilityMask"/> skip works one bounce later.
    ///
    /// <para>Best-effort approximation: non-delta reflection rays are
    /// labelled <see cref="RayCategory.Diffuse"/> rather than parsing the
    /// BSDF lobe — Arnold's <c>diffuse</c>/<c>glossy</c> visibility split
    /// for rough specular would need lobe metadata propagated from the BSDF
    /// sampler. The 4 distinct rays (camera primary, diffuse bounce, perfect
    /// mirror reflection, transmission) all resolve correctly.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RayCategory ClassifyScatteredRay(Vector3 normal, Vector3 wo, bool isDelta)
    {
        bool transmitted = Vector3.Dot(normal, wo) < 0f;
        if (transmitted) return RayCategory.Transmission;
        return isDelta ? RayCategory.Glossy : RayCategory.Diffuse;
    }

    /// <summary>
    /// Advances the caustic-chain state across a bounce. A non-delta bounce
    /// (re)starts a diffuse vertex; a delta bounce promotes a diffuse vertex
    /// into the suppress state (specular chain after a diffuse hit) and keeps a
    /// pure-from-camera specular chain in <see cref="CausticNone"/> so a mirror's
    /// view of a light is never suppressed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextCausticState(int state, bool isDelta, bool shadowPassable)
    {
        if (!isDelta) return CausticDiffuse;
        int s = state & CausticStateMask;
        if (s == CausticNone) return CausticNone;
        bool chainPassable = shadowPassable && (s == CausticDiffuse || (state & ChainShadowPassable) != 0);
        return CausticSuppress | (chainPassable ? ChainShadowPassable : 0);
    }

    /// <summary>
    /// Whether a sampled direction crosses the surface the way a shadow ray
    /// would also cross it (see <see cref="ChainShadowPassable"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShadowPassable(IMaterial material, in HitRecord rec, Vector3 wo)
    {
        if (Vector3.Dot(wo, rec.Normal) >= 0f) return false;   // reflection
        if (_causticsActive) return false;                      // photon rule owns chains
        return _transparentShadows || material.TransmitsStraight(rec);
    }

    /// <summary>
    /// Emission reached through a specular chain after a diffuse vertex is
    /// already counted by another estimator: by the photon gather (photon
    /// caustics, every chain) or by the transparent shadow ray (transparent
    /// shadows, chains that went through a surface).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SuppressChainEmission(int state)
    {
        if ((state & CausticStateMask) != CausticSuppress) return false;
        return _causticsActive || (state & ChainShadowPassable) != 0;
    }

    /// <summary>
    /// True when the light sample just taken reached the shading point through
    /// a transparent shadow: its BSDF counterpart is suppressed
    /// (<see cref="SuppressChainEmission"/>), so it must keep the full weight.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool LightSampleOwnsTransport() => ShadowRay.LastCrossedTransparent;

    /// <summary>
    /// Density estimate of the focused-caustic radiance reflected toward
    /// <paramref name="viewDir"/> at a diffuse/glossy receiver: gathers the
    /// k nearest caustic photons and sums <c>f_r(ω_p, ω_o)·Φ_p</c> over the disc
    /// of the adaptive kernel radius (Jensen's estimator,
    /// <c>L = Σ f_r·Φ / (π r²)</c>). Returns zero when caustics are inactive or
    /// no photon lies within the radius cap. Allocation-free (the bounded
    /// max-heap lives in stack spans).
    /// </summary>
    private Vector3 GatherCaustics(in HitRecord rec, Vector3 viewDir, IMaterial material)
    {
        PhotonMap? map = _photonMap;
        if (map == null) return Vector3.Zero;

        // Adaptive-radius gather: expanding-shell search over the cell
        // neighbourhood (nearest cells first, whole cells pruned once the heap
        // is full) using the distance to the k-th nearest photon as the kernel
        // radius, so the focal spot — where photons cluster tightly — gets a
        // small radius and a bright, sharp caustic, while sparse regions stay
        // dim. Fast in empty regions (empty buckets) and returns zero where
        // there is nothing to gather.
        Span<int>   idx  = stackalloc int[CausticGatherK];
        Span<float> heap = stackalloc float[CausticGatherK];
        int count = map.GatherCaustic(rec.Point, CausticGatherK, _causticGatherRadius,
                                      idx, heap, out float radius);
        if (count == 0) return Vector3.Zero;

        // Under-occupied gather: fewer than k photons exist in the whole search
        // sphere, so the region is sparse. Keep the searched cap as the kernel
        // radius (PBRT's convention — shrinking it around the few stray photons
        // would inflate the density into a bright dot) AND fade the estimate by
        // the gather occupancy below. Together with the cone filter this stops an
        // isolated reflective photon from stamping a flat full-radius disc on the
        // floor — the "ring of discs" artefact under glossy near-specular casters
        // (e.g. clearcoat spheres) whose weak caustics deposit only a few photons.
        bool underOccupied = count < CausticGatherK;
        if (underOccupied) radius = _causticGatherRadius;
        if (radius <= 0f) return Vector3.Zero;

        // Cone-filter density estimate: weight each photon by its distance to the
        // gather point (1 at the centre, → 0 at the kernel edge) so the caustic
        // has a smooth radial falloff instead of a hard disc edge. The cone
        // normalisation keeps the energy identical to the flat kernel where
        // photons fill the disc, so focused caustics keep their brightness.
        float invR = 1f / radius;
        ReadOnlySpan<Photon> photons = map.Photons;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < count; i++)
        {
            ref readonly Photon p = ref photons[idx[i]];
            // Light-leak guard: ignore photons arriving from behind the surface.
            if (Vector3.Dot(p.IncidentDir, rec.Normal) <= 0f) continue;
            float w = 1f - Vector3.Distance(p.Position, rec.Point) * invR / CausticConeK;
            if (w <= 0f) continue;
            // BRDF without cosine — the photon flux already carries the incident
            // geometry through the surface photon density.
            sum += material.Evaluate(viewDir, p.IncidentDir, rec) * (p.Power * w);
        }

        Vector3 estimate = sum * (1f / (CausticConeNorm * MathF.PI * radius * radius));

        // Occupancy confidence: a focused caustic fills the gather (count == k)
        // and passes through untouched; a sparse region (count ≪ k) is faded out
        // smoothly so weak stray reflective photons stop painting visible discs
        // while genuine, dense caustics keep their full strength.
        if (underOccupied)
            estimate *= CausticOccupancyWeight(count, CausticGatherK);
        return estimate;
    }

    /// <summary>
    /// Confidence weight for an under-occupied caustic gather. A full gather
    /// (<paramref name="count"/> ≥ <paramref name="k"/>, i.e. a dense focused
    /// caustic) returns 1 and is left untouched; a sparse gather (count ≪ k, a
    /// region holding only a few weak stray reflective photons) is faded out by a
    /// smoothstep on the occupancy ratio, so a near-isolated photon no longer
    /// stamps a flat full-radius disc on the receiver — the "ring of discs"
    /// artefact under glossy near-specular casters. Monotonic non-decreasing in
    /// <paramref name="count"/>.
    /// </summary>
    internal static float CausticOccupancyWeight(int count, int k)
    {
        if (count >= k) return 1f;
        if (count <= 0) return 0f;
        float t = (float)count / k;
        return t * t * (3f - 2f * t);   // smoothstep(0,1,t)
    }

    /// <summary>
    /// MIS weight of a light sample whose own density is <paramref name="own"/>,
    /// against every light strategy that samples the same emitter in that
    /// direction (<paramref name="sum"/>, <paramref name="sumSq"/>, own included)
    /// and the BSDF or phase density <paramref name="pBsdf"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float NeeWeight(float own, float sum, float sumSq, float pBsdf)
        => _misHeuristic == MisHeuristic.Power
            ? own * own / (sumSq + pBsdf * pBsdf + 1e-30f)
            : own / (sum + pBsdf + 1e-30f);

    /// <summary>MIS weight of emission reached by a BSDF sample: the complement of <see cref="NeeWeight"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float BsdfWeight(float pBsdf, float sum, float sumSq)
        => _misHeuristic == MisHeuristic.Power
            ? pBsdf * pBsdf / (pBsdf * pBsdf + sumSq + 1e-30f)
            : pBsdf / (pBsdf + sum + 1e-30f);

    /// <summary>
    /// MIS weight for a light sample at a surface or medium vertex: 1 when no
    /// BSDF sample can deliver this light (delta lights, a directional disc,
    /// a proxy-less area light), otherwise the share of the light strategies.
    /// </summary>
    private float LightSampleWeight(ILight light, float pPick, Vector3 origin, Vector3 dir, float pBsdf)
    {
        if (pBsdf <= 0f || !_mis.PairsWithBsdf(light)) return 1f;
        var (own, sum, sumSq) = _mis.NeeDensity(light, pPick, origin, dir);
        return NeeWeight(own, sum, sumSq, pBsdf);
    }

    /// <summary>
    /// Applies the MIS weight to surface emission at the current hit — the
    /// "BSDF-sample hit a light" half of Veach's estimator. The light is the
    /// one that owns the surface hit at <paramref name="t"/>.
    /// </summary>
    private Vector3 WeightEmission(Vector3 rawEmission, IMaterial material, Ray ray, float t,
                                    float prevBsdfPdf, bool prevIsDelta)
    {
        if (prevIsDelta)
            return rawEmission;

        ILight? light = _mis.EmitterOwner(material, ray, t);
        if (light == null)
            return rawEmission;   // no next-event sampler covers it: full weight

        float pLight = _mis.Pick(light, ray.Origin) * light.PdfSolidAngle(ray.Origin, ray.Direction);
        if (prevBsdfPdf + pLight <= 1e-20f)
            return Vector3.Zero;
        return rawEmission * BsdfWeight(prevBsdfPdf, pLight, pLight * pLight);
    }

    /// <summary>
    /// Evaluates the sky for a missed ray and applies the MIS weight for the
    /// "BSDF-sample escaped into the environment" half of the estimator.
    ///
    /// <para>The sky's analytical sun disc (if any) is included only on delta
    /// bounces and camera rays — for non-delta bounces a paired
    /// <see cref="Lights.PhysicalSun"/> handles the sun via NEE, so adding
    /// it here would double-count. This matches Arnold / Cycles HDRI
    /// sun-extraction behaviour.</para>
    ///
    /// <para>For non-delta BSDF escapes onto HDRI environments, we additionally
    /// derive a mipmap LOD from the BSDF PDF: a wide lobe (low pdf) reads from
    /// a smoother HDRI level, killing the firefly spike from undersampled
    /// pixel peaks. The heuristic is
    /// <c>lod ≈ 0.5 · log₂(W·H / (4π · pdf_bsdf))</c>, derived from the
    /// solid-angle-to-texel-count of a uniform cone with the BSDF's effective
    /// width. Sharp specular (delta) bounces use LOD 0.</para>
    /// </summary>
    private Vector3 SampleSky(Ray ray, float prevBsdfPdf, bool prevIsDelta, int causticState, RayCategory category)
    {
        // A sky NEE-sampled through the same transparent surfaces already
        // delivered this transport (see SuppressChainEmission).
        bool skyIsNeeSampled = _envLight != null && _envLight.Sky.CanSampleDirectly;
        if (skyIsNeeSampled && !_causticsActive && SuppressChainEmission(causticState))
            return Vector3.Zero;

        // Camera rays + delta-mirror bounces see the full sky (sun included).
        // Non-delta indirect bounces see the sky body only. Visibility flags
        // and the background plate follow the ray's own category: a mirror
        // reflects the sky its glossy flag shows, not the camera's plate (a
        // delta bounce used to count as a camera ray here).
        bool showSun = prevIsDelta;
        var cat = category;

        // LOD heuristic for glossy HDRI lookups: a prefiltered (blurred) sky
        // tames the fireflies of a bright HDRI that only BSDF rays find. It
        // is not the sky the light samples see: when the environment is a
        // next-event light the MIS weights pair the two, and the blur spread
        // the bright features' light into directions weighted for the BSDF,
        // counting it twice (+26% on a diffuse floor under a bright patch,
        // PIANO-REVIEW.md Phase 6). There MIS already removes the fireflies,
        // so the prefilter is kept for a sky no light samples, never in the
        // reference estimator. The sky body handles the is-HDRI / not check
        // internally: a non-zero LOD on a gradient/Preetham sky is a no-op.
        float lod = 0f;
        if (!prevIsDelta && prevBsdfPdf > 1e-6f && !_mis.HasSkyLights && !_referenceMode
            && _sky.LightingModel is Sky.HdriSky hdriSky)
        {
            int W = hdriSky.Map.Width;
            int H = hdriSky.Map.Height;
            // Texels per steradian × cone solid angle (1/pdf) = texels covered.
            // lod = log₂(√texelsCovered) = 0.5 · log₂(texelsCovered).
            float texelsCovered = W * H / (4f * MathF.PI * prevBsdfPdf);
            if (texelsCovered > 1f)
                lod = 0.5f * MathF.Log2(texelsCovered);
            lod = MathF.Max(0f, MathF.Min(lod, hdriSky.Map.MaxMipLevel));
        }

        Vector3 sky = _sky.Sample(ray, cat, includeAnalyticalSun: showSun, mipLod: lod);

        // ── AOV: environment miss commits the sky colour ────────────────────
        // Background pixels get the (clamped) sky radiance as their albedo
        // guide and a zero normal; depth keeps its −1 "no hit" sentinel.
        if (_captureAovs && t_aovProbe is { Pending: true } aovProbe)
            aovProbe.CommitAs(Clamp01(sky), Vector3.Zero);

        // A delta bounce or a camera ray sees the sky (sun included) at full
        // weight: no light sampler produces its direction.
        if (prevIsDelta)
            return sky;

        // The sky body, weighted against the environment and every portal
        // (they all sample it), when any of them is a next-event light.
        Vector3 result = sky;
        if (_mis.HasSkyLights)
        {
            var (sum, sumSq) = _mis.SkyDensity(ray.Origin, ray.Direction);
            result = prevBsdfPdf + sum <= 1e-20f ? Vector3.Zero : sky * BsdfWeight(prevBsdfPdf, sum, sumSq);
        }

        // The analytic sun is left out of the sky body above: it is a light of
        // its own, sampled by its own NEE, and a BSDF ray inside its disc
        // collects it here with the matching weight (it used to collect
        // nothing while the sun's samples were still weighted, losing light
        // on glossy surfaces).
        foreach (var sun in _mis.Suns)
        {
            Vector3 Ls = sun.RadianceToward(ray.Direction);
            if (Ls == Vector3.Zero) continue;
            float pSun = _mis.Pick(sun, ray.Origin) * sun.PdfSolidAngle(ray.Origin, ray.Direction);
            result += Ls * BsdfWeight(prevBsdfPdf, pSun, pSun * pSun);
        }
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DIRECT LIGHTING (NEXT EVENT ESTIMATION)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes direct illumination from all lights at a surface hit point.
    ///
    /// 0. Ambient fill — applied unconditionally to all materials.
    /// 1. Per-light, per-shadow-sample:
    ///    - shadow test + stratified emitter sample
    ///    - material.Evaluate(toEye, toLight) · |N·L| for the BSDF shape
    ///    - balance-heuristic MIS weight w_nee = p_light / (p_light + p_bsdf)
    ///      using the material's own IMaterial.Pdf() in solid angle.
    ///
    /// For delta lights (point/directional/spot) no BSDF sampler can reach them,
    /// so w_nee = 1 unconditionally. A pure-delta material has Pdf 0 for every
    /// light direction and Evaluate 0 there too, so it gets no direct light.
    ///
    /// PBRT/Arnold direct-lighting convention:
    /// The integrand at the shadow-ray direction is
    /// <c>f(V, L) · |N·L| · visibility</c>, with
    /// every material colour factor (baseColor, metallic Fresnel, sheen tint,
    /// subsurface tint) baked in. The caller adds this contribution directly
    /// to the radiance estimator: <c>L = Le + L_direct + scatter_attn × L_indirect</c>.
    /// The scatter attenuation never multiplies the direct term — that would
    /// couple the indirect importance sample to the shadow estimator and
    /// bias the result for any direction-dependent BSDF.
    /// </summary>
    /// <summary>
    /// NEE at a surface. A surface that transmits (rough glass, diffuse
    /// transmission) also receives light from behind: the lights keep those
    /// samples and start the shadow ray on the far side while the switch is up
    /// (PIANO-REVIEW.md §3.36).
    /// </summary>
    private Vector3 DirectLightingAt(in HitRecord rec, Vector3 viewDir, IMaterial? material, in MediumStack media)
    {
        bool transmits = material?.HasTransmission(in rec) ?? false;
        ShadowRay.ReceiverTransmits = transmits;
        // The shadow rays start in the media the path is in, and cross this
        // surface's boundary when they leave from its far side.
        ShadowRay.BeginMedia(in media, _globalMedium);
        if (transmits && material!.TryGetDielectricIor(in rec, out _) || rec.MediumIface.Interior != null)
            ShadowRay.SetReceiver(Vector3.Dot(rec.Normal, viewDir) >= 0f ? rec.Normal : -rec.Normal,
                                  rec.FrontFace, rec.MediumIface.Interior, rec.EntityRoot);
        try
        {
            return ComputeDirectLighting(in rec, viewDir, material);
        }
        finally
        {
            ShadowRay.ReceiverTransmits = false;
            ShadowRay.EndMedia();
        }
    }

    /// <summary>
    /// The direct-lighting integrand f(V, L) |N·L| for one light direction:
    /// the material's own BSDF, the same the indirect bounce samples
    /// (PIANO-REVIEW.md §3.12). A surface with no material is Lambert white.
    /// </summary>
    private static Vector3 DirectIntegrand(IMaterial? material, in HitRecord rec, Vector3 viewDir, Vector3 dirToLight)
    {
        float cos = Vector3.Dot(rec.Normal, dirToLight);
        if (material == null) return new Vector3(MathF.Max(cos, 0f) / MathF.PI);
        return material.Evaluate(viewDir, dirToLight, in rec) * MathF.Abs(cos);
    }

    /// <summary>
    /// Shadow-terminator factor for the light direction, on the side of the
    /// surface the light is: for a light behind a transmissive surface the
    /// horizon is the back one (it used to read 0 there, so NEE never lit
    /// anything through rough glass, §3.36).
    /// </summary>
    private static float LightSideTerminator(in HitRecord rec, Vector3 dirToLight)
        => Vector3.Dot(rec.ShadingNg, dirToLight) >= 0f
            ? MathUtils.ShadowTerminatorTerm(rec.ShadingNg, rec.Normal, dirToLight)
            : MathUtils.ShadowTerminatorTerm(-rec.ShadingNg, -rec.Normal, dirToLight);

    private Vector3 ComputeDirectLighting(in HitRecord rec, Vector3 viewDir, IMaterial? material)
    {
        Vector3 result = Vector3.Zero;

        if (_mis.PicksOneLight)
        {
            // ── Single-light picking (power, uniform or light BVH) ───────────
            // Unbiased single-sample estimator (PBRT §16.3.2):
            //   contribution = lightAccum / pPick
            // MIS uses pdf_combined = pPick × pLightSample for non-delta lights.
            if (_lights.Count == 0)
            {
                return result;
            }
            float xi = MathUtils.RandomFloat();
            var (lightIdx, pPick) = _mis.Choose(rec.Point, xi);
            if (lightIdx < 0) return result;
            ILight light = _lights[lightIdx];

            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                var (inShadow, lightColor, dirToLight, distance) =
                    SampleLight(light, rec.Point, rec.Normal, s, rec.Time);

                if (inShadow) continue;

                Vector3 brdf = DirectIntegrand(material, in rec, viewDir, dirToLight);

                // Shadow-terminator softening on the light side (Chiang 2019):
                // cull the energy a perturbed shading normal would fabricate where
                // the light grazes below the true surface horizon (no-op where the
                // normal is unperturbed). The BSDF-sampled bounce carries it too.
                brdf *= LightSideTerminator(in rec, dirToLight);

                // The contribution is wNee · lightColor · brdf · Tr. When the
                // BRDF integrand is zero (light below the shading hemisphere,
                // delta lobe), the sample adds nothing — skip the medium
                // transmittance walk and the BSDF-pdf MIS evaluation entirely.
                if (brdf == Vector3.Zero) continue;

                // MIS: the NEE density carries the pick probability.
                float wNee = material != null && _mis.PairsWithBsdf(light)
                    ? LightSampleWeight(light, pPick, rec.Point, dirToLight, MisBsdfPdf(material, viewDir, dirToLight, in rec))
                    : 1f;

                if (LightSampleOwnsTransport()) wNee = 1f;
                lightAccum += wNee * lightColor * brdf;
            }

            // Divide by pPick to get the unbiased estimator (1 light sampled, not all).
            if (pPick > 0f)
                result += lightAccum / pPick;
        }
        else
        {
            // ── Sum over all lights (original behaviour) ─────────────────────
            foreach (var light in _lights)
            {
                int samples = light.ShadowSamples;
                Vector3 lightAccum = Vector3.Zero;

                for (int s = 0; s < samples; s++)
                {
                    var (inShadow, lightColor, dirToLight, distance) =
                        SampleLight(light, rec.Point, rec.Normal, s, rec.Time);

                    if (inShadow) continue;

                    Vector3 brdf = DirectIntegrand(material, in rec, viewDir, dirToLight);

                    // Shadow-terminator softening on the light side (Chiang 2019):
                    // cull the energy a perturbed shading normal would fabricate where
                    // the light grazes below the true surface horizon (no-op where the
                    // normal is unperturbed). The BSDF-sampled bounce carries it too.
                    brdf *= LightSideTerminator(in rec, dirToLight);

                    // Zero integrand (light below the hemisphere / delta lobe) →
                    // the sample contributes nothing; skip the medium walk and
                    // the BSDF-pdf MIS evaluation. See the single-light branch.
                    if (brdf == Vector3.Zero) continue;

                    // ── MIS weight (balance or power, configured per-renderer) ──
                    // Delta lights: always weight 1 (no BSDF sampler can hit them).
                    // Non-delta lights: if the material exposes Pdf() > 0, the
                    // weight reduces variance via Veach's combined estimator.
                    float wNee = material != null && _mis.PairsWithBsdf(light)
                        ? LightSampleWeight(light, 1f, rec.Point, dirToLight, MisBsdfPdf(material, viewDir, dirToLight, in rec))
                        : 1f;

                    if (LightSampleOwnsTransport()) wNee = 1f;
                lightAccum += wNee * lightColor * brdf;
                }

                result += lightAccum;
            }
        }

        return result;
    }

    /// <summary>
    /// Unified light-sample dispatcher: calls the stratified variant for
    /// area/sphere/geometry/sun-disc/multi-sample-spot lights and falls back
    /// to the base <see cref="ILight.IlluminateAndTest"/> for all other lights.
    /// </summary>
    private (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        SampleLight(ILight light, Vector3 point, Vector3 normal, int sampleIndex, float time)
        // Single virtual dispatch — each light type self-selects stratified vs.
        // single-sample behaviour (directional sun-disc and multi-sample spots
        // guard on their own parameters internally). Replaces the former
        // per-sample type-ladder. See ILight.IlluminateAndTestStratified.
    {
        // Lights without a shadow walk (none today) must not inherit a stale flag.
        ShadowRay.LastCrossedTransparent = false;
        return light.IlluminateAndTestStratified(point, normal, _world, sampleIndex, time);
    }

    /// <summary>
    /// Next-event estimation at a scattering event in a medium, with the
    /// event's phase function in the BSDF's place. The shadow rays start in
    /// <paramref name="media"/> (the path's stack, the global medium when it
    /// is empty) and are attenuated by every medium they cross on the way
    /// (<see cref="ShadowRay.BeginMedia"/>).
    /// </summary>
    /// <param name="wo">Direction of the ray that produced the event (it points
    ///                  into the event, the phase functions' convention).</param>
    private Vector3 ComputeDirectLightingMedium(Vector3 p, Vector3 wo, IPhaseFunction phase,
                                                in MediumStack media, float time)
    {
        ShadowRay.BeginMedia(in media, _globalMedium);
        try
        {
            return DirectLightingInMedium(p, wo, phase, time);
        }
        finally
        {
            ShadowRay.EndMedia();
        }
    }

    private Vector3 DirectLightingInMedium(Vector3 p, Vector3 wo, IPhaseFunction phase, float time)
    {
        Vector3 result = Vector3.Zero;

        // Lights use `surfaceNormal` ONLY to offset the shadow-ray origin via
        // OffsetOrigin(p, n) = p + n × ε. For a volumetric scattering event
        // there is no surface to self-intersect with, so no offset is needed.
        // Passing Zero ensures OffsetOrigin returns the point unchanged.
        Vector3 dummyNormal = Vector3.Zero;

        if (_mis.PicksOneLight)
        {
            // ── Single-light picking in volumetric path ──────────────────────
            if (_lights.Count == 0) return result;
            float xi = MathUtils.RandomFloat();
            var (lightIdx, pPick) = _mis.Choose(p, xi);
            if (lightIdx < 0) return result;
            ILight light = _lights[lightIdx];

            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                var (inShadow, lightColor, dirToLight, distance) =
                    SampleLight(light, p, dummyNormal, s, time);

                if (inShadow) continue;

                float phaseVal = phase.Evaluate(wo, dirToLight);

                float wNee = _mis.PairsWithBsdf(light)
                    ? LightSampleWeight(light, pPick, p, dirToLight, MisPhasePdf(phase, wo, dirToLight))
                    : 1f;

                if (LightSampleOwnsTransport()) wNee = 1f;
                wNee *= EquiangularNeeWeight(light, pPick);
                lightAccum += wNee * lightColor * phaseVal;
            }

            if (pPick > 0f)
                result += lightAccum / pPick;
        }
        else
        {
            // ── Sum over all lights ──────────────────────────────────────────
            foreach (var light in _lights)
            {
                int samples = light.ShadowSamples;
                Vector3 lightAccum = Vector3.Zero;

                for (int s = 0; s < samples; s++)
                {
                    // Mirror the dispatch used by ComputeDirectLighting so that
                    // AreaLight and SphereLight keep their stratified samples in
                    // the volumetric path too — this is where low-discrepancy
                    // shadow sampling matters most (large area lights seen through
                    // dense fog are the worst-case variance scenario).
                    var (inShadow, lightColor, dirToLight, distance) =
                        SampleLight(light, p, dummyNormal, s, time);

                    if (inShadow) continue;

                    // Phase function value for this in-scattering direction.
                    float phaseVal = phase.Evaluate(wo, dirToLight);

                    // ── MIS weight (phase-function vs light sampler) ────────────
                    // Delta lights cannot be reached by phase sampling; emit at
                    // full weight. Otherwise pair the analytic phase PDF against
                    // the light PDF using the configured heuristic.
                    float wNee = _mis.PairsWithBsdf(light)
                        ? LightSampleWeight(light, 1f, p, dirToLight, MisPhasePdf(phase, wo, dirToLight))
                        : 1f;

                    if (LightSampleOwnsTransport()) wNee = 1f;
                    wNee *= EquiangularNeeWeight(light, 1f);
                lightAccum += wNee * lightColor * phaseVal;
                }

                result += lightAccum;
            }
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SKY / ENVIRONMENT
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the sky/environment radiance for a ray that escaped the scene.
    /// Delegates to <see cref="SkySettings"/> which handles both legacy flat
    /// backgrounds and the new gradient sky with sun disk.
    /// </summary>
    private Vector3 CalculateSkyColor(Ray ray)
    {
        return _sky.Sample(ray);
    }

    /// <summary>
    /// Perturbs the shading normal using a tangent-space normal map.
    ///
    /// The normal map stores normals in tangent space where (0, 0, 1)
    /// means "unperturbed — same as the geometric normal". The TBN matrix
    /// transforms this into world space:
    ///
    ///   worldNormal = T * mapNormal.X + B * mapNormal.Y + N * mapNormal.Z
    ///
    /// The result replaces rec.Normal, affecting all subsequent shading.
    /// </summary>
    private static void ApplyNormalMap(ref HitRecord rec, NormalMapTexture normalMap)
    {
        // Sample the normal map at the hit UV
        Vector3 tsNormal = normalMap.SampleNormal(rec.U, rec.V);
 
        Vector3 T = rec.Tangent;
        Vector3 B = rec.Bitangent;
        Vector3 N = rec.Normal;

        // Gram-Schmidt orthogonalization to ensure T is exactly perpendicular to N.
        Vector3 tOrt = T - Vector3.Dot(T, N) * N;
        if (tOrt.LengthSquared() > 1e-8f)
            T = Vector3.Normalize(tOrt);
        
        // Orthogonalize B against N and T to preserve its original intended 
        // parametric direction while making it purely orthogonal to the basis.
        Vector3 bOrt = B - Vector3.Dot(B, N) * N - Vector3.Dot(B, T) * T;
        if (bOrt.LengthSquared() > 1e-8f)
            B = Vector3.Normalize(bOrt);

        // If the normal N was flipped because we hit a backface (inside of the object),
        // we must also flip T and B to prevent the tangent space from changing handedness,
        // which would otherwise visually invert the normal map bumps.
        if (!rec.FrontFace)
        {
            T = -T;
            B = -B;
        }
 
        Vector3 perturbedNormal = Vector3.Normalize(
            T * tsNormal.X + B * tsNormal.Y + N * tsNormal.Z
        );

        rec.Normal = perturbedNormal;
    }

    /// <summary>
    /// How a unit step along the world tangent <paramref name="t"/> and
    /// bitangent <paramref name="b"/> moves a texture's inputs at this hit:
    /// (u, v) through the inverse of the surface's ∂P/∂u, ∂P/∂v (least
    /// squares on the tangent plane), and the 3D point through the inverse of
    /// the rotation the transforms took it out of (<see cref="HitRecord.LocalToWorld"/>).
    /// A primitive without ∂P/∂u reads as one UV unit per unit of length.
    /// </summary>
    internal static BumpFrame BumpFrameAt(in HitRecord rec, Vector3 t, Vector3 b)
    {
        Vector2 uvT, uvB;
        Vector3 dpdu = rec.DpDu, dpdv = rec.DpDv;
        float a00 = Vector3.Dot(dpdu, dpdu), a01 = Vector3.Dot(dpdu, dpdv), a11 = Vector3.Dot(dpdv, dpdv);
        float det = a00 * a11 - a01 * a01;
        if (det > 1e-12f * MathF.Max(a00 * a11, 1e-30f) && a00 > 0f && a11 > 0f)
        {
            float inv = 1f / det;
            Vector2 Solve(Vector3 x)
            {
                float r0 = Vector3.Dot(dpdu, x), r1 = Vector3.Dot(dpdv, x);
                return new Vector2((a11 * r0 - a01 * r1) * inv, (a00 * r1 - a01 * r0) * inv);
            }
            uvT = Solve(t);
            uvB = Solve(b);
        }
        else
        {
            float side = rec.FrontFace ? 1f : -1f;
            uvT = new Vector2(side, 0f);
            uvB = new Vector2(0f, side);
        }

        Vector3 pT = t, pB = b;
        if (rec.HasLocalFrame)
        {
            Quaternion toLocal = Quaternion.Conjugate(rec.LocalToWorld);
            pT = Vector3.Transform(t, toLocal);
            pB = Vector3.Transform(b, toLocal);
        }
        return new BumpFrame { UvT = uvT, UvB = uvB, PointT = pT, PointB = pB };
    }

    /// <summary>
    /// Bump-map perturbation. Mirrors <see cref="ApplyNormalMap"/> but the
    /// tangent-space normal comes from finite-difference gradients of a
    /// luminance height field rather than an RGB-encoded normal texture.
    ///
    /// Runs AFTER <see cref="ApplyNormalMap"/> when both are present:
    /// the TBN is re-orthogonalised against the already-perturbed
    /// <c>rec.Normal</c> so bump details sit on top of the normal-map
    /// medium-frequency relief (Arnold/Cycles composition order).
    /// </summary>
    private static void ApplyBumpMap(ref HitRecord rec, IBumpMap bump)
    {
        Vector3 T = rec.Tangent;
        Vector3 B = rec.Bitangent;
        Vector3 N = rec.Normal;

        // Gram-Schmidt re-orthogonalisation against the (possibly normal-
        // mapped) shading normal. Identical to ApplyNormalMap.
        Vector3 tOrt = T - Vector3.Dot(T, N) * N;
        if (tOrt.LengthSquared() > 1e-8f)
            T = Vector3.Normalize(tOrt);

        Vector3 bOrt = B - Vector3.Dot(B, N) * N - Vector3.Dot(B, T) * T;
        if (bOrt.LengthSquared() > 1e-8f)
            B = Vector3.Normalize(bOrt);

        // Preserve tangent-space handedness on backfaces.
        if (!rec.FrontFace)
        {
            T = -T;
            B = -B;
        }

        Vector3 tsNormal = bump.SampleTangentNormal(
            rec.U, rec.V, rec.LocalPoint, BumpFrameAt(in rec, T, B), rec.ObjectSeed);

        Vector3 perturbedNormal = Vector3.Normalize(
            T * tsNormal.X + B * tsNormal.Y + N * tsNormal.Z
        );

        rec.Normal = perturbedNormal;
    }
}
