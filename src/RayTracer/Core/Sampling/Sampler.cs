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
        public uint Limit;      // first dimension past the current vertex's block
        public uint Vertex;     // path vertices opened so far in this sample
        public uint Overflow;   // draws past a block's budget (see OverflowBase)
        public bool Active;     // false = fall back to MathUtils PRNG
    }

    // ── Dimension layout per camera sample ──────────────────────────────────
    // [0, CameraDims): camera (pixel jitter, shutter time, lens).
    // Vertex k of the path: [CameraDims + k·VertexDims, … + VertexDims).
    // Draws past a block's budget (a long subsurface walk, many lights) go to
    // OverflowBase + n, never reused within the sample. Fixed blocks keep the
    // same decision on the same dimension across the samples of a pixel even
    // when an earlier vertex consumed a different number of draws, which is
    // what lets a low-discrepancy sequence stratify that decision.
    private const uint CameraDims = 8u;
    private const uint VertexDims = 64u;
    private const uint OverflowBase = 0x40000000u;
    // Per-thread context, stored in a [ThreadStatic] field rather than a
    // ThreadLocal<T> wrapper: this is read on every random draw (hundreds of
    // millions of times per render) and a [ThreadStatic] field access is
    // markedly cheaper than ThreadLocal<T>.Value. Lazily initialised per thread
    // via LocalCtx (a [ThreadStatic] initializer would only run on one thread).
    [ThreadStatic] private static Ctx? _ctx;
    private static Ctx LocalCtx => _ctx ??= new Ctx();

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
        var c = LocalCtx;
        c.PixelSeed   = pixelSeed;
        c.SampleIndex = sampleIndex;
        c.Dimension   = 0u;
        c.Limit       = CameraDims;
        c.Vertex      = 0u;
        c.Overflow    = 0u;
        c.Active      = true;
    }

    /// <summary>
    /// Opens the dimension block of the next path vertex. Called once per
    /// vertex of the path loop; no-op without an active Sobol context.
    /// </summary>
    public static void BeginVertex()
    {
        var c = LocalCtx;
        if (!c.Active) return;
        uint v = c.Vertex++;
        if (v >= (OverflowBase - CameraDims) / VertexDims)
        {
            // Absurdly deep path: everything goes to the overflow region.
            c.Dimension = c.Limit = 0u;
            return;
        }
        c.Dimension = CameraDims + v * VertexDims;
        c.Limit     = c.Dimension + VertexDims;
    }

    private static uint NextDimension(Ctx c)
        => c.Dimension < c.Limit ? c.Dimension++ : OverflowBase + c.Overflow++;

    /// <summary>
    /// Ends the current per-pixel-sample context. Subsequent calls to
    /// <see cref="Sample1D"/> fall back to the PRNG until the next
    /// <see cref="BeginPixelSample"/>. Safe to call from any thread and
    /// idempotent.
    /// </summary>
    public static void EndPixelSample()
    {
        var c = LocalCtx;
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
        var c = LocalCtx;
        if (!c.Active)
        {
            // Fast path: no active Sobol context (e.g. called from a unit
            // test or from a non-render thread). Drop through to the
            // thread-local PRNG via the host's existing path.
            return (float)MathUtils.Rng.NextDouble();
        }
        uint dim = NextDimension(c);
        return OwenSobol.Sample(c.SampleIndex, dim, c.PixelSeed);
    }

    /// <summary>
    /// 2D draw for a two-dimensional decision (a direction, a point on a
    /// light, a lens position). With Sobol it first aligns the dimension
    /// counter to an even dimension, so the two coordinates are the two halves
    /// of one padded Sobol block and are jointly stratified; two loose
    /// <see cref="Sample1D"/> calls are only stratified one by one.
    /// </summary>
    public static (float u, float v) Sample2D()
    {
        var c = LocalCtx;
        if (c.Active)
        {
            // Align to an even dimension, in the vertex block if both halves
            // fit, else in the overflow region.
            uint aligned = (c.Dimension + 1u) & ~1u;
            if (aligned + 2u <= c.Limit)
                c.Dimension = aligned;
            else
            {
                c.Dimension = c.Limit;
                c.Overflow = (c.Overflow + 1u) & ~1u;
            }
        }
        float u = Sample1D();
        float v = Sample1D();
        return (u, v);
    }
}
