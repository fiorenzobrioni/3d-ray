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
    // Glass: Beer-Lambert interior absorption (step 9)
    //
    // The Disney glass lobe carries a medium-switch signal on refraction
    // (BsdfSample.NextSegmentAbsorption) so the renderer can apply
    // exp(-σ_a · t) along the next segment. These tests verify that σ_a is
    // derived from transmission_color / transmission_depth according to the
    // Beer-Lambert formula σ_a = -ln(C) / D, and that front-face vs back-face
    // refractions populate the signal correctly.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TransmissionSample_FrontFace_ReportsBeerLambertSigma()
    {
        // tColor in (0, 1) at depth 1 → σ_a = -ln(tColor).
        var tColor = new Vector3(0.8f, 0.5f, 0.1f);
        var expectedSigma = new Vector3(
            -MathF.Log(tColor.X),
            -MathF.Log(tColor.Y),
            -MathF.Log(tColor.Z));

        var material = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            metallic: 0f, roughness: 0.0f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 1f, ior: 1.5f,
            anisotropic: 0f, anisotropicRotation: 0f,
            transmissionColor: new SolidColor(tColor),
            transmissionDepth: new FloatTexture(1.0f));

        // View pointing into the surface from above (N = +Y). FrontFace = true.
        var rec = MakeRec();
        var view = Vector3.UnitY;

        int refractions = 0;
        for (int i = 0; i < 256; i++)
        {
            var s = material.Sample(view, rec);
            if (!s.HasValue) continue;
            var sample = s.Value;
            float NdotWo = Vector3.Dot(rec.Normal, sample.Wo);
            if (NdotWo < 0f && sample.IsDelta)
            {
                refractions++;
                Assert.True(sample.NextSegmentAbsorption.HasValue,
                    "front-face refraction sample must carry a medium-switch signal");
                AssertVectorClose(sample.NextSegmentAbsorption!.Value, expectedSigma,
                                  relTol: 1e-3f, absTol: 1e-4f);
            }
        }
        // 256 samples on a smooth glass at normal incidence: most Schlick
        // coin flips favour refraction (F ≈ 0.04). Requiring ≥ 1 refraction
        // is extremely conservative and guards against a degenerate lobe
        // selection that would silently skip the assertion.
        Assert.True(refractions > 0,
            "expected at least one front-face refraction sample");
    }

    [Fact]
    public void TransmissionSample_ThinGlass_EmitsNoMediumSwitch()
    {
        // With depth = 0 the σ_a is zero → the sampler should not emit a
        // medium-switch signal on FrontFace refractions (there is no
        // interior medium to enter). The per-hit tint is carried by
        // BsdfSample.F instead.
        var material = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            metallic: 0f, roughness: 0.0f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 1f, ior: 1.5f,
            anisotropic: 0f, anisotropicRotation: 0f,
            transmissionColor: new SolidColor(new Vector3(0.9f, 0.6f, 0.3f)),
            transmissionDepth: new FloatTexture(0f));

        var rec = MakeRec();
        var view = Vector3.UnitY;

        int refractions = 0;
        for (int i = 0; i < 256; i++)
        {
            var s = material.Sample(view, rec);
            if (!s.HasValue) continue;
            var sample = s.Value;
            float NdotWo = Vector3.Dot(rec.Normal, sample.Wo);
            if (NdotWo < 0f && sample.IsDelta)
            {
                refractions++;
                Assert.False(sample.NextSegmentAbsorption.HasValue,
                    "thin-glass refraction must not signal a medium switch");
            }
        }
        Assert.True(refractions > 0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disney 2015: thin_walled / diff_trans / flatness / subsurface_color
    // (phase 2 step 10)
    //
    // Thin-walled glass is a membrane: refraction is disabled, Fresnel is
    // evaluated on both faces with η = 1/IOR, no Beer-Lambert medium switch
    // is emitted. diff_trans adds a dedicated cosine-weighted back-hemisphere
    // diffuse lobe tinted by subsurface_color. Flatness blends the Lambert
    // shape toward the HK "flat" subsurface approximation independently of
    // the Subsurface parameter.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThinWalled_RefractionKeepsIncomingDirection_NoMediumSwitch()
    {
        // Smooth thin-walled glass with a saturated tint. Refraction samples
        // must continue along the incoming ray (no bending) and must NOT
        // emit a medium-switch signal — thin-walled has no interior volume.
        var material = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            metallic: 0f, roughness: 0f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 1f, ior: 1.5f,
            anisotropic: 0f, anisotropicRotation: 0f,
            transmissionColor: new SolidColor(new Vector3(0.4f, 0.8f, 0.2f)),
            transmissionDepth: new FloatTexture(3f), // would be Beer-Lambert if not thin-walled
            thinWalled: true);

        var rec = MakeRec();
        // Oblique view so refraction would normally bend the ray visibly.
        var view = Vector3.Normalize(new Vector3(0.4f, 0.9f, 0.15f));
        Vector3 expectedWo = -view; // incoming direction = -view

        int refractions = 0;
        for (int i = 0; i < 512; i++)
        {
            var s = material.Sample(view, rec);
            if (!s.HasValue) continue;
            var sample = s.Value;
            float NdotWo = Vector3.Dot(rec.Normal, sample.Wo);
            if (NdotWo < 0f && sample.IsDelta)
            {
                refractions++;
                Assert.False(sample.NextSegmentAbsorption.HasValue,
                    "thin-walled refraction must not signal a medium switch");
                AssertVectorClose(sample.Wo, expectedWo, relTol: 1e-4f, absTol: 1e-5f);
            }
        }
        Assert.True(refractions > 0,
            "expected at least one thin-walled refraction sample");
    }

    [Fact]
    public void DiffTrans_ProducesBackHemisphereSamples_WithSubsurfaceTint()
    {
        // A pure foliage material: all diffuse energy goes backward, tinted
        // by subsurface_color (simulating the translucent green a leaf
        // acquires when backlit).
        var leafTint = new Vector3(0.2f, 0.7f, 0.1f);
        var material = new DisneyBsdf(
            baseColor: new SolidColor(new Vector3(0.4f, 0.6f, 0.25f)),
            metallic: 0f, roughness: 1f, subsurface: 0f, specular: 0f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            anisotropic: 0f, anisotropicRotation: 0f,
            subsurfaceColor: new SolidColor(leafTint),
            diffTrans: 1f); // all diffuse goes backward

        var rec = MakeRec();
        var view = Vector3.UnitY;

        int backSamples = 0;
        for (int i = 0; i < 512; i++)
        {
            var s = material.Sample(view, rec);
            if (!s.HasValue) continue;
            float NdotWo = Vector3.Dot(rec.Normal, s.Value.Wo);
            if (NdotWo < 0f) backSamples++;
        }
        Assert.True(backSamples > 100,
            $"expected majority of samples to land in back hemisphere; got {backSamples}/512");

        // Analytical BRDF at a back-hemisphere L should be leafTint · (1/π)
        // scaled by diffAll (= 1 here). Reflection lobes must return zero.
        var Lback = Vector3.Normalize(new Vector3(0.1f, -0.8f, 0.2f));
        var f = material.Evaluate(view, Lback, rec);
        var expected = leafTint / MathF.PI;
        AssertVectorClose(f, expected, relTol: 1e-4f, absTol: 1e-6f);
    }

    [Fact]
    public void DiffTrans_PdfIsPositiveAndNormalised()
    {
        // With diff_trans = 0.5 the PDF splits evenly between the forward
        // diffuse hemisphere and the back diff_trans hemisphere. Integrated
        // over the unit sphere it must still land on 1.
        var material = new DisneyBsdf(
            baseColor: new SolidColor(new Vector3(0.5f, 0.5f, 0.5f)),
            metallic: 0f, roughness: 1f, subsurface: 0f, specular: 0f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            diffTrans: 0.5f);

        var rec = MakeRec();
        var view = Vector3.Normalize(new Vector3(0.2f, 0.9f, 0.1f));

        var rng = new Random(4242);
        const int N = 32768;
        double sum = 0;
        int backHits = 0;
        for (int i = 0; i < N; i++)
        {
            var l = UniformSphereDirection(rng);
            float p = material.Pdf(view, l, rec);
            sum += p;
            if (p > 0f && Vector3.Dot(rec.Normal, l) < 0f) backHits++;
        }
        double integral = 4.0 * Math.PI * sum / N;
        Assert.InRange(integral, 0.92, 1.08);
        Assert.True(backHits > 100,
            $"expected back-hemisphere PDF to be nonzero for diff_trans > 0; got {backHits}");
    }

    [Fact]
    public void Flatness_FullyFlat_MatchesSubsurfaceFullyOn()
    {
        // flatness=1 should produce the same Evaluate value as subsurface=1,
        // flatness=0 — both collapse the Lambert↔HK-flat blend onto the
        // pure HK-flat shape regardless of the other slider.
        var baseColor = new SolidColor(new Vector3(0.7f, 0.5f, 0.3f));
        var flatOnly = new DisneyBsdf(baseColor,
            metallic: 0f, roughness: 0.6f, subsurface: 0f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0.5f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            flatness: 1f);
        var ssOnly = new DisneyBsdf(baseColor,
            metallic: 0f, roughness: 0.6f, subsurface: 1f, specular: 0.5f,
            specularTint: 0f, sheen: 0f, sheenTint: 0.5f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            flatness: 0f);

        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));
        var L = Vector3.Normalize(new Vector3(-0.15f, 0.85f, 0.5f));

        AssertVectorClose(
            flatOnly.Evaluate(V, L, rec),
            ssOnly.Evaluate(V, L, rec),
            relTol: 1e-5f, absTol: 1e-7f);
    }

    [Fact]
    public void SubsurfaceColor_OverridesBaseColorInFlatLobe()
    {
        // With subsurface = 1 the diffuse shape is entirely HK-flat and is
        // tinted by subsurface_color rather than base_color. A red base with
        // a blue subsurface_color should produce a blue-biased diffuse
        // response, not a red one.
        var baseColor = new Vector3(0.9f, 0.1f, 0.1f); // red
        var ssTint    = new Vector3(0.1f, 0.1f, 0.9f); // blue
        var material = new DisneyBsdf(
            baseColor: new SolidColor(baseColor),
            metallic: 0f, roughness: 0.8f, subsurface: 1f, specular: 0f,
            specularTint: 0f, sheen: 0f, sheenTint: 0f,
            clearcoat: 0f, clearcoatGloss: 1f, specTrans: 0f, ior: 1.5f,
            subsurfaceColor: new SolidColor(ssTint));

        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f));
        var L = Vector3.Normalize(new Vector3(-0.1f, 0.9f, 0.3f));

        var f = material.Evaluate(V, L, rec);
        // Blue component must dominate — exact value depends on the HK
        // shape but the hue shift must be unmistakable.
        Assert.True(f.Z > f.X * 2f,
            $"subsurface_color tint should dominate: expected blue > 2× red; got {f}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Coat parameters (Arnold standard_surface parity: coat_ior, coat_roughness,
    // coat_normal). These tests pin the new behaviour added in Step 11 of the
    // Disney BSDF roadmap.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Higher coat_ior → larger F0 → brighter clearcoat highlight at normal
    /// incidence. Default IOR is 1.5 (F0 ≈ 0.04, classic Disney); a high-IOR
    /// lacquer like η = 2.4 gives F0 ≈ 0.17, more than 4× the energy. The
    /// test isolates the coat lobe (everything else off) and compares the
    /// lobe peak across two IOR values.
    /// </summary>
    [Fact]
    public void CoatIor_HigherIor_BrightensFresnel()
    {
        var baseColor = new SolidColor(new Vector3(0.5f));
        // Clearcoat fully on, no other lobes — diffuse weight is 1 by Disney's
        // base-Lambert default but we'll evaluate at a near-normal direction
        // where the coat lobe sits on top of it; subtracting the diffuse
        // reference would couple the two, so we sweep IOR and compare DELTA.
        DisneyBsdf Coat(float ior) => new(
            baseColor,
            metallic: 0f, roughness: 1f, specular: 0f, sheen: 0f,
            clearcoat: 1f, clearcoatGloss: 1f,
            coatIor: ior);

        var rec = MakeRec();
        var V = NormalUp;       // straight at the surface
        var L = NormalUp;       // perfect retroreflection — coat peak

        Vector3 fLow  = Coat(1.5f).Evaluate(V, L, rec);
        Vector3 fHigh = Coat(2.4f).Evaluate(V, L, rec);

        // F0(2.4)/F0(1.5) = 0.1701 / 0.04 ≈ 4.25, but the diffuse base lobe
        // contributes too. The clearcoat *delta* must be substantially
        // positive: at NdotL = 1 the coat highlight is at peak intensity.
        float lumLow  = MathUtils.Luminance(fLow);
        float lumHigh = MathUtils.Luminance(fHigh);
        Assert.True(lumHigh > lumLow * 1.5f,
            $"high coat_ior should noticeably brighten the lobe: low={lumLow}, high={lumHigh}");
    }

    /// <summary>
    /// coat_roughness, when supplied, overrides the legacy clearcoat_gloss
    /// path. A material with coat_roughness = 0.3 should behave the same
    /// as one with the corresponding α-equivalent gloss disabled. This pins
    /// that the new path is reached and the legacy gloss slider doesn't
    /// shadow it.
    /// </summary>
    [Fact]
    public void CoatRoughness_OverridesGloss()
    {
        var baseColor = new SolidColor(new Vector3(0.5f));
        // Same coat_roughness, but contradictory gloss values. If gloss
        // overrode roughness, the two would diverge. They should be equal.
        var matA = new DisneyBsdf(baseColor,
            clearcoat: 1f, clearcoatGloss: 0.0f,   // legacy α = 0.1
            coatRoughness: 0.3f);                  // explicit α = 0.09
        var matB = new DisneyBsdf(baseColor,
            clearcoat: 1f, clearcoatGloss: 1.0f,   // legacy α = 0.001
            coatRoughness: 0.3f);                  // explicit α = 0.09

        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));
        var L = Vector3.Normalize(new Vector3(-0.15f, 0.85f, 0.5f));

        AssertVectorClose(
            matA.Evaluate(V, L, rec),
            matB.Evaluate(V, L, rec),
            relTol: 1e-5f, absTol: 1e-7f);
    }

    /// <summary>
    /// Lower coat_roughness narrows the lobe (more energy at the specular
    /// peak, less in the tails). We probe the lobe peak (V == L == N) and
    /// require the smoother coat to outshine the rougher one.
    /// </summary>
    [Fact]
    public void CoatRoughness_LowerRoughness_NarrowsLobe()
    {
        var baseColor = new SolidColor(new Vector3(0.5f));
        DisneyBsdf Coat(float r) => new(
            baseColor,
            metallic: 0f, roughness: 1f, specular: 0f, sheen: 0f,
            clearcoat: 1f,
            coatRoughness: r);

        var rec = MakeRec();
        var V = NormalUp;
        var L = NormalUp;

        Vector3 fSmooth = Coat(0.05f).Evaluate(V, L, rec);
        Vector3 fRough  = Coat(0.6f) .Evaluate(V, L, rec);

        Assert.True(MathUtils.Luminance(fSmooth) > MathUtils.Luminance(fRough),
            $"smoother coat should peak higher: smooth={fSmooth}, rough={fRough}");
    }

    /// <summary>
    /// PDF normalisation must hold under the new explicit-roughness coat
    /// path too. We pick a moderately rough coat (broad lobe, MC-tractable)
    /// and integrate over the unit sphere via uniform sampling.
    /// </summary>
    [Fact]
    public void Pdf_IntegratesToOne_WithCoatRoughness()
    {
        var baseColor = new SolidColor(new Vector3(0.5f));
        var material = new DisneyBsdf(baseColor,
            metallic: 0f, roughness: 1f, specular: 0f, sheen: 0f,
            clearcoat: 1f,
            coatRoughness: 0.5f);

        var rec = MakeRec();
        var V = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));

        const int samples = 100_000;
        const float surfaceArea = 4f * MathF.PI;
        var rng = new Random(0xC0A7);
        double sum = 0.0;
        for (int i = 0; i < samples; i++)
            sum += material.Pdf(V, UniformSphereDirection(rng), rec);
        double integral = sum * surfaceArea / samples;
        Assert.InRange(integral, 0.95, 1.05);
    }

    /// <summary>
    /// When coat_roughness is omitted the legacy ClearcoatGloss path must
    /// drive α exactly as before — pinned by reproducing a hand-computed
    /// value from the gloss → α mapping (α = lerp(0.1, 0.001, gloss),
    /// gloss = 1 → α = 0.001, gloss = 0 → α = 0.1). We compare two
    /// materials at the lobe peak (V == L == N): the one with the higher
    /// gloss must have a higher peak.
    /// </summary>
    [Fact]
    public void ClearcoatGloss_StillDrivesLegacyAlpha_WhenCoatRoughnessOmitted()
    {
        var baseColor = new SolidColor(new Vector3(0.5f));
        // No coat_roughness → legacy path. gloss=1 → α=0.001 (very sharp);
        // gloss=0 → α=0.1 (broader).
        var glossy = new DisneyBsdf(baseColor,
            metallic: 0f, roughness: 1f, specular: 0f, sheen: 0f,
            clearcoat: 1f, clearcoatGloss: 1f);
        var satin = new DisneyBsdf(baseColor,
            metallic: 0f, roughness: 1f, specular: 0f, sheen: 0f,
            clearcoat: 1f, clearcoatGloss: 0f);

        var rec = MakeRec();
        var V = NormalUp;
        var L = NormalUp;

        Vector3 fGlossy = glossy.Evaluate(V, L, rec);
        Vector3 fSatin  = satin .Evaluate(V, L, rec);

        Assert.True(MathUtils.Luminance(fGlossy) > MathUtils.Luminance(fSatin),
            $"legacy gloss=1 must peak higher than gloss=0: glossy={fGlossy}, satin={fSatin}");
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
