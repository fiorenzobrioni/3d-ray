using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The lobes the OpenPBR material is assembled from (PIANO-REVIEW.md, Phase 5),
/// each against its defining integral: the EON diffuse keeps a white surface
/// white at every roughness, the fuzz lobe is a normalised density its sampler
/// draws, and the tabulated dielectric albedos match a direct quadrature of
/// the microfacet BRDF they stand for. Tangent space, z = normal.
/// </summary>
public class OpenPbrLobeTests
{
    /// <summary>∫ over the upper hemisphere of f(l), midpoint rule in (θ, φ).</summary>
    private static double Hemisphere(Func<Vector3, double> f, int nTheta = 400, int nPhi = 400)
    {
        double sum = 0, dTheta = Math.PI / 2 / nTheta, dPhi = 2 * Math.PI / nPhi;
        for (int i = 0; i < nTheta; i++)
        {
            double theta = (i + 0.5) * dTheta, st = Math.Sin(theta), ct = Math.Cos(theta);
            for (int j = 0; j < nPhi; j++)
            {
                double phi = (j + 0.5) * dPhi;
                var l = new Vector3((float)(st * Math.Cos(phi)), (float)(st * Math.Sin(phi)), (float)ct);
                sum += f(l) * st;
            }
        }
        return sum * dTheta * dPhi;
    }

    private static Vector3 View(float mu) => new(MathF.Sqrt(1f - mu * mu), 0f, mu);

    // ── EON diffuse ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.3f)]
    [InlineData(0.7f)]
    [InlineData(1.0f)]
    public void Eon_WhiteSurfaceReflectsEverything(float sigma)
    {
        foreach (float mu in new[] { 0.05f, 0.3f, 0.6f, 1f })
        {
            Vector3 v = View(mu);
            double e = Hemisphere(l => Eon.Evaluate(Vector3.One, sigma, l.Z, mu, Vector3.Dot(l, v)).X * l.Z);
            Assert.InRange(e, 0.997, 1.003);
        }
    }

    [Theory]
    [InlineData(0.4f)]
    [InlineData(1.0f)]
    public void Eon_AlbedoMatchesItsIntegral(float sigma)
    {
        // Coloured: the multiple-scattering colour ρ_ms saturates the albedo,
        // and the closed form lerp(ρ_ms, ρ, Ê) must be the lobe's integral.
        var rho = new Vector3(0.2f, 0.5f, 0.9f);
        foreach (float mu in new[] { 0.1f, 0.5f, 0.95f })
        {
            Vector3 v = View(mu);
            Vector3 expected = Eon.Albedo(rho, sigma, mu);
            for (int c = 0; c < 3; c++)
            {
                double e = Hemisphere(l =>
                {
                    Vector3 f = Eon.Evaluate(rho, sigma, l.Z, mu, Vector3.Dot(l, v));
                    return (c == 0 ? f.X : c == 1 ? f.Y : f.Z) * l.Z;
                });
                float ex = c == 0 ? expected.X : c == 1 ? expected.Y : expected.Z;
                Assert.InRange(e, ex - 3e-3, ex + 3e-3);
            }
        }
    }

    [Fact]
    public void Eon_FujiiAlbedoClosedFormMatchesTheSingleScatteringLobe()
    {
        const float sigma = 0.8f;
        const float a = 1f / (1f + (0.5f - 2f / (3f * MathF.PI)) * sigma);
        foreach (float mu in new[] { 0.1f, 0.4f, 0.8f })
        {
            Vector3 v = View(mu);
            double e = Hemisphere(l =>
            {
                float s = Vector3.Dot(l, v) - l.Z * mu;
                float st = s > 0f ? s / MathF.Max(l.Z, mu) : s;
                return a * (1 + sigma * st) / Math.PI * l.Z;
            });
            Assert.InRange(e, Eon.FujiiAlbedo(mu, sigma) - 2e-3, Eon.FujiiAlbedo(mu, sigma) + 2e-3);
        }
        // ⟨Ê⟩ = 2 ∫ Ê(μ) μ dμ.
        double avg = 0;
        const int n = 2000;
        for (int i = 0; i < n; i++) { double mu = (i + 0.5) / n; avg += 2 * Eon.FujiiAlbedo((float)mu, sigma) * mu / n; }
        Assert.InRange(avg, Eon.FujiiAverage(sigma) - 1e-3, Eon.FujiiAverage(sigma) + 1e-3);
    }

    [Fact]
    public void Eon_RoughnessZeroIsLambert()
    {
        var rho = new Vector3(0.3f, 0.6f, 0.9f);
        Vector3 f = Eon.Evaluate(rho, 0f, 0.4f, 0.7f, 0.1f);
        Assert.Equal(rho.X / MathF.PI, f.X, 6);
        Assert.Equal(rho.Z / MathF.PI, f.Z, 6);
    }

