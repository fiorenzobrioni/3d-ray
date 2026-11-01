using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracer.Core;

namespace RayTracer.Acceleration;

/// <summary>
/// A node of a flattened BVH: 32 bytes, two to a cache line. The two
/// children of an internal node are adjacent (<see cref="Offset"/> and
/// <see cref="Offset"/> + 1), so a traversal tests both boxes from one line
/// and visits the nearer first. A leaf holds <see cref="Count"/> primitives
/// from index <see cref="Offset"/> of the BVH's primitive order.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BvhFlatNode
{
    public Vector3 Min;
    public int Offset;
    public Vector3 Max;
    public int Count; // 0 for an internal node

    public readonly bool IsLeaf => Count > 0;
}

/// <summary>
/// Builds a flattened BVH over a set of boxes with the binned surface-area
/// heuristic (PBRT §4.3): per split, the primitives' centroids are bucketed
/// into <see cref="NumBins"/> bins on each axis and the cheapest of the
/// between-bin splits is taken, with cost
/// <c>TraversalCost + (N_L·A_L + N_R·A_R)/A</c>. A range becomes a leaf when
/// it holds at most <see cref="MaxLeafSize"/> primitives, or when no split
/// is cheaper than testing them all (up to <see cref="MaxSahLeafSize"/>).
/// Large ranges build their two halves in parallel; children are allocated in
/// pairs from a shared counter, so the result is a flat array with no
/// pointers and the input is never modified.
/// </summary>
public static class BvhBuilder
{
    /// <summary>A range this small is always a leaf.</summary>
    public const int MaxLeafSize = 4;

    /// <summary>Largest leaf the SAH may choose over splitting.</summary>
    public const int MaxSahLeafSize = 8;

    /// <summary>SAH bins per axis (PBRT's 16: within 1% of a full sweep, Phase 2).</summary>
    public const int NumBins = 16;

    /// <summary>Cost of a node visit relative to a primitive test.</summary>
    public const float TraversalCost = 0.5f;

    private const int ParallelThreshold = 8192;

    /// <summary>
    /// Depth after which ranges are halved by count: a traversal stack of
    /// <see cref="MaxStack"/> entries then always suffices (one entry per level).
    /// </summary>
    private const int MaxSahDepth = 64;

    /// <summary>Traversal stack size (depth ≤ 64 + log2 of 2³¹).</summary>
    public const int MaxStack = 128;

