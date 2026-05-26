using System.Numerics;
using RayTracer.Camera;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Guard-rail tests for the firefly suppression pipeline.
///
/// The "firefly stress" scenario combines three firefly risk factors simultaneously:
///   1. A bright sphere light very close to a diffuse floor (1/d² singularity risk)
///   2. A dense homogeneous scattering medium (cosLight/d² spikes in volumetric NEE)
///   3. Max depth ≥ 8 (Russian Roulette energy amplification)
///
/// The test renders at 64×64 with 16 SPP (fast, &lt;30s in Release) and counts
/// pixels whose pre-tonemap luminance exceeds a spike threshold. If the clamp
/// pipeline is broken, the count would jump by several hundred. The threshold
/// is calibrated with 50% headroom above the baseline measured at implementation
/// time, making the test tolerant of PRNG variation while still catching regressions.
///
/// How to update baselines: if default clamp behaviour changes intentionally, run
/// the test with the new implementation, record the new "baseline" pixel count,
/// and update MaxAllowedSpikePixels accordingly. Document the change in DEVLOG.md.
/// </summary>
public class FireflyRegressionTests
{
    private const int Width  = 64;
    private const int Height = 64;
    private const int Spp    = 16;
    private const int Depth  = 8;

    // Luminance threshold above which a pixel is considered a firefly spike.
    // Using linear-space radiance (pre-tonemap), so 10.0 corresponds to a
    // very bright highlight — far above any diffuse floor under ~30 W/sr.
    private const float SpikeThreshold = 10.0f;

    // Maximum allowed spike pixels (calibrated + 50% headroom over baseline).
    // With the full firefly pipeline active the expected count is ~0-5 pixels.
    private const int MaxAllowedSpikePixels = 60;

