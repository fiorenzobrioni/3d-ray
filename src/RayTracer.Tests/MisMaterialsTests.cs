using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Validates the symmetric BSDF API (Evaluate / Pdf / Sample) on the
/// "legacy" materials migrated to MIS — Lambertian, Metal, MixMaterial —
/// and the phase-function Pdf surface area introduced for volumetric MIS.
///
/// Layout mirrors <see cref="DisneyBsdfTests"/>: PDF ≈ 1 over the unit
/// sphere, Sample/F/Pdf consistency, mixture linearity, and a couple of
/// degenerate-input sanity checks. Phase-function Pdf is exercised against
/// Evaluate (must agree by construction in this codebase).
/// </summary>
public class MisMaterialsTests
{
    private static readonly Vector3 NormalUp = Vector3.UnitY;

    private static HitRecord MakeRec() => new()
    {
        Normal = NormalUp,
        Point = Vector3.Zero,
        LocalPoint = Vector3.Zero,
        U = 0f,
        V = 0f,
        ObjectSeed = 0,
        FrontFace = true,
    };

    private static Vector3 UniformSphere(Random rng)
    {
        float z = (float)(2.0 * rng.NextDouble() - 1.0);
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        float phi = (float)(2.0 * Math.PI * rng.NextDouble());
        return new Vector3(r * MathF.Cos(phi), z, r * MathF.Sin(phi));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Lambertian — PDF integrates to 1, Sample matches Evaluate/Pdf.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Lambertian_Pdf_IntegratesToOne()
    {
        var material = new Lambertian(new Vector3(0.7f, 0.5f, 0.3f));
        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f));

        var rng = new Random(0xA17B);
        const int N = 32768;
        double sum = 0;
        for (int i = 0; i < N; i++)
            sum += material.Pdf(V, UniformSphere(rng), rec);

