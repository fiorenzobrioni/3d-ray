using System.Buffers;
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
/// <b>Algorithm — All-hits ray classification:</b>
///
/// For each child, we collect ALL ray–surface intersections (not just the first
/// entry/exit pair). Each intersection carries a FrontFace flag indicating
/// whether the ray is entering or leaving the solid at that point.
///
/// The Boolean result is determined by testing each surface intersection from
/// one child against the other child's solid state at that parameter t. The
/// solid state is computed by counting surface crossings — a point is "inside"
/// if the ray has crossed an odd number of entry surfaces to reach it.
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
/// - Supports arbitrary nesting: a CsgObject is itself an IHittable, so complex
///   trees like ((A ∪ B) \ C) or ((A ∩ B) ∪ (C \ D)) work correctly even when
///   intermediate results are non-convex (e.g., union of disjoint solids).
/// - BVH-compatible: BoundingBox() returns a tight AABB for each operation type.
/// - Transform-compatible: can be wrapped in Transform for scale/rotate/translate.
///
/// <b>Performance:</b>
/// For convex primitives (Sphere, Box, Cylinder, Disk), CollectAllHits produces
/// exactly 2 hits (or 0 on miss) — same cost as the previous two-shot approach.
/// The overhead of the loop and array is negligible compared to the intersection
/// math. For deeply nested CSG trees, the all-hits approach is actually faster
/// because it avoids the information loss that caused incorrect renders, which
/// in turn caused wasted shading work on phantom surfaces.
/// </summary>
public class CsgObject : IHittable
{
    /// <summary>The Boolean operation to perform.</summary>
    public CsgOperation Operation { get; }

    /// <summary>Left operand (A).</summary>
    public IHittable Left { get; }

    /// <summary>Right operand (B).</summary>
    public IHittable Right { get; }

    private int _seed;

    /// <summary>
    /// A CSG object is one logical solid: setting Seed propagates the same
    /// value to both Left and Right operands so procedural textures are
    /// uniform across the carved/combined result.
    /// </summary>
    public int Seed
    {
        get => _seed;
        set
        {
            _seed = value;
            Left.Seed = value;
            Right.Seed = value;
        }
    }

    private readonly AABB _boundingBox;
    // True when the computed AABB is AABB.Empty (non-overlapping Intersection).
    // AABB.Hit() returns true for AABB.Empty (slab test is always satisfied by the
    // inverted min/max sentinels), so we need an explicit flag for early-out.
    private readonly bool _isEmpty;

    public CsgObject(CsgOperation operation, IHittable left, IHittable right)
    {
        Operation = operation;
        Left = left;
        Right = right;
        _boundingBox = ComputeBoundingBox();
        _isEmpty = _boundingBox.Min.X > _boundingBox.Max.X;
    }

    // =========================================================================
    //  Surface hit collection
    // =========================================================================

    /// <summary>
    /// Maximum number of surface intersections collected per child per ray.
    /// A convex primitive produces at most 2; a nested CSG tree with N
    /// subtractions can produce up to 2(N+1). 16 is generous for practical
    /// scenes and prevents runaway loops on degenerate geometry.
    /// </summary>
    private const int MaxHitsPerChild = 16;

    /// <summary>
    /// Absolute floor for the per-hit advance step in CollectAllHits.
    /// The actual step is max(StepEpsAbs, t × StepEpsRel) so the advance
    /// always exceeds one float ULP regardless of the t magnitude.
    ///
    /// Float ULP(t) = t × 2⁻²³ ≈ t × 1.19e-7. At t = 100 the ULP is ~1.2e-5,
    /// so a constant 1e-6 would be a no-op — the loop would stall, collecting
    /// MaxHitsPerChild identical entries whose even count corrupts the parity
    /// counter in IsInsideSolid and inverts the entire CSG result.
    /// </summary>
    private const float StepEpsAbs = 1e-5f;
    private const float StepEpsRel = 1e-5f;

