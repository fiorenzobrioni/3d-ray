using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Scene;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Quality of the BVH build (PIANO-REVIEW.md Phase 2): the SAH cost of the
/// binned build against the cost of a top-down build that tries every
/// object split on every axis (a full sweep: the best a top-down object-split
/// build can do, at O(N log² N)). Speed is Phase 11's.
/// </summary>
public class BvhQualityTests
{
    private readonly ITestOutputHelper _out;
    public BvhQualityTests(ITestOutputHelper output) => _out = output;

    private static readonly IMaterial M = new Lambertian(new Vector3(0.5f));
    private const double TraversalCost = 0.5;

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
        return dir != null ? Path.Combine(dir.FullName, relative) : relative;
    }

    public static TheoryData<string> Sets => new() { "icosphere", "teapot", "soup", "grass" };

    private static List<IHittable> Set(string name)
    {
        switch (name)
        {
            case "icosphere":
                return GeometryAuditTests.Icosphere(1f, 5, out _).Cast<IHittable>().ToList();
            case "teapot":
                var mesh = ObjLoader.Load(RepoFile("scenes/models/utah-teapot.obj"), M)!;
                return mesh.Triangles.ToList();
            case "soup":
            {
                // Triangles of very different sizes, scattered.
                var rng = new Random(5);
                var list = new List<IHittable>();
                for (int i = 0; i < 20000; i++)
                {
                    Vector3 c = new(rng.NextSingle() * 20, rng.NextSingle() * 20, rng.NextSingle() * 20);
                    float s = MathF.Pow(10f, rng.NextSingle() * 2 - 1.5f);
                    Vector3 R() => new(rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f, rng.NextSingle() - 0.5f);
                    list.Add(new Triangle(c + s * R(), c + s * R(), c + s * R(), M));
                }
                return list;
            }
            default:
            {
                // Long thin blades standing on a plane: the case that bounds
                // overlap the most.
                var rng = new Random(9);
                var list = new List<IHittable>();
                for (int i = 0; i < 20000; i++)
                {
                    Vector3 b = new(rng.NextSingle() * 10, 0, rng.NextSingle() * 10);
                    Vector3 lean = new(rng.NextSingle() - 0.5f, 1, rng.NextSingle() - 0.5f);
                    list.Add(new Triangle(b, b + new Vector3(0.02f, 0, 0), b + 0.5f * lean, M));
                }
                return list;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void BinnedSah_IsCloseToAFullSweep(string name)
    {
        var prims = Set(name);
        double binned = new BvhNode(new List<IHittable>(prims)).SahCost();
        var boxes = prims.Select(p => p.BoundingBox()).ToArray();
        AABB root = boxes.Aggregate(AABB.SurroundingBox);
        double sweep = SweepCost(boxes, root.SurfaceArea());
        _out.WriteLine($"{name}: {prims.Count} primitives, SAH cost binned {binned:F2}, full sweep {sweep:F2} (ratio {binned / sweep:F3})");
        Assert.True(binned < 1.10 * sweep);
    }

    // Top-down full-sweep SAH, leaves of up to 4 primitives (as the BVH).
    private static double SweepCost(AABB[] boxes, double rootArea)
    {
        AABB box = boxes.Aggregate(AABB.SurroundingBox);
        double p = box.SurfaceArea() / rootArea;
        int n = boxes.Length;
        if (n <= BvhNode.MaxPrimitivesPerLeaf) return p * n;

        double best = double.MaxValue;
        AABB[]? bestLeft = null, bestRight = null;
        var prefix = new double[n];
        for (int axis = 0; axis < 3; axis++)
        {
            var sorted = boxes.OrderBy(b => Axis((b.Min + b.Max) * 0.5f, axis)).ToArray();
            AABB acc = AABB.Empty;
            for (int i = 0; i < n; i++) { acc = AABB.SurroundingBox(acc, sorted[i]); prefix[i] = acc.SurfaceArea(); }
            acc = AABB.Empty;
            double area = box.SurfaceArea();
            for (int i = n - 1; i > 0; i--)
            {
                acc = AABB.SurroundingBox(acc, sorted[i]);
                double cost = TraversalCost + (i * prefix[i - 1] + (n - i) * acc.SurfaceArea()) / area;
                if (cost < best) { best = cost; bestLeft = sorted[..i]; bestRight = sorted[i..]; }
            }
        }
        return p * TraversalCost + SweepCost(bestLeft!, rootArea) + SweepCost(bestRight!, rootArea);
    }

    private static float Axis(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
}
