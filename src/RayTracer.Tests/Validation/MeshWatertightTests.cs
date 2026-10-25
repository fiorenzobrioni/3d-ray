using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Triangle meshes must be watertight (PIANO-REVIEW.md Phase 2): a ray aimed
/// exactly at a shared edge or vertex of a closed mesh hits it, and a line
/// through a closed convex mesh crosses it exactly twice. Also at any scale: a
/// test with an absolute epsilon on the determinant loses small triangles.
/// </summary>
public class MeshWatertightTests
{
    private readonly ITestOutputHelper _out;
    public MeshWatertightTests(ITestOutputHelper output) => _out = output;

    private static readonly IMaterial M = new Lambertian(new Vector3(0.5f));

    private static (Mesh Mesh, List<Triangle> Tris) Sphere(float radius, bool smooth)
    {
        var tris = GeometryAuditTests.Icosphere(radius, 3, out _);
        List<IHittable> list = smooth
            ? tris.Select(t => (IHittable)new SmoothTriangle(t.V0, t.V1, t.V2, Vector3.Normalize(t.V0),
                                                             Vector3.Normalize(t.V1), Vector3.Normalize(t.V2), M)).ToList()
            : tris.Cast<IHittable>().ToList();
        return (new Mesh(list, M), tris);
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(1f, true)]
    [InlineData(1e-4f, false)]
    [InlineData(3e3f, false)]
    public void RaysAtEdgesAndVertices_AlwaysHit(float radius, bool smooth)
    {
        var (mesh, tris) = Sphere(radius, smooth);
        var rng = new Random(11);
        int rays = 0, misses = 0;
        foreach (var tri in tris)
        {
            // A vertex, the middle of an edge and a random point of an edge.
            var targets = new[] { tri.V0, 0.5f * (tri.V0 + tri.V1), Vector3.Lerp(tri.V1, tri.V2, rng.NextSingle()) };
            foreach (var target in targets)
            {
                // From outside, toward the target, from a random direction of
                // the outer hemisphere.
                Vector3 n = Vector3.Normalize(target);
                Vector3 d;
                do d = Vector3.Normalize(new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1));
                while (Vector3.Dot(d, n) > -0.2f);
                var o = target - 3f * radius * d;
                var rec = new HitRecord();
                rays++;
                if (!mesh.Hit(new Ray(o, d), 1e-5f * radius, 10f * radius, ref rec)) misses++;
            }
        }
        _out.WriteLine($"radius {radius}, smooth {smooth}: {misses} misses out of {rays} rays at edges and vertices");
        Assert.Equal(0, misses);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1e-4f)]
    public void LinesThroughAClosedMesh_CrossItTwice(float radius)
    {
        // The icosphere is convex: a line through a point of its surface
        // either enters it (two crossings, at the entry and the exit) or only
        // touches it there (a supporting line). The planes of the faces tell
        // which, exactly; a ray through a shared vertex or edge must be counted
        // once (hits at the same distance are one crossing).
        var (mesh, tris) = Sphere(radius, false);
        var rng = new Random(13);
        int lines = 0, wrong = 0, touching = 0;
        foreach (var tri in tris)
        {
            Vector3 target = rng.Next(2) == 0 ? tri.V1 : 0.5f * (tri.V0 + tri.V2);
            Vector3 d = Vector3.Normalize(new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1));
            var ray = new Ray(target - 3f * radius * d, d);

            double tEnter = double.NegativeInfinity, tExit = double.PositiveInfinity;
            foreach (var f in tris)
            {
                // Outward plane n·x ≤ n·V0, in double.
                Vector3 nf = Vector3.Cross(f.V1 - f.V0, f.V2 - f.V0);
                double nx = nf.X, ny = nf.Y, nz = nf.Z;
                double c = nx * f.V0.X + ny * f.V0.Y + nz * f.V0.Z;
                double no = nx * ray.Origin.X + ny * ray.Origin.Y + nz * ray.Origin.Z;
                double nd = nx * d.X + ny * d.Y + nz * d.Z;
                double tp = (c - no) / nd;
                if (nd < 0) tEnter = Math.Max(tEnter, tp); else tExit = Math.Min(tExit, tp);
            }
            int expected;
            if (tExit - tEnter > 1e-3 * radius) expected = 2;
            else if (tExit - tEnter < -1e-3 * radius) continue;
            else { touching++; continue; }

            int n = 0;
            float tMin = 1e-6f * radius;
            for (int k = 0; k < 16; k++)
            {
                var rec = new HitRecord();
                if (!mesh.Hit(ray, tMin, 10f * radius, ref rec)) break;
                n++;
                tMin = rec.T + 1e-6f * radius;
            }
            lines++;
            if (n != expected) wrong++;
        }
        _out.WriteLine($"radius {radius}: {wrong} wrong crossing counts out of {lines} lines ({touching} supporting lines left out)");
        Assert.True(lines > 1000);
        Assert.Equal(0, wrong);
    }
}
