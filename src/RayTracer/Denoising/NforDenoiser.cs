using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>
/// Denoiser entry point. Consumes the dual-buffer render capture
/// (<see cref="RenderBuffers"/>) and produces a denoised LINEAR beauty buffer
/// (tone mapping stays downstream, in the renderer's display transform).
///
/// Pipeline:
///   1. depth normalisation (sky sentinel → far plane, scale to [0,1]);
///   2. feature prefiltering (albedo/normal/depth, dual-buffer cross NL-means);
///   3. highlights far above the image's level set aside (added back
///      unfiltered at the end), beauty variance from the A/B halves;
///   4. main filter —
///      <see cref="DenoiserKind.Nlm"/>: joint NL-means, cross-filtered halves;
///      <see cref="DenoiserKind.Nfor"/>: NL-means-weighted first-order
///      regression on the prefiltered features, per-pixel MSE candidate
///      selection (see <see cref="NforRegression"/>).
/// </summary>
public static class NforDenoiser
{
    // Joint-filter / regression feature bandwidths (squared-difference scale).
    // Calibrated on the regression test scene; features are prefiltered, so
    // tight bandwidths are safe.
    private const float AlbedoInvBandwidth = 1f / 2e-3f;
    private const float NormalInvBandwidth = 1f / 1e-2f;
    private const float DepthInvBandwidth  = 1f / 2e-3f;

    public static FrameBuffer Denoise(RenderBuffers buffers, DenoiserOptions opts)
    {
        if (!buffers.HasAovs)
            throw new InvalidOperationException(
                "Denoising requires AOV capture (RenderCaptureOptions.Full).");

        int w = buffers.Width, h = buffers.Height;
        int nA = buffers.SamplesA, nB = buffers.SamplesB;

        // ── 1. Depth → normalised feature planes ────────────────────────────
        var (depthNa, depthNb) = NormaliseDepth(buffers);

        // ── 2. Clean guide features ─────────────────────────────────────────
        var albedo = FeaturePrefilter.Prefilter(buffers.AlbedoA!, buffers.AlbedoB!, nA, nB);
        var normal = FeaturePrefilter.Prefilter(buffers.NormalA!, buffers.NormalB!, nA, nB);
        var depth  = FeaturePrefilter.Prefilter(depthNa, depthNb, nA, nB);

        // ── 3. Highlights set aside, beauty variance ────────────────────────
        var (beautyA, beautyB, excess) = SplitHighlights(buffers);
        var beautyMean = PlaneOps.Combine(beautyA, beautyB, nA, nB);
        var fullVar = VarianceEstimator.FullVariance(beautyA, beautyB, beautyMean);
        var halfVar = VarianceEstimator.HalfFromFull(fullVar);

        // ── 4. Main filter ──────────────────────────────────────────────────
        var result = opts.Kind switch
        {
            DenoiserKind.Nlm => JointNlm(buffers, beautyA, beautyB, halfVar, albedo, normal, depth, opts),
            DenoiserKind.Nfor => NforRegression.Run(buffers, beautyA, beautyB, halfVar, albedo, normal, depth, opts),
            _ => throw new ArgumentOutOfRangeException(nameof(opts), $"Unexpected denoiser kind {opts.Kind}."),
        };
        if (excess != null)
        {
            var r = result.Data;
            var e = excess.Data;
            for (int i = 0; i < r.Length; i++) r[i] += e[i];
        }
        return result;
    }

    /// <summary>
    /// Highlights above this multiple of the image's 99th-percentile luminance
    /// are not filtered (<see cref="SplitHighlights"/>). 8 (was 32 until
    /// Phase 9): the out-of-focus lights of <c>showcases/camera-physical.yaml</c>
    /// came out as squares, relMSE 0.51 against the noisy 0.055 at 16 spp;
    /// 0.11 at 8, and the Cornell box 0.0012 instead of 0.0021. Lower
    /// factors cost the Cornell box at 4 spp. Bokeh still loses to the noisy
    /// image with NFOR: OIDN reconstructs it (0.014).
    /// </summary>
    internal const float HighlightFactor = 8f;

