namespace RayTracer.Core.Sampling;

/// <summary>
/// Sampler mode selector. Kept flat (no ISampler interface, no virtual
/// dispatch on the hot path) because the mode is a process-wide choice
/// made once per render and the renderer calls <see cref="Sample1D"/>
/// hundreds of millions of times — devirtualisation matters.
/// </summary>
public enum SamplerKind
{
    /// <summary>Thread-local System.Random — the legacy sampler.</summary>
    Prng,
    /// <summary>Burley 2020 hash-based Owen-scrambled Sobol — 2-5× faster
    /// convergence per spp on typical path-traced dimensions.</summary>
    Sobol,
}

/// <summary>
/// Thread-local low-discrepancy sampler driver. Wraps the static
/// <see cref="OwenSobol"/> machinery in a dimension counter so legacy
/// call sites that only expose <c>MathUtils.RandomFloat()</c> (every
/// BSDF scatter, every light sample, every volumetric step) pick up
/// Sobol automatically when the renderer has initialised the sampler
/// for the current pixel/sample.
///
/// Contract for the renderer:
///   1. Call <see cref="SetKind"/> once at startup to pick Sobol vs PRNG.
///   2. At the start of each per-pixel sample, call
///      <see cref="BeginPixelSample"/> with the pixel hash and sample
///      index — this resets the per-sample dimension counter.
///   3. Call <see cref="Sample1D"/> / <see cref="Sample2D"/> (or let
///      <c>MathUtils.RandomFloat</c> do it transparently) for each
///      random draw on the path.
/// </summary>
public static class Sampler
{
    // Global sampler mode — set once from the CLI before rendering starts.
    // Reads on the hot path are lock-free; writes happen exactly once.
    private static SamplerKind _kind = SamplerKind.Prng;

    // Per-thread context. Holds the current pixel seed, sample index,
    // and an ever-incrementing dimension counter so successive Sample1D
    // calls draw from different dimensions.
    private sealed class Ctx
    {
        public uint PixelSeed;
        public uint SampleIndex;
        public uint Dimension;
        public bool Active;   // false = fall back to MathUtils PRNG
    }
    private static readonly ThreadLocal<Ctx> _ctx = new(() => new Ctx());

    /// <summary>
    /// Installs the chosen sampler mode for the rest of the process.
    /// Must be called before the render loop starts; calling it
    /// mid-render produces undefined per-thread behaviour.
    /// </summary>
    public static void SetKind(SamplerKind kind) => _kind = kind;

    /// <summary>Currently active sampler mode.</summary>
    public static SamplerKind Kind => _kind;

    /// <summary>
    /// Opens a new per-pixel-sample dimension slot. The pixel seed is
    /// combined with the process-global sampler kind so PRNG and Sobol
    /// renders of the same scene don't collide on RNG state; the sample
    /// index is the 0-based count within the pixel.
    /// </summary>
    public static void BeginPixelSample(uint pixelSeed, uint sampleIndex)
    {
        if (_kind != SamplerKind.Sobol) return;
        var c = _ctx.Value!;
        c.PixelSeed   = pixelSeed;
        c.SampleIndex = sampleIndex;
        c.Dimension   = 0u;
        c.Active      = true;
    }

    /// <summary>
    /// Ends the current per-pixel-sample context. Subsequent calls to
    /// <see cref="Sample1D"/> fall back to the PRNG until the next
    /// <see cref="BeginPixelSample"/>. Safe to call from any thread and
    /// idempotent.
    /// </summary>
    public static void EndPixelSample()
    {
        var c = _ctx.Value!;
        c.Active = false;
    }

    /// <summary>
    /// Returns the next low-discrepancy draw in [0, 1). When Sobol is
    /// active, each call advances the dimension counter so successive
    /// draws are mutually decorrelated by Owen scrambling; when PRNG is
    /// active or no per-pixel context is open, defers to System.Random.
    ///
    /// Intentionally non-thread-safe — each thread owns its own Ctx
    /// and the renderer pins one pixel per thread at a time.
    /// </summary>
    public static float Sample1D()
    {
        var c = _ctx.Value!;
        if (!c.Active)
        {
            // Fast path: no active Sobol context (e.g. called from a unit
            // test or from a non-render thread). Drop through to the
            // thread-local PRNG via the host's existing path.
            return (float)MathUtils.Rng.NextDouble();
        }
        uint dim = c.Dimension++;
        return OwenSobol.Sample(c.SampleIndex, dim, c.PixelSeed);
    }

    /// <summary>
    /// 2D draw — consumes two dimensions atomically. Kept as its own
    /// entry so future stratified-per-pair samplers (e.g. true Sobol
    /// (2D) with coordinated direction matrices) can plug in without
    /// touching call sites.
    /// </summary>
    public static (float u, float v) Sample2D()
    {
        float u = Sample1D();
        float v = Sample1D();
        return (u, v);
    }
}
