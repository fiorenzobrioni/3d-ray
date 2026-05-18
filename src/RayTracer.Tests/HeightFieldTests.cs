using System.Collections.Generic;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Behavioural tests for the <see cref="HeightField"/> primitive. The pyramid
/// + bisection pipeline is validated against analytic surfaces (flat, ramp)
/// where the expected hit point is known in closed form, and the strata /
/// sea-level paths are checked for correct material selection.
/// </summary>
public class HeightFieldTests
{
    private static IMaterial Mat(float r, float g, float b)
        => new Lambertian(new Vector3(r, g, b));

    private static HeightField FlatField(float h, IMaterial mat, int n = 32)
    {
        var samples = new float[n * n];
        for (int i = 0; i < samples.Length; i++) samples[i] = h;
        return new HeightField(-10, -10, 10, 10, samples, n, n, 10f, mat);
    }

    [Fact]
    public void Hit_FlatHeightmap_ReturnsExpectedY()
    {
        // Flat heightmap at normalised h=0.3 with HeightScale=10 ⇒ surface at y=3.
        var mat = Mat(0.5f, 0.5f, 0.5f);
        var hf = FlatField(0.3f, mat);

        var ray = new Ray(new Vector3(0f, 20f, 0f), new Vector3(0f, -1f, 0f));
        HitRecord rec = default;
        Assert.True(hf.Hit(ray, 0.001f, 1000f, ref rec));
        Assert.Equal(3f, rec.Point.Y, precision: 2);
        // Normal is straight up for a flat surface.
        Assert.True(Vector3.Dot(rec.Normal, Vector3.UnitY) > 0.999f);
        Assert.Same(mat, rec.Material);
    }

    [Fact]
    public void Hit_MissesAbove_NoIntersection()
    {
        var hf = FlatField(0.2f, Mat(1, 1, 1));
        // Horizontal ray at y=5 over a max-height-2 surface — misses.
        var ray = new Ray(new Vector3(-20f, 5f, 0f), new Vector3(1f, 0f, 0f));
        HitRecord rec = default;
        Assert.False(hf.Hit(ray, 0.001f, 1000f, ref rec));
    }

    [Fact]
    public void Hit_RampHeightmap_LinearInterpolation()
    {
        // Ramp: height varies linearly from 0 at x=-10 to 1 at x=+10, constant
        // along Z. HeightScale=10 ⇒ surface y = (x + 10)/2.
        const int n = 33;
        var samples = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                samples[i + j * n] = (float)i / (n - 1);
        var mat = Mat(1f, 0f, 0f);
        var hf = new HeightField(-10, -10, 10, 10, samples, n, n, 10f, mat);

        // Shoot straight down at x=4, where the ramp expects y = 7.
        // Pick z = 0.5 so the ray sits inside one cell (not on a grid line —
        // the slab test with NaN at exact cell boundaries is undefined).
        var ray = new Ray(new Vector3(4f, 50f, 0.5f), new Vector3(0f, -1f, 0f));
        HitRecord rec = default;
        Assert.True(hf.Hit(ray, 0.001f, 1000f, ref rec));
        Assert.Equal(7f, rec.Point.Y, precision: 1);
        // Surface slope ∂y/∂x = 0.5 ⇒ outward normal = (−0.5, 1, 0)/√1.25 ≈
        // (−0.447, 0.894, 0). Verify the X tilt and that the normal points
        // generally up (Y dominant).
        Assert.True(rec.Normal.X < -0.3f, $"normal.X = {rec.Normal.X}");
        Assert.True(rec.Normal.Y > 0.8f, $"normal.Y = {rec.Normal.Y}");
    }

    [Fact]
    public void Hit_BelowSeaLevel_PicksWaterMaterial()
    {
        // Flat terrain at y=2, water plane at y=5 ⇒ vertical ray hits water first.
        var land = Mat(0.4f, 0.3f, 0.2f);
        var water = Mat(0.0f, 0.2f, 0.5f);
        const int n = 8;
        var samples = new float[n * n];
        for (int i = 0; i < samples.Length; i++) samples[i] = 0.2f;
        var hf = new HeightField(-10, -10, 10, 10, samples, n, n,
                                 heightScale: 10f, material: land,
                                 seaLevel: 5f, seaMaterial: water);

        var ray = new Ray(new Vector3(0f, 20f, 0f), new Vector3(0f, -1f, 0f));
        HitRecord rec = default;
        Assert.True(hf.Hit(ray, 0.001f, 1000f, ref rec));
        Assert.Same(water, rec.Material);
        Assert.Equal(5f, rec.Point.Y, precision: 3);
    }

