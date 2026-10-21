using System.Numerics;
using RayTracer.Materials;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Gates built on <see cref="BsdfTestKit"/> (PIANO-REVIEW.md, Phase 0). The
/// passing tests lock in what was verified correct in the Phase 0 baseline;
/// the skipped ones reproduce a known bug and name the plan section that
/// fixes it, to be enabled when that fix lands. The budget is smaller than the
/// audit's (<see cref="BsdfAuditTests"/>) to keep the suite fast; seeds are
/// fixed, so the results are deterministic.
/// </summary>
public class BsdfStatisticalTests
{
    private const int Samples = 100_000;
    private static readonly float[] Views = { 0.8f, 0.3f };

    // Šidák-style threshold for the whole family of chi-square tests: a correct
    // sampler fails one of ~40 tests with probability ~0.4%.
    private const double ChiSquareAlpha = 1e-4;

    private static IMaterial Get(string name) => BsdfCatalog.All().First(e => e.Name == name).Material;

    // Several tests read the same (material, view) report: compute it once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, float), Lazy<BsdfTestKit.Report>> Cache = new();

    private static BsdfTestKit.Report Run(string name, float cosV) =>
        Cache.GetOrAdd((name, cosV), key => new Lazy<BsdfTestKit.Report>(() =>
            BsdfTestKit.Run(key.Item1, Get(key.Item1), key.Item2, samples: Samples, cosBins: 16, phiBins: 32, sub: 8))).Value;

    public static TheoryData<string> ChiSquareMaterials() => new()
    {
        "lambert-0.8", "metal-white-fuzz0.3", "metal-white-fuzz0.6", "metal-white-fuzz1.0",
        "disney-plastic-r0.5", "disney-plastic-r0.3", "disney-plastic-r1.0",
        "disney-metal-white-r0.3", "disney-metal-white-r0.6", "disney-metal-aniso-r0.5",
        "disney-sheen", "disney-coat-rough", "disney-glass-rough0.4", "mix-lambert-metal",
    };

    [Theory]
    [MemberData(nameof(ChiSquareMaterials))]
    public void Sampler_DrawsFromItsPdf(string name)
    {
        foreach (float cosV in Views)
        {
            var r = Run(name, cosV);
            Assert.True(r.PValue > ChiSquareAlpha, r.ToString());
            Assert.Equal(0, r.InconsistentSamples);
        }
    }

    [Theory]
    [MemberData(nameof(ChiSquareMaterials))]
    public void SampleWeight_IntegratesTheBsdf(string name)
    {
        // Non-delta albedo from the sampler (Σ f·cos/pdf) against ∫ f·cos by
        // quadrature: a gap means the estimator is biased.
        foreach (float cosV in Views)
        {
            var r = Run(name, cosV);
            Assert.True(r.AlbedoCrossCheckPasses, r.ToString());
        }
    }

    public static TheoryData<string> EnergyConservingMaterials() => new()
    {
        "lambert-0.8", "metal-white-fuzz0.3", "metal-white-fuzz0.6", "metal-white-fuzz1.0",
        "disney-plastic-r0.5", "disney-metal-white-r0.3", "disney-metal-white-r0.6",
        "disney-metal-aniso-r0.5", "disney-coat-rough", "disney-difftrans", "mix-lambert-metal",
    };

    [Theory]
    [MemberData(nameof(EnergyConservingMaterials))]
    public void WhiteFurnace_AlbedoDoesNotExceedOne(string name)
    {
        foreach (float cosV in Views)
        {
            var r = Run(name, cosV);
            // 1% covers Monte Carlo noise and the Kulla-Conty LUT interpolation.
            Assert.True(r.MaxAlbedo <= 1.01f, r.ToString());
        }
    }

    [Theory]
    [InlineData("lambert-0.8")]
    [InlineData("metal-white-fuzz0.6")]
    [InlineData("disney-plastic-r0.5")]
    [InlineData("disney-metal-aniso-r0.5")]
    [InlineData("disney-difftrans")]
    [InlineData("mix-lambert-metal")]
    // Not here by design (OpenPBR layering, PIANO-REVIEW.md §3.38): the coat
    // and the fuzz (sheen) scale what lies below by the view direction's
    // albedo, the form that conserves energy over any base, not symmetric.
    public void Bsdf_IsReciprocal(string name)
        => Assert.True(BsdfTestKit.ReciprocityError(Get(name)) < 1e-3);

    [Theory]
    [InlineData("lambert-0.8")]
    [InlineData("disney-plastic-r0.5")]
    [InlineData("disney-metal-white-r0.6")]
    [InlineData("disney-metal-aniso-r0.5")]
    [InlineData("disney-sheen")]
    [InlineData("disney-coat-rough")]
    [InlineData("disney-difftrans")]
    [InlineData("disney-glass-rough0.4")]
    public void DirectLighting_UsesTheSameBsdfAsSampling(string name)
        => Assert.True(BsdfTestKit.DirectVsEvaluateError(Get(name)) < 1e-3);

    // ── Known bugs (PIANO-REVIEW.md §3): enable when the fix lands ───────────

    // Was the known bug §3.7 (a Lambertian lobe in the Metal's direct lighting only), fixed in Phase 5.
    [Theory]
    [InlineData("metal-white-fuzz0.3")]
    [InlineData("metal-white-fuzz1.0")]
    [InlineData("mix-lambert-metal")]
    public void Metal_DirectLightingMatchesSampling(string name)
        => Assert.True(BsdfTestKit.DirectVsEvaluateError(Get(name)) < 1e-3);

    // Was the known bug §3.2 (delta lobes weighted by max(p, 0.1) but chosen
    // with p), fixed in Phase 5 by the translation onto OpenPBR.
    [Fact]
    public void Disney_SmoothSpecularMatchesFresnel()
    {
        // Smooth plastic, specular 0.5 → IOR 1.5: the delta part of the
        // albedo is the exact Fresnel reflectance of the mirror lobe.
        foreach (float cosV in Views)
        {
            var r = Run("disney-plastic-smooth", cosV);
            float deltaPart = r.Albedo.X - r.AlbedoNonDeltaSampled.X;
            float fresnel = RayTracer.Core.MathUtils.FresnelDielectric(cosV, 1f / 1.5f);
            Assert.InRange(deltaPart, fresnel * 0.98f, fresnel * 1.02f);
        }
    }

    // Was the known bug §3.17 (sheen, clearcoat and glass created energy), fixed in Phase 5.
    [Theory]
    [InlineData("disney-sheen")]
    [InlineData("disney-coat-smooth")]
    [InlineData("disney-glass-smooth")]
    [InlineData("disney-glass-rough0.4")]
    [InlineData("disney-plastic-r1.0")]
    public void Disney_WhiteFurnaceAlbedoDoesNotExceedOne(string name)
    {
        // The gain grows toward grazing view, so this also checks cosθ = 0.2.
        foreach (float cosV in new[] { 0.8f, 0.3f, 0.2f })
        {
            var r = Run(name, cosV);
            Assert.True(r.MaxAlbedo <= 1.01f, r.ToString());
        }
    }

    // Was the known bug §3.18 (pdf mass below the horizon where diff_trans has support), fixed in Phase 5.
    [Fact]
    public void Disney_DiffTransSampleWeightIntegratesTheBsdf()
        => SampleWeight_IntegratesTheBsdf("disney-difftrans");
}
