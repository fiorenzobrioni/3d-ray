using System.Collections.Generic;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Contract tests for the Phase-2c CSG caustic caster: a boolean solid focuses
/// light through its curved leaves, and a converged specular vertex is admissible
/// only if it still lies on the boolean result's boundary (the CSG analog of the
/// mesh per-triangle clamp). The membership test reuses the same
/// <see cref="CsgObject.ContainsPoint"/> the clamp does, so we assert it directly
/// plus one end-to-end connection.
/// </summary>
public class CsgCausticTests
{
    private static readonly IMaterial Glass = new Dielectric(1.5f);

    // A unit Box scaled/translated into world space (Box is the [-0.5,0.5] cube).
    private static IHittable SizedBox(Vector3 scale, Vector3 translate)
        => new Transform(new Box(Glass),
                         Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(translate));

    private static bool OnResultBoundary(CsgObject c, Vector3 p, Vector3 n)
        => c.ContainsPoint(p + 1e-3f * n) != c.ContainsPoint(p - 1e-3f * n);

    [Fact]
    public void ContainsPoint_ComposesPerOperation()
    {
        var sphere = new Sphere(Vector3.Zero, 1f, Glass);
        var bigBox = SizedBox(new Vector3(10f), Vector3.Zero);

        var inter = new CsgObject(CsgOperation.Intersection, sphere, bigBox);
        Assert.True(inter.ContainsPoint(Vector3.Zero));               // inside both
        Assert.False(inter.ContainsPoint(new Vector3(2f, 0, 0)));     // outside sphere

        // Subtraction carves the front half (z < -0.5) out of the sphere.
        var clip = SizedBox(new Vector3(10f), new Vector3(0, 0, -5.5f)); // z ∈ [-10.5, -0.5]
        var carved = new CsgObject(CsgOperation.Subtraction, sphere, clip);
        Assert.False(carved.ContainsPoint(new Vector3(0, 0, -0.9f)));  // removed region
        Assert.True(carved.ContainsPoint(new Vector3(0, 0, 0.9f)));    // kept region
    }

    [Fact]
    public void ResultBoundary_ClampRejectsClippedVertices()
    {
        var sphere = new Sphere(Vector3.Zero, 1f, Glass);
        var clip = SizedBox(new Vector3(10f), new Vector3(0, 0, -5.5f)); // removes z < -0.5
        var carved = new CsgObject(CsgOperation.Subtraction, sphere, clip);

        // A sphere point in the KEPT region is on the result boundary (accepted).
        Assert.True(OnResultBoundary(carved, new Vector3(1f, 0, 0), new Vector3(1f, 0, 0)));
        // A sphere point in the CARVED-away region is NOT on the boundary (clamp
        // rejects a manifold vertex that converged there).
        Assert.False(OnResultBoundary(carved, new Vector3(0, 0, -1f), new Vector3(0, 0, -1f)));
    }

    [Fact]
    public void GlassCsgSphere_OnAxis_FocusesLikeTheBareSphere()
    {
        // Sphere ∩ (large box) == the sphere, but routed through the CSG caster:
        // exercises CSG seeding (HitPrimitive → child chart) and the boundary clamp.
        var sphere = new Sphere(Vector3.Zero, 1f, Glass);
        var bigBox = SizedBox(new Vector3(10f), Vector3.Zero);
        var csg = new CsgObject(CsgOperation.Intersection, sphere, bigBox);

        var caster = new CausticCasterRegistry.Caster(csg, csg, csg.BoundingBox());
        var ci = new CausticInterface(isTransmissive: true, ior: 1.5f, tint: Vector3.One, absorption: Vector3.Zero);

        Vector3 x = new(0f, 0f, -4f);
        Vector3 y = new(0f, 0f, 4f);
        bool ok = ManifoldWalker.Connect(caster, ci, x, y, new Vector3(0, 0, -1f),
                                         ManifoldWalker.DefaultMaxIterations, out var conn);

        Assert.True(ok, "on-axis connection through the glass CSG sphere must converge");
        Assert.True((conn.FirstVertex - new Vector3(0, 0, -1f)).Length() < 1e-2f, $"front {conn.FirstVertex}");
        Assert.True((conn.LastVertex - new Vector3(0, 0, 1f)).Length() < 1e-2f, $"back {conn.LastVertex}");
        Assert.True(conn.Throughput.X > 0.85f && conn.Throughput.X <= 1f, $"throughput {conn.Throughput}");
    }

    [Fact]
    public void Registry_AcceptsCurvedCsg()
    {
        var sphere = new Sphere(Vector3.Zero, 1f, Glass);
        var bigBox = SizedBox(new Vector3(10f), Vector3.Zero);
        var csg = new CsgObject(CsgOperation.Intersection, sphere, bigBox);
        Assert.Equal(1, new CausticCasterRegistry(new List<IHittable> { csg }).Count);
    }
}
