using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>
/// Prefilters the noisy AOV guide buffers (albedo, normal, normalised depth)
/// before they drive the main filter: each feature is NL-means filtered using
/// its OWN dual-buffer variance, cross-filtered (weights from one half applied
/// to the other) so the cleaned feature carries no self-correlated noise.
/// Features converge much faster than beauty, so a small window suffices.
/// </summary>
internal static class FeaturePrefilter
{
    private static readonly NlmParams Params = new(searchRadius: 5, patchRadius: 3, k: 1.0f);

    /// <summary>
    /// Returns the cleaned full-buffer feature obtained from the A/B halves.
    /// </summary>
    public static FrameBuffer Prefilter(FrameBuffer halfA, FrameBuffer halfB, int samplesA, int samplesB)
    {
        var mean = PlaneOps.Combine(halfA, halfB, samplesA, samplesB);
        var fullVar = VarianceEstimator.FullVariance(halfA, halfB, mean);
        var halfVar = VarianceEstimator.HalfFromFull(fullVar);

        var filteredA = NlMeansCore.Filter(guide: halfB, guideVar: halfVar, target: halfA, Params, features: null);
        var filteredB = NlMeansCore.Filter(guide: halfA, guideVar: halfVar, target: halfB, Params, features: null);
        return PlaneOps.Combine(filteredA, filteredB, samplesA, samplesB);
    }
}
