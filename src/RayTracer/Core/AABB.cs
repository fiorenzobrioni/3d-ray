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
        // Reject the sentinel/degenerate empty box explicitly. AABB.Empty has
        // inverted bounds (Min = +inf, Max = -inf); without this guard the slab
        // test below would spuriously report a hit (tExit ≥ tEnter holds for the
        // inverted interval), silently disabling the early-out for any empty
        // HittableList / Group child. A *valid* flat box has Min == Max on an
        // axis (never Min > Max), so this never rejects real geometry.
        if (Min.X > Max.X | Min.Y > Max.Y | Min.Z > Max.Z) return false;

        Vector3 t0 = (Min - ray.Origin) * ray.InvDirection;
        Vector3 t1 = (Max - ray.Origin) * ray.InvDirection;

        Vector3 tSmall = Vector3.Min(t0, t1);
        Vector3 tLarge = Vector3.Max(t0, t1);

        float tEnter = MathF.Max(tMin, MathF.Max(tSmall.X, MathF.Max(tSmall.Y, tSmall.Z)));
        float tExit  = MathF.Min(tMax, MathF.Min(tLarge.X, MathF.Min(tLarge.Y, tLarge.Z)));

        // Inclusive comparison so a flat (zero-thickness) box — a quad/plane/disk
        // BVH leaf, or a ray grazing a face where tEnter == tExit — is reported
        // as a hit instead of being silently dropped. Callers no longer need to
        // pad such leaves' bounds by an epsilon.
        return tExit >= tEnter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AABB SurroundingBox(AABB a, AABB b)
    {
        var min = Vector3.Min(a.Min, b.Min);
        var max = Vector3.Max(a.Max, b.Max);
        return new AABB(min, max);
    }

    /// <summary>
    /// Surface area of the box (2·(w·h + w·d + h·d)). Used by the BVH
    /// Surface-Area Heuristic to weight candidate splits by the probability
    /// that a random ray hits each child — which is proportional to the
    /// child's surface area over the parent's.
    /// Returns 0 for an empty/degenerate box (any extent negative).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float SurfaceArea()
    {
        Vector3 d = Max - Min;
        if (d.X < 0f || d.Y < 0f || d.Z < 0f) return 0f;
        return 2f * (d.X * d.Y + d.X * d.Z + d.Y * d.Z);
    }

    public static AABB Empty => new(
        new Vector3(float.MaxValue), new Vector3(float.MinValue));
}
