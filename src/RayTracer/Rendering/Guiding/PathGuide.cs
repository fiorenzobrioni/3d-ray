using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Guiding;

/// <summary>Settings of the path guide (CLI <c>--guiding</c>).</summary>
public sealed record GuidingSettings
{
    /// <summary>
    /// Samples per pixel spent on training, over passes of 1, 2, 4, ... spp,
    /// out of the render's total: the passes join the image, the render takes
    /// the rest.
    /// </summary>
    public int TrainingSpp { get; init; } = 16;

    /// <summary>
    /// Share of the samples drawn from the BSDF (or phase function) where
    /// nothing has been learned yet; each region then learns its own.
    /// </summary>
    public float BsdfFraction { get; init; } = 0.5f;

    /// <summary>Learn the BSDF share per region (Müller 2019); off keeps <see cref="BsdfFraction"/> everywhere.</summary>
    public bool LearnBsdfFraction { get; init; } = true;

    /// <summary>Energy share above which a directional cell is split (Müller 2017: 0.01).</summary>
    public float DirectionalThreshold { get; init; } = 0.01f;

    /// <summary>
    /// Records per spatial leaf, times √(spp of the pass), above which the
    /// leaf is split. Müller 2017 uses 12 000; at the resolutions measured in
    /// PIANO-REVIEW.md Phase 7b (a sunlit room), 3 000 learned a sharper
    /// guide and rendered the same error 10% faster.
    /// </summary>
    public int SpatialThreshold { get; init; } = 3000;

    /// <summary>Spatial leaves at most (each holds two directional trees of a few kB).</summary>
    public int MaxLeaves { get; init; } = 1 << 15;

    /// <summary>
    /// Training spp default for a render of <paramref name="spp"/> in all:
    /// half of it (the passes join the image, weighted by their noise, so a
    /// long training costs little and teaches more; measured against a
    /// quarter in PIANO-REVIEW.md Phase 7b), at least 3 (passes 1+2).
    /// </summary>
    public static int DefaultTraining(int spp) => Math.Max(3, spp / 2);
}

/// <summary>
/// Stochastic box filter of the records (Müller 2019, "Practical Path Guiding
/// in Production"): a record lands at a point drawn uniformly in a box the
/// size of its leaf around where it was taken, so the leaves near a boundary
/// learn from their neighbours' samples too and a region's distribution is
/// smoother for the same records. The draw is a hash of the record, so a
/// render stays deterministic.
/// </summary>
public static class GuideFilter
{
    public static Vector3 Jitter(Vector3 p, Vector2 square, Vector3 leafSize)
    {
        ulong h = Mix(BitConverter.SingleToUInt32Bits(p.X)
                      ^ ((ulong)BitConverter.SingleToUInt32Bits(p.Y) << 21)
                      ^ ((ulong)BitConverter.SingleToUInt32Bits(p.Z) << 42)
                      ^ ((ulong)BitConverter.SingleToUInt32Bits(square.X) << 11)
                      ^ ((ulong)BitConverter.SingleToUInt32Bits(square.Y) << 33));
        float u = (h & 0xFFFFF) / (float)0x100000 - 0.5f;
        float v = ((h >> 20) & 0xFFFFF) / (float)0x100000 - 0.5f;
        float w = ((h >> 40) & 0xFFFFF) / (float)0x100000 - 0.5f;
        return p + new Vector3(u, v, w) * leafSize;
    }

    // SplitMix64 finaliser.
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>
/// One sample of incident radiance for the guide: where, from which
/// direction, how much (radiance luminance over the density it was drawn
/// with). Where the direction came from the BSDF/guide mixture, also the
/// densities of the two and the luminance of radiance times BSDF times
/// cosine, for learning the BSDF share.
/// </summary>
public readonly record struct GuideRecord(int Leaf, Vector2 Square, float Weight,
                                          float BsdfPdf = 0f, float GuidePdf = 0f, float Product = 0f, bool Mixed = false);

/// <summary>
/// Spatio-directional radiance cache for path guiding (Müller, Gross, Novák
/// 2017): a binary tree over the scene's bounds, split at the midpoint along
/// x, y, z in turn, whose leaves hold a directional <see cref="DTree"/> of the
/// radiance arriving there. Trained over passes of doubling sample counts:
/// each pass samples with the distributions learned so far and records into
/// fresh ones; between passes the leaves with many records split in space
/// and the directional trees refine where the energy is.
///
/// <para>Deterministic: during a pass the structure is fixed and records are
/// splatted afterwards leaf by leaf in a fixed order, so the same render
/// gives the same guide on any number of threads.</para>
/// </summary>
public sealed class PathGuide
{
    private struct Node
    {
        public int Axis;      // split axis of an interior node
        public int Child;     // first child (the second is Child + 1); -1 for a leaf
        public int Leaf;      // leaf index when Child == -1
    }

