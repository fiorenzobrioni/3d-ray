using System.Collections.Generic;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Contract tests for the Phase-2c caustic casters: the curved analytic
/// primitives (cylinder, cone, capsule, torus) and smooth triangle meshes.
///
/// <para>The decisive correctness property for seeding is that
/// <see cref="IManifoldSurface.EvaluateManifold"/> is the exact inverse of the
/// (u, v) a straight-ray <c>Hit</c> records: the manifold walk seeds itself from
/// a hit's (u, v) and must reconstruct the same world point and outward normal,
/// or Newton starts on the wrong surface. We assert that round-trip directly
/// (an equivalence oracle, like <c>BvhEquivalenceTests</c>), then run one
/// end-to-end mesh connection.</para>
/// </summary>
public class MeshCausticTests
{
    private static readonly IMaterial Glass = new Dielectric(1.5f);

    // Shoots a ray, then checks EvaluateManifold(rec.U, rec.V) reproduces the hit
    // point and outward normal. Rays are fired from outside so rec.Normal is the
    // outward (front-face) normal the chart returns.
    private static void AssertChartInvertsHit(IHittable prim, Vector3 origin, Vector3 target)
    {
        Vector3 dir = Vector3.Normalize(target - origin);
        var rec = new HitRecord();
        Assert.True(prim.Hit(new Ray(origin, dir), 1e-4f, 1e9f, ref rec), "ray must hit the primitive");
        Assert.True(rec.FrontFace, "ray must strike the outward face");

        var surf = Assert.IsAssignableFrom<IManifoldSurface>(prim);
        Assert.True(surf.EvaluateManifold(rec.U, rec.V, out ManifoldPoint pt), "chart must evaluate the hit (u,v)");

        Assert.True((pt.P - rec.Point).Length() < 1e-3f, $"point mismatch: {pt.P} vs {rec.Point}");
        Assert.True((pt.N - rec.Normal).Length() < 2e-3f, $"normal mismatch: {pt.N} vs {rec.Normal}");
        Assert.True(System.MathF.Abs(pt.N.Length() - 1f) < 1e-4f, "chart normal must be unit length");
    }

    [Fact]
    public void Cylinder_Chart_InvertsLateralHit()
    {
        var cyl = new Cylinder(new Vector3(0.3f, -0.5f, -0.2f), 1.2f, 2.0f, Glass);
        AssertChartInvertsHit(cyl, new Vector3(5f, 0.4f, -0.2f), new Vector3(0.3f, 0.4f, -0.2f));
        AssertChartInvertsHit(cyl, new Vector3(-4f, 1.0f, 2.5f), new Vector3(0.3f, 1.0f, -0.2f));
    }

    [Fact]
    public void Cone_Chart_InvertsLateralHit()
    {
        var cone = new Cone(new Vector3(0f, 0f, 0f), 1.0f, 0.3f, 2.0f, Glass);
        AssertChartInvertsHit(cone, new Vector3(5f, 0.5f, 0f), new Vector3(0f, 0.5f, 0f));
        AssertChartInvertsHit(cone, new Vector3(-3f, 1.5f, 1f), new Vector3(0f, 1.5f, 0f));
    }

    [Fact]
    public void Capsule_Chart_InvertsBodyAndHemisphereHits()
    {
        var cap = new Capsule(new Vector3(0f, 0f, 0f), 0.5f, 1.0f, Glass);
        // Cylindrical body (v ∈ [0.25, 0.75]).
        AssertChartInvertsHit(cap, new Vector3(4f, 0.5f, 0f), new Vector3(0f, 0.5f, 0f));
        // Top hemisphere (y > yMax = 1.0).
        AssertChartInvertsHit(cap, new Vector3(3f, 1.3f, 0f), new Vector3(0f, 1.3f, 0f));
        // Bottom hemisphere (y < yMin = 0.0).
        AssertChartInvertsHit(cap, new Vector3(3f, -0.3f, 0f), new Vector3(0f, -0.3f, 0f));
    }

