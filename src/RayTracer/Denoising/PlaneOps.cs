using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>
/// Small filters and reductions over single float planes (W×H, row-major),
/// shared by the variance estimator and the denoiser pipeline. Borders are
/// handled by truncating the kernel and renormalising — no clamping or
/// mirroring, so flat regions stay exactly flat up to the edge.
/// </summary>
internal static class PlaneOps
{
    /// <summary>
    /// Separable 7×7 binomial smoothing ([1,6,15,20,15,6,1]/64 per axis),
    /// truncated and renormalised at the borders. Used to stabilise the
    /// χ²-noisy raw dual-buffer variance.
    /// </summary>
    public static void BinomialSmooth7(float[] src, float[] dst, float[] tmp, int w, int h)
    {
        Span<float> k = stackalloc float[] { 1f, 6f, 15f, 20f, 15f, 6f, 1f };
        SeparablePass(src, tmp, w, h, k, horizontal: true);
        SeparablePass(tmp, dst, w, h, k, horizontal: false);
    }

    private static void SeparablePass(float[] src, float[] dst, int w, int h,
                                      ReadOnlySpan<float> kernel, bool horizontal)
    {
        int r = kernel.Length / 2;
        // Copy to local arrays the lambda can capture (spans can't cross).
        float[] kArr = kernel.ToArray();
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float sum = 0f, wSum = 0f;
                for (int i = -r; i <= r; i++)
                {
                    int xx = horizontal ? x + i : x;
                    int yy = horizontal ? y : y + i;
                    if (xx < 0 || xx >= w || yy < 0 || yy >= h) continue;
                    float kw = kArr[i + r];
                    sum += kw * src[yy * w + xx];
                    wSum += kw;
                }
                dst[row + x] = sum / wSum;
            }
        });
    }

    /// <summary>
    /// Normalised box smoothing of radius <paramref name="r"/> (truncated at
    /// borders). Used for the per-candidate MSE maps and selection softening.
    /// </summary>
    public static void BoxSmooth(float[] src, float[] dst, int w, int h, int r)
    {
        var tmp = new float[w * h];
        // Horizontal running sums.
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            int row = y * w;
            float run = 0f;
            for (int x = 0; x < Math.Min(r + 1, w); x++) run += src[row + x];
            for (int x = 0; x < w; x++)
            {
                int lo = Math.Max(0, x - r), hi = Math.Min(w - 1, x + r);
                tmp[row + x] = run / (hi - lo + 1);
                // Slide window to x+1.
                if (x + r + 1 < w) run += src[row + x + r + 1];
                if (x - r >= 0) run -= src[row + x - r];
            }
        });
        // Vertical.
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            int lo = Math.Max(0, y - r), hi = Math.Min(h - 1, y + r);
            float inv = 1f / (hi - lo + 1);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int yy = lo; yy <= hi; yy++) sum += tmp[yy * w + x];
                dst[row + x] = sum * inv;
            }
        });
    }

    /// <summary>Percentile of the values ≥ 0 in a plane (NaN-free input
    /// assumed). Returns <paramref name="fallback"/> when no value qualifies.</summary>
    public static float PercentileOfNonNegative(float[] plane, float percentile, float fallback)
    {
        var finite = new List<float>(plane.Length);
        foreach (float v in plane)
            if (v >= 0f) finite.Add(v);
        if (finite.Count == 0) return fallback;
        finite.Sort();
        int idx = Math.Clamp((int)(percentile * (finite.Count - 1)), 0, finite.Count - 1);
        return finite[idx];
    }

    /// <summary>Weighted per-element combination of two equal-shape buffers:
    /// wa·a + (1−wa)·b. With <paramref name="samplesB"/> = 0, returns a copy
    /// of <paramref name="a"/>.</summary>
    public static FrameBuffer Combine(FrameBuffer a, FrameBuffer b, int samplesA, int samplesB)
    {
        var result = new FrameBuffer(a.Width, a.Height, a.Channels);
        if (samplesB <= 0)
        {
            Array.Copy(a.Data, result.Data, a.Data.Length);
            return result;
        }
        float wa = (float)samplesA / (samplesA + samplesB);
        float wb = 1f - wa;
        var da = a.Data; var db = b.Data; var dst = result.Data;
        for (int i = 0; i < dst.Length; i++)
            dst[i] = wa * da[i] + wb * db[i];
        return result;
    }
}