        double integral = 4.0 * Math.PI * sum / N;
        Assert.InRange(integral, 0.97, 1.03);
    }

    [Fact]
    public void Lambertian_Sample_ConsistentWithEvaluateAndPdf()
    {
        var material = new Lambertian(new Vector3(0.7f, 0.5f, 0.3f));
        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f));

        for (int i = 0; i < 256; i++)
        {
            var sample = material.Sample(V, rec);
            if (sample is null) continue;

            var s = sample.Value;
            Assert.False(s.IsDelta);
            var fEval = material.Evaluate(V, s.Wo, rec);
            float pEval = material.Pdf(V, s.Wo, rec);

            AssertVectorClose(s.F, fEval, 1e-4f, 1e-6f);
            AssertClose(s.Pdf, pEval, 1e-4f, 1e-6f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Metal — Pdf consistency with Sample, plus delta-mirror invariants.
    //
    // Note: Metal's sampler is the classic GGX NDF (D·NdotH) with
    // half-vector → reflect(-V, H), so high-roughness samples can land
    // below the surface and get rejected (Sample returns null). The
    // analytical Metal.Pdf intentionally reports the unrejected NDF density
    // — this matches BsdfSample.Pdf bit-for-bit, which is what MIS needs to
    // stay unbiased; the ratio f·cos/Pdf is what cancels in the estimator.
    // We therefore do NOT assert ∫ Pdf dω == 1 globally — only Sample/Pdf
    // self-consistency.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Metal_PerfectMirror_SampleIsDelta()
    {
        var material = new Metal(new Vector3(0.9f, 0.9f, 0.9f), 0f);
        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f));

        var sample = material.Sample(V, rec);
        Assert.NotNull(sample);
        Assert.True(sample!.Value.IsDelta);
        Assert.Equal(1f, sample.Value.Pdf);
        // Pdf() of a delta lobe must report zero (no analytic density).
        Assert.Equal(0f, material.Pdf(V, sample.Value.Wo, rec));
        Assert.Equal(Vector3.Zero, material.Evaluate(V, sample.Value.Wo, rec));
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.7f)]
    public void Metal_Sample_ConsistentWithEvaluateAndPdf(float fuzz)
    {
        var material = new Metal(new Vector3(0.7f, 0.5f, 0.3f), fuzz);
        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));

        int nonDelta = 0;
        for (int i = 0; i < 256; i++)
        {
            var sample = material.Sample(V, rec);
            if (sample is null || sample.Value.IsDelta) continue;
            nonDelta++;

            var wo = sample.Value.Wo;
            AssertVectorClose(sample.Value.F, material.Evaluate(V, wo, rec), 1e-3f, 1e-6f);
            AssertClose(sample.Value.Pdf, material.Pdf(V, wo, rec), 1e-3f, 1e-6f);
        }
        Assert.True(nonDelta > 100, $"expected >100 non-delta samples, got {nonDelta}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. MixMaterial — Pdf is exactly the linear blend of the children's Pdfs.
    //   This is the property MIS NEE relies on, so we check it analytically
    //   (no MC) for a fixed (V, L) under a deterministic 50/50 blend.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1.0f)]
    public void MixMaterial_Pdf_IsLinearBlendOfChildren(float t)
    {
        var lamb = new Lambertian(new Vector3(0.7f, 0.5f, 0.3f));
        var metal = new Metal(new Vector3(0.85f, 0.65f, 0.10f), 0.4f);
        var mix = new MixMaterial(lamb, metal, t);
        var rec = MakeRec();

        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));
        var L = Vector3.Normalize(new Vector3(-0.1f, 0.9f, 0.3f));

        float pA = lamb.Pdf(V, L, rec);
        float pB = metal.Pdf(V, L, rec);
        float expected = (1f - t) * pA + t * pB;

        AssertClose(mix.Pdf(V, L, rec), expected, 1e-5f, 1e-7f);
    }

    [Fact]
    public void MixMaterial_Evaluate_IsLinearBlend()
    {
        var lamb = new Lambertian(new Vector3(0.7f, 0.5f, 0.3f));
        var metal = new Metal(new Vector3(0.85f, 0.65f, 0.10f), 0.4f);
        var mix = new MixMaterial(lamb, metal, 0.3f);
        var rec = MakeRec();

        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));
        var L = Vector3.Normalize(new Vector3(-0.1f, 0.9f, 0.3f));

        Vector3 expected = Vector3.Lerp(lamb.Evaluate(V, L, rec), metal.Evaluate(V, L, rec), 0.3f);
        AssertVectorClose(mix.Evaluate(V, L, rec), expected, 1e-5f, 1e-7f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Phase function Pdf == Evaluate (codebase invariant: every phase's
    //    Sample returns Evaluate(wo, wi) as Pdf, so the dedicated Pdf entry
    //    must agree with Evaluate to keep MIS unbiased).
    // ─────────────────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> PhaseFunctions()
    {
        yield return new object[] { (IPhaseFunction)new IsotropicPhase() };
        yield return new object[] { (IPhaseFunction)new HenyeyGreensteinPhase(0.7f) };
        yield return new object[] { (IPhaseFunction)new HenyeyGreensteinPhase(-0.4f) };
        yield return new object[] { (IPhaseFunction)new DoubleHenyeyGreensteinPhase(0.85f, -0.3f, 0.5f) };
        yield return new object[] { (IPhaseFunction)new RayleighPhase() };
        yield return new object[] { (IPhaseFunction)new SchlickPhase(0.5f) };
    }

    [Theory]
    [MemberData(nameof(PhaseFunctions))]
    public void PhaseFunction_PdfMatchesEvaluate(IPhaseFunction phase)
    {
        var rng = new Random(HashCode.Combine("phase", phase.GetType().Name));
        var wo = Vector3.Normalize(new Vector3(0.1f, 1f, 0.2f));
        for (int i = 0; i < 64; i++)
        {
            var wi = UniformSphere(rng);
            wi = wi.LengthSquared() > 1e-6f ? Vector3.Normalize(wi) : Vector3.UnitX;
            float p = phase.Pdf(wo, wi);
            float e = phase.Evaluate(wo, wi);
            AssertClose(p, e, 1e-5f, 1e-7f);
        }
    }

    [Theory]
    [MemberData(nameof(PhaseFunctions))]
    public void PhaseFunction_PdfIntegratesToOne(IPhaseFunction phase)
    {
        var rng = new Random(HashCode.Combine("phase-int", phase.GetType().Name));
        var wo = Vector3.Normalize(new Vector3(0f, 0f, 1f));

        const int N = 65536;
        double sum = 0;
        for (int i = 0; i < N; i++)
            sum += phase.Pdf(wo, UniformSphere(rng));

        double integral = 4.0 * Math.PI * sum / N;
        // Each phase function is normalised over the unit sphere; ±5%
        // covers Monte-Carlo noise on the most concentrated lobe (HG g=0.7).
        Assert.InRange(integral, 0.95, 1.05);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers (copy of the small assertion utilities from DisneyBsdfTests —
    // intentionally local rather than shared so the test binary stays a flat
    // set of independent fixtures).
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssertVectorClose(Vector3 a, Vector3 b, float relTol, float absTol)
    {
        AssertClose(a.X, b.X, relTol, absTol);
        AssertClose(a.Y, b.Y, relTol, absTol);
        AssertClose(a.Z, b.Z, relTol, absTol);
    }

    private static void AssertClose(float a, float b, float relTol, float absTol)
    {
        float diff = MathF.Abs(a - b);
        float scale = MathF.Max(MathF.Abs(a), MathF.Abs(b));
        Assert.True(diff <= absTol + relTol * scale,
            $"expected {a} ≈ {b} (|Δ| = {diff}, rel = {(scale > 0 ? diff / scale : 0):0.###e+00})");
    }
}