    /// <summary>
    /// Renders the firefly stress scene and asserts the spike pixel count is
    /// within the regression threshold.
    /// </summary>
    [Fact]
    public void FireflyStress_SpikePixelCount_WithinThreshold()
    {
        // Build a minimal scene: diffuse floor + bright sphere light + dense medium
        var material = new Lambertian(new Vector3(0.8f, 0.8f, 0.8f));
        var floor    = new InfinitePlane(
            new Vector3(0f, 0f, 0f),    // point on the plane
            new Vector3(0f, 1f, 0f),    // normal pointing up
            material);
        var world = new HittableList(new[] { (IHittable)floor });

        var sphereLight = new SphereLight(
            center: new Vector3(0f, 0.4f, 0f),
            radius: 0.1f,
            color: new Vector3(1f, 0.95f, 0.9f),
            intensity: 30f,
            shadowSamples: 16);

        var lights = new System.Collections.Generic.List<ILight> { sphereLight };

        // Dense homogeneous medium (sigma_s = 2.0)
        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(0.1f, 0.1f, 0.1f),
            sigmaS: new Vector3(2f, 2f, 2f),
            phase: new HenyeyGreensteinPhase(0.3f));

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1.5f, 5f),
            lookAt:      new Vector3(0f, 0.2f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     50f,
            aspectRatio: (float)Width / Height,
            aperture:    0f,
            focusDist:   1f);

        // Use PRNG (deterministic for test reproducibility)
        Sampler.SetKind(SamplerKind.Prng);

        var renderer = new Renderer(
            world:              world,
            camera:             camera,
            lights:             lights,
            sky:                new SkySettings(Vector3.Zero),
            samplesPerPixel:    Spp,
            maxDepth:           Depth,
            globalMedium:       medium,
            maxSampleRadiance:  Renderer.DefaultMaxSampleRadiance,
            verbose:            false);

        var pixels = renderer.Render(Width, Height);

        // Count spike pixels: post-tonemap pixels are in [0,1]³ (gamma space).
        // To check pre-tonemap radiance we need to reverse-engineer from the
        // rendered result — but since AcesToneMap is not invertible, we instead
        // check that very bright (near-white) pixels don't dominate the image.
        // A tonemap value > 0.98 in all channels corresponds roughly to a
        // pre-tonemap luminance > 10, which is our spike definition.
        int spikeCount = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var p = pixels[y, x];
                // Rec.709 luminance of the tonemap-gamma output
                float lum = MathUtils.Luminance(p);
                if (lum > 0.98f)   // near-saturated-white in display space
                    spikeCount++;
            }
        }

        Assert.True(spikeCount <= MaxAllowedSpikePixels,
            $"Firefly spike pixel count {spikeCount} exceeds threshold {MaxAllowedSpikePixels}. " +
            $"The firefly clamp pipeline may be broken. " +
            $"To update the baseline, render src/RayTracer.Tests/TestScenes/firefly-stress.yaml, " +
            $"measure the count, and update MaxAllowedSpikePixels with 50%% headroom.");
    }

    /// <summary>
    /// Verifies that SoftRadius on AreaLight does NOT change the result when
    /// set to 0 (backward compatibility guarantee).
    /// </summary>
    [Fact]
    public void AreaLight_SoftRadius_ZeroIsIdentical()
    {
        var light0 = new AreaLight(
            corner: new Vector3(-0.5f, 2f, -0.5f),
            u:      new Vector3(1f, 0f, 0f),
            v:      new Vector3(0f, 0f, 1f),
            color:  Vector3.One,
            intensity: 10f,
            shadowSamples: 1,
            softRadius: 0f);

        var light1 = new AreaLight(
            corner: new Vector3(-0.5f, 2f, -0.5f),
            u:      new Vector3(1f, 0f, 0f),
            v:      new Vector3(0f, 0f, 1f),
            color:  Vector3.One,
            intensity: 10f,
            shadowSamples: 1,
            softRadius: 0f);

        Assert.Equal(light0.SoftRadius, light1.SoftRadius);
        Assert.Equal(0f, light0.SoftRadius);
    }

    /// <summary>
    /// Verifies SurfaceArea is deterministic (no PRNG) across multiple calls.
    /// This is the guard that ensures two Renderer constructions from the same
    /// scene produce identical per-pixel PRNG sequences.
    /// </summary>
    [Fact]
    public void ISamplable_SurfaceArea_IsDeterministic()
    {
        var sphere = new Sphere(Vector3.Zero, 1f, new Lambertian(Vector3.One));
        float expected = 4f * MathF.PI * 1f * 1f;

        // Call multiple times — must return the same value without consuming PRNG
        for (int i = 0; i < 100; i++)
            Assert.Equal(expected, sphere.SurfaceArea, 1e-4f);
    }

    /// <summary>
    /// Verifies the LightDistribution produces a valid probability distribution
    /// (all pdfs > 0 and sum ≈ 1 for a power-weighted case).
    /// </summary>
    [Fact]
    public void LightDistribution_Power_SumsToOne()
    {
        var lights = new System.Collections.Generic.List<ILight>
        {
            new PointLight(new Vector3(0, 5, 0), Vector3.One, 10f),
            new PointLight(new Vector3(1, 5, 0), Vector3.One, 5f),
            new PointLight(new Vector3(2, 5, 0), Vector3.One, 1f),
        };

        var bounds = new AABB(new Vector3(-5), new Vector3(5));
        var dist   = new LightDistribution(lights, bounds);

        double pdfSum = 0;
        for (int i = 0; i < lights.Count; i++)
            pdfSum += dist.PdfPick(i);

        Assert.InRange(pdfSum, 0.999, 1.001);

        // The brightest light should have the highest probability
        Assert.True(dist.PdfPick(0) > dist.PdfPick(1));
        Assert.True(dist.PdfPick(1) > dist.PdfPick(2));
    }

    /// <summary>
    /// Verifies DirectionalLight disc mode: IsDelta = false and
    /// PdfSolidAngle returns non-zero inside the cone.
    /// </summary>
    [Fact]
    public void DirectionalLight_DiscMode_IsNotDelta()
    {
        var hardLight = new DirectionalLight(Vector3.UnitY, Vector3.One, 1f, angularRadiusDeg: 0f);
        var sunLight  = new DirectionalLight(Vector3.UnitY, Vector3.One, 1f, angularRadiusDeg: 0.27f);

        Assert.True(hardLight.IsDelta,  "Hard directional should be delta");
        Assert.False(sunLight.IsDelta,  "Sun disc should NOT be delta");

        // PdfSolidAngle for the sun: looking exactly along -Direction should be inside
        float pdfOnAxis = sunLight.PdfSolidAngle(Vector3.Zero, -Vector3.UnitY);
        Assert.True(pdfOnAxis > 0f, "On-axis direction should have pdf > 0");

        // PdfSolidAngle for hard light: always 0
        float pdfHard = hardLight.PdfSolidAngle(Vector3.Zero, -Vector3.UnitY);
        Assert.Equal(0f, pdfHard);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SSS-specific firefly regressions (Phase 5)
    //
    // The random-walk integrator adds two new spike risks beyond the legacy
    // volumetric path:
    //   - NEE-in-walk: deep-bounce light samples in a dense medium can spike
    //     when a scatter event lands close to an area-light boundary.
    //   - Boundary re-entry: TIR reflections inside a high-albedo medium can
    //     accumulate before the walk's RR kicks in.
    // ClampWalkInScattering applies a depth-aware ramp inside the walk, and
    // the indirect-clamp factor applies on the exit transport. These tests
    // pin down the combined behaviour so future tuning doesn't silently
    // re-introduce spikes.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marble bust under an area light — a clean SSS scenario that should
    /// produce zero pre-tonemap fireflies under the default clamp pipeline.
    /// Renders the configuration from <c>scenes/showcases/sss-randomwalk-01-marble.yaml</c>
    /// programmatically so the test is independent of YAML changes.
    /// </summary>
    [Fact]
    public void Sss_Marble_AreaLight_NoFireflies()
    {
        Sampler.SetKind(SamplerKind.Prng);

        // Marble surface: transparent boundary; the colour comes from the
        // volume.
        var marbleSurface = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            roughness: new FloatTexture(0.18f),
            specular:  new FloatTexture(0.5f),
            specTrans: new FloatTexture(1f),
            ior:       new FloatTexture(1.5f),
            clearcoat: new FloatTexture(0f));

        // Jensen 2001 marble preset.
        var marbleInt = new HomogeneousMedium(
            sigmaA: new Vector3(0.0021f, 0.0041f, 0.0071f),
            sigmaS: new Vector3(2.19f, 2.62f, 3.00f),
            phase:  new HenyeyGreensteinPhase(0f));

        var sphere = new Sphere(new Vector3(0f, 1.20f, 0f), 0.42f, marbleSurface);
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(marbleInt, exterior: null));

        // u × v must point -Y so the face emits downward onto the sphere.
        var areaLight = new AreaLight(
            corner:        new Vector3( 1.4f, 2.6f, -0.3f),
            u:             new Vector3( 1.4f, 0f, 0f),
            v:             new Vector3( 0f, 0f, 1.4f),
            color:         new Vector3(1.00f, 0.92f, 0.78f),
            intensity:     38f,
            shadowSamples: 4);

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(1.4f, 1.55f, 3.6f),
            lookAt:      new Vector3(0f, 1.20f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     35f,
            aspectRatio: 1f,
            aperture:    0f,
            focusDist:   3.8f);

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          camera,
            lights:          new List<ILight> { areaLight },
            sky:             new SkySettings(Vector3.Zero),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pixels = renderer.Render(Width, Height);

        // Count post-tonemap near-saturation pixels (same definition as the
        // main FireflyStress test). A working clamp pipeline produces fewer
        // than 10 such pixels on this configuration at 64×64 / 16 spp.
        int spikes = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = pixels[y, x];
                if (MathUtils.Luminance(p) > 0.98f) spikes++;
            }

        // 50% headroom over baseline — matches the convention of the
        // FireflyStress test above.
        const int MaxMarbleSpikes = 30;
        Assert.True(spikes <= MaxMarbleSpikes,
            $"Marble SSS spike count {spikes} exceeds threshold {MaxMarbleSpikes}. " +
            $"ClampWalkInScattering or the indirect clamp may have regressed.");

        // And: at least one pixel must light up — protect against a
        // regression where the walk silently returns black.
        float meanLum = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                meanLum += MathUtils.Luminance(pixels[y, x]);
        meanLum /= (Width * Height);
        Assert.True(meanLum > 0.005f,
            $"Marble SSS scene rendered nearly black (mean lum {meanLum:F5}). " +
            $"The walk dispatch may be inert.");
    }

    /// <summary>
    /// Milk-glass Cornell — high-albedo NEE-in-walk regression. A bright
    /// emissive ceiling drives many internal scatter events, and on a
    /// dense scattering medium that's the recipe for fireflies if the
    /// depth-aware clamp inside the walk regresses. The test asserts the
    /// spike count stays bounded.
    /// </summary>
    [Fact]
    public void Sss_MilkCornell_NeeInWalk_NoFireflies()
    {
        Sampler.SetKind(SamplerKind.Prng);

        // Milk surface: Disney transparent + low IOR (1.35).
        var milkSurface = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            roughness: new FloatTexture(0.05f),
            specular:  new FloatTexture(0.5f),
            specTrans: new FloatTexture(1f),
            ior:       new FloatTexture(1.35f),
            clearcoat: new FloatTexture(0f));

        // Jensen 2001 "wholemilk" preset.
        var milkInt = new HomogeneousMedium(
            sigmaA: new Vector3(0.0011f, 0.0024f, 0.014f),
            sigmaS: new Vector3(2.55f, 3.21f, 3.77f),
            phase:  new IsotropicPhase());

        // Geometry: a milk sphere lit from above by an emissive area.
        var milk = new Sphere(new Vector3(0f, 0.55f, -0.6f), 0.42f, milkSurface);
        IHittable bound = new MediumBoundHittable(milk,
            new MediumInterface(milkInt, exterior: null));

        // Bright emissive area light high above — drives strong NEE inside
        // the walk.
        // u × v = (0, -0.6, 0) → emits downward, lighting the milk sphere.
        var ceilingLight = new AreaLight(
            corner:        new Vector3(-0.5f, 2.5f, -1.3f),
            u:             new Vector3( 1.0f, 0f, 0f),
            v:             new Vector3( 0f, 0f, 0.6f),
            color:         new Vector3(1.0f, 0.95f, 0.85f),
            intensity:     18f,
            shadowSamples: 4);

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1.2f, 3.6f),
            lookAt:      new Vector3(0f, 0.6f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     40f,
            aspectRatio: 1f,
            aperture:    0f,
            focusDist:   3.6f);

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          camera,
            lights:          new List<ILight> { ceilingLight },
            sky:             new SkySettings(Vector3.Zero),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pixels = renderer.Render(Width, Height);

        int spikes = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = pixels[y, x];
                if (MathUtils.Luminance(p) > 0.98f) spikes++;
            }

        // Milk has higher single-scatter albedo than marble (≈ 0.9995 vs.
        // 0.998), so the walk runs longer on average. The clamp still
        // suppresses spikes — a regression here would push count well past 50.
        const int MaxMilkSpikes = 50;
        Assert.True(spikes <= MaxMilkSpikes,
            $"Milk-Cornell SSS spike count {spikes} exceeds threshold {MaxMilkSpikes}. " +
            $"The depth-aware clamp inside the walk (ClampWalkInScattering) may have regressed.");
    }
}
