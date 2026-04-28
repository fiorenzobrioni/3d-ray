using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Lights;

/// <summary>
/// Builds a discrete probability distribution over a list of lights for
/// power-weighted importance sampling (or uniform sampling).
///
/// <para><b>Usage.</b>
/// Construct once per renderer — the distribution is invariant across frames.
/// Call <see cref="Sample"/> with a uniform random number ξ ∈ [0,1) to pick
/// a light index; multiply the NEE contribution by <c>1/pdf</c> to obtain an
/// unbiased estimator.</para>
///
/// <para><b>Power-weighted picking (PBRT §16.3.2).</b>
/// Each light is assigned a probability proportional to its
/// <see cref="ILight.ApproximatePower"/> value.  The combined NEE pdf for a
/// particular shadow-ray direction from <c>hitPoint</c> towards light <c>i</c>
/// under a single-light strategy is:
/// <c>pdf_combined = pdf_pick(i) × pdf_light_sample(i, wi)</c>
/// and the contribution is divided by <c>pdf_pick(i)</c> to make the
/// estimator unbiased.  The MIS weight should use <c>pdf_combined</c> in the
/// numerator (see <see cref="Renderer"/> where this is applied).</para>
///
/// <para><b>Fallback.</b>
/// If all lights report zero power (e.g. an <see cref="EnvironmentLight"/>
/// whose sky does not support direct sampling) the distribution falls back to
/// uniform picking so the renderer never degenerates silently.</para>
/// </summary>
public class LightDistribution
{
    private readonly IReadOnlyList<ILight> _lights;
    private readonly float[] _cdf;          // CDF[i] = sum of normalised weights [0..i]
    private readonly float[] _pdf;          // pdf[i] = power[i] / totalPower (or 1/N for uniform)
    private readonly float   _totalPower;

    /// <summary>Number of lights in the distribution.</summary>
    public int Count => _lights.Count;

    public LightDistribution(IReadOnlyList<ILight> lights, AABB sceneBounds,
                             bool forceUniform = false)
    {
        _lights = lights;
        int n = lights.Count;

        _pdf = new float[n];
        _cdf = new float[n];

        if (n == 0)
        {
            _totalPower = 0f;
            return;
        }

        // Compute unnormalised powers (skipped when forceUniform = true)
        float[] power = new float[n];
        float sum = 0f;
        if (!forceUniform)
        {
            for (int i = 0; i < n; i++)
            {
                power[i] = MathF.Max(0f, lights[i].ApproximatePower(sceneBounds));
                sum += power[i];
            }
        }
        _totalPower = sum;

        if (sum > 0f)
        {
            float inv = 1f / sum;
            float cumulative = 0f;
            for (int i = 0; i < n; i++)
            {
                _pdf[i] = power[i] * inv;
                cumulative += _pdf[i];
                _cdf[i] = cumulative;
            }
        }
        else
        {
            // Fallback: uniform distribution
            float uniform = 1f / n;
            float cumulative = 0f;
            for (int i = 0; i < n; i++)
            {
                _pdf[i] = uniform;
                cumulative += uniform;
                _cdf[i] = cumulative;
            }
        }

        // Guard: ensure last CDF entry is exactly 1 to prevent off-by-one from
        // floating-point accumulation.
        _cdf[n - 1] = 1f;
    }

    /// <summary>
    /// Samples a light index using the distribution.
    /// </summary>
    /// <param name="xi">Uniform random in [0, 1).</param>
    /// <returns>
    /// <c>(index, pdf)</c> where <c>pdf = probability of picking this light</c>.
    /// Returns <c>(0, 1)</c> for an empty distribution.
    /// </returns>
    public (int Index, float Pdf) Sample(float xi)
    {
        int n = _lights.Count;
        if (n == 0) return (0, 1f);
        if (n == 1) return (0, _pdf[0]);

        // Binary search on CDF
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_cdf[mid] < xi)
                lo = mid + 1;
            else
                hi = mid;
        }
        return (lo, _pdf[lo]);
    }

    /// <summary>
    /// Returns the probability of picking light at <paramref name="index"/>.
    /// </summary>
    public float PdfPick(int index) => _pdf[index];

    /// <summary>
    /// Whether power data is available (totalPower &gt; 0).
    /// When false, the distribution uses uniform weights.
    /// </summary>
    public bool HasPowerData => _totalPower > 0f;
}
