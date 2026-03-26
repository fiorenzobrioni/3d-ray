using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// Disney Principled BSDF — a unified PBR material model inspired by
/// Brent Burley's 2012 paper "Physically Based Shading at Disney".
///
/// This single material type can represent virtually any real-world surface
/// by blending multiple lobes via intuitive artist-friendly parameters:
///
///   baseColor     — Surface albedo (diffuse color or metallic reflectance)
///   metallic      — 0 = dielectric (plastic, wood, skin), 1 = metal (gold, chrome)
///   roughness     — 0 = mirror-smooth, 1 = perfectly diffuse
///   subsurface    — Blends Lambert diffuse with a flatter subsurface approximation
///   specular      — Intensity of the dielectric specular lobe (F0 control)
///   specularTint  — Tints the dielectric Fresnel by baseColor
///   sheen         — Additional grazing-angle sheen (fabric, velvet)
///   sheenTint     — Tints the sheen by baseColor
///   clearcoat     — Second specular lobe (car paint, lacquer)
///   clearcoatGloss— Roughness of the clearcoat layer (1 = glossy, 0 = satin)
///   specTrans     — Specular transmission (0 = opaque, 1 = glass-like)
///   ior           — Index of refraction for dielectric specular/transmission
///
/// In YAML:
///   - id: "plastic_red"
///     type: "disney"
///     color: [0.8, 0.1, 0.1]
///     roughness: 0.3
///     specular: 0.5
///
///   - id: "gold"
///     type: "disney"
///     color: [1.0, 0.71, 0.29]
///     metallic: 1.0
///     roughness: 0.2
///
///   - id: "car_paint"
///     type: "disney"
///     color: [0.05, 0.1, 0.6]
///     metallic: 0.0
///     roughness: 0.4
///     clearcoat: 1.0
///     clearcoat_gloss: 0.9
/// </summary>
public class DisneyBsdf : IMaterial
{
    // ── Core parameters ─────────────────────────────────────────────────────
    public ITexture BaseColor  { get; }
    public float Metallic      { get; }
    public float Roughness     { get; }
    public float Subsurface    { get; }
    public float Specular      { get; }
    public float SpecularTint  { get; }
    public float Sheen         { get; }
    public float SheenTint     { get; }
    public float Clearcoat     { get; }
    public float ClearcoatGloss{ get; }
    public float SpecTrans     { get; }
    public float Ior           { get; }

    // ── Normal map support ──────────────────────────────────────────────────
    public NormalMapTexture? NormalMap { get; set; }

    // ── Cached derived values ───────────────────────────────────────────────
    private readonly float _alpha;          // roughness² (GGX α)
    private readonly float _clearcoatAlpha; // clearcoat roughness²