    /// <summary>
    /// A single surface intersection on a child solid, with the full HitRecord
    /// preserving material, UV, normal, and TBN from the original primitive.
    /// </summary>
    private struct SurfaceHit
    {
        public float T;
        public HitRecord Rec;
    }

    /// <summary>
    /// All surface intersections of a ray with one CSG child, collected by
    /// repeatedly calling Hit() and advancing past each intersection.
    ///
    /// The hits are stored in ascending T order (guaranteed by the collection
    /// loop which advances tMin monotonically). FrontFace in each HitRecord
    /// indicates entry (true) vs exit (false) of the solid.
    ///
    /// For convex primitives, Count is 0 (miss) or 2 (entry + exit) or
    /// 1 (ray origin inside → exit only, or tangential graze).
    ///
    /// For non-convex CSG children, Count can be higher (up to MaxHitsPerChild),
    /// capturing all disjoint solid spans along the ray.
    ///
    /// Storage is rented from <see cref="ArrayPool{T}.Shared"/> — every CSG
    /// ray hit used to allocate two fresh <c>SurfaceHit[MaxHitsPerChild]</c>
    /// arrays on the GC heap. <c>SurfaceHit</c> contains a managed reference
    /// (<c>IMaterial</c> in <c>HitRecord</c>) so it cannot be stackalloc'd; the
    /// pool is the next best thing — rent/return is allocation-free after warm-up.
    /// </summary>
    private struct ChildHits
    {
        public SurfaceHit[] Hits;
        public int Count;
        /// <summary>
        /// True if the ray origin is inside the child solid (first intersection
        /// is a back-face / exit). Used by IsInsideSolid for points before the
        /// first intersection.
        /// </summary>
        public bool StartsInside;
    }

    // =========================================================================
    //  Core intersection — IHittable.Hit()
    // =========================================================================

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Early AABB rejection — avoids expensive child intersection tests.
        // _isEmpty must be checked first: AABB.Hit() incorrectly returns true
        // for AABB.Empty because the inverted sentinel values satisfy the slab
        // test for any ray direction.
        if (_isEmpty || !_boundingBox.Hit(ray, tMin, tMax))
            return false;

