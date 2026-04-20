using System.Numerics;
using RayTracer.Core;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// AABB slab-test contract: the vectorised implementation must agree with a
/// straightforward reference slab test for representative hit and miss cases,
/// including axis-aligned rays (zero direction component → ±infinity inverse)
/// and rays that start inside the box.
/// </summary>
public class AabbTests
{
    private static bool ReferenceHit(AABB box, Ray ray, float tMin, float tMax)
    {
        // Straight transcription of the classic scalar slab test, used as an
        // independent oracle against the SIMD implementation in AABB.Hit.
        for (int a = 0; a < 3; a++)
        {
            float origin = a == 0 ? ray.Origin.X : a == 1 ? ray.Origin.Y : ray.Origin.Z;
            float dir    = a == 0 ? ray.Direction.X : a == 1 ? ray.Direction.Y : ray.Direction.Z;
            float min    = a == 0 ? box.Min.X : a == 1 ? box.Min.Y : box.Min.Z;
            float max    = a == 0 ? box.Max.X : a == 1 ? box.Max.Y : box.Max.Z;

            float invD = 1f / dir;
            float t0 = (min - origin) * invD;
            float t1 = (max - origin) * invD;
            if (invD < 0f) (t0, t1) = (t1, t0);

            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;
            if (tMax <= tMin) return false;
        }
        return true;
    }

    private static readonly AABB UnitBox =
        new(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

    [Fact]
    public void Hit_DirectFrontRay_Accepts()
    {
        var ray = new Ray(new Vector3(0, 0, -5), Vector3.UnitZ);
        Assert.True(UnitBox.Hit(ray, 0f, 1e30f));
    }

    [Fact]
    public void Hit_BehindBox_Rejects()
    {
        // Ray pointing away from the box
        var ray = new Ray(new Vector3(0, 0, -5), -Vector3.UnitZ);
        Assert.False(UnitBox.Hit(ray, 0f, 1e30f));
    }

    [Fact]
    public void Hit_RayFromInsideBox_Accepts()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        Assert.True(UnitBox.Hit(ray, 0f, 1e30f));
    }

    [Fact]
    public void Hit_AxisAlignedRay_BehaviourMatchesReference()
    {
        // Axis-aligned rays: invD has a ±∞ component. The slab test must
        // agree with the reference implementation for a range of origins.
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var origin = new Vector3(
                (float)(rng.NextDouble() * 6 - 3),
                (float)(rng.NextDouble() * 6 - 3),
                (float)(rng.NextDouble() * 6 - 3));
            Vector3 dir = (i % 3) switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                _ => Vector3.UnitZ
            };
            var ray = new Ray(origin, dir);
            Assert.Equal(
                ReferenceHit(UnitBox, ray, 0f, 1e30f),
                UnitBox.Hit(ray, 0f, 1e30f));
        }
    }

    [Fact]
    public void Hit_GeneralRays_BehaviourMatchesReference()
    {
        var rng = new Random(123);
        for (int i = 0; i < 500; i++)
        {
            var origin = new Vector3(
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 10 - 5));
            var dir = Vector3.Normalize(new Vector3(
                (float)(rng.NextDouble() * 2 - 1) + 1e-3f,
                (float)(rng.NextDouble() * 2 - 1) + 1e-3f,
                (float)(rng.NextDouble() * 2 - 1) + 1e-3f));
            var ray = new Ray(origin, dir);
            Assert.Equal(
                ReferenceHit(UnitBox, ray, 0.001f, 1e30f),
                UnitBox.Hit(ray, 0.001f, 1e30f));
        }
    }

    [Fact]
    public void Ray_InvDirection_IsComponentwiseReciprocal()
    {
        var ray = new Ray(Vector3.Zero, new Vector3(2f, -4f, 0.5f));
        Assert.Equal(0.5f, ray.InvDirection.X, 6);
        Assert.Equal(-0.25f, ray.InvDirection.Y, 6);
        Assert.Equal(2f, ray.InvDirection.Z, 6);
    }
}
