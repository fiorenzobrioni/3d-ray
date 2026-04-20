using System.Numerics;
using BenchmarkDotNet.Attributes;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Benchmarks;

/// <summary>
/// Synthetic BVH benchmark: builds a BVH of N randomly-placed unit spheres and
/// sweeps a fixed ray batch through it. Measures the combined cost of AABB
/// slab tests + sphere intersections on the BVH hot path.
/// </summary>
[MemoryDiagnoser]
public class BvhBenchmarks
{
    private BvhNode _bvh = null!;
    private Ray[] _rays = null!;

    [Params(100, 1_000, 10_000)]
    public int SphereCount;

    [Params(1024)]
    public int RayCount;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        var material = new Lambertian(new Vector3(0.5f, 0.5f, 0.5f));

        var objects = new List<IHittable>(SphereCount);
        for (int i = 0; i < SphereCount; i++)
        {
            var center = new Vector3(
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 20 - 10));
            float radius = 0.05f + (float)(rng.NextDouble() * 0.2);
            objects.Add(new Sphere(center, radius, material));
        }
        _bvh = new BvhNode(objects);

        _rays = new Ray[RayCount];
        for (int i = 0; i < RayCount; i++)
        {
            // Rays all launched from outside the sphere cloud looking roughly
            // towards the origin — representative of primary camera rays.
            var origin = new Vector3(
                (float)(rng.NextDouble() * 4 - 2),
                (float)(rng.NextDouble() * 4 - 2),
                -20f);
            var target = new Vector3(
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 20 - 10));
            var dir = Vector3.Normalize(target - origin);
            _rays[i] = new Ray(origin, dir);
        }
    }

    [Benchmark]
    public int HitSweep()
    {
        int hits = 0;
        var rays = _rays;
        var rec = new HitRecord();
        for (int i = 0; i < rays.Length; i++)
        {
            if (_bvh.Hit(rays[i], 0.001f, 1e30f, ref rec))
                hits++;
        }
        return hits;
    }
}
