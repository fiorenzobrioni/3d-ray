using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Acceleration;

/// <summary>
/// BVH builder for triangles with spatial splits (SBVH, Stich, Friedrich and
/// Dietrich 2009). Where the two halves of the best object split overlap, a
/// split by a plane is also tried: a triangle that straddles the plane goes to
/// both sides, each with the box of its own part (the triangle clipped to that
/// side). Long, thin or diagonal triangles, whose boxes overlap most, stop
/// dragging their neighbours' rays through every node. The extra references
/// are capped at <see cref="DuplicateBudget"/> of the triangle count, and the
/// splits are only tried where the overlap is more than <see cref="Alpha"/> of
/// the root area. Same cost model and leaf rule as <see cref="BvhBuilder"/>.
/// </summary>
public static class SpatialSplitBuilder
{
    /// <summary>Extra references allowed, as a share of the triangles.</summary>
    public const float DuplicateBudget = 0.3f;

    /// <summary>Overlap (over the root area) above which spatial splits are tried.</summary>
    public const float Alpha = 1e-5f;

    private const int NumBins = 16;
    private const int ParallelThreshold = 8192;
    private const int MaxDepth = 64;

    private struct Ref
    {
        public int Prim;
        public AABB Box;
    }

    /// <summary>
    /// Builds the tree over the triangles (<paramref name="vertices"/>: three
    /// per triangle), each box grown by <paramref name="padding"/>. Returns the
    /// nodes and, per leaf slot, the triangle it holds (a triangle can appear
    /// in more than one leaf).
    /// </summary>
    public static (BvhFlatNode[] Nodes, int[] Order) Build(Vector3[] vertices, float padding = 0f)
    {
        int n = vertices.Length / 3;
        var refs = new Ref[n];
        var pad = new Vector3(MathF.Max(0f, padding));
        for (int i = 0; i < n; i++)
        {
            Vector3 a = vertices[3 * i], b = vertices[3 * i + 1], c = vertices[3 * i + 2];
            refs[i] = new Ref { Prim = i, Box = new AABB(Vector3.Min(Vector3.Min(a, b), c) - pad, Vector3.Max(Vector3.Max(a, b), c) + pad) };
        }
        if (n == 0) return BvhBuilder.Build(ReadOnlySpan<AABB>.Empty);

        var state = new State(vertices, pad, n);
        AABB root = Bounds(refs);
        state.RootArea = root.SurfaceArea();
        state.Build(0, refs, 0, parallel: n > ParallelThreshold);

        // Leaves hold their references in their own arrays: lay them out in
        // node order.
        var nodes = state.Nodes;
        Array.Resize(ref nodes, state.NodeCount);
        var order = new List<int>(n + state.Duplicates);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!nodes[i].IsLeaf) continue;
            var prims = state.LeafPrims[i]!;
            nodes[i].Offset = order.Count;
            order.AddRange(prims);
        }
        return (nodes, order.ToArray());
    }

    private static AABB Bounds(ReadOnlySpan<Ref> refs)
    {
        AABB b = AABB.Empty;
        foreach (var r in refs) b = AABB.SurroundingBox(b, r.Box);
        return b;
    }

    private sealed class State(Vector3[] vertices, Vector3 pad, int triangles)
    {
        public BvhFlatNode[] Nodes = new BvhFlatNode[2 * (triangles + (int)(triangles * DuplicateBudget) + 1)];
        public int[]?[] LeafPrims = new int[2 * (triangles + (int)(triangles * DuplicateBudget) + 1)][];
        public float RootArea;
        private int _next = 1;
        private int _duplicates;
        private readonly int _budget = (int)(triangles * DuplicateBudget);

        public int NodeCount => _next;
        public int Duplicates => _duplicates;

        public void Build(int nodeIndex, Ref[] refs, int depth, bool parallel)
        {
            AABB box = Bounds(refs);
            int count = refs.Length;
            Ref[]? left = null, right = null;
            if (count > BvhBuilder.MaxLeafSize)
                (left, right) = depth < MaxDepth ? Split(refs, box) : Halve(refs);
            if (left == null)
            {
                var prims = new int[count];
                for (int i = 0; i < count; i++) prims[i] = refs[i].Prim;
                Nodes[nodeIndex] = new BvhFlatNode { Min = box.Min, Max = box.Max, Count = count };
                LeafPrims[nodeIndex] = prims;
                return;
            }
            int pair = Interlocked.Add(ref _next, 2) - 2;
            Nodes[nodeIndex] = new BvhFlatNode { Min = box.Min, Max = box.Max, Offset = pair, Count = 0 };
            if (parallel && count > ParallelThreshold)
                Parallel.Invoke(() => Build(pair, left, depth + 1, true), () => Build(pair + 1, right!, depth + 1, true));
            else
            {
                Build(pair, left, depth + 1, false);
                Build(pair + 1, right!, depth + 1, false);
            }
        }

        private static (Ref[], Ref[]) Halve(Ref[] refs) => (refs[..(refs.Length / 2)], refs[(refs.Length / 2)..]);

        private (Ref[]? Left, Ref[]? Right) Split(Ref[] refs, AABB box)
        {
            int count = refs.Length;
            float area = MathF.Max(box.SurfaceArea(), float.Epsilon);

            // ── Object split (binned SAH on centroids) ───────────────────
            Vector3 cmin = new(float.MaxValue), cmax = new(float.MinValue);
            foreach (var r in refs)
            {
                Vector3 c = (r.Box.Min + r.Box.Max) * 0.5f;
                cmin = Vector3.Min(cmin, c);
                cmax = Vector3.Max(cmax, c);
            }
            float bestObject = float.MaxValue;
            int objAxis = -1, objBin = -1;
            AABB objLeft = AABB.Empty, objRight = AABB.Empty;
            Span<int> counts = stackalloc int[NumBins];
            Span<AABB> bins = stackalloc AABB[NumBins];
            Span<AABB> leftBox = stackalloc AABB[NumBins];
            Span<int> leftCount = stackalloc int[NumBins];
            Vector3 cext = cmax - cmin;
            for (int axis = 0; axis < 3; axis++)
            {
                float ext = BvhBuilder.Comp(cext, axis);
                if (ext <= 0f) continue;
                float cbase = BvhBuilder.Comp(cmin, axis), scale = NumBins / ext;
                counts.Clear();
                bins.Fill(AABB.Empty);
                foreach (var r in refs)
                {
                    int b = Bin(BvhBuilder.Comp((r.Box.Min + r.Box.Max) * 0.5f, axis), cbase, scale);
                    counts[b]++;
                    bins[b] = AABB.SurroundingBox(bins[b], r.Box);
                }
                AABB acc = AABB.Empty;
                int cnt = 0;
                for (int i = 0; i < NumBins; i++)
                {
                    acc = AABB.SurroundingBox(acc, bins[i]);
                    cnt += counts[i];
                    leftBox[i] = acc;
                    leftCount[i] = cnt;
                }
                acc = AABB.Empty;
                cnt = 0;
                for (int i = NumBins - 1; i > 0; i--)
                {
                    acc = AABB.SurroundingBox(acc, bins[i]);
                    cnt += counts[i];
                    int nl = leftCount[i - 1];
                    if (nl == 0 || cnt == 0) continue;
                    float cost = BvhBuilder.TraversalCost + (nl * leftBox[i - 1].SurfaceArea() + cnt * acc.SurfaceArea()) / area;
                    if (cost < bestObject)
                    {
                        bestObject = cost;
                        objAxis = axis;
                        objBin = i - 1;
                        objLeft = leftBox[i - 1];
                        objRight = acc;
                    }
                }
            }

            // ── Spatial split, where the object split's halves overlap ──
            float bestSpatial = float.MaxValue;
            int spAxis = -1;
            float spPos = 0f;
            if (objAxis >= 0 && Volatile.Read(ref _duplicates) < _budget)
            {
                Vector3 omin = Vector3.Max(objLeft.Min, objRight.Min), omax = Vector3.Min(objLeft.Max, objRight.Max);
                float overlap = omin.X <= omax.X && omin.Y <= omax.Y && omin.Z <= omax.Z
                    ? new AABB(omin, omax).SurfaceArea() : 0f;
                if (overlap > Alpha * RootArea)
                    (bestSpatial, spAxis, spPos) = BestSpatial(refs, box, area);
            }

            if (objAxis < 0 && spAxis < 0)
                return count > BvhBuilder.MaxLeafSize ? Halve(refs) : (null, null);
            float best = MathF.Min(bestObject, bestSpatial);
            if (count <= BvhBuilder.MaxSahLeafSize && best >= count) return (null, null);

            if (spAxis >= 0 && bestSpatial < bestObject)
            {
                var split = SplitSpatial(refs, spAxis, spPos);
                if (split.Left != null) return split;
            }
            if (objAxis < 0) return Halve(refs);

            // Object partition.
            float bb = BvhBuilder.Comp(cmin, objAxis), bs = NumBins / BvhBuilder.Comp(cext, objAxis);
            var l = new List<Ref>(count);
            var rr = new List<Ref>(count);
            foreach (var r in refs)
                (Bin(BvhBuilder.Comp((r.Box.Min + r.Box.Max) * 0.5f, objAxis), bb, bs) <= objBin ? l : rr).Add(r);
            if (l.Count == 0 || rr.Count == 0) return Halve(refs);
            return (l.ToArray(), rr.ToArray());
        }

        private (float Cost, int Axis, float Pos) BestSpatial(Ref[] refs, AABB box, float area)
        {
            float best = float.MaxValue, pos = 0f;
            int bestAxis = -1;
            Span<AABB> bins = stackalloc AABB[NumBins];
            Span<int> entry = stackalloc int[NumBins];
            Span<int> exit = stackalloc int[NumBins];
            Span<AABB> leftBox = stackalloc AABB[NumBins];
            Span<int> leftCount = stackalloc int[NumBins];
            Span<Vector3> poly = stackalloc Vector3[9];
            for (int axis = 0; axis < 3; axis++)
            {
                float lo = BvhBuilder.Comp(box.Min, axis), hi = BvhBuilder.Comp(box.Max, axis);
                float width = (hi - lo) / NumBins;
                if (!(width > 0f)) continue;
                bins.Fill(AABB.Empty);
                entry.Clear();
                exit.Clear();
                foreach (var r in refs)
                {
                    int b0 = SpatialBin(BvhBuilder.Comp(r.Box.Min, axis), lo, width);
                    int b1 = SpatialBin(BvhBuilder.Comp(r.Box.Max, axis), lo, width);
                    entry[b0]++;
                    exit[b1]++;
                    if (b0 == b1) { bins[b0] = AABB.SurroundingBox(bins[b0], r.Box); continue; }
                    for (int b = b0; b <= b1; b++)
                    {
                        float s0 = b == b0 ? float.NegativeInfinity : lo + b * width;
                        float s1 = b == b1 ? float.PositiveInfinity : lo + (b + 1) * width;
                        if (ClippedBox(r, axis, s0, s1, poly, out AABB part))
                            bins[b] = AABB.SurroundingBox(bins[b], part);
                    }
                }
                AABB acc = AABB.Empty;
                int cnt = 0;
                for (int i = 0; i < NumBins; i++)
                {
                    acc = AABB.SurroundingBox(acc, bins[i]);
                    cnt += entry[i];
                    leftBox[i] = acc;
                    leftCount[i] = cnt;
                }
                acc = AABB.Empty;
                cnt = 0;
                for (int i = NumBins - 1; i > 0; i--)
                {
                    acc = AABB.SurroundingBox(acc, bins[i]);
                    cnt += exit[i];
                    int nl = leftCount[i - 1];
                    if (nl == 0 || cnt == 0) continue;
                    float cost = BvhBuilder.TraversalCost + (nl * leftBox[i - 1].SurfaceArea() + cnt * acc.SurfaceArea()) / area;
                    if (cost < best)
                    {
                        best = cost;
                        bestAxis = axis;
                        pos = lo + i * width;
                    }
                }
            }
            return (best, bestAxis, pos);
        }

        private (Ref[]? Left, Ref[]? Right) SplitSpatial(Ref[] refs, int axis, float pos)
        {
            var l = new List<Ref>(refs.Length);
            var r = new List<Ref>(refs.Length);
            Span<Vector3> poly = stackalloc Vector3[9];
            int added = 0;
            foreach (var x in refs)
            {
                float mn = BvhBuilder.Comp(x.Box.Min, axis), mx = BvhBuilder.Comp(x.Box.Max, axis);
                if (mx <= pos) { l.Add(x); continue; }
                if (mn >= pos) { r.Add(x); continue; }
                bool hasL = ClippedBox(x, axis, float.NegativeInfinity, pos, poly, out AABB bl);
                bool hasR = ClippedBox(x, axis, pos, float.PositiveInfinity, poly, out AABB br);
                if (hasL && hasR)
                {
                    l.Add(new Ref { Prim = x.Prim, Box = bl });
                    r.Add(new Ref { Prim = x.Prim, Box = br });
                    added++;
                }
                else (hasL ? l : r).Add(hasL ? new Ref { Prim = x.Prim, Box = bl } : new Ref { Prim = x.Prim, Box = br });
            }
            if (l.Count == 0 || r.Count == 0) return (null, null);
            Interlocked.Add(ref _duplicates, added);
            return (l.ToArray(), r.ToArray());
        }

        /// <summary>
        /// Box of the part of the reference's triangle inside its box and the
        /// slab [s0, s1] on the axis (Sutherland-Hodgman), grown by a few ulps
        /// so rounding never leaves a sliver of the triangle outside every
        /// part. The slab bounds themselves are kept exact: the two sides of a
        /// split share the plane.
        /// </summary>
        private bool ClippedBox(in Ref r, int axis, float s0, float s1, Span<Vector3> poly, out AABB part)
        {
            int p = r.Prim;
            poly[0] = vertices[3 * p];
            poly[1] = vertices[3 * p + 1];
            poly[2] = vertices[3 * p + 2];
            int n = 3;
            // The reference's own box, the triangle's grown by the padding.
            Vector3 bmin = r.Box.Min + pad, bmax = r.Box.Max - pad;
            float lo = MathF.Max(s0, BvhBuilder.Comp(bmin, axis)), hi = MathF.Min(s1, BvhBuilder.Comp(bmax, axis));
            for (int a = 0; a < 3 && n > 0; a++)
            {
                float amin = a == axis ? lo : BvhBuilder.Comp(bmin, a);
                float amax = a == axis ? hi : BvhBuilder.Comp(bmax, a);
                n = Clip(poly, n, a, amin, keepAbove: true);
                n = Clip(poly, n, a, amax, keepAbove: false);
            }
            if (n == 0) { part = AABB.Empty; return false; }
            Vector3 mn = poly[0], mx = poly[0];
            for (int i = 1; i < n; i++) { mn = Vector3.Min(mn, poly[i]); mx = Vector3.Max(mx, poly[i]); }
            Vector3 am = Vector3.Max(Vector3.Abs(mn), Vector3.Abs(mx));
            float m = MathF.Max(am.X, MathF.Max(am.Y, am.Z));
            var g = new Vector3(4f * (MathF.BitIncrement(m) - m));
            // Never larger than the reference was; the split plane exact.
            mn = Vector3.Max(mn - g - pad, r.Box.Min);
            mx = Vector3.Min(mx + g + pad, r.Box.Max);
            if (float.IsFinite(s0)) mn = With(mn, axis, MathF.Max(BvhBuilder.Comp(mn, axis), s0));
            if (float.IsFinite(s1)) mx = With(mx, axis, MathF.Min(BvhBuilder.Comp(mx, axis), s1));
            part = new AABB(mn, mx);
            return true;
        }

        // Clips the convex polygon to the half-space coordinate[a] >= v (or <= v).
        private static int Clip(Span<Vector3> poly, int n, int a, float v, bool keepAbove)
        {
            if (n == 0) return 0;
            Span<Vector3> output = stackalloc Vector3[9];
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = poly[i], q = poly[(i + 1) % n];
                float dp = BvhBuilder.Comp(p, a) - v, dq = BvhBuilder.Comp(q, a) - v;
                bool pin = keepAbove ? dp >= 0f : dp <= 0f;
                bool qin = keepAbove ? dq >= 0f : dq <= 0f;
                if (pin && m < 9) output[m++] = p;
                if (pin != qin && m < 9)
                {
                    float t = dp / (dp - dq);
                    Vector3 x = p + (q - p) * t;
                    output[m++] = With(x, a, v);
                }
            }
            output[..m].CopyTo(poly);
            return m;
        }

        private static Vector3 With(Vector3 v, int axis, float x) =>
            axis == 0 ? new Vector3(x, v.Y, v.Z) : axis == 1 ? new Vector3(v.X, x, v.Z) : new Vector3(v.X, v.Y, x);

        private static int SpatialBin(float x, float lo, float width)
        {
            int b = (int)((x - lo) / width);
            return b < 0 ? 0 : b >= NumBins ? NumBins - 1 : b;
        }
    }

    private static int Bin(float c, float cbase, float scale)
    {
        int b = (int)((c - cbase) * scale);
        return b < 0 ? 0 : b >= NumBins ? NumBins - 1 : b;
    }
}
