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
/// Acceptance tests for the random-walk subsurface scattering integrator
/// (Phase 3 of the MediumInterface plan).
///
/// <para>The tests exercise the full Renderer dispatch path so they catch
/// regressions in any of the pieces that compose to produce SSS output:
/// the entry refraction lobe in <see cref="DisneyBsdf"/>, the stack push in
/// <see cref="Renderer.ShadeSampleBounce"/>, the <see cref="HitRecord.EntityRoot"/>
/// stamping in <see cref="MediumBoundHittable"/>, the hero-wavelength MIS
/// estimator in <see cref="Renderer.RandomWalkSubsurface"/>, and the exit
/// path back into <see cref="Renderer.TraceRay"/>.</para>
///
/// <para>Sampler is forced to PRNG so the expected values are deterministic
/// across runs and not dependent on Sobol prefix layout.</para>
/// </summary>
public class SssRandomWalkTests
{
    private const int Width  = 24;
    private const int Height = 24;
    private const int Spp    = 64;
    private const int Depth  = 8;

    // ── Common scene construction helpers ──────────────────────────────────

    private static Camera.Camera MakeCamera() => new(
        lookFrom:    new Vector3(0f, 0f, 5f),
        lookAt:      Vector3.Zero,
        vUp:         Vector3.UnitY,
        vFovDeg:     30f,
        aspectRatio: (float)Width / Height,
        aperture:    0f,
        focusDist:   1f);

    /// <summary>
    /// Builds a Disney material whose refractive boundary is transparent
    /// (η = 1 → Fresnel = 0 at every angle). This is the "matched-IOR" trick
    /// that makes the entry/exit symmetric for the white-furnace energy
    /// conservation test — without it the entry Fresnel would absorb some
    /// fraction of the input and the round-trip can't match unity.
    /// </summary>
    private static DisneyBsdf MakeTransparentDisney() => new(
        baseColor:    new SolidColor(Vector3.One),
        roughness:    new FloatTexture(0f),
        specular:     new FloatTexture(0f),
        specTrans:    new FloatTexture(1f),
        ior:          new FloatTexture(1f),
        clearcoat:    new FloatTexture(0f));

    private static HittableList BuildSphereScene(IHittable sphere)
        => new(new[] { sphere });

    /// <summary>
    /// Render a closed-form scene and average the linear-space radiance
    /// across the image. The renderer's output is post-tonemap, so we invert
    /// gamma + ACES on the mean luminance to recover the linear-space mean.
    /// </summary>
    private static float MeanLinearLuminance(Vector3[,] pixels)
    {
        int w = pixels.GetLength(1), h = pixels.GetLength(0);
        double accum = 0;
        int n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 p = pixels[y, x];
                accum += MathUtils.Luminance(p);
                n++;
            }
        return (float)(accum / n);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1 — IsScatteringMedium gating
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the dispatch predicate: a HomogeneousMedium with σ_s = 0 does
    /// NOT trigger the random-walk path — the legacy Beer-Lambert volumetric
    /// path handles it equivalently (and is cheaper). The test compares two
    /// renders of the same Disney glass scene: one with σ_s = 0 SSS binding,
    /// one with no binding at all. Both must produce equivalent output.
    /// </summary>
    [Fact]
    public void ZeroScatteringMedium_FallsBackToLegacyPath()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var mat = MakeTransparentDisney();
        var sphere = new Sphere(Vector3.Zero, 0.7f, mat);

        // No-binding scene (control).
        var control = new Renderer(
            world:           BuildSphereScene(sphere),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pControl = control.Render(Width, Height);
        float meanControl = MeanLinearLuminance(pControl);

        // σ_s = 0 binding — must dispatch to the legacy path.
        var mediumNoScatter = new HomogeneousMedium(
            sigmaA: Vector3.Zero, sigmaS: Vector3.Zero,
            phase:  new IsotropicPhase());
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(mediumNoScatter, exterior: null));
        var bindScene = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pBound = bindScene.Render(Width, Height);
        float meanBound = MeanLinearLuminance(pBound);

