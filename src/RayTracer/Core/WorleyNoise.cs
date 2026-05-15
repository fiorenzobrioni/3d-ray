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