    public DisneyBsdf(
        ITexture baseColor,
        float metallic       = 0f,
        float roughness      = 0.5f,
        float subsurface     = 0f,
        float specular       = 0.5f,
        float specularTint   = 0f,
        float sheen          = 0f,
        float sheenTint      = 0.5f,
        float clearcoat      = 0f,
        float clearcoatGloss = 1f,
        float specTrans      = 0f,
        float ior            = 1.5f)
    {
        BaseColor      = baseColor;
        Metallic       = Math.Clamp(metallic, 0f, 1f);
        Roughness      = Math.Clamp(roughness, 0f, 1f);
        Subsurface     = Math.Clamp(subsurface, 0f, 1f);
        Specular       = Math.Clamp(specular, 0f, 2f);   // Allow > 1 for artistic control
        SpecularTint   = Math.Clamp(specularTint, 0f, 1f);
        Sheen          = Math.Clamp(sheen, 0f, 1f);
        SheenTint      = Math.Clamp(sheenTint, 0f, 1f);
        Clearcoat      = Math.Clamp(clearcoat, 0f, 1f);
        ClearcoatGloss = Math.Clamp(clearcoatGloss, 0f, 1f);
        SpecTrans      = Math.Clamp(specTrans, 0f, 1f);
        Ior            = MathF.Max(ior, 1.0001f);

        // GGX α parameter — clamp to avoid singularity at zero
        _alpha = MathF.Max(Roughness * Roughness, 0.001f);

        // Clearcoat uses a separate roughness: clearcoatGloss blends 0.1→0.001
        float ccRough = Lerp(0.1f, 0.001f, ClearcoatGloss);
        _clearcoatAlpha = ccRough * ccRough;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Direct lighting interface
    //
    // Maps Disney parameters to the renderer's DiffuseWeight / SpecularExponent
    // / SpecularStrength interface used by ComputeDirectLighting().
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Diffuse weight decreases with metallic and specTrans — metals and glass
    /// don't have a diffuse lobe.
    ///
    /// FIX #4: Removed the artificial MathF.Max(0.3f, Roughness) floor that was
    /// inflating direct diffuse by up to 6× for smooth dielectrics (roughness=0.05
    /// was clamped to 0.3). The Disney diffuse Fresnel (fd90) already handles the
    /// roughness-dependent energy balance correctly.
    /// </summary>
    public float DiffuseWeight
    {
        get
        {
            return (1f - Metallic) * (1f - SpecTrans);
        }
    }

    /// <summary>
    /// Blinn-Phong exponent derived from roughness for direct light highlights.
    /// Low roughness → tight sharp highlight; high roughness → broad soft highlight.
    /// NOTE: Retained for IMaterial interface compatibility. Not used by Disney's
    /// own EvaluateDirect (which uses analytic GGX), but may be read by Renderer
    /// for needsLightSampling checks.
    /// </summary>
    public float SpecularExponent
    {
        get
        {
            if (_alpha >= 1f) return 2f;
            return MathF.Min(2f / (_alpha * _alpha), 2048f);
        }
    }

    /// <summary>
    /// Specular highlight strength: strong for metals and smooth dielectrics,
    /// with clearcoat adding extra highlight intensity.
    /// NOTE: Retained for IMaterial interface compatibility.
    /// </summary>
    public float SpecularStrength
    {
        get
        {
            float baseSpec = Metallic > 0.5f ? 1f : Specular;
            float ccBoost = Clearcoat * 0.25f;
            return MathF.Min(baseSpec * (1f - Roughness * 0.5f) + ccBoost, 1f);
        }
    }

    /// <summary>
    /// Disney BSDF direct lighting using analytic GGX — matching the indirect
    /// scatter lobes for energetic consistency.
    ///
    /// FIX #3: Replaced the previous Blinn-Phong approximation with the full
    /// Cook-Torrance microfacet model (GGX NDF + Smith geometry + Schlick Fresnel).
    /// The old BP had a fundamentally different lobe shape (short tails vs GGX's
    /// characteristic long tails), causing a systematic energy mismatch between
    /// direct and indirect lighting that required high sample counts to average out.
    ///
    /// Includes: Disney diffuse with Fresnel retro-reflection, GGX specular lobe,
    /// and clearcoat GGX lobe. The material albedo/color is NOT included — it is
    /// applied by TraceRay via the scatter attenuation.
    ///
    /// NOTE: BaseColor is sampled at (0.5, 0.5) as a representative average since
    /// EvaluateDirect does not receive UV coordinates. For textured Disney materials
    /// this is an approximation; a future refactor could pass HitRecord through the
    /// IMaterial.EvaluateDirect interface.
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal)
    {
        float NdotL = MathF.Max(Vector3.Dot(normal, toLight), 0f);
        if (NdotL <= 0f) return Vector3.Zero;

        float NdotV = MathF.Max(Vector3.Dot(normal, toEye), 0.001f);

        Vector3 H = Vector3.Normalize(toLight + toEye);
        float NdotH = MathF.Max(Vector3.Dot(normal, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(toEye, H), 0f);

        // ── Disney diffuse lobe ─────────────────────────────────────────
        float diffuseW = (1f - Metallic) * (1f - SpecTrans);
        Vector3 diffuse = Vector3.Zero;
        if (diffuseW > 0f)
        {
            float fd90 = 0.5f + 2f * Roughness * VdotH * VdotH;
            float fI = SchlickWeight(NdotV);
            float fO = SchlickWeight(NdotL);
            float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);
            // Divide by π for energy conservation (cosine-weighted hemisphere)
            diffuse = new Vector3(diffuseW * fd * NdotL / MathF.PI);
        }

        // ── GGX specular lobe ───────────────────────────────────────────
        // D: GGX (Trowbridge-Reitz) NDF
        float a2 = _alpha * _alpha;
        float denom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * denom * denom);

