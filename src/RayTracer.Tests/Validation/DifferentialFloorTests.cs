using System.Collections.Concurrent;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The texture footprint the renderer hands to a camera ray's first hit
/// (PIANO-REVIEW.md 3.31): one pixel divided by √spp, never under an eighth of
/// a pixel; secondary hits carry none and are point-sampled.
/// </summary>
public class DifferentialFloorTests
{
    private const int Size = 16;

    /// <summary>Records the footprint width of every lookup it serves.</summary>
    private sealed class FootprintProbe : ITexture
    {
        public readonly ConcurrentBag<float> Widths = new();
        public readonly ConcurrentBag<float> FilteredZ = new();
        public readonly ConcurrentBag<float> PointZ = new();

        public Vector3 Value(float u, float v, Vector3 p, int objectSeed) => new(0.5f);

        public Vector3 Value(in HitRecord rec)
        {
            if (rec.Footprint.HasFootprint)
            {
                Widths.Add(rec.Footprint.DPdx.Length());
                FilteredZ.Add(rec.Point.Z);
            }
            else PointZ.Add(rec.Point.Z);
            return new Vector3(0.5f);
        }
    }

    private static FootprintProbe Render(int spp)
    {
        var probe = new FootprintProbe();
        // A wall facing the camera at distance 4: one pixel spans
        // 2·4·tan(fov/2) / Size on it.
        var wall = new Quad(new Vector3(-50, -50, -4), new Vector3(100, 0, 0), new Vector3(0, 100, 0),
                            new Lambertian(probe));
        // A second wall behind the camera catches the diffuse bounces.
        var back = new Quad(new Vector3(-50, -50, 4), new Vector3(100, 0, 0), new Vector3(0, 100, 0),
                            new Lambertian(probe));
        var world = new HittableList(new[] { (IHittable)wall, back });
        var camera = new RayTracer.Camera.Camera(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 60f, 1f, 0f, 1f);
        Sampler.SetKind(SamplerKind.Sobol);
        var renderer = new Renderer(world, camera, new List<ILight>(), new SkySettings(Vector3.One),
                                    samplesPerPixel: spp, maxDepth: 2, verbose: false);
        renderer.Render(Size, Size);
        return probe;
    }

    private static float Median(IEnumerable<float> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        return a[a.Length / 2];
    }

    [Fact]
    public void Footprint_ShrinksWithSpp_DownToAnEighthOfAPixel()
    {
        float pixel = 2f * 4f * MathF.Tan(MathF.PI / 6f) / Size;
        float w1 = Median(Render(1).Widths);
        float w16 = Median(Render(16).Widths);
        float w1024 = Median(Render(1024).Widths);

        Assert.InRange(w1, pixel * 0.97f, pixel * 1.1f);          // centre pixels: ≈ one pixel
        Assert.InRange(w1 / w16, 3.9f, 4.1f);                     // 1/√16
        Assert.InRange(w1 / w1024, 7.9f, 8.1f);                   // floored at 1/8, not 1/32
    }

    [Fact]
    public void SecondaryHits_ArePointSampled()
    {
        // Every filtered lookup is on the front wall (the camera rays' first
        // hit); the bounces that reach the back wall look it up at a point.
        var probe = Render(4);
        Assert.NotEmpty(probe.FilteredZ);
        Assert.All(probe.FilteredZ, z => Assert.True(z < 0f));
        Assert.Contains(probe.PointZ, z => z > 0f);
    }
}