    /// <summary>
    /// Sets aside what the brightest pixels have above a threshold, so the
    /// filter sees them clamped and the excess is added back unfiltered: the
    /// image keeps its energy, and the highlight its size. A pixel thousands
    /// of times brighter than its neighbours (the sun in a mirror, a small
    /// light) otherwise dominates every regression window that holds it, and
    /// the denoiser spreads it into a square with a dark rim (PIANO-REVIEW.md
    /// Phase 6). The clamp keeps the colour's hue (it scales by luminance).
    /// Returns the inputs unchanged and a null excess when nothing exceeds it.
    /// </summary>
    internal static (FrameBuffer A, FrameBuffer B, FrameBuffer? Excess) SplitHighlights(RenderBuffers buffers)
    {
        int w = buffers.Width, h = buffers.Height, n = w * h;
        int nA = buffers.SamplesA, nB = buffers.SamplesB;
        var mean = PlaneOps.Combine(buffers.BeautyA, buffers.BeautyB, nA, nB);
        var lum = new float[n];
        var m = mean.Data;
        for (int i = 0; i < n; i++) lum[i] = Luminance(m[i], m[n + i], m[2 * n + i]);
        float p99 = PlaneOps.PercentileOfNonNegative(lum, 0.99f, fallback: 0f);
        if (!(p99 > 0f)) return (buffers.BeautyA, buffers.BeautyB, null);
        float limit = HighlightFactor * p99;

        bool any = false;
        foreach (var half in new[] { buffers.BeautyA, buffers.BeautyB })
        {
            var d = half.Data;
            for (int i = 0; i < n && !any; i++)
                any = Luminance(d[i], d[n + i], d[2 * n + i]) > limit;
        }
        if (!any) return (buffers.BeautyA, buffers.BeautyB, null);

        FrameBuffer Clamp(FrameBuffer src, FrameBuffer excess)
        {
            var dst = new FrameBuffer(w, h, 3);
            Array.Copy(src.Data, dst.Data, src.Data.Length);
            var d = dst.Data; var x = excess.Data;
            for (int i = 0; i < n; i++)
            {
                float l = Luminance(d[i], d[n + i], d[2 * n + i]);
                if (!(l > limit)) continue;
                float k = limit / l;
                for (int c = 0; c < 3; c++)
                {
                    int p = c * n + i;
                    x[p] = d[p] * (1f - k);
                    d[p] *= k;
                }
            }
            return dst;
        }

        var excessA = new FrameBuffer(w, h, 3);
        var excessB = new FrameBuffer(w, h, 3);
        var a = Clamp(buffers.BeautyA, excessA);
        var b = Clamp(buffers.BeautyB, excessB);
        return (a, b, PlaneOps.Combine(excessA, excessB, nA, nB));
    }

    private static float Luminance(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    /// <summary>
    /// Joint NL-means: weights from the colour patch distance of one half
    /// (variance-cancelled) plus pointwise prefiltered-feature distances,
    /// cross-applied to the other half; the two filtered halves recombine
    /// sample-weighted.
    /// </summary>
    private static FrameBuffer JointNlm(RenderBuffers buffers, FrameBuffer beautyA, FrameBuffer beautyB, FrameBuffer halfVar,
                                        FrameBuffer albedo, FrameBuffer normal, FrameBuffer depth,
                                        DenoiserOptions opts)
    {
        int w = buffers.Width, h = buffers.Height;
        var guides = FeatureGuides.Build(w, h,
            (albedo, AlbedoInvBandwidth),
            (normal, NormalInvBandwidth),
            (depth, DepthInvBandwidth));

        float k = opts.Quality == DenoiseQuality.High ? 1.0f : 0.7f;
        var prm = new NlmParams(opts.SearchRadius, DenoiserOptions.PatchRadius, k);

        var filteredA = NlMeansCore.Filter(
            guide: beautyB, guideVar: halfVar, target: beautyA, prm, guides);
        var filteredB = NlMeansCore.Filter(
            guide: beautyA, guideVar: halfVar, target: beautyB, prm, guides);
        return PlaneOps.Combine(filteredA, filteredB, buffers.SamplesA, buffers.SamplesB);
    }

    /// <summary>
    /// Maps the raw depth halves (world distance, −1 = sky sentinel) onto a
    /// resolution- and scene-scale-independent [0,1] feature: distances are
    /// clamped to a far plane at 1.05 × the 99th percentile of finite depth
    /// and divided by it; sky pixels sit exactly at the far plane.
    /// </summary>
    internal static (FrameBuffer A, FrameBuffer B) NormaliseDepth(RenderBuffers buffers)
    {
        var combined = buffers.CombineDepthHalves();
        float p99 = PlaneOps.PercentileOfNonNegative(combined.Data, 0.99f, fallback: 1f);
        float far = MathF.Max(1.05f * p99, 1e-6f);
        float invFar = 1f / far;

        FrameBuffer Normalise(FrameBuffer src)
        {
            var dst = new FrameBuffer(src.Width, src.Height, 1);
            var s = src.Data; var d = dst.Data;
            for (int i = 0; i < d.Length; i++)
            {
                float v = s[i];
                d[i] = v < 0f ? 1f : MathF.Min(v * invFar, 1f);
            }
            return dst;
        }

        return (Normalise(buffers.DepthA!), Normalise(buffers.DepthB!));
    }
}
