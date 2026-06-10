using System.Linq;
using RayTracer.Core.Sampling;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Sanity checks for the Burley 2020 Owen-scrambled Sobol sampler.
/// The full convergence claim (2-5× over PRNG) is impossible to assert
/// in a unit test — these tests instead pin the structural properties
/// the sampler must preserve to be a valid LDS replacement: range,
/// per-pixel decorrelation, low-discrepancy stratification on the unit
/// square, and graceful fall-through to the PRNG when no per-pixel
/// context is open.
///
/// <para><b>Why these tests drive <see cref="OwenSobol.Sample"/> directly
/// rather than <c>Sampler.SetKind(Sobol)</c> + <c>Sample1D()</c>:</b>
/// <c>Sampler.SetKind</c> writes a <i>process-global</i> mode, and xUnit
/// runs distinct test classes in parallel. Several render-test classes
/// defensively call <c>SetKind(Prng)</c>; when one of them fired while a
/// Sobol-mode test here was between <c>SetKind(Sobol)</c> and its draws,
/// <c>BeginPixelSample</c> silently no-opped and the draws fell back to
/// the PRNG — making the exact (0,1)-net assertions fail sporadically in
/// CI (same class of race the <c>SceneLoader</c> collection exists for).
/// <see cref="OwenSobol.Sample"/> is a pure function of
/// (sampleIndex, dimension, pixelSeed) — exactly what <c>Sample1D</c>
/// evaluates per draw — so testing it directly pins the same structure
/// with zero shared state. With no test ever setting Sobol globally, every
/// remaining <c>SetKind(Prng)</c> call in the suite writes the default and
/// the race is gone suite-wide. The thin <c>Sampler</c> wrapper (context +
/// dimension counter) keeps a race-tolerant plumbing test below.</para>
/// </summary>
public class SamplerTests
{
    /// <summary>
    /// All draws must lie strictly within [0, 1). 1.0f exactly would
    /// translate to a stratum index of N when bucketed into N bins,
    /// silently dropping the sample on the integration grid. A regression
    /// here would corrupt every rendered pixel one sample in 4 billion,
    /// which is a nightmare to trace at runtime — pin it now.
    /// </summary>
    [Fact]
    public void Sobol_DrawsStayInUnitInterval()
    {
        for (uint pixel = 0; pixel < 16; pixel++)
        {
            for (uint sample = 0; sample < 256; sample++)
            {
                for (uint dim = 0; dim < 32; dim++)
                {
                    float u = OwenSobol.Sample(sample, dim, pixel * 0xdeadbeefu);
                    Assert.InRange(u, 0f, 1f - 1e-7f);
                }
            }
        }
    }

    /// <summary>
    /// Per-pixel scramble means two pixels with different seeds must walk
    /// independent sequences — without this the Sobol prefix would repeat
    /// pixel-to-pixel and produce visible Moiré. Test: at the same
    /// (sampleIndex, dimension), different pixel seeds must produce
    /// different draws (with overwhelming probability).
    /// </summary>
    [Fact]
    public void Sobol_PixelSeedDecorrelatesPixels()
    {
        int collisions = 0;
        for (uint sample = 0; sample < 64; sample++)
        {
            float a = OwenSobol.Sample(sample, 0u, seedA);
            float b = OwenSobol.Sample(sample, 0u, seedB);
            if (a == b) collisions++;
        }
        Assert.True(collisions <= 1,
            $"two different pixel seeds collided on {collisions}/64 samples — Owen scramble seed is not effective");
    }
    private const uint seedA = 0xa5a5a5a5u;
    private const uint seedB = 0x5a5a5a5au;

    /// <summary>
    /// 1D stratification (per dimension): Owen-scrambled van der Corput
    /// on N = 2^k samples must give a perfect (0, 1)-net in 1D — every
    /// one of the 2^k equal-width strata is hit exactly once for every
    /// dimension independently.
    ///
    /// The full joint (0, 1, 2)-net property in 2D would require true
    /// Sobol direction matrices (per-dimension primitive polynomials).
    /// Burley-Sobol sacrifices the joint net for a 1 KB direction-table
    /// budget and decorrelates dimensions through Owen scrambling
    /// instead, which is what every modern path tracer actually ships.
    /// The 1D guarantee per dimension is the structural invariant the
    /// renderer relies on.
    /// </summary>
    [Fact]
    public void Sobol_StratifiesEachDimension1D()
    {
        const int N = 16;
        int[] uHits = new int[N];
        int[] vHits = new int[N];
        float[] us = new float[N];
        float[] vs = new float[N];
        for (uint s = 0; s < N; s++)
        {
            float u = OwenSobol.Sample(s, 0u, 0u);
            float v = OwenSobol.Sample(s, 1u, 0u);
            us[s] = u; vs[s] = v;
            uHits[System.Math.Min((int)(u * N), N - 1)]++;
            vHits[System.Math.Min((int)(v * N), N - 1)]++;
        }
        string uStr = string.Join(", ", us.Select(x => x.ToString("F3")));
        string vStr = string.Join(", ", vs.Select(x => x.ToString("F3")));
        for (int i = 0; i < N; i++)
        {
            Assert.True(uHits[i] == 1, $"dim-0 stratum {i} hit {uHits[i]} times — values: [{uStr}]");
            Assert.True(vHits[i] == 1, $"dim-1 stratum {i} hit {vHits[i]} times — values: [{vStr}]");
        }
    }

