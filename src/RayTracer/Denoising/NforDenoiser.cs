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
///   3. beauty variance estimation from the A/B halves;
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

        // ── 3. Beauty variance ──────────────────────────────────────────────
        var beautyMean = PlaneOps.Combine(buffers.BeautyA, buffers.BeautyB, nA, nB);
        var fullVar = VarianceEstimator.FullVariance(buffers.BeautyA, buffers.BeautyB, beautyMean);
        var halfVar = VarianceEstimator.HalfFromFull(fullVar);

        // ── 4. Main filter ──────────────────────────────────────────────────
        return opts.Kind switch
        {
            DenoiserKind.Nlm => JointNlm(buffers, halfVar, albedo, normal, depth, opts),
            DenoiserKind.Nfor => NforRegression.Run(buffers, halfVar, albedo, normal, depth, opts),
            _ => throw new ArgumentOutOfRangeException(nameof(opts), $"Unexpected denoiser kind {opts.Kind}."),
        };
    }

    /// <summary>
    /// Joint NL-means: weights from the colour patch distance of one half
    /// (variance-cancelled) plus pointwise prefiltered-feature distances,
    /// cross-applied to the other half; the two filtered halves recombine
    /// sample-weighted.
    /// </summary>
    private static FrameBuffer JointNlm(RenderBuffers buffers, FrameBuffer halfVar,
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
            guide: buffers.BeautyB, guideVar: halfVar, target: buffers.BeautyA, prm, guides);
        var filteredB = NlMeansCore.Filter(
            guide: buffers.BeautyA, guideVar: halfVar, target: buffers.BeautyB, prm, guides);
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