    [Fact]
    public void Torus_Chart_InvertsHit()
    {
        var torus = new Torus(2.0f, 0.5f, Glass);
        // Outer equator of the tube (φ = 0).
        AssertChartInvertsHit(torus, new Vector3(6f, 0f, 0f), new Vector3(2f, 0f, 0f));
        // Top of the tube, different azimuth.
        AssertChartInvertsHit(torus, new Vector3(0f, 4f, 2f), new Vector3(0f, 0f, 2f));
    }

    [Fact]
    public void SmoothTriangle_Chart_InvertsBarycentricHit()
    {
        var tri = new SmoothTriangle(
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
            Vector3.Normalize(new Vector3(-0.2f, -0.2f, 1f)),
            Vector3.Normalize(new Vector3(0.3f, -0.1f, 1f)),
            Vector3.Normalize(new Vector3(-0.1f, 0.3f, 1f)),
            Glass);
        var rec = new HitRecord();
        // Fire from the +z side so the front face (outward normal ≈ +z) is hit.
        Assert.True(tri.Hit(new Ray(new Vector3(0.3f, 0.3f, 2f), new Vector3(0, 0, -1)), 1e-4f, 1e9f, ref rec));
        Assert.True(rec.FrontFace);

        tri.Barycentric(rec.Point, out float u, out float v);
        Assert.True(tri.EvaluateManifold(u, v, out ManifoldPoint pt));
        Assert.True((pt.P - rec.Point).Length() < 1e-4f, $"{pt.P} vs {rec.Point}");
        Assert.True((pt.N - rec.Normal).Length() < 1e-4f, $"{pt.N} vs {rec.Normal}");
    }

    // ── End-to-end: a smooth glass sphere mesh focuses light on-axis, exercising
    //    chart-enter ≠ chart-exit (the two interfaces land on different
    //    triangles). On the symmetry axis the straight-line seed coincides with
    //    the true specular vertex, so the per-triangle clamp keeps the walk inside
    //    the seeded facets — the regime where mesh caustics are well-posed. The
    //    tessellation (50×25) is deliberately not a multiple of 4 so the axis
    //    crossings (u = 0.25, 0.75) land mid-triangle, not on a vertex/edge. ──
    [Fact]
    public void GlassSphereMesh_OnAxis_FocusesOnTheSphereSurface()
    {
        var mesh = BuildGlassSphereMesh(Vector3.Zero, 1f, Glass, 50, 25);
        var caster = new CausticCasterRegistry.Caster(mesh, mesh, mesh.BoundingBox());
        var ci = new CausticInterface(isTransmissive: true, ior: 1.5f, tint: Vector3.One, absorption: Vector3.Zero);

        // Pure on-axis: the straight-line seed coincides with the true specular
        // vertex, so it stays inside the seeded facet. The mesh tessellation is
        // rotated (see BuildGlassSphereMesh) so the z-axis crossings land mid-
        // triangle, not on a vertex/diagonal where the clamp would pin the walk.
        Vector3 x = new(0f, 0f, -4f);
        Vector3 y = new(0f, 0f, 4f);
        bool ok = ManifoldWalker.Connect(caster, ci, x, y, new Vector3(0f, 0f, -1f),
                                         ManifoldWalker.DefaultMaxIterations, out var conn);

        Assert.True(ok, "on-axis connection through the glass sphere mesh must converge");
        // The two specular vertices sit on the sphere near the front/back poles.
        Assert.True((conn.FirstVertex - new Vector3(0, 0, -1f)).Length() < 0.05f, $"front vertex {conn.FirstVertex}");
        Assert.True((conn.LastVertex - new Vector3(0, 0, 1f)).Length() < 0.05f, $"back vertex {conn.LastVertex}");
        // Throughput is the product of the two near-normal transmission Fresnels
        // (~0.96 each at η = 1.5) — strictly within (0, 1].
        Assert.True(conn.Throughput.X > 0.85f && conn.Throughput.X <= 1f, $"throughput {conn.Throughput}");
    }

    [Fact]
    public void FlatTriangleMesh_IsNotASmoothCaster()
    {
        var flat = new List<IHittable>
        {
            new Triangle(new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(0,1,0), Glass),
        };
        var flatMesh = new Mesh(flat, Glass);
        Assert.False(flatMesh.HasVertexNormals);

        var smooth = BuildGlassSphereMesh(Vector3.Zero, 1f, Glass, 8, 4);
        Assert.True(smooth.HasVertexNormals);
    }

