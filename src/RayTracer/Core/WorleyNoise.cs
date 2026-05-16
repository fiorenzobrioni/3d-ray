using System.Collections.Concurrent;
using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Worley (cellular) noise — Steven Worley 1996. The space is divided into a
/// 3-D grid of unit cells; each cell contains one or more feature points
/// scattered deterministically. At a query point, the noise returns
/// distances/IDs of the nearest features.
///
/// <para>
/// Pro renderers expose this as Arnold's <c>cell_noise</c>, Cycles' Voronoi
/// Texture and RenderMan's <c>PxrVoronoise</c>. Common output modes:
/// <list type="bullet">
///   <item><description><b>F1</b>: distance to the nearest feature point.</description></item>
///   <item><description><b>F2</b>: distance to the second-nearest.</description></item>
///   <item><description><b>F2 − F1</b>: "crackle" — sharp ridges between cells.</description></item>
///   <item><description><b>Cell</b>: a deterministic color drawn from the owning cell's feature ID.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Distance metric selection (Euclidean, Manhattan, Chebyshev) reproduces the
/// visual styles available in Cycles/Houdini. <c>Randomness</c> in [0, 1]
/// jitters feature points away from the cell centre.
/// </para>
/// </summary>
public sealed class WorleyNoise
{
    public enum Metric
    {
        Euclidean,
        EuclideanSquared,
        Manhattan,
        Chebyshev,
    }

    private const int PointCount = 256;
    private readonly Vector3[] _jitter;
    private readonly int[] _permX;
    private readonly int[] _permY;
    private readonly int[] _permZ;

    private static readonly ConcurrentDictionary<int, WorleyNoise> _seedCache = new();

    public static WorleyNoise GetOrCreate(int seed) =>
        _seedCache.GetOrAdd(seed, s => new WorleyNoise(s));

    public WorleyNoise() : this(0) { }

    public WorleyNoise(int seed)
    {
        var rng = new Random(seed);
        _jitter = new Vector3[PointCount];
        for (int i = 0; i < PointCount; i++)
        {
            _jitter[i] = new Vector3(
                (float)rng.NextDouble(),
                (float)rng.NextDouble(),
                (float)rng.NextDouble());
        }
        _permX = GeneratePerm(rng);
        _permY = GeneratePerm(rng);
        _permZ = GeneratePerm(rng);
    }

    /// <summary>
    /// Evaluates the cellular noise at <paramref name="p"/>. Returns F1 and F2
    /// distances plus a deterministic hash (cell ID) of the owning cell.
    /// </summary>
    public void Evaluate(Vector3 p, Metric metric, float randomness, out float f1, out float f2, out int cellId)
    {
        randomness = Math.Clamp(randomness, 0f, 1f);

        int ix = (int)MathF.Floor(p.X);
        int iy = (int)MathF.Floor(p.Y);
        int iz = (int)MathF.Floor(p.Z);

        float bestF1 = float.MaxValue;
        float bestF2 = float.MaxValue;
        int bestId = 0;

        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ix + dx;
            int cy = iy + dy;
            int cz = iz + dz;

            int hash = HashCell(cx, cy, cz);
            Vector3 jitter = _jitter[hash];

            // Centred jitter in [0.5 - r/2, 0.5 + r/2] so randomness=0 collapses
            // the feature to the cell centre (regular grid pattern).
            Vector3 feature = new(
                cx + 0.5f + (jitter.X - 0.5f) * randomness,
                cy + 0.5f + (jitter.Y - 0.5f) * randomness,
                cz + 0.5f + (jitter.Z - 0.5f) * randomness);

            float d = Distance(p, feature, metric);
            if (d < bestF1)
            {
                bestF2 = bestF1;
                bestF1 = d;
                bestId = HashCellId(cx, cy, cz);
            }
            else if (d < bestF2)
            {
                bestF2 = d;
            }
        }