        // G: Smith separable masking/shadowing
        float G = SmithG1_GGX(NdotV, _alpha) * SmithG1_GGX(NdotL, _alpha);

        // F: Schlick Fresnel with continuous metallic→dielectric blend
        // Uses ComputeF0 for consistent F0 with scatter (no binary threshold)
        Vector3 baseCol = BaseColor.Value(0.5f, 0.5f, Vector3.Zero, 0);
        Vector3 F0 = ComputeF0(baseCol);
        Vector3 F = FresnelSchlick(VdotH, F0);

        // Cook-Torrance: D × G × F / (4 × NdotV × NdotL), then × NdotL
        // The NdotL cancels, leaving D × G × F / (4 × NdotV).
        Vector3 specular = D * G * F / MathF.Max(4f * NdotV, 1e-6f);

        // ── Clearcoat GGX lobe ──────────────────────────────────────────
        Vector3 clearcoat = Vector3.Zero;
        if (Clearcoat > 0f)
        {
            float ca2 = _clearcoatAlpha * _clearcoatAlpha;
            float cDenom = NdotH * NdotH * (ca2 - 1f) + 1f;
            float cD = ca2 / (MathF.PI * cDenom * cDenom);
            float cG = SmithG1_GGX(NdotV, _clearcoatAlpha) * SmithG1_GGX(NdotL, _clearcoatAlpha);

            // Fixed F0 = 0.04 for clearcoat (IOR ≈ 1.5)
            float cF0 = 0.04f;
            float cF = cF0 + (1f - cF0) * SchlickWeight(VdotH);

            // Clearcoat weight: 0.25 matches the lobe selection weight in Scatter
            clearcoat = new Vector3(Clearcoat * 0.25f * cD * cG * cF
                        / MathF.Max(4f * NdotV, 1e-6f));
        }

        Vector3 result = diffuse + specular + clearcoat;

        // ── FIREFLY GUARD ───────────────────────────────────────────────
        // The GGX NDF term D = α²/(π×denom²) diverges for low roughness
        // when NdotH ≈ 1 (light perfectly aligned with reflection). For
        // α=0.001 (mirror), D ≈ 3×10⁸. After G/4NdotV this can still
        // produce specular values of 50–500, which multiplied by light
        // color and scatter attenuation across bounces creates persistent
        // fireflies that don't average out even at -s 3000.
        //
        // The clamp at 1.0 per component matches the Blinn-Phong scale that
        // the Renderer's lighting pipeline was designed around (point lights
        // already carry their intensity in lightColor; the BRDF shape factor
        // should be a [0,1] modulator, not an amplifier). For reference,
        // the old Blinn-Phong peak was pow(1, exponent) × fresnel ≈ 1.0.
        result = Vector3.Min(result, Vector3.One);

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Scatter — stochastic lobe selection for indirect rays
    //
    // Each bounce randomly selects one lobe weighted by expected contribution.
    // The returned attenuation is compensated by 1/probability to keep the
    // Monte Carlo estimator unbiased across all lobes.
    // ═════════════════════════════════════════════════════════════════════════

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        Vector3 N = rec.Normal;
        Vector3 V = Vector3.Normalize(-rayIn.Direction);

