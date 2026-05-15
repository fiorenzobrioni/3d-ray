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
/// </list>
/// </summary>
public enum LightSamplingStrategy
{
    All,
    Power,
    Uniform
}

public class Renderer
{
    private readonly IHittable _world;
    private readonly Camera.Camera _camera;
    private readonly List<ILight> _lights;
    private readonly SkySettings _sky;
    private readonly int _maxDepth;
    private readonly int _samplesPerPixel;
    private readonly Dictionary<Emissive, ILight> _emitterToLight;
    private readonly EnvironmentLight? _envLight;
    private readonly IMedium? _globalMedium;
    private readonly bool _verbose;
    private readonly MisHeuristic _misHeuristic;
    private readonly LightSamplingStrategy _lightSamplingStrategy;
    private readonly LightDistribution? _lightDist; // non-null when strategy ≠ All

    // ── Russian Roulette configuration ──────────────────────────────────────
    //
    // RR is scene-adaptive AND path-throughput based (PBRT §13.7.1, Veach §10.4):
    // the survival probability uses the cumulative path throughput β tracked
    // from the camera, NOT the local single-bounce attenuation. β decays
    // multiplicatively through every bounce, so paths through dim/dark
    // surfaces or many lossy bounces get killed aggressively, while paths
    // that have stayed close to unit throughput are preserved. The local-
    // attenuation form (used in the previous implementation) could not see
    // the long-bounce decay — a 50%-grey surface chained four times has
    // β = 0.0625 yet each local attenuation is still 0.5, so the old test
    // kept paths alive far longer than they were contributing to the image.
    //
    // The renderer also detects whether the scene relies primarily on
    // indirect/emissive light and tightens both knobs to give those paths
    // more time to find the (rare) emissive geometry before being culled.
    //
    // Normal scenes (bright explicit lights):
    //   MinBounces = 3, MinSurvival = 0.10 → max boost 10×
    //   Direct lighting (NEE) carries most energy, indirect is correction.
    //   Aggressive RR is fine — killed paths lose only the small indirect term.
    //
    // Indirect-dominant scenes (emissive-only or very dim lights):
    //   MinBounces = 6, MinSurvival = 0.40 → max boost 2.5×
    //   ALL energy comes from indirect bounces hitting emissive surfaces.
    //   Aggressive RR kills paths BEFORE they find the emissive → dark spots.
    //   Conservative RR lets more paths survive to find the light source.
    //
    private const int   RR_MinBounces_Normal  = 3;
    private const float RR_MinSurvival_Normal = 0.10f;

    private const int   RR_MinBounces_Indirect  = 6;
    private const float RR_MinSurvival_Indirect = 0.40f;

    // Below this max-channel throughput the path is numerically dead — its
    // contribution to the final pixel is below the post-clamp / post-tone-map
    // noise floor. Skipping the recursive TraceRay (and the BVH traversal
    // that goes with it) is a pure win, because the radiance the call would
    // have returned is multiplied back by β at the caller anyway.
    private const float DeadPathThroughputEpsilon = 1e-4f;

    // Effective values, computed at construction based on scene analysis
    private readonly int _rrMinBounces;
    private readonly float _rrMinSurvival;

    // ── Firefly suppression ─────────────────────────────────────────────────
    // Maximum per-sample radiance (before tone mapping). Catches outliers from
    // RR boost, Disney lobe compensation, specular caustics, and NaN/Inf edge
    // cases. After ACES tone mapping any luminance ≳ 5 already saturates to
    // white, so a default of 10 leaves all legitimate highlights untouched
    // while killing the rare bright spikes that produce visible firefly noise.
    // Aligns with the Cycles `clamp_indirect = 10` and Arnold `AA_clamp ≈ 10`
    // industry defaults. Override via the constructor (CLI flag --clamp/-C).
    public const float DefaultMaxSampleRadiance = 10f;
    private readonly float _maxSampleRadiance;

    // ── Depth-aware indirect firefly clamp ──────────────────────────────────
    // A second (typically tighter) clamp is applied to the indirect (bounce ≥ 1)
    // contribution inside ShadeSurface/ShadeSampleBounce, mirroring the
    // Cycles/Arnold "indirect clamp" feature. Default 0.25 → indirect clamp =
    // 0.25 × primary clamp = 2.5 with the default --clamp 10, which targets
    // caustic / specular-chain fireflies that survive the primary clamp.
    // Set to 1.0 to disable the extra suppression.
    //
    // CLI: --indirect-clamp-factor <f>   (e.g. 1.0 = same as primary clamp)
    public const float DefaultIndirectClampFactor = 0.25f;
    private readonly float _indirectMaxSampleRadiance;

    // Threshold below which the scene is considered indirect-dominant.
    // Expressed as the scene-averaged irradiance [Rec.709 luminance]:
    //
    //     Ē = Φ_total / (4π · R²)
    //
    // where Φ_total is the sum of `ApproximatePower(sceneBounds)` across all
    // lights and R is the scene bounding-sphere radius. This quantity is the
    // mean luminance landing on a sphere enclosing the scene and is invariant
    // to scene translation and — crucially — to uniform scene scaling: point
    // lights' 4π·I normalised by 4π·R² gives I/R², area lights' π·L·A divided
    // by 4π·R² gives L·A/(4R²), both of which describe the "light-per-surface"
    // that actually drives NEE convergence. Identical lighting setups classify
    // identically regardless of the scene's world-space units.
    //
    // Calibration: a Cornell-like enclosed emissive-only scene (∼Ē < 0.3)
    // stays conservative; a daylight-bright sky (Ē ≈ π/4 ≈ 0.78) or a unit-
    // intensity directional sun (Ē ≈ 0.25 per scaled-I) in a normal-radius
    // scene crosses the threshold; 0.5 is the midpoint that preserves the
    // original classifier's intent on reference scenes without its
    // scene-scale dependence.
    private const float IndirectDominantThreshold = 0.5f;