        f1 = bestF1;
        f2 = bestF2;
        cellId = bestId;
    }

    /// <summary>
    /// Extended cellular evaluation — returns the four closest feature
    /// distances <c>F1 ≤ F2 ≤ F3 ≤ F4</c>, the cell ID of the F1 cell, and the
    /// absolute feature-point position of the F1 cell.
    ///
    /// <para>
    /// F3 and F4 are exposed for hierarchical cellular shading (cell-in-cell
    /// patterns, multi-scale leather, large-band border masks): the wider
    /// <c>F3 − F1</c> band has lower spectral frequency than <c>F2 − F1</c>
    /// and so produces softer, larger crackle networks. The feature position
    /// is the per-cell deterministic XYZ jitter point — used as a "random per
    /// cell" stochastic ID to drive another texture (Cycles' <c>Position</c>
    /// output, Houdini Voronoi <c>P_</c> attribute, RenderMan
    /// <c>PxrVoronoise</c>'s position output).
    /// </para>
    ///
    /// <para>
    /// Same O(27) cost as <see cref="Evaluate"/>: every neighbouring cell is
    /// already scanned and the extra book-keeping is three additional float
    /// comparisons per cell. For the F1/F2 channels this method is
    /// bit-identical to <see cref="Evaluate"/> by construction (the
    /// insertion ladder reproduces the same two-slot update on the top of the
    /// list).
    /// </para>
    /// </summary>
    public void EvaluateExtended(
        Vector3 p, Metric metric, float randomness,
        out float f1, out float f2, out float f3, out float f4,
        out int cellId, out Vector3 featurePosition)
    {
        randomness = Math.Clamp(randomness, 0f, 1f);

        int ix = (int)MathF.Floor(p.X);
        int iy = (int)MathF.Floor(p.Y);
        int iz = (int)MathF.Floor(p.Z);

        float b1 = float.MaxValue;
        float b2 = float.MaxValue;
        float b3 = float.MaxValue;
        float b4 = float.MaxValue;
        int bestId = 0;
        Vector3 bestFeature = Vector3.Zero;

        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ix + dx;
            int cy = iy + dy;
            int cz = iz + dz;

            int hash = HashCell(cx, cy, cz);
            Vector3 jitter = _jitter[hash];
            Vector3 feature = new(
                cx + 0.5f + (jitter.X - 0.5f) * randomness,
                cy + 0.5f + (jitter.Y - 0.5f) * randomness,
                cz + 0.5f + (jitter.Z - 0.5f) * randomness);

            float d = Distance(p, feature, metric);

            // 4-slot insertion sort. Reproduces the F1/F2 update of
            // Evaluate() exactly on the first two slots (same compare order,
            // same swap semantics), so the F1/F2/cellId fields are
            // bit-identical to the 2-slot version on every input.
            if (d < b1)
            {
                b4 = b3; b3 = b2; b2 = b1; b1 = d;
                bestId = HashCellId(cx, cy, cz);
                bestFeature = feature;
            }
            else if (d < b2)
            {
                b4 = b3; b3 = b2; b2 = d;
            }
            else if (d < b3)
            {
                b4 = b3; b3 = d;
            }
            else if (d < b4)
            {
                b4 = d;
            }
        }

        f1 = b1;
        f2 = b2;
        f3 = b3;
        f4 = b4;
        cellId = bestId;
        featurePosition = bestFeature;
    }

    /// <summary>
    /// Smooth-Voronoi variant of <see cref="Evaluate"/>. When
    /// <paramref name="smoothness"/> is &gt; 0 the hard <c>min()</c> over the
    /// neighbouring cells is replaced by Inigo Quilez' "Smooth Voronoi"
    /// soft-min <c>-log(Σ exp(-k·d_i)) / k</c> with <c>k = 20/smoothness</c>
    /// (k→∞ at smoothness→0 recovers the hard <see cref="Evaluate"/> result
    /// exactly).
    ///
    /// <para>
    /// The F2 channel is produced by the same log-sum-exp accumulator with
    /// the dominant (closest-cell) contribution subtracted before the log —
    /// continuous across 2-cell boundaries by symmetry, giving the
    /// "smooth crackle" look that hard F2−F1 cannot reach: bordi morbidi,
    /// niente alias a step lungo le creste.
    /// </para>
    ///
    /// <para>
    /// Numerical stability: every exponent is rebased on the hard nearest
    /// distance so all <c>exp()</c> arguments stay in <c>(-∞, 0]</c> and the
    /// largest weight is exactly <c>1</c>. Without rebasing, <c>k</c> values
    /// of order 20 over distances of order 1 would push <c>exp()</c> well
    /// outside single-precision range at high smoothness.
    /// </para>
    /// </summary>
    public void EvaluateSmooth(
        Vector3 p, Metric metric, float randomness, float smoothness,
        out float f1, out float f2, out int cellId)
    {
        if (smoothness <= 0f)
        {
            Evaluate(p, metric, randomness, out f1, out f2, out cellId);
            return;
        }

        randomness = Math.Clamp(randomness, 0f, 1f);
        smoothness = Math.Clamp(smoothness, 0f, 1f);
        // IQ "Smooth Voronoi" — k controls the softness of the min().
        // k = 20/smoothness so smoothness=1 gives k=20 (visibly soft) and
        // smoothness→0 gives k→∞ (pure hard min).
        double k = 20.0 / smoothness;

        int ix = (int)MathF.Floor(p.X);
        int iy = (int)MathF.Floor(p.Y);
        int iz = (int)MathF.Floor(p.Z);

        // First pass: hard nearest, so we can rebase the log-sum-exp on it.
        Span<float> dists = stackalloc float[27];
        float hardF1 = float.MaxValue;
        int hardCellId = 0;
        int hardIdx = 0;
        int n = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ix + dx;
            int cy = iy + dy;
            int cz = iz + dz;

            int hash = HashCell(cx, cy, cz);
            Vector3 jitter = _jitter[hash];
            Vector3 feature = new(
                cx + 0.5f + (jitter.X - 0.5f) * randomness,
                cy + 0.5f + (jitter.Y - 0.5f) * randomness,
                cz + 0.5f + (jitter.Z - 0.5f) * randomness);

            float d = Distance(p, feature, metric);
            dists[n] = d;
            if (d < hardF1)
            {
                hardF1 = d;
                hardCellId = HashCellId(cx, cy, cz);
                hardIdx = n;
            }
            n++;
        }

        // sumAll  = Σ exp(-k · (d_i − hardF1))         (closest contributes 1)
        // sumRest = Σ_{i ≠ hardIdx} exp(-k · (d_i − hardF1))
        //
        // Accumulating sumRest directly (instead of sumAll − 1) avoids the
        // catastrophic cancellation that would otherwise eat the second-nearest
        // weight in single precision once it drops below float32 epsilon
        // against 1.0. Production renderers (PxrVoronoise, Cycles "Smooth F1")
        // do the same: track the dominant weight by index, not by subtraction.
        // The accumulator runs in double precision so the per-cell exponentials
        // (which can decay 13+ decades for moderate smoothness) compose without
        // loss of significance even after dozens of MADD operations.
        double sumAll  = 1.0;
        double sumRest = 0.0;
        for (int i = 0; i < 27; i++)
        {
            if (i == hardIdx) continue;
            double w = Math.Exp(-k * (dists[i] - hardF1));
            sumAll  += w;
            sumRest += w;
        }

        float softF1 = (float)(hardF1 - Math.Log(sumAll) / k);

        // Hard second-nearest, computed once for the two roles below:
        //   (a) upper-bound clamp on softF2 (it must never exceed the true
        //       second distance; bounded above by Jensen on the convex −log);
        //   (b) underflow fallback when every non-dominant weight collapses
        //       beneath the double-precision floor (≈ 1e-308). That happens
        //       once k·(hardF2 − hardF1) ≫ 700, i.e. at extremely small
        //       smoothness or extremely well-separated features — both of
        //       which are the "hard regime" by definition, so the limit
        //       softF2 → hardF2 is the right answer there.
        float hardF2 = float.MaxValue;
        for (int i = 0; i < 27; i++)
            if (i != hardIdx && dists[i] < hardF2) hardF2 = dists[i];

        float softF2;
        if (sumRest < 1e-300)
        {
            softF2 = hardF2;
        }
        else
        {
            softF2 = (float)(hardF1 - Math.Log(sumRest) / k);
            if (softF2 > hardF2) softF2 = hardF2;
        }

        f1 = softF1;
        f2 = softF2;
        cellId = hardCellId;
    }

    private static float Distance(Vector3 a, Vector3 b, Metric metric)
    {
        Vector3 d = a - b;
        return metric switch
        {
            Metric.Euclidean        => MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z),
            Metric.EuclideanSquared => d.X * d.X + d.Y * d.Y + d.Z * d.Z,
            Metric.Manhattan        => MathF.Abs(d.X) + MathF.Abs(d.Y) + MathF.Abs(d.Z),
            Metric.Chebyshev        => MathF.Max(MathF.Abs(d.X), MathF.Max(MathF.Abs(d.Y), MathF.Abs(d.Z))),
            _                       => MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z),
        };
    }

    private int HashCell(int x, int y, int z) =>
        _permX[x & 255] ^ _permY[y & 255] ^ _permZ[z & 255];

    private static int HashCellId(int x, int y, int z)
    {
        // 32-bit splitmix-style cell hash, independent from the jitter perm tables
        // so distinct seeds with the same x/y/z map to the same cell ID (useful
        // for matching cell colors between F1 and F2 lookups).
        uint h = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791));
        h = unchecked((h ^ (h >> 16)) * 0x85ebca6bu);
        h = unchecked((h ^ (h >> 13)) * 0xc2b2ae35u);
        h ^= h >> 16;
        return unchecked((int)h);
    }

    private static int[] GeneratePerm(Random rng)
    {
        var p = new int[PointCount];
        for (int i = 0; i < PointCount; i++) p[i] = i;
        for (int i = PointCount - 1; i > 0; i--)
        {
            int t = rng.Next(0, i + 1);
            (p[i], p[t]) = (p[t], p[i]);
        }
        return p;
    }

    /// <summary>
    /// Maps a 32-bit cell ID to a saturated RGB triple. Used by Voronoi Cell
    /// output mode. Matches the Cycles "Color" output style: each cell gets a
    /// random but stable colour.
    /// </summary>
    public static Vector3 CellColor(int cellId)
    {
        uint h = unchecked((uint)cellId);
        float r = ((h >>  0) & 0xFF) / 255f;
        float g = ((h >>  8) & 0xFF) / 255f;
        float b = ((h >> 16) & 0xFF) / 255f;
        return new Vector3(r, g, b);
    }
}
