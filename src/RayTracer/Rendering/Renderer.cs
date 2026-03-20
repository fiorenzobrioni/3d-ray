using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;

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

    private Vector3 TraceRay(Ray ray, int depth)
    {
        if (depth <= 0)
            return Vector3.Zero;

        HitRecord rec = default;

        if (!_world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec))
            return CalculateSkyColor(ray);

        Vector3 directLight = ComputeDirectLighting(rec);

        if (rec.Material != null && rec.Material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
        {
            // BUG FIX: was returning raw directLight when attenuation ≈ 0,
            // which incorrectly ignored the material's absorption. Both terms
            // must be weighted by the material attenuation (albedo).
            // For near-black materials the recursive term is negligible, so
            // we can skip it safely — but the direct term must still be attenuated.
            if (attenuation.LengthSquared() < 0.001f)
                return directLight * attenuation; // ≈ zero, avoids one recursive call

            return attenuation * (directLight + TraceRay(scattered, depth - 1));
        }

        // No scatter (fully absorbed) — return whatever direct light was computed
        return directLight;
    }

    /// <summary>
    /// Computes direct illumination from all lights at a surface hit point.
    ///
    /// For area lights (ShadowSamples > 1) this casts multiple shadow rays to random points
    /// on the light surface and averages the result, producing physically-based soft shadows
    /// with penumbra gradients. The energy is already normalised inside AreaLight so the
    /// averaged sum gives the correct radiometric value.
    ///
    /// For point/directional/spot lights (ShadowSamples == 1) the loop runs once, matching
    /// the previous behaviour exactly.
    /// </summary>
    private Vector3 ComputeDirectLighting(HitRecord rec)
    {
        Vector3 result = _ambientLight;

        foreach (var light in _lights)
        {
            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                // IlluminateAndTest guarantees the shadow ray and illumination colour
                // both reference the SAME random sample point on the light surface.
                // For point/directional lights this is equivalent to the old separate calls.
                var (inShadow, lightColor, dirToLight, _) =
                    light.IlluminateAndTest(rec.Point, _world);

                if (inShadow) continue;

                float nDotL = MathF.Max(0f, Vector3.Dot(rec.Normal, dirToLight));
                lightAccum += lightColor * nDotL;
            }

            // Average the samples.
            // Note: AreaLight pre-divides by ShadowSamples in its energy formula so
            // summing (not averaging) the unshadowed samples gives the correct result.
            // Point/Directional lights with ShadowSamples==1 are unaffected either way.
            result += lightAccum;
        }

        return result;
    }

    private Vector3 CalculateSkyColor(Ray ray)
    {
        return _background;
    }
}