    // Clamp used on the world AABB before scene analysis. InfinitePlane reports
    // a ±1e6 AABB (its "fake finite box" for BVH compatibility); this would
    // dominate the bounding sphere and drive DirectionalLight / EnvironmentLight
    // flux toward ∞ while still dividing by ∞² in the normalisation, so the
    // classifier would silently collapse to "zero irradiance" on any scene
    // with an infinite plane. Clamping to ±1e3 recovers a realistic finite
    // extent for all practical YAML scenes while still accommodating
    // architectural exteriors hundreds of units across.
    private const float SceneBoundsClamp = 1.0e3f;

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
        float indirectClampFactor = DefaultIndirectClampFactor)
    {
        _world = world;
        _camera = camera;
        _lights = lights;
        _sky = sky;
        _samplesPerPixel = samplesPerPixel;
        _maxDepth = maxDepth;
        _globalMedium = globalMedium;
        _maxSampleRadiance = maxSampleRadiance ?? DefaultMaxSampleRadiance;
        _indirectMaxSampleRadiance = _maxSampleRadiance * MathF.Max(0f, indirectClampFactor);
        _verbose = verbose;
        _misHeuristic = misHeuristic;
        _lightSamplingStrategy = lightSamplingStrategy;

        // ── Scene analysis: detect indirect-dominant lighting ────────────
        // Sum each light's approximate radiant flux [Rec.709 luminance] and
        // normalise by the scene's enclosing-sphere surface area (4π R²).
        // The resulting mean irradiance is invariant to scene scale and
        // translation — see IndirectDominantThreshold for the full rationale.
        //
        // The bounding sphere is computed from the *finite* portion of the
        // world only: InfinitePlane reports a ±1e6 AABB (a BVH-compatibility
        // sentinel), which — unclamped — would inflate R until a scene's
        // physical lights become invisible to the classifier. We fall back
        // to a hard clamp only if no finite geometry exists at all.
        AABB sceneBounds = ComputeFiniteSceneBounds(world);

        float totalFlux = 0f;
        foreach (var light in lights)
            totalFlux += light.ApproximatePower(sceneBounds);

        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        // Guard against degenerate single-point bounds (empty scenes,
        // synthetic unit tests). 1e-3 floors the radius at 1 mm of world unit
        // so the division is safe; the normalised irradiance will be huge and
        // the scene correctly classified as direct-dominant.
        float sceneRadius = MathF.Max(0.5f * extent.Length(), 1e-3f);
        float sphereArea  = 4f * MathF.PI * sceneRadius * sceneRadius;
        float meanIrradiance = totalFlux / sphereArea;

        bool isIndirectDominant = meanIrradiance < IndirectDominantThreshold;
        _rrMinBounces  = isIndirectDominant ? RR_MinBounces_Indirect  : RR_MinBounces_Normal;
        _rrMinSurvival = isIndirectDominant ? RR_MinSurvival_Indirect : RR_MinSurvival_Normal;

        _emitterToLight = new Dictionary<Emissive, ILight>();
        foreach (var gl in lights.OfType<GeometryLight>())
        {
            // Last-write-wins if the same material is shared across multiple
            // geometry lights — that's degenerate and not expected in practice.
            _emitterToLight[gl.Material] = gl;
        }
        // Sphere/area lights register a visible emissive proxy so BSDF rays
        // can hit them (closing Veach's MIS estimator on smooth-specular
        // surfaces — see ILight.ProxyMaterial). The proxy material is bound
        // to its parent light here so WeightEmission can pull the light's own
        // PdfSolidAngle for the MIS weight at the next-bounce emission.
        foreach (var light in lights)
        {
            if (light.ProxyMaterial is { } proxy)
                _emitterToLight[proxy] = light;
        }
        _envLight = lights.OfType<EnvironmentLight>().FirstOrDefault();

        // Light importance sampling: build CDF for power/uniform strategies.
        // The 'all' strategy skips this — it sums every light directly.
        if (lightSamplingStrategy != LightSamplingStrategy.All && lights.Count > 0)
        {
            bool forceUniform = lightSamplingStrategy == LightSamplingStrategy.Uniform;
            _lightDist = new LightDistribution(lights, sceneBounds, forceUniform);
        }

        // Pre-warm the Kulla-Conty energy-compensation LUT on the construction
        // thread. Without this, the table is built lazily on first access,
        // which in the multi-threaded render path can saturate the shared
        // ThreadPool and deadlock. Pre-warming also moves the (~few-hundred-ms)
        // build cost out of the wall-clock render time and into Renderer setup.
        EnergyCompensationLut.Prewarm();
        
        if (isIndirectDominant)
        {
            Console.WriteLine($"  Lighting:    indirect-dominant (conservative RR)");
            if (_verbose)
            {
                Console.WriteLine($"  RR detail:   irradiance={meanIrradiance:F3}, " +
                                  $"flux={totalFlux:F1}, R={sceneRadius:F1}, " +
                                  $"minBounces={_rrMinBounces}, minSurvival={_rrMinSurvival:F2}");
            }
        }
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
    /// Uses Parallel.For over scanlines for multi-core rendering.
    /// </summary>
    public Vector3[,] Render(int width, int height)
    {
        var pixels = new Vector3[height, width];
        int completedRows = 0;
        int totalRows = height;

        // Two pixel-sampling strategies share the same TraceRay core:
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
        int sqrtSpp = (int)MathF.Ceiling(MathF.Sqrt(_samplesPerPixel));
        float invSqrtSpp = 1f / sqrtSpp;
        int actualSamples = useSobol ? _samplesPerPixel : sqrtSpp * sqrtSpp;

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
        {
            for (int i = 0; i < width; i++)
            {
                Vector3 cumulativeColor = Vector3.Zero;

                // Per-pixel scramble seed: combine pixel coordinates so each
                // pixel walks an independent Owen-scrambled sequence. Without
                // this, every pixel would start from the same Sobol prefix
                // and the resulting Moiré would be more visible than plain
                // PRNG noise — the very failure mode Owen scrambling exists
                // to fix.
                uint pixelSeed = (uint)(i * 73856093) ^ (uint)(j * 19349663);

                if (useSobol)
                {
                    for (int s = 0; s < actualSamples; s++)
                    {
                        Sampler.BeginPixelSample(pixelSeed, (uint)s);

                        // Let Sobol(2D) place the camera-jitter point natively.
                        // The (0,2,2)-net property guarantees the full set of
                        // spp points covers the pixel as a perfect stratified
                        // grid at every power-of-two prefix.
                        float jitterU = MathUtils.RandomFloat();
                        float jitterV = MathUtils.RandomFloat();

                        float u = (i + jitterU) / width;
                        float v = (height - j - 1 + jitterV) / height;

                        var ray = _camera.GetRay(u, v);
                        Vector3 sample = TraceRay(ray, _maxDepth, prevBsdfPdf: 0f, prevIsDelta: true,
                                                   pathThroughput: Vector3.One);

                        Sampler.EndPixelSample();

                        sample = ClampRadiance(sample);
                        cumulativeColor += sample;
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

                            float u = (i + jitterU) / width;
                            float v = (height - j - 1 + jitterV) / height;

                            var ray = _camera.GetRay(u, v);
                            Vector3 sample = TraceRay(ray, _maxDepth, prevBsdfPdf: 0f, prevIsDelta: true,
                                                       pathThroughput: Vector3.One);

                            sample = ClampRadiance(sample);
                            cumulativeColor += sample;
                        }
                    }
                }

                Vector3 linearColor = cumulativeColor / actualSamples;
                pixels[j, i] = AcesToneMap(linearColor);
            }

            int done = Interlocked.Increment(ref completedRows);
            if (done % 20 == 0 || done == totalRows)
            {
                float pct = 100f * done / totalRows;
                Console.Write($"\r  Rendering: {pct:F1}% ({done}/{totalRows} scanlines)   ");
            }
        });

        Console.WriteLine();
        return pixels;
    }

    /// <summary>
    /// ACES filmic tone mapping followed by gamma 2.2 correction.
    /// Provides natural highlight rolloff and richer colors compared to simple sqrt gamma.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 AcesToneMap(Vector3 color)
    {
        color = Vector3.Max(color, Vector3.Zero);

        // ACES filmic curve: (x * (2.51x + 0.03)) / (x * (2.43x + 0.59) + 0.14)
        Vector3 a = color * (2.51f * color + new Vector3(0.03f));
        Vector3 b = color * (2.43f * color + new Vector3(0.59f)) + new Vector3(0.14f);
        Vector3 mapped = new Vector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

        const float invGamma = 1f / 2.2f;
        return new Vector3(
            MathF.Pow(Math.Clamp(mapped.X, 0f, 1f), invGamma),
            MathF.Pow(Math.Clamp(mapped.Y, 0f, 1f), invGamma),
            MathF.Pow(Math.Clamp(mapped.Z, 0f, 1f), invGamma));
    }

    /// <summary>
    /// Clamps a radiance sample to suppress firefly artifacts.
    /// Also replaces NaN/Inf values with black to prevent corruption
    /// from propagating into the pixel accumulator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampRadiance(Vector3 color)
    {
        // NaN / Inf guard — any non-finite component becomes zero.
        if (float.IsNaN(color.X) || float.IsInfinity(color.X)) color.X = 0f;
        if (float.IsNaN(color.Y) || float.IsInfinity(color.Y)) color.Y = 0f;
        if (float.IsNaN(color.Z) || float.IsInfinity(color.Z)) color.Z = 0f;

        // Luminance-preserving clamp — scales the entire vector down
        // to prevent Hue-shifting heavily saturated bright highlights.
        float lum = MathUtils.Luminance(color);
        if (lum > _maxSampleRadiance)
        {
            color *= _maxSampleRadiance / lum;
        }

        return Vector3.Max(color, Vector3.Zero);
    }

    /// <summary>
    /// Clamps the indirect bounce radiance using the depth-aware secondary
    /// clamp threshold (<c>_indirectMaxSampleRadiance</c>). When the
    /// <c>--indirect-clamp-factor</c> is 1.0 (default) this is identical to
    /// <see cref="ClampRadiance"/> and has no observable effect. Setting the
    /// factor below 1 suppresses caustics / deep-specular fireflies that
    /// survive the primary pixel-level clamp.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampRadianceIndirect(Vector3 color)
    {
        if (float.IsNaN(color.X) || float.IsInfinity(color.X)) color.X = 0f;
        if (float.IsNaN(color.Y) || float.IsInfinity(color.Y)) color.Y = 0f;
        if (float.IsNaN(color.Z) || float.IsInfinity(color.Z)) color.Z = 0f;

        float lum = MathUtils.Luminance(color);
        if (lum > _indirectMaxSampleRadiance && lum > 0f)
            color *= _indirectMaxSampleRadiance / lum;

        return Vector3.Max(color, Vector3.Zero);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CORE PATH TRACER
    // ═════════════════════════════════════════════════════════════════════════

    // ═════════════════════════════════════════════════════════════════════════
    // Multiple Importance Sampling (Veach, balance heuristic)
    //
    // Each bounce threads forward (prevBsdfPdf, prevIsDelta) to the next
    // ShadeSurface / sky miss. This encodes three cases:
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
    //                                      that expose IMaterial.Sample() and
    //                                      return a non-delta reflection sample
    //                                      (Disney BSDF today).
    //
    //   prevIsDelta = false, pdf = 0       Legacy "NEE replaced emission" mode
    //                                      for materials that only expose
    //                                      Scatter() (Lambert, Metal, Dielectric).
    //                                      Emission from NEE-registered emitters
    //                                      is zeroed; unregistered emissives keep
    //                                      their full emission. This preserves
    //                                      the pre-MIS behavior without double-
    //                                      counting until those materials are
    //                                      migrated to the Sample() API.
    // ═════════════════════════════════════════════════════════════════════════

    private Vector3 ShadeSurface(Ray ray, HitRecord rec, int depth,
                                  float prevBsdfPdf, bool prevIsDelta,
                                  Vector3 currentAbsorption,
                                  Vector3 pathThroughput)
    {
        IMaterial? material = rec.Material;

        // ── Normal map perturbation ─────────────────────────────────────────
        if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
        {
            ApplyNormalMap(ref rec, material.NormalMap);
        }

        // ── Bump map perturbation (after normal map, Arnold/Cycles order) ──
        if (material?.BumpMap != null && rec.Tangent.LengthSquared() > 0.5f)
        {
            ApplyBumpMap(ref rec, material.BumpMap);
        }

        // ── Autobump (mesh-level, step 5 of the surface-displacement stack) ──
        // Applied AFTER the material's bump map so the composition order is
        //     normal_map → material.bump_map → mesh.autobump
        // on top of the already-displaced geometry. This is the
        // "macro silhouette via displacement, sub-pixel detail via autobump"
        // workflow Arnold pioneered with autobump_visibility — and it keeps
        // a clearcoat normal map (Disney) independent, by design.
        if (rec.AutoBump != null && rec.Tangent.LengthSquared() > 0.5f)
        {
            ApplyBumpMap(ref rec, rec.AutoBump);
        }

        // ── Emission (MIS-weighted) ─────────────────────────────────────────
        Vector3 emitted = Vector3.Zero;
        if (material != null)
        {
            Vector3 raw = material.Emit(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.FrontFace);
            if (raw.X > 0f || raw.Y > 0f || raw.Z > 0f)
            {
                emitted = WeightEmission(raw, material, ray, prevBsdfPdf, prevIsDelta);
            }
        }

        // viewDir is shared between NEE direct-lighting and the indirect
        // bounce's material.Sample() — compute it once and pass it down.
        // Previously each surface hit Normalized -ray.Direction twice
        // (once in ComputeDirectLighting, once below at the Sample call).
        Vector3 viewDir = Vector3.Normalize(-ray.Direction);

        // ── Direct lighting (Next Event Estimation, MIS-weighted) ───────────
        bool needsLightSampling = material?.NeedsDirectLighting ?? true;
        Vector3 directLight = needsLightSampling
            ? ComputeDirectLighting(rec, viewDir, material)
            : Vector3.Zero;

        if (material == null)
            return emitted + directLight;

        // ── Indirect bounce ─────────────────────────────────────────────────
        // Prefer the Sample() API when the material implements it — it gives
        // us a well-defined BSDF PDF for MIS at the next bounce. Fall back to
        // Scatter() for legacy materials (Lambert, Metal, Dielectric, Mix)
        // which still use the IsDeltaScatter-encoded suppression convention.
        BsdfSample? mis = material.Sample(viewDir, rec);
        if (mis.HasValue)
        {
            return ShadeSampleBounce(material, rec, mis.Value, depth, emitted, directLight,
                                     currentAbsorption, pathThroughput);
        }

        if (material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
        {
            // Path-throughput-based RR (PBRT §13.7.1). Survival probability is
            // driven by the cumulative β projected into this bounce, not the
            // local single-bounce attenuation. This kills paths whose total
            // contribution to the image has decayed through repeated dim
            // bounces, even when the current attenuation is moderate.
            Vector3 nextThroughput = pathThroughput * attenuation;
            int bouncesUsed = _maxDepth - depth;
            if (bouncesUsed >= _rrMinBounces)
            {
                float survivalProb = MathF.Max(nextThroughput.X,
                                       MathF.Max(nextThroughput.Y, nextThroughput.Z));
                survivalProb = MathF.Max(survivalProb, _rrMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f);
                if (MathUtils.RandomFloat() > survivalProb)
                    return emitted + directLight;
                float invSurvival = 1f / survivalProb;
                attenuation *= invSurvival;
                nextThroughput *= invSurvival;
            }

            if (attenuation.LengthSquared() < 0.001f)
                return emitted + directLight;

            // IsDeltaScatter materials (mirror/refraction) emit a delta bounce,
            // so emission passes through at full weight at the next hit. Non-
            // delta Scatter materials rely on the legacy "NEE replaced emission"
            // convention (prevBsdfPdf = 0) to suppress double-counting.
            bool nextIsDelta = material.IsDeltaScatter;
            // Legacy Scatter materials don't participate in volume stacking —
            // they don't emit medium-switch signals. Pass through the incoming
            // currentAbsorption so any enclosing Disney-glass interior still
            // absorbs along the continued ray segment.
            Vector3 indirect = TraceRay(scattered, depth - 1,
                                         prevBsdfPdf: 0f, prevIsDelta: nextIsDelta,
                                         currentAbsorption: currentAbsorption,
                                         pathThroughput: nextThroughput);
            // Depth-aware indirect clamp: suppresses deep-specular fireflies that
            // survive the primary pixel-level ClampRadiance. When the factor is
            // 1.0 (default) ClampRadianceIndirect == ClampRadiance — no change.
            indirect = ClampRadianceIndirect(indirect);
            // PBRT/Arnold convention: direct lighting is the standalone
            // rendering-equation integrand at the shadow-ray direction
            // (computed by ComputeDirectLighting above), and the scatter
            // attenuation weights ONLY the indirect path. Multiplying
            // direct lighting by the BSDF importance weight from a
            // randomly-sampled bounce direction couples two independent
            // estimators and biases the direct contribution for any
            // direction-dependent BSDF (Disney diffuse retro-reflection,
            // GGX specular, Charlie sheen). The legacy multiplied form
            // happened to be correct only for Lambertian (constant
            // attenuation == albedo); the new form is unbiased for every
            // material whose EvaluateDirect returns the full BRDF·cosθ.
            return emitted + directLight + attenuation * indirect;
        }

        return emitted + directLight;
    }

    /// <summary>
    /// Handles an indirect bounce using the material.Sample() path — produces
    /// a direction with an explicit BSDF PDF, applies Russian Roulette, and
    /// recurses into TraceRay with the MIS metadata.
    /// </summary>
    private Vector3 ShadeSampleBounce(IMaterial material, HitRecord rec,
                                       BsdfSample s, int depth,
                                       Vector3 emitted, Vector3 directLight,
                                       Vector3 currentAbsorption,
                                       Vector3 pathThroughput)
    {
        Vector3 attenuation;
        if (s.IsDelta)
        {
            // Delta lobe: F already carries the full attenuation (Fresnel ×
            // baseColor tint for transmission, etc.). No cos or pdf factor.
            attenuation = s.F;
        }
        else
        {
            // Non-delta sample. |NdotWo| handles both reflection (upper
            // hemisphere) and diffuse-transmission samples (lower hemisphere
            // for Disney's diff_trans lobe — Lambertian back-side scattering
            // through foliage / paper / fabric). The BSDF importance weight
            // is f · |cosθ| / pdf in both cases; the sign of NdotWo is
            // already captured by the BRDF / Pdf branch on hemisphere.
            float NdotWo = Vector3.Dot(rec.Normal, s.Wo);
            float absNdotWo = MathF.Abs(NdotWo);
            if (absNdotWo <= 0f || s.Pdf <= 0f)
                return emitted + directLight;
            attenuation = s.F * absNdotWo / s.Pdf;
        }

        if (attenuation.LengthSquared() < 0.001f)
            return emitted + directLight;

        // Path-throughput-based RR (PBRT §13.7.1). See ShadeSurface above for
        // the full rationale.
        Vector3 nextThroughput = pathThroughput * attenuation;
        int bouncesUsed = _maxDepth - depth;
        if (bouncesUsed >= _rrMinBounces)
        {
            float survivalProb = MathF.Max(nextThroughput.X,
                                   MathF.Max(nextThroughput.Y, nextThroughput.Z));
            survivalProb = MathF.Max(survivalProb, _rrMinSurvival);
            survivalProb = MathF.Min(survivalProb, 0.95f);
            if (MathUtils.RandomFloat() > survivalProb)
                return emitted + directLight;
            float invSurvival = 1f / survivalProb;
            attenuation *= invSurvival;
            nextThroughput *= invSurvival;
        }

        // Offset ray origin on the side of the normal that matches the outgoing
        // direction — transmission bounces sit on the far side of the surface.
        Vector3 offsetDir = Vector3.Dot(s.Wo, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
        var scattered = new Ray(MathUtils.OffsetOrigin(rec.Point, offsetDir), s.Wo);

        float nextPdf = s.IsDelta ? 0f : s.Pdf;
        bool nextIsDelta = s.IsDelta;
        // Interior-medium switch: refraction samples emit NextSegmentAbsorption
        // to tell the renderer to switch the σ_a tracked along the next segment
        // (entering glass → σ_a; exiting → vacuum). Reflection samples keep
        // the caller's currentAbsorption untouched.
        Vector3 nextAbsorption = s.NextSegmentAbsorption ?? currentAbsorption;
        Vector3 indirect = TraceRay(scattered, depth - 1, nextPdf, nextIsDelta,
                                     currentAbsorption: nextAbsorption,
                                     pathThroughput: nextThroughput);
        // Depth-aware indirect clamp — see ShadeSurface's Scatter path above.
        indirect = ClampRadianceIndirect(indirect);
        // PBRT/Arnold convention — see ShadeSurface above.
        return emitted + directLight + attenuation * indirect;
    }

    /// <summary>
    /// Computes the MIS weight w(p, q) under the configured heuristic
    /// (balance or power, see <see cref="MisHeuristic"/>). The denominator
    /// epsilon prevents 0/0 when both samplers report zero density.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float MisWeight(float p, float q)
    {
        if (_misHeuristic == MisHeuristic.Power)
        {
            float p2 = p * p;
            float q2 = q * q;
            return p2 / (p2 + q2 + 1e-30f);
        }
        return p / (p + q + 1e-30f);
    }

    /// <summary>
    /// Applies the MIS weight to surface emission at the current hit — the
    /// "BSDF-sample hit a light" half of Veach's estimator.
    /// </summary>
    private Vector3 WeightEmission(Vector3 rawEmission, IMaterial material, Ray ray,
                                    float prevBsdfPdf, bool prevIsDelta)
    {
        if (prevIsDelta)
            return rawEmission;

        if (material is Emissive em && _emitterToLight.TryGetValue(em, out var light))
        {
            float pLight = light.PdfSolidAngle(ray.Origin, ray.Direction);
            if (prevBsdfPdf + pLight <= 1e-20f)
                return Vector3.Zero;
            return rawEmission * MisWeight(prevBsdfPdf, pLight);
        }

        // Unregistered emitter (no NEE sampler could reach it) — full weight.
        return rawEmission;
    }

    /// <summary>
    /// Top-level radiance estimator. Decides between the surface-only path
    /// (no medium → bit-identical to pre-volumetric builds) and the volumetric
    /// path (homogeneous global medium with Beer-Lambert + free-path sampling).
    ///
    /// <paramref name="currentAbsorption"/> is the Beer-Lambert coefficient
    /// σ_a of whatever medium the ray is currently traversing (zero vector
    /// = vacuum, populated by refractive Disney samples when the ray enters
    /// a dielectric interior). A non-zero value multiplies the returned
    /// radiance by exp(-σ_a · t) where t is the distance the ray travelled
    /// before its next interaction. The global <see cref="_globalMedium"/>
    /// stacks multiplicatively with this interior absorption — they track
    /// independent media and are accumulated in series.
    /// </summary>
    private Vector3 TraceRay(Ray ray, int depth, float prevBsdfPdf, bool prevIsDelta,
                             Vector3 currentAbsorption = default,
                             Vector3 pathThroughput = default)
    {
        if (depth <= 0) return Vector3.Zero;

        // Dead-path early-out. Once the cumulative β has decayed below the
        // post-tone-map noise floor on every channel, the radiance this call
        // would return is multiplied back by β at the caller anyway, so the
        // BVH traversal + shading are pure waste. Cheap test, large win on
        // long bounce paths through dim/coloured surfaces.
        //
        // All call sites (camera rays in Render, every recursive Shade* /
        // volumetric branch) pass an explicit pathThroughput, so default
        // (zero) here genuinely means "no throughput" and short-circuits.
        float betaMax = MathF.Max(pathThroughput.X,
                          MathF.Max(pathThroughput.Y, pathThroughput.Z));
        if (betaMax < DeadPathThroughputEpsilon)
            return Vector3.Zero;

        // ── Camera-visibility filter (Arnold "camera" / Cycles "Ray
        //    Visibility → Camera") ──────────────────────────────────────────
        // On the primary camera ray only, advance past any hit flagged with
        // rec.CameraInvisible (set by CameraInvisibleHittable wrapping an
        // entity or light proxy with `visible_to_camera: false`). The light's
        // own proxy stays in the BVH for non-primary rays — mirror/glass
        // reflections, NEE shadow tests and BSDF-MIS closure continue to see
        // it unchanged. depth == _maxDepth uniquely identifies the primary
        // ray because every recursive TraceRay call decrements depth.
        const int MaxCameraInvisibleSkips = 8;
        bool isPrimaryRay = depth == _maxDepth;
        Ray currentRay = ray;
        var rec = new HitRecord();
        bool hit;
        int skipCount = 0;
        while (true)
        {
            rec = new HitRecord();
            hit = _world.Hit(currentRay, MathUtils.Epsilon, MathUtils.Infinity, ref rec);
            if (!hit || !isPrimaryRay || !rec.CameraInvisible) break;
            currentRay = new Ray(
                MathUtils.OffsetOrigin(rec.Point, currentRay.Direction),
                currentRay.Direction);
            if (++skipCount >= MaxCameraInvisibleSkips) break;
        }

        // ── Surface-only fast path (no medium) ──────────────────────────────
        if (_globalMedium == null)
        {
            Vector3 result = !hit
                ? SampleSky(currentRay, prevBsdfPdf, prevIsDelta)
                : ShadeSurface(currentRay, rec, depth, prevBsdfPdf, prevIsDelta, currentAbsorption,
                               pathThroughput);
            // Beer-Lambert along the segment just traversed. Sky miss with
            // non-zero σ_a means the ray escaped the bounded medium — the
            // exp(-σ_a · ∞) is zero for any absorbing channel, so we collapse
            // it to black; the common vacuum case skips this entirely.
            return ApplyBeerLambert(result, currentAbsorption, hit ? rec.T : float.PositiveInfinity);
        }

        // ── Volumetric path ─────────────────────────────────────────────────
        float tMax = hit ? rec.T : 1e30f;
        bool didScatter = _globalMedium.Sample(currentRay, tMax, out float tMed, out Vector3 beta, out _);

        if (didScatter)
        {
            // Medium scattering event at p = ray(tMed).
            Vector3 p = currentRay.Origin + currentRay.Direction * tMed;

            // NEE in-scattering: shadow ray to each light, weighted by phase × Tr.
            Vector3 Lnee = ComputeDirectLightingMedium(p, currentRay.Direction);

            // ── Russian Roulette on the indirect (phase-sampled) bounce ─────
            // Throughput-based: β_after = pathThroughput · medium-β. Same
            // motivation as the surface RR — a path that has already lost
            // most of its energy on the way to this scattering event should
            // be killed before spending another phase-sampled bounce.
            Vector3 Lind = Vector3.Zero;
            Vector3 medThroughput = pathThroughput * beta;
            int bouncesUsedS = _maxDepth - depth;
            float indirectScale = 1f;
            bool killIndirect = false;
            if (bouncesUsedS >= _rrMinBounces)
            {
                float survivalProb = MathF.Max(medThroughput.X,
                                       MathF.Max(medThroughput.Y, medThroughput.Z));
                survivalProb = MathF.Max(survivalProb, _rrMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f);
                if (MathUtils.RandomFloat() > survivalProb) killIndirect = true;
                else indirectScale = 1f / survivalProb;
            }

            if (!killIndirect)
            {
                // Indirect: importance-sample the phase function. The sampled
                // phase PDF is threaded forward as prevBsdfPdf so that the
                // next hit's emission / sky miss is MIS-weighted against the
                // NEE light PDF, mirroring the surface-side MIS.
                var (wi, phasePdf) = _globalMedium.Phase.Sample(currentRay.Direction);
                float phaseVal = _globalMedium.Phase.Evaluate(currentRay.Direction, wi);
                float phaseWeight = phasePdf > 1e-20f ? phaseVal / phasePdf : 0f;
                Vector3 nextThroughput = medThroughput * (phaseWeight * indirectScale);
                Lind = phaseWeight * indirectScale
                     * TraceRay(new Ray(p, wi), depth - 1,
                                 prevBsdfPdf: phasePdf, prevIsDelta: false,
                                 currentAbsorption: currentAbsorption,
                                 pathThroughput: nextThroughput);
            }

            return ApplyBeerLambert(beta * (Lnee + Lind), currentAbsorption, tMed);
        }

        // No medium event before tMax → continue with surface (or sky) shading,
        // attenuated by the medium throughput beta = Tr / pdf.
        Vector3 surfaceOrSky = !hit
            ? beta * SampleSky(currentRay, prevBsdfPdf, prevIsDelta)
            : beta * ShadeSurface(currentRay, rec, depth, prevBsdfPdf, prevIsDelta, currentAbsorption,
                                   pathThroughput * beta);
        return ApplyBeerLambert(surfaceOrSky, currentAbsorption, hit ? rec.T : float.PositiveInfinity);
    }

    /// <summary>
    /// exp(-σ_a · t) per channel. Vacuum (σ_a = 0) short-circuits the exp()
    /// calls; an infinite segment with any non-zero σ_a collapses the channel
    /// to zero (the ray was inside a bounded medium but didn't hit its
    /// boundary — treat as fully absorbed).
    /// </summary>
    private static Vector3 ApplyBeerLambert(Vector3 radiance, Vector3 sigma, float t)
    {
        if (sigma.X <= 0f && sigma.Y <= 0f && sigma.Z <= 0f) return radiance;
        if (float.IsPositiveInfinity(t)) return Vector3.Zero;
        if (t <= 0f) return radiance;
        return radiance * new Vector3(
            sigma.X > 0f ? MathF.Exp(-sigma.X * t) : 1f,
            sigma.Y > 0f ? MathF.Exp(-sigma.Y * t) : 1f,
            sigma.Z > 0f ? MathF.Exp(-sigma.Z * t) : 1f);
    }

    /// <summary>
    /// Evaluates the sky for a missed ray and applies the MIS weight for the
    /// "BSDF-sample escaped into the environment" half of the estimator.
    /// </summary>
    private Vector3 SampleSky(Ray ray, float prevBsdfPdf, bool prevIsDelta)
    {
        Vector3 sky = CalculateSkyColor(ray);

        // When the sky isn't registered as an NEE light, or this is a delta
        // bounce / camera ray, show it at full weight — nothing else sampled it.
        if (prevIsDelta || _envLight == null || !_envLight.Sky.CanSampleDirectly)
            return sky;

        float pLight = _envLight.PdfSolidAngle(ray.Origin, ray.Direction);
        if (prevBsdfPdf + pLight <= 1e-20f)
            return Vector3.Zero;
        return sky * MisWeight(prevBsdfPdf, pLight);
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
    ///    - material.EvaluateDirect(toLight, toEye, normal) for the BRDF shape
    ///    - balance-heuristic MIS weight w_nee = p_light / (p_light + p_bsdf)
    ///      using the material's own IMaterial.Pdf() in solid angle.
    ///
    /// For delta lights (point/directional/spot) no BSDF sampler can reach them,
    /// so w_nee = 1 unconditionally. For materials that do not implement Pdf()
    /// (Lambert, Metal, Dielectric, Mix) the default IMaterial.Pdf returns 0,
    /// which also yields w_nee = 1 — equivalent to the pre-MIS behavior and
    /// unbiased when paired with those materials' Scatter() legacy emission
    /// suppression.
    ///
    /// PBRT/Arnold direct-lighting convention:
    /// EvaluateDirect returns the FULL rendering-equation integrand at the
    /// shadow-ray direction — <c>f(V, L) · max(N·L, 0) · visibility</c> — with
    /// every material colour factor (baseColor, metallic Fresnel, sheen tint,
    /// subsurface tint) baked in. The caller adds this contribution directly
    /// to the radiance estimator: <c>L = Le + L_direct + scatter_attn × L_indirect</c>.
    /// The scatter attenuation never multiplies the direct term — that would
    /// couple the indirect importance sample to the shadow estimator and
    /// bias the result for any direction-dependent BSDF.
    /// </summary>
    private Vector3 ComputeDirectLighting(HitRecord rec, Vector3 viewDir, IMaterial? material)
    {
        Vector3 result = Vector3.Zero;

        if (_lightDist != null)
        {
            // ── Single-light picking (power or uniform) ──────────────────────
            // Unbiased single-sample estimator (PBRT §16.3.2):
            //   contribution = lightAccum / pPick
            // MIS uses pdf_combined = pPick × pLightSample for non-delta lights.
            if (_lights.Count == 0) return result;
            float xi = MathUtils.RandomFloat();
            var (lightIdx, pPick) = _lightDist.Sample(xi);
            ILight light = _lights[lightIdx];

            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                var (inShadow, lightColor, dirToLight, distance) =
                    SampleLight(light, rec.Point, rec.Normal, s);

                if (inShadow) continue;

                Vector3 brdf = material?.EvaluateDirect(dirToLight, viewDir, rec.Normal, rec)
                               ?? new Vector3(MathF.Max(Vector3.Dot(rec.Normal, dirToLight), 0f) / MathF.PI);

                Vector3 Tr = Vector3.One;
                if (_globalMedium != null)
                {
                    float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                    Tr = _globalMedium.Transmittance(new Ray(rec.Point, dirToLight), shadowDist);
                }

                // MIS: combined NEE pdf = pPick × pLightSample
                float wNee = 1f;
                if (!light.IsDelta && material != null)
                {
                    float pBsdf = material.Pdf(viewDir, dirToLight, rec);
                    if (pBsdf > 0f)
                    {
                        float pLightSample = light.PdfSolidAngle(rec.Point, dirToLight);
                        float pNee = pPick * pLightSample; // combined pdf for this direction
                        wNee = (pNee + pBsdf > 0f) ? MisWeight(pNee, pBsdf) : 1f;
                    }
                }

                lightAccum += wNee * lightColor * brdf * Tr;
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
                        SampleLight(light, rec.Point, rec.Normal, s);

                    if (inShadow) continue;

                    Vector3 brdf = material?.EvaluateDirect(dirToLight, viewDir, rec.Normal, rec)
                                   ?? new Vector3(MathF.Max(Vector3.Dot(rec.Normal, dirToLight), 0f) / MathF.PI);

                    Vector3 Tr = Vector3.One;
                    if (_globalMedium != null)
                    {
                        float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                        var shadowRay = new Ray(rec.Point, dirToLight);
                        Tr = _globalMedium.Transmittance(shadowRay, shadowDist);
                    }

                    // ── MIS weight (balance or power, configured per-renderer) ──
                    // Delta lights: always weight 1 (no BSDF sampler can hit them).
                    // Non-delta lights: if the material exposes Pdf() > 0, the
                    // weight reduces variance via Veach's combined estimator.
                    float wNee = 1f;
                    if (!light.IsDelta && material != null)
                    {
                        float pBsdf = material.Pdf(viewDir, dirToLight, rec);
                        if (pBsdf > 0f)
                        {
                            float pLight = light.PdfSolidAngle(rec.Point, dirToLight);
                            wNee = (pLight + pBsdf > 0f) ? MisWeight(pLight, pBsdf) : 1f;
                        }
                    }

                    lightAccum += wNee * lightColor * brdf * Tr;
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
        SampleLight(ILight light, Vector3 point, Vector3 normal, int sampleIndex)
    {
        return light switch
        {
            AreaLight al      => al.IlluminateAndTestStratified(point, normal, _world, sampleIndex),
            SphereLight sl    => sl.IlluminateAndTestStratified(point, normal, _world, sampleIndex),
            GeometryLight gl  => gl.IlluminateAndTestStratified(point, normal, _world, sampleIndex),
            DirectionalLight dl when dl.AngularRadiusDeg > 0f
                              => dl.IlluminateAndTestStratified(point, normal, _world, sampleIndex),
            SpotLight sp when sp.ShadowSamples > 1
                              => sp.IlluminateAndTestStratified(point, normal, _world, sampleIndex),
            _                 => light.IlluminateAndTest(point, normal, _world)
        };
    }

    /// <summary>
    /// Direct lighting at a medium scattering event.
    /// Mirrors ComputeDirectLighting but uses the phase function in place of
    /// the BRDF and attenuates each shadow ray by the medium transmittance.
    /// </summary>
    /// <param name="p">Scattering point in world space (along the ray at tMed).</param>
    /// <param name="wo">Direction of the ray that produced the scattering event
    ///                  (i.e. ray.Direction). Passed to the phase function as-is —
    ///                  IsotropicPhase / HenyeyGreensteinPhase use the convention
    ///                  that wo points "into" the event.</param>
    private Vector3 ComputeDirectLightingMedium(Vector3 p, Vector3 wo)
    {
        Vector3 result = Vector3.Zero;
        if (_globalMedium == null) return result;

        // Lights use `surfaceNormal` ONLY to offset the shadow-ray origin via
        // OffsetOrigin(p, n) = p + n × ε. For a volumetric scattering event
        // there is no surface to self-intersect with, so no offset is needed.
        // Passing Zero ensures OffsetOrigin returns the point unchanged.
        Vector3 dummyNormal = Vector3.Zero;

        if (_lightDist != null)
        {
            // ── Single-light picking in volumetric path ──────────────────────
            if (_lights.Count == 0) return result;
            float xi = MathUtils.RandomFloat();
            var (lightIdx, pPick) = _lightDist.Sample(xi);
            ILight light = _lights[lightIdx];

            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                var (inShadow, lightColor, dirToLight, distance) =
                    SampleLight(light, p, dummyNormal, s);

                if (inShadow) continue;

                float phaseVal = _globalMedium.Phase.Evaluate(wo, dirToLight);
                float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                Vector3 Tr = _globalMedium.Transmittance(new Ray(p, dirToLight), shadowDist);

                float wNee = 1f;
                if (!light.IsDelta)
                {
                    float phasePdf = _globalMedium.Phase.Pdf(wo, dirToLight);
                    if (phasePdf > 0f)
                    {
                        float pLightSample = light.PdfSolidAngle(p, dirToLight);
                        float pNee = pPick * pLightSample;
                        if (pNee + phasePdf > 0f)
                            wNee = MisWeight(pNee, phasePdf);
                    }
                }

                lightAccum += wNee * lightColor * phaseVal * Tr;
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
                        SampleLight(light, p, dummyNormal, s);

                    if (inShadow) continue;

                    // Phase function value for this in-scattering direction.
                    float phaseVal = _globalMedium.Phase.Evaluate(wo, dirToLight);

                    // Beer-Lambert attenuation along the shadow ray.
                    float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                    Vector3 Tr = _globalMedium.Transmittance(new Ray(p, dirToLight), shadowDist);

                    // ── MIS weight (phase-function vs light sampler) ────────────
                    // Delta lights cannot be reached by phase sampling; emit at
                    // full weight. Otherwise pair the analytic phase PDF against
                    // the light PDF using the configured heuristic.
                    float wNee = 1f;
                    if (!light.IsDelta)
                    {
                        float phasePdf = _globalMedium.Phase.Pdf(wo, dirToLight);
                        if (phasePdf > 0f)
                        {
                            float pLight = light.PdfSolidAngle(p, dirToLight);
                            if (pLight + phasePdf > 0f)
                                wNee = MisWeight(pLight, phasePdf);
                        }
                    }

                    lightAccum += wNee * lightColor * phaseVal * Tr;
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
    /// Bump-map perturbation. Mirrors <see cref="ApplyNormalMap"/> but the
    /// tangent-space normal comes from finite-difference gradients of a
    /// luminance height field rather than an RGB-encoded normal texture.
    ///
    /// Runs AFTER <see cref="ApplyNormalMap"/> when both are present:
    /// the TBN is re-orthogonalised against the already-perturbed
    /// <c>rec.Normal</c> so bump details sit on top of the normal-map
    /// medium-frequency relief (Arnold/Cycles composition order).
    /// </summary>
    private static void ApplyBumpMap(ref HitRecord rec, BumpMapTexture bump)
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

        // Sample with the unit T/B so the bump texture can perturb both UV
        // and 3D position consistently (3D procedurals like noise/marble
        // sample on p; image textures sample on u,v).
        Vector3 tsNormal = bump.SampleTangentNormal(
            rec.U, rec.V, rec.LocalPoint, T, B, rec.ObjectSeed);

        Vector3 perturbedNormal = Vector3.Normalize(
            T * tsNormal.X + B * tsNormal.Y + N * tsNormal.Z
        );

        rec.Normal = perturbedNormal;
    }
}
