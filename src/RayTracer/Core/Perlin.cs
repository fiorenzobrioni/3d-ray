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

        // Gather the 8 lattice gradient vectors into locals instead of a
        // heap-allocated Vector3[2,2,2] (the old code allocated 144 bytes on
        // every Noise() call — and Noise is invoked once per fractal octave at
        // essentially every shaded hit). The permutation lookups for the two
        // neighbours on each axis are hoisted so each table is indexed twice,
        // not eight times. The trilinear blend below reproduces the previous
        // PerlinInterp accumulation order exactly, so output is bit-identical.
        int px0 = _permX[i & 255], px1 = _permX[(i + 1) & 255];
        int py0 = _permY[j & 255], py1 = _permY[(j + 1) & 255];
        int pz0 = _permZ[k & 255], pz1 = _permZ[(k + 1) & 255];

        Vector3 c000 = _ranvec[px0 ^ py0 ^ pz0];
        Vector3 c001 = _ranvec[px0 ^ py0 ^ pz1];
        Vector3 c010 = _ranvec[px0 ^ py1 ^ pz0];
        Vector3 c011 = _ranvec[px0 ^ py1 ^ pz1];
        Vector3 c100 = _ranvec[px1 ^ py0 ^ pz0];
        Vector3 c101 = _ranvec[px1 ^ py0 ^ pz1];
        Vector3 c110 = _ranvec[px1 ^ py1 ^ pz0];
        Vector3 c111 = _ranvec[px1 ^ py1 ^ pz1];

        float uu = u * u * (3 - 2 * u);
        float vv = v * v * (3 - 2 * v);
        float ww = w * w * (3 - 2 * w);

        // Hermite weights per corner: (1-uu) for the low side, uu for the high
        // side — matching the old (i*uu + (1-i)*(1-uu)) form exactly.
        float accum = 0f;
        accum += (1 - uu) * (1 - vv) * (1 - ww) * Vector3.Dot(c000, new Vector3(u,     v,     w));
        accum += (1 - uu) * (1 - vv) * (ww)     * Vector3.Dot(c001, new Vector3(u,     v,     w - 1));
        accum += (1 - uu) * (vv)     * (1 - ww) * Vector3.Dot(c010, new Vector3(u,     v - 1, w));
        accum += (1 - uu) * (vv)     * (ww)     * Vector3.Dot(c011, new Vector3(u,     v - 1, w - 1));
        accum += (uu)     * (1 - vv) * (1 - ww) * Vector3.Dot(c100, new Vector3(u - 1, v,     w));
        accum += (uu)     * (1 - vv) * (ww)     * Vector3.Dot(c101, new Vector3(u - 1, v,     w - 1));
        accum += (uu)     * (vv)     * (1 - ww) * Vector3.Dot(c110, new Vector3(u - 1, v - 1, w));
        accum += (uu)     * (vv)     * (ww)     * Vector3.Dot(c111, new Vector3(u - 1, v - 1, w - 1));
        return accum;
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
    /// Heterogeneous Terrain — Musgrave fractal (Ebert/Musgrave/Peachey/Perlin
    /// "Texturing &amp; Modeling: A Procedural Approach", 3rd ed. §16.3.3).
    /// Same family used by Cycles' Musgrave Texture node "Hetero Terrain",
    /// Houdini's <c>turbulence</c> with heterogeneous mode, and the classic
    /// MojoWorld / RenderMan terrain shaders.
    ///
    /// <para>
    /// Each octave's contribution is multiplied by the current running value,
    /// so the surface picks up more high-frequency roughness where elevation
    /// (i.e. accumulated signal) is already high, and stays smooth at "sea
    /// level". This is the hallmark of natural eroded terrain — sharp ridges
    /// up top, gentle valleys below — that pure fBm cannot reproduce (fBm has
    /// identical statistics at every altitude).
    /// </para>
    ///
    /// <para>
    /// Parameters mirror Musgrave's original signature:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>H</b> (<paramref name="h"/>, "fractal increment"):
    ///     controls how fast amplitude decays vs frequency. Spectral weight
    ///     of octave i is <c>lacunarity^(-i·H)</c>. H = 1 ⇒ statistical
    ///     self-similarity (rough at every scale); H → 0 ⇒ white-noise-ish;
    ///     H ≫ 1 ⇒ smooth, low-frequency dominated. Typical terrain: 0.25.</description></item>
    ///   <item><description><b>offset</b> (<paramref name="offset"/>, "sea level"):
    ///     additive bias inside each octave. Values around 0.7 produce the
    ///     classic terrain look; raising it sinks more area below the multiplier
    ///     threshold (more "flat plains"), lowering it raises mountains everywhere.</description></item>
    /// </list>
    /// </summary>
    public float HeteroTerrain(Vector3 p, int octaves, float lacunarity, float h, float offset)
    {
        // First unscaled octave — sets the baseline elevation field.
        float value = offset + Noise(p);
        p *= lacunarity;

        // Spectral construction. The exponent_array of Musgrave's original
        // implementation is computed inline as `lacunarity^(-i·H)`: cheap, no
        // allocation, and remains numerically identical to the pre-baked
        // version since lacunarity^(-H) is constant across the loop.
        float frequencyWeight = 1f;          // lacunarity^(-0·H) = 1 at i = 0
        float weightDecay = MathF.Pow(lacunarity, -h);
        for (int i = 1; i < octaves; i++)
        {
            frequencyWeight *= weightDecay;
            float increment = (Noise(p) + offset) * frequencyWeight * value;
            value += increment;
            p *= lacunarity;
        }
        return value;
    }

    /// <summary>
    /// Hybrid Multifractal — Musgrave fractal (Ebert/Musgrave/Peachey/Perlin
    /// "Texturing &amp; Modeling", 3rd ed. §16.3.4). The other half of the
    /// Cycles "Musgrave" / RenderMan terrain pair.
    ///
    /// <para>
    /// Compared to <see cref="HeteroTerrain"/>, the per-octave amplitude is
    /// multiplied by a running <i>weight</i> (clamped to 1) instead of the raw
    /// running value. This produces stratified rock layers and crisp peaks
    /// that <i>only</i> hybrid multifractal can reach — useful for high-altitude
    /// rocks, alien planet surfaces, weathered stratified marble where each
    /// "stratum" has its own intra-frequency statistics.
    /// </para>
    /// </summary>
    public float HybridMultifractal(Vector3 p, int octaves, float lacunarity, float h, float offset)
    {
        float frequencyWeight = 1f;
        float weightDecay = MathF.Pow(lacunarity, -h);

        // First octave — `weight` is initialised to the raw signal so the
        // recursion below decays naturally when the field is locally low.
        float result = (Noise(p) + offset) * frequencyWeight;
        float weight = result;
        p *= lacunarity;

        for (int i = 1; i < octaves; i++)
        {
            // Prevent divergence — weight ≥ 1 would let high-frequency
            // octaves dominate and blow the field up exponentially.
            if (weight > 1f) weight = 1f;

            frequencyWeight *= weightDecay;
            float signal = (Noise(p) + offset) * frequencyWeight;
            result += weight * signal;
            weight *= signal;
            p *= lacunarity;
        }
        return result;
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

}
