using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for <see cref="AnimatedTransform"/> — the transform-motion-blur
/// keyframe wrapper. Three invariant families:
///   (a) Interpolation: the matrix at a key time equals the static TRS
///       composition of that key, and mid-segment poses are the component-wise
///       lerp / quaternion slerp (shortest arc) of the surrounding keys.
///   (b) Equivalence: identical keyframes ≡ a static <see cref="Transform"/>
///       carrying the same matrix — same hit/miss, rec.T within 1e-4, same
///       normals/LocalPoint — and a ray at t=0 sees exactly the base pose.
///   (c) Motion bounds: BoundingBox() is a conservative union over the whole
///       key range — whenever a ray (at any time) hits the animated object,
///       the box reports a hit too, so a BVH built on it never culls a true
///       hit. Includes a large-rotation case exercising the chord padding.
/// </summary>
public class AnimatedTransformTests
{
    private const float TEpsilon = 1e-4f;

    private static IMaterial Mat() => new Lambertian(new Vector3(0.5f));

    /// <summary>Mirrors SceneLoader's Euler→quaternion conversion (X then Y then Z, row-vector).</summary>
    private static Quaternion Rot(float degX, float degY, float degZ)
    {
        var m = Matrix4x4.CreateRotationX(MathUtils.DegreesToRadians(degX))
              * Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(degY))
              * Matrix4x4.CreateRotationZ(MathUtils.DegreesToRadians(degZ));
        return Quaternion.CreateFromRotationMatrix(m);
    }

    private static MotionKey Key(float t, Vector3 translate, Quaternion? rotate = null, Vector3? scale = null)
        => new(t, translate, rotate ?? Quaternion.Identity, scale ?? Vector3.One);

    private static void AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual, float eps = 1e-5f)
    {
        Assert.Equal(expected.M11, actual.M11, eps); Assert.Equal(expected.M12, actual.M12, eps);
        Assert.Equal(expected.M13, actual.M13, eps); Assert.Equal(expected.M21, actual.M21, eps);
        Assert.Equal(expected.M22, actual.M22, eps); Assert.Equal(expected.M23, actual.M23, eps);
        Assert.Equal(expected.M31, actual.M31, eps); Assert.Equal(expected.M32, actual.M32, eps);
        Assert.Equal(expected.M33, actual.M33, eps); Assert.Equal(expected.M41, actual.M41, eps);
        Assert.Equal(expected.M42, actual.M42, eps); Assert.Equal(expected.M43, actual.M43, eps);
    }

    // ── (a) Interpolation ───────────────────────────────────────────────────

    [Fact]
    public void TranslationLerpsLinearly()
    {
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(1f, new Vector3(2f, 4f, -6f)),
        });

        AssertMatrixEqual(Matrix4x4.CreateTranslation(0f, 0f, 0f), at.MatrixAt(0f));
        AssertMatrixEqual(Matrix4x4.CreateTranslation(1f, 2f, -3f), at.MatrixAt(0.5f));
        AssertMatrixEqual(Matrix4x4.CreateTranslation(2f, 4f, -6f), at.MatrixAt(1f));
        // Outside the key range → clamped to the end poses.
        AssertMatrixEqual(at.MatrixAt(0f), at.MatrixAt(-0.5f));
        AssertMatrixEqual(at.MatrixAt(1f), at.MatrixAt(1.5f));
    }

    [Fact]
    public void RotationSlerpsHalfwayAngle()
    {
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(1f, Vector3.Zero, Rot(0f, 90f, 0f)),
        });

        AssertMatrixEqual(Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(45f)), at.MatrixAt(0.5f));
        AssertMatrixEqual(Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(90f)), at.MatrixAt(1f));
    }

    [Fact]
    public void RotationSlerpTakesShortestArc()
    {
        // 0° → 350° about Y: the shortest arc is −10°, so the midpoint must be
        // −5° (= 355°), NOT +175° (the long way around).
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(1f, Vector3.Zero, Rot(0f, 350f, 0f)),
        });

        AssertMatrixEqual(Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(-5f)), at.MatrixAt(0.5f));
    }

    [Fact]
    public void CombinedTrsMatchesStaticCompositionAtKeys()
    {
        // At a key time the interpolated matrix must equal the same TRS
        // composed the way SceneLoader.ComputeTransformMatrix does it.
        var translate = new Vector3(1f, 2f, 3f);
        var scale = new Vector3(2f, 0.5f, 1.5f);
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(1f, translate, Rot(30f, 60f, -45f), scale),
        });

        var expected = Matrix4x4.CreateScale(scale)
                     * Matrix4x4.CreateRotationX(MathUtils.DegreesToRadians(30f))
                     * Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(60f))
                     * Matrix4x4.CreateRotationZ(MathUtils.DegreesToRadians(-45f))
                     * Matrix4x4.CreateTranslation(translate);
        AssertMatrixEqual(expected, at.MatrixAt(1f));
    }

    [Fact]
    public void MultiSegmentKeysInterpolatePerSegment()
    {
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(0.5f, new Vector3(1f, 0f, 0f)),
            Key(1f, new Vector3(1f, 2f, 0f)),
        });

        AssertMatrixEqual(Matrix4x4.CreateTranslation(0.5f, 0f, 0f), at.MatrixAt(0.25f));
        AssertMatrixEqual(Matrix4x4.CreateTranslation(1f, 0f, 0f), at.MatrixAt(0.5f));
        AssertMatrixEqual(Matrix4x4.CreateTranslation(1f, 1f, 0f), at.MatrixAt(0.75f));
    }

    // ── (b) Equivalence with static Transform ───────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1337)]
    public void IdenticalKeys_BehavesLikeStaticTransform(int seed)
    {
        var translate = new Vector3(0.5f, 1f, -0.5f);
        var rotate = Rot(20f, 45f, 10f);
        var scale = new Vector3(1.5f, 0.8f, 1.2f);
        var matrix = Matrix4x4.CreateScale(scale)
                   * Matrix4x4.CreateFromQuaternion(rotate)
                   * Matrix4x4.CreateTranslation(translate);

        var animated = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            new MotionKey(0f, translate, rotate, scale),
            new MotionKey(1f, translate, rotate, scale),
        });
        var statiq = new Transform(new Sphere(Vector3.Zero, 1f, Mat()), matrix);

        var rng = new Random(seed);
        for (int i = 0; i < 200; i++)
        {
            var origin = new Vector3(
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 10 - 5));
            var target = new Vector3(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1)) + translate;
            float time = (float)rng.NextDouble();
            var ray = new Ray(origin, target - origin, time);

            var recA = new HitRecord();
            var recS = new HitRecord();
            bool hitA = animated.Hit(ray, 1e-3f, float.PositiveInfinity, ref recA);
            bool hitS = statiq.Hit(ray, 1e-3f, float.PositiveInfinity, ref recS);

            Assert.Equal(hitS, hitA);
            if (!hitS) continue;
            Assert.Equal(recS.T, recA.T, TEpsilon);
            Assert.True((recS.Normal - recA.Normal).Length() < 1e-3f);
            Assert.True((recS.Point - recA.Point).Length() < 1e-3f);
            Assert.True((recS.LocalPoint - recA.LocalPoint).Length() < 1e-3f);
        }
    }

    [Fact]
    public void RayAtTimeZero_SeesBasePose()
    {
        // Sphere translating [0,0,0] → [4,0,0]. A ray at t=0 down the Z axis
        // through the origin must hit the base pose; the same ray at t=1 must
        // miss (the sphere has moved 4 units away).
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(1f, new Vector3(4f, 0f, 0f)),
        });

        var rec = new HitRecord();
        Assert.True(at.Hit(new Ray(new Vector3(0, 0, -5), Vector3.UnitZ, 0f), 1e-3f, 1e30f, ref rec));
        rec = new HitRecord();
        Assert.False(at.Hit(new Ray(new Vector3(0, 0, -5), Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
        rec = new HitRecord();
        Assert.True(at.Hit(new Ray(new Vector3(4, 0, -5), Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
    }

    [Fact]
    public void NestedUnderStaticTransform_RayTimeReachesInnerAnimation()
    {
        // AnimatedTransform inside a static Transform: the outer Hit must
        // forward ray.Time on the object-space ray, otherwise the inner
        // animation silently freezes at t=0. Outer = translate +2 on Y; inner
        // animates 0 → +4 on X.
        var inner = new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()), new[]
        {
            Key(0f, Vector3.Zero),
            Key(1f, new Vector3(4f, 0f, 0f)),
        });
        var outer = new Transform(inner, Matrix4x4.CreateTranslation(0f, 2f, 0f));

        var rec = new HitRecord();
        Assert.True(outer.Hit(new Ray(new Vector3(4, 2, -5), Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
        rec = new HitRecord();
        Assert.False(outer.Hit(new Ray(new Vector3(0, 2, -5), Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
    }

    // ── (c) Motion bounds ────────────────────────────────────────────────────

    [Theory]
    [InlineData(7)]
    [InlineData(99)]
    public void BoundingBox_NeverCullsATrueHit_Translation(int seed)
    {
        var at = new AnimatedTransform(new Sphere(Vector3.Zero, 0.5f, Mat()), new[]
        {
            Key(0f, new Vector3(-3f, 0f, 0f)),
            Key(1f, new Vector3(3f, 2f, -1f)),
        });
        AssertBoxConservative(at, seed);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(202)]
    public void BoundingBox_NeverCullsATrueHit_LargeRotation(int seed)
    {
        // Long thin box (unit cube scaled to 6×0.2×0.2) sweeping 170° about Y:
        // corners travel on wide arcs that bulge outside the chord between
        // sampled poses — exercises the sagitta padding.
        var slab = new Vector3(6f, 0.2f, 0.2f);
        var at = new AnimatedTransform(new Box(Mat()), new[]
        {
            Key(0f, Vector3.Zero, null, slab),
            Key(1f, Vector3.Zero, Rot(0f, 170f, 0f), slab),
        });
        AssertBoxConservative(at, seed);
    }

    private static void AssertBoxConservative(AnimatedTransform at, int seed)
    {
        AABB box = at.BoundingBox();
        var rng = new Random(seed);
        int hits = 0;
        for (int i = 0; i < 2000; i++)
        {
            var origin = new Vector3(
                (float)(rng.NextDouble() * 16 - 8),
                (float)(rng.NextDouble() * 16 - 8),
                (float)(rng.NextDouble() * 16 - 8));
            float time = (float)rng.NextDouble();
            // Aim at a random point of the object's local extent transformed
            // to its pose at the sampled time — most rays then truly hit.
            var localTarget = new Vector3(
                (float)(rng.NextDouble() - 0.5),
                (float)(rng.NextDouble() - 0.5),
                (float)(rng.NextDouble() - 0.5));
            var target = Vector3.Transform(localTarget, at.MatrixAt(time));
            var ray = new Ray(origin, target - origin, time);

            var rec = new HitRecord();
            if (!at.Hit(ray, 1e-3f, float.PositiveInfinity, ref rec)) continue;
            hits++;
            // The invariant a BVH relies on: a true hit implies a box hit.
            Assert.True(box.Hit(ray, 1e-3f, float.PositiveInfinity),
                $"Motion box culled a true hit at time {time:0.###} (ray #{i}).");
        }
        Assert.True(hits > 200, $"Degenerate test: only {hits} rays hit the object.");
    }

    [Fact]
    public void Constructor_RequiresAtLeastTwoKeys()
    {
        Assert.Throws<ArgumentException>(() =>
            new AnimatedTransform(new Sphere(Vector3.Zero, 1f, Mat()),
                                  new[] { Key(0f, Vector3.Zero) }));
    }
}
