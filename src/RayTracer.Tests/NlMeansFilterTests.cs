using RayTracer.Denoising;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Behavioural tests for the offset-decomposed NL-means engine: strong noise
/// reduction on flat regions, negligible bias, and edge preservation thanks
/// to the variance-normalised patch distance.
/// </summary>
public class NlMeansFilterTests
{
    private const int W = 96, H = 96;

    private static float Gaussian(Random rng, float sigma)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return sigma * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    private static FrameBuffer ConstantVariance(float variance, int channels = 1)
    {
        var fb = new FrameBuffer(W, H, channels);
        Array.Fill(fb.Data, variance);
        return fb;
    }

    [Fact]
    public void FlatNoisyPlane_VarianceReducedTenfold_WithNegligibleBias()
    {
        const float Mean = 1.0f, Sigma = 0.2f;
        var rng = new Random(99);
        var guide = new FrameBuffer(W, H, 1);
        var target = new FrameBuffer(W, H, 1);
        for (int i = 0; i < W * H; i++)
        {
            guide.Data[i] = Mean + Gaussian(rng, Sigma);
            target.Data[i] = Mean + Gaussian(rng, Sigma);
        }

        var filtered = NlMeansCore.Filter(
            guide, ConstantVariance(Sigma * Sigma), target,
            new NlmParams(searchRadius: 5, patchRadius: 3, k: 1.0f), features: null);

        // Interior statistics (borders see truncated windows).
        double sum = 0, sumSq = 0; int count = 0;
        for (int y = 8; y < H - 8; y++)
        for (int x = 8; x < W - 8; x++)
        {
            float v = filtered.Get(0, x, y);
            sum += v; sumSq += (double)v * v; count++;
        }
        double mean = sum / count;
        double variance = sumSq / count - mean * mean;

        Assert.True(Math.Abs(mean - Mean) < 5e-3, $"bias too large: mean {mean} vs {Mean}");
        Assert.True(variance < Sigma * Sigma / 10.0,
            $"variance {variance} not reduced ≥10× from {Sigma * Sigma}");
    }

    [Fact]
    public void StepEdge_ContrastPreserved()
    {
        const float Lo = 0.2f, Hi = 1.0f, Sigma = 0.05f;
        var rng = new Random(123);
        var guide = new FrameBuffer(W, H, 1);
        var target = new FrameBuffer(W, H, 1);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float baseV = x < W / 2 ? Lo : Hi;
            guide.Set(0, x, y, baseV + Gaussian(rng, Sigma));
            target.Set(0, x, y, baseV + Gaussian(rng, Sigma));
        }

        var filtered = NlMeansCore.Filter(
            guide, ConstantVariance(Sigma * Sigma), target,
            new NlmParams(searchRadius: 5, patchRadius: 3, k: 1.0f), features: null);

        // Sample the two columns adjacent to the edge: ≥90% of the original
        // step must survive the filtering.
        double loSum = 0, hiSum = 0;
        for (int y = 8; y < H - 8; y++)
        {
            loSum += filtered.Get(0, W / 2 - 2, y);
            hiSum += filtered.Get(0, W / 2 + 1, y);
        }
        int rows = H - 16;
        double contrast = hiSum / rows - loSum / rows;
        Assert.True(contrast > 0.9 * (Hi - Lo),
            $"edge contrast {contrast} fell below 90% of {Hi - Lo}");
    }
}
