using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// The BVH must not change what a ray hits — only accelerate finding it.
/// These tests compare Bvh.Hit (and TriangleBvh.Hit for meshes) against the trivial HittableList.Hit
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
        var reference = new HittableList(new List<IHittable>(primitives));
        IHittable bvh = TriangleBvh.Accepts(primitives) ? new TriangleBvh(primitives) : new Bvh(primitives);

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

    [Fact]
    public void Bvh_DoesNotModifyItsInput()
    {
        var primitives = RandomSpheres(500, 17);
        var copy = new List<IHittable>(primitives);
        _ = new Bvh(primitives);
        Assert.Equal(copy, primitives);
    }

    private static List<IHittable> RandomTriangles(int count, int seed)
    {
        var rng = new Random(seed);
        var mat = Mat();
        var list = new List<IHittable>(count);
        for (int i = 0; i < count; i++)
        {
            var c = new Vector3(rng.NextSingle() * 20 - 10, rng.NextSingle() * 20 - 10, rng.NextSingle() * 20 - 10);
            Vector3 R() => new(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f);
            Vector3 a = c + 2 * R(), b = c + 2 * R(), d = c + 2 * R();
            list.Add(i % 2 == 0
                ? new Triangle(a, b, d, mat)
                : new SmoothTriangle(a, b, d, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ,
                                     Vector2.Zero, Vector2.UnitX, Vector2.UnitY, mat));
        }
        return list;
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(300, 2)]
    [InlineData(5000, 3)]
    public void TriangleBvh_GivesTheSameHitRecord_AsTheTriangles(int count, int seed)
    {
        // The mesh BVH finds the closest triangle with its own traversal and
        // lets that triangle fill the record: every field must be what the
        // triangle list gives, bit for bit.
        var tris = RandomTriangles(count, seed);
        var reference = new HittableList(new List<IHittable>(tris));
        var bvh = new TriangleBvh(tris);
        var rng = new Random(seed + 7);
        int hits = 0;
        for (int k = 0; k < 2000; k++)
        {
            // Aimed at a random triangle (near its centroid), from anywhere.
            var o = new Vector3(rng.NextSingle() * 40 - 20, rng.NextSingle() * 40 - 20, rng.NextSingle() * 40 - 20);
            var target = tris[rng.Next(count)].BoundingBox();
            var aim = (target.Min + target.Max) * 0.5f
                      + 0.3f * new Vector3(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f);
            var ray = new Ray(o, Vector3.Normalize(aim - o));
            var a = new HitRecord();
            var b = new HitRecord();
            bool ha = reference.Hit(ray, 1e-3f, 1e30f, ref a);
            bool hb = bvh.Hit(ray, 1e-3f, 1e30f, ref b);
            Assert.Equal(ha, hb);
            if (!ha) continue;
            hits++;
            Assert.Equal(a.T, b.T);
            Assert.Equal(a.Point, b.Point);
            Assert.Equal(a.Normal, b.Normal);
            Assert.Equal((a.U, a.V), (b.U, b.V));
            Assert.Equal(a.FrontFace, b.FrontFace);
        }
        Assert.True(hits > 500, $"{hits} hits");
    }

    [Fact]
    public void Bvh_SurvivesAPathologicalDistribution()
    {
        // Sizes and positions growing geometrically: every SAH split peels one
        // primitive off, the deepest a binned build can go. The depth cap must
        // keep the traversal stack in bounds.
        var mat = Mat();
        var prims = new List<IHittable>();
        for (int i = 0; i < 3000; i++)
        {
            float x = MathF.Pow(1.01f, i);
            prims.Add(new Sphere(new Vector3(x, 0, 0), 0.25f * x * 0.01f + 1e-4f, mat));
        }
        var rng = new Random(4);
        var rays = Enumerable.Range(0, 200).Select(_ =>
        {
            float x = MathF.Pow(1.01f, rng.Next(3000));
            return new Ray(new Vector3(x, 5, 0), Vector3.Normalize(new Vector3(rng.NextSingle() * 0.02f - 0.01f, -1, 0)));
        });
        AssertEquivalent(prims, rays);
    }
}