    /// <summary>
    /// Builds the tree over <paramref name="boxes"/>. Returns the nodes (root
    /// at 0) and the primitive order: leaf ranges index into
    /// <c>order</c>, which maps to the input indices.
    /// </summary>
    public static (BvhFlatNode[] Nodes, int[] Order) Build(ReadOnlySpan<AABB> boxes)
    {
        int n = boxes.Length;
        if (n == 0) return ([new BvhFlatNode { Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue), Count = 0, Offset = -1 }], []);
        var state = new State(boxes.ToArray());
        state.Build(0, 0, n, 0, parallel: n > ParallelThreshold);
        var nodes = state.Nodes;
        Array.Resize(ref nodes, state.NodeCount);
        return (nodes, state.Order);
    }

    /// <summary>
    /// The tree's SAH cost: the expected work of a ray that hits the root box,
    /// each node weighted by the chance a ray through the root also crosses its
    /// box (area ratio): <see cref="TraversalCost"/> per internal node, one per
    /// primitive of a leaf. A machine-independent measure of the build.
    /// </summary>
    public static double SahCost(BvhFlatNode[] nodes)
    {
        double root = Area(nodes[0].Min, nodes[0].Max);
        if (!(root > 0)) return double.NaN;
        double cost = 0;
        for (int i = 0; i < nodes.Length; i++)
        {
            double p = Area(nodes[i].Min, nodes[i].Max) / root;
            cost += nodes[i].IsLeaf ? p * nodes[i].Count : p * TraversalCost;
        }
        return cost;
    }

    private static double Area(Vector3 min, Vector3 max)
    {
        Vector3 d = max - min;
        if (d.X < 0 || d.Y < 0 || d.Z < 0) return 0;
        return 2.0 * ((double)d.X * d.Y + (double)d.X * d.Z + (double)d.Y * d.Z);
    }

    private sealed class State
    {
        public readonly AABB[] Boxes;
        public readonly Vector3[] Centroids;
        public readonly int[] Order;
        public BvhFlatNode[] Nodes;
        private int _next = 1;

        public State(AABB[] boxes)
        {
            Boxes = boxes;
            int n = boxes.Length;
            Centroids = new Vector3[n];
            Order = new int[n];
            for (int i = 0; i < n; i++)
            {
                Centroids[i] = (boxes[i].Min + boxes[i].Max) * 0.5f;
                Order[i] = i;
            }
            Nodes = new BvhFlatNode[Math.Max(1, 2 * n - 1)];
        }

        public int NodeCount => _next;

        public void Build(int nodeIndex, int start, int end, int depth, bool parallel)
        {
            int span = end - start;
            Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
            Vector3 cmin = new(float.MaxValue), cmax = new(float.MinValue);
            for (int i = start; i < end; i++)
            {
                int p = Order[i];
                bmin = Vector3.Min(bmin, Boxes[p].Min);
                bmax = Vector3.Max(bmax, Boxes[p].Max);
                cmin = Vector3.Min(cmin, Centroids[p]);
                cmax = Vector3.Max(cmax, Centroids[p]);
            }

            int mid = -1;
            if (span > MaxLeafSize)
                mid = depth < MaxSahDepth ? Split(start, end, bmin, bmax, cmin, cmax) : start + span / 2;
            if (mid < 0)
            {
                Nodes[nodeIndex] = new BvhFlatNode { Min = bmin, Max = bmax, Offset = start, Count = span };
                return;
            }

            int pair = Interlocked.Add(ref _next, 2) - 2;
            Nodes[nodeIndex] = new BvhFlatNode { Min = bmin, Max = bmax, Offset = pair, Count = 0 };
            if (parallel && span > ParallelThreshold)
                Parallel.Invoke(
                    () => Build(pair, start, mid, depth + 1, true),
                    () => Build(pair + 1, mid, end, depth + 1, true));
            else
            {
                Build(pair, start, mid, depth + 1, false);
                Build(pair + 1, mid, end, depth + 1, false);
            }
        }

        /// <summary>
        /// Partitions [start, end) around the cheapest SAH split and returns
        /// its position, or −1 when the range should be a leaf.
        /// </summary>
        private int Split(int start, int end, Vector3 bmin, Vector3 bmax, Vector3 cmin, Vector3 cmax)
        {
            int span = end - start;
            Vector3 extent = cmax - cmin;
            float parentArea = new AABB(bmin, bmax).SurfaceArea();

            int bestAxis = -1, bestBin = -1;
            float bestCost = float.MaxValue;
            if (extent.X > 0f || extent.Y > 0f || extent.Z > 0f)
            {
                Span<int> counts = stackalloc int[NumBins];
                Span<AABB> bounds = stackalloc AABB[NumBins];
                Span<float> leftArea = stackalloc float[NumBins];
                Span<int> leftCount = stackalloc int[NumBins];
                for (int axis = 0; axis < 3; axis++)
                {
                    float ext = Comp(extent, axis);
                    if (ext <= 0f) continue;
                    float cbase = Comp(cmin, axis), scale = NumBins / ext;
                    counts.Clear();
                    bounds.Fill(AABB.Empty);
                    for (int i = start; i < end; i++)
                    {
                        int p = Order[i];
                        int b = Bin(Comp(Centroids[p], axis), cbase, scale);
                        counts[b]++;
                        bounds[b] = AABB.SurroundingBox(bounds[b], Boxes[p]);
                    }
                    AABB acc = AABB.Empty;
                    int cnt = 0;
                    for (int i = 0; i < NumBins; i++)
                    {
                        acc = AABB.SurroundingBox(acc, bounds[i]);
                        cnt += counts[i];
                        leftArea[i] = acc.SurfaceArea();
                        leftCount[i] = cnt;
                    }
                    acc = AABB.Empty;
                    cnt = 0;
                    for (int i = NumBins - 1; i > 0; i--)
                    {
                        acc = AABB.SurroundingBox(acc, bounds[i]);
                        cnt += counts[i];
                        int nl = leftCount[i - 1];
                        if (nl == 0 || cnt == 0) continue;
                        float cost = TraversalCost + (nl * leftArea[i - 1] + cnt * acc.SurfaceArea())
                                     / MathF.Max(parentArea, float.Epsilon);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestAxis = axis;
                            bestBin = i - 1;
                        }
                    }
                }
            }

            if (bestAxis < 0)
            {
                // Every centroid in one point: no split separates them. Halve
                // the range by count so the recursion still terminates.
                return span > MaxLeafSize ? start + span / 2 : -1;
            }
            // A leaf when the split does not pay: testing every primitive costs span.
            if (span <= MaxSahLeafSize && bestCost >= span) return -1;

            float baseB = Comp(cmin, bestAxis), scaleB = NumBins / Comp(extent, bestAxis);
            int write = start;
            for (int i = start; i < end; i++)
            {
                int p = Order[i];
                if (Bin(Comp(Centroids[p], bestAxis), baseB, scaleB) <= bestBin)
                {
                    (Order[i], Order[write]) = (Order[write], Order[i]);
                    write++;
                }
            }
            return write == start || write == end ? start + span / 2 : write;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Bin(float c, float cbase, float scale)
        {
            int b = (int)((c - cbase) * scale);
            return b < 0 ? 0 : b >= NumBins ? NumBins - 1 : b;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Comp(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}

/// <summary>
/// The ray quantities a BVH traversal reuses at every node, and the
/// conservative slab test of <see cref="AABB.Hit"/> on a flat node.
/// </summary>
internal readonly struct BvhRay
{
    public readonly Vector3 Origin;
    public readonly Vector3 InvDirection;

    // 1 + 2γ₃ (see AABB.Hit).
    private const float ExitWidening = 1f + 2f * (3f * 5.96046448e-8f / (1f - 3f * 5.96046448e-8f));

    public BvhRay(in Ray ray)
    {
        Origin = ray.Origin;
        InvDirection = ray.InvDirection;
    }

    /// <summary>
    /// Slab test of the node's box over [tMin, tMax], exactly as
    /// <see cref="AABB.Hit"/> (NaN slabs ignored, exit widened by 1 + 2γ₃);
    /// <paramref name="tEnter"/> orders the visit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Hit(in BvhFlatNode node, float tMin, float tMax, out float tEnter)
    {
        Vector3 t0 = (node.Min - Origin) * InvDirection;
        Vector3 t1 = (node.Max - Origin) * InvDirection;
        Vector3 tSmall = Vector3.Min(t0, t1);
        Vector3 tLarge = Vector3.Max(t0, t1);
        tEnter = float.MaxNumber(tMin, float.MaxNumber(tSmall.X, float.MaxNumber(tSmall.Y, tSmall.Z)));
        float tExit = float.MinNumber(tMax, float.MinNumber(tLarge.X, float.MinNumber(tLarge.Y, tLarge.Z)));
        return tExit * ExitWidening >= tEnter;
    }

    /// <summary>True when a node entered at <paramref name="tEnter"/> can still
    /// hold a hit closer than <paramref name="tMax"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool StillOpen(float tEnter, float tMax) => tMax * ExitWidening >= tEnter;
}
