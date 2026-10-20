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
    [InlineData("disney-sheen")]
    [InlineData("disney-coat-rough")]
    [InlineData("disney-difftrans")]
    [InlineData("mix-lambert-metal")]
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

    [Theory(Skip = "Bug noto PIANO-REVIEW.md §3.7: EvaluateDirect di Metal aggiunge un lobo lambertiano che Evaluate/Sample non hanno (NEE e BSDF stimano due materiali diversi). Fase 5.")]
    [InlineData("metal-white-fuzz0.3")]
    [InlineData("mix-lambert-metal")]
    public void KnownBug_Metal_DirectLightingMatchesSampling(string name)
        => Assert.True(BsdfTestKit.DirectVsEvaluateError(Get(name)) < 1e-3);

    [Fact(Skip = "Bug noto PIANO-REVIEW.md §3.2: il lobo speculare delta viene diviso per max(p, 0.1) mentre è scelto con p ≈ 0.09, riflesso sottostimato del 9%. Fase 5.")]
    public void KnownBug_Disney_SmoothSpecularMatchesFresnel()
    {
        // Smooth plastic, specular 0.5 → F0 = 0.04: the delta part of the
        // albedo is the Fresnel reflectance of the mirror lobe.
        foreach (float cosV in Views)
        {
            var r = Run("disney-plastic-smooth", cosV);
            float deltaPart = r.Albedo.X - r.AlbedoNonDeltaSampled.X;
            float schlick = 0.04f + 0.96f * MathF.Pow(1f - cosV, 5f);
            Assert.InRange(deltaPart, schlick * 0.98f, schlick * 1.02f);
        }
    }

    [Theory(Skip = "Bug noto PIANO-REVIEW.md §3.17: sheen, clearcoat e vetro Disney superano albedo 1 (energia creata). Fase 5.")]
    [InlineData("disney-sheen")]
    [InlineData("disney-coat-smooth")]
    [InlineData("disney-glass-smooth")]
    [InlineData("disney-glass-rough0.4")]
    [InlineData("disney-plastic-r1.0")]
    public void KnownBug_Disney_WhiteFurnaceAlbedoDoesNotExceedOne(string name)
    {
        // The gain grows toward grazing view, so this also checks cosθ = 0.2.
        foreach (float cosV in new[] { 0.8f, 0.3f, 0.2f })
        {
            var r = Run(name, cosV);
            Assert.True(r.MaxAlbedo <= 1.01f, r.ToString());
        }
    }

    [Fact(Skip = "Bug noto PIANO-REVIEW.md §3.18: la Pdf Disney include massa VNDF sotto l'orizzonte dove diff_trans ha supporto, peso sottostimato (~1%). Fase 5.")]
    public void KnownBug_Disney_DiffTransSampleWeightIntegratesTheBsdf()
        => SampleWeight_IntegratesTheBsdf("disney-difftrans");
}
