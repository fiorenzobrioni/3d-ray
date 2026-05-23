using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Multi-scale ridged-multifractal field with soft-max compositing.
///
/// <para>
/// Pro renderers (Arnold, Cycles, RenderMan, Mitsuba) build organic vein /
/// crack structures by overlaying several independent ridged layers at
/// decoupled scales. Naïve <i>summing</i> the layers smears the ridges into
/// muddy bands; the right operator is a smooth max so each layer can locally
/// dominate where it has the highest ridge, while the boundary between
/// dominant layers stays C¹ continuous and free of aliasing.
/// </para>
///
/// <para>
/// The soft-max here is a log-sum-exp normalised by the sharpness parameter
/// (same numerical scheme used by <see cref="WorleyNoise.EvaluateSmooth"/>):
/// </para>
/// <code>
/// softMax(x_i, w_i, k) = (1/k) · log(Σ w_i · exp(k · x_i))
/// </code>
/// <para>
/// rebased on the running maximum so every exponent stays in <c>(-∞, 0]</c>
/// and the largest weight is exactly <c>1</c>. At <c>k → ∞</c> this recovers
/// the hard <c>max</c>; at small <c>k</c> the layers blend gently.
/// </para>
/// </summary>
public static class MultiScaleRidgedField
{
    // Decorrelated offsets — one per layer — so independent scales sample
    // disjoint regions of the noise field and never visibly correlate.
    private static readonly Vector3[] _layerOffsets =
    {
        new( 19.7f,  73.1f,  41.2f),
        new(127.3f, 211.9f,  53.4f),
        new(303.5f,   7.1f, 159.8f),
    };

    /// <summary>
    /// Samples the multi-scale ridged field at <paramref name="p"/>.
    ///
    /// <para>
    /// <paramref name="scales"/> and <paramref name="weights"/> must have the
    /// same length (1..3); each entry defines a ridged layer with its own
    /// world-space scale and soft-max weight. The result is the soft-max of
    /// <c>weights[i] · Ridged(p · scales[i] + offset[i], octaves, lac, gain)</c>
    /// and is normalised to <c>[0, 1]</c>.
    /// </para>
    /// </summary>
    public static float Sample(
        Perlin noise,
        Vector3 p,
        ReadOnlySpan<float> scales,
        ReadOnlySpan<float> weights,
        int octaves,
        float lacunarity,
        float gain,
        float softMaxSharpness)
    {
        int n = scales.Length;
        if (n == 0 || n != weights.Length)
            return 0f;

        // Single layer is the hard case: skip the soft-max accumulator and
        // return the weighted ridged value directly (still clamped to [0, 1]).
        if (n == 1)
        {
            float w0 = weights[0];
            float v0 = noise.Ridged(p * scales[0] + _layerOffsets[0], octaves, lacunarity, gain);
            return Math.Clamp(w0 * v0, 0f, 1f);
        }

        // Hard-max pass — provides the rebase point so every exp() argument
        // sits in (-∞, 0] and the dominant weight equals 1. Avoids overflow
        // in single precision at high sharpness.
        Span<float> values = stackalloc float[3];
        float hardMax = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            float v = weights[i] * noise.Ridged(
                p * scales[i] + _layerOffsets[i],
                octaves, lacunarity, gain);
            values[i] = v;
            if (v > hardMax) hardMax = v;
        }

        // Pure hard-max fallback when sharpness is huge / zero / degenerate.
        if (!(softMaxSharpness > 0f) || float.IsInfinity(softMaxSharpness))
        {
            return Math.Clamp(hardMax, 0f, 1f);
        }

        double k = softMaxSharpness;
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            sum += Math.Exp(k * (values[i] - hardMax));
        }
        float soft = (float)(hardMax + Math.Log(sum) / k);
        return Math.Clamp(soft, 0f, 1f);
    }
}
