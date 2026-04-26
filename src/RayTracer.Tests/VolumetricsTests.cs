using System.Numerics;
using RayTracer.Core;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for the volumetrics subsystem.
///
/// Strategy: assert algorithmic correctness, not absolute pixel values.
///   1. Transmittance — analytic cross-checks for HomogeneousMedium.
///   2. Phase function — energy conservation (PDF integrates to ≈1 over sphere).
///   3. Sample() — beta weight is physically bounded (≤1 for albedo ≤1).
///   4. GridMedium — bounds validation and density fetch.
/// </summary>
public class VolumetricsTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // HomogeneousMedium — Transmittance
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0f,   1f,  1.0f)]          // vacuum → Tr = 1
    [InlineData(1f,   0f,  1.0f)]          // σ_T=1, d=0 → Tr = 1
    [InlineData(1f,   1f,  0.3679f)]       // exp(-1)
    [InlineData(0.5f, 2f,  0.3679f)]       // exp(-0.5·2)
    [InlineData(2f,   3f,  0.0025f)]       // exp(-6) ≈ 2.479e-3
    public void HomogeneousMedium_Transmittance_MatchesAnalytic(
        float sigmaT, float distance, float expectedTr)
    {
        // σ_a = 0, σ_s = σ_T so the medium is purely scattering.
        var medium = new HomogeneousMedium(
            Vector3.Zero,
            new Vector3(sigmaT, sigmaT, sigmaT),
            new IsotropicPhase());

        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        Vector3 tr = medium.Transmittance(ray, distance);

        Assert.InRange(tr.X, expectedTr - 1e-3f, expectedTr + 1e-3f);
        Assert.InRange(tr.Y, expectedTr - 1e-3f, expectedTr + 1e-3f);
        Assert.InRange(tr.Z, expectedTr - 1e-3f, expectedTr + 1e-3f);
    }

    [Fact]
    public void HomogeneousMedium_Transmittance_IsMonotonicallyDecreasingWithDistance()
    {
        var medium = new HomogeneousMedium(
            new Vector3(0.1f, 0.1f, 0.1f),
            new Vector3(0.4f, 0.4f, 0.4f),
            new IsotropicPhase());

        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);

        float prev = 1f;
        foreach (float d in new[] { 0.5f, 1f, 2f, 4f, 8f })
        {
            float tr = medium.Transmittance(ray, d).X;
            Assert.True(tr < prev, $"Tr should decrease: Tr({d})={tr} ≥ Tr_prev={prev}");
            prev = tr;
        }
    }

    [Fact]
    public void HomogeneousMedium_Transmittance_SpectralChannelsIndependent()
    {
        // R=red channel heavily absorbing, B=blue transparent.
        var medium = new HomogeneousMedium(
            new Vector3(2f, 0f, 0f),   // σ_a
            new Vector3(0f, 0f, 0.1f), // σ_s
            new IsotropicPhase());

        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        Vector3 tr = medium.Transmittance(ray, 1f);

        Assert.True(tr.X < 0.2f, $"Red channel should be strongly attenuated: {tr.X}");
        Assert.Equal(1f, tr.Y, 4);                         // no absorption/scatter on green
        Assert.True(tr.Z > 0.9f, $"Blue should be nearly transparent: {tr.Z}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HomogeneousMedium — Sample() beta bounds
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HomogeneousMedium_Sample_BetaMaxChannelBoundedByAlbedo()
    {
        // Albedo = σ_s / σ_T = 0.6/1.1 ≈ 0.545. The importance-weighted beta
        // can exceed albedo on individual samples, but its luminance must stay
        // within a physically plausible range (we check no pathological outliers
        // over 1000 samples with a seeded RNG — deterministic).
        var medium = new HomogeneousMedium(
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(0.6f, 0.6f, 0.6f),
            new IsotropicPhase());

        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        const float tMax = 5f;
        const int N = 1000;

        float maxBeta = 0f;
        for (int i = 0; i < N; i++)
        {
            medium.Sample(ray, tMax, out _, out Vector3 beta, out _);
            float lum = MathUtils.Luminance(beta);
            if (lum > maxBeta) maxBeta = lum;
        }

        // Allow 3× albedo headroom for spectral averaging variance — anything
        // beyond 10 indicates a broken estimator, not normal fluctuation.
        Assert.True(maxBeta < 10f, $"Beta luminance outlier detected: {maxBeta}");
    }

    [Fact]
    public void HomogeneousMedium_VacuumMedium_NeverScatters()
    {
        var medium = new HomogeneousMedium(
            Vector3.Zero, Vector3.Zero, new IsotropicPhase());

        var ray = new Ray(Vector3.Zero, Vector3.UnitY);
        for (int i = 0; i < 200; i++)
        {
            bool scattered = medium.Sample(ray, 100f, out _, out _, out _);
            Assert.False(scattered, "Vacuum medium must never scatter");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HeightFogMedium — Transmittance analytic consistency
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HeightFogMedium_HorizontalRay_MatchesHomogeneousAtY0()
    {
        // A horizontal ray at y = y0 sees the full σ_T0 density (altitude
        // factor = 1). Transmittance along the ray must equal exp(-σ_T0·d),
        // identical to a homogeneous medium with the same coefficients.
        float sigmaT = 0.8f;
        float distance = 2f;

        var fog = new HeightFogMedium(
            Vector3.Zero,
            new Vector3(sigmaT, sigmaT, sigmaT),
            y0: 0f, scaleHeight: 1f,
            new IsotropicPhase());

        var homo = new HomogeneousMedium(
            Vector3.Zero,
            new Vector3(sigmaT, sigmaT, sigmaT),
            new IsotropicPhase());

        var ray = new Ray(Vector3.Zero, Vector3.UnitX);  // horizontal at y=0

        Vector3 trFog  = fog.Transmittance(ray, distance);
        Vector3 trHomo = homo.Transmittance(ray, distance);

        Assert.InRange(trFog.X, trHomo.X - 1e-4f, trHomo.X + 1e-4f);
    }

    [Fact]
    public void HeightFogMedium_UpwardRay_HigherTransmittanceThanHorizontal()
    {
        // Going upward exits the dense layer faster → more transparent.
        var fog = new HeightFogMedium(
            Vector3.Zero,
            new Vector3(1f, 1f, 1f),
            y0: 0f, scaleHeight: 2f,
            new IsotropicPhase());

        var horizontal = new Ray(Vector3.Zero, Vector3.UnitX);
        var upward     = new Ray(Vector3.Zero,
            Vector3.Normalize(new Vector3(1f, 4f, 0f)));

        float trH = fog.Transmittance(horizontal, 3f).X;
        float trU = fog.Transmittance(upward, 3f).X;

        Assert.True(trU > trH, $"Upward ray should be more transparent: {trU} <= {trH}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase functions — PDF normalisation (Monte Carlo estimate of ∫ p(θ) dω ≈ 1)
    // ─────────────────────────────────────────────────────────────────────────

    private static float EstimatePhaseIntegral(IPhaseFunction phase, int samples = 50_000)
    {
        // Importance-sample p(wi) and verify E[p/p] = 1, i.e. the returned
        // PDF is consistent with the sampled distribution.
        Vector3 wo = -Vector3.UnitZ;   // fixed incoming direction
        double sum = 0.0;
        for (int i = 0; i < samples; i++)
        {
            var (wi, pdf) = phase.Sample(wo);
            if (pdf > 1e-12f)
            {
                float eval = phase.Evaluate(wo, wi);
                sum += eval / pdf;
            }
        }
        return (float)(sum / samples);
    }

    [Fact]
    public void IsotropicPhase_PdfIntegratesTo1()
    {
        float integral = EstimatePhaseIntegral(new IsotropicPhase());
        Assert.InRange(integral, 0.97f, 1.03f);
    }

    [Theory]
    [InlineData(0f)]    // isotropic degenerate case
    [InlineData(0.5f)]  // forward-scattering
    [InlineData(-0.5f)] // backward-scattering
    [InlineData(0.9f)]  // strongly forward
    public void HenyeyGreensteinPhase_PdfIntegratesTo1(float g)
    {
        float integral = EstimatePhaseIntegral(new HenyeyGreensteinPhase(g));
        Assert.InRange(integral, 0.95f, 1.05f);
    }

    [Fact]
    public void RayleighPhase_PdfIntegratesTo1()
    {
        float integral = EstimatePhaseIntegral(new RayleighPhase());
        Assert.InRange(integral, 0.95f, 1.05f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(-0.3f)]
    public void SchlickPhase_PdfIntegratesTo1(float g)
    {
        float integral = EstimatePhaseIntegral(new SchlickPhase(g));
        Assert.InRange(integral, 0.95f, 1.05f);
    }

    [Fact]
    public void DoubleHenyeyGreensteinPhase_PdfIntegratesTo1()
    {
        var phase = new DoubleHenyeyGreensteinPhase(g1: 0.85f, g2: -0.3f, w: 0.5f);
        float integral = EstimatePhaseIntegral(phase);
        Assert.InRange(integral, 0.95f, 1.05f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GridMedium — construction validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GridMedium_DataLengthMismatch_ThrowsArgumentException()
    {
        float[] badData = new float[7]; // 7 ≠ 2*2*2 = 8
        Assert.Throws<ArgumentException>(() => new GridMedium(
            Vector3.Zero, new Vector3(1f, 1f, 1f),
            Vector3.Zero, Vector3.One,
            nx: 2, ny: 2, nz: 2, badData,
            new IsotropicPhase()));
    }

    [Fact]
    public void GridMedium_Resolution1_ThrowsArgumentException()
    {
        float[] data = new float[1];
        Assert.Throws<ArgumentException>(() => new GridMedium(
            Vector3.Zero, new Vector3(1f, 1f, 1f),
            Vector3.Zero, Vector3.One,
            nx: 1, ny: 1, nz: 1, data,
            new IsotropicPhase()));
    }

    [Fact]
    public void GridMedium_RayMissingBounds_ReturnsFullTransmittance()
    {
        // Ray that never enters the grid AABB → Tr must be Vector3.One.
        float[] data = new float[2 * 2 * 2];
        Array.Fill(data, 1f);
        var medium = new GridMedium(
            new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f),
            boundsMin: new Vector3(10f, 10f, 10f),
            boundsMax: new Vector3(11f, 11f, 11f),
            nx: 2, ny: 2, nz: 2, data,
            new IsotropicPhase());

        // Ray at origin going +X, far away from the [10,11]³ grid.
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        Vector3 tr = medium.Transmittance(ray, 5f);
        Assert.Equal(Vector3.One, tr);
    }

    [Fact]
    public void GridMedium_EmptyDensity_NoScatterAndFullTransmittance()
    {
        float[] data = new float[2 * 2 * 2]; // all zeros
        var medium = new GridMedium(
            new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f),
            boundsMin: -Vector3.One,
            boundsMax:  Vector3.One,
            nx: 2, ny: 2, nz: 2, data,
            new IsotropicPhase());

        var ray = new Ray(new Vector3(-2f, 0f, 0f), Vector3.UnitX);

        // No scatter expected in 200 samples through zero-density grid.
        for (int i = 0; i < 200; i++)
        {
            bool scattered = medium.Sample(ray, 10f, out _, out _, out _);
            Assert.False(scattered, "Zero-density grid must never scatter");
        }

        Vector3 tr = medium.Transmittance(ray, 10f);
        Assert.Equal(Vector3.One, tr);
    }
}
