using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The GGX microfacet core every glossy lobe is built on (PIANO-REVIEW.md,
/// Phase 5): the distribution is normalized, it satisfies the projected-area
/// identity with its Smith masking term, and the visible-normal sampler draws
/// from the visible-normal density it promises. Integrals by quadrature over
/// the hemisphere, in tangent space (z = normal).
/// </summary>
public class MicrofacetTests
{
    public static TheoryData<float, float> Alphas => new()
    {
        { 0.1f, 0.1f }, { 0.3f, 0.3f }, { 0.7f, 0.7f }, { 1.0f, 1.0f }, { 0.15f, 0.6f }, { 0.8f, 0.25f },
    };

    /// <summary>∫ over the upper hemisphere of f(h), on a grid in (θ, φ) fine
    /// enough for the narrowest lobe tested (α = 0.1).</summary>
    private static double Hemisphere(Func<Vector3, double> f, int nTheta = 3000, int nPhi = 600)
    {
        double sum = 0, dTheta = Math.PI / 2 / nTheta, dPhi = 2 * Math.PI / nPhi;
        for (int i = 0; i < nTheta; i++)
        {
            double theta = (i + 0.5) * dTheta, st = Math.Sin(theta), ct = Math.Cos(theta);
            for (int j = 0; j < nPhi; j++)
            {
                double phi = (j + 0.5) * dPhi;
                var h = new Vector3((float)(st * Math.Cos(phi)), (float)(st * Math.Sin(phi)), (float)ct);
                sum += f(h) * st;
            }
        }
        return sum * dTheta * dPhi;
    }

    [Theory]
    [MemberData(nameof(Alphas))]
    public void Ndf_IsNormalized(float ax, float ay)
    {
        // ∫ D(h) (h·n) dω_h = 1: the microfacets' projected area is the
        // macro-surface's.
        double integral = Hemisphere(h => Microfacet.DGgxAniso(h, ax, ay) * h.Z);
        Assert.InRange(integral, 0.999, 1.001);
    }

    [Theory]
    [MemberData(nameof(Alphas))]
    public void SmithMasking_SatisfiesTheProjectedAreaIdentity(float ax, float ay)
    {
        // ∫ G1(v) max(0, v·h) D(h) dω_h = v·n for every v (Heitz 2014, eq. 2):
        // the visible microfacet area seen from v equals the visible
        // macro-surface area. The correctness condition of Λ for its D.
        foreach (var (theta, phi) in new[] { (0.2, 0.3), (0.9, 1.7), (1.3, 4.0), (1.5, 2.2) })
        {
            var v = new Vector3((float)(Math.Sin(theta) * Math.Cos(phi)), (float)(Math.Sin(theta) * Math.Sin(phi)), (float)Math.Cos(theta));
            double g1 = Microfacet.G1GgxAniso(v, ax, ay);
            double integral = Hemisphere(h => g1 * Math.Max(0f, Vector3.Dot(v, h)) * Microfacet.DGgxAniso(h, ax, ay));
            Assert.InRange(integral / v.Z, 0.998, 1.002);
        }
    }

    [Theory]
    [InlineData(0.3f, 0.3f, 0.4)]
    [InlineData(0.6f, 0.6f, 1.2)]
    [InlineData(0.2f, 0.7f, 0.9)]
    [InlineData(0.9f, 0.9f, 1.45)]
    public void VisibleNormalSampling_DrawsTheVisibleNormalDensity(float ax, float ay, double theta)
    {
        // D_v(h) = G1(v) max(0, v·h) D(h) / (v·n), Heitz 2018.
        var v = new Vector3((float)Math.Sin(theta) * 0.8f, (float)Math.Sin(theta) * 0.6f, (float)Math.Cos(theta));
        double g1 = Microfacet.G1GgxAniso(v, ax, ay);
        MathUtils.ReseedThread(11);
        var (p, integral, chi2, dof) = BsdfTestKit.DirectionalChiSquare(
            () =>
            {
                var (u1, u2) = MathUtils.Random2D();
                return Microfacet.SampleGgxVndfAniso(v, ax, ay, u1, u2);
            },
            h => h.Z <= 0f ? 0.0 : g1 * Math.Max(0f, Vector3.Dot(v, h)) * Microfacet.DGgxAniso(h, ax, ay) / v.Z);
        Assert.True(p > 1e-4, $"chi2 {chi2:0.0} / {dof}, p {p:E2}");
        Assert.InRange(integral, 0.99, 1.01);
    }

    [Theory]
    [MemberData(nameof(Alphas))]
    public void HeightCorrelatedG2_LiesBetweenTheUncorrelatedAndTheMinimum(float ax, float ay)
    {
        // Height correlation can only make masking and shadowing less
        // independent: G1·G1 <= G2 <= min(G1, G1), with equality at normal
        // incidence of either direction.
        var rng = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            Vector3 Rand()
            {
                double t = Math.Acos(rng.NextDouble()), ph = 2 * Math.PI * rng.NextDouble();
                return new Vector3((float)(Math.Sin(t) * Math.Cos(ph)), (float)(Math.Sin(t) * Math.Sin(ph)), (float)Math.Cos(t));
            }
            var v = Rand();
            var l = Rand();
            float gv = Microfacet.G1GgxAniso(v, ax, ay), gl = Microfacet.G1GgxAniso(l, ax, ay);
            float g2 = Microfacet.G2GgxAniso(v, l, ax, ay);
            Assert.InRange(g2, gv * gl - 1e-6f, MathF.Min(gv, gl) + 1e-6f);
        }
        Assert.Equal(Microfacet.G1GgxAniso(new Vector3(0.6f, 0f, 0.8f), ax, ay),
                     Microfacet.G2GgxAniso(Vector3.UnitZ, new Vector3(0.6f, 0f, 0.8f), ax, ay), 6);
    }
}
