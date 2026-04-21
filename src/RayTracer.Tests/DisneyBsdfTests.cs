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
    // lobes (IsDelta = true) F carries the Scatter attenuation directly
    // and Pdf is 1 by convention; these samples are skipped by this test
    // since Evaluate / Pdf are defined only for the non-delta reflection
    // lobes.
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
    // Textured parameter dispatch (phase 2 step 6)
    //
    // When a Disney parameter is texture-backed, EvalParams must pick up the
    // texture's per-point value instead of the ctor scalar. We spot-check by
    // wrapping "metallic" in a solid-color texture (constant = 1) and
    // confirming Evaluate returns the same value it would for a scalar
    // metallic = 1 build. Equality must hold at floating-point precision —
    // the code path is identical, only the source of the scalar differs.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvalParams_TextureBackedMetallic_MatchesScalarBuild()
    {
        var baseColor = new SolidColor(new Vector3(0.7f, 0.5f, 0.3f));

        var scalarBuild = new DisneyBsdf(baseColor,
            metallic: 1f, roughness: 0.4f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f);

        var textureBuild = new DisneyBsdf(baseColor,
            metallic: new FloatTexture(new SolidColor(Vector3.One)), // 1.0 everywhere
            roughness: 0.4f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f);

        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));
        var L = Vector3.Normalize(new Vector3(-0.2f, 0.8f, 0.4f));

        AssertVectorClose(
            scalarBuild.Evaluate(V, L, rec),
            textureBuild.Evaluate(V, L, rec),
            relTol: 1e-6f, absTol: 1e-8f);

        AssertClose(
            scalarBuild.Pdf(V, L, rec),
            textureBuild.Pdf(V, L, rec),
            relTol: 1e-6f, absTol: 1e-8f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Anisotropy (phase 2 step 7)
    //
    // With anisotropic > 0 the specular NDF is stretched along the tangent
    // axis (αx = α/aspect > αy = α·aspect), so highlights widen along T and
    // tighten along B. We verify the direction-dependent asymmetry with a
    // pure metal at V = N, sampling two L directions of equal NdotL — one
    // pushed toward +T, the other toward +B. The T-aligned direction must
    // land in a region of higher NDF energy, so Evaluate must be strictly
    // greater along T than along B.
    //
    // Reciprocity and PDF-integration are also checked once more under
    // anisotropy to confirm the tangent-space formulation has not broken
    // either invariant.
    // ─────────────────────────────────────────────────────────────────────────

    private static HitRecord MakeRecWithTBN(Vector3 N, Vector3 T)
    {
        var bit = Vector3.Normalize(Vector3.Cross(N, T));
        var tan = Vector3.Cross(bit, N); // re-orthogonalised tangent
        return new HitRecord
        {
            Normal = Vector3.Normalize(N),
            Tangent = Vector3.Normalize(tan),
            Bitangent = bit,
            Point = Vector3.Zero,
            LocalPoint = Vector3.Zero,
            U = 0f,
            V = 0f,
            ObjectSeed = 0,
            FrontFace = true,
        };
    }

    [Fact]
    public void Anisotropic_StretchesLobeAlongTangent()
    {
        // Smooth metal (α ≈ 0.09) with heavy anisotropy: αx ≈ 0.21, αy ≈ 0.039.
        var baseColor = new SolidColor(new Vector3(1f, 1f, 1f));
        var material = new DisneyBsdf(
            baseColor,
            metallic: 1f, roughness: 0.3f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            anisotropic: 0.9f, anisotropicRotation: 0f);

        // Shading frame with T = +X, N = +Y → B = -Z.
        var rec = MakeRecWithTBN(N: Vector3.UnitY, T: Vector3.UnitX);

        // V along the normal so the perfect-reflection direction is +N. Any
        // off-normal L then sits symmetrically in the lobe and the asymmetry
        // isolates to the (αx, αy) anisotropy.
        var V = Vector3.UnitY;
        const float off = 0.35f;
        float up = MathF.Sqrt(1f - off * off);

        // Push L equally along T (+X) and along B-axis (±Z). Both have the
        // same NdotL and the same reflection-half-vector elevation, so any
        // difference in Evaluate is attributable to αx ≠ αy.
        var Ltan = Vector3.Normalize(new Vector3(off, up, 0f));
        var Lbit = Vector3.Normalize(new Vector3(0f, up, off));

        var fT = material.Evaluate(V, Ltan, rec);
        var fB = material.Evaluate(V, Lbit, rec);

        // Lobe is stretched along T → the T-aligned sample is still inside
        // the bright core while the B-aligned sample has fallen onto the
        // narrow, steep flank. Factor of 2× is a conservative floor; the
        // real ratio at these params is ~10×.
        Assert.True(fT.X > 2f * fB.X,
            $"expected tangent-aligned f > 2× bitangent-aligned f; got fT={fT.X}, fB={fB.X}");
    }

    [Fact]
    public void Anisotropic_RotationSwapsTangentAndBitangent()
    {
        // A 0.25 rotation (90° CCW around N) maps T → B and B → -T, so the
        // stretch axis rotates accordingly and the T-vs-B relationship
        // from the previous test flips sign.
        var baseColor = new SolidColor(Vector3.One);
        var rotated = new DisneyBsdf(
            baseColor,
            metallic: 1f, roughness: 0.3f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            anisotropic: 0.9f, anisotropicRotation: 0.25f);

        var rec = MakeRecWithTBN(N: Vector3.UnitY, T: Vector3.UnitX);
        var V = Vector3.UnitY;
        const float off = 0.35f;
        float up = MathF.Sqrt(1f - off * off);
        var Ltan = Vector3.Normalize(new Vector3(off, up, 0f));
        var Lbit = Vector3.Normalize(new Vector3(0f, up, off));

        var fT = rotated.Evaluate(V, Ltan, rec);
        var fB = rotated.Evaluate(V, Lbit, rec);

        Assert.True(fB.X > 2f * fT.X,
            $"expected bitangent-aligned f > 2× tangent-aligned f after 90° rotation; got fT={fT.X}, fB={fB.X}");
    }

    [Fact]
    public void Anisotropic_ReciprocityHolds()
    {
        var baseColor = new SolidColor(new Vector3(0.7f, 0.5f, 0.3f));
        var material = new DisneyBsdf(
            baseColor,
            metallic: 1f, roughness: 0.4f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            anisotropic: 0.8f, anisotropicRotation: 0.1f);
        var rec = MakeRecWithTBN(N: Vector3.UnitY, T: Vector3.UnitX);

        var rng = new Random(9741);
        for (int i = 0; i < 256; i++)
        {
            var v = UniformHemisphereDirection(rec.Normal, rng);
            var l = UniformHemisphereDirection(rec.Normal, rng);
            var fVL = material.Evaluate(v, l, rec);
            var fLV = material.Evaluate(l, v, rec);
            AssertVectorClose(fVL, fLV, relTol: 1e-4f, absTol: 1e-6f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Multi-scatter energy compensation (phase 2 step 8)
    //
    // Smith single-scattering GGX leaks energy at high α — a white rough
    // metal without compensation integrates to ~0.65 directional albedo
    // instead of 1. The Kulla-Conty additive lobe recovers the missing
    // energy; at white F₀ = 1 the closed-form identity makes the total
    // directional-hemispherical reflectance land exactly on 1 (modulo MC
    // noise).
    //
    // We verify the closed-form identity with a uniform-sphere MC estimator
    // on Evaluate — high sample count because the narrow near-mirror tail
    // of the specular lobe converges slowly even at α = 0.81. The PDF
    // invariant is re-checked with compensation active to confirm the new
    // cosine-weighted multiscatter lobe preserves the one-sphere integral.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Multiscatter_RoughWhiteMetal_ApproachesUnityReflectance()
    {
        // White metal, α = 0.81 (roughness = 0.9). E(1, 0.81) ≈ 0.7 and
        // E_avg(0.81) ≈ 0.7, so the single-scatter directional albedo is
        // ~0.70. After compensation the identity
        //   R = E(μo) + F_ms · (1 − E(μo)) · 1   (with F̄ = F_ms = 1)
        //     = E(μo) + (1 − E(μo)) = 1
        // holds exactly for white F₀; MC noise on uniform-sphere sampling
        // of a glossy lobe floors the measurable reflectance around 0.90.
        var baseColor = new SolidColor(Vector3.One);
        var material = new DisneyBsdf(
            baseColor,
            metallic: 1f, roughness: 0.9f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f);

        var rec = MakeRec();
        var view = Vector3.UnitY; // normal incidence: μo = 1

        var rng = new Random(2026);
        const int N = 131072;
        double sumR = 0.0;
        int hits = 0;
        for (int i = 0; i < N; i++)
        {
            var l = UniformSphereDirection(rng);
            float nDotL = Vector3.Dot(NormalUp, l);
            if (nDotL <= 0f) continue;
            hits++;
            var f = material.Evaluate(view, l, rec);
            sumR += f.X * nDotL;
        }
        double reflectance = 4.0 * Math.PI * sumR / N;

        Assert.True(hits > N / 3, $"expected ≥N/3 upper-hemisphere samples, got {hits}");
        // Floor at 0.90 gives ample headroom for MC noise at 131k samples;
        // without compensation the value would be ~0.70 and would fail
        // this bound cleanly, catching regressions on the LUT wiring.
        Assert.True(reflectance > 0.90,
            $"expected reflectance > 0.90 after multiscatter, got {reflectance}");
        // Upper bound guards against over-compensation — a physical BRDF
        // can never exceed unit directional-hemispherical reflectance.
        Assert.True(reflectance <= 1.05,
            $"reflectance should not exceed 1 (furnace violation), got {reflectance}");
    }

    [Fact]
    public void Multiscatter_ColouredMetal_RetainsHueAtHighRoughness()
    {
        // Without compensation a rough gold metal darkens AND desaturates
        // (every channel loses ~30% proportionally, but the saturation drop
        // is visible because channels drop by slightly different amounts).
        // With compensation the per-channel hue is preserved while each
        // channel reaches its own F̄-conditioned ceiling.
        //
        // We check that the red channel (baseCol.X = 1) lands much closer
        // to 1 than the blue channel (baseCol.Z = 0.3) — i.e. chrominance
        // is retained, not washed out.
        var baseColor = new SolidColor(new Vector3(1.0f, 0.71f, 0.29f));
        var material = new DisneyBsdf(
            baseColor,
            metallic: 1f, roughness: 0.85f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f);

        var rec = MakeRec();
        var view = Vector3.UnitY;

        var rng = new Random(77);
        const int N = 131072;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < N; i++)
        {
            var l = UniformSphereDirection(rng);
            float nDotL = Vector3.Dot(NormalUp, l);
            if (nDotL <= 0f) continue;
            sum += material.Evaluate(view, l, rec) * nDotL;
        }
        Vector3 reflectance = 4f * MathF.PI * sum / N;

        // Red channel lands close to 1 (F₀ = 1), blue near its F̄ ceiling (~0.34).
        Assert.True(reflectance.X > 0.85,
            $"red reflectance should retain close to F₀=1 intensity, got {reflectance.X}");
        // The ratio R/B must stay substantially above 1 — if it collapsed
        // toward 1 we'd have desaturated gold to grey, the exact failure
        // mode compensation is supposed to avoid.
        Assert.True(reflectance.X / reflectance.Z > 2.0f,
            $"gold should retain chroma (R/B ratio), got {reflectance.X / reflectance.Z}");
    }

    [Fact]
    public void Anisotropic_PdfIntegratesToOne()
    {
        var baseColor = new SolidColor(Vector3.One);
        // Rough metal — broad enough lobe to let uniform-sphere MC land
        // close to 1 in a reasonable sample budget even under anisotropy.
        var material = new DisneyBsdf(
            baseColor,
            metallic: 1f, roughness: 0.7f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            anisotropic: 0.6f, anisotropicRotation: 0f);
        var rec = MakeRecWithTBN(N: Vector3.UnitY, T: Vector3.UnitX);
        var view = Vector3.Normalize(new Vector3(0.3f, 0.9f, 0.2f));

        var rng = new Random(8842);
        const int N = 32768;
        double sum = 0;
        for (int i = 0; i < N; i++)
        {
            var l = UniformSphereDirection(rng);
            sum += material.Pdf(view, l, rec);
        }
        double integral = 4.0 * Math.PI * sum / N;
        Assert.InRange(integral, 0.92, 1.08);
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
