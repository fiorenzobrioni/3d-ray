using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Constructive Solid Geometry (CSG) — Boolean operations on solid primitives.
///
/// Supports three operations:
///   • Union        — A ∪ B  (combined volume of both solids)
///   • Intersection — A ∩ B  (only the overlapping volume)
///   • Subtraction  — A \ B  (A with B carved out)
///
/// <b>Algorithm — Interval-based ray classification:</b>
///
/// For convex primitives (sphere, box, cylinder, disk-capped cylinders) each
/// ray–solid intersection produces exactly one entry/exit interval [tEnter, tExit].
/// The CSG result is determined by collecting ALL surface intersections from both
/// children, then selecting the closest one that satisfies the Boolean condition:
///
///   Union:        surface point is NOT inside the OTHER solid
///   Intersection: surface point IS inside the OTHER solid
///   Subtraction:  A-surface NOT inside B, or B-surface inside A (with flipped normal)
///
/// <b>Design notes:</b>
/// - No modifications to IHittable or existing primitives are required.
/// - Each child's HitRecord (material, UV, normal, TBN) is preserved as-is,
///   so textures and normal maps work correctly on CSG surfaces.
/// - For subtraction (A \ B), hits on B's surface that form the carved boundary
///   have their normals flipped (the interior of B becomes an exterior surface).
/// - Supports nesting: a CsgObject is itself an IHittable, so complex trees
///   like (A ∪ B) \ C or (A ∩ B) ∪ (C \ D) work naturally.
/// - BVH-compatible: BoundingBox() returns a tight AABB for each operation type.
/// - Transform-compatible: can be wrapped in Transform for scale/rotate/translate.
///
/// <b>Convex-primitive assumption (phase 1):</b>
/// The current implementation assumes each child produces at most one contiguous
/// interval per ray (two surface intersections: one entry, one exit). This is
/// correct for all current primitives (Sphere, Box, Cylinder, Disk) and for
/// Transform-wrapped versions of these. For future non-convex primitives
/// (e.g., Torus) or deeply nested CSG trees that may produce multiple disjoint
/// intervals per child, the interval collection can be extended to handle
/// multiple spans. The architecture makes this extension straightforward.
/// </summary>
public class CsgObject : IHittable
{
    /// <summary>The Boolean operation to perform.</summary>
    public CsgOperation Operation { get; }

    /// <summary>Left operand (A).</summary>
    public IHittable Left { get; }

    /// <summary>Right operand (B).</summary>
    public IHittable Right { get; }

    public int Seed { get; set; }

    private readonly AABB _boundingBox;

    public CsgObject(CsgOperation operation, IHittable left, IHittable right)
    {
        Operation = operation;
        Left = left;
        Right = right;
        _boundingBox = ComputeBoundingBox();
    }

    // =========================================================================
    //  Ray interval — represents a solid span [tEnter, tExit] along a ray
    // =========================================================================

    /// <summary>
    /// A contiguous interval where the ray is inside a solid, along with the
    /// HitRecords at the entry and exit boundaries. These records carry material,
    /// UV, normal, and TBN data from the original primitive.
    /// </summary>
    private struct RayInterval
    {
        public float TEnter;
        public float TExit;
        public HitRecord EnterHit;  // Surface data at the entry point
        public HitRecord ExitHit;   // Surface data at the exit point
        public bool Valid;          // True if the interval was successfully computed
    }

    // =========================================================================
    //  Core intersection — IHittable.Hit()
    // =========================================================================

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Early AABB rejection — avoids expensive child intersection tests
        if (!_boundingBox.Hit(ray, tMin, tMax))
            return false;

        // Collect ray intervals for both children
        var intervalA = ComputeInterval(Left, ray, tMin, tMax);
        var intervalB = ComputeInterval(Right, ray, tMin, tMax);

