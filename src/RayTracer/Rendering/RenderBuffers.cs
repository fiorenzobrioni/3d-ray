namespace RayTracer.Rendering;

/// <summary>
/// Selects which auxiliary buffers <see cref="Renderer.Render(int,int,RenderCaptureOptions)"/>
/// captures alongside the tone-mapped pixels. <see cref="None"/> (the default)
/// keeps the renderer on the exact legacy code path — output is bit-identical
/// to the two-argument <c>Render</c> overload.
/// </summary>
public readonly struct RenderCaptureOptions
{
    /// <summary>Capture linear-HDR beauty plus the half-sample
    /// (A/B) splits the denoiser's dual-buffer variance estimate needs.</summary>
    public bool CaptureBeautyHalves { get; init; }

    /// <summary>Capture first-non-delta-hit albedo/normal/depth AOVs
    /// (also split into A/B halves).</summary>
    public bool CaptureAovs { get; init; }

    public bool Any => CaptureBeautyHalves || CaptureAovs;

    public static RenderCaptureOptions None => default;
    public static RenderCaptureOptions Full => new() { CaptureBeautyHalves = true, CaptureAovs = true };
}

/// <summary>
/// Result of a capturing render: the tone-mapped display pixels (identical to
/// the legacy <c>Render</c> output) plus the optional linear-HDR buffer set.
/// </summary>
public sealed class RenderResult
{
    public required System.Numerics.Vector3[,] Pixels { get; init; }
    public RenderBuffers? Buffers { get; init; }
}

