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

    // Specular-chain state threaded through the eye path so a transparent
    // shadow and the BSDF-sampled emission are counted exactly once. None: no
    // non-delta bounce yet (camera / pure-specular-from-camera). Diffuse: last
    // bounce was non-delta. Chain: a specular chain followed a diffuse vertex.
    private const int ChainNone = 0, ChainDiffuse = 1, ChainSpecular = 2;
    // Bit on top of the state: every event of the specular chain since the
    // last diffuse vertex was a transmission a shadow ray also crosses (straight
    // transmission always, any transmission with transparent shadows). Such a
    // chain ending on a light is exactly what the light sample already
    // delivered through the same surfaces, so its emission is dropped.
    private const int ChainStateMask = 3, ChainShadowPassable = 4;

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
    // vertex merging.
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
    // scene radius (ray offsets, merging radius, light power of the
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
        LightSamplingStrategy lightSamplingStrategy = LightSamplingStrategy.Auto,
        float indirectClampFactor = DefaultIndirectClampFactor,
        TextureFilteringMode textureFiltering = TextureFilteringMode.Auto,
        float exposureEv = DefaultExposureEv,
        SssMode sssMode = SssMode.Auto,
        MotionBlurSettings motionBlur = default,
        bool referenceMode = false,
        int seed = 0,
        bool transparentShadows = false,
        PixelFilter? pixelFilter = null,
        DisplayTransform? displayTransform = null,
        PathDepths? depths = null)
    {
        _depths = depths ?? PathDepths.Uniform(maxDepth);
        _pixelFilter = pixelFilter ?? PixelFilter.Create(PixelFilterKind.Gaussian);
        _display = displayTransform ?? DisplayTransform.Default;
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
        // out): its radius scales the ray offsets and the merging radius.
        AABB sceneBounds = ComputeFiniteSceneBounds(world);
        _finiteSceneBounds = sceneBounds;
        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        float sceneRadius = MathF.Max(0.5f * extent.Length(), 1e-3f);
        _sceneScale = sceneRadius;

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
    /// dual-buffer half splits) and first-non-delta-hit AOVs
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
        if (Integrator != IntegratorKind.Path)
        {
            Interlocked.Exchange(ref s_nonFiniteCount, 0);
            return RenderBidirectional(width, height, options);
        }
        LastRenderMeanSamples = 0;
        if (AdaptiveActive)
            return RenderAdaptive(width, height, options);
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
        GuidedPassSums? finalPass = trained > 0 ? new GuidedPassSums(width, height, trained, actualSamples) : null;
        bool captureAovs = _captureAovs;
        // Dual-buffer halves (RenderBuffers): with Sobol the first ⌈n/2⌉
        // samples of the pass against the rest, with the PRNG's jittered grid
        // the even cells against the odd ones.
        int halfA = (actualSamples + 1) / 2;

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
            // never leak across renders: shadow rays are opaque to glass unless
            // transparent shadows were asked for, so light through glass
            // arrives only along BSDF-sampled paths, the unbiased estimator.
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
                        if (buffers != null) capAcc.Add(sample, s < halfA, captureAovs);
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
                            if (buffers != null) capAcc.Add(sample, ((sy * sqrtSpp + sx) & 1) == 0, captureAovs);
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
}
