namespace RayTracer.Rendering;

/// <summary>Pixel reconstruction filter (<c>--filter</c>).</summary>
public enum PixelFilterKind
{
    /// <summary>Each sample counts only for its own pixel, uniformly: the
    /// sharpest and the most aliased (its spectrum has strong side lobes).</summary>
    Box,
    /// <summary>Gaussian, σ = 0.5 px, truncated at 4σ = 2 px and shifted to
    /// zero there. The CLI default.</summary>
    Gaussian,
    /// <summary>Blackman-Harris window over a 1.5 px radius (σ ≈ 0.42 px):
    /// slightly sharper than the Gaussian, with very low side lobes.</summary>
    BlackmanHarris,
}

/// <summary>
/// Separable pixel reconstruction filter, applied by <em>filter importance
/// sampling</em> (PIANO-REVIEW.md, Phase 3): each camera sample is placed at
/// an offset from the pixel centre drawn with density proportional to the
/// filter, and then counts with weight 1 for that pixel only. Compared with
/// splatting every sample into its neighbours this keeps the noise of
/// adjacent pixels independent (what the denoiser and the dual-buffer
/// variance estimate assume) and needs no weights, so captures, AOVs and the
/// sample count are unchanged. Only non-negative filters can be sampled this
/// way, which is why there is no Mitchell or Lanczos.
///
/// <para>The profile is tabulated in <see cref="Bins"/> bins across its
/// support and sampled by inverting the piecewise-linear CDF: the filter
/// actually applied is that histogram (steps of 1/256 px for the Gaussian),
/// indistinguishable from the analytic curve. Each axis is an increasing map
/// of its uniform, so the stratification of the sampler's 2D pixel pair is
/// carried over to the filtered positions.</para>
/// </summary>
public sealed class PixelFilter
{
    private const int Bins = 1024;

    public static readonly PixelFilter Box = new(PixelFilterKind.Box);

    public PixelFilterKind Kind { get; }

    /// <summary>Half-width of the support, in pixels.</summary>
    public float Radius { get; }

    // Normalized CDF at the bin edges (Bins + 1 entries); null for the box.
    private readonly float[]? _cdf;

    private PixelFilter(PixelFilterKind kind)
    {
        Kind = kind;
        Radius = kind switch
        {
            PixelFilterKind.Gaussian => 2.0f,
            PixelFilterKind.BlackmanHarris => 1.5f,
            _ => 0.5f,
        };
        if (kind == PixelFilterKind.Box) return;

        var cdf = new double[Bins + 1];
        double h = 2.0 * Radius / Bins;
        for (int k = 0; k < Bins; k++)
            cdf[k + 1] = cdf[k] + Math.Max(0.0, Evaluate(-Radius + (k + 0.5) * h));
        _cdf = new float[Bins + 1];
        for (int k = 0; k <= Bins; k++) _cdf[k] = (float)(cdf[k] / cdf[Bins]);
        _cdf[Bins] = 1f;
    }

    public static PixelFilter Create(PixelFilterKind kind) =>
        kind == PixelFilterKind.Box ? Box : new PixelFilter(kind);

    /// <summary>1D profile at offset <paramref name="x"/> pixels from the centre (unnormalized).</summary>
    public double Evaluate(double x)
    {
        double ax = Math.Abs(x);
        if (ax > Radius) return 0.0;
        switch (Kind)
        {
            case PixelFilterKind.Gaussian:
            {
                const double sigma = 0.5;
                double alpha = -1.0 / (2.0 * sigma * sigma);
                return Math.Max(0.0, Math.Exp(alpha * x * x) - Math.Exp(alpha * Radius * Radius));
            }
            case PixelFilterKind.BlackmanHarris:
            {
                double phase = 2.0 * Math.PI * (x / (2.0 * Radius) + 0.5);
                return 0.35875 - 0.48829 * Math.Cos(phase)
                     + 0.14128 * Math.Cos(2.0 * phase) - 0.01168 * Math.Cos(3.0 * phase);
            }
            default:
                return 1.0;
        }
    }

    /// <summary>
    /// Position inside the pixel's footprint for the uniform <paramref name="u"/>,
    /// measured from the pixel's lower edge, as the box filter's jitter is: the
    /// box returns <paramref name="u"/> itself (in [0, 1)), wider filters a value
    /// in (0.5 - Radius, 0.5 + Radius).
    /// </summary>
    public float SamplePosition(float u)
    {
        if (_cdf == null) return u;
        int lo = 0, hi = Bins;          // _cdf[lo] <= u < _cdf[hi]
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_cdf[mid] <= u) lo = mid; else hi = mid;
        }
        float span = _cdf[hi] - _cdf[lo];
        float f = span > 0f ? (u - _cdf[lo]) / span : 0.5f;
        float h = 2f * Radius / Bins;
        return 0.5f - Radius + (lo + f) * h;
    }

    public static bool TryParse(string? name, out PixelFilterKind kind)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "box": kind = PixelFilterKind.Box; return true;
            case "gaussian": kind = PixelFilterKind.Gaussian; return true;
            case "blackman-harris": case "blackmanharris": case "bh": kind = PixelFilterKind.BlackmanHarris; return true;
            default: kind = PixelFilterKind.Box; return false;
        }
    }
}
