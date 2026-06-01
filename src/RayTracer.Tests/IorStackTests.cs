using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for <see cref="IorStack"/> and the relative-IOR refraction it
/// drives (nested dielectrics). The stack tracks the index of refraction of the
/// medium a ray is currently inside, so a liquid inside glass refracts against
/// the relative ratio η_glass/η_liquid instead of always assuming air outside.
/// </summary>
public class IorStackTests
{
    [Fact]
    public void Empty_TopAndEnclosing_AreAir()
    {
        var s = new IorStack();
        Assert.Equal(0, s.Depth);
        Assert.Equal(1f, s.Top);        // ray in air/vacuum
        Assert.Equal(1f, s.Enclosing);  // nothing enclosing
    }

    [Fact]
    public void SingleEntry_TopIsMaterial_EnclosingIsAir()
    {
        var s = new IorStack();
        s.Push(1.5f);
        Assert.Equal(1, s.Depth);
        Assert.Equal(1.5f, s.Top);
        Assert.Equal(1f, s.Enclosing); // glass surrounded by air
    }

    [Fact]
    public void NestedWineInGlass_TopAndEnclosing_TrackTheStack()
    {
        // Camera in air → enters glass (1.70) → enters wine (1.345).
        var s = new IorStack();
        s.Push(1.70f);
        s.Push(1.345f);
        Assert.Equal(2, s.Depth);
        Assert.Equal(1.345f, s.Top);       // currently inside the wine
        Assert.Equal(1.70f, s.Enclosing);  // enclosed by the glass

        // Exit the wine back into the glass.
        Assert.Equal(1.345f, s.Pop());
        Assert.Equal(1.70f, s.Top);
        Assert.Equal(1f, s.Enclosing);
    }

    [Fact]
    public void Pop_OnEmpty_ReturnsAir_DepthStaysZero()
    {
        var s = new IorStack();
        Assert.Equal(1f, s.Pop());
        Assert.Equal(0, s.Depth);
    }

    [Fact]
    public void Overflow_DropsOldest_ReturnsFalse()
    {
        var s = new IorStack();
        for (int i = 0; i < IorStack.Capacity; i++)
            Assert.True(s.Push(1.1f + i));
        // One past capacity: oldest dropped, returns false, depth pinned.
        Assert.False(s.Push(9.9f));
        Assert.Equal(IorStack.Capacity, s.Depth);
        Assert.Equal(9.9f, s.Top);
    }

    [Fact]
    public void ValueCopy_DoesNotAliasOriginal()
    {
        // The renderer copies the stack at refraction events (copy-on-write),
        // so the recursion's push/pop must not leak back to the caller.
        var s = new IorStack();
        s.Push(1.5f);

        IorStack copy = s;
        copy.Push(1.33f);

        Assert.Equal(1, s.Depth);
        Assert.Equal(1.5f, s.Top);
        Assert.Equal(2, copy.Depth);
        Assert.Equal(1.33f, copy.Top);
    }

    // ── Relative-IOR refraction through Dielectric.Scatter ──────────────────

    private static HitRecord FrontFaceHit(Vector3 normal) => new()
    {
        Point = Vector3.Zero,
        Normal = normal,
        FrontFace = true,
    };

    // Oblique incoming ray at ~20° from the surface normal +Z, travelling into
    // the surface. Below the critical angle for every ratio used here, so the
    // stochastic Fresnel split always has a refraction branch to sample.
    private static Vector3 ObliqueIncoming()
    {
        float theta = 20f * MathF.PI / 180f;
        return Vector3.Normalize(new Vector3(MathF.Sin(theta), 0f, -MathF.Cos(theta)));
    }

    private static Vector3 FirstRefraction(Dielectric mat, HitRecord rec, Vector3 dir)
    {
        // Loop over the stochastic Fresnel coin flip until a transmission is
        // produced (direction crosses to the far side of the shading normal).
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(mat.Scatter(new Ray(rec.Point, dir), rec, out _, out Ray scattered));
            if (Vector3.Dot(scattered.Direction, rec.Normal) < 0f)
                return scattered.Direction;
        }
        throw new Xunit.Sdk.XunitException("no refraction sampled in 1000 tries");
    }

    [Fact]
    public void Dielectric_UnsetRelativeEta_RefractsAgainstAir()
    {
        // Sentinel RelativeEta (0) must reproduce the legacy air-relative eta
        // exactly: this is what keeps non-nested scenes bit-identical.
        var mat = new Dielectric(1.5f);
        var rec = FrontFaceHit(Vector3.UnitZ); // RelativeEta defaults to 0
        Vector3 dir = ObliqueIncoming();

        Vector3 got = FirstRefraction(mat, rec, dir);
        Vector3 expected = MathUtils.Refract(Vector3.Normalize(dir), rec.Normal, 1f / 1.5f);

        Assert.True((got - expected).Length() < 1e-5f,
            $"expected air-relative refraction {expected}, got {got}");
    }

    [Fact]
    public void Dielectric_RelativeEta_BendsAgainstEnclosingMedium()
    {
        // Wine (η = 1.345) entered from inside glass (η = 1.70): the renderer
        // stamps RelativeEta = Top/matIor = 1.70/1.345. The refraction must
        // follow that relative ratio, NOT the air-relative 1/1.345.
        float relEta = 1.70f / 1.345f;
        var mat = new Dielectric(1.345f);
        var rec = FrontFaceHit(Vector3.UnitZ);
        rec.RelativeEta = relEta;
        Vector3 dir = ObliqueIncoming();

        Vector3 got = FirstRefraction(mat, rec, dir);
        Vector3 expectedRel = MathUtils.Refract(Vector3.Normalize(dir), rec.Normal, relEta);
        Vector3 expectedAir = MathUtils.Refract(Vector3.Normalize(dir), rec.Normal, 1f / 1.345f);

        Assert.True((got - expectedRel).Length() < 1e-5f,
            $"expected glass-relative refraction {expectedRel}, got {got}");
        // The two differ measurably — the relative bend is physically distinct
        // from the spurious air-relative one.
        Assert.True((expectedRel - expectedAir).Length() > 1e-3f);
    }
}
