using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;

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
    private const float MaxSampleRadiance = 100f;

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
        int maxDepth)
    {
        _world = world;
        _camera = camera;
        _lights = lights;
        _ambientLight = ambientLight;
        _sky = sky;
        _samplesPerPixel = samplesPerPixel;
        _maxDepth = maxDepth;

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
                        Vector3 sample = TraceRay(ray, _maxDepth);

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
    private static Vector3 ClampRadiance(Vector3 color)
    {
        // NaN / Inf guard — any non-finite component becomes zero.
        if (float.IsNaN(color.X) || float.IsInfinity(color.X)) color.X = 0f;
        if (float.IsNaN(color.Y) || float.IsInfinity(color.Y)) color.Y = 0f;
        if (float.IsNaN(color.Z) || float.IsInfinity(color.Z)) color.Z = 0f;

        // Luminance-preserving clamp — scales the entire vector down
        // to prevent Hue-shifting heavily saturated bright highlights.
        float lum = MathUtils.Luminance(color);
        if (lum > MaxSampleRadiance)
        {
            color *= MaxSampleRadiance / lum;
        }

        return Vector3.Max(color, Vector3.Zero);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CORE PATH TRACER
    // ═════════════════════════════════════════════════════════════════════════

    private Vector3 TraceRay(Ray ray, int depth)
    {
        if (depth <= 0)
            return Vector3.Zero;

        HitRecord rec = default;

        if (!_world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec))
            return CalculateSkyColor(ray);

        // ── Material properties ─────────────────────────────────────────────
        IMaterial? material = rec.Material;

        // ── Normal map perturbation ─────────────────────────────────────
        // If the material has a normal map AND the hit record has valid TBN
        // vectors, perturb the shading normal BEFORE any lighting or scatter.
        // This affects everything: direct lighting N·L, specular N·H,
        // scatter direction, and emission face test.
        if (material?.NormalMap != null && rec.Tangent.LengthSquared() > 0.5f)
        {
            ApplyNormalMap(ref rec, material.NormalMap);
        }


        float diffuseWeight = material?.DiffuseWeight ?? 1f;
        float specExponent = material?.SpecularExponent ?? 0f;
        float specStrength = material?.SpecularStrength ?? 0f;

        // ── Emission ────────────────────────────────────────────────────────
        // Emissive materials add their own radiance independently of any
        // external lighting or scattering. This is additive and NOT modulated
        // by attenuation — the surface IS the light source.
        Vector3 emitted = material?.Emit(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.FrontFace)
                          ?? Vector3.Zero;

        // ── Direct lighting (Next Event Estimation) ─────────────────────────
        // The ambient term is ALWAYS included for all materials — it represents
        // omnidirectional fill light. Even mirrors and glass interact with it:
        //   - Mirrors reflect it (tinted by attenuation = metal color)
        //   - Glass transmits it (tinted by attenuation = glass tint)
        //   - Diffuse surfaces scatter it (weighted by attenuation = albedo)
        // The per-light diffuse/specular components are gated by material props.
        Vector3 directLight = _ambientLight;
        bool needsLightSampling = (diffuseWeight > 0f) || (specExponent > 0f);

        if (needsLightSampling)
            directLight = ComputeDirectLighting(rec, ray, diffuseWeight, specExponent, specStrength);

        // ── Scatter (indirect lighting) ─────────────────────────────────────
        if (material != null && material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
        {
            // ── Russian Roulette ────────────────────────────────────────────
            // After a minimum number of bounces, probabilistically terminate
            // low-energy paths. The surviving paths are boosted by 1/p to keep
            // the estimator unbiased. This is more efficient and less biased
            // than hard-cutting at maxDepth.
            int bouncesUsed = _maxDepth - depth;
            if (bouncesUsed >= _rrMinBounces)
            {
                float survivalProb = MathF.Max(MathUtils.Luminance(attenuation), _rrMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f); // Cap to avoid infinite paths

                if (MathUtils.RandomFloat() > survivalProb)
                    return emitted + attenuation * directLight; // Terminated — return only direct contribution

                // Surviving path: boost energy to compensate for killed siblings
                attenuation /= survivalProb;

                // NOTE: No attenuation cap here. With scene-adaptive RR,
                // indirect-dominant scenes use MinSurvival=0.5 (max boost 2×)
                // and MinBounces=8, giving paths plenty of opportunity to find
                // emissive sources. Normal scenes use MinSurvival=0.15 (max
                // boost 7×). Any remaining extreme outliers are caught by
                // ClampRadiance() at the end of the render loop.
            }

            // Skip indirect recursion for near-black materials (optimisation)
            if (attenuation.LengthSquared() < 0.001f)
                return emitted + directLight * attenuation;

            Vector3 indirect = TraceRay(scattered, depth - 1);
            return emitted + attenuation * (directLight + indirect);
        }

        // No scatter (fully absorbed) — return direct light contribution only
        return emitted + directLight;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DIRECT LIGHTING (NEXT EVENT ESTIMATION)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes direct illumination from all lights at a surface hit point.
    ///
    /// The computation has three parts:
    ///
    /// 0. **Ambient fill**: applied to ALL materials unconditionally as a base.
    ///    Represents omnidirectional environment light that even specular surfaces
    ///    reflect (tinted by their attenuation). NOT multiplied by diffuseWeight —
    ///    a mirror in a room reflects the room's ambient light.
    ///
    /// 1. **Diffuse (Lambert)**: lightColor * N·L * diffuseWeight
    ///    Only for materials with diffuseWeight > 0.
    ///
    /// 2. **Specular (Blinn-Phong)**: lightColor * (N·H)^exponent * specStrength
    ///    Adds visible "hotspot" highlights on shiny surfaces.
    /// </summary>
    private Vector3 ComputeDirectLighting(HitRecord rec, Ray incomingRay,
                                          float diffuseWeight, float specExponent, float specStrength)
    {
        // ── Ambient fill ────────────────────────────────────────────────────
        // NOT multiplied by diffuseWeight!
        //
        // The ambient term approximates omnidirectional environment light.
        // All materials interact with this light — the material's attenuation
        // vector in TraceRay handles the correct modulation:
        //   - Diffuse: attenuation = albedo → ambient * albedo
        //   - Metal:   attenuation = metal color → ambient reflected with tint
        //   - Glass:   attenuation = glass tint → ambient transmitted with tint
        //
        // Previous version had `_ambientLight * diffuseWeight` which zeroed
        // the ambient for metals (diffuseWeight=0), making mirror scenes with
        // ambient_light > 0 appear completely black. This was a bug.
        Vector3 result = _ambientLight;

        // Pre-compute view direction for specular highlights
        Vector3 viewDir = Vector3.Zero;
        bool doSpecular = specExponent > 0f && specStrength > 0f;
        if (doSpecular)
            viewDir = Vector3.Normalize(-incomingRay.Direction);

        foreach (var light in _lights)
        {
            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                // Use stratified sampling for area lights — the sample index is
                // passed through to pick a point from a specific grid cell.
                bool inShadow;
                Vector3 lightColor;
                Vector3 dirToLight;
                float distance;

                if (light is AreaLight areaLight)
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        areaLight.IlluminateAndTestStratified(rec.Point, rec.Normal, _world, s);
                }
                else
                {
                    (inShadow, lightColor, dirToLight, distance) =
                        light.IlluminateAndTest(rec.Point, rec.Normal, _world);
                }

                if (inShadow) continue;

                // ── Diffuse component (Lambert) ─────────────────────────────
                float nDotL = MathF.Max(0f, Vector3.Dot(rec.Normal, dirToLight));

                if (diffuseWeight > 0f)
                    lightAccum += lightColor * nDotL * diffuseWeight;

                // ── Specular component (Blinn-Phong) ────────────────────────
                // The half-vector between the view direction and the light
                // direction gives us the "highlight" position. The N·H term
                // raised to the specular exponent controls the size/tightness.
                if (doSpecular && nDotL > 0f)
                {
                    Vector3 halfDir = Vector3.Normalize(dirToLight + viewDir);
                    float nDotH = MathF.Max(0f, Vector3.Dot(rec.Normal, halfDir));
                    float spec = MathF.Pow(nDotH, specExponent);
                    lightAccum += lightColor * spec * specStrength;
                }
            }

            // Note on energy normalisation:
            // AreaLight pre-divides by ShadowSamples in its energy formula, so
            // summing (not averaging) the unshadowed samples gives correct energy.
            // Point/Directional/Spot lights with ShadowSamples==1 are unaffected.
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
