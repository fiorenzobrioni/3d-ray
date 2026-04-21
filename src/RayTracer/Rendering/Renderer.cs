using System.Numerics;
using System.Linq;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

public class Renderer
{
    private readonly IHittable _world;
    private readonly Camera.Camera _camera;
    private readonly List<ILight> _lights;
    private readonly Vector3 _ambientLight;
    private readonly SkySettings _sky;
    private readonly int _maxDepth;
    private readonly int _samplesPerPixel;
    private readonly Dictionary<Emissive, ILight> _emitterToLight;
    private readonly EnvironmentLight? _envLight;
    private readonly IMedium? _globalMedium;

    // ── Russian Roulette configuration ──────────────────────────────────────
    //
    // RR is scene-adaptive: the renderer detects at construction whether the
    // scene relies primarily on indirect/emissive light (no or dim explicit
    // lights) and adjusts the RR aggressiveness accordingly.
    //
    // Normal scenes (bright explicit lights):
    //   MinBounces = 4, MinSurvival = 0.15 → max boost 7×
    //   Direct lighting (NEE) carries most energy, indirect is correction.
    //   Aggressive RR is fine — killed paths lose only the small indirect term.
    //
    // Indirect-dominant scenes (emissive-only or very dim lights):
    //   MinBounces = 8, MinSurvival = 0.5 → max boost 2×
    //   ALL energy comes from indirect bounces hitting emissive surfaces.
    //   Aggressive RR kills paths BEFORE they find the emissive → dark spots.
    //   Conservative RR lets more paths survive to find the light source.
    //
    private const int RR_MinBounces_Normal   = 4;
    private const float RR_MinSurvival_Normal = 0.15f;

    private const int RR_MinBounces_Indirect   = 8;
    private const float RR_MinSurvival_Indirect = 0.5f;

    // Effective values, computed at construction based on scene analysis
    private readonly int _rrMinBounces;
    private readonly float _rrMinSurvival;

    // ── Firefly suppression ─────────────────────────────────────────────────
    // Maximum per-sample radiance (before tone mapping). Sits just above the
    // safety net that catches any remaining outliers from RR boost, Disney
    // lobe compensation, specular caustics, or NaN/Inf from edge cases.
    // Increased from 15f to 100f to prevent catastrophic energy loss on
    // highly emissive elements, while still removing true numerical spikes.
    // Can be overridden via the constructor (CLI flag --clamp/-C).
    public const float DefaultMaxSampleRadiance = 100f;
    private readonly float _maxSampleRadiance;

    // Threshold below which the scene is considered indirect-dominant.
    // Computed as the sum of luminance of all explicit lights evaluated at
    // the world origin. Scenes with total light power below this rely
    // primarily on emissive geometry and sky for illumination.
    private const float IndirectDominantThreshold = 1.0f;