        return Operation switch
        {
            CsgOperation.Union        => HitUnion(ray, tMin, tMax, ref rec, in intervalA, in intervalB),
            CsgOperation.Intersection => HitIntersection(ray, tMin, tMax, ref rec, in intervalA, in intervalB),
            CsgOperation.Subtraction  => HitSubtraction(ray, tMin, tMax, ref rec, in intervalA, in intervalB),
            _ => false
        };
    }

    // =========================================================================
    //  Interval computation
    // =========================================================================

    /// <summary>
    /// Computes the solid interval [tEnter, tExit] for a convex child.
    ///
    /// Strategy for convex solids — a ray intersects a convex solid at most
    /// twice (entry and exit). We find both by:
    ///
    ///   1. First Hit() call → gives the nearest intersection.
    ///      If FrontFace=true, the ray enters the solid (exterior → interior).
    ///      If FrontFace=false, the ray origin is inside the solid (exit first).
    ///
    ///   2. Second Hit() call with tMin advanced just past hit1 → gives the
    ///      exit point when the ray started outside.
    ///
    /// For nested CSG children, the inner CsgObject.Hit() returns only the
    /// nearest visible surface, not raw intervals — but because we call it
    /// twice (once for entry, once for exit), we still reconstruct the
    /// composite interval correctly.
    /// </summary>
    private static RayInterval ComputeInterval(IHittable child, Ray ray, float tMin, float tMax)
    {
        var interval = new RayInterval { Valid = false };

        var hit1 = new HitRecord();
        if (!child.Hit(ray, tMin, tMax, ref hit1))
            return interval; // Ray misses entirely

        // Advance slightly past the first hit to find the second intersection.
        const float stepEps = 1e-4f;
        var hit2 = new HitRecord();
        bool hasSecondHit = child.Hit(ray, hit1.T + stepEps, tMax, ref hit2);

        if (hit1.FrontFace)
        {
            // Ray entered the solid at hit1 (front face = entering from outside)
            interval.TEnter = hit1.T;
            interval.EnterHit = hit1;

            if (hasSecondHit)
            {
                // Normal case: entry at hit1, exit at hit2
                interval.TExit = hit2.T;
                interval.ExitHit = hit2;
            }
            else
            {
                // Only one intersection — solid extends beyond tMax, or numerical
                // edge case (ray tangent to surface). Treat as half-open interval.
                interval.TExit = tMax;
                interval.ExitHit = hit1;
            }
            interval.Valid = true;
        }
        else
        {
            // Ray started INSIDE the solid — hit1 is a back face (exit point).
            // The entry is behind the ray origin (t < tMin), so the effective
            // interval starts at tMin.
            interval.TEnter = tMin;
            interval.EnterHit = hit1;
            interval.TExit = hit1.T;
            interval.ExitHit = hit1;
            interval.Valid = true;
        }

        return interval;
    }

    // =========================================================================
    //  Point-in-solid test
    // =========================================================================

    /// <summary>
    /// Tests whether a point at parameter t along the ray is inside a solid,
    /// given the precomputed interval. Uses a small tolerance to handle surface
    /// coincidence (two primitives sharing the same face).
    /// </summary>
    private static bool IsInsideSolid(float t, in RayInterval interval)
    {
        if (!interval.Valid) return false;
        // Small tolerance to handle points exactly on the surface boundary.
        // Without this, a point at exactly tEnter or tExit could flip due to
        // floating-point imprecision, causing surface acne on CSG boundaries.
        const float tolerance = 1e-5f;
        return t >= (interval.TEnter - tolerance) && t <= (interval.TExit + tolerance);
    }

    // =========================================================================
    //  Candidate evaluation helper
    // =========================================================================

    /// <summary>
    /// Evaluates a candidate hit point. If valid and closer than the current best,
    /// updates bestT, bestHit, and sets found=true.
    /// </summary>
    private static void TryCandidate(
        float t, in HitRecord hit, Ray ray,
        float tMin, float tMax,
        ref float bestT, ref HitRecord bestHit, ref bool found,
        bool flipNormal = false)
    {
        if (t < tMin || t > tMax || t >= bestT) return;

        bestT = t;
        bestHit = hit;
        bestHit.T = t;
        bestHit.Point = ray.At(t);

        if (flipNormal)
        {
            // Flip normal — interior surface of B becomes exterior surface of (A \ B).
            // Also flip FrontFace to maintain consistency with the shading pipeline
            // (Dielectric and Disney BSDF use FrontFace for IOR direction).
            bestHit.Normal = -bestHit.Normal;
            bestHit.FrontFace = !bestHit.FrontFace;
        }

        found = true;
    }

    // =========================================================================
    //  Boolean operation handlers
    // =========================================================================

    /// <summary>
    /// A ∪ B — Union. The ray hits the union wherever it enters either solid
    /// from the outside (i.e., not already inside the other solid).
    ///
    /// Valid surfaces:
    ///   • Entry of A, if NOT inside B (A's outer surface exposed)
    ///   • Entry of B, if NOT inside A (B's outer surface exposed)
    ///   • Exit of A, if NOT inside B (A's inner surface visible from inside)
    ///   • Exit of B, if NOT inside A
    /// </summary>
    private static bool HitUnion(Ray ray, float tMin, float tMax,
        ref HitRecord rec, in RayInterval a, in RayInterval b)
    {
        float bestT = float.MaxValue;
        HitRecord bestHit = default;
        bool found = false;

        if (a.Valid)
        {
            if (!IsInsideSolid(a.TEnter, in b))
                TryCandidate(a.TEnter, in a.EnterHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
            if (!IsInsideSolid(a.TExit, in b))
                TryCandidate(a.TExit, in a.ExitHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        if (b.Valid)
        {
            if (!IsInsideSolid(b.TEnter, in a))
                TryCandidate(b.TEnter, in b.EnterHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
            if (!IsInsideSolid(b.TExit, in a))
                TryCandidate(b.TExit, in b.ExitHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        if (!found) return false;
        rec = bestHit;
        return true;
    }

    /// <summary>
    /// A ∩ B — Intersection. The ray hits only where both solids overlap.
    ///
    /// Valid surfaces:
    ///   • Entry of A, if inside B (A's surface enters the overlap zone)
    ///   • Entry of B, if inside A (B's surface enters the overlap zone)
    ///   • Exit of A, if inside B (leaving through A's surface)
    ///   • Exit of B, if inside A (leaving through B's surface)
    /// </summary>
    private static bool HitIntersection(Ray ray, float tMin, float tMax,
        ref HitRecord rec, in RayInterval a, in RayInterval b)
    {
        float bestT = float.MaxValue;
        HitRecord bestHit = default;
        bool found = false;

        if (a.Valid)
        {
            if (IsInsideSolid(a.TEnter, in b))
                TryCandidate(a.TEnter, in a.EnterHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
            if (IsInsideSolid(a.TExit, in b))
                TryCandidate(a.TExit, in a.ExitHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        if (b.Valid)
        {
            if (IsInsideSolid(b.TEnter, in a))
                TryCandidate(b.TEnter, in b.EnterHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
            if (IsInsideSolid(b.TExit, in a))
                TryCandidate(b.TExit, in b.ExitHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        if (!found) return false;
        rec = bestHit;
        return true;
    }

    /// <summary>
    /// A \ B — Subtraction. The ray hits where A is solid and B is not.
    ///
    /// Valid surfaces:
    ///   • Entry of A, if NOT inside B (A's outer surface is exposed)
    ///   • Exit of A, if NOT inside B (A's exit is exposed)
    ///   • Entry of B, if inside A (B carves into A — normal FLIPPED)
    ///   • Exit of B, if inside A (B stops carving — normal FLIPPED)
    ///
    /// B's surface normals are flipped because the interior of B becomes an
    /// exterior surface of the resulting solid. This affects:
    ///   - Direct lighting (N·L for diffuse, N·H for specular)
    ///   - Scatter direction (reflection/refraction)
    ///   - Shadow ray origin offset
    ///   - Normal map TBN frame (inverted along N)
    /// </summary>
    private static bool HitSubtraction(Ray ray, float tMin, float tMax,
        ref HitRecord rec, in RayInterval a, in RayInterval b)
    {
        float bestT = float.MaxValue;
        HitRecord bestHit = default;
        bool found = false;

        // A's surfaces — visible where B is absent
        if (a.Valid)
        {
            if (!IsInsideSolid(a.TEnter, in b))
                TryCandidate(a.TEnter, in a.EnterHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
            if (!IsInsideSolid(a.TExit, in b))
                TryCandidate(a.TExit, in a.ExitHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        // B's surfaces — visible where they carve into A (normals flipped)
        if (b.Valid)
        {
            if (IsInsideSolid(b.TEnter, in a))
                TryCandidate(b.TEnter, in b.EnterHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found, flipNormal: true);
            if (IsInsideSolid(b.TExit, in a))
                TryCandidate(b.TExit, in b.ExitHit, ray, tMin, tMax, ref bestT, ref bestHit, ref found, flipNormal: true);
        }

        if (!found) return false;
        rec = bestHit;
        return true;
    }

    // =========================================================================
    //  Bounding box
    // =========================================================================

    public AABB BoundingBox() => _boundingBox;

    /// <summary>
    /// Computes the tightest AABB for the CSG result based on the operation:
    ///   Union:        enclosing box of both children
    ///   Intersection: overlap of both AABBs (always ≤ either child)
    ///   Subtraction:  same as A (result cannot exceed A's volume)
    /// </summary>
    private AABB ComputeBoundingBox()
    {
        var boxA = Left.BoundingBox();
        var boxB = Right.BoundingBox();

        return Operation switch
        {
            CsgOperation.Union => AABB.SurroundingBox(boxA, boxB),

            CsgOperation.Intersection => new AABB(
                Vector3.Max(boxA.Min, boxB.Min),
                Vector3.Min(boxA.Max, boxB.Max)),

            CsgOperation.Subtraction => boxA,

            _ => AABB.SurroundingBox(boxA, boxB)
        };
    }
}
