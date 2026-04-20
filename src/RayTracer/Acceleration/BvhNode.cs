using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Acceleration;

/// <summary>
/// Bounding Volume Hierarchy node for O(log N) ray intersection.
///
/// <b>Build heuristic — binned Surface-Area Heuristic (SAH)</b>, in the style
/// of PBRT §4.3. For every internal split the algorithm:
/// <list type="number">
///   <item><description>Computes the centroid bounds of the primitives in the range.</description></item>
///   <item><description>Buckets each primitive into one of <see cref="NumBins"/>
///   equal-width bins along each candidate axis, accumulating a per-bin AABB
///   and primitive count.</description></item>
///   <item><description>Uses prefix/suffix sums over the bins to evaluate
///   every between-bin split in O(<see cref="NumBins"/>) and picks the
///   minimum-cost split across all three axes, weighted by
///   <c>N·SurfaceArea</c> for each side.</description></item>
///   <item><description>Partitions the range in place around the chosen bin
///   boundary (O(N) — no sort needed).</description></item>
/// </list>
/// Replaces the previous longest-axis object-median heuristic. A degenerate
/// range where all centroids coincide falls back to a simple median split so
/// recursion still terminates.
///
/// <b>Structural tricks on top of SAH:</b>
/// <list type="bullet">
///   <item><description><b>Fat leaves</b>: ranges of up to <see cref="MaxPrimitivesPerLeaf"/>
///   primitives are stored in a flat array and tested linearly. This bounds
///   tree depth and reduces AABB-test pressure on clustered scenes.</description></item>
///   <item><description><b>Ordered traversal</b>: each internal node remembers
///   its split axis. At hit time the child on the near side of the ray along
///   that axis is tested first, so <c>tMax</c> is tightened before the far
///   child is considered.</description></item>
///   <item><description><b>Parallel build</b>: when the range is larger than
///   <see cref="ParallelBuildSpanThreshold"/> the two subtrees are built on
///   independent threads via <see cref="Parallel.Invoke(Action[])"/>. The
///   subtree ranges are disjoint and sorted/partitioned in place, so no
///   locking is required.</description></item>
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

    /// <summary>
    /// Number of SAH bins per axis. 16 is the PBRT default and gives a good
    /// trade-off between evaluation cost (O(bins) prefix sums) and split
    /// quality; values above 32 rarely pay back on practical scenes.
    /// </summary>
    private const int NumBins = 16;

    /// <summary>
    /// Relative cost of a BVH node traversal vs a primitive intersection.
    /// 0.5 is a common choice; the SAH cost model compares
    /// <c>TraversalCost + (N_L·A_L + N_R·A_R)/A_parent</c> against
    /// <c>N·IntersectionCost</c> (implicitly 1).
    /// </summary>
    private const float TraversalCost = 0.5f;

    /// <summary>
    /// Minimum subtree size for spawning a <see cref="Parallel.Invoke"/>
    /// split during construction. Below this threshold the recursion runs on
    /// the calling thread so the work-item is bigger than the scheduling
    /// overhead. 8192 keeps per-task work comfortably above the
    /// <see cref="Parallel"/> overhead on all target platforms.
    /// </summary>
    private const int ParallelBuildSpanThreshold = 8192;

    public int Seed { get; set; }

    // Internal-node children (both non-null) OR fat-leaf payload (_primitives
    // non-null, children null). Exactly one of the two representations is
    // active per instance; _splitAxis is only meaningful for internal nodes.
    private readonly BvhNode? _left;
    private readonly BvhNode? _right;
    private readonly IHittable[]? _primitives;

    private readonly AABB _box;
    private readonly int _splitAxis;

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

        // Internal node: try an SAH split; fall back to a median split if all
        // centroids coincide. Partitioning is O(N) in place.
        int bestAxis;
        int mid;
        if (!TryFindSAHSplit(objects, start, end, out bestAxis, out mid))
        {
            // Degenerate: all centroids equal on every axis. Halve the range
            // by count so the recursion still terminates.
            bestAxis = 0;
            mid = start + span / 2;
        }

        _splitAxis = bestAxis;

        // Build the two subtrees. For large sub-ranges spawn both builds on
        // the thread pool — the sub-ranges are disjoint so in-place partitioning
        // inside each subtree is safe without locks.
        BvhNode left, right;
        if (span > ParallelBuildSpanThreshold)
        {
            int midCopy = mid;
            BvhNode? tempLeft = null;
            BvhNode? tempRight = null;
            Parallel.Invoke(
                () => tempLeft = new BvhNode(objects, start, midCopy),
                () => tempRight = new BvhNode(objects, midCopy, end));
            left = tempLeft!;
            right = tempRight!;
        }
        else
        {
            left = new BvhNode(objects, start, mid);
            right = new BvhNode(objects, mid, end);
        }

        _left = left;
        _right = right;
        _box = AABB.SurroundingBox(left._box, right._box);
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

        // Internal node: test the near child first. Because the SAH partition
        // placed lower-bin primitives in [start, mid) and higher-bin primitives
        // in [mid, end), _left always sits on the low side of _splitAxis — so
        // a positive ray component on that axis means _left is closer to the
        // ray origin. Visiting the near child first gives a tighter tMax for
        // the far child and maximises early-reject pruning.
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

    // ───────────────────────── SAH binning ──────────────────────────────

    /// <summary>
    /// Finds the best SAH split across all three axes using bin aggregation,
    /// then partitions the range in place so [start, mid) holds the primitives
    /// whose centroid falls in the low-side bins. Returns false when every
    /// axis has zero centroid extent — the caller must pick a median fallback.
    /// </summary>
    private static bool TryFindSAHSplit(
        List<IHittable> objects, int start, int end,
        out int bestAxis, out int mid)
    {
        int span = end - start;

        // Centroid bounds across the range.
        Vector3 centroidMin = new(float.MaxValue);
        Vector3 centroidMax = new(float.MinValue);
        for (int i = start; i < end; i++)
        {
            var b = objects[i].BoundingBox();
            Vector3 c = (b.Min + b.Max) * 0.5f;
            centroidMin = Vector3.Min(centroidMin, c);
            centroidMax = Vector3.Max(centroidMax, c);
        }

        Vector3 centroidExtent = centroidMax - centroidMin;
        float maxExtent = MathF.Max(centroidExtent.X, MathF.Max(centroidExtent.Y, centroidExtent.Z));
        if (maxExtent <= 0f)
        {
            bestAxis = -1;
            mid = start;
            return false;
        }

        bestAxis = -1;
        int bestBinSplit = -1;
        float bestCost = float.MaxValue;

        Span<int> binCounts = stackalloc int[NumBins];
        Span<AABB> binBounds = stackalloc AABB[NumBins];
        Span<int> prefixCount = stackalloc int[NumBins];
        Span<AABB> prefixBounds = stackalloc AABB[NumBins];
        Span<int> suffixCount = stackalloc int[NumBins];
        Span<AABB> suffixBounds = stackalloc AABB[NumBins];

        for (int axis = 0; axis < 3; axis++)
        {
            float extentAxis = GetAxis(centroidExtent, axis);
            if (extentAxis <= 0f) continue; // all centroids coincide on this axis

            float cmin = GetAxis(centroidMin, axis);
            // Map centroid position to bin index: floor((c - cmin) / extent · NumBins).
            // Guard the high edge so cmax lands in the last bin.
            float scale = NumBins / extentAxis;

            for (int i = 0; i < NumBins; i++)
            {
                binCounts[i] = 0;
                binBounds[i] = AABB.Empty;
            }

            for (int i = start; i < end; i++)
            {
                var b = objects[i].BoundingBox();
                float c = GetAxis((b.Min + b.Max) * 0.5f, axis);
                int idx = (int)((c - cmin) * scale);
                if (idx < 0) idx = 0;
                else if (idx >= NumBins) idx = NumBins - 1;

                binCounts[idx]++;
                binBounds[idx] = AABB.SurroundingBox(binBounds[idx], b);
            }

            // Prefix sums (left side of each candidate split).
            int cum = 0;
            AABB cumBox = AABB.Empty;
            for (int i = 0; i < NumBins; i++)
            {
                cum += binCounts[i];
                cumBox = AABB.SurroundingBox(cumBox, binBounds[i]);
                prefixCount[i] = cum;
                prefixBounds[i] = cumBox;
            }

            // Suffix sums (right side of each candidate split).
            cum = 0;
            cumBox = AABB.Empty;
            for (int i = NumBins - 1; i >= 0; i--)
            {
                cum += binCounts[i];
                cumBox = AABB.SurroundingBox(cumBox, binBounds[i]);
                suffixCount[i] = cum;
                suffixBounds[i] = cumBox;
            }

            float parentArea = prefixBounds[NumBins - 1].SurfaceArea();
            if (parentArea <= 0f) continue; // fully degenerate on this axis

            // Evaluate the NumBins-1 between-bin splits.
            for (int i = 0; i < NumBins - 1; i++)
            {
                int nL = prefixCount[i];
                int nR = suffixCount[i + 1];
                if (nL == 0 || nR == 0) continue;

                float cost = TraversalCost +
                    (nL * prefixBounds[i].SurfaceArea() +
                     nR * suffixBounds[i + 1].SurfaceArea()) / parentArea;

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestBinSplit = i;
                }
            }
        }

        if (bestAxis < 0)
        {
            mid = start;
            return false;
        }

        // Partition the range around the chosen bin boundary on the best axis.
        float axisMin = GetAxis(centroidMin, bestAxis);
        float axisScale = NumBins / GetAxis(centroidExtent, bestAxis);
        int writePos = start;
        for (int i = start; i < end; i++)
        {
            var b = objects[i].BoundingBox();
            float c = GetAxis((b.Min + b.Max) * 0.5f, bestAxis);
            int idx = (int)((c - axisMin) * axisScale);
            if (idx < 0) idx = 0;
            else if (idx >= NumBins) idx = NumBins - 1;

            if (idx <= bestBinSplit)
            {
                (objects[i], objects[writePos]) = (objects[writePos], objects[i]);
                writePos++;
            }
        }

        mid = writePos;

        // Defensive: if every primitive landed on one side (can happen when the
        // minimum-cost split is heavily imbalanced and the other side was
        // discarded for nL==0 || nR==0), fall back to the median split so we
        // still make progress.
        if (mid == start || mid == end)
            mid = start + span / 2;

        return true;
    }

    private static float GetAxis(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z
    };
}