    private sealed class Leaf
    {
        public DTree Sampling = DTree.Empty();
        public DTree Building = DTree.Empty();
        public long Records;

        // BSDF share: θ with α = sigmoid(θ), optimised by Adam; the pass
        // samples with the α fixed at its start.
        public float SamplingFraction;
        public float Theta, M, V;
        public float B1Power = 1f, B2Power = 1f;   // β1^t, β2^t of Adam's bias correction

        public Leaf CloneLeaf() => (Leaf)MemberwiseClone();
    }

    private readonly List<Node> _nodes = new();
    private readonly List<Leaf> _leaves = new();
    private readonly Vector3 _min, _extent;

    public GuidingSettings Settings { get; }

    /// <summary>Passes completed.</summary>
    public int Iterations { get; private set; }

    public int LeafCount => _leaves.Count;

    /// <summary>True once a pass has produced distributions to sample.</summary>
    public bool CanSample => Iterations > 0;

    public PathGuide(AABB bounds, GuidingSettings settings)
    {
        Settings = settings;
        // A cube around the scene, a little larger, so the split cells stay
        // cubes and no point falls on the boundary.
        Vector3 c = 0.5f * (bounds.Min + bounds.Max);
        Vector3 e = bounds.Max - bounds.Min;
        float side = MathF.Max(MathF.Max(e.X, e.Y), MathF.Max(e.Z, 1e-3f)) * 1.02f;
        _extent = new Vector3(side);
        _min = c - 0.5f * _extent;
        float a = Math.Clamp(settings.BsdfFraction, MinFraction, MaxFraction);
        _leaves.Add(new Leaf { SamplingFraction = a, Theta = MathF.Log(a / (1f - a)) });
        _nodes.Add(new Node { Axis = 0, Child = -1, Leaf = 0 });
    }

    // The learned share stays inside these: the BSDF always keeps a tenth of
    // the samples (a guide that missed a lobe must not starve it), and the
    // guide a hundredth.
    private const float MinFraction = 0.1f, MaxFraction = 0.99f;

    /// <summary>Index of the leaf containing <paramref name="p"/> (clamped into the bounds).</summary>
    public int LeafAt(Vector3 p) => LeafAt(p, out _);

    /// <summary>Index of the leaf containing <paramref name="p"/>, and the leaf's size.</summary>
    public int LeafAt(Vector3 p, out Vector3 size)
    {
        size = _extent;
        Vector3 q = (p - _min) / _extent;
        q = Vector3.Clamp(q, Vector3.Zero, new Vector3(0.99999994f));
        int n = 0;
        while (true)
        {
            Node node = _nodes[n];
            if (node.Child < 0) return node.Leaf;
            float x = node.Axis switch { 0 => q.X, 1 => q.Y, _ => q.Z };
            int side = x < 0.5f ? 0 : 1;
            x = side == 0 ? 2f * x : 2f * x - 1f;
            switch (node.Axis)
            {
                case 0: q.X = x; size.X *= 0.5f; break;
                case 1: q.Y = x; size.Y *= 0.5f; break;
                default: q.Z = x; size.Z *= 0.5f; break;
            }
            n = node.Child + side;
        }
    }

    /// <summary>The distribution to sample at leaf <paramref name="leaf"/>, or null when it has none.</summary>
    public DTree? SamplingTree(int leaf)
    {
        DTree t = _leaves[leaf].Sampling;
        return t.Total > 0f ? t : null;
    }

    /// <summary>Share of the samples the BSDF draws at leaf <paramref name="leaf"/> during this pass.</summary>
    public float BsdfFraction(int leaf) => _leaves[leaf].SamplingFraction;

    /// <summary>
    /// Splats one pass's records: grouped by leaf in the order given, then
    /// each leaf's records in sequence (leaves in parallel). The order inside
    /// a leaf is the order of <paramref name="records"/>, so the sums do not
    /// depend on the scheduling of the threads that produced them.
    /// </summary>
    public void Splat(IReadOnlyList<List<GuideRecord>> recordsInOrder)
    {
        // Counting sort by leaf, stable: each leaf's records keep their order.
        int leaves = _leaves.Count, total = 0;
        var start = new int[leaves + 1];
        foreach (var list in recordsInOrder)
        {
            total += list.Count;
            foreach (var r in list) start[r.Leaf + 1]++;
        }
        for (int i = 0; i < leaves; i++) start[i + 1] += start[i];
        var sorted = new GuideRecord[total];
        var fill = (int[])start.Clone();
        foreach (var list in recordsInOrder)
            foreach (var r in list) sorted[fill[r.Leaf]++] = r;

        bool learn = Settings.LearnBsdfFraction;
        Parallel.For(0, leaves, i =>
        {
            int a = start[i], b = start[i + 1];
            if (a == b) return;
            Leaf leaf = _leaves[i];
            for (int k = a; k < b; k++)
            {
                ref readonly GuideRecord r = ref sorted[k];
                leaf.Building.Record(r.Square, r.Weight);
                if (learn && r.Mixed) LearnFraction(leaf, r);
            }
            leaf.Records += b - a;
        });
    }

