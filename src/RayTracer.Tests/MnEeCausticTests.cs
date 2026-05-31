using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Analytic contract tests for the Manifold-Next-Event-Estimation caustic
/// solver (<see cref="ManifoldWalker"/>), in the equivalence-oracle style of
/// <c>BvhEquivalenceTests</c>: configurations whose specular path has a
/// closed-form solution, so the Newton walk can be checked against exact
/// geometry and Fresnel values rather than a golden image.
/// </summary>
public class MnEeCausticTests
{
    private static CausticCasterRegistry.Caster GlassSphere(Vector3 center, float radius, float ior)
    {
        var s = new Sphere(center, radius, new Dielectric(ior));
        return new CausticCasterRegistry.Caster(s, new AnalyticManifoldCaster(s, s), s.BoundingBox());
    }

    [Fact]
    public void OnAxis_SolidGlassSphere_ResolvesBothInterfacesAndFresnel()
    {
        // x, sphere centre and y are colinear on the Z axis, so by symmetry the
        // two refraction vertices sit exactly on the axis at ±R and the caustic
        // direction is +Z. Normal incidence ⇒ per-interface Fresnel
        // F = ((n-1)/(n+1))² = 0.04, throughput = (1-F)² = 0.9216.
        var caster = GlassSphere(Vector3.Zero, 1f, 1.5f);
        var ci = new CausticInterface(isTransmissive: true, ior: 1.5f,
                                      tint: Vector3.One, absorption: Vector3.Zero);

        Vector3 x = new(0f, 0f, -5f);
        Vector3 y = new(0f, 0f, 5f);
        Vector3 yN = new(0f, 0f, -1f); // light faces back toward the sphere

        bool ok = ManifoldWalker.Connect(caster, ci, x, y, yN,
                                         ManifoldWalker.DefaultMaxIterations, out var conn);

        Assert.True(ok, "MNEE failed to find the on-axis solid-glass path");
        Assert.True((conn.FirstVertex - new Vector3(0f, 0f, -1f)).Length() < 1e-2f,
                    $"front vertex {conn.FirstVertex} not at (0,0,-1)");
        Assert.True((conn.LastVertex - new Vector3(0f, 0f, 1f)).Length() < 1e-2f,
                    $"back vertex {conn.LastVertex} not at (0,0,1)");
        Assert.True(MathF.Abs(conn.WiAtReceiver.Z - 1f) < 1e-2f,
                    $"caustic direction {conn.WiAtReceiver} not +Z");
        Assert.InRange(conn.Throughput.X, 0.90f, 0.94f); // 0.9216 ± numeric slack
        Assert.True(conn.G > 0f && !float.IsNaN(conn.G), $"degenerate geometric term {conn.G}");
    }

    [Fact]
    public void OffAxis_Converges_AndSatisfiesSnellAtVertices()
    {
        // Off-axis receiver: no closed form for the vertices, but the converged
        // path must satisfy Snell's law at each interface — verified directly
        // by checking the tangential half-vector residual is ~0.
        var caster = GlassSphere(Vector3.Zero, 1f, 1.5f);
        var ci = new CausticInterface(true, 1.5f, Vector3.One, Vector3.Zero);

        Vector3 x = new(0.4f, -0.3f, -4f);
        Vector3 y = new(-0.2f, 0.1f, 4f);
        Vector3 yN = Vector3.Normalize(new Vector3(0f, 0f, -1f));

        bool ok = ManifoldWalker.Connect(caster, ci, x, y, yN,
                                         ManifoldWalker.DefaultMaxIterations, out var conn);
        Assert.True(ok, "MNEE failed to converge off-axis");

        // Snell residual at the first vertex: η_a·ω_a + η_b·ω_b must be parallel
        // to the surface normal (no tangential component).
        Vector3 p1 = conn.FirstVertex;
        Vector3 n1 = Vector3.Normalize(p1 - Vector3.Zero); // sphere outward normal
        Vector3 wa = Vector3.Normalize(x - p1);
        Vector3 wb = Vector3.Normalize(conn.LastVertex - p1);
        float ea = Vector3.Dot(wa, n1) > 0f ? 1f : 1.5f;
        float eb = Vector3.Dot(wb, n1) > 0f ? 1f : 1.5f;
        Vector3 h = Vector3.Normalize(ea * wa + eb * wb);
        Vector3 tangential = h - n1 * Vector3.Dot(h, n1);
        Assert.True(tangential.Length() < 1e-3f,
                    $"Snell not satisfied at vertex 1 (tangential {tangential.Length():G3})");
        Assert.True(conn.Throughput.X > 0f && conn.G > 0f);
    }

    [Fact]
    public void MissingCaster_BetweenReceiverAndLight_Fails()
    {
        // When the straight segment does not pass through the sphere, the
        // refractive seed finds no crossing and the walk declines (no bias).
        var caster = GlassSphere(new Vector3(10f, 0f, 0f), 1f, 1.5f);
        var ci = new CausticInterface(true, 1.5f, Vector3.One, Vector3.Zero);

        Vector3 x = new(0f, 0f, -5f);
        Vector3 y = new(0f, 0f, 5f);
        bool ok = ManifoldWalker.Connect(caster, ci, x, y, new Vector3(0f, 0f, -1f),
                                         ManifoldWalker.DefaultMaxIterations, out _);
        Assert.False(ok);
    }
}