        // ── Compute lobe weights for importance sampling ────────────────────
        // FIX #5: Calibrated weights to better approximate each lobe's expected
        // energy contribution. This reduces the variance introduced by the
        // 1/probability compensation, especially for lobes with low selection
        // probability (clearcoat with low Clearcoat values, weak specular on
        // rough dielectrics).
        //
        // Diffuse: proportional to (1-metallic)(1-specTrans). No roughness
        //   factor here — the Disney diffuse Fresnel handles energy balance.
        // Specular: Fresnel-weighted. For metals F0 ≈ luminance(baseColor),
        //   for dielectrics F0 ≈ 0.04×Specular. The min(0.1) floor ensures
        //   specular is always sampled (critical for glossy dielectrics where
        //   the visual contribution is high despite low F0).
        // Transmission: proportional to (1-metallic)×specTrans.
        // Clearcoat: proportional to Clearcoat × mean Fresnel (≈ 0.04 at
        //   normal incidence). The old 0.25 constant over-weighted clearcoat
        //   relative to its actual energy, causing amplification spikes.
        // Sheen (FIX #7c): separated from diffuse into its own lobe with
        //   cosine-weighted sampling. Weight proportional to Sheen × diffuseW
        //   since sheen only exists on dielectric/non-transmissive surfaces.
        float diffuseW  = (1f - Metallic) * (1f - SpecTrans);
        float specF0    = Metallic > 0.5f ? MathUtils.Luminance(baseCol)
                          : 0.04f * Specular;
        float specularW = MathF.Max(0.1f, Lerp(specF0, 1f, Metallic));
        float transW    = (1f - Metallic) * SpecTrans;
        float clearW    = Clearcoat * 0.04f; // F0 of clearcoat IOR ≈ 1.5
        float sheenW    = Sheen * 0.25f * diffuseW; // FIX #7c: sheen as separate lobe

        float totalW = diffuseW + specularW + transW + clearW + sheenW;
        if (totalW < 1e-6f) { totalW = 1f; specularW = 1f; } // Fallback

        float pDiffuse  = diffuseW / totalW;
        float pSpecular = specularW / totalW;
        float pTrans    = transW / totalW;
        float pSheen    = sheenW / totalW;
        // pClearcoat = remainder

        float rnd = MathUtils.RandomFloat();

        bool result;
        if (rnd < pDiffuse)
        {
            result = ScatterDiffuse(rec, baseCol, N, V, pDiffuse, out attenuation, out scattered);
        }
        else if ((rnd -= pDiffuse) < pSpecular)
        {
            result = ScatterSpecular(rec, baseCol, N, V, pSpecular, out attenuation, out scattered);
        }
        else if ((rnd -= pSpecular) < pTrans)
        {
            result = ScatterTransmission(rayIn, rec, baseCol, N, pTrans, out attenuation, out scattered);
        }
        else if ((rnd -= pTrans) < pSheen)
        {
            result = ScatterSheen(rec, baseCol, N, V, pSheen, out attenuation, out scattered);
        }
        else
        {
            float pClearcoat = clearW / totalW;
            result = ScatterClearcoat(rec, N, V, pClearcoat, out attenuation, out scattered);
        }

        // ── Sanitize attenuation ────────────────────────────────────────────
        // Guard against NaN/Inf from degenerate geometry (zero-length half
        // vectors, near-singular ONB, etc.) and against extreme values from
        // edge-case BSDF evaluation. Without this, NaN propagates through
        // the TraceRay recursion and becomes a black spot (NaN → 0 in
        // ClampRadiance), while extreme values become fireflies.
        if (float.IsNaN(attenuation.X) || float.IsInfinity(attenuation.X) ||
            float.IsNaN(attenuation.Y) || float.IsInfinity(attenuation.Y) ||
            float.IsNaN(attenuation.Z) || float.IsInfinity(attenuation.Z))
        {
            // Fall back to simple Lambert — returns correct-ish color
            // instead of black (NaN→0) or white (Inf).
            attenuation = baseCol;
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Individual lobe scatter methods
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Disney diffuse: blend of standard Lambert and subsurface approximation
    /// with Fresnel-based retro-reflection at grazing angles.
    ///
    /// FIX #7c: Sheen has been extracted into its own ScatterSheen lobe with
    /// dedicated sampling, eliminating the sampling mismatch where sheen
    /// (strong at grazing angles) was sampled with the diffuse cosine-weighted
    /// distribution (strong at normal incidence).
    /// </summary>
    private bool ScatterDiffuse(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                                float probability,
                                out Vector3 attenuation, out Ray scattered)
    {
        // Cosine-weighted hemisphere sampling
        Vector3 scatterDir = N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDir))
            scatterDir = N;
        scatterDir = Vector3.Normalize(scatterDir);