    /// <summary>
    /// Ends a pass of <paramref name="passSpp"/> samples per pixel: splits the
    /// spatial leaves that received many records (both halves inherit the
    /// directional data), then each leaf samples what it learned and starts
    /// recording into a refined, empty tree.
    /// </summary>
    public void EndIteration(int passSpp)
    {
        long threshold = (long)(Settings.SpatialThreshold * MathF.Sqrt(passSpp));
        int count = _nodes.Count;
        for (int n = 0; n < count; n++) SplitIfNeeded(n, threshold, 0);

        foreach (var leaf in _leaves)
        {
            leaf.Sampling = leaf.Building;
            leaf.Building = leaf.Sampling.Refined(Settings.DirectionalThreshold);
            leaf.Records = 0;
            if (Settings.LearnBsdfFraction)
                leaf.SamplingFraction = Math.Clamp(Sigmoid(leaf.Theta), MinFraction, MaxFraction);
        }
        Iterations++;
    }

    /// <summary>
    /// One Adam step on θ (α = sigmoid θ) against the KL divergence between
    /// the product f·L·cos and the mixture α p_bsdf + (1 - α) p_guide
    /// (Müller 2019, "Practical Path Guiding in Production"): with the sample
    /// drawn from the mixture p it was sampled with and p_θ the mixture at
    /// the current α, ∂KL/∂θ ≈ -(fLcos / p) (p_bsdf - p_guide) α (1 - α) / p_θ.
    /// </summary>
    private static void LearnFraction(Leaf leaf, in GuideRecord r)
    {
        float a = Sigmoid(leaf.Theta);
        float aS = leaf.SamplingFraction;   // the share the sample was drawn with
        float p = aS * r.BsdfPdf + (1f - aS) * r.GuidePdf;
        if (!(p > 0f) || !(r.Product >= 0f) || !float.IsFinite(r.Product)) return;
        float pTheta = a * r.BsdfPdf + (1f - a) * r.GuidePdf;
        if (!(pTheta > 0f)) return;
        float g = -(r.Product / p) * (r.BsdfPdf - r.GuidePdf) * a * (1f - a) / pTheta;
        if (!float.IsFinite(g)) return;
        g += RegularizationWeight * leaf.Theta;
        const float Lr = 0.01f, B1 = 0.9f, B2 = 0.999f, Eps = 1e-8f;
        leaf.B1Power *= B1;
        leaf.B2Power *= B2;
        leaf.M = B1 * leaf.M + (1f - B1) * g;
        leaf.V = B2 * leaf.V + (1f - B2) * g * g;
        float mHat = leaf.M / (1f - leaf.B1Power);
        float vHat = leaf.V / (1f - leaf.B2Power);
        leaf.Theta = Math.Clamp(leaf.Theta - Lr * mHat / (MathF.Sqrt(vHat) + Eps), -20f, 20f);
    }

    // A faint pull of θ towards 0 (α = 1/2) where the gradient says little.
    private const float RegularizationWeight = 0.01f;

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    private void SplitIfNeeded(int n, long threshold, int depth)
    {
        Node node = _nodes[n];
        if (node.Child >= 0) return;
        Leaf leaf = _leaves[node.Leaf];
        if (leaf.Records <= threshold || depth > 48 || _leaves.Count >= Settings.MaxLeaves) return;

        int axis = AxisOf(n);
        var second = leaf.CloneLeaf();
        second.Sampling = leaf.Sampling.Clone();
        second.Building = leaf.Building.Clone();
        second.Records = leaf.Records / 2;
        leaf.Records /= 2;
        _leaves.Add(second);
        int c = _nodes.Count;
        _nodes.Add(new Node { Axis = (axis + 1) % 3, Child = -1, Leaf = node.Leaf });
        _nodes.Add(new Node { Axis = (axis + 1) % 3, Child = -1, Leaf = _leaves.Count - 1 });
        _nodes[n] = new Node { Axis = axis, Child = c, Leaf = -1 };
        // The halves may still be over the threshold.
        SplitIfNeeded(c, threshold, depth + 1);
        SplitIfNeeded(c + 1, threshold, depth + 1);
    }

    // A leaf splits along the axis stored in it (the next one after its parent's).
    private int AxisOf(int n) => _nodes[n].Axis;
}
