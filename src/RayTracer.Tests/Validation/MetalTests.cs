using System.Numerics;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The conductor (PIANO-REVIEW.md, Phase 5): the complex Fresnel against
/// Mitsuba's, the F82 model's defining properties, energy conservation of the
/// multiple-scattering lobe (a white metal reflects everything at every
/// roughness) and the single-scattering albedo table against a direct
/// quadrature of the BSDF.
/// </summary>
public class MetalTests
{
    // Reference: mitsuba.fresnel_conductor (Mitsuba 3.9.1, scalar_rgb) with
    // the preset's RGB n + ik.
    public static TheoryData<string, float, float, float, float> MitsubaFresnel => new()
    {
        { "gold", 1.0f, 0.966679f, 0.802011f, 0.324121f },
        { "gold", 0.7f, 0.965219f, 0.800898f, 0.336637f },
        { "gold", 0.3f, 0.959025f, 0.829748f, 0.462677f },
        { "gold", 0.1f, 0.973435f, 0.919499f, 0.718890f },
        { "aluminium", 1.0f, 0.928045f, 0.917801f, 0.918906f },
        { "aluminium", 0.7f, 0.924155f, 0.913733f, 0.915254f },
        { "aluminium", 0.3f, 0.887272f, 0.881998f, 0.893294f },
        { "aluminium", 0.1f, 0.844954f, 0.882289f, 0.916212f },
    };

    [Theory]
    [MemberData(nameof(MitsubaFresnel))]
    public void ComplexFresnel_MatchesMitsuba(string preset, float cos, float r, float g, float b)
    {
        var ior = ConductorIor.Presets[preset];
        var f = ConductorFresnel.Complex(cos, ior.Eta, ior.K);
        Assert.Equal(r, f.X, 4);
        Assert.Equal(g, f.Y, 4);
        Assert.Equal(b, f.Z, 4);
    }

    [Fact]
    public void F82_IsSchlickForAWhiteTint_AndHitsTheTintAt82Degrees()
    {
        var f0 = new Vector3(0.9f, 0.6f, 0.3f);
        var tint = new Vector3(0.8f, 0.9f, 1.0f);
        for (float mu = 0f; mu <= 1f; mu += 0.05f)
        {
            float m5 = MathF.Pow(1 - mu, 5);
            var schlick = f0 + (Vector3.One - f0) * m5;
            Assert.True(Vector3.Distance(schlick, ConductorFresnel.F82(mu, f0, Vector3.One)) < 1e-6f);
        }
        float mb = 1f / 7f;
        var sBar = f0 + (Vector3.One - f0) * MathF.Pow(1 - mb, 5);
        Assert.True(Vector3.Distance(tint * sBar, ConductorFresnel.F82(mb, f0, tint)) < 1e-5f);
        Assert.True(Vector3.Distance(f0, ConductorFresnel.F82(1f, f0, tint)) < 1e-6f);
        Assert.True(Vector3.Distance(Vector3.One, ConductorFresnel.F82(0f, f0, tint)) < 1e-6f);
    }

    [Fact]
    public void FresnelAverages_MatchQuadrature()
    {
        var f0 = new Vector3(0.95f, 0.64f, 0.54f);
        var tint = new Vector3(0.7f, 0.85f, 1f);
        Vector3 sum = Vector3.Zero;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            float mu = (i + 0.5f) / n;
            sum += ConductorFresnel.F82(mu, f0, tint) * mu;
        }
        Assert.True(Vector3.Distance(2f * sum / n, ConductorFresnel.F82Average(f0, tint)) < 1e-4f);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.6f)]
    [InlineData(1.0f)]
    public void WhiteMetal_ReflectsAllTheLight_AtEveryRoughness(float fuzz)
    {
        // F = 1: single scattering plus the Kulla-Conty lobe must integrate
        // to 1. Without the lobe a fuzz-1 white metal returns 0.31 head-on.
        var metal = new Metal(Vector3.One, fuzz);
        foreach (float cosV in new[] { 0.9f, 0.5f, 0.2f })
        {
            var r = BsdfTestKit.Run($"white-{fuzz}", metal, cosV);
            Assert.InRange(r.Albedo.X, 0.985f, 1.015f);
            Assert.True(r.PValue > 1e-4, r.ToString());
        }
    }

    [Theory]
    [InlineData(0.3f, 0.9f)]
    [InlineData(0.7f, 0.5f)]
    [InlineData(1.0f, 0.2f)]
    public void SingleScatteringAlbedo_MatchesTheTable(float fuzz, float cosV)
    {
        var metal = new Metal(new RayTracer.Textures.SolidColor(Vector3.One), fuzz, multipleScattering: false);
        var r = BsdfTestKit.Run($"white-ss-{fuzz}", metal, cosV);
        float e = MicrofacetAlbedo.E(cosV, fuzz * fuzz);
        Assert.InRange(r.AlbedoNonDeltaQuadrature.X, e - 0.01f, e + 0.01f);
    }
}
