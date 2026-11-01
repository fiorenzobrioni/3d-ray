using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Constructive Solid Geometry (CSG) — Boolean operations on solid primitives.
///
/// Supports three operations:
///   • Union        — A ∪ B  (combined volume of both solids)
///   • Intersection — A ∩ B  (only the overlapping volume)
///   • Subtraction  — A \ B  (A with B carved out)
///
/// <b>Algorithm (PIANO-REVIEW.md Phase 2):</b> the crossings of the ray with
/// each child are collected over the range asked, each with its FrontFace
/// (entering or leaving that child). They are merged in order of t, and
/// crossings of either child within a small tolerance of each other are one
/// event; after an event each child's state is the FrontFace of its last
/// crossing in it. The result is inside where op(inside A, inside B) holds,
/// and has a surface exactly where that changes. Coincident faces follow:
/// the face two touching solids share inside a union is not a surface, nor
/// the mouth of a pocket cut flush with a face, and A \ A is empty.
///
/// <b>Records:</b> the surface is the record of a child whose own crossing
/// goes the way the result's does (A preferred when both). For A \ B a
/// crossing of B bounds the result the other way round, so its FrontFace
/// flips; rec.Normal, the shading normal, stays against the ray. Each child's
/// record (material, UV, TBN) is otherwise kept as is. A CsgObject is itself
/// a solid, so trees nest.
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
    /// Crossings of the two children closer than this are one event: two
    /// coincident faces (a box flush with another's face, two boxes touching)
    /// are found by different primitives a few ulps apart. The tolerance is a
    /// distance across the surface (relative to the hit point's coordinates,
    /// with a floor that scales with the scene, <see cref="MathUtils.RayEpsilon"/>),
    /// so along the ray it grows as 1/|cos θ|: at grazing incidence the
    /// distances of two coincident faces differ by their position error
    /// divided by the cosine. Features thinner than that are merged.
    /// </summary>
    private const float GroupRel = 1f / 131072f; // 2^-17

    private const float StepRel = 1f / 1048576f; // 2^-20

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GroupTolerance(float t) => MathF.Max(8f * MathUtils.RayEpsilon, MathF.Abs(t) * GroupRel);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GroupTolerance(in Ray ray, in HitRecord head)
    {
        Vector3 p = Vector3.Abs(head.Point);
        float across = MathF.Max(4f * MathUtils.RayEpsilon, MathF.Max(p.X, MathF.Max(p.Y, p.Z)) * GroupRel);
        float cos = MathF.Abs(Vector3.Dot(head.Normal, ray.Direction)) / ray.Direction.Length();
        return MathF.Max(GroupTolerance(head.T), across / MathF.Max(cos, 1e-3f));
    }

    /// <summary>
    /// Walks the crossings of a ray with one CSG child in ascending t, one
    /// query at a time: <see cref="Current"/> is the next crossing not yet
    /// consumed, <see cref="Last"/> the last one consumed. Two record slots
    /// swap roles, so consuming a crossing copies nothing. FrontFace of a
    /// record says whether the ray enters (true) or leaves the child there,
    /// which is the child's state after the crossing.
    /// </summary>
    private struct Cursor
    {
        private HitRecord _slot0, _slot1;
        private bool _currentIs1;
        private float _next;
        private int _count;
        public bool Has;
        public bool StartsInside;

        [UnscopedRef] public ref HitRecord Current => ref (_currentIs1 ? ref _slot1 : ref _slot0);
        [UnscopedRef] public ref HitRecord Last => ref (_currentIs1 ? ref _slot0 : ref _slot1);

        /// <summary>
        /// The first crossing over [tMin, tEnd] and the state at tMin: from
        /// that crossing (leaving means the ray started inside), or with none
        /// in the range from one more query past it.
        /// </summary>
        public void Start(IHittable child, in Ray ray, float tMin, float tEnd)
        {
            _next = tMin;
            Advance(child, ray, tEnd);
            if (Has) StartsInside = !Current.FrontFace;
            else if (float.IsFinite(tEnd))
            {
                var probe = new HitRecord();
                if (child.Hit(ray, tEnd, float.PositiveInfinity, ref probe)) StartsInside = !probe.FrontFace;
            }
        }

        /// <summary>Consumes the current crossing and queries the next.</summary>
        public void Consume(IHittable child, in Ray ray, float tEnd)
        {
            _currentIs1 = !_currentIs1; // the current record becomes Last
            Advance(child, ray, tEnd);
        }

        private void Advance(IHittable child, in Ray ray, float tEnd)
        {
            ref HitRecord rec = ref Current;
            rec = default;
            Has = _count < MaxHitsPerChild && child.Hit(ray, _next, tEnd, ref rec);
            if (!Has) return;
            _count++;
            // Just past the crossing (about 8 ulps of t): a longer step skips
            // the exit of a ray that clips an edge, and the child would stay
            // "inside" for the rest of the ray.
            _next = rec.T + MathF.Max(MathUtils.RayEpsilon, MathF.Abs(rec.T) * StepRel);
        }
    }

    // =========================================================================
    //  Core intersection — IHittable.Hit()
    // =========================================================================

    /// <summary>
    /// The first boundary of the Boolean result in [tMin, tMax]. The crossings
    /// of both children are merged in order of t; crossings within
    /// <see cref="GroupTolerance"/> of each other are one event, after which
    /// each child's state is the FrontFace of its last crossing there (not a
    /// parity count, so a crossing found twice does not invert the rest of the
    /// ray). The result has a surface where its state changes, and only there:
    /// the face two touching solids share inside a union, the mouth of a
    /// pocket cut flush with a face, A \ A, all have none.
    ///
    /// <para>The crossings are queried lazily, only up to the event that
    /// decides the result (PIANO-REVIEW.md Phase 11): the queries made and
    /// their order are those of collecting every crossing first, so the
    /// result is the same, at a fraction of the cost on a long ray.</para>
    /// </summary>
    [SkipLocalsInit]
    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // _isEmpty must be checked first: AABB.Hit() returns true for
        // AABB.Empty (the inverted sentinels satisfy the slab test).
        if (_isEmpty || !_boundingBox.Hit(ray, tMin, tMax))
            return false;

        // Crossings a little past tMax still group with one just before it.
        float tEnd = float.IsFinite(tMax) ? tMax + GroupTolerance(tMax) : float.PositiveInfinity;
        var a = new Cursor();
        var b = new Cursor();
        a.Start(Left, ray, tMin, tEnd);
        b.Start(Right, ray, tMin, tEnd);
        if (!a.Has && !b.Has) return false;

        bool inA = a.StartsInside, inB = b.StartsInside;
        bool inside = Combine(inA, inB);
        while (a.Has || b.Has)
        {
            bool fromA = a.Has && (!b.Has || a.Current.T <= b.Current.T);
            ref readonly HitRecord head = ref (fromA ? ref a.Current : ref b.Current);
            float t = head.T;
            if (t > tMax) return false;
            float limit = t + GroupTolerance(ray, head);

            bool tookA = false, tookB = false;
            while (a.Has && a.Current.T <= limit) { inA = a.Current.FrontFace; tookA = true; a.Consume(Left, ray, tEnd); }
            while (b.Has && b.Current.T <= limit) { inB = b.Current.FrontFace; tookB = true; b.Consume(Right, ray, tEnd); }

            bool now = Combine(inA, inB);
            if (now == inside) continue;

            // The record of a child whose crossing goes the way the result
            // does: entering A (or B in a union or intersection) where the
            // result is entered; for A \ B, leaving B is entering the result.
            bool flipB = Operation == CsgOperation.Subtraction;
            bool aFits = tookA && a.Last.FrontFace == now;
            bool bFits = tookB && (b.Last.FrontFace != flipB) == now;
            if (aFits || (!bFits && tookA)) rec = a.Last;
            else
            {
                // B's surface bounds the result. Only FrontFace changes for
                // a subtraction: rec.Normal is the shading normal, always
                // against the ray, whichever solid owns the face.
                rec = b.Last;
            }
            rec.FrontFace = now;
            return rec.T >= tMin && rec.T <= tMax;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Combine(bool inA, bool inB) => Operation switch
    {
        CsgOperation.Union => inA || inB,
        CsgOperation.Intersection => inA && inB,
        _ => inA && !inB,
    };

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
