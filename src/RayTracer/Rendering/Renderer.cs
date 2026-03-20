using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;

namespace RayTracer.Rendering;

public class Renderer
{
    private readonly IHittable _world;
    private readonly Camera.Camera _camera;
    private readonly List<ILight> _lights;
    private readonly Vector3 _ambientLight;
    private readonly Vector3 _background;
    private readonly int _maxDepth;
    private readonly int _samplesPerPixel;

    // ── Russian Roulette configuration ──────────────────────────────────────
    // Start applying Russian Roulette after this many bounces. Before this depth
    // all rays survive unconditionally. This avoids premature termination of
    // important early bounces while efficiently culling deep low-energy paths.
    private const int RussianRouletteMinBounces = 4;
    private const float RussianRouletteMinSurvival = 0.05f;

    public Renderer(
        IHittable world,
        Camera.Camera camera,
        List<ILight> lights,
        Vector3 ambientLight,
        Vector3 background,
        int samplesPerPixel,
        int maxDepth)
    {
        _world = world;
        _camera = camera;
        _lights = lights;
        _ambientLight = ambientLight;
        _background = background;
        _samplesPerPixel = samplesPerPixel;
        _maxDepth = maxDepth;
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
                        cumulativeColor += TraceRay(ray, _maxDepth);
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
        float diffuseWeight = material?.DiffuseWeight ?? 1f;
        float specExponent = material?.SpecularExponent ?? 0f;
        float specStrength = material?.SpecularStrength ?? 0f;

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
            if (bouncesUsed >= RussianRouletteMinBounces)
            {
                float survivalProb = MathF.Max(MathUtils.Luminance(attenuation), RussianRouletteMinSurvival);
                survivalProb = MathF.Min(survivalProb, 0.95f); // Cap to avoid infinite paths

                if (MathUtils.RandomFloat() > survivalProb)
                    return attenuation * directLight; // Terminated — return only direct contribution

                // Surviving path: boost energy to compensate for killed siblings
                attenuation /= survivalProb;
            }

            // Skip indirect recursion for near-black materials (optimisation)
            if (attenuation.LengthSquared() < 0.001f)
                return directLight * attenuation;

            Vector3 indirect = TraceRay(scattered, depth - 1);
            return attenuation * (directLight + indirect);
        }

        // No scatter (fully absorbed) — return direct light contribution only
        return directLight;
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

    private Vector3 CalculateSkyColor(Ray ray)
    {
        return _background;
    }
}
