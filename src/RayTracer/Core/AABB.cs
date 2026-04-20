using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer.Core;

public readonly struct AABB
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Branchless vectorised slab test. Uses the precomputed
    /// <see cref="Ray.InvDirection"/> so no division happens here — all three
    /// axes are processed in parallel through <see cref="Vector3"/> SIMD
    /// intrinsics. Replaces the previous per-axis switch-based scalar loop.
    /// NaN handling relies on <see cref="Vector3.Min(Vector3, Vector3)"/> and
    /// <see cref="Vector3.Max(Vector3, Vector3)"/> returning the non-NaN
    /// operand, which preserves the behaviour of the original slab method for
    /// degenerate axis-aligned rays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Hit(Ray ray, float tMin, float tMax)
    {
        Vector3 t0 = (Min - ray.Origin) * ray.InvDirection;
        Vector3 t1 = (Max - ray.Origin) * ray.InvDirection;

        Vector3 tSmall = Vector3.Min(t0, t1);
        Vector3 tLarge = Vector3.Max(t0, t1);

        float tEnter = MathF.Max(tMin, MathF.Max(tSmall.X, MathF.Max(tSmall.Y, tSmall.Z)));
        float tExit  = MathF.Min(tMax, MathF.Min(tLarge.X, MathF.Min(tLarge.Y, tLarge.Z)));

        return tExit > tEnter;
    }

    public static AABB SurroundingBox(AABB a, AABB b)
    {
        var min = Vector3.Min(a.Min, b.Min);
        var max = Vector3.Max(a.Max, b.Max);
        return new AABB(min, max);
    }

    public static AABB Empty => new(
        new Vector3(float.MaxValue), new Vector3(float.MinValue));
}
