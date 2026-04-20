using System.Numerics;
using BenchmarkDotNet.Attributes;
using RayTracer.Core;

namespace RayTracer.Benchmarks;

/// <summary>
/// Micro-benchmark for the ray-AABB slab test. The BVH hot path calls
/// <see cref="AABB.Hit"/> tens-to-hundreds of times per primary ray, so even
/// small per-call savings compound.
/// </summary>
[MemoryDiagnoser]
public class AabbBenchmarks
{
    private AABB _box;
    private Ray[] _rays = null!;

    [Params(1024)]
    public int RayCount;

    [GlobalSetup]
    public void Setup()
    {
        _box = new AABB(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

        var rng = new Random(42);
        _rays = new Ray[RayCount];
        for (int i = 0; i < RayCount; i++)
        {
            var origin = new Vector3(
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 10 - 5));
            var dir = Vector3.Normalize(new Vector3(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1)));
            _rays[i] = new Ray(origin, dir);
        }
    }

    [Benchmark]
    public int HitSweep()
    {
        int hits = 0;
        var rays = _rays;
        for (int i = 0; i < rays.Length; i++)
        {
            if (_box.Hit(rays[i], 0.001f, 1e30f))
                hits++;
        }
        return hits;
    }
}
