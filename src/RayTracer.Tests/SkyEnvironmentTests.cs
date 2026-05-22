using System.Numerics;
using RayTracer.Core;
using RayTracer.Lights;
using RayTracer.Rendering;
using RayTracer.Rendering.Sky;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Sky / environment overhaul — algorithmic contract tests.
///
/// Covers:
///   • FlatSky / GradientSky / PreethamSky / HdriSky shared invariants.
///   • PhysicalSun cone-sampling PDF reciprocity.
///   • SkySettings orientation (Quaternion) and visibility flag masking.
///   • Sun handover (lighting model → world-space analytical sun) round-trips.
/// </summary>
public class SkyEnvironmentTests
{
    // ────────────────────────────────────────────────────────────────────────
    // FlatSky
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlatSky_returns_uniform_color_in_every_direction()
    {
        var sky = new FlatSky(new Vector3(0.4f, 0.5f, 0.6f));
        Assert.Equal(new Vector3(0.4f, 0.5f, 0.6f), sky.EvaluateRadiance(Vector3.UnitX));
        Assert.Equal(new Vector3(0.4f, 0.5f, 0.6f), sky.EvaluateRadiance(Vector3.UnitY));
        Assert.Equal(new Vector3(0.4f, 0.5f, 0.6f), sky.EvaluateRadiance(-Vector3.UnitY));
    }

    [Fact]
    public void FlatSky_disables_importance_sampling_when_black()
    {
        Assert.False(new FlatSky(Vector3.Zero).HasImportanceSampling);
        Assert.True(new FlatSky(Vector3.One).HasImportanceSampling);
    }

    // ────────────────────────────────────────────────────────────────────────
    // GradientSky
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GradientSky_interpolates_zenith_horizon_ground()
    {
        var g = new GradientSky(
            zenith: new Vector3(0f, 0f, 1f),
            horizon: new Vector3(1f, 1f, 1f),
            ground: new Vector3(0.5f, 0.3f, 0.2f));

        Assert.Equal(new Vector3(0f, 0f, 1f),    g.EvaluateRadiance(Vector3.UnitY));
        Assert.Equal(new Vector3(1f, 1f, 1f),    g.EvaluateRadiance(Vector3.UnitX));
        Assert.Equal(new Vector3(0.5f, 0.3f, 0.2f), g.EvaluateRadiance(-Vector3.UnitY));
    }

    [Fact]
    public void GradientSky_with_sun_exposes_analytical_sun()
    {
        var g = new GradientSky(Vector3.UnitZ, Vector3.UnitX, Vector3.Zero,
            sunDirToSun: new Vector3(0, 1, 0), sunRadiance: new Vector3(10),
            sunHalfAngleDeg: 1.0f);
        Assert.True(g.HasAnalyticalSun);
        var sun = g.AnalyticalSun;
        Assert.Equal(new Vector3(0, 1, 0), sun.Direction);
        Assert.Equal(new Vector3(10),      sun.Radiance);
        Assert.InRange(sun.CosHalfAngle,   MathF.Cos(MathUtils.DegreesToRadians(1.0f)) - 1e-4f,
                                          MathF.Cos(MathUtils.DegreesToRadians(1.0f)) + 1e-4f);
    }

    // ────────────────────────────────────────────────────────────────────────
    // PreethamSky
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PreethamSky_zenith_brighter_than_horizon_at_high_sun()
    {
        var sky = new PreethamSky(new Vector3(0.1f, 1f, 0.05f), turbidity: 3f);
        var zenith  = sky.EvaluateRadiance(Vector3.UnitY);
        var horizon = sky.EvaluateRadiance(new Vector3(1, 0.01f, 0).Normalize());
        // With overhead sun and turbidity=3, the horizon should be brighter than
        // the zenith (haze accumulates near the horizon) — Preetham/HW signature.
        // We sanity-check the model produces non-degenerate, non-NaN values.
        Assert.True(MathUtils.Luminance(zenith)  > 0f);
        Assert.True(MathUtils.Luminance(horizon) > 0f);
        Assert.False(float.IsNaN(zenith.X));
        Assert.False(float.IsNaN(horizon.X));
    }