    // ── Fuzz (Zeltner 2022 LTC) ──────────────────────────────────────────────

    [Theory]
    [InlineData(0.9f, 0.5f)]
    [InlineData(0.4f, 0.25f)]
    [InlineData(0.15f, 0.8f)]
    [InlineData(0.7f, 1.0f)]
    public void Fuzz_SamplerDrawsTheLobeDensity(float mu, float r)
    {
        Vector3 n = Vector3.UnitZ, v = View(mu);
        MathUtils.ReseedThread(5);
        var (p, integral, chi2, dof) = BsdfTestKit.DirectionalChiSquare(
            () =>
            {
                var (u1, u2) = MathUtils.Random2D();
                return FuzzLtc.Sample(n, v, r, u1, u2);
            },
            l => FuzzLtc.Density(n, v, l, r));
        Assert.True(p > 1e-4, $"chi2 {chi2:0.0} / {dof}, p {p:E2}");
        Assert.InRange(integral, 0.99, 1.01);
    }

    [Fact]
    public void Fuzz_AlbedoIsAFraction()
    {
        for (int i = 0; i <= 20; i++)
        for (int j = 0; j <= 20; j++)
        {
            float e = FuzzLtc.Albedo(i / 20f, j / 20f);
            Assert.InRange(e, 0f, 1f);
        }
        // Fibres along the normal: a smooth fuzz reflects little face-on and
        // most at grazing (the sheen), a rough one is dust, brighter face-on.
        Assert.True(FuzzLtc.Albedo(0.1f, 0.3f) > FuzzLtc.Albedo(1f, 0.3f));
    }

    // ── Dielectric reflection albedo ─────────────────────────────────────────

    private static double ReflectionAlbedo(float mu, float alpha, float eta)
    {
        Vector3 v = View(mu);
        return Hemisphere(l =>
        {
            Vector3 h = Vector3.Normalize(v + l);
            float d = Microfacet.DGgxAniso(h, alpha, alpha);
            float g = Microfacet.G2GgxAniso(v, l, alpha, alpha);
            float f = MathUtils.FresnelDielectric(Vector3.Dot(v, h), 1f / eta);
            return f * d * g / (4 * mu * l.Z) * l.Z;
        }, 1200, 400);
    }

    [Theory]
    [InlineData(0.8f, 0.30f, 1.5f)]
    [InlineData(0.3f, 0.45f, 1.33f)]
    [InlineData(0.55f, 0.70f, 2.1f)]
    [InlineData(0.15f, 1.00f, 1.6f)]
    [InlineData(0.95f, 0.62f, 1.07f)]
    public void DielectricAlbedo_MatchesTheBrdfIntegral(float mu, float roughness, float eta)
    {
        float alpha = roughness * roughness;
        double reference = ReflectionAlbedo(mu, alpha, eta);
        float table = DielectricAlbedo.E(mu, alpha, eta);
        Assert.InRange(table, reference - 2.5e-3, reference + 2.5e-3);
    }

    [Fact]
    public void DielectricAlbedo_PerfectReflectorIsTheWhiteTable()
    {
        foreach (float mu in new[] { 0.2f, 0.6f, 1f })
        foreach (float a in new[] { 0.04f, 0.3f, 1f })
            Assert.InRange(DielectricAlbedo.E(mu, a, 1e9f), MicrofacetAlbedo.E(mu, a) - 2e-3, MicrofacetAlbedo.E(mu, a) + 2e-3);
        Assert.Equal(0f, DielectricAlbedo.E(0.5f, 0.2f, 1f), 6);
    }

    [Theory]
    [InlineData(1.33f)]
    [InlineData(1.5f)]
    [InlineData(2.4f)]
    public void DielectricAlbedo_FresnelAverage(float eta)
    {
        double avg = 0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            double mu = (i + 0.5) / n;
            avg += 2 * MathUtils.FresnelDielectric((float)mu, 1f / eta) * mu / n;
        }
        Assert.InRange(DielectricAlbedo.FresnelAvg(eta), avg - 1e-3, avg + 1e-3);
        // From the dense side, total internal reflection fills the rest.
        double inside = 0;
        for (int i = 0; i < n; i++)
        {
            double mu = (i + 0.5) / n;
            inside += 2 * MathUtils.FresnelDielectric((float)mu, eta) * mu / n;
        }
        Assert.InRange(DielectricAlbedo.FresnelAvg(1f / eta), inside - 2e-3, inside + 2e-3);
    }
}