    [Fact]
    public void Registry_AcceptsCurvedAndSmoothMesh_RejectsFlatBox()
    {
        var box = new Box(Glass);
        Assert.Equal(0, new CausticCasterRegistry(new List<IHittable> { box }).Count);

        var sphere = new Sphere(Vector3.Zero, 1f, Glass);
        Assert.Equal(1, new CausticCasterRegistry(new List<IHittable> { sphere }).Count);

        var cyl = new Cylinder(Vector3.Zero, 1f, 2f, Glass);
        Assert.Equal(1, new CausticCasterRegistry(new List<IHittable> { cyl }).Count);

        var mesh = BuildGlassSphereMesh(Vector3.Zero, 1f, Glass, 8, 4);
        Assert.Equal(1, new CausticCasterRegistry(new List<IHittable> { mesh }).Count);
    }

    [Fact]
    public void TransformedSphere_Chart_MapsToWorldSpace()
    {
        var sphere = new Sphere(Vector3.Zero, 1f, Glass);
        var moved = new Transform(sphere, Matrix4x4.CreateTranslation(3f, 1f, -2f));
        var caster = CausticCasterRegistry.BuildSeeder(moved);
        Assert.NotNull(caster);

        var ci = new CausticInterface(isTransmissive: true, ior: 1.5f, tint: Vector3.One, absorption: Vector3.Zero);
        Vector3 center = new(3f, 1f, -2f);
        Vector3 x = center + new Vector3(0f, 0f, -5f);
        Vector3 y = center + new Vector3(0f, 0f, 5f);
        var rcaster = new CausticCasterRegistry.Caster(moved, caster!, moved.BoundingBox());
        bool ok = ManifoldWalker.Connect(rcaster, ci, x, y, new Vector3(0, 0, -1),
                                         ManifoldWalker.DefaultMaxIterations, out var conn);
        Assert.True(ok);
        Assert.True(System.MathF.Abs(conn.FirstVertex.Z - (center.Z - 1f)) < 1e-2f, $"{conn.FirstVertex}");
    }

    // Builds a smooth (per-vertex normal) UV sphere of glass triangles. The whole
    // triangulation is rotated by a fixed generic angle: the sphere shape is
    // unchanged (still the unit sphere), but the z-axis crossings then land mid-
    // triangle instead of on a vertex/diagonal — the well-posed regime for the
    // per-triangle clamp on the symmetry axis.
    private static Mesh BuildGlassSphereMesh(Vector3 center, float radius, IMaterial mat, int nU, int nV)
    {
        Matrix4x4 rot = Matrix4x4.CreateRotationY(0.31f) * Matrix4x4.CreateRotationX(0.27f);

        Vector3 At(int iu, int iv)
        {
            float u = (float)iu / nU;          // longitude 0..1
            float v = (float)iv / nV;          // latitude  0..1 (0 = south pole)
            float theta = System.MathF.PI * v; // 0..π
            float phi = 2f * System.MathF.PI * u;
            float st = System.MathF.Sin(theta), ct = System.MathF.Cos(theta);
            var n = new Vector3(st * System.MathF.Cos(phi), ct, st * System.MathF.Sin(phi));
            return Vector3.Normalize(Vector3.TransformNormal(n, rot));
        }

        var tris = new List<IHittable>();
        for (int iv = 0; iv < nV; iv++)
        for (int iu = 0; iu < nU; iu++)
        {
            Vector3 a = At(iu, iv), b = At(iu + 1, iv), c = At(iu + 1, iv + 1), d = At(iu, iv + 1);
            // Normals = positions on the unit sphere; points = center + radius·n.
            tris.Add(new SmoothTriangle(center + radius * a, center + radius * b, center + radius * c, a, b, c, mat));
            tris.Add(new SmoothTriangle(center + radius * a, center + radius * c, center + radius * d, a, c, d, mat));
        }
        return new Mesh(tris, mat, (nU + 1) * (nV + 1));
    }
}
