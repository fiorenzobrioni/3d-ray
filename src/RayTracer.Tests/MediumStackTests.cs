using System.Numerics;
using RayTracer.Core;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for <see cref="MediumStack"/>: push/pop balance, overflow drop-
/// oldest, depth tracking, and value-copy semantics under copy-on-write.
/// These are the invariants the path tracer relies on when threading the
/// stack through recursive refraction events.
/// </summary>
public class MediumStackTests
{
    private sealed class FakeMedium : IMedium
    {
        public IPhaseFunction Phase { get; } = new IsotropicPhase();
        public Vector3 Transmittance(Ray ray, float tMax) => Vector3.One;
        public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
        {
            t = tMax; beta = Vector3.One; scattered = false; return false;
        }
    }

    [Fact]
    public void EmptyStack_TopIsNull_DepthIsZero()
    {
        var s = new MediumStack();
        Assert.Equal(0, s.Depth);
        Assert.Null(s.Top);
    }

    [Fact]
    public void Push_IncrementsDepth_TopMatchesLastPushed()
    {
        var a = new FakeMedium();
        var b = new FakeMedium();
        var s = new MediumStack();
        s.Push(a);
        Assert.Equal(1, s.Depth);
        Assert.Same(a, s.Top);
        s.Push(b);
        Assert.Equal(2, s.Depth);
        Assert.Same(b, s.Top);
    }

    [Fact]
    public void Pop_RestoresPreviousTop_AndReturnsPopped()
    {
        var a = new FakeMedium();
        var b = new FakeMedium();
        var s = new MediumStack();
        s.Push(a);
        s.Push(b);
        var popped = s.Pop();
        Assert.Same(b, popped);
        Assert.Equal(1, s.Depth);
        Assert.Same(a, s.Top);
    }

    [Fact]
    public void Pop_OnEmpty_ReturnsNull_DepthStaysZero()
    {
        var s = new MediumStack();
        var popped = s.Pop();
        Assert.Null(popped);
        Assert.Equal(0, s.Depth);
    }

    [Fact]
    public void Overflow_DropsOldest_ReturnsFalse()
    {
        var s = new MediumStack();
        var media = new FakeMedium[MediumStack.Capacity + 1];
        for (int i = 0; i < MediumStack.Capacity; i++)
        {
            media[i] = new FakeMedium();
            Assert.True(s.Push(media[i]));
        }
        media[MediumStack.Capacity] = new FakeMedium();
        // Capacity-th push overflows: oldest is dropped, returns false.
        Assert.False(s.Push(media[MediumStack.Capacity]));
        Assert.Equal(MediumStack.Capacity, s.Depth);
        Assert.Same(media[MediumStack.Capacity], s.Top);

        // Popping all back: most recently pushed comes out first, oldest is gone.
        Assert.Same(media[MediumStack.Capacity], s.Pop());
        for (int i = MediumStack.Capacity - 1; i >= 1; i--)
            Assert.Same(media[i], s.Pop());
        Assert.Null(s.Pop()); // media[0] was dropped on overflow
    }

    [Fact]
    public void Push_Null_IsTopOfStack()
    {
        // Disney glass refracts through a wrapped entity whose
        // MediumInterface.Interior is null (binding declared only on the
        // exterior side, or boundary set up by the SSS auto-derivation
        // without an internal medium). The push must still occur so the
        // matching exit pop balances correctly, but Top must reflect the
        // null — the renderer's `mediums.Top ?? _globalMedium` then falls
        // through cleanly to the global medium / vacuum.
        var s = new MediumStack();
        s.Push(null);
        Assert.Equal(1, s.Depth);
        Assert.Null(s.Top);
        Assert.Null(s.Pop());
        Assert.Equal(0, s.Depth);
    }

    [Fact]
    public void ValueCopy_DoesNotAliasOriginal()
    {
        // Renderer's copy-on-write at refraction depends on this: the
        // recursion's mutations to its local copy must not leak back to
        // the caller's stack.
        var s = new MediumStack();
        var a = new FakeMedium();
        s.Push(a);

        MediumStack copy = s; // value semantics
        var b = new FakeMedium();
        copy.Push(b);

        Assert.Equal(1, s.Depth);
        Assert.Same(a, s.Top);
        Assert.Equal(2, copy.Depth);
        Assert.Same(b, copy.Top);
    }
}

/// <summary>
/// Routing test for <see cref="Geometry.MediumBoundHittable"/>: every hit
/// against the wrapped entity must populate <see cref="HitRecord.MediumIface"/>
/// so the renderer can read the binding without consulting any scene-level
/// dictionary.
/// </summary>
public class MediumBoundHittableTests
{
    private sealed class FakeMedium : IMedium
    {
        public IPhaseFunction Phase { get; } = new IsotropicPhase();
        public Vector3 Transmittance(Ray ray, float tMax) => Vector3.One;
        public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
        {
            t = tMax; beta = Vector3.One; scattered = false; return false;
        }
    }

    [Fact]
    public void Hit_StampsMediumInterfaceOnRecord()
    {
        var interior = new FakeMedium();
        var sphere = new RayTracer.Geometry.Sphere(Vector3.Zero, 1f, new RayTracer.Materials.Lambertian(Vector3.One));
        var bound = new RayTracer.Geometry.MediumBoundHittable(sphere, new MediumInterface(interior, exterior: null));

        // Ray from -Z hitting the sphere head-on.
        var ray = new Ray(new Vector3(0f, 0f, -3f), Vector3.UnitZ);
        var rec = new HitRecord();
        bool hit = bound.Hit(ray, 0.001f, 100f, ref rec);

        Assert.True(hit);
        Assert.Same(interior, rec.MediumIface.Interior);
        Assert.Null(rec.MediumIface.Exterior);
        // The wrapper must forward bounding box + seed unchanged.
        Assert.Equal(sphere.BoundingBox().Min, bound.BoundingBox().Min);
        Assert.Equal(sphere.BoundingBox().Max, bound.BoundingBox().Max);
    }

    [Fact]
    public void Miss_DoesNotTouchRecord()
    {
        var interior = new FakeMedium();
        var sphere = new RayTracer.Geometry.Sphere(Vector3.Zero, 1f, new RayTracer.Materials.Lambertian(Vector3.One));
        var bound = new RayTracer.Geometry.MediumBoundHittable(sphere, new MediumInterface(interior, null));

        // Ray pointing away from the sphere.
        var ray = new Ray(new Vector3(0f, 0f, -3f), -Vector3.UnitZ);
        var rec = new HitRecord();
        bool hit = bound.Hit(ray, 0.001f, 100f, ref rec);

        Assert.False(hit);
        Assert.Null(rec.MediumIface.Interior);
        Assert.Null(rec.MediumIface.Exterior);
    }
}
