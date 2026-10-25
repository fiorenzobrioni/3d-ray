using System.Numerics;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>White balance by colour temperature (PIANO-REVIEW.md, Phase 3).</summary>
public class WhiteBalanceTests
{
    /// <summary>Linear Rec.709 colour of a light with chromaticity xy, at luminance 1.</summary>
    private static Vector3 LightColour((double x, double y) c) =>
        ColorSpaces.XyzToRec709.Apply(new Vector3((float)(c.x / c.y), 1f, (float)((1 - c.x - c.y) / c.y)));

    [Fact]
    public void D65_IsTheIdentity()
    {
        var m = WhiteBalance.Matrix(6504);
        foreach (var v in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.3f, 0.5f, 0.1f) })
            Assert.True(Vector3.Distance(v, m.Apply(v)) < 2e-3f, $"{v} -> {m.Apply(v)}");
    }

    [Theory]
    [InlineData(1900)]   // candle
    [InlineData(2700)]   // incandescent
    [InlineData(3200)]   // tungsten studio lamp
    [InlineData(4000)]
    [InlineData(5600)]   // photographic daylight
    [InlineData(9000)]   // overcast shade
    public void ALightOfTheChosenTemperature_RendersNeutral(int kelvin)
    {
        var light = LightColour(WhiteBalance.Chromaticity(kelvin));
        var balanced = WhiteBalance.Matrix(kelvin).Apply(light);
        float spread = MathF.Max(balanced.X, MathF.Max(balanced.Y, balanced.Z)) - MathF.Min(balanced.X, MathF.Min(balanced.Y, balanced.Z));
        Assert.True(spread < 2e-3f * balanced.Y, $"{kelvin} K light balanced to {balanced}");
    }

    [Fact]
    public void LowTemperature_CoolsAGrey_HighTemperature_WarmsIt()
    {
        var grey = new Vector3(0.5f);
        var tungsten = WhiteBalance.Matrix(3200).Apply(grey);
        var shade = WhiteBalance.Matrix(9000).Apply(grey);
        Assert.True(tungsten.Z > tungsten.X, $"3200 K: {tungsten}");
        Assert.True(shade.X > shade.Z, $"9000 K: {shade}");
    }

    [Fact]
    public void Locus_IsContinuous()
    {
        // Across the blend between the Planckian and the daylight locus.
        var prev = WhiteBalance.Chromaticity(3000);
        for (int k = 3010; k <= 5000; k += 10)
        {
            var c = WhiteBalance.Chromaticity(k);
            Assert.True(Math.Abs(c.x - prev.x) + Math.Abs(c.y - prev.y) < 2e-3, $"jump at {k} K");
            prev = c;
        }
        var d65 = WhiteBalance.Chromaticity(6504);
        Assert.Equal(0.3127, d65.x, 3);
        Assert.Equal(0.3290, d65.y, 3);
    }
}
