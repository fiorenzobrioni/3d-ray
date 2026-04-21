using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// BSDF-level correctness tests for <see cref="DisneyBsdf"/>'s symmetric
/// interface (<c>Evaluate</c> / <c>Pdf</c> / <c>Sample</c>) introduced for
/// MIS and energy-conservation work.
///
/// The tests fall in three groups:
///   1. Reciprocity — f(V, L) == f(L, V) for every reflection lobe.
///   2. PDF integration — ∫ pdf(L) dω ≈ 1 over the unit sphere for every
///      opaque material configuration (transmission is intentionally
///      excluded from the analytical PDF until the VNDF step).
///   3. Sampler consistency — the (Wo, F, Pdf) triple returned by
///      <see cref="DisneyBsdf.Sample"/> matches what <c>Evaluate</c> and
///      <c>Pdf</c> produce for the same direction.
///
/// Random seeds are injected via <see cref="Xunit.InlineDataAttribute"/> so
/// failures replay deterministically. The tests only exercise reflection
/// configurations (specTrans = 0); transmission sampling will be revisited
/// once the dedicated glass BSDF lands.
/// </summary>
public class DisneyBsdfTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    //
    // All tests use a world-space shading frame with N = +Y, so directions can
    // be written directly in Cartesian form without going through a TBN. A
    // HitRecord is built with that normal and zero-initialised UV/point so the
    // BaseColor texture lookup degenerates to a constant.
    // ─────────────────────────────────────────────────────────────────────────

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

    private static Vector3 UniformSphereDirection(Random rng)
    {
        // Cosine of the polar angle is uniformly distributed on [-1, 1]
        // for a uniform sphere sample (Archimedes' hat-box theorem).
        float z = (float)(2.0 * rng.NextDouble() - 1.0);
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        float phi = (float)(2.0 * Math.PI * rng.NextDouble());
        return new Vector3(r * MathF.Cos(phi), z, r * MathF.Sin(phi));
    }

    private static Vector3 UniformHemisphereDirection(Vector3 normal, Random rng)
    {
        var d = UniformSphereDirection(rng);
        return Vector3.Dot(d, normal) >= 0f ? d : -d;
    }

    /// <summary>
    /// Representative opaque Disney material configurations. The goal is to
    /// exercise every reflection lobe independently and in combination,
    /// including edge cases (perfect mirror, pure diffuse, clearcoat-only).
    /// </summary>
    public static IEnumerable<object[]> OpaqueMaterials()
    {
        // metallic, roughness, specular, sheen, clearcoat, clearcoatGloss
        // Each case uses baseColor = (0.7, 0.5, 0.3) unless noted.
        yield return new object[] { 0f, 1.0f,  0.5f, 0f,    0f,   1f };   // pure diffuse
        yield return new object[] { 0f, 0.25f, 0.5f, 0f,    0f,   1f };   // glossy dielectric
        yield return new object[] { 0f, 0.05f, 0.5f, 0f,    0f,   1f };   // near-mirror dielectric
        yield return new object[] { 1f, 0.15f, 0f,   0f,    0f,   1f };   // smooth metal
        yield return new object[] { 1f, 0.6f,  0f,   0f,    0f,   1f };   // rough metal
        yield return new object[] { 0f, 0.4f,  0.5f, 0.6f,  0f,   1f };   // dielectric + sheen
        yield return new object[] { 0f, 0.3f,  0.5f, 0f,    1f,   0.9f }; // clearcoated plastic
        yield return new object[] { 0f, 0.8f,  0.5f, 0.3f,  0.5f, 0.5f }; // all-lobes mix
    }

    /// <summary>
    /// Subset of <see cref="OpaqueMaterials"/> restricted to broad lobes where
    /// uniform-sphere Monte Carlo integration converges in a reasonable sample
    /// budget. Narrow lobes (low roughness, any nonzero clearcoat) put their
    /// PDF mass into a tiny solid angle that gets missed by uniform samples —
    /// verifying those via MC would require 10⁷+ samples or stratified
    /// sphere sampling, and is covered indirectly by the sampler-consistency
    /// test instead.
    /// </summary>
    public static IEnumerable<object[]> BroadLobeMaterials()
    {
        yield return new object[] { 0f, 1.0f, 0.5f, 0f,   0f, 1f }; // pure diffuse
        yield return new object[] { 0f, 0.8f, 0.5f, 0f,   0f, 1f }; // rough dielectric
        yield return new object[] { 1f, 0.6f, 0f,   0f,   0f, 1f }; // rough metal
        yield return new object[] { 0f, 0.5f, 0.5f, 0.6f, 0f, 1f }; // dielectric + sheen
    }

    private static DisneyBsdf MakeMaterial(
        float metallic, float roughness, float specular,
        float sheen, float clearcoat, float clearcoatGloss)
    {
        var baseColor = new SolidColor(new Vector3(0.7f, 0.5f, 0.3f));
        return new DisneyBsdf(
            baseColor,
            metallic:       metallic,
            roughness:      roughness,
            subsurface:     0f,
            specular:       specular,
            specularTint:   0f,
            sheen:          sheen,
            sheenTint:      0.5f,
            clearcoat:      clearcoat,
            clearcoatGloss: clearcoatGloss,
            specTrans:      0f,
            ior:            1.5f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Reciprocity
    //
    // The Disney BRDF is constructed from reflection lobes that are all
    // symmetric in (V, L) — Disney diffuse (Burley 2012 eq. 4) uses the
    // product fI(V)·fO(L) which commutes under V↔L, sheen depends only on
    // LdotH (= VdotH by symmetry of the half-vector), and every Cook-Torrance
    // lobe swaps NdotV ↔ NdotL without changing the value. So for any pair
    // of directions above the surface, f(V, L) must equal f(L, V) exactly
    // up to floating-point noise.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OpaqueMaterials))]
    public void Evaluate_IsReciprocal(
        float metallic, float roughness, float specular,
        float sheen, float clearcoat, float clearcoatGloss)
    {
        var material = MakeMaterial(metallic, roughness, specular, sheen, clearcoat, clearcoatGloss);
        var rec = MakeRec();

        var rng = new Random(
            HashCode.Combine(metallic, roughness, specular, sheen, clearcoat, clearcoatGloss));

        for (int i = 0; i < 256; i++)
        {
            var v = UniformHemisphereDirection(NormalUp, rng);
            var l = UniformHemisphereDirection(NormalUp, rng);

            var fVL = material.Evaluate(v, l, rec);
            var fLV = material.Evaluate(l, v, rec);

            AssertVectorClose(fVL, fLV, relTol: 1e-4f, absTol: 1e-6f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. PDF integration
    //
    // The BSDF PDF is a probability density over the unit sphere: ∫ pdf dω
    // must equal 1. Using uniform sphere sampling (density 1/(4π)), the
    // Monte Carlo estimator is 4π · E[pdf(L_i)] — so averaging many pdf
    // evaluations for uniformly-sampled L and multiplying by 4π approximates
    // the integral.
    //
    // For opaque configurations, Disney's PDF lives entirely in the upper
    // hemisphere (lower-hemisphere L returns zero), so the estimator should
    // land on 1 within statistical tolerance.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BroadLobeMaterials))]
    public void Pdf_IntegratesToOne_OverUnitSphere(
        float metallic, float roughness, float specular,
        float sheen, float clearcoat, float clearcoatGloss)
    {
        var material = MakeMaterial(metallic, roughness, specular, sheen, clearcoat, clearcoatGloss);
        var rec = MakeRec();

        // Viewing direction kept well off grazing so D·G doesn't produce
        // degenerate samples that inflate MC variance into false failures.
        var view = Vector3.Normalize(new Vector3(0.3f, 0.9f, 0.2f));

        var rng = new Random(
            HashCode.Combine("pdf", metallic, roughness, specular, sheen, clearcoat, clearcoatGloss));

        const int N = 32768;
        double sum = 0;
        for (int i = 0; i < N; i++)
        {
            var l = UniformSphereDirection(rng);
            sum += material.Pdf(view, l, rec);
        }

        double integral = 4.0 * Math.PI * sum / N;
        // Tolerance 8% — GGX PDFs have heavy tails near grazing angles so
        // uniform-sphere MC converges slowly there. Failing outside ±8%
        // with 32k samples indicates a real bug, not MC noise.
        Assert.InRange(integral, 0.92, 1.08);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Sampler consistency
    //
    // A well-formed BsdfSample must carry the same F and Pdf values that
    // Evaluate and Pdf would return for the sampled direction — anything
    // else would break the MIS weight computation downstream. For delta
    // lobes (IsDelta = true) the stored F is zero and Pdf is one by
    // convention and is skipped by this test.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OpaqueMaterials))]
    public void Sample_FAndPdf_MatchEvaluateAndPdf(
        float metallic, float roughness, float specular,
        float sheen, float clearcoat, float clearcoatGloss)
    {
        var material = MakeMaterial(metallic, roughness, specular, sheen, clearcoat, clearcoatGloss);
        var rec = MakeRec();
        var view = Vector3.Normalize(new Vector3(0.2f, 1.0f, 0.3f));

        int nonDelta = 0;
        for (int i = 0; i < 256; i++)
        {
            var sample = material.Sample(view, rec);
            if (sample is null || sample.Value.IsDelta) continue;
            nonDelta++;

            var wo = sample.Value.Wo;
            var expectedF = material.Evaluate(view, wo, rec);
            var expectedPdf = material.Pdf(view, wo, rec);

            AssertVectorClose(sample.Value.F, expectedF, relTol: 1e-3f, absTol: 1e-6f);
            Assert.InRange(sample.Value.Pdf, expectedPdf * 0.999f - 1e-6f,
                                             expectedPdf * 1.001f + 1e-6f);
        }

        // Sanity: the sampler should actually produce non-delta samples for
        // opaque reflection configurations. If it returns only deltas we'd
        // have silently skipped every assertion above.
        Assert.True(nonDelta > 100,
            $"expected >100 non-delta samples for opaque material, got {nonDelta}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Energy conservation — baseline diffuse furnace test
    //
    // For a fully diffuse Disney material (metallic=0, specular=0, sheen=0,
    // clearcoat=0, roughness=1 — which disables the retro-reflection
    // boost), the directional-hemispherical reflectance
    //   ∫_Ω f(V, L) · max(N·L, 0) dω
    // must not exceed 1 and should land close to the baseColor average.
    //
    // This is the minimum viable furnace test. More aggressive tests (high
    // roughness metal, coat + diffuse combinations) will follow once the
    // multi-scatter compensation lands — today's single-scatter Smith
    // already loses 20-40% of energy there, which is a separate workstream.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Diffuse_DirectionalHemisphericalReflectance_DoesNotExceedOne()
    {
        var baseColor = new Vector3(0.9f, 0.9f, 0.9f);
        var material = new DisneyBsdf(
            new SolidColor(baseColor),
            metallic: 0f, roughness: 1f, subsurface: 0f, specular: 0f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f,
            specTrans: 0f, ior: 1.5f);

        var rec = MakeRec();
        var view = Vector3.Normalize(new Vector3(0f, 1f, 0f));

        var rng = new Random(1234);
        const int N = 32768;
        Vector3 sum = Vector3.Zero;
        int hits = 0;
        for (int i = 0; i < N; i++)
        {
            var l = UniformSphereDirection(rng);
            float nDotL = Vector3.Dot(NormalUp, l);
            if (nDotL <= 0f) continue;
            hits++;
            sum += material.Evaluate(view, l, rec) * nDotL;
        }
        // Uniform sphere MC estimator: ∫ g dω ≈ 4π · Σ g_i / N.
        Vector3 reflectance = 4f * MathF.PI * sum / N;

        Assert.True(hits > N / 3, $"expected ≥N/3 upper-hemisphere samples, got {hits}");
        Assert.InRange(reflectance.X, 0.0, 1.0);
        Assert.InRange(reflectance.Y, 0.0, 1.0);
        Assert.InRange(reflectance.Z, 0.0, 1.0);

        // A perfect Lambert on baseColor = 0.9 returns exactly 0.9; Disney's
        // roughness=1 diffuse retains that identity (fd90 = 0.5 + 2·LdotH² > 1,
        // but fI·fO cancels out to 1 at roughness = 1 by design).
        Assert.InRange(reflectance.X, 0.85, 0.95);
        Assert.InRange(reflectance.Y, 0.85, 0.95);
        Assert.InRange(reflectance.Z, 0.85, 0.95);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
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
