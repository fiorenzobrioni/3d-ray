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

    /// <summary>
    /// Fractional Brownian motion (fBm): sum of octaves of Perlin noise with
    /// configurable lacunarity (frequency multiplier) and gain (amplitude decay).
    /// Returns a signed value roughly in [-1, 1] when <paramref name="signed"/>
    /// is true; otherwise remapped to [0, 1].
    ///
    /// <para>
    /// Pro-grade defaults match the de-facto industry standard used by Arnold's
    /// <c>noise</c>, RenderMan's <c>PxrFractal</c> and Cycles' Noise Texture in
    /// fBm mode (lacunarity 2.0, gain 0.5).
    /// </para>
    /// </summary>
    public float Fbm(Vector3 p, int octaves, float lacunarity, float gain, bool signed = false)
    {
        float accum = 0f;
        float amplitude = 1f;
        float maxAmp = 0f;
        Vector3 tempP = p;

        for (int i = 0; i < octaves; i++)
        {
            accum += amplitude * Noise(tempP);
            maxAmp += amplitude;
            amplitude *= gain;
            tempP *= lacunarity;
        }

        // Normalise into roughly [-1, 1] regardless of octave count
        float result = maxAmp > 0f ? accum / maxAmp : 0f;
        return signed ? result : (result + 1f) * 0.5f;
    }

    /// <summary>
    /// Ridged multifractal noise (Musgrave 1998). Produces sharp ridges by
    /// inverting and squaring |Noise| at each octave. Widely used in pro
    /// renderers for rocks, mountains, marble veins.
    /// Output is clamped to [0, 1].
    /// </summary>
    public float Ridged(Vector3 p, int octaves, float lacunarity, float gain)
    {
        float accum = 0f;
        float amplitude = 1f;
        float maxAmp = 0f;
        Vector3 tempP = p;

        for (int i = 0; i < octaves; i++)
        {
            float n = 1f - MathF.Abs(Noise(tempP));
            n *= n; // sharpen ridges
            accum += amplitude * n;
            maxAmp += amplitude;
            amplitude *= gain;
            tempP *= lacunarity;
        }

        return maxAmp > 0f ? Math.Clamp(accum / maxAmp, 0f, 1f) : 0f;
    }

    /// <summary>
    /// Billowed noise: sum of |Noise| octaves. Produces puffy cloud-like
    /// shapes. Output remapped to [0, 1].
    /// </summary>
    public float Billow(Vector3 p, int octaves, float lacunarity, float gain)
    {
        float accum = 0f;
        float amplitude = 1f;
        float maxAmp = 0f;
        Vector3 tempP = p;

        for (int i = 0; i < octaves; i++)
        {
            accum += amplitude * MathF.Abs(Noise(tempP));
            maxAmp += amplitude;
            amplitude *= gain;
            tempP *= lacunarity;
        }

        return maxAmp > 0f ? Math.Clamp(accum / maxAmp, 0f, 1f) : 0f;
    }

    /// <summary>
    /// Returns a 3-D Perlin vector sampled at <paramref name="p"/> and shifted
    /// offsets. Used to warp the input of other noise functions (domain
    /// warping / distortion), a technique pioneered by Ken Perlin and made
    /// famous by Inigo Quilez. Each component samples a different region of
    /// the noise field so the warp is decorrelated.
    /// </summary>
    public Vector3 NoiseVector(Vector3 p) => new(
        Noise(p),
        Noise(p + new Vector3(31.416f, 0f, 0f)),
        Noise(p + new Vector3(0f, 47.853f, 0f)));

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