    [Fact]
    public void Hit_AboveSeaLevel_PicksLandMaterial()
    {
        // Same setup but the terrain is now ABOVE the water — water must NOT
        // be reported (no floating water sheets).
        var land = Mat(0.4f, 0.3f, 0.2f);
        var water = Mat(0.0f, 0.2f, 0.5f);
        const int n = 8;
        var samples = new float[n * n];
        for (int i = 0; i < samples.Length; i++) samples[i] = 0.8f; // y = 8
        var hf = new HeightField(-10, -10, 10, 10, samples, n, n,
                                 heightScale: 10f, material: land,
                                 seaLevel: 5f, seaMaterial: water);

        var ray = new Ray(new Vector3(0f, 20f, 0f), new Vector3(0f, -1f, 0f));
        HitRecord rec = default;
        Assert.True(hf.Hit(ray, 0.001f, 1000f, ref rec));
        Assert.Same(land, rec.Material);
        Assert.Equal(8f, rec.Point.Y, precision: 1);
    }

    [Fact]
    public void Strata_HighAltitude_PicksSnow()
    {
        // Two strata: ground for low altitudes, snow for high altitudes.
        // A flat heightmap at y=9 must land in the snow band.
        var ground = Mat(0.3f, 0.2f, 0.1f);
        var snow = Mat(0.95f, 0.95f, 1f);
        var strata = new List<HeightField.StratumBand>
        {
            new() { MinAltitude = 0.00f, MaxAltitude = 0.50f, Material = ground },
            new() { MinAltitude = 0.50f, MaxAltitude = 1.00f, Material = snow },
        };
        const int n = 8;
        var samples = new float[n * n];
        for (int i = 0; i < samples.Length; i++) samples[i] = 0.9f;
        var hf = new HeightField(-10, -10, 10, 10, samples, n, n,
                                 heightScale: 10f, material: ground,
                                 seaLevel: null, seaMaterial: null,
                                 strata: strata);

        var ray = new Ray(new Vector3(0f, 20f, 0f), new Vector3(0f, -1f, 0f));
        HitRecord rec = default;
        Assert.True(hf.Hit(ray, 0.001f, 1000f, ref rec));
        Assert.Same(snow, rec.Material);
    }

    [Fact]
    public void Strata_LowAltitude_PicksGround()
    {
        var ground = Mat(0.3f, 0.2f, 0.1f);
        var snow = Mat(0.95f, 0.95f, 1f);
        var strata = new List<HeightField.StratumBand>
        {
            new() { MinAltitude = 0.00f, MaxAltitude = 0.50f, Material = ground },
            new() { MinAltitude = 0.50f, MaxAltitude = 1.00f, Material = snow },
        };
        const int n = 8;
        var samples = new float[n * n];
        for (int i = 0; i < samples.Length; i++) samples[i] = 0.1f; // y = 1, altNorm ≈ 0.1
        var hf = new HeightField(-10, -10, 10, 10, samples, n, n,
                                 heightScale: 10f, material: ground,
                                 seaLevel: null, seaMaterial: null,
                                 strata: strata);

        var ray = new Ray(new Vector3(0f, 20f, 0f), new Vector3(0f, -1f, 0f));
        HitRecord rec = default;
        Assert.True(hf.Hit(ray, 0.001f, 1000f, ref rec));
        Assert.Same(ground, rec.Material);
    }

    [Fact]
    public void BoundingBox_CoversWholeExtent()
    {
        var hf = FlatField(0.5f, Mat(1, 1, 1));
        var box = hf.BoundingBox();
        Assert.Equal(-10f, box.Min.X, precision: 2);
        Assert.Equal(-10f, box.Min.Z, precision: 2);
        Assert.Equal( 10f, box.Max.X, precision: 2);
        Assert.Equal( 10f, box.Max.Z, precision: 2);
        Assert.True(box.Max.Y >= 5f); // accommodates max sample at h=5
    }
}