    public Renderer(
        IHittable world,
        Camera.Camera camera,
        List<ILight> lights,
        Vector3 ambientLight,
        SkySettings sky,
        int samplesPerPixel,
        int maxDepth,
        IMedium? globalMedium = null,
        float? maxSampleRadiance = null)
    {
        _world = world;
        _camera = camera;
        _lights = lights;
        _ambientLight = ambientLight;
        _sky = sky;
        _samplesPerPixel = samplesPerPixel;
        _maxDepth = maxDepth;
        _globalMedium = globalMedium;
        _maxSampleRadiance = maxSampleRadiance ?? DefaultMaxSampleRadiance;

        // ── Scene analysis: detect indirect-dominant lighting ────────────
        // Evaluate all explicit lights at the world origin to get a rough
        // estimate of total direct light power. If this is very low, the
        // scene relies on emissive geometry or sky → use conservative RR.
        float totalLightPower = 0f;
        foreach (var light in lights)
        {
            var (color, _, _) = light.Illuminate(Vector3.Zero);
            totalLightPower += MathUtils.Luminance(color);
        }

        bool isIndirectDominant = totalLightPower < IndirectDominantThreshold;
        _rrMinBounces  = isIndirectDominant ? RR_MinBounces_Indirect  : RR_MinBounces_Normal;
        _rrMinSurvival = isIndirectDominant ? RR_MinSurvival_Indirect : RR_MinSurvival_Normal;

        _emitterToLight = new Dictionary<Emissive, ILight>();
        foreach (var gl in lights.OfType<GeometryLight>())
        {
            // Last-write-wins if the same material is shared across multiple
            // geometry lights — that's degenerate and not expected in practice.
            _emitterToLight[gl.Material] = gl;
        }
        _envLight = lights.OfType<EnvironmentLight>().FirstOrDefault();

        // Pre-warm the Kulla-Conty energy-compensation LUT on the construction
        // thread. Without this, the table is built lazily on first access,
        // which in the multi-threaded render path can saturate the shared
        // ThreadPool and deadlock. Pre-warming also moves the (~few-hundred-ms)
        // build cost out of the wall-clock render time and into Renderer setup.
        EnergyCompensationLut.Prewarm();
        
        if (isIndirectDominant)
        {
            Console.WriteLine($"  Scene analysis: indirect-dominant lighting detected " +
                              $"(light power {totalLightPower:F3}). " +
                              $"Using conservative RR (minBounces={_rrMinBounces}, " +
                              $"minSurvival={_rrMinSurvival:F2}).");
        }
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

        // Pre-compute stratification grid dimensions
        int sqrtSpp = (int)MathF.Ceiling(MathF.Sqrt(_samplesPerPixel));
        int actualSamples = sqrtSpp * sqrtSpp;
        float invSqrtSpp = 1f / sqrtSpp;

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
        {
            for (int i = 0; i < width; i++)
            {
                Vector3 cumulativeColor = Vector3.Zero;

                // Stratified sampling: divide pixel into sqrtSpp x sqrtSpp grid
                for (int sy = 0; sy < sqrtSpp; sy++)
                {
                    for (int sx = 0; sx < sqrtSpp; sx++)
                    {
                        float jitterU = (sx + MathUtils.RandomFloat()) * invSqrtSpp;
                        float jitterV = (sy + MathUtils.RandomFloat()) * invSqrtSpp;

                        float u = (i + jitterU) / width;
                        float v = (height - j - 1 + jitterV) / height;

                        var ray = _camera.GetRay(u, v);
                        // Camera rays: treat as "delta" so emission at the primary
                        // hit is shown at full weight.
                        Vector3 sample = TraceRay(ray, _maxDepth, prevBsdfPdf: 0f, prevIsDelta: true);

                        // ── Firefly suppression ────────────────────────────
                        // Clamp individual sample radiance to prevent outliers
                        // (caused by specular caustics, RR boosting, or lobe
                        // probability compensation) from dominating the average.
                        // The threshold is generous enough to preserve all
                        // legitimate HDR detail while killing extreme spikes.
                        sample = ClampRadiance(sample);

                        cumulativeColor += sample;
                    }
                }

                Vector3 linearColor = cumulativeColor / actualSamples;
                pixels[j, i] = AcesToneMap(linearColor);
            }

            int done = Interlocked.Increment(ref completedRows);
            if (done % 20 == 0 || done == totalRows)
            {
                float pct = 100f * done / totalRows;
                Console.Write($"\rRendering: {pct:F1}% ({done}/{totalRows} scanlines)   ");
            }
        });