/// <summary>
/// Linear-HDR frame data captured during rendering for denoising, HDR (PFM)
/// output, and — later — adaptive sampling.
///
/// Dual-buffer convention: every captured quantity is split into two halves,
/// which store MEANS, not sums; with n samples A holds ⌈n/2⌉ samples and B
/// ⌊n/2⌋ (see <see cref="SamplesA"/>/<see cref="SamplesB"/>). The per-pixel
/// variance of the full-buffer mean is then estimated as ((Ā−B̄)/2)². With
/// the Sobol sampler A is the first half of the pixel's sequence and B the
/// second: each is a stratified net, and the estimate is within 8-14% of the
/// true variance, on the conservative side (DualBufferVarianceTests). The
/// even and odd samples, the earlier split, fill each other's gaps and
/// overstated it 2.4-3 times. With <c>--sampler prng</c> the halves are the
/// even and odd cells of the jittered grid (overstated 1.2-1.4 times).
/// Adaptive sampling stops pixels at different counts: <see cref="HalfCountA"/>
/// then holds each pixel's A count (B = <see cref="SampleCount"/> − A).
///
/// Depth uses world-space ray distance (camera rays are not normalised, so
/// the capture multiplies rec.T by |direction|); pixels whose every sample
/// missed all geometry carry the sentinel −1.
/// </summary>
public sealed class RenderBuffers
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Per-pixel sample count (uniform without adaptive sampling).</summary>
    public int[] SampleCount { get; }

    /// <summary>
    /// Per-pixel samples in half A when the counts vary from pixel to pixel
    /// (adaptive sampling), null when every pixel has
    /// <see cref="SamplesA"/>/<see cref="SamplesB"/>.
    /// </summary>
    public int[]? HalfCountA { get; init; }

    /// <summary>Samples in the A half (the first of the sequence): ⌈n/2⌉.</summary>
    public int SamplesA { get; }

    /// <summary>Samples in the B half: ⌊n/2⌋. Zero when n == 1 —
    /// consumers must treat B as a copy of A in that case.</summary>
    public int SamplesB { get; }

    /// <summary>Full linear-HDR beauty mean (pre-exposure, pre-tonemap),
    /// accumulated by the unmodified legacy sample loop.</summary>
    public FrameBuffer Beauty { get; }

    public FrameBuffer BeautyA { get; }
    public FrameBuffer BeautyB { get; }

    public FrameBuffer? AlbedoA { get; }
    public FrameBuffer? AlbedoB { get; }
    public FrameBuffer? NormalA { get; }
    public FrameBuffer? NormalB { get; }
    public FrameBuffer? DepthA { get; }
    public FrameBuffer? DepthB { get; }

    public bool HasAovs => AlbedoA != null;

    public RenderBuffers(int width, int height, int samplesPerPixel, bool captureAovs)
    {
        Width = width;
        Height = height;
        SamplesA = (samplesPerPixel + 1) / 2;
        SamplesB = samplesPerPixel / 2;
        SampleCount = new int[width * height];

        Beauty  = new FrameBuffer(width, height, 3);
        BeautyA = new FrameBuffer(width, height, 3);
        BeautyB = new FrameBuffer(width, height, 3);

        if (captureAovs)
        {
            AlbedoA = new FrameBuffer(width, height, 3);
            AlbedoB = new FrameBuffer(width, height, 3);
            NormalA = new FrameBuffer(width, height, 3);
            NormalB = new FrameBuffer(width, height, 3);
            DepthA  = new FrameBuffer(width, height, 1);
            DepthB  = new FrameBuffer(width, height, 1);
        }
    }

    /// <summary>Full-buffer mean of an A/B pair: (nA·Ā + nB·B̄) / n.</summary>
    public FrameBuffer CombineHalves(FrameBuffer a, FrameBuffer b)
    {
        var combined = new FrameBuffer(Width, Height, a.Channels);
        if (HalfCountA != null)
        {
            int np = Width * Height;
            for (int c = 0; c < a.Channels; c++)
            for (int i = 0; i < np; i++)
            {
                var (ha, hb) = HalfWeights(i);
                int p = c * np + i;
                combined.Data[p] = ha * a.Data[p] + hb * b.Data[p];
            }
            return combined;
        }
        int n = SamplesA + SamplesB;
        if (SamplesB == 0)
        {
            Array.Copy(a.Data, combined.Data, a.Data.Length);
            return combined;
        }
        float wa = (float)SamplesA / n;
        float wb = (float)SamplesB / n;
        var src = a.Data; var srcB = b.Data; var dst = combined.Data;
        for (int i = 0; i < dst.Length; i++)
            dst[i] = wa * src[i] + wb * srcB[i];
        return combined;
    }

    /// <summary>Weights of the two half means in pixel <paramref name="i"/>'s full mean.</summary>
    public (float A, float B) HalfWeights(int i)
    {
        int nA = HalfCountA?[i] ?? SamplesA;
        int n = HalfCountA != null ? SampleCount[i] : SamplesA + SamplesB;
        if (n <= nA) return (1f, 0f);
        return ((float)nA / n, (float)(n - nA) / n);
    }

    /// <summary>
    /// Sentinel-aware combination of the depth halves: a half that never hit
    /// geometry (−1) defers to the other; pixels where both halves missed keep
    /// the −1 "no hit" sentinel.
    /// </summary>
    public FrameBuffer CombineDepthHalves()
    {
        var a = DepthA ?? throw new InvalidOperationException("Depth AOV was not captured.");
        var b = DepthB!;
        var combined = new FrameBuffer(Width, Height, 1);
        var da = a.Data; var db = b.Data; var dst = combined.Data;
        for (int i = 0; i < dst.Length; i++)
        {
            float va = da[i], vb = db[i];
            var (wa, wb) = HalfWeights(i);
            dst[i] = (va < 0f, vb < 0f) switch
            {
                (true, true)   => -1f,
                (true, false)  => vb,
                (false, true)  => va,
                (false, false) => wb == 0f ? va : wa * va + wb * vb,
            };
        }
        return combined;
    }

    /// <summary>
    /// Raw (unsmoothed) dual-buffer variance of the full beauty mean,
    /// ((Ā−B̄)/2)² per channel (weighted by the half counts when they differ). The denoiser smooths and floors this; the
    /// <c>--aov variance</c> output writes it as-is.
    /// </summary>
    public FrameBuffer RawBeautyVariance()
    {
        var variance = new FrameBuffer(Width, Height, 3);
        var da = BeautyA.Data; var db = BeautyB.Data; var dst = variance.Data;
        int np = Width * Height;
        for (int i = 0; i < dst.Length; i++)
        {
            // Var[full] = (Ā − B̄)² · nA·nB / n²: (Ā − B̄)²/4 for equal halves.
            var (wa, wb) = HalfWeights(i % np);
            float d = da[i] - db[i];
            dst[i] = wa * wb * d * d;
        }
        return variance;
    }
}
