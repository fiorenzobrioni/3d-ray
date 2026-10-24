using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer.Core;

public readonly struct AABB
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    // 1 + 2γ₃, γₙ = nε/(1 − nε) with ε = 2⁻²⁴ (half an ulp of 1).
    private const float ExitWidening = 1f + 2f * (3f * 5.96046448e-8f / (1f - 3f * 5.96046448e-8f));

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
    public bool Hit(in Ray ray, float tMin, float tMax)
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

        // A ray parallel to an axis whose origin lies exactly in a face plane
        // gives 0 × ∞ = NaN on that axis. MaxNumber/MinNumber drop the NaN, so
        // that slab puts no constraint on the ray (conservative, as PBRT):
        // MathF.Max would propagate it and reject the whole BVH subtree.
        float tEnter = float.MaxNumber(tMin, float.MaxNumber(tSmall.X, float.MaxNumber(tSmall.Y, tSmall.Z)));
        float tExit  = float.MinNumber(tMax, float.MinNumber(tLarge.X, float.MinNumber(tLarge.Y, tLarge.Z)));

        // Conservative: the slab distances carry a rounding error of up to
        // γ₃ each, so tExit is widened by 1 + 2γ₃ (Ericson 2004; PBRT-v4
        // §6.1.2) and a ray through the edge of a box, or of the zero-thickness
        // box of a flat triangle, is never rejected by rounding. No primitive
        // needs to pad its bounds by an absolute epsilon, which at small scales
        // would swamp the geometry and at large ones fall below the float
        // spacing (PIANO-REVIEW.md Phase 2). The comparison is inclusive so a
        // flat box with tEnter == tExit is a hit.
        return tExit * ExitWidening >= tEnter;
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
