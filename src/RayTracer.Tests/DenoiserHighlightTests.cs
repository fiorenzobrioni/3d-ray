using RayTracer.Denoising;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// A pixel thousands of times brighter than the rest (the sun in a mirror)
/// must stay where it is, keep its energy, and leave its neighbours alone
/// (PIANO-REVIEW.md Phase 6: the denoiser drew it as a square with a dark rim).
/// </summary>
public class DenoiserHighlightTests
{
    private static RenderBuffers NoisyFlat(int size, int hotX, int hotY, float hot)
    {
        var b = new RenderBuffers(size, size, 16, captureAovs: true);
        int n = size * size;
        var rng = new Random(7);
        int half = 0;
        foreach (var (beauty, albedo, normal, depth) in new[] { (b.BeautyA, b.AlbedoA!, b.NormalA!, b.DepthA!),
                                                               (b.BeautyB, b.AlbedoB!, b.NormalB!, b.DepthB!) })
        {
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    beauty.Data[c * n + i] = 0.5f + 0.1f * (float)(rng.NextDouble() - 0.5);
                    albedo.Data[c * n + i] = 0.5f;
                }
                normal.Data[n + i] = 1f;
                depth.Data[i] = 1f;
            }
            // A highlight a few samples catch: the halves disagree wildly, as
            // they do on the sun's reflection, and so does a neighbour.
            int p = hotY * size + hotX;
            for (int c = 0; c < 3; c++)
            {
                beauty.Data[c * n + p] = half == 0 ? hot : 0.2f * hot;
                if (half == 1) beauty.Data[c * n + p + 1] = 0.05f * hot;
            }
            half++;
        }
        return b;
    }

    [Theory]
    [InlineData(DenoiserKind.Nfor)]
    [InlineData(DenoiserKind.Nlm)]
    public void HotPixel_StaysPutAndKeepsItsEnergy(DenoiserKind kind)
    {
        const int size = 48, hx = 20, hy = 25;
        const float hot = 1e5f;
        var buffers = NoisyFlat(size, hx, hy, hot);
        var input = buffers.CombineHalves(buffers.BeautyA, buffers.BeautyB);
        var output = NforDenoiser.Denoise(buffers, new DenoiserOptions { Kind = kind });
        int n = size * size;

        double sumIn = 0, sumOut = 0;
        for (int i = 0; i < n; i++) { sumIn += input.Data[n + i]; sumOut += output.Data[n + i]; }
        Assert.Equal(1.0, sumOut / sumIn, 0.01);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (Math.Abs(x - hx) <= 2 && Math.Abs(y - hy) <= 2) continue;
                float v = output.Data[n + y * size + x];
                Assert.InRange(v, 0.35f, 0.65f);
            }
    }
}