        // Collect ALL surface intersections for both children. Buffers are
        // rented from the shared pool so the per-hit allocation drops to zero
        // after the first warm-up call on each thread. ArrayPool may hand back
        // an array LARGER than requested — Count is the source of truth, not
        // Hits.Length, so the rest of the code already does the right thing.
        var pool = ArrayPool<SurfaceHit>.Shared;
        SurfaceHit[] bufA = pool.Rent(MaxHitsPerChild);
        SurfaceHit[] bufB = pool.Rent(MaxHitsPerChild);
        try
        {
            ChildHits hitsA = CollectAllHits(Left,  ray, tMin, bufA);
            ChildHits hitsB = CollectAllHits(Right, ray, tMin, bufB);

            return Operation switch
            {
                CsgOperation.Union        => HitUnion(ray, tMin, tMax, ref rec, in hitsA, in hitsB),
                CsgOperation.Intersection => HitIntersection(ray, tMin, tMax, ref rec, in hitsA, in hitsB),
                CsgOperation.Subtraction  => HitSubtraction(ray, tMin, tMax, ref rec, in hitsA, in hitsB),
                _ => false
            };
        }
        finally
        {
            // clearArray: false — SurfaceHit is overwritten before reuse by
            // the next Rent caller, no need to zero it out and pay the wipe.
            pool.Return(bufA);
            pool.Return(bufB);
        }
    }

    // =========================================================================
    //  All-hits collection
    // =========================================================================

    /// <summary>
    /// Collects every ray–surface intersection with a child solid by repeatedly
    /// calling Hit() and advancing tMin past each found intersection.
    ///
    /// This replaces the old two-shot ComputeInterval approach. For convex
    /// primitives the result is identical (2 hits), but for non-convex CSG
    /// children it correctly captures all disjoint solid spans.
    /// </summary>
    private static ChildHits CollectAllHits(IHittable child, Ray ray, float tMin, SurfaceHit[] buffer)
    {
        var result = new ChildHits
        {
            Hits = buffer,
            Count = 0,
            StartsInside = false
        };

        float currentT = tMin;

        // ArrayPool may hand back an array larger than MaxHitsPerChild — cap
        // collection at the logical limit, not the physical buffer length, so
        // the algorithm's worst-case complexity stays the same.
        int cap = Math.Min(buffer.Length, MaxHitsPerChild);
        while (result.Count < cap)
        {
            var rec = new HitRecord();
            // Search all the way to infinity — we need the full geometry for
            // IsInsideSolid tests, not just hits within the caller's [tMin, tMax].
            if (!child.Hit(ray, currentT, float.PositiveInfinity, ref rec))
                break;

            // First hit tells us whether the ray starts inside the solid.
            if (result.Count == 0 && !rec.FrontFace)
                result.StartsInside = true;

            result.Hits[result.Count++] = new SurfaceHit { T = rec.T, Rec = rec };
            currentT = rec.T + MathF.Max(StepEpsAbs, rec.T * StepEpsRel);
        }

        return result;
    }

    // =========================================================================
    //  Point-in-solid test (crossing-number method)
    // =========================================================================

    /// <summary>
    /// Tests whether a point at parameter t along the ray is inside a child
    /// solid, using the crossing-number method on the collected surface hits.
    ///
    /// The ray starts outside (or inside if StartsInside is true). Each surface
    /// crossing toggles the inside/outside state. A point exactly on a surface
    /// (within tolerance) is treated as inside for robustness at CSG boundaries.
    ///
    /// This replaces the old interval-based IsInsideSolid and correctly handles
    /// non-convex children with multiple disjoint solid spans.
    /// </summary>
    private static bool IsInsideSolid(float t, in ChildHits hits)
    {
        if (hits.Count == 0) return false;

        const float tolerance = 1e-5f;

        bool inside = hits.StartsInside;
        for (int i = 0; i < hits.Count; i++)
        {
            float hitT = hits.Hits[i].T;

            // Point is ON this surface (within tolerance) — treat as inside.
            // This prevents surface acne on CSG boundaries where two primitives
            // share exactly the same face.
            if (t >= hitT - tolerance && t <= hitT + tolerance)
                return true;

            // This surface crossing is before our test point — toggle state.
            if (hitT < t - tolerance)
            {
                inside = !inside;
            }
            else
            {
                // All remaining hits are past t — stop.
                break;
            }
        }

        return inside;
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

        if (flipNormal)
        {
            // Subtraction reuses B's hit as a boundary of (A \ B), but the
            // geometric outward of (A \ B) is opposite to B's geometric outward
            // (B's interior becomes (A \ B)'s exterior). For the shading pipeline
            // only FrontFace must flip — rec.Normal is the SHADING normal, which
            // is by convention always oriented against the incoming ray. That
            // orientation is independent of the geometric outward and is
            // unchanged by the role swap, so flipping rec.Normal here would push
            // it co-directional with the ray and zero out NdotL on Lambertian /
            // Disney reflection lobes (the carved interior renders black).
            // FrontFace, on the other hand, encodes "is the ray entering the
            // shaded solid?" — which DOES flip when B's surface becomes part of
            // (A \ B): a B back-face hit (ray exiting B from inside the cavity
            // back into A material) is an entry of (A \ B), and vice versa.
            bestHit.FrontFace = !bestHit.FrontFace;
        }

        found = true;
    }

    // =========================================================================
    //  Boolean operation handlers
    // =========================================================================

    /// <summary>
    /// A ∪ B — Union. A surface from either child is visible if the point is
    /// NOT inside the other child.
    /// </summary>
    private static bool HitUnion(Ray ray, float tMin, float tMax,
        ref HitRecord rec, in ChildHits hitsA, in ChildHits hitsB)
    {
        float bestT = float.MaxValue;
        HitRecord bestHit = default;
        bool found = false;

        // All surfaces of A — visible where NOT inside B
        for (int i = 0; i < hitsA.Count; i++)
        {
            ref readonly var s = ref hitsA.Hits[i];
            if (!IsInsideSolid(s.T, in hitsB))
                TryCandidate(s.T, in s.Rec, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        // All surfaces of B — visible where NOT inside A
        for (int i = 0; i < hitsB.Count; i++)
        {
            ref readonly var s = ref hitsB.Hits[i];
            if (!IsInsideSolid(s.T, in hitsA))
                TryCandidate(s.T, in s.Rec, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        if (!found) return false;
        rec = bestHit;
        return true;
    }

    /// <summary>
    /// A ∩ B — Intersection. A surface from either child is visible only if the
    /// point IS inside the other child (both solids overlap at that point).
    /// </summary>
    private static bool HitIntersection(Ray ray, float tMin, float tMax,
        ref HitRecord rec, in ChildHits hitsA, in ChildHits hitsB)
    {
        float bestT = float.MaxValue;
        HitRecord bestHit = default;
        bool found = false;

        // Surfaces of A — visible where inside B
        for (int i = 0; i < hitsA.Count; i++)
        {
            ref readonly var s = ref hitsA.Hits[i];
            if (IsInsideSolid(s.T, in hitsB))
                TryCandidate(s.T, in s.Rec, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        // Surfaces of B — visible where inside A
        for (int i = 0; i < hitsB.Count; i++)
        {
            ref readonly var s = ref hitsB.Hits[i];
            if (IsInsideSolid(s.T, in hitsA))
                TryCandidate(s.T, in s.Rec, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        if (!found) return false;
        rec = bestHit;
        return true;
    }

    /// <summary>
    /// A \ B — Subtraction. The result exists where A is solid and B is not.
    ///
    ///   • A's surfaces are visible where NOT inside B  (A's exposed outer/inner walls)
    ///   • B's surfaces are visible where inside A      (B carves into A — normals FLIPPED)
    ///
    /// B's surface normals are flipped because the interior of B becomes an
    /// exterior surface of the resulting solid.
    /// </summary>
    private static bool HitSubtraction(Ray ray, float tMin, float tMax,
        ref HitRecord rec, in ChildHits hitsA, in ChildHits hitsB)
    {
        float bestT = float.MaxValue;
        HitRecord bestHit = default;
        bool found = false;

        // A's surfaces — visible where B is absent
        for (int i = 0; i < hitsA.Count; i++)
        {
            ref readonly var s = ref hitsA.Hits[i];
            if (!IsInsideSolid(s.T, in hitsB))
                TryCandidate(s.T, in s.Rec, ray, tMin, tMax, ref bestT, ref bestHit, ref found);
        }

        // B's surfaces — visible where they carve into A (normals flipped)
        for (int i = 0; i < hitsB.Count; i++)
        {
            ref readonly var s = ref hitsB.Hits[i];
            if (IsInsideSolid(s.T, in hitsA))
                TryCandidate(s.T, in s.Rec, ray, tMin, tMax, ref bestT, ref bestHit, ref found, flipNormal: true);
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

            CsgOperation.Intersection => ComputeIntersectionBox(boxA, boxB),

            CsgOperation.Subtraction => boxA,

            _ => AABB.SurroundingBox(boxA, boxB)
        };
    }

    private static AABB ComputeIntersectionBox(AABB boxA, AABB boxB)
    {
        var min = Vector3.Max(boxA.Min, boxB.Min);
        var max = Vector3.Min(boxA.Max, boxB.Max);
        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            return AABB.Empty;
        return new AABB(min, max);
    }
}