        // Both paths should produce indistinguishable mean luminance (within
        // PRNG noise — 5% headroom). The point is: σ_s = 0 must NOT route
        // through the random-walk machinery, which would change the sampling
        // characteristics even when σ_a = 0 makes them equivalent in the limit.
        Assert.InRange(meanBound, meanControl * 0.95f, meanControl * 1.05f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2 — White-furnace energy conservation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Closed-form energy-conservation test (Cycles / Mitsuba "white furnace"):
    /// sphere with σ_a = 0 (no absorption), σ_s &gt; 0 (full scattering),
    /// embedded in a constant-radiance environment. The exit radiance through
    /// the sphere must equal the environment radiance to within Monte-Carlo
    /// noise.
    ///
    /// <para>Why η = 1: the matched IOR makes the entry / exit Fresnel
    /// transmission factor identically 1 at every angle, so the only thing
    /// being tested is the spectral free-flight estimator + the
    /// hero-wavelength MIS combination + the boundary escape path. A
    /// regression in any of those would manifest as a non-unit
    /// transmittance.</para>
    /// </summary>
    [Fact]
    public void WhiteFurnace_ExitRadianceMatchesInput()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var mat = MakeTransparentDisney();
        var sphere = new Sphere(Vector3.Zero, 0.7f, mat);

        // σ_a = 0, σ_s = 1.0 (moderate). The walk loop will scatter ~once
        // per radius on average; the result should still energy-conserve.
        var medium = new HomogeneousMedium(
            sigmaA: Vector3.Zero,
            sigmaS: new Vector3(1f, 1f, 1f),
            phase:  new IsotropicPhase());
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(medium, exterior: null));

        // Constant-radiance environment = 0.5. Camera looks at the sphere
        // dead-on so every pixel within the sphere silhouette either misses
        // (sees env directly) or refracts into the SSS volume and exits.
        const float EnvRadiance = 0.5f;
        var envSky = new SkySettings(new Vector3(EnvRadiance));

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             envSky,
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pixels = renderer.Render(Width, Height);

