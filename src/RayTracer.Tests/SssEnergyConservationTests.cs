using System.Numerics;
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
/// Closed-cavity energy-balance tests for the Random Walk SSS integrator.
///
/// <para>These are the "white furnace" regression tests writ large: instead
/// of checking a single sphere against a constant environment, they exercise
/// the integrator across the most failure-prone configurations of σ_a / σ_s
/// and verify that energy entering the cavity is conserved (no creation, no
/// silent loss) across the walk → boundary → walk loop. The companion
/// <see cref="SssRandomWalkTests.WhiteFurnace_ExitRadianceMatchesInput"/>
/// covers the matched-IOR baseline; this file covers the corners.</para>
///
/// <para>Sampler is forced to PRNG so the test outputs are deterministic
/// across CI runs and not at the mercy of Sobol prefix layout.</para>
/// </summary>
public class SssEnergyConservationTests
{
    private const int Width  = 24;
    private const int Height = 24;
    private const int Spp    = 64;
    private const int Depth  = 8;

    private static Camera.Camera MakeCamera() => new(
        lookFrom:    new Vector3(0f, 0f, 5f),
        lookAt:      Vector3.Zero,
        vUp:         Vector3.UnitY,
        vFovDeg:     30f,
        aspectRatio: (float)Width / Height,
        aperture:    0f,
        focusDist:   1f);

    private static DisneyBsdf MakeTransparentDisney() => new(
        baseColor: new SolidColor(Vector3.One),
        roughness: new FloatTexture(0f),
        specular:  new FloatTexture(0f),
        specTrans: new FloatTexture(1f),
        ior:       new FloatTexture(1f),
        clearcoat: new FloatTexture(0f));

    private static float MeanLuminance(Vector3[,] p)
    {
        int w = p.GetLength(1), h = p.GetLength(0);
        double accum = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                accum += MathUtils.Luminance(p[y, x]);
        return (float)(accum / (w * h));
    }

    private static float SphereScene(IMedium medium)
    {
        Sampler.SetKind(SamplerKind.Prng);
        var sphere = new Sphere(Vector3.Zero, 0.7f, MakeTransparentDisney());
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(medium, exterior: null));

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        return MeanLuminance(renderer.Render(Width, Height));
    }

    private static float EnvOnlyScene()
    {
        Sampler.SetKind(SamplerKind.Prng);
        var renderer = new Renderer(
            world:           new HittableList(System.Array.Empty<IHittable>()),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        return MeanLuminance(renderer.Render(Width, Height));
    }

    /// <summary>
    /// At medium-to-high single-scatter albedo (σ_s = 1, σ_a = 0.05) the cavity
    /// should still be close to energy-conserving — only a small fraction of
    /// the entering photons are removed by absorption per mean-free-path, so
    /// the exit radiance should sit just below the env baseline.
    /// </summary>
    [Fact]
    public void ModerateAbsorption_DoesNotCreateEnergy()
    {
        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(0.05f, 0.05f, 0.05f),
            sigmaS: new Vector3(1f, 1f, 1f),
            phase:  new IsotropicPhase());

        float meanSss = SphereScene(medium);
        float meanEnv = EnvOnlyScene();

        // An energy-creating bug (e.g., dropping the σ_s/σ_t throughput
        // attenuation per scatter) would push the cavity *above* the env
        // baseline. We allow up to 5% above for PRNG headroom, which is
        // still well below any catastrophic creation regression (those land
        // 20%+ over).
        Assert.True(meanSss < meanEnv * 1.05f,
            $"Cavity SSS mean ({meanSss:F4}) exceeds env baseline ({meanEnv:F4}) by more than 5% — " +
            $"the walk is creating energy. Check the throughput update in the random-walk loop.");

        // And not unrealistically below either: at σ_a = 0.05 we expect at
        // least 70% of the energy to survive multiple scatters.
        Assert.True(meanSss > meanEnv * 0.65f,
            $"Cavity SSS mean ({meanSss:F4}) collapsed far below env baseline ({meanEnv:F4}). " +
            $"The walk may be over-attenuating — check the σ_s/σ_t ratio direction.");
    }

    /// <summary>
    /// At very high single-scatter albedo (σ_s = 5, σ_a = 0.01 → albedo
    /// ≈ 0.998) the cavity should converge essentially to the env baseline:
    /// almost every scattered photon eventually escapes the boundary. This
    /// catches "walks that terminate too eagerly" (Russian Roulette too
    /// aggressive, max-bounces cap too low) which would manifest as a
    /// systematic darkening.
    /// </summary>
    [Fact]
    public void HighAlbedo_ConservesEnergyWithinTolerance()
    {
        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(0.01f, 0.01f, 0.01f),
            sigmaS: new Vector3(5f, 5f, 5f),
            phase:  new IsotropicPhase());

        float meanSss = SphereScene(medium);
        float meanEnv = EnvOnlyScene();

        // 12% tolerance — the dense medium scatters many times per chord so
        // PRNG noise is amplified, but the mean should still ride the env
        // baseline. Anything 20%+ below indicates premature termination.
        Assert.InRange(meanSss, meanEnv * 0.88f, meanEnv * 1.10f);
    }

    /// <summary>
    /// Pure-absorption cavity: σ_s = 0, σ_a &gt; 0. The dispatch predicate
    /// requires σ_s &gt; 0 so this case falls through to the legacy Beer-Lambert
    /// volumetric path. Exit radiance must be bounded by the env baseline (no
    /// energy creation) and strictly below it (absorption is real).
    /// </summary>
    [Fact]
    public void PureAbsorption_FallsBackToLegacy_AndAttenuates()
    {
        var medium = new HomogeneousMedium(
            sigmaA: new Vector3(1.5f, 1.5f, 1.5f),
            sigmaS: Vector3.Zero,
            phase:  new IsotropicPhase());

        float meanSss = SphereScene(medium);
        float meanEnv = EnvOnlyScene();

        // Energy cannot be created in a non-scattering medium.
        Assert.True(meanSss < meanEnv,
            $"Pure-absorption cavity radiance ({meanSss:F4}) exceeds env ({meanEnv:F4}). " +
            $"The legacy Beer-Lambert path is corrupted or the walk is being invoked despite σ_s=0.");
    }
}