    /// <summary>
    /// 2D occupancy: Burley-Sobol decorrelates dimensions through
    /// independent Owen scrambles, so the joint distribution behaves
    /// like the product of two independent stratified marginals — much
    /// better than independent PRNG draws. With 64 samples in an 8 × 8
    /// grid (so the average is exactly 1 per cell) Sobol typically hits
    /// 50-60 distinct cells, vs. Poisson PRNG which averages ~40. We
    /// require at least 45 distinct cells, a margin that Sobol clears
    /// easily and PRNG fails most of the time.
    /// </summary>
    [Fact]
    public void Sobol_StratifiesUnitSquare_BetterThanPrng()
    {
        const int N = 64;
        const int side = 8;
        bool[,] occupied = new bool[side, side];
        for (uint s = 0; s < N; s++)
        {
            float u = OwenSobol.Sample(s, 0u, 0u);
            float v = OwenSobol.Sample(s, 1u, 0u);
            int cu = System.Math.Min((int)(u * side), side - 1);
            int cv = System.Math.Min((int)(v * side), side - 1);
            occupied[cu, cv] = true;
        }
        int distinct = 0;
        for (int x = 0; x < side; x++)
            for (int y = 0; y < side; y++)
                if (occupied[x, y]) distinct++;
        Assert.True(distinct >= 45,
            $"Sobol covered only {distinct}/64 cells — independent-dimension Owen scrambling should reach ≥ 45 well above PRNG's ~40 expected cells");
    }

    /// <summary>
    /// 1D stratification at high dimensions. The Joe-Kuo direction
    /// matrices only cover dim 0 and 1; every higher dim falls through
    /// to the Burley 2020 hash-based path. That path used to call a
    /// bare Laine-Karras permutation on the sample index where Burley
    /// requires a full nested-uniform-scramble, which silently dropped
    /// the (0,1)-net guarantee — Sobol degenerated to PRNG-quality at
    /// dim ≥ 2 and ended up *noisier* than plain PRNG on every
    /// indirect-dominant scene (the Cornell box at -s 64 -d 8 was the
    /// failure mode that surfaced this). Pin the high-dim invariant
    /// so a future scrambler regression can't silently re-introduce
    /// the same bug.
    /// </summary>
    [Fact]
    public void Sobol_StratifiesEachDimension1D_AtHighDims()
    {
        const int N = 64;
        for (uint targetDim = 2; targetDim < 16; targetDim++)
        {
            int[] hits = new int[N];
            for (uint s = 0; s < N; s++)
            {
                float v = OwenSobol.Sample(s, targetDim, 0u);
                hits[System.Math.Min((int)(v * N), N - 1)]++;
            }
            for (int i = 0; i < N; i++)
                Assert.True(hits[i] == 1,
                    $"dim {targetDim} stratum {i} hit {hits[i]} times — Burley high-dim shuffle is no longer a (0,1)-net");
        }
    }

    /// <summary>
    /// Plumbing check for the thin <see cref="Sampler"/> wrapper: with a
    /// Sobol context open, successive <see cref="Sampler.Sample1D"/> calls
    /// must stay in [0, 1) and advance the dimension counter (i.e. not
    /// return one frozen value). Deliberately tolerant of the global-kind
    /// race documented in the class header: if a parallel test class resets
    /// the kind to PRNG mid-test, the draws fall back to System.Random and
    /// every assertion below still holds — the exact Sobol *structure* is
    /// pinned race-free by the OwenSobol tests above.
    /// </summary>
    [Fact]
    public void SamplerWrapper_RoutesDrawsAndAdvancesDimensions()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        try
        {
            var draws = new float[8];
            Sampler.BeginPixelSample(0x12345678u, 7u);
            for (int d = 0; d < draws.Length; d++)
            {
                draws[d] = Sampler.Sample1D();
                Assert.InRange(draws[d], 0f, 1f - 1e-7f);
            }
            Sampler.EndPixelSample();

            Assert.True(draws.Distinct().Count() > 1,
                "Sample1D returned the same value for every dimension — the dimension counter is not advancing");
        }
        finally { Sampler.SetKind(SamplerKind.Prng); }
    }

    /// <summary>
    /// PRNG mode and "Sobol mode without an open context" must both
    /// route through System.Random — the legacy fallback that lets unit
    /// tests, the scene loader, and any non-render callers keep working.
    /// Test: with PRNG kind set, 1000 draws stay strictly in [0, 1).
    /// </summary>
    [Fact]
    public void PrngFallback_StaysValid()
    {
        Sampler.SetKind(SamplerKind.Prng);
        for (int i = 0; i < 1000; i++)
        {
            float u = Sampler.Sample1D();
            Assert.InRange(u, 0f, 1f - 1e-7f);
        }
    }
}
