using System.Numerics;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Rendering;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Small helper for validation tests that need the linear-HDR beauty of a
/// programmatically built scene (no tone mapping, no denoiser).
/// </summary>
internal static class RenderHarness
{
    public static Camera.Camera LookAt(Vector3 from, Vector3 to, float vFovDeg, int width, int height)
        => new(from, to, Vector3.UnitY, vFovDeg, (float)width / height, aperture: 0f, focusDist: 1f);

    /// <summary>Renders and returns the linear beauty as [y, x] RGB.</summary>
    public static Vector3[,] RenderLinear(
        IHittable world, List<ILight> lights, SkySettings sky, Camera.Camera camera,
        int width, int height, int spp, int depth,
        bool reference = true, int seed = 0, float? clamp = null, PixelFilter? filter = null,
        LightSamplingStrategy lightSampling = LightSamplingStrategy.All,
        PathDepths? depths = null)
    {
        var renderer = new Renderer(
            world, camera, lights, sky, spp, depth,
            maxSampleRadiance: clamp,
            lightSamplingStrategy: lightSampling,
            referenceMode: reference, seed: seed, pixelFilter: filter, depths: depths);
        var result = renderer.Render(width, height, new RenderCaptureOptions { CaptureBeautyHalves = true });
        var beauty = result.Buffers!.Beauty;
        var img = new Vector3[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[y, x] = beauty.GetRgb(x, y);
        return img;
    }

    public static Vector3 Mean(Vector3[,] img)
    {
        Vector3 sum = Vector3.Zero;
        foreach (var v in img) sum += v;
        return sum / img.Length;
    }
}
