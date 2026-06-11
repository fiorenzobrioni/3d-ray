using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>
/// Dual-buffer variance estimation. The render loop splits samples into
/// even/odd halves A/B; for the full-buffer mean the unbiased per-pixel
/// estimate is Var[mean] ≈ ((Ā−B̄)/2)². The raw estimate is χ²₁-distributed
/// (wildly noisy), so it is stabilised with a 7×7 binomial smooth and floored
/// at a small fraction of the squared local mean so downstream weight
/// normalisations never divide by (near-)zero confidence.
/// </summary>
internal static class VarianceEstimator
{
    /// <summary>Relative variance floor: var ≥ 1e-5 · mean².</summary>
    private const float RelativeFloor = 1e-5f;
    private const float AbsoluteFloor = 1e-12f;

    /// <summary>
    /// Variance of the FULL-buffer mean from the half means. The variance of
    /// each half mean (used when comparing half-buffer values, as the
    /// cross-filter does) is twice this — see <see cref="HalfFromFull"/>.
    /// </summary>
    public static FrameBuffer FullVariance(FrameBuffer halfA, FrameBuffer halfB, FrameBuffer mean)
    {
        int w = halfA.Width, h = halfA.Height, c = halfA.Channels;
        var variance = new FrameBuffer(w, h, c);
        var raw = new float[w * h];
        var tmp = new float[w * h];
        var smooth = new float[w * h];

        for (int ch = 0; ch < c; ch++)
        {
            var da = halfA.Plane(ch); var db = halfB.Plane(ch);
            for (int i = 0; i < raw.Length; i++)
            {
                float d = 0.5f * (da[i] - db[i]);
                raw[i] = d * d;
            }
            PlaneOps.BinomialSmooth7(raw, smooth, tmp, w, h);
            var dm = mean.Plane(ch); var dst = variance.Plane(ch);
            for (int i = 0; i < raw.Length; i++)
            {
                float m = dm[i];
                dst[i] = MathF.Max(smooth[i], MathF.Max(RelativeFloor * m * m, AbsoluteFloor));
            }
        }
        return variance;
    }

    /// <summary>Half-buffer variance = 2 × full-buffer variance (each half
    /// carries half the samples).</summary>
    public static FrameBuffer HalfFromFull(FrameBuffer fullVariance)
    {
        var half = new FrameBuffer(fullVariance.Width, fullVariance.Height, fullVariance.Channels);
        var src = fullVariance.Data; var dst = half.Data;
        for (int i = 0; i < dst.Length; i++)
            dst[i] = 2f * src[i];
        return half;
    }

    /// <summary>Inverse of <see cref="HalfFromFull"/>.</summary>
    public static FrameBuffer HalfToFull(FrameBuffer halfVariance)
    {
        var full = new FrameBuffer(halfVariance.Width, halfVariance.Height, halfVariance.Channels);
        var src = halfVariance.Data; var dst = full.Data;
        for (int i = 0; i < dst.Length; i++)
            dst[i] = 0.5f * src[i];
        return full;
    }
}
