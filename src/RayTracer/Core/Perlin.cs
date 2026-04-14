using System.Collections.Concurrent;
using System.Numerics;

namespace RayTracer.Core;

public class Perlin
{
    private const int PointCount = 256;
    private readonly Vector3[] _ranvec;
    private readonly int[] _permX;
    private readonly int[] _permY;
    private readonly int[] _permZ;

    /// <summary>
    /// Process-wide cache of Perlin instances keyed by seed. Two Perlin
    /// instances built from the same seed are bit-identical, so sharing
    /// them across textures saves memory and construction cost while
    /// preserving full determinism.
    /// </summary>
    private static readonly ConcurrentDictionary<int, Perlin> _seedCache = new();

    /// <summary>
    /// Returns a Perlin instance deterministically derived from <paramref name="seed"/>.
    /// Multiple callers sharing the same seed receive the SAME cached instance,
    /// so textures with the same object seed produce identical procedural patterns
    /// across the whole render and across consecutive renders of the same scene.
    /// </summary>
    public static Perlin GetOrCreate(int seed) =>
        _seedCache.GetOrAdd(seed, s => new Perlin(s));

    /// <summary>
    /// Default constructor — uses a fixed canonical seed so the noise pattern is
    /// reproducible across renders even when no explicit object seed is set.
    /// Equivalent to <c>new Perlin(0)</c>.
    /// </summary>
    public Perlin() : this(0)
    {
    }

    /// <summary>
    /// Builds a Perlin noise table from a deterministic seed. A given seed
    /// always yields the same gradient vectors and permutation tables, so
    /// renders of the same scene are bit-reproducible (modulo other
    /// non-determinism such as path-tracing sampling, which has its own RNG).
    /// </summary>
    public Perlin(int seed)
    {
        // Local Random — independent from MathUtils.Rng (which is seeded from
        // Environment.TickCount and would make every render different).
        var rng = new Random(seed);

        _ranvec = new Vector3[PointCount];
        for (int i = 0; i < PointCount; i++)
        {
            _ranvec[i] = Vector3.Normalize(RandomVector3(rng, -1f, 1f));
        }

        _permX = GeneratePerm(rng);
        _permY = GeneratePerm(rng);
        _permZ = GeneratePerm(rng);
    }

    public float Noise(Vector3 p)
    {
        float u = p.X - MathF.Floor(p.X);
        float v = p.Y - MathF.Floor(p.Y);
        float w = p.Z - MathF.Floor(p.Z);

        int i = (int)MathF.Floor(p.X);
        int j = (int)MathF.Floor(p.Y);
        int k = (int)MathF.Floor(p.Z);

        var c = new Vector3[2, 2, 2];

        for (int di = 0; di < 2; di++)
        {
            for (int dj = 0; dj < 2; dj++)
            {
                for (int dk = 0; dk < 2; dk++)
                {
                    c[di, dj, dk] = _ranvec[
                        _permX[(i + di) & 255] ^
                        _permY[(j + dj) & 255] ^
                        _permZ[(k + dk) & 255]
                    ];
                }
            }
        }

        return PerlinInterp(c, u, v, w);
    }

    public float Turbulence(Vector3 p, int depth = 7)
    {
        float accum = 0f;
        Vector3 tempP = p;
        float weight = 1f;

        for (int i = 0; i < depth; i++)
        {
            accum += weight * Noise(tempP);
            weight *= 0.5f;
            tempP *= 2f;
        }

        return MathF.Abs(accum);
    }

    private static int[] GeneratePerm(Random rng)
    {
        var p = new int[PointCount];
        for (int i = 0; i < PointCount; i++)
            p[i] = i;

        Permute(rng, p, PointCount);
        return p;
    }

    private static void Permute(Random rng, int[] p, int n)
    {
        for (int i = n - 1; i > 0; i--)
        {
            int target = rng.Next(0, i + 1);
            (p[i], p[target]) = (p[target], p[i]);
        }
    }

    private static Vector3 RandomVector3(Random rng, float min, float max)
    {
        float Span() => min + (max - min) * (float)rng.NextDouble();
        return new Vector3(Span(), Span(), Span());
    }

    private static float PerlinInterp(Vector3[,,] c, float u, float v, float w)
    {
        float uu = u * u * (3 - 2 * u);
        float vv = v * v * (3 - 2 * v);
        float ww = w * w * (3 - 2 * w);
        float accum = 0f;

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector3 weightV = new Vector3(u - i, v - j, w - k);
                    accum += (i * uu + (1 - i) * (1 - uu))
                           * (j * vv + (1 - j) * (1 - vv))
                           * (k * ww + (1 - k) * (1 - ww))
                           * Vector3.Dot(c[i, j, k], weightV);
                }
            }
        }

        return accum;
    }
}
