namespace RayTracer.Core.Sampling;

/// <summary>
/// Owen-scrambled, padded 2D Sobol sampler (Burley 2020, "Practical
/// Hash-based Owen Scrambling"; PBRT-v4 PaddedSobolSampler).
///
/// Dimensions are grouped in pairs: dimensions 2k and 2k+1 form block k,
/// and every block is the first two dimensions of the Sobol sequence (van
/// der Corput and the x² + x + 1 Joe-Kuo dimension), which together are a
/// (0, 2)-sequence: every power-of-two prefix stratifies every 2^a × 2^b
/// grid of the unit square. Each block then gets
///
///   • its own shuffle of the sample index (nested uniform scramble, so a
///     power-of-two prefix maps onto an aligned block of the sequence, still
///     a net), which decorrelates the blocks from one another;
///   • an independent Owen scramble per coordinate and per pixel, which
///     randomises the net without breaking it and removes the Moiré of
///     pixels sharing one sequence.
///
/// So every dimension is stratified in 1D, every aligned pair (a 2D decision
/// drawn through <see cref="Sampler.Sample2D"/>) is stratified in 2D, and
/// different pairs behave as independent. The previous construction drew
/// every dimension past 1 from its own shuffled van der Corput sequence:
/// pairs were never jointly stratified and, measured, converged more slowly
/// than independent random numbers (PIANO-REVIEW.md §3.10).
/// </summary>
internal static class OwenSobol
{
    /// <summary>
    /// Owen-scrambled padded Sobol sample for one dimension.
    /// </summary>
    /// <param name="sampleIndex">Sample index within the pixel (0 .. spp-1).</param>
    /// <param name="dimension">Sequence dimension; 2k and 2k+1 share block k.</param>
    /// <param name="pixelSeed">Per-pixel seed of the shuffles and scrambles.</param>
    public static float Sample(uint sampleIndex, uint dimension, uint pixelSeed)
    {
        uint block = dimension >> 1;
        uint shuffleSeed = Hash(pixelSeed ^ Hash(block * 0x68bc21ebu + 0xc7afe638u));
        uint index = OwenScramble(sampleIndex, shuffleSeed);

        uint v = (dimension & 1u) == 0u ? ReverseBits(index) : SobolValue(index, 1u);

        uint scrambleSeed = Hash(pixelSeed ^ Hash(dimension * 0xa511e9b3u + 0x736f6c34u));
        v = OwenScramble(v, scrambleSeed);
        return ToFloat01(v);
    }

    /// <summary>
    /// Sobol value = XOR over set bits k of <paramref name="index"/> of
    /// the k-th direction vector for the given dimension. Direction
    /// vectors are MSB-aligned 32-bit words (V_0 has its leading 1 at
    /// bit 31), so the result is an MSB-ordered "Sobol fraction" with
    /// 32 bits of resolution.
    /// </summary>
    private static uint SobolValue(uint index, uint dimension)
    {
        // Dimension 0's direction matrix is the identity (V_k has its single bit
        // at position 31-k), so the XOR over set bits collapses exactly to
        // ReverseBits(index) — the van der Corput sequence. Short-circuit it:
        // dim 0 is one of the two most-used dimensions (camera jitter), drawn on
        // every sample, so skipping the per-bit loop is a measurable win.
        if (dimension == 0u)
            return ReverseBits(index);

        uint v = 0u;
        int k = 0;
        while (index != 0u)
        {
            if ((index & 1u) != 0u) v ^= Dim1Directions[k];
            index >>= 1;
            k++;
        }
        return v;
    }

    // Dim 0's direction vectors (V_k = 1 << (31 - k)) are no longer tabulated:
    // the XOR over set bits collapses to ReverseBits(index), handled directly in
    // SobolValue.

