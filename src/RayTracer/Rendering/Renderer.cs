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
                        // Jittered sample within the stratum
                        float jitterU = (sx + MathUtils.RandomFloat()) * invSqrtSpp;
                        float jitterV = (sy + MathUtils.RandomFloat()) * invSqrtSpp;

                        float u = (i + jitterU) / width;
                        float v = (height - j - 1 + jitterV) / height;

                        var ray = _camera.GetRay(u, v);
                        cumulativeColor += TraceRay(ray, _maxDepth);
                    }
                }

                // Average samples
                Vector3 linearColor = cumulativeColor / actualSamples;

                // ACES filmic tone mapping for proper HDR handling
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
        // Clamp negatives
        color = Vector3.Max(color, Vector3.Zero);

        // ACES filmic curve: (x * (2.51x + 0.03)) / (x * (2.43x + 0.59) + 0.14)
        Vector3 a = color * (2.51f * color + new Vector3(0.03f));
        Vector3 b = color * (2.43f * color + new Vector3(0.59f)) + new Vector3(0.14f);
        Vector3 mapped = new Vector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

        // Clamp to [0,1] and apply gamma 2.2
        const float invGamma = 1f / 2.2f;
        return new Vector3(
            MathF.Pow(Math.Clamp(mapped.X, 0f, 1f), invGamma),
            MathF.Pow(Math.Clamp(mapped.Y, 0f, 1f), invGamma),
            MathF.Pow(Math.Clamp(mapped.Z, 0f, 1f), invGamma));
    }

    private Vector3 TraceRay(Ray ray, int depth)
    {
        // Termination condition: if depth reaches 0, stop calculating light
        if (depth <= 0)
            return Vector3.Zero;

        // Zero-allocation hit record init
        HitRecord rec = default;
        
        if (!_world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec))
        {
            return CalculateSkyColor(ray);
        }

        Vector3 directLight = ComputeDirectLighting(rec);

        if (rec.Material != null && rec.Material.Scatter(ray, rec, out Vector3 attenuation, out Ray scattered))
        {
            // Early exit optimization
            if (attenuation.LengthSquared() < 0.001f)
                return directLight;

            return directLight * attenuation + attenuation * TraceRay(scattered, depth - 1);
        }

        return directLight; // Absorbed light or no scatter
    }

    private Vector3 ComputeDirectLighting(HitRecord rec)
    {
        Vector3 result = _ambientLight; // Base ambient

        foreach (var light in _lights)
        {
            if (light.IsInShadow(rec.Point, _world))
                continue;

            var (lightColor, dirToLight, _) = light.Illuminate(rec.Point);
            float nDotL = MathF.Max(0, Vector3.Dot(rec.Normal, dirToLight));
            result += lightColor * nDotL;
        }

        return result;
    }

    private Vector3 CalculateSkyColor(Ray ray)
    {
        // Return the scene background color directly.
        // For sky gradients or HDRI environments, use IBL (future feature).
        return _background;
    }
}

