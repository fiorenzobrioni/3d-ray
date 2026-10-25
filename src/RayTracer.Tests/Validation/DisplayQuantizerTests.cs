using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>Quantization of the display image (PIANO-REVIEW.md, Phase 3).</summary>
public class DisplayQuantizerTests
{
    [Theory]
    [InlineData(0.5f / 255f)]
    [InlineData(0.3f)]
    [InlineData(100.25f / 255f)]
    [InlineData(254.7f / 255f)]
    public void Dither_KeepsTheMeanOfAFlatField(float value)
    {
        // A flat field between two codes: plain rounding sends every pixel to
        // the same code (a band); the dithered mean is the exact value.
        double sum = 0;
        int n = 0;
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++, n++)
                sum += DisplayQuantizer.To8Bit(value, x, y, 0, dither: true);
        double mean = sum / n;
        double exact = Math.Clamp(value * 255.0, 0.0, 255.0);
        // Codes 0 and 255 clip the noise: the mean is exact only inside.
        if (exact > 1 && exact < 254)
            Assert.InRange(mean, exact - 0.01, exact + 0.01);
        Assert.InRange(mean, exact - 0.5, exact + 0.5);
    }

    [Fact]
    public void Dither_IsDeterministicAndAtMostOneCodeAway()
    {
        for (int i = 0; i < 1000; i++)
        {
            float v = i / 999f;
            byte a = DisplayQuantizer.To8Bit(v, i, 7, 1, true);
            Assert.Equal(a, DisplayQuantizer.To8Bit(v, i, 7, 1, true));
            Assert.InRange(a - v * 255f, -1.5f, 1.5f);
        }
    }

    [Fact]
    public void Undithered_RoundsToNearest_And16BitIsExactAtTheEnds()
    {
        Assert.Equal(0, DisplayQuantizer.To8Bit(0.4f / 255f, 0, 0, 0, false));
        Assert.Equal(1, DisplayQuantizer.To8Bit(0.6f / 255f, 0, 0, 0, false));
        Assert.Equal(255, DisplayQuantizer.To8Bit(2f, 0, 0, 0, false));
        Assert.Equal(0, DisplayQuantizer.To8Bit(float.NaN, 0, 0, 0, true));
        Assert.Equal(0, DisplayQuantizer.To16Bit(0f));
        Assert.Equal(65535, DisplayQuantizer.To16Bit(1f));
        Assert.Equal(32768, DisplayQuantizer.To16Bit(0.5f));
    }
}
