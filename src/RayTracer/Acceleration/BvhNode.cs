using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Acceleration;

/// <summary>
/// Bounding Volume Hierarchy node for O(log N) ray intersection.
/// Build heuristic: longest-axis object-median split — at each level the axis
/// with the largest extent across object centroids is chosen, the objects in
/// the range are sorted along that axis, and the range is split at the median
/// index. This is *not* a Surface Area Heuristic (SAH); it is the simpler
/// object-count median split popularised by "Ray Tracing in One Weekend".
///
/// The implementation uses two structural tricks on top of the baseline:
/// <list type="bullet">
///   <item><description><b>Fat leaves</b>: ranges of up to <see cref="MaxPrimitivesPerLeaf"/>
///   primitives are stored in a flat array and tested linearly, instead of
///   forcing the tree to a depth of log₂(N). This reduces AABB-test overhead
///   and improves cache locality for small clusters.</description></item>
///   <item><description><b>Ordered traversal</b>: each internal node remembers
///   the split axis used during construction. At hit time the child whose
///   sub-range sits on the near side of the ray (along that axis) is tested
///   first, so <c>tMax</c> is tightened earlier and the far sibling is more
///   often culled on its root AABB.</description></item>
/// </list>
/// </summary>
public class BvhNode : IHittable
{
    /// <summary>
    /// Upper bound on primitives kept in a flat fat leaf. Chosen empirically:
    /// small enough that a linear scan fits in a cache line or two and the
    /// extra BVH internal-node memory is negligible, large enough to materially
    /// reduce tree depth and AABB-test pressure on clustered scenes.
    /// </summary>
    public const int MaxPrimitivesPerLeaf = 4;

    public int Seed { get; set; }

    // Internal-node children (both non-null) OR fat-leaf payload (_primitives
    // non-null, children null). Exactly one of the two representations is
    // active per instance; _splitAxis is only meaningful for internal nodes.
    private readonly BvhNode? _left;
    private readonly BvhNode? _right;
    private readonly IHittable[]? _primitives;

    private readonly AABB _box;
    private readonly int _splitAxis;

    // OPT-06: pre-allocated static comparers — zero allocations during BVH construction.
    // Previously Comparer<T>.Create(comparator) was called at every recursive node,
    // causing O(N) heap allocations. One delegate per axis, created once at startup.
    private static readonly Comparer<IHittable> _compareX =
        Comparer<IHittable>.Create((a, b) => CompareAxis(a, b, 0));
    private static readonly Comparer<IHittable> _compareY =
        Comparer<IHittable>.Create((a, b) => CompareAxis(a, b, 1));
    private static readonly Comparer<IHittable> _compareZ =
        Comparer<IHittable>.Create((a, b) => CompareAxis(a, b, 2));

    public BvhNode(List<IHittable> objects, int start, int end)
    {
        int span = end - start;

        if (span <= MaxPrimitivesPerLeaf)
        {
            // Fat leaf: copy the range into a private array so the caller can
            // safely mutate the source list afterwards. The bounding box is the
            // union of the primitive AABBs.
            var prims = new IHittable[span];
            AABB box = objects[start].BoundingBox();
            prims[0] = objects[start];
            for (int i = 1; i < span; i++)
            {
                prims[i] = objects[start + i];
                box = AABB.SurroundingBox(box, prims[i].BoundingBox());
            }
            _primitives = prims;
            _box = box;
            _splitAxis = 0;
            return;
        }

        // Internal node: pick the longest centroid-extent axis, sort by it,
        // split at the median index. Children are BvhNodes themselves (which
        // may be fat leaves if small enough).
        int axis = ComputeLongestAxis(objects, start, end);
        var comparer = axis switch { 0 => _compareX, 1 => _compareY, _ => _compareZ };
        objects.Sort(start, span, comparer);
        int mid = start + span / 2;
        _left = new BvhNode(objects, start, mid);
        _right = new BvhNode(objects, mid, end);
        _splitAxis = axis;
        _box = AABB.SurroundingBox(_left._box, _right._box);
    }


    public BvhNode(List<IHittable> objects) : this(objects, 0, objects.Count) { }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (!_box.Hit(ray, tMin, tMax))
            return false;

        // Fat leaf: linear scan, tightening tMax on each successful hit.
        if (_primitives is not null)
        {
            bool hit = false;
            float closest = tMax;
            var prims = _primitives;
            for (int i = 0; i < prims.Length; i++)
            {
                if (prims[i].Hit(ray, tMin, closest, ref rec))
                {
                    hit = true;
                    closest = rec.T;
                }
            }
            return hit;
        }

        // Internal node: test the near child first. Because children were
        // sorted by _splitAxis at build time, _left holds the lower-coordinate
        // range — so a positive ray component on that axis means _left is
        // closer to the ray origin. Visiting the near child first gives a
        // tighter tMax for the far child and maximises early-reject pruning.
        float dirOnAxis = _splitAxis switch
        {
            0 => ray.Direction.X,
            1 => ray.Direction.Y,
            _ => ray.Direction.Z
        };

        BvhNode near, far;
        if (dirOnAxis >= 0f)
        {
            near = _left!;
            far = _right!;
        }
        else
        {
            near = _right!;
            far = _left!;
        }

        bool hitNear = near.Hit(ray, tMin, tMax, ref rec);
        bool hitFar = far.Hit(ray, tMin, hitNear ? rec.T : tMax, ref rec);
        return hitNear || hitFar;
    }

    public AABB BoundingBox() => _box;

    private static int CompareAxis(IHittable a, IHittable b, int axis)
    {
        var boxA = a.BoundingBox();
        var boxB = b.BoundingBox();
        float va = axis switch { 0 => boxA.Min.X, 1 => boxA.Min.Y, _ => boxA.Min.Z };
        float vb = axis switch { 0 => boxB.Min.X, 1 => boxB.Min.Y, _ => boxB.Min.Z };
        return va.CompareTo(vb);
    }

    /// <summary>
    /// Selects the axis (0=X, 1=Y, 2=Z) with the longest extent across the
    /// centroids of the objects in [start, end), for optimal BVH splitting.
    /// </summary>
    private static int ComputeLongestAxis(List<IHittable> objects, int start, int end)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (int i = start; i < end; i++)
        {
            var box = objects[i].BoundingBox();
            // Use centroid of each AABB
            float cx = (box.Min.X + box.Max.X) * 0.5f;
            float cy = (box.Min.Y + box.Max.Y) * 0.5f;
            float cz = (box.Min.Z + box.Max.Z) * 0.5f;

            if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
            if (cz < minZ) minZ = cz; if (cz > maxZ) maxZ = cz;
        }

        float extX = maxX - minX;
        float extY = maxY - minY;
        float extZ = maxZ - minZ;

        if (extX >= extY && extX >= extZ) return 0;
        if (extY >= extZ) return 1;
        return 2;
    }
}