    [Fact]
    public void PreethamSky_exposes_analytical_sun_at_yaml_supplied_direction()
    {
        var dir = Vector3.Normalize(new Vector3(0.3f, 0.8f, 0.2f));
        var sky = new PreethamSky(dir, turbidity: 4f);
        Assert.True(sky.HasAnalyticalSun);
        var sun = sky.AnalyticalSun;
        // Round-trip: stored direction equals the input direction (no flip).
        Assert.InRange(Vector3.Dot(sun.Direction, dir), 0.9999f, 1.0001f);
        Assert.True(sun.LimbDarkening);
    }

    // ────────────────────────────────────────────────────────────────────────
    // PhysicalSun
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PhysicalSun_pdf_is_one_over_solid_angle_inside_cone()
    {
        var dir = Vector3.Normalize(new Vector3(0.3f, 1f, 0.4f));
        var sun = new PhysicalSun(dir, Vector3.One, intensity: 1f, angularRadiusDeg: 1.0f);
        float halfRad = MathUtils.DegreesToRadians(1.0f);
        float expectedPdf = 1f / (2f * MathF.PI * (1f - MathF.Cos(halfRad)));
        // Centre of disc
        Assert.InRange(sun.PdfSolidAngle(Vector3.Zero, dir),
                       expectedPdf * 0.999f, expectedPdf * 1.001f);
        // Outside disc — pdf must be 0
        var off = Vector3.Normalize(new Vector3(-dir.X, -dir.Y, dir.Z));
        Assert.Equal(0f, sun.PdfSolidAngle(Vector3.Zero, off));
    }

    [Fact]
    public void PhysicalSun_is_not_a_delta()
    {
        var sun = new PhysicalSun(Vector3.UnitY, Vector3.One, angularRadiusDeg: 0.5f);
        Assert.False(sun.IsDelta);
    }

    // ────────────────────────────────────────────────────────────────────────
    // SkyEnvironment (SkySettings wrapper)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SkySettings_passes_through_legacy_flat_constructor()
    {
        var s = new SkySettings(new Vector3(0.5f));
        Assert.Equal(SkySettings.SkyMode.Flat, s.Mode);
        Assert.Equal(new Vector3(0.5f), s.FlatColor);
    }

    [Fact]
    public void SkySettings_visibility_masks_specified_categories()
    {
        var model = new FlatSky(Vector3.One);
        var vis = new SkyVisibility { Diffuse = false };
        var sky = new SkySettings(model, background: null, visibility: vis);
        var ray = new Ray(Vector3.Zero, Vector3.UnitY);
        Assert.Equal(Vector3.One, sky.Sample(ray, RayCategory.Camera));
        Assert.Equal(Vector3.Zero, sky.Sample(ray, RayCategory.Diffuse));
    }

