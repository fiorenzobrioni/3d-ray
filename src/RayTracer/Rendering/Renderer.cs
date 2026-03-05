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
    /// Renders the scene and returns a float[,] (y, x) array of Vector3 colors (linear space).
    /// Uses Parallel.For over scanlines for multi-core rendering.
    /// </summary>
    public Vector3[,] Render(int width, int height)
    {
        var pixels = new Vector3[height, width];
        int completedRows = 0;
        int totalRows = height;

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
        {
            for (int i = 0; i < width; i++)
            {
                Vector3 cumulativeColor = Vector3.Zero;

                for (int s = 0; s < _samplesPerPixel; s++)
                {
                    float u = (i + MathUtils.RandomFloat()) / (width - 1);
                    float v = ((height - 1 - j) + MathUtils.RandomFloat()) / (height - 1);

                    var ray = _camera.GetRay(u, v);
                    cumulativeColor += TraceRay(ray, _maxDepth);
                }

                // Average samples and gamma correction (Approx 2.0)
                Vector3 finalColor = cumulativeColor / _samplesPerPixel;
                finalColor = new Vector3(
                    MathF.Sqrt(MathF.Max(0, finalColor.X)),
                    MathF.Sqrt(MathF.Max(0, finalColor.Y)),
                    MathF.Sqrt(MathF.Max(0, finalColor.Z)));

                pixels[j, i] = finalColor;
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
        var unitDir = Vector3.Normalize(ray.Direction);
        float t = 0.5f * (unitDir.Y + 1f);
        return (1f - t) * Vector3.One + t * _background;
    }
}
