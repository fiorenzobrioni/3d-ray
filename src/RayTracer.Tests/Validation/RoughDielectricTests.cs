using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The rough dielectric (Walter et al. 2007; PIANO-REVIEW.md, Phase 5 §3.6):
/// reflection and transmission sampled from their own pdf, from outside and
/// from inside (where total internal reflection takes over), consistent with
/// Evaluate, converging to the smooth Fresnel split as the roughness goes to
/// zero, and obeying the generalised reciprocity of refraction.
/// </summary>
public class RoughDielectricTests
{
    private static Dielectric Glass(float roughness) => new(1.5f, new SolidColor(Vector3.One), roughness);

    [Theory]
    [InlineData(0.3f, 0.9f, true)]
    [InlineData(0.3f, 0.3f, true)]
    [InlineData(0.6f, 0.6f, true)]
    [InlineData(0.3f, 0.9f, false)]   // from inside
    [InlineData(0.5f, 0.5f, false)]   // from inside, past the critical angle for part of the lobe
    public void SamplesItsPdf_AndItsWeightIntegratesTheBsdf(float roughness, float cosV, bool frontFace)
    {
        var r = BsdfTestKit.Run($"glass-r{roughness}", Glass(roughness), cosV, frontFace: frontFace, sub: 24);
        Assert.True(r.PValue > 1e-4, r.ToString());
        Assert.True(r.PdfIntegralError < 0.01, r.ToString());
        Assert.True(r.AlbedoCrossCheckError < 0.02, r.ToString());
        Assert.Equal(0, r.InconsistentSamples);
    }

    [Theory]
    [InlineData(0.9f)]
    [InlineData(0.5f)]
    public void NearlySmooth_SplitsLikeFresnel(float cosV)
    {
        // α → 0: reflected F, transmitted (1 − F) (η_i/η_t)² in radiance.
        var r = BsdfTestKit.Run("glass-r0.03", Glass(0.03f), cosV);
        float ri = 1f / 1.5f;
        float f = MathUtils.FresnelDielectric(cosV, ri);
        float expected = f + (1f - f) * ri * ri;
        Assert.InRange(r.Albedo.X, expected - 0.01f, expected + 0.01f);
    }

    [Fact]
    public void Transmission_ObeysGeneralisedReciprocity()
    {
        // f(V→L) / n_V² = f(L→V) / n_L²: from outside (air, n 1) into glass
        // (n 1.5) against the same pair seen from inside.
        var glass = Glass(0.4f);
        var outside = BsdfTestKit.MakeRec(frontFace: true);
        var inside = BsdfTestKit.MakeRec(frontFace: false);
        inside.Normal = -outside.Normal;
        var rng = new Random(5);
        double worst = 0;
        int compared = 0;
        for (int i = 0; i < 4000; i++)
        {
            double t1 = Math.Acos(rng.NextDouble()), p1 = 2 * Math.PI * rng.NextDouble();
            double t2 = Math.Acos(rng.NextDouble()), p2 = 2 * Math.PI * rng.NextDouble();
            var v = new Vector3((float)(Math.Sin(t1) * Math.Cos(p1)), (float)Math.Cos(t1), (float)(Math.Sin(t1) * Math.Sin(p1)));
            var l = new Vector3((float)(Math.Sin(t2) * Math.Cos(p2)), -(float)Math.Cos(t2), (float)(Math.Sin(t2) * Math.Sin(p2)));
            float fwd = glass.Evaluate(v, l, outside).X / 1f;           // V in air
            float back = glass.Evaluate(l, v, inside).X / (1.5f * 1.5f); // V in glass
            if (fwd < 1e-3f && back < 1e-3f) continue;
            compared++;
            worst = Math.Max(worst, Math.Abs(fwd - back) / Math.Max(fwd, back));
        }
        Assert.True(compared > 500, $"only {compared} pairs with transmission");
        Assert.True(worst < 1e-3, $"generalised reciprocity off by {worst:E2}");
    }

    [Fact]
    public void DirectLighting_UsesTheSameBsdfAsSampling()
        => Assert.True(BsdfTestKit.DirectVsEvaluateError(Glass(0.4f)) < 1e-3);
}
