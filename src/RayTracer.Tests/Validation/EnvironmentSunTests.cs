using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The environment map's importance sampler draws the directions the lookup
/// shows bright (PIANO-REVIEW.md §3.44), and the HDRI sun extraction moves
/// energy from the map to the sun without creating or losing any.
/// </summary>
public class EnvironmentSunTests
{
    private const int W = 256, H = 128;

    /// <summary>A grey sky with a bright disc at (φ, θ) of the given angular radius.</summary>
    private static float[] SkyWithSun(float phiDeg, float latDeg, float radiusDeg, float sunL, float skyL = 1f)
    {
        var px = new float[W * H * 3];
        Vector3 c = EnvironmentMap.DirectionOf(phiDeg * MathF.PI / 180f, latDeg * MathF.PI / 180f);
        float cosR = MathF.Cos(radiusDeg * MathF.PI / 180f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var d = EnvironmentMap.DirectionOf(((x + 0.5f) / W - 0.5f) * 2f * MathF.PI, (0.5f - (y + 0.5f) / H) * MathF.PI);
                // A warm sky, darker towards the ground, so in-painting has something to match.
                float L = skyL * (1.2f + 0.5f * d.Y) * (Vector3.Dot(d, c) >= cosR ? sunL : 1f);
                int i = (y * W + x) * 3;
                px[i] = L; px[i + 1] = 0.9f * L; px[i + 2] = 0.8f * L;
            }
        return px;
    }

    private static Vector3 Integral(float[] px)
    {
        Vector3 sum = Vector3.Zero;
        for (int y = 0; y < H; y++)
        {
            float dw = MathF.Cos((0.5f - (y + 0.5f) / H) * MathF.PI) * (MathF.PI / H) * (2f * MathF.PI / W);
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 3;
                sum += new Vector3(px[i], px[i + 1], px[i + 2]) * dw;
            }
        }
        return sum;
    }

    [Theory]
    [InlineData(40f, 30f, 0f)]
    [InlineData(-130f, 10f, 45f)]
    [InlineData(179.5f, 20f, 0f)]    // across the ±180° seam
    [InlineData(70f, 75f, 0f)]       // high sun: φ stretched by 1/cos(lat)
    public void SampledDirections_LookUpTheTexelSampled(float phiDeg, float latDeg, float rotationDeg)
    {
        var map = new EnvironmentMap(SkyWithSun(phiDeg, latDeg, 2f, 50000f), W, H, rotationDeg: rotationDeg);
        MathUtils.ReseedThread(11);
        int bright = 0;
        const int n = 4000;
        for (int i = 0; i < n; i++)
        {
            var (d, _) = map.SampleDirection();
            if (MathUtils.Luminance(map.Sample(d)) > 1000f) bright++;
        }
        // The sun holds over 90% of the energy, so most samples must see it
        // (its edge texels blend with the sky under the bilinear lookup).
        Assert.True(bright > 0.8 * n, $"{bright}/{n} sampled directions look up the sun");
    }

    [Theory]
    [InlineData(40f, 30f, 1.5f, 0f)]
    [InlineData(-130f, 10f, 0.8f, 45f)]
    [InlineData(179.5f, 20f, 1.5f, 0f)]
    [InlineData(70f, 75f, 1.5f, 0f)]
    public void SunExtraction_ConservesEnergy_AndFindsTheSun(float phiDeg, float latDeg, float radiusDeg, float rotationDeg)
    {
        float[] px = SkyWithSun(phiDeg, latDeg, radiusDeg, 50000f);
        Vector3 before = Integral(px);
        var map = new EnvironmentMap((float[])px.Clone(), W, H, rotationDeg: rotationDeg);
        var sun = HdriSunExtractor.Extract(map);
        Assert.NotNull(sun);

        // Map after + sun = map before.
        float half = sun!.HalfAngleDeg * MathF.PI / 180f;
        Vector3 sunEnergy = sun.Radiance * 2f * MathF.PI * (1f - MathF.Cos(half));
        Vector3 after = Integral(sun.InpaintedPixels) + sunEnergy;
        Assert.InRange(after.X / before.X, 0.999f, 1.001f);
        Assert.InRange(after.Z / before.Z, 0.999f, 1.001f);
        // Nearly all of the disc's excess went to the sun.
        Assert.True(Integral(sun.InpaintedPixels).X < before.X * 0.5f);

        // The sun direction, through the lookup, is where the map shows it.
        Assert.True(MathUtils.Luminance(map.Sample(sun.Direction)) > 1000f,
            $"sun direction {sun.Direction} does not look up the sun");
        // A disc smaller than a texel (1.4° here) reads as half a texel.
        float halfTexelDeg = 0.5f * 180f / H;
        Assert.InRange(sun.HalfAngleDeg, MathF.Min(radiusDeg * 0.9f, halfTexelDeg * 0.98f), radiusDeg + 1.5f);
    }
}