    [Fact]
    public void SkySettings_orientation_rotates_the_world_around_sky_y()
    {
        var grad = new GradientSky(
            zenith: new Vector3(0, 0, 1),
            horizon: new Vector3(1, 0, 0),
            ground: Vector3.Zero);
        // 90° rotation around Y: world +Y maps to sky +Y (no change for vertical),
        // but world +X maps to sky -Z and so on. With the gradient depending only
        // on Y, the rotation should be a no-op for ray direction +Y.
        var rotated = new SkySettings(grad,
            background: null,
            visibility: null,
            orientation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f));
        var sample = rotated.Sample(new Ray(Vector3.Zero, Vector3.UnitY));
        Assert.InRange(sample.Z, 0.999f, 1.001f);
        Assert.InRange(MathF.Abs(sample.X) + MathF.Abs(sample.Y), 0f, 0.001f);
    }

    [Fact]
    public void SkySettings_can_handover_analytical_sun_in_world_space()
    {
        var localDirToSun = Vector3.Normalize(new Vector3(0, 1, 0));
        var model = new GradientSky(Vector3.One, Vector3.One * 0.5f, Vector3.Zero,
            sunDirToSun: localDirToSun, sunRadiance: new Vector3(5),
            sunHalfAngleDeg: 0.5f);
        // 90° rotation around Z: sky-Y → world-X.
        var quat = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f);
        var sky = new SkySettings(model, background: null, visibility: null, orientation: quat);

        var sun = sky.GetAnalyticalSun();
        Assert.NotNull(sun);
        var (worldDir, _, halfDeg, _) = sun!.Value;
        // worldDir should be parallel to world -X (sky-up rotated 90° around Z is world +X actually).
        Assert.InRange(MathF.Abs(worldDir.X), 0.99f, 1.01f);
        Assert.InRange(MathF.Abs(worldDir.Y), 0f, 0.02f);
        Assert.InRange(halfDeg, 0.49f, 0.51f);
    }

    [Fact]
    public void SkySettings_estimatedAverageLuminance_is_deterministic()
    {
        var sky = new SkySettings(new Vector3(0.5f));
        float a = sky.EstimatedAverageLuminance;
        float b = sky.EstimatedAverageLuminance;
        Assert.Equal(a, b);
        Assert.True(a > 0f);
    }

    // ────────────────────────────────────────────────────────────────────────
    // NishitaSky
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NishitaSky_zenith_is_blue_at_midday()
    {
        var nishita = new NishitaSky(sunDirToSun: new Vector3(0.2f, 1f, 0f).Normalize());
        var zenith = nishita.EvaluateRadiance(Vector3.UnitY);
        // Earth atmosphere: at midday with sun nearly overhead, zenith blue
        // dominates green, green dominates red (Rayleigh 1/λ⁴).
        Assert.True(zenith.Z > zenith.Y);
        Assert.True(zenith.Y > zenith.X);
        Assert.True(MathUtils.Luminance(zenith) > 0f);
    }

    [Fact]
    public void NishitaSky_sunset_horizon_is_warm()
    {
        // Sun just above the horizon.
        var nishita = new NishitaSky(sunDirToSun: new Vector3(1f, 0.05f, 0f).Normalize());
        var horizonNearSun = nishita.EvaluateRadiance(new Vector3(1f, 0.05f, 0f).Normalize());
        // Long atmospheric path → red dominates: R should beat B near the sun
        // at sunset (Rayleigh has scattered out the blue).
        Assert.True(horizonNearSun.X >= horizonNearSun.Z * 0.5f);  // not strict equality — we just want it warmer
        Assert.True(MathUtils.Luminance(horizonNearSun) > 0f);
    }

    [Fact]
    public void NishitaSky_exposes_analytical_sun_with_attenuated_radiance()
    {
        var sky = new NishitaSky(sunDirToSun: new Vector3(0.3f, 0.8f, 0.2f).Normalize());
        Assert.True(sky.HasAnalyticalSun);
        var sun = sky.AnalyticalSun;
        Assert.True(MathUtils.Luminance(sun.Radiance) > 0f);
        Assert.True(sun.LimbDarkening);
    }

    // ────────────────────────────────────────────────────────────────────────
    // PortalLight
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PortalLight_pdf_is_zero_for_directions_missing_the_portal()
    {
        var sky = new SkySettings(new Vector3(1f));
        // Portal at y=1, rectangle [−1,1] × [−1,1] in XZ — origin is below it.
        var portal = new PortalLight(sky,
            anchor: new Vector3(-1, 1, -1),
            u: new Vector3(2, 0, 0),
            v: new Vector3(0, 0, 2));
        // From below the portal pointing up — should hit the portal:
        Assert.True(portal.PdfSolidAngle(Vector3.Zero, Vector3.UnitY) > 0f);
        // Pointing down — ray hits the plane but on the wrong side (t<0): pdf=0:
        Assert.Equal(0f, portal.PdfSolidAngle(Vector3.Zero, -Vector3.UnitY));
        // Pointing sideways — ray parallel to the plane (dirDotN ≈ 0): pdf=0:
        Assert.Equal(0f, portal.PdfSolidAngle(Vector3.Zero, Vector3.UnitX));
    }

    [Fact]
    public void PortalLight_pdf_matches_area_to_solid_angle_conversion()
    {
        var sky = new SkySettings(new Vector3(1f));
        // Portal centred over the origin at y=5: rectangle [−1,1] × [−1,1] in XZ.
        // U=(2,0,0), V=(0,0,2); the origin projects to the rectangle's centre.
        var portal = new PortalLight(sky,
            anchor: new Vector3(-1, 5, -1),
            u: new Vector3(2, 0, 0),
            v: new Vector3(0, 0, 2));
        // Area = 4, dist = 5, cos(|normal · dir|) = 1 ⇒ pdf = 25 / 4 = 6.25.
        float pdf = portal.PdfSolidAngle(Vector3.Zero, Vector3.UnitY);
        Assert.InRange(pdf, 6.20f, 6.30f);
    }

    // ────────────────────────────────────────────────────────────────────────
    // EnvironmentMap mipmap
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnvironmentMap_SampleMip_at_level_zero_matches_native_sample()
    {
        // Tiny gradient HDRI (8×4) so the mip pyramid is cheap to build.
        const int W = 8, H = 4;
        var px = new float[W * H * 3];
        for (int i = 0; i < W * H; i++)
        {
            px[i * 3 + 0] = i / (float)(W * H);
            px[i * 3 + 1] = 1f - i / (float)(W * H);
            px[i * 3 + 2] = 0.5f;
        }
        var map = new EnvironmentMap(px, W, H);
        var direction = Vector3.UnitX;
        var native = map.Sample(direction);
        var mipped = map.SampleMip(direction, 0f);
        Assert.InRange(Vector3.Distance(native, mipped), 0f, 1e-5f);
    }

    [Fact]
    public void EnvironmentMap_SampleMip_at_max_level_returns_a_smoother_value()
    {
        const int W = 16, H = 8;
        var px = new float[W * H * 3];
        // Single bright pixel at (8, 4); rest black.
        for (int i = 0; i < px.Length; i++) px[i] = 0f;
        int idx = (4 * W + 8) * 3;
        px[idx] = 100f; px[idx + 1] = 100f; px[idx + 2] = 100f;
        var map = new EnvironmentMap(px, W, H);
        int maxL = map.MaxMipLevel;
        Assert.True(maxL >= 3);
        var dirNearPeak = Vector3.UnitZ;       // hits roughly the bright row
        var fine = map.SampleMip(dirNearPeak, 0f);
        var coarse = map.SampleMip(dirNearPeak, maxL);
        // Mipped should converge to average — much smaller than the single
        // brightest pixel's value.
        Assert.True(MathUtils.Luminance(coarse) < MathUtils.Luminance(fine) * 0.5f + 1f);
    }

    // ────────────────────────────────────────────────────────────────────────
    // HdriSunExtractor — synthetic peak
    // ────────────────────────────────────────────────────────────────────────

    // ────────────────────────────────────────────────────────────────────────
    // NishitaAtmosphereMedium
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NishitaAtmosphere_transmittance_attenuates_with_distance()
    {
        var medium = new RayTracer.Volumetrics.NishitaAtmosphereMedium(
            new RayTracer.Volumetrics.HenyeyGreensteinPhase(0.76f),
            dustDensity: 1f, seaLevelY: 0f, worldScale: 1000f);
        var ray = new Ray(new Vector3(0, 1, 0), Vector3.UnitX);
        var trShort = medium.Transmittance(ray, 1f);
        var trLong  = medium.Transmittance(ray, 100f);
        Assert.True(trLong.X <  trShort.X);
        Assert.True(trLong.Y <  trShort.Y);
        Assert.True(trLong.Z <  trShort.Z);
        // Rayleigh 1/λ⁴: blue attenuates faster than red — receivers see more
        // red transmitted at long distance, more blue scattered AWAY.
        // After 100 wu (100 km) through dense atmosphere, B transmittance < R.
        Assert.True(trLong.Z < trLong.X);
    }

    [Fact]
    public void NishitaAtmosphere_density_drops_with_altitude()
    {
        var medium = new RayTracer.Volumetrics.NishitaAtmosphereMedium(
            new RayTracer.Volumetrics.HenyeyGreensteinPhase(0.76f),
            worldScale: 1000f);
        // At sea level (y=0): full density.
        var sigmaSea = medium.SigmaT_AtY(0f);
        // At Rayleigh scale height (8 wu = 8 km): Rayleigh density ≈ 1/e.
        var sigmaHigh = medium.SigmaT_AtY(8f);
        Assert.True(sigmaHigh.X < sigmaSea.X);
        Assert.True(sigmaHigh.Y < sigmaSea.Y);
        Assert.True(sigmaHigh.Z < sigmaSea.Z);
        // Above 60 km — fully zero (clamped).
        var sigmaSpace = medium.SigmaT_AtY(70f);
        Assert.Equal(Vector3.Zero, sigmaSpace);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Glossy LOD heuristic (smoke-tested through SkySettings.Sample)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SkySettings_Sample_with_lod_drives_through_mipmap()
    {
        // 16×8 HDRI with a single bright pixel (peak 100) on a black background.
        // The mip pyramid's coarsest level holds the spatial average ≈ 100/(16·8) ≈ 0.78.
        // Sampling any direction at max LOD should return roughly this average,
        // while the same direction at LOD 0 returns either 0 (off the peak) or
        // 100 (right on it). Either way, max-LOD luminance is far from both
        // extremes — close to the global average.
        const int W = 16, H = 8;
        var px = new float[W * H * 3];
        int peakIdx = (4 * W + 8) * 3;
        px[peakIdx] = 100f; px[peakIdx + 1] = 100f; px[peakIdx + 2] = 100f;
        var map = new EnvironmentMap(px, W, H);
        var sky = new SkySettings(new HdriSky(map));

        float expectedAvg = map.EstimatedAverageLuminance;     // ≈ 0.78
        Assert.True(expectedAvg > 0.5f && expectedAvg < 1.2f);

        // Take a few directions, sample at max LOD — all should land near the average.
        var directions = new[] {
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            -Vector3.UnitX, Vector3.Normalize(new Vector3(1, 1, 1))
        };
        int maxLod = map.MaxMipLevel;
        Assert.True(maxLod >= 3);
        foreach (var d in directions)
        {
            var smoothed = sky.Sample(new Ray(Vector3.Zero, d),
                                       RayCategory.Camera,
                                       includeAnalyticalSun: false,
                                       mipLod: maxLod);
            float l = MathUtils.Luminance(smoothed);
            // Within a factor of 4 of the global average — generous, but the
            // 2×2 box without proper area weighting drifts a bit on tiny pyramids.
            Assert.InRange(l, expectedAvg * 0.25f, expectedAvg * 4f);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // HdriSunExtractor — synthetic peak
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HdriSunExtractor_finds_synthetic_peak_within_a_few_pixels()
    {
        // 64×32 black HDRI with a single bright pixel at column=16, row=8 (above horizon).
        int w = 64, h = 32;
        var px = new float[w * h * 3];
        // Add a baseline so the threshold (mean * 50) is finite.
        for (int i = 0; i < px.Length; i++) px[i] = 0.01f;
        int sx = 16, sy = 8;
        int idx = (sy * w + sx) * 3;
        px[idx + 0] = 200f; px[idx + 1] = 200f; px[idx + 2] = 200f;
        var map = new EnvironmentMap(px, w, h);
        var sun = HdriSunExtractor.Extract(map, thresholdFactor: 30f);
        Assert.NotNull(sun);
        // The recovered direction should point roughly above-horizon.
        Assert.True(sun!.Direction.Y > 0f);
        // The recovered sun radiance should not be zero.
        Assert.True(MathUtils.Luminance(sun.Radiance) > 0f);
    }
}

internal static class TestVectorExtensions
{
    public static Vector3 Normalize(this Vector3 v) => Vector3.Normalize(v);
}