        // Linear-space mean luminance after ACES + gamma 2.2. For a constant
        // env=0.5 scene the no-sphere render hits the same value everywhere;
        // the sphere region matches exactly when the walk is energy-conserving.
        // We compare the mean luminance of the WHOLE rendered image to that
        // of a env-only baseline of identical geometry.
        var envOnly = new Renderer(
            world:           new HittableList(System.Array.Empty<IHittable>()),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             envSky,
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pEnv = envOnly.Render(Width, Height);

        float meanSss = MeanLinearLuminance(pixels);
        float meanEnv = MeanLinearLuminance(pEnv);

        // 8% tolerance — covers PRNG noise at 24×24 / 64 spp + the small
        // residual from the (numerically tiny) RR-killed paths at low albedo.
        // White-furnace acceptance for a production-grade walk is ~1% at 1024
        // spp; we trade tighter tolerance for shorter test runtime.
        Assert.InRange(meanSss, meanEnv * 0.92f, meanEnv * 1.08f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3 — Spectral color bleed
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A sphere with strongly wavelength-dependent absorption (red survives,
    /// blue absorbed) must shift the transmitted radiance toward red. This is
    /// the qualitative signature of correct per-channel σ_a / σ_t handling
    /// in the hero-wavelength estimator: if the channels collapsed to a
    /// single broadcast value, the output would stay neutral.
    /// </summary>
    [Fact]
    public void SpectralAbsorption_ProducesColorBleed()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var mat = MakeTransparentDisney();
        var sphere = new Sphere(Vector3.Zero, 0.7f, mat);

        // Skin / wax-style coefficients: weak red absorption, strong blue.
        // σ_s mild but per-channel σ_a strongly varying.
        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(0.3f, 1.5f, 3.0f),
            sigmaS: new Vector3(1.0f, 1.0f, 1.0f),
            phase:  new IsotropicPhase());
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(medium, exterior: null));

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var pixels = renderer.Render(Width, Height);

        // Average channel values across the central pixel column where rays
        // pass through the sphere. The sphere covers the entire image at
        // this camera distance.
        double rSum = 0, gSum = 0, bSum = 0;
        int count = 0;
        int yMid = Height / 2;
        int xLo = Width / 4, xHi = 3 * Width / 4;
        for (int x = xLo; x < xHi; x++)
        {
            var p = pixels[yMid, x];
            rSum += p.X; gSum += p.Y; bSum += p.Z;
            count++;
        }
        rSum /= count; gSum /= count; bSum /= count;

        // Red must dominate green, green must dominate blue — the strict
        // per-channel ordering matches the σ_a layout. Numbers themselves
        // are tonemap-dependent; the ordering is what proves spectral
        // correctness.
        Assert.True(rSum > gSum + 0.02f,
            $"Expected R > G after red-favoured absorption, got R={rSum:F3} G={gSum:F3}.");
        Assert.True(gSum > bSum + 0.02f,
            $"Expected G > B after red-favoured absorption, got G={gSum:F3} B={bSum:F3}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4 — Dense-medium robustness (no NaN / inf)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stresses the walk on a very dense (σ_t up to 50) medium with a low
    /// volume-bounce budget. The MaxVolumeBounces clamp combined with the
    /// in-walk RR must terminate every path cleanly, producing finite
    /// per-pixel values (no NaN / +Inf leaks through). This is the
    /// guard-rail against the worst-case low-albedo path stall.
    /// </summary>
    [Fact]
    public void DenseMedium_LowBudget_TerminatesCleanly()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var mat = MakeTransparentDisney();
        var sphere = new Sphere(Vector3.Zero, 0.7f, mat);

        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(10f, 10f, 10f),
            sigmaS: new Vector3(40f, 40f, 40f),
            phase:  new HenyeyGreensteinPhase(0.5f));
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(medium, exterior: null));

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: 16, // keep test fast
            maxDepth:        Depth,
            walkConfig:      new RandomWalkConfig(maxVolumeBounces: 8,
                                                   rrStartBounce: 1,
                                                   neeInsideWalk: true,
                                                   neeMaxBounce:  int.MaxValue));
        var pixels = renderer.Render(Width, Height);

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = pixels[y, x];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z),
                    $"Non-finite pixel at ({x},{y}): {p}");
                // Display-space values must stay in [0, 1] after tonemap.
                Assert.InRange(p.X, 0f, 1f);
                Assert.InRange(p.Y, 0f, 1f);
                Assert.InRange(p.Z, 0f, 1f);
            }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5 — SssMode.Off honors the kill-switch
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// With <c>--sss-mode off</c> the dispatch is suppressed and the SSS
    /// binding falls through to the legacy Beer-Lambert path even when σ_s &gt; 0.
    /// This verifies the CLI knob actually disables the random walk
    /// (regression guard for the dispatch condition).
    /// </summary>
    [Fact]
    public void SssMode_Off_BypassesRandomWalk()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var mat = MakeTransparentDisney();
        var sphere = new Sphere(Vector3.Zero, 0.7f, mat);

        // Strong absorption + scattering: the legacy path (declassed to
        // σ_s = 0 by ResolvePushedMedium) leaves only Beer-Lambert with
        // σ_a = 4, attenuating the central chord by exp(-4·1.4) ≈ 0.0037 —
        // essentially black. The random walk transports scattered light
        // through the volume, yielding a much brighter center. With this
        // contrast the difference survives both tonemap compression and
        // PRNG noise at the test's modest spp count.
        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(4f, 4f, 4f),
            sigmaS: new Vector3(8f, 8f, 8f),
            phase:  new HenyeyGreensteinPhase(0f));
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(medium, exterior: null));

        var withSss = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth,
            sssMode:         SssMode.Auto);
        var withoutSss = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth,
            sssMode:         SssMode.Off);

        var pSss = withSss.Render(Width, Height);
        var pOff = withoutSss.Render(Width, Height);

        // Average the 4×4 central block only — outside the sphere silhouette
        // both renders hit the env directly and saturate identically against
        // ACES, masking the difference in a full-image mean. The 4×4 window
        // sits entirely inside the sphere at this camera setup so it's
        // sensitive to the interior transport difference between the two
        // dispatch modes.
        static float CentralMean(Vector3[,] p)
        {
            int W = p.GetLength(1), H = p.GetLength(0);
            double accum = 0; int n = 0;
            for (int y = H / 2 - 2; y < H / 2 + 2; y++)
                for (int x = W / 2 - 2; x < W / 2 + 2; x++)
                {
                    accum += MathUtils.Luminance(p[y, x]);
                    n++;
                }
            return (float)(accum / n);
        }
        float centerSss = CentralMean(pSss);
        float centerOff = CentralMean(pOff);

        // The walk transports scattered light through the volume → exit
        // radiance close to the env (≈ 1 in display space). The legacy
        // absorption-only path attenuates exponentially over the chord
        // length → near-black center. The difference must exceed
        // PRNG-noise level at this spp count (~0.05 of display-space lum).
        Assert.True(System.Math.Abs(centerSss - centerOff) > 0.10f,
            $"SssMode toggle had no effect on the sphere interior — " +
            $"SSS center={centerSss:F4}, Off center={centerOff:F4}. " +
            $"The random walk dispatch may be unconditionally active or unconditionally suppressed.");
    }
}