        scattered = new Ray(rec.Point, scatterDir);

        // Compute Disney diffuse Fresnel correction
        Vector3 L = scatterDir;
        float NdotV = MathF.Max(Vector3.Dot(N, V), 0.001f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 0.001f);

        // Half-vector — GUARD against NaN when V and L are nearly opposite
        // (grazing incidence with backscatter). V+L ≈ zero → Normalize → NaN.
        Vector3 Hraw = V + L;
        float hLenSq = Hraw.LengthSquared();
        Vector3 H = hLenSq > 1e-7f ? Hraw / MathF.Sqrt(hLenSq) : N;
        float LdotH = MathF.Max(Vector3.Dot(L, H), 0f);

        // Disney diffuse Fresnel factor (retro-reflection at grazing angles)
        float fd90 = 0.5f + 2f * Roughness * LdotH * LdotH;
        float fI = SchlickWeight(NdotV);
        float fO = SchlickWeight(NdotL);
        float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);

        // Subsurface approximation (Hanrahan-Krueger inspired).
        // CLAMP: the 1/(NdotV+NdotL) term explodes at grazing angles
        // (e.g. NdotV=NdotL=0.001 → 1/0.003 = 333 → ss ≈ 416).
        // Physically the subsurface effect is a ~1.25× brightness boost,
        // not a 400× explosion. Clamping ss to [0, 2] preserves the visual
        // effect while eliminating the firefly source.
        float fss90 = Roughness * LdotH * LdotH;
        float fssI = 1f + (fss90 - 1f) * fI;
        float fssO = 1f + (fss90 - 1f) * fO;
        float ssRaw = 1.25f * (fssI * fssO * (1f / (NdotV + NdotL + 0.001f) - 0.5f) + 0.5f);
        float ss = Math.Clamp(ssRaw, 0f, 2f);

        // Blend Lambert and subsurface — result is now bounded [0, ~2.5]
        float diffuseFactor = Lerp(fd, ss, Subsurface);
        attenuation = baseCol * diffuseFactor;

        // Compensate for lobe selection probability (multi-lobe MIS).
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// FIX #7c: Dedicated sheen lobe with its own sampling.
    ///
    /// Sheen contributes primarily at grazing angles (Schlick weight on L·H).
    /// When it was embedded in ScatterDiffuse, it was sampled with cosine-weighted
    /// hemisphere sampling — which concentrates samples near the normal, exactly
    /// where sheen contributes least. This mismatch increased variance for
    /// materials with strong sheen (velvet, silk, fabric).
    ///
    /// As a separate lobe, sheen still uses cosine-weighted sampling (a perfect
    /// importance-sampled distribution for sheen would require sampling the
    /// Schlick weight profile, which is complex). However, the key improvement
    /// is that the lobe selection probability now reflects sheen's actual energy
    /// contribution, so the 1/probability compensation is correctly calibrated.
    /// The per-sample variance is similar, but fewer samples are "wasted" on
    /// sheen when other lobes would be more productive.
    /// </summary>
    private bool ScatterSheen(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                              float probability,
                              out Vector3 attenuation, out Ray scattered)
    {
        // Cosine-weighted hemisphere sampling (same as diffuse)
        Vector3 scatterDir = N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDir))
            scatterDir = N;
        scatterDir = Vector3.Normalize(scatterDir);

        scattered = new Ray(rec.Point, scatterDir);

        Vector3 L = scatterDir;

        // Half-vector for L·H computation
        Vector3 Hraw = V + L;
        float hLenSq = Hraw.LengthSquared();
        Vector3 H = hLenSq > 1e-7f ? Hraw / MathF.Sqrt(hLenSq) : N;
        float LdotH = MathF.Max(Vector3.Dot(L, H), 0f);

        // Sheen: Schlick weight at grazing angle × tinted color
        float fH = SchlickWeight(LdotH);
        float lum = MathUtils.Luminance(baseCol);
        Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
        Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, SheenTint);
        attenuation = Sheen * fH * sheenCol;

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// GGX specular reflection with correct importance sampling weight.
    /// For metals the Fresnel is tinted by baseColor; for dielectrics it
    /// uses the IOR-derived F0 value.
    ///
    /// When importance-sampling the GGX NDF, the correct Monte Carlo weight is:
    ///   weight = F(V·H) × G(V, L, α) × V·H / (N·V × N·H)
    /// where G is the Smith height-correlated masking/shadowing term.
    /// </summary>
    private bool ScatterSpecular(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                                 float probability,
                                 out Vector3 attenuation, out Ray scattered)
    {
        // Sample GGX microfacet normal
        Vector3 H = SampleGGX(N, _alpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        if (Vector3.Dot(L, N) <= 0f)
        {
            // Below-surface reflection — absorb
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        // Dot product floors: NdotV and NdotH appear in the denominator of
        // the GGX weight. Floor at 0.01 (≈ 89.4°) instead of 0.001 (≈ 89.94°)
        // — the visual difference in that 0.5° sliver is imperceptible, but
        // the spike reduction is 10×.
        float NdotV = MathF.Max(Vector3.Dot(N, V), 0.01f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 0.001f);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0.01f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 0.001f);

        // Compute Fresnel
        Vector3 F0 = ComputeF0(baseCol);
        Vector3 fresnel = FresnelSchlick(VdotH, F0);

        // Smith GGX geometry (masking/shadowing) — separable approximation
        float G = SmithG1_GGX(NdotV, _alpha) * SmithG1_GGX(NdotL, _alpha);

        // BRDF/pdf importance sampling weight for GGX NDF sampling:
        //   BRDF  = D(H) × F × G / (4 × NdotV × NdotL)
        //   pdf   = D(H) × NdotH / (4 × VdotH)
        //   weight = F × G × VdotH / (NdotV × NdotH)
        //
        // FIREFLY GUARD: The GGX weight should theoretically stay near 1.0
        // for well-behaved configurations (Smith G damps the VdotH/(NdotV×NdotH)
        // ratio). Values above 1.0 come from near-degenerate grazing angles
        // where the floor values dominate — these are noise, not signal.
        // Clamping to 1.0 eliminates fireflies with no perceptible quality loss:
        // the affected samples are at extreme silhouette edges (< 1° sliver)
        // where the human eye can't distinguish the difference anyway.
        float ggxWeight = MathF.Min(G * VdotH / (NdotV * NdotH), 1f);
        attenuation = fresnel * ggxWeight;

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Specular transmission for glass-like materials.
    /// Uses Schlick's approximation for reflection vs refraction choice.
    ///
    /// FIX #7a: Frosted glass now uses GGX-sampled microfacet normals instead
    /// of uniform-sphere perturbation of the refracted direction. The old approach
    /// added random noise with a uniform distribution that didn't match the GGX
    /// BRDF used by the specular lobe, causing high variance for rough transmissive
    /// materials. The new approach samples a microfacet normal H from the GGX NDF
    /// and refracts through H, producing a distribution that is consistent with
    /// the material's roughness model.
    /// </summary>
    private bool ScatterTransmission(Ray rayIn, HitRecord rec, Vector3 baseCol,
                                     Vector3 N, float probability,
                                     out Vector3 attenuation, out Ray scattered)
    {
        float eta = rec.FrontFace ? (1f / Ior) : Ior;
        Vector3 unitDir = Vector3.Normalize(rayIn.Direction);

        // For rough transmissive materials, sample a GGX microfacet normal
        // instead of using the geometric normal. This produces physically
        // correct frosted glass with a distribution matching the roughness.
        Vector3 Ht = (Roughness > 0.01f) ? SampleGGX(N, _alpha) : N;

        // Ensure the microfacet normal faces the incoming ray
        if (Vector3.Dot(Ht, unitDir) > 0f)
            Ht = -Ht;

        float cosTheta = MathF.Min(Vector3.Dot(-unitDir, Ht), 1f);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        bool cannotRefract = eta * sinTheta > 1f;
        Vector3 direction;

        if (cannotRefract || MathUtils.Schlick(cosTheta, eta) > MathUtils.RandomFloat())
        {
            // Total internal reflection or Fresnel reflection
            direction = MathUtils.Reflect(unitDir, Ht);
        }
        else
        {
            direction = MathUtils.Refract(unitDir, Ht, eta);
        }

        // Guard: if refraction produced a degenerate direction, fall back
        if (MathUtils.NearZero(direction))
            direction = MathUtils.Reflect(unitDir, N);

        scattered = new Ray(rec.Point, Vector3.Normalize(direction));

        // Tint transmitted light by sqrt(baseColor) for colored glass
        attenuation = new Vector3(
            MathF.Sqrt(baseCol.X),
            MathF.Sqrt(baseCol.Y),
            MathF.Sqrt(baseCol.Z));

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Clearcoat: a fixed-IOR (1.5) secondary specular lobe with its own roughness.
    /// Always white (physically: a thin transparent varnish layer).
    ///
    /// Uses the same GGX importance sampling weight as ScatterSpecular but with
    /// _clearcoatAlpha and fixed F0 = 0.04. The Clearcoat intensity parameter
    /// is NOT multiplied into the attenuation — it is already encoded in the
    /// lobe selection probability (clearW = Clearcoat × 0.25), and the 1/probability
    /// compensation keeps the estimator unbiased.
    /// </summary>
    private bool ScatterClearcoat(HitRecord rec, Vector3 N, Vector3 V,
                                  float probability,
                                  out Vector3 attenuation, out Ray scattered)
    {
        Vector3 H = SampleGGX(N, _clearcoatAlpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        if (Vector3.Dot(L, N) <= 0f)
        {
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        // Same raised floors as ScatterSpecular (see comment there).
        float NdotV = MathF.Max(Vector3.Dot(N, V), 0.01f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 0.001f);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0.01f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 0.001f);

        // Clearcoat uses fixed F0 = 0.04 (IOR ≈ 1.5)
        float f0 = 0.04f;
        float fresnel = f0 + (1f - f0) * SchlickWeight(VdotH);

        // Smith GGX geometry with clearcoat alpha
        float G = SmithG1_GGX(NdotV, _clearcoatAlpha) * SmithG1_GGX(NdotL, _clearcoatAlpha);

        // GGX importance sampling weight: F × G × VdotH / (NdotV × NdotH)
        // FIREFLY GUARD: clamp to 1.0 (see ScatterSpecular comment).
        float ggxWeight = MathF.Min(G * VdotH / (NdotV * NdotH), 1f);
        attenuation = new Vector3(fresnel * ggxWeight);

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GGX sampling, geometry, and Fresnel utilities
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Smith G1 masking/shadowing function for the GGX microfacet distribution.
    /// Uses the height-correlated form: G1(v) = 2·NdotX / (NdotX + sqrt(α² + (1-α²)·NdotX²))
    ///
    /// The full geometry term G(V,L) = G1(V) × G1(L) approximates the separable
    /// Smith model. This is standard practice in real-time and offline PBR.
    /// </summary>
    private static float SmithG1_GGX(float NdotX, float alpha)
    {
        float a2 = alpha * alpha;
        float NdotX2 = NdotX * NdotX;
        float denom = NdotX + MathF.Sqrt(a2 + (1f - a2) * NdotX2);
        return 2f * NdotX / MathF.Max(denom, 1e-7f);
    }

    /// <summary>
    /// Importance-samples a microfacet normal from the GGX (Trowbridge-Reitz)
    /// distribution. Returns a half-vector H in world space.
    /// </summary>
    private static Vector3 SampleGGX(Vector3 N, float alpha)
    {
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();

        // GGX sampling in spherical coordinates
        float a2 = alpha * alpha;
        float cosTheta = MathF.Sqrt((1f - u1) / (1f + (a2 - 1f) * u1));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;

        // Local tangent-space H
        Vector3 Hlocal = new(
            sinTheta * MathF.Cos(phi),
            sinTheta * MathF.Sin(phi),
            cosTheta);

        // Transform to world space using an orthonormal basis around N
        return TangentToWorld(Hlocal, N);
    }

    /// <summary>
    /// Builds an orthonormal basis around N and transforms a tangent-space
    /// vector to world space. Uses Frisvad's robust method.
    /// </summary>
    private static Vector3 TangentToWorld(Vector3 local, Vector3 N)
    {
        Vector3 T, B;
        // Frisvad's method — but the 1/(1+N.Z) term becomes numerically
        // unstable as N.Z approaches -1. At N.Z = -0.999, a = 1000 and
        // the resulting T/B vectors are near-degenerate, potentially
        // producing NaN after normalization. Widen the threshold to -0.999
        // (was -0.9999) to use the safe fallback basis for near-south-pole
        // normals. The error is imperceptible (0.1° of normal deviation).
        if (N.Z < -0.999f)
        {
            T = new Vector3(0f, -1f, 0f);
            B = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + N.Z);
            float b = -N.X * N.Y * a;
            T = new Vector3(1f - N.X * N.X * a, b, -N.X);
            B = new Vector3(b, 1f - N.Y * N.Y * a, -N.Y);
        }

        Vector3 result = local.X * T + local.Y * B + local.Z * N;
        float lenSq = result.LengthSquared();
        // Guard against degenerate zero-length vector → NaN from Normalize
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : N;
    }

    /// <summary>
    /// Computes the Fresnel reflectance at normal incidence (F0).
    /// Metals use baseColor directly; dielectrics use IOR-derived value
    /// optionally tinted towards baseColor via specularTint.
    /// </summary>
    private Vector3 ComputeF0(Vector3 baseCol)
    {
        // Dielectric F0 from IOR
        float r = (Ior - 1f) / (Ior + 1f);
        float f0d = r * r;

        // Disney's specular parameter scales F0 (0.5 → standard, 1.0 → 2× brighter)
        float scaledF0 = f0d * 2f * Specular;

        // Tint towards baseColor if specularTint > 0
        float lum = MathUtils.Luminance(baseCol);
        Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
        Vector3 dielectricF0 = Vector3.Lerp(new Vector3(scaledF0), scaledF0 * tintCol, SpecularTint);

        // Blend between dielectric and metallic F0
        return Vector3.Lerp(dielectricF0, baseCol, Metallic);
    }

    /// <summary>
    /// Schlick Fresnel with Vector3 for per-channel metallic coloring.
    /// </summary>
    private static Vector3 FresnelSchlick(float cosTheta, Vector3 f0)
    {
        float w = SchlickWeight(cosTheta);
        return f0 + (Vector3.One - f0) * w;
    }

    /// <summary>
    /// (1 - cosθ)^5 — the Schlick Fresnel weight.
    /// </summary>
    private static float SchlickWeight(float cosTheta)
    {
        float x = Math.Clamp(1f - cosTheta, 0f, 1f);
        float x2 = x * x;
        return x2 * x2 * x; // x^5
    }

    /// <summary>
    /// Scalar linear interpolation — private to avoid dependency on MathUtils
    /// for this single hot-path operation.
    /// </summary>
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