        Console.WriteLine();
        return pixels;
    }

    /// <summary>
    /// ACES filmic tone mapping followed by gamma 2.2 correction.
    /// Provides natural highlight rolloff and richer colors compared to simple sqrt gamma.
    /// </summary>
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
                                  Vector3 currentAbsorption)
    {
        IMaterial? material = rec.Material;

        // ── Normal map perturbation ─────────────────────────────────────────
        if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
        {
            ApplyNormalMap(ref rec, material.NormalMap);
        }

        float diffuseWeight = material?.DiffuseWeight ?? 1f;
        float specExponent  = material?.SpecularExponent ?? 0f;

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

        // ── Direct lighting (Next Event Estimation, MIS-weighted) ───────────
        bool needsLightSampling = (diffuseWeight > 0f) || (specExponent > 0f);
        Vector3 directLight = needsLightSampling
            ? ComputeDirectLighting(rec, ray, material)
            : Vector3.Zero;

        if (material == null)
            return emitted + directLight;

        // ── Indirect bounce ─────────────────────────────────────────────────
        // Prefer the Sample() API when the material implements it — it gives
        // us a well-defined BSDF PDF for MIS at the next bounce. Fall back to
        // Scatter() for legacy materials (Lambert, Metal, Dielectric, Mix)
        // which still use the diffuseWeight-encoded suppression convention.
        Vector3 viewDir = Vector3.Normalize(-ray.Direction);
        BsdfSample? mis = material.Sample(viewDir, rec);
        if (mis.HasValue)
        {
            return ShadeSampleBounce(material, rec, mis.Value, depth, emitted, directLight,
                                     currentAbsorption);
        }

        if (material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
        {
            int bouncesUsed = _maxDepth - depth;
            if (bouncesUsed >= _rrMinBounces)
            {
                float survivalProb = MathF.Max(MathUtils.Luminance(attenuation), _rrMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f);
                if (MathUtils.RandomFloat() > survivalProb)
                    return emitted + attenuation * directLight;
                attenuation /= survivalProb;
            }

            if (attenuation.LengthSquared() < 0.001f)
                return emitted + directLight * attenuation;

            // Legacy encoding: purely specular scatter (diffuseWeight = 0) =>
            // delta bounce, emission passes through. Diffuse scatter => NEE
            // replaces emission, suppress registered emitters at next hit.
            bool nextIsDelta = diffuseWeight <= 0f;
            // Legacy Scatter materials don't participate in volume stacking —
            // they don't emit medium-switch signals. Pass through the incoming
            // currentAbsorption so any enclosing Disney-glass interior still
            // absorbs along the continued ray segment.
            Vector3 indirect = TraceRay(scattered, depth - 1,
                                         prevBsdfPdf: 0f, prevIsDelta: nextIsDelta,
                                         currentAbsorption: currentAbsorption);
            return emitted + attenuation * (directLight + indirect);
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
                                       Vector3 currentAbsorption)
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
            float NdotWo = Vector3.Dot(rec.Normal, s.Wo);
            if (NdotWo <= 0f || s.Pdf <= 0f)
                return emitted + directLight;
            attenuation = s.F * NdotWo / s.Pdf;
        }

        if (attenuation.LengthSquared() < 0.001f)
            return emitted + directLight * attenuation;

        int bouncesUsed = _maxDepth - depth;
        if (bouncesUsed >= _rrMinBounces)
        {
            float survivalProb = MathF.Max(MathUtils.Luminance(attenuation), _rrMinSurvival);
            survivalProb = MathF.Min(survivalProb, 0.95f);
            if (MathUtils.RandomFloat() > survivalProb)
                return emitted + attenuation * directLight;
            attenuation /= survivalProb;
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
                                     currentAbsorption: nextAbsorption);
        return emitted + attenuation * (directLight + indirect);
    }

    /// <summary>
    /// Applies the balance-heuristic MIS weight to surface emission at the
    /// current hit — the "BSDF-sample hit a light" half of Veach's estimator.
    /// </summary>
    private Vector3 WeightEmission(Vector3 rawEmission, IMaterial material, Ray ray,
                                    float prevBsdfPdf, bool prevIsDelta)
    {
        if (prevIsDelta)
            return rawEmission;

        if (material is Emissive em && _emitterToLight.TryGetValue(em, out var light))
        {
            float pLight = light.PdfSolidAngle(ray.Origin, ray.Direction);
            float denom = prevBsdfPdf + pLight;
            if (denom <= 1e-20f)
                return Vector3.Zero;
            float wBsdf = prevBsdfPdf / denom;
            return rawEmission * wBsdf;
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
                             Vector3 currentAbsorption = default)
    {
        if (depth <= 0) return Vector3.Zero;

        var rec = new HitRecord();
        bool hit = _world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec);

        // ── Surface-only fast path (no medium) ──────────────────────────────
        if (_globalMedium == null)
        {
            Vector3 result = !hit
                ? SampleSky(ray, prevBsdfPdf, prevIsDelta)
                : ShadeSurface(ray, rec, depth, prevBsdfPdf, prevIsDelta, currentAbsorption);
            // Beer-Lambert along the segment just traversed. Sky miss with
            // non-zero σ_a means the ray escaped the bounded medium — the
            // exp(-σ_a · ∞) is zero for any absorbing channel, so we collapse
            // it to black; the common vacuum case skips this entirely.
            return ApplyBeerLambert(result, currentAbsorption, hit ? rec.T : float.PositiveInfinity);
        }

        // ── Volumetric path ─────────────────────────────────────────────────
        float tMax = hit ? rec.T : 1e30f;
        bool didScatter = _globalMedium.Sample(ray, tMax, out float tMed, out Vector3 beta, out _);

        if (didScatter)
        {
            // Medium scattering event at p = ray(tMed).
            Vector3 p = ray.Origin + ray.Direction * tMed;

            // NEE in-scattering: shadow ray to each light, weighted by phase × Tr.
            Vector3 Lnee = ComputeDirectLightingMedium(p, ray.Direction);

            // ── Russian Roulette on the indirect (phase-sampled) bounce ─────
            Vector3 Lind = Vector3.Zero;
            int bouncesUsedS = _maxDepth - depth;
            float indirectScale = 1f;
            bool killIndirect = false;
            if (bouncesUsedS >= _rrMinBounces)
            {
                float survivalProb = MathF.Max(MathUtils.Luminance(beta), _rrMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f);
                if (MathUtils.RandomFloat() > survivalProb) killIndirect = true;
                else indirectScale = 1f / survivalProb;
            }

            if (!killIndirect)
            {
                // Indirect: importance-sample the phase function. Phase-MIS is
                // not implemented yet — the recursive call uses legacy "NEE
                // replaced emission" semantics (prevBsdfPdf=0, prevIsDelta=false)
                // so the next hit's registered emission is suppressed to avoid
                // double-counting with ComputeDirectLightingMedium above.
                var (wi, phasePdf) = _globalMedium.Phase.Sample(ray.Direction);
                float phaseVal = _globalMedium.Phase.Evaluate(ray.Direction, wi);
                float phaseWeight = phasePdf > 1e-20f ? phaseVal / phasePdf : 0f;
                Lind = phaseWeight * indirectScale
                     * TraceRay(new Ray(p, wi), depth - 1,
                                 prevBsdfPdf: 0f, prevIsDelta: false,
                                 currentAbsorption: currentAbsorption);
            }

            return ApplyBeerLambert(beta * (Lnee + Lind), currentAbsorption, tMed);
        }

        // No medium event before tMax → continue with surface (or sky) shading,
        // attenuated by the medium throughput beta = Tr / pdf.
        Vector3 surfaceOrSky = !hit
            ? beta * SampleSky(ray, prevBsdfPdf, prevIsDelta)
            : beta * ShadeSurface(ray, rec, depth, prevBsdfPdf, prevIsDelta, currentAbsorption);
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
        float denom = prevBsdfPdf + pLight;
        if (denom <= 1e-20f)
            return Vector3.Zero;
        float wBsdf = prevBsdfPdf / denom;
        return sky * wBsdf;
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
    /// The material albedo/color is NOT included in EvaluateDirect — it is applied
    /// by TraceRay via the scatter attenuation, keeping direct and indirect paths
    /// energetically consistent.
    /// </summary>
    private Vector3 ComputeDirectLighting(HitRecord rec, Ray incomingRay, IMaterial? material)
    {
        Vector3 result = _ambientLight;

        Vector3 viewDir = Vector3.Normalize(-incomingRay.Direction);

        foreach (var light in _lights)
        {
            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                bool inShadow;
                Vector3 lightColor;
                Vector3 dirToLight;
                float distance;

                if (light is AreaLight areaLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        areaLight.IlluminateAndTestStratified(rec.Point, rec.Normal, _world, s);
                }
                else if (light is SphereLight sphereLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        sphereLight.IlluminateAndTestStratified(rec.Point, rec.Normal, _world, s);
                }
                else if (light is GeometryLight geometryLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        geometryLight.IlluminateAndTestStratified(rec.Point, rec.Normal, _world, s);
                }
                else
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        light.IlluminateAndTest(rec.Point, rec.Normal, _world);
                }

                if (inShadow) continue;

                Vector3 brdf = material?.EvaluateDirect(dirToLight, viewDir, rec.Normal, rec)
                               ?? new Vector3(MathF.Max(Vector3.Dot(rec.Normal, dirToLight), 0f));

                Vector3 Tr = Vector3.One;
                if (_globalMedium != null)
                {
                    float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                    var shadowRay = new Ray(rec.Point, dirToLight);
                    Tr = _globalMedium.Transmittance(shadowRay, shadowDist);
                }

                // ── MIS balance-heuristic weight ────────────────────────────
                // Delta lights: always weight 1 (no BSDF sampler can hit them).
                // Non-delta lights: if the material exposes Pdf() > 0, the
                // weight reduces variance via Veach's balance heuristic.
                float wNee = 1f;
                if (!light.IsDelta && material != null)
                {
                    float pBsdf = material.Pdf(viewDir, dirToLight, rec);
                    if (pBsdf > 0f)
                    {
                        float pLight = light.PdfSolidAngle(rec.Point, dirToLight);
                        float denom = pLight + pBsdf;
                        wNee = denom > 0f ? pLight / denom : 1f;
                    }
                }

                lightAccum += wNee * lightColor * brdf * Tr;
            }

            result += lightAccum;
        }

        return result;
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
                bool inShadow;
                Vector3 lightColor;
                Vector3 dirToLight;
                float distance;

                if (light is AreaLight areaLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        areaLight.IlluminateAndTestStratified(p, dummyNormal, _world, s);
                }
                else if (light is SphereLight sphereLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        sphereLight.IlluminateAndTestStratified(p, dummyNormal, _world, s);
                }
                else if (light is GeometryLight geometryLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        geometryLight.IlluminateAndTestStratified(p, dummyNormal, _world, s);
                }
                else
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        light.IlluminateAndTest(p, dummyNormal, _world);
                }

                if (inShadow) continue;

                // Phase function value for this in-scattering direction.
                float phaseVal = _globalMedium.Phase.Evaluate(wo, dirToLight);

                // Beer-Lambert attenuation along the shadow ray.
                float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                Vector3 Tr = _globalMedium.Transmittance(new Ray(p, dirToLight), shadowDist);

                lightAccum += lightColor * phaseVal * Tr;
            }

            result += lightAccum;
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
}
