using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// The BVH must not change what a ray hits — only accelerate finding it.
/// These tests compare BvhNode.Hit against the trivial HittableList.Hit
/// reference on the same (scene, ray) pair and assert that:
///   (a) the two implementations agree on hit/miss for every ray, and
///   (b) on a hit, they agree on the hit distance <c>rec.T</c> within a
///       numeric tolerance.
/// </summary>
public class BvhEquivalenceTests
{
    private const float TEpsilon = 1e-4f;

    private static IMaterial Mat() => new Lambertian(new Vector3(0.5f));

    private static List<IHittable> RandomSpheres(int count, int seed)
    {
        var rng = new Random(seed);
        var list = new List<IHittable>(count);
        var mat = Mat();
        for (int i = 0; i < count; i++)
        {
            var c = new Vector3(
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 20 - 10));
            float r = 0.05f + (float)(rng.NextDouble() * 0.5);
            list.Add(new Sphere(c, r, mat));
        }
        return list;
    }

    private static Ray RandomRay(Random rng)
    {
        var o = new Vector3(
            (float)(rng.NextDouble() * 40 - 20),
            (float)(rng.NextDouble() * 40 - 20),
            (float)(rng.NextDouble() * 40 - 20));
        var d = Vector3.Normalize(new Vector3(
            (float)(rng.NextDouble() * 2 - 1),
            (float)(rng.NextDouble() * 2 - 1),
            (float)(rng.NextDouble() * 2 - 1)));
        return new Ray(o, d);
    }

    private static void AssertEquivalent(List<IHittable> primitives, IEnumerable<Ray> rays)
    {
        // BvhNode may sort the list in place; pass copies so the reference list
        // stays in the original order.
        var reference = new HittableList(new List<IHittable>(primitives));
        var bvh = new BvhNode(new List<IHittable>(primitives));

        foreach (var ray in rays)
        {
            var refRec = new HitRecord();
            var bvhRec = new HitRecord();
            bool refHit = reference.Hit(ray, 0.001f, 1e30f, ref refRec);
            bool bvhHit = bvh.Hit(ray, 0.001f, 1e30f, ref bvhRec);

            Assert.Equal(refHit, bvhHit);
            if (refHit)
                Assert.InRange(bvhRec.T, refRec.T - TEpsilon, refRec.T + TEpsilon);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]     // exactly at fat-leaf boundary
    [InlineData(5, 5)]     // first internal-node split
    [InlineData(20, 6)]
    [InlineData(200, 7)]
    [InlineData(2000, 8)]
    public void Bvh_Matches_LinearList(int count, int seed)
    {
        var primitives = RandomSpheres(count, seed);
        var rng = new Random(seed + 1000);
        var rays = Enumerable.Range(0, 256).Select(_ => RandomRay(rng));
        AssertEquivalent(primitives, rays);
    }

    [Fact]
    public void Bvh_Matches_LinearList_AxisAlignedRays()
    {
        // Axis-aligned rays have a zero direction component, which makes the
        // precomputed inverse direction ±infinity. The slab test must still
        // behave correctly for these — use them explicitly instead of random.
        var primitives = RandomSpheres(50, 42);
        var rays = new List<Ray>();
        for (float k = -10f; k <= 10f; k += 1.5f)
        {
            rays.Add(new Ray(new Vector3(k, 0, -20), Vector3.UnitZ));
            rays.Add(new Ray(new Vector3(0, k, -20), Vector3.UnitZ));
            rays.Add(new Ray(new Vector3(-20, k, 0), Vector3.UnitX));
            rays.Add(new Ray(new Vector3(k, -20, 0), Vector3.UnitY));
            rays.Add(new Ray(new Vector3(0, 0, 20), -Vector3.UnitZ));
        }
        AssertEquivalent(primitives, rays);
    }

    [Fact]
    public void Bvh_Matches_LinearList_ClusteredPrimitives()
    {
        // Degenerate build: many primitives at the same position — centroid
        // extent along every axis is zero, so longest-axis selection falls
        // through to axis 0. Ensures the split path doesn't loop/crash when
        // centroids coincide.
        var mat = Mat();
        var primitives = new List<IHittable>();
        for (int i = 0; i < 16; i++)
            primitives.Add(new Sphere(new Vector3(0, 0, 0), 0.5f + i * 0.01f, mat));

        var rng = new Random(123);
        var rays = Enumerable.Range(0, 64).Select(_ => RandomRay(rng));
        AssertEquivalent(primitives, rays);
    }

    [Fact]
    public void Bvh_Matches_LinearList_RayStartingInsideAABB()
    {
        // A ray whose origin is inside the scene's overall bounding volume:
        // the top-level slab must not cull it just because the origin is
        // inside the box (slab test's tMin/tMax interval still contains 0).
        var primitives = RandomSpheres(30, 99);
        var rng = new Random(321);
        var rays = Enumerable.Range(0, 64)
            .Select(_ => new Ray(
                new Vector3(0, 0, 0),
                Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1)))));
        AssertEquivalent(primitives, rays);
    }
}
