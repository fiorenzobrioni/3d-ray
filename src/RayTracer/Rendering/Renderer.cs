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
    private readonly HashSet<Emissive> _registeredEmitterMaterials;
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

        _registeredEmitterMaterials = lights
            .OfType<GeometryLight>()
            .Select(gl => gl.Material)
            .ToHashSet();
        
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
                        Vector3 sample = TraceRay(ray, _maxDepth, prevUsedNee: false);

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

    private Vector3 ShadeSurface(Ray ray, HitRecord rec, int depth, bool prevUsedNee)
    { 
        // ── Material properties ─────────────────────────────────────────────
        IMaterial? material = rec.Material;
 
        // ── Normal map perturbation ─────────────────────────────────────────
        if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
        {
            ApplyNormalMap(ref rec, material.NormalMap);
        }
 
        float diffuseWeight = material?.DiffuseWeight ?? 1f;
        float specExponent  = material?.SpecularExponent ?? 0f;
        float specStrength  = material?.SpecularStrength ?? 0f;
 
        // ── Emission ────────────────────────────────────────────────────────
        // Double-counting guard:
        //   When prevUsedNee=true the previous surface was diffuse and fired NEE,
        //   which already sampled this emitter's direct contribution. Adding Emit()
        //   again here would count the same light twice.
        //
        //   We suppress emission ONLY for emitters registered in GeometryLight
        //   (i.e. those that NEE can actually reach). Unregistered emissives
        //   (e.g. back-faces, non-ISamplable objects) always emit normally.
        //
        //   Camera → emitter directly: prevUsedNee=false → emission shown. ✓
        //   Mirror  → emitter:         prevUsedNee=false (specular, no NEE) → shown. ✓
        //   Diffuse → emitter (NEE on): prevUsedNee=true → suppressed. ✓
        Vector3 emitted = Vector3.Zero;
        if (material != null)
        {
            bool suppressEmission = prevUsedNee
                && material is Emissive em
                && _registeredEmitterMaterials.Contains(em);
 
            if (!suppressEmission)
                emitted = material.Emit(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.FrontFace);
        }
 
        // ── Direct lighting (Next Event Estimation) ─────────────────────────
        // Pure emitters (diffuseWeight = 0, specExponent = 0) do NOT receive
        // external illumination — they are the light source. Adding ambient to
        // them would double-bias the emission. Only non-emissive hits carry the
        // ambient fill and fire NEE.
        bool needsLightSampling = (diffuseWeight > 0f) || (specExponent > 0f);
        Vector3 directLight = needsLightSampling
            ? ComputeDirectLighting(rec, ray, material)
            : Vector3.Zero;
 
        // ── Scatter (indirect lighting) ─────────────────────────────────────
        if (material != null && material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
        {
            // ── Russian Roulette ────────────────────────────────────────────
            int bouncesUsed = _maxDepth - depth;
            if (bouncesUsed >= _rrMinBounces)
            {
                float survivalProb = MathF.Max(MathUtils.Luminance(attenuation), _rrMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f);
 
                if (MathUtils.RandomFloat() > survivalProb)
                    return emitted + attenuation * directLight;
 
                attenuation /= survivalProb;
            }
 
            // Skip indirect recursion for near-black materials (optimisation)
            if (attenuation.LengthSquared() < 0.001f)
                return emitted + directLight * attenuation;
 
            // Pass prevUsedNee for the next bounce's double-counting guard.
            //
            // Only suppress emission when THIS surface has a diffuse NEE
            // component (diffuseWeight > 0). The diffuse lobe's NEE properly
            // samples emitters via ComputeDirectLighting — adding the emitter's
            // Emit() on the next bounce would double-count that energy.
            //
            // Purely specular surfaces (Dielectric, smooth Metal with fuzz=0)
            // have diffuseWeight=0. Their NEE is just a small approximation
            // (Blinn-Phong glint or narrow GGX peak) that does NOT replace the
            // traced reflected ray as the primary path to see emissive objects.
            // Suppressing emission on reflected rays would make glass and mirrors
            // unable to reflect registered emissive lights → black reflections.
            //
            // This decouples "needs NEE for direct lighting" (needsLightSampling)
            // from "NEE properly replaces the emitter contribution" (diffuseWeight > 0).
            bool neeReplacesEmission = diffuseWeight > 0f;
            Vector3 indirect = TraceRay(scattered, depth - 1, prevUsedNee: neeReplacesEmission);
            return emitted + attenuation * (directLight + indirect);
        }
 
        // No scatter (fully absorbed) — return direct light contribution only
        return emitted + directLight;
    }

    /// <summary>
    /// Top-level radiance estimator. Decides between the surface-only path
    /// (no medium → bit-identical to pre-volumetric builds) and the volumetric
    /// path (homogeneous global medium with Beer-Lambert + free-path sampling).
    /// </summary>
    private Vector3 TraceRay(Ray ray, int depth, bool prevUsedNee)
    {
        if (depth <= 0) return Vector3.Zero;

        var rec = new HitRecord();
        bool hit = _world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec);

        // ── Surface-only fast path (no medium) ──────────────────────────────
        // Consumes zero extra random numbers → output is bit-identical to the
        // pre-volumetric renderer when world.medium is absent.
        if (_globalMedium == null)
        {
            if (!hit) return CalculateSkyColor(ray);
            return ShadeSurface(ray, rec, depth, prevUsedNee);
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
            // Applied only to the recursive continuation, not to Lnee, so the
            // estimator stays unbiased regardless of the kill/survive outcome.
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
                // Indirect: importance-sample the phase function.
                // We keep phase/pdf explicit so future phase functions where
                // Sample.Pdf ≠ Evaluate (e.g. multi-lobe / truncated) remain
                // unbiased. For Isotropic and HG this factor collapses to 1.
                var (wi, phasePdf) = _globalMedium.Phase.Sample(ray.Direction);
                float phaseVal = _globalMedium.Phase.Evaluate(ray.Direction, wi);
                float phaseWeight = phasePdf > 1e-20f ? phaseVal / phasePdf : 0f;
                Lind = phaseWeight * indirectScale
                     * TraceRay(new Ray(p, wi), depth - 1, prevUsedNee: true);
            }

            return beta * (Lnee + Lind);
        }

        // No medium event before tMax → continue with surface (or sky) shading,
        // attenuated by the medium throughput beta = Tr / pdf.
        if (!hit) return beta * CalculateSkyColor(ray);
        return beta * ShadeSurface(ray, rec, depth, prevUsedNee);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DIRECT LIGHTING (NEXT EVENT ESTIMATION)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes direct illumination from all lights at a surface hit point.
    ///
    /// 0. Ambient fill — applied unconditionally to all materials.
    /// 1. Per-light: shadow test + material.EvaluateDirect(toLight, toEye, normal).
    ///    EvaluateDirect encapsulates the BRDF shape (Lambert N·L + Fresnel-boosted
    ///    Blinn-Phong), replacing the previous hard-coded diffuseWeight/specExponent/
    ///    specStrength triple. Each material provides its own physically-calibrated
    ///    implementation; the default in IMaterial matches the previous behaviour.
    ///
    /// The material albedo/color is NOT included in EvaluateDirect — it is applied
    /// by TraceRay via the scatter attenuation, keeping direct and indirect paths
    /// energetically consistent.
    /// </summary>
    private Vector3 ComputeDirectLighting(HitRecord rec, Ray incomingRay, IMaterial? material)
    {
        // ── Ambient fill ────────────────────────────────────────────────────
        // Applied to ALL materials: diffuse, metal, glass.
        // Not gated by diffuseWeight — a mirror in a room reflects ambient light.
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

                // EvaluateDirect: BRDF shape factor (diffuse N·L + specular with Fresnel).
                Vector3 brdf = material?.EvaluateDirect(dirToLight, viewDir, rec.Normal, rec)
                               ?? new Vector3(MathF.Max(Vector3.Dot(rec.Normal, dirToLight), 0f));

                // Volumetric attenuation along the shadow ray (Beer-Lambert).
                // No-op when there is no global medium → bit-identical to surface-only path.
                Vector3 Tr = Vector3.One;
                if (_globalMedium != null)
                {
                    float shadowDist = float.IsInfinity(distance) ? 1e30f : distance;
                    var shadowRay = new Ray(rec.Point, dirToLight);
                    Tr = _globalMedium.Transmittance(shadowRay, shadowDist);
                }

                lightAccum += lightColor * brdf * Tr;
            }
 
            // AreaLight pre-divides by ShadowSamples in its energy formula → sum (not average).
            // Point/Directional/Spot always have ShadowSamples=1 → loop runs once.
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