    /// <summary>
    /// Dim 1 direction vectors: Sobol's primitive polynomial p(x) =
    /// x² + x + 1 with initial direction number m_1 = 1. The values
    /// are the recurrence-generated table tabulated as 32-bit words
    /// in PBRT-v4 (sobolmatrices.cpp) and Cycles
    /// (sobol/sobol.h). Together with dim 0 they form a (0, 2, 2)-net
    /// at every power-of-two prefix — every (2^a × 2^b) grid is hit
    /// the right number of times for a + b ≤ k where N = 2^k.
    /// </summary>
    private static readonly uint[] Dim1Directions =
    {
        0x80000000u, 0xc0000000u, 0xa0000000u, 0xf0000000u,
        0x88000000u, 0xcc000000u, 0xaa000000u, 0xff000000u,
        0x80800000u, 0xc0c00000u, 0xa0a00000u, 0xf0f00000u,
        0x88880000u, 0xcccc0000u, 0xaaaa0000u, 0xffff0000u,
        0x80008000u, 0xc000c000u, 0xa000a000u, 0xf000f000u,
        0x88008800u, 0xcc00cc00u, 0xaa00aa00u, 0xff00ff00u,
        0x80808080u, 0xc0c0c0c0u, 0xa0a0a0a0u, 0xf0f0f0f0u,
        0x88888888u, 0xccccccccu, 0xaaaaaaaau, 0xffffffffu,
    };

    /// <summary>
    /// Burley 2020 Owen scramble: bit-reverse, Laine-Karras
    /// permutation in LSB form, bit-reverse back. The bit-reversal
    /// flip turns the LSB-only avalanche of Laine-Karras into an
    /// MSB-only one, which is exactly the Owen-tree structure
    /// (each bit of the output depends only on bits ≥ k of the input).
    /// </summary>
    private static uint OwenScramble(uint v, uint seed)
    {
        v = ReverseBits(v);
        v = LaineKarrasPermutation(v, seed);
        v = ReverseBits(v);
        return v;
    }

    /// <summary>
    /// Laine-Karras 2011 four-round multiply-XOR permutation, the
    /// LSB-ordered building block underlying Burley's Owen scramble.
    /// Operates in-place on a 32-bit word: bit k of the output
    /// depends only on bits 0..k of the input (LSB-to-MSB avalanche).
    /// Magic constants from Burley's 2020 published implementation;
    /// the same constants ship in Cycles' sobol_burley.h.
    /// </summary>
    private static uint LaineKarrasPermutation(uint v, uint seed)
    {
        v += seed;
        v ^= v * 0x6c50b47cu;
        v ^= v * 0xb82f1e52u;
        v ^= v * 0xc7afe638u;
        v ^= v * 0x8d22f6e6u;
        return v;
    }

    /// <summary>
    /// Single-input mix (xx-hash style) used to decorrelate dimensions
    /// before they enter the scramble. Avalanche quality is sufficient
    /// for the 32-bit Owen seed — what matters is that adjacent
    /// dimensions and adjacent pixel seeds map to seeds with no
    /// detectable bit-level correlation.
    /// </summary>
    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    private static uint ReverseBits(uint v)
    {
        v = ((v & 0xaaaaaaaau) >> 1) | ((v & 0x55555555u) << 1);
        v = ((v & 0xccccccccu) >> 2) | ((v & 0x33333333u) << 2);
        v = ((v & 0xf0f0f0f0u) >> 4) | ((v & 0x0f0f0f0fu) << 4);
        v = ((v & 0xff00ff00u) >> 8) | ((v & 0x00ff00ffu) << 8);
        v = (v >> 16) | (v << 16);
        return v;
    }

    /// <summary>
    /// Maps a 32-bit integer to [0, 1). Uses 24-bit mantissa precision,
    /// matching float resolution — the bottom 8 bits are dropped to
    /// avoid the rounding cliff at exactly 1.0 that 32-bit conversion
    /// can hit on aggressive optimisers.
    /// </summary>
    private static float ToFloat01(uint v) => (v >> 8) * (1f / 16777216f);
}
