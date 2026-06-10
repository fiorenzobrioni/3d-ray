namespace RayTracer.Rendering;

/// <summary>
/// Selects which auxiliary buffers <see cref="Renderer.Render(int,int,RenderCaptureOptions)"/>
/// captures alongside the tone-mapped pixels. <see cref="None"/> (the default)
/// keeps the renderer on the exact legacy code path — output is bit-identical
/// to the two-argument <c>Render</c> overload.
/// </summary>
public readonly struct RenderCaptureOptions
{
    /// <summary>Capture linear-HDR beauty plus the even/odd half-sample
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
/// Dual-buffer convention: every captured quantity is split into two halves by
/// sample-index parity (A = even samples, B = odd). The halves store MEANS,
/// not sums; with n samples A holds ⌈n/2⌉ samples and B ⌊n/2⌋ (see
/// <see cref="SamplesA"/>/<see cref="SamplesB"/>). The unbiased per-pixel
/// variance of the full-buffer mean is then estimated as ((Ā−B̄)/2)².
/// With the Sobol sampler the halves are mildly correlated (the even/odd
/// subsequences of one Owen-scrambled sequence), making the estimate slightly
/// optimistic; with <c>--sampler prng</c> they are independent.
///
/// Depth uses world-space ray distance (camera rays are not normalised, so
/// the capture multiplies rec.T by |direction|); pixels whose every sample
/// missed all geometry carry the sentinel −1.
/// </summary>
public sealed class RenderBuffers
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Per-pixel sample count (uniform today; the adaptive-sampling
    /// hook point).</summary>
    public int[] SampleCount { get; }

    /// <summary>Samples in the A (even-index) half: ⌈n/2⌉.</summary>
    public int SamplesA { get; }

    /// <summary>Samples in the B (odd-index) half: ⌊n/2⌋. Zero when n == 1 —
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
        int n = SamplesA + SamplesB;
        float wa = n > 0 ? (float)SamplesA / n : 1f;
        float wb = 1f - wa;
        var da = a.Data; var db = b.Data; var dst = combined.Data;
        for (int i = 0; i < dst.Length; i++)
        {
            float va = da[i], vb = db[i];
            dst[i] = (va < 0f, vb < 0f) switch
            {
                (true, true)   => -1f,
                (true, false)  => vb,
                (false, true)  => va,
                (false, false) => SamplesB == 0 ? va : wa * va + wb * vb,
            };
        }
        return combined;
    }

    /// <summary>
    /// Raw (unsmoothed) dual-buffer variance of the full beauty mean,
    /// ((Ā−B̄)/2)² per channel. The denoiser smooths and floors this; the
    /// <c>--aov variance</c> output writes it as-is.
    /// </summary>
    public FrameBuffer RawBeautyVariance()
    {
        var variance = new FrameBuffer(Width, Height, 3);
        var da = BeautyA.Data; var db = BeautyB.Data; var dst = variance.Data;
        for (int i = 0; i < dst.Length; i++)
        {
            float d = 0.5f * (da[i] - db[i]);
            dst[i] = d * d;
        }
        return variance;
    }
}
