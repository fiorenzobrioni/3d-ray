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
    // All scalar Disney parameters are stored as FloatTexture, enabling
    // per-shading-point variation from a texture lookup. Pass a plain
    // float to any constructor argument — the implicit conversion produces
    // a constant FloatTexture transparently.
    public ITexture BaseColor          { get; }
    public FloatTexture Metallic       { get; }
    public FloatTexture Roughness      { get; }
    public FloatTexture Subsurface     { get; }
    public FloatTexture Specular       { get; }
    public FloatTexture SpecularTint   { get; }
    public FloatTexture Sheen          { get; }
    public FloatTexture SheenTint      { get; }
    public FloatTexture Clearcoat      { get; }
    public FloatTexture ClearcoatGloss { get; }
    public FloatTexture SpecTrans      { get; }
    public FloatTexture Ior            { get; }

    // ── Normal map support ──────────────────────────────────────────────────
    public NormalMapTexture? NormalMap { get; set; }

    // ── Cached representative values (for material-wide queries) ───────────
    // Rendered once at construction by sampling the texture at (0,0,0).
    // Used by the Renderer's needsLightSampling gate and the legacy-emission
    // suppression flag — neither path needs per-point accuracy, and both
    // need a constant answer across a material.
    private readonly float _repDiffuseWeight;
    private readonly float _repAlpha;

    public DisneyBsdf(
        ITexture baseColor,
        FloatTexture? metallic       = null,
        FloatTexture? roughness      = null,
        FloatTexture? subsurface     = null,
        FloatTexture? specular       = null,
        FloatTexture? specularTint   = null,
        FloatTexture? sheen          = null,
        FloatTexture? sheenTint      = null,
        FloatTexture? clearcoat      = null,
        FloatTexture? clearcoatGloss = null,
        FloatTexture? specTrans      = null,
        FloatTexture? ior            = null)
    {
        BaseColor      = baseColor;
        Metallic       = metallic       ?? new FloatTexture(0f);
        Roughness      = roughness      ?? new FloatTexture(0.5f);
        Subsurface     = subsurface     ?? new FloatTexture(0f);
        Specular       = specular       ?? new FloatTexture(0.5f);
        SpecularTint   = specularTint   ?? new FloatTexture(0f);
        Sheen          = sheen          ?? new FloatTexture(0f);
        SheenTint      = sheenTint      ?? new FloatTexture(0.5f);
        Clearcoat      = clearcoat      ?? new FloatTexture(0f);
        ClearcoatGloss = clearcoatGloss ?? new FloatTexture(1f);
        SpecTrans      = specTrans      ?? new FloatTexture(0f);
        Ior            = ior            ?? new FloatTexture(1.5f);

        // Representative values — evaluated once, never per-shading-point.
        float repMetal     = Math.Clamp(Metallic.RepresentativeValue,  0f, 1f);
        float repRoughness = Math.Clamp(Roughness.RepresentativeValue, 0f, 1f);
        float repTrans     = Math.Clamp(SpecTrans.RepresentativeValue, 0f, 1f);
        _repDiffuseWeight  = (1f - repMetal) * (1f - repTrans);
        _repAlpha          = MathF.Max(repRoughness * repRoughness, 0.001f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Per-shading-point parameter snapshot.
    //
    // Textures vary with (u, v, p) — so all 11 scalar parameters are sampled
    // once at the top of every public entry (Scatter, Evaluate, Pdf,
    // EvaluateDirect) and passed by ref-readonly through the private lobe
    // helpers. This keeps the number of texture fetches per BSDF call small
    // and predictable, and centralises the Math.Clamp / _alpha derivation.
    // ═════════════════════════════════════════════════════════════════════════
    private readonly struct ShadingParams
    {
        public readonly float Metallic;
        public readonly float Roughness;
        public readonly float Subsurface;
        public readonly float Specular;
        public readonly float SpecularTint;
        public readonly float Sheen;
        public readonly float SheenTint;
        public readonly float Clearcoat;
        public readonly float ClearcoatGloss;
        public readonly float SpecTrans;
        public readonly float Ior;
        public readonly float Alpha;           // roughness² (GGX α)
        public readonly float ClearcoatAlpha;  // Burley 2012 §5.4

        public ShadingParams(
            float metallic, float roughness, float subsurface,
            float specular, float specularTint,
            float sheen, float sheenTint,
            float clearcoat, float clearcoatGloss,
            float specTrans, float ior)
        {
            Metallic       = Math.Clamp(metallic, 0f, 1f);
            Roughness      = Math.Clamp(roughness, 0f, 1f);
            Subsurface     = Math.Clamp(subsurface, 0f, 1f);
            Specular       = Math.Clamp(specular, 0f, 2f);
            SpecularTint   = Math.Clamp(specularTint, 0f, 1f);
            Sheen          = Math.Clamp(sheen, 0f, 1f);
            SheenTint      = Math.Clamp(sheenTint, 0f, 1f);
            Clearcoat      = Math.Clamp(clearcoat, 0f, 1f);
            ClearcoatGloss = Math.Clamp(clearcoatGloss, 0f, 1f);
            SpecTrans      = Math.Clamp(specTrans, 0f, 1f);
            Ior            = MathF.Max(ior, 1.0001f);

            Alpha          = MathF.Max(Roughness * Roughness, 0.001f);
            ClearcoatAlpha = Lerp(0.1f, 0.001f, ClearcoatGloss);
        }
    }

    private ShadingParams EvalParams(HitRecord rec)
    {
        float u = rec.U, v = rec.V;
        Vector3 p = rec.LocalPoint;
        int seed = rec.ObjectSeed;
        return new ShadingParams(
            Metallic.Value(u, v, p, seed),
            Roughness.Value(u, v, p, seed),
            Subsurface.Value(u, v, p, seed),
            Specular.Value(u, v, p, seed),
            SpecularTint.Value(u, v, p, seed),
            Sheen.Value(u, v, p, seed),
            SheenTint.Value(u, v, p, seed),
            Clearcoat.Value(u, v, p, seed),
            ClearcoatGloss.Value(u, v, p, seed),
            SpecTrans.Value(u, v, p, seed),
            Ior.Value(u, v, p, seed));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Direct lighting interface
    //
    // Maps Disney parameters to the renderer's DiffuseWeight / SpecularExponent
    // / SpecularStrength interface used by ComputeDirectLighting().
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Material-wide representative DiffuseWeight used by the renderer's
    /// needsLightSampling gate and legacy emission suppression. Computed
    /// once from the parameters' representative values; texture variation
    /// is ignored here since both downstream consumers only need a
    /// yes/no answer ("does this material have a diffuse lobe at all?").
    /// </summary>
    public float DiffuseWeight => _repDiffuseWeight;

    /// <summary>
    /// Representative Blinn-Phong exponent derived from the material-wide
    /// roughness. Retained for IMaterial compatibility; used only by the
    /// renderer's needsLightSampling gate, which is a coarse boolean and
    /// can tolerate a constant-per-material answer.
    /// </summary>
    public float SpecularExponent
    {
        get
        {
            if (_repAlpha >= 1f) return 2f;
            return MathF.Min(2f / (_repAlpha * _repAlpha), 2048f);
        }
    }

    /// <summary>
    /// Representative specular highlight strength. Used only for the
    /// renderer's needsLightSampling gate (IMaterial compatibility).
    /// </summary>
    public float SpecularStrength
    {
        get
        {
            float rMetal     = Metallic.RepresentativeValue;
            float rSpec      = Specular.RepresentativeValue;
            float rClearcoat = Clearcoat.RepresentativeValue;
            float rRoughness = Roughness.RepresentativeValue;
            float baseSpec = rMetal > 0.5f ? 1f : rSpec;
            float ccBoost = rClearcoat * 0.25f;
            return MathF.Min(baseSpec * (1f - rRoughness * 0.5f) + ccBoost, 1f);
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
    /// BaseColor is sampled at the hit point's UV coordinates from the HitRecord,
    /// giving correct Fresnel F0 for textured Disney materials (e.g. a metallic
    /// texture map with varying colors).
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, HitRecord rec)
    {
        float NdotL = MathF.Max(Vector3.Dot(normal, toLight), 0f);
        if (NdotL <= 0f) return Vector3.Zero;

        float NdotV = MathF.Max(Vector3.Dot(normal, toEye), 0.001f);

        Vector3 H = Vector3.Normalize(toLight + toEye);
        float NdotH = MathF.Max(Vector3.Dot(normal, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(toEye, H), 0f);

        ShadingParams sp = EvalParams(rec);

        // ── Disney diffuse lobe ─────────────────────────────────────────
        float diffuseW = (1f - sp.Metallic) * (1f - sp.SpecTrans);
        Vector3 diffuse = Vector3.Zero;
        if (diffuseW > 0f)
        {
            float fd90 = 0.5f + 2f * sp.Roughness * VdotH * VdotH;
            float fI = SchlickWeight(NdotV);
            float fO = SchlickWeight(NdotL);
            float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);
            // Divide by π for energy conservation (cosine-weighted hemisphere)
            diffuse = new Vector3(diffuseW * fd * NdotL / MathF.PI);
        }

        // ── GGX specular lobe ───────────────────────────────────────────
        // D: GGX (Trowbridge-Reitz) NDF
        float a2 = sp.Alpha * sp.Alpha;
        float denom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * denom * denom);

        // G: Smith separable masking/shadowing
        float G = SmithG1_GGX(NdotV, sp.Alpha) * SmithG1_GGX(NdotL, sp.Alpha);

        // F: Schlick Fresnel with continuous metallic→dielectric blend
        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        Vector3 F0 = ComputeF0(baseCol, sp);
        Vector3 F = FresnelSchlick(VdotH, F0);

        // Cook-Torrance: D × G × F / (4 × NdotV × NdotL), then × NdotL
        Vector3 specular = D * G * F / MathF.Max(4f * NdotV, 1e-6f);

        // ── Clearcoat GGX lobe ──────────────────────────────────────────
        Vector3 clearcoat = Vector3.Zero;
        if (sp.Clearcoat > 0f)
        {
            float ca2 = sp.ClearcoatAlpha * sp.ClearcoatAlpha;
            float cDenom = NdotH * NdotH * (ca2 - 1f) + 1f;
            float cD = ca2 / (MathF.PI * cDenom * cDenom);
            float cG = SmithG1_GGX(NdotV, sp.ClearcoatAlpha) * SmithG1_GGX(NdotL, sp.ClearcoatAlpha);

            float cF0 = 0.04f;
            float cF = cF0 + (1f - cF0) * SchlickWeight(VdotH);

            clearcoat = new Vector3(sp.Clearcoat * 0.25f * cD * cG * cF
                        / MathF.Max(4f * NdotV, 1e-6f));
        }

        // The global firefly clamp lives in the renderer (`--clamp`/`-C`) and
        // is the right place to cap the per-sample radiance if a scene still
        // produces outliers. The old 10.0 bias on the BRDF itself is gone
        // now that the indirect lobes use VNDF sampling and no longer
        // systematically overshoot the direct-light contribution.
        return diffuse + specular + clearcoat;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Symmetric BSDF interface: Evaluate, Pdf, Sample
    //
    // These methods expose the multi-lobe BRDF value and its solid-angle PDF
    // at an arbitrary (V, L) direction pair, without the cosine term. They
    // consume the same lobe weights used by Scatter so Monte Carlo estimators
    // built on top of them (MIS, furnace tests, reciprocity tests) stay
    // consistent with the indirect-lighting path.
    //
    // Transmission is currently excluded from Evaluate/Pdf (returns zero for
    // L below the surface). The transmission lobe lives on a different
    // hemisphere and requires its own half-vector construction and dispatch —
    // that work belongs to the VNDF step and will land together with the
    // generalised glass BSDF.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluates the multi-lobe Disney BRDF f(V, L) at the hit point, without
    /// the N·L cosine. Reflection lobes only — returns zero for L below the
    /// surface.
    /// </summary>
    public Vector3 Evaluate(Vector3 V, Vector3 L, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotL = Vector3.Dot(N, L);
        float NdotV = Vector3.Dot(N, V);
        if (NdotL <= 0f || NdotV <= 0f) return Vector3.Zero;

        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() < 1e-14f) return Vector3.Zero;
        Vector3 H = Vector3.Normalize(Hraw);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 0f);
        float LdotH = MathF.Max(Vector3.Dot(L, H), 0f);

        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        ShadingParams sp = EvalParams(rec);

        // ── Disney diffuse (retro-reflection Fresnel) ──────────────────────
        float diffuseScalar = (1f - sp.Metallic) * (1f - sp.SpecTrans);
        Vector3 diffuse = Vector3.Zero;
        if (diffuseScalar > 0f)
        {
            float fd90 = 0.5f + 2f * sp.Roughness * LdotH * LdotH;
            float fI = SchlickWeight(NdotV);
            float fO = SchlickWeight(NdotL);
            float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);
            diffuse = baseCol * (diffuseScalar * fd / MathF.PI);
        }

        // ── Sheen (separate lobe since FIX #7c) ────────────────────────────
        Vector3 sheen = Vector3.Zero;
        if (sp.Sheen > 0f && diffuseScalar > 0f)
        {
            float lum = MathUtils.Luminance(baseCol);
            Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
            Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, sp.SheenTint);
            sheen = sp.Sheen * SchlickWeight(LdotH) * sheenCol;
        }

        // ── GGX specular (full Cook-Torrance) ──────────────────────────────
        float a2 = sp.Alpha * sp.Alpha;
        float denom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * denom * denom);
        float G = SmithG1_GGX(NdotV, sp.Alpha) * SmithG1_GGX(NdotL, sp.Alpha);
        Vector3 F0 = ComputeF0(baseCol, sp);
        Vector3 F = FresnelSchlick(VdotH, F0);
        Vector3 specular = D * G / MathF.Max(4f * NdotV * NdotL, 1e-7f) * F;

        // ── Clearcoat GGX ──────────────────────────────────────────────────
        Vector3 clearcoat = Vector3.Zero;
        if (sp.Clearcoat > 0f)
        {
            float ca2 = sp.ClearcoatAlpha * sp.ClearcoatAlpha;
            float cDenom = NdotH * NdotH * (ca2 - 1f) + 1f;
            float cD = ca2 / (MathF.PI * cDenom * cDenom);
            float cG = SmithG1_GGX(NdotV, sp.ClearcoatAlpha) * SmithG1_GGX(NdotL, sp.ClearcoatAlpha);
            float cF0 = 0.04f;
            float cF = cF0 + (1f - cF0) * SchlickWeight(VdotH);
            float cc = sp.Clearcoat * 0.25f * cD * cG * cF
                     / MathF.Max(4f * NdotV * NdotL, 1e-7f);
            clearcoat = new Vector3(cc);
        }

        return diffuse + sheen + specular + clearcoat;
    }

    /// <summary>
    /// Solid-angle PDF of sampling L from this BSDF's importance distribution,
    /// given the view direction V. Reflection lobes only (mirrors Evaluate).
    ///
    /// Combined PDF = Σ_lobe p_lobe · pdf_lobe(L), matching the one-sample
    /// mixture estimator used in Scatter. Transmission is excluded — returning
    /// zero for below-surface L keeps MIS unbiased for non-transmissive
    /// materials; transmissive materials fall back to the Scatter-only path
    /// until the VNDF + glass work lands.
    /// </summary>
    public float Pdf(Vector3 V, Vector3 L, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotL = Vector3.Dot(N, L);
        float NdotV = Vector3.Dot(N, V);
        if (NdotL <= 0f || NdotV <= 0f) return 0f;

        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() < 1e-14f) return 0f;
        Vector3 H = Vector3.Normalize(Hraw);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 1e-7f);

        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        ShadingParams sp = EvalParams(rec);
        LobeWeights w = ComputeLobeWeights(baseCol, sp);

        // Cosine-weighted PDF shared by diffuse and sheen.
        float cosPdf = NdotL / MathF.PI;

        // VNDF PDF in L-space (Heitz 2018):
        //   Dv(H) = G1(V) · max(V·H, 0) · D(H) / NdotV
        // Reflection Jacobian dωH/dωL = 1/(4·|V·H|) → pdf_L cancels the
        // (V·H) factor and yields G1(V) · D / (4 · NdotV).
        float safeNdotV = MathF.Max(NdotV, 1e-7f);

        float a2 = sp.Alpha * sp.Alpha;
        float dDenom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * dDenom * dDenom);
        float g1V = SmithG1_GGX(NdotV, sp.Alpha);
        float specPdf = g1V * D / (4f * safeNdotV);

        float ca2 = sp.ClearcoatAlpha * sp.ClearcoatAlpha;
        float cDenom = NdotH * NdotH * (ca2 - 1f) + 1f;
        float cD = ca2 / (MathF.PI * cDenom * cDenom);
        float cG1V = SmithG1_GGX(NdotV, sp.ClearcoatAlpha);
        float ccPdf = cG1V * cD / (4f * safeNdotV);

        return w.PDiffuse * cosPdf
             + w.PSheen * cosPdf
             + w.PSpecular * specPdf
             + w.PClearcoat * ccPdf;
    }

    /// <summary>
    /// Samples an outgoing direction from the multi-lobe Disney BSDF. Wraps
    /// <see cref="Scatter"/> internally and re-evaluates F and Pdf via the
    /// symmetric methods so the returned sample is consumable by MIS.
    ///
    /// Returns null when Scatter fails (below-surface reflection etc.) or
    /// when the sampled direction is in the transmission hemisphere (which
    /// is not yet covered by Evaluate/Pdf — use Scatter directly for glass).
    /// </summary>
    public BsdfSample? Sample(Vector3 V, HitRecord rec)
    {
        // Synthesize an incoming ray with direction -V. Origin is unused by
        // Scatter beyond rec.Point, so any origin works.
        Ray incoming = new(rec.Point, -V);
        if (!Scatter(incoming, rec, out Vector3 scatterAttn, out Ray scattered))
            return null;

        Vector3 wo = scattered.Direction;
        float NdotWo = Vector3.Dot(rec.Normal, wo);
        // Transmission lobe → treat as a delta sample. F carries Scatter's
        // attenuation directly (it already contains the Fresnel + sqrt(baseColor)
        // tint); delta samples in BsdfSample are interpreted by the renderer as
        // "attenuation = F" with no cos / pdf factor.
        if (NdotWo <= 0f)
            return new BsdfSample(wo, scatterAttn, 1f, isDelta: true);

        Vector3 f = Evaluate(V, wo, rec);
        float pdf = Pdf(V, wo, rec);
        return new BsdfSample(wo, f, pdf, isDelta: false);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Lobe selection probabilities (shared between Scatter and Pdf)
    //
    // Historical calibration (see FIX #5, FIX #7c, FIX #8e):
    //   - Diffuse ∝ (1-metallic)(1-specTrans). No roughness factor — the
    //     Disney diffuse Fresnel handles the energy balance.
    //   - Specular Fresnel-weighted: F0 ≈ luminance(baseColor) for metals,
    //     0.04·Specular for dielectrics. An adaptive floor ensures specular
    //     is always sampled for opaque glossy dielectrics but is relaxed for
    //     glass (where the transmission lobe already handles Fresnel
    //     reflection internally, avoiding the redundant-sampling halo).
    //   - Transmission ∝ (1-metallic)·specTrans.
    //   - Clearcoat ∝ Clearcoat × mean Fresnel (≈ 0.04).
    //   - Sheen as a separate lobe ∝ Sheen × diffuseW (post FIX #7c).
    //
    // Returns the absolute weights plus their sum. Probabilities are computed
    // as weight/total by callers (avoids recomputing the total in tight loops).
    // ═════════════════════════════════════════════════════════════════════════
    private readonly struct LobeWeights
    {
        public readonly float Diffuse;
        public readonly float Specular;
        public readonly float Transmission;
        public readonly float Clearcoat;
        public readonly float Sheen;
        public readonly float Total;

        public LobeWeights(float d, float s, float t, float c, float sh)
        {
            Diffuse = d; Specular = s; Transmission = t; Clearcoat = c; Sheen = sh;
            float sum = d + s + t + c + sh;
            Total = sum < 1e-6f ? 1f : sum;
        }

        public float PDiffuse      => Diffuse / Total;
        public float PSpecular     => Specular / Total;
        public float PTransmission => Transmission / Total;
        public float PClearcoat    => Clearcoat / Total;
        public float PSheen        => Sheen / Total;
    }

    private static LobeWeights ComputeLobeWeights(Vector3 baseCol, in ShadingParams sp)
    {
        float diffuseW  = (1f - sp.Metallic) * (1f - sp.SpecTrans);
        float specF0    = sp.Metallic > 0.5f ? MathUtils.Luminance(baseCol)
                          : 0.04f * sp.Specular;
        float specFloor = 0.1f * (1f - sp.SpecTrans * 0.9f); // FIX #8e
        float specularW = MathF.Max(specFloor, Lerp(specF0, 1f, sp.Metallic));
        float transW    = (1f - sp.Metallic) * sp.SpecTrans;
        float clearW    = sp.Clearcoat * 0.04f;
        float sheenW    = sp.Sheen * 0.25f * diffuseW;
        return new LobeWeights(diffuseW, specularW, transW, clearW, sheenW);
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
        ShadingParams sp = EvalParams(rec);
        Vector3 N = rec.Normal;
        Vector3 V = Vector3.Normalize(-rayIn.Direction);

        LobeWeights w = ComputeLobeWeights(baseCol, sp);
        float pDiffuse  = w.PDiffuse;
        float pSpecular = w.PSpecular;
        float pTrans    = w.PTransmission;
        float pSheen    = w.PSheen;
        // pClearcoat = remainder

        float rnd = MathUtils.RandomFloat();

        bool result;
        if (rnd < pDiffuse)
        {
            result = ScatterDiffuse(rec, baseCol, N, V, sp, pDiffuse, out attenuation, out scattered);
        }
        else if ((rnd -= pDiffuse) < pSpecular)
        {
            result = ScatterSpecular(rec, baseCol, N, V, sp, pSpecular, out attenuation, out scattered);
        }
        else if ((rnd -= pSpecular) < pTrans)
        {
            result = ScatterTransmission(rayIn, rec, baseCol, N, V, sp, pTrans, out attenuation, out scattered);
        }
        else if ((rnd -= pTrans) < pSheen)
        {
            result = ScatterSheen(rec, baseCol, N, V, sp, pSheen, out attenuation, out scattered);
        }
        else
        {
            float pClearcoat = w.PClearcoat;
            result = ScatterClearcoat(rec, N, V, sp, pClearcoat, out attenuation, out scattered);
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
                                in ShadingParams sp, float probability,
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
        float fd90 = 0.5f + 2f * sp.Roughness * LdotH * LdotH;
        float fI = SchlickWeight(NdotV);
        float fO = SchlickWeight(NdotL);
        float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);

        // Subsurface approximation (Hanrahan-Krueger inspired).
        // CLAMP: the 1/(NdotV+NdotL) term explodes at grazing angles
        // (e.g. NdotV=NdotL=0.001 → 1/0.003 = 333 → ss ≈ 416).
        // Physically the subsurface effect is a ~1.25× brightness boost,
        // not a 400× explosion. Clamping ss to [0, 2] preserves the visual
        // effect while eliminating the firefly source.
        float fss90 = sp.Roughness * LdotH * LdotH;
        float fssI = 1f + (fss90 - 1f) * fI;
        float fssO = 1f + (fss90 - 1f) * fO;
        float ssRaw = 1.25f * (fssI * fssO * (1f / (NdotV + NdotL + 0.001f) - 0.5f) + 0.5f);
        float ss = Math.Clamp(ssRaw, 0f, 2f);

        // Blend Lambert and subsurface — result is now bounded [0, ~2.5]
        float diffuseFactor = Lerp(fd, ss, sp.Subsurface);
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
    ///
    /// FIX #8d: Added Fresnel-aware weighting. The sheen energy is concentrated
    /// at grazing angles where SchlickWeight ≈ 1, but cosine sampling puts most
    /// samples near normal incidence where SchlickWeight ≈ 0. We clamp the raw
    /// sheen contribution to avoid the pathological case where a near-normal
    /// sample with fH ≈ 0 gets divided by a small probability, producing a
    /// low-valued but still noisy result.
    /// </summary>
    private bool ScatterSheen(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                              in ShadingParams sp, float probability,
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
        Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, sp.SheenTint);
        attenuation = sp.Sheen * fH * sheenCol;

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
                                 in ShadingParams sp, float probability,
                                 out Vector3 attenuation, out Ray scattered)
    {
        // Sample a visible microfacet normal (Heitz 2018)
        Vector3 H = SampleGgxVndf(N, V, sp.Alpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        if (Vector3.Dot(L, N) <= 0f)
        {
            // Below-surface reflection — absorb. VNDF reduces the
            // frequency of this case dramatically versus NDF sampling.
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        float NdotL = MathF.Max(Vector3.Dot(N, L), 1e-4f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 1e-4f);

        Vector3 F0 = ComputeF0(baseCol, sp);
        Vector3 fresnel = FresnelSchlick(VdotH, F0);

        // VNDF importance-sampling weight.
        //   BRDF            = F · D · G / (4 · NdotV · NdotL)
        //   VNDF pdf_L      = G1(V) · D / (4 · NdotV)
        //   BRDF·cos / pdf  = F · G / G1(V) = F · G1(L)   (separable Smith)
        //
        // G1(L) is inherently in [0, 1], so the old "clamp ggxWeight to 1"
        // firefly guard from Walter-2007 NDF sampling is no longer needed.
        float g1L = SmithG1_GGX(NdotL, sp.Alpha);
        attenuation = fresnel * g1L;

        // Compensate for lobe selection probability.
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Specular transmission for glass-like materials. Selects between
    /// Fresnel reflection and refraction stochastically (Schlick approximation)
    /// and applies Beer-like tinting via sqrt(baseColor).
    ///
    /// Frosted glass (Roughness &gt; 0.01) samples the microfacet normal via
    /// visible-NDF sampling (Heitz 2018) and reduces to a G1(L) geometry
    /// weight in [0, 1] — the same closed form used by <see cref="ScatterSpecular"/>.
    /// Smooth glass reuses the geometric normal and needs no weight.
    ///
    /// A full dispersive dielectric (unified reflection/refraction lobe with
    /// Beer-Lambert attenuation through a volume stack) is scheduled for the
    /// glass-BSDF step and will replace this approximation.
    /// </summary>
    private bool ScatterTransmission(Ray rayIn, HitRecord rec, Vector3 baseCol,
                                     Vector3 N, Vector3 V, in ShadingParams sp,
                                     float probability,
                                     out Vector3 attenuation, out Ray scattered)
    {
        float eta = rec.FrontFace ? (1f / sp.Ior) : sp.Ior;
        Vector3 unitDir = Vector3.Normalize(rayIn.Direction);

        // For rough transmissive materials, sample a visible microfacet
        // normal (Heitz 2018 VNDF) instead of the geometric normal. VNDF
        // samples are drawn from the subset of the GGX distribution that
        // is actually visible from V, which eliminates the masked-sample
        // variance that forced the old NDF-sampling path to clamp the
        // geometry weight to 1.
        bool isRough = sp.Roughness > 0.01f;
        Vector3 Ht = isRough ? SampleGgxVndf(N, V, sp.Alpha) : N;

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

        // ── VNDF geometry weight for rough transmission ────────────────────
        // Same F · G1(L) simplification as reflection: with VNDF sampling
        // the transmission BSDF/pdf ratio collapses to G1(L) (and the
        // Fresnel factor is applied separately by the caller via the
        // branch probability). For smooth glass (isRough = false), Ht = N
        // so G1(L) = G1(NdotL) = 1 within rounding and the weight is
        // effectively unity — we skip the computation.
        if (isRough)
        {
            Vector3 L = scattered.Direction;
            float NdotL_t = MathF.Max(MathF.Abs(Vector3.Dot(N, L)), 1e-4f);
            float g1L = SmithG1_GGX(NdotL_t, sp.Alpha);
            attenuation *= g1L;
        }

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Clearcoat: a fixed-IOR (1.5) secondary specular lobe with its own
    /// roughness. Always white (physically: a thin transparent varnish layer).
    ///
    /// Same VNDF sampling and F · G1(L) weight as <see cref="ScatterSpecular"/>,
    /// but with <c>sp.ClearcoatAlpha</c> and fixed F0 = 0.04. The clearcoat
    /// intensity is encoded in the lobe selection probability, not in the
    /// attenuation — the 1/probability compensation keeps the estimator unbiased.
    /// </summary>
    private bool ScatterClearcoat(HitRecord rec, Vector3 N, Vector3 V,
                                  in ShadingParams sp, float probability,
                                  out Vector3 attenuation, out Ray scattered)
    {
        Vector3 H = SampleGgxVndf(N, V, sp.ClearcoatAlpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        if (Vector3.Dot(L, N) <= 0f)
        {
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        float NdotL = MathF.Max(Vector3.Dot(N, L), 1e-4f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 1e-4f);

        // Clearcoat: fixed F0 = 0.04 (IOR ≈ 1.5)
        float f0 = 0.04f;
        float fresnel = f0 + (1f - f0) * SchlickWeight(VdotH);

        // VNDF weight: F · G1(L) (see ScatterSpecular for the derivation).
        float g1L = SmithG1_GGX(NdotL, sp.ClearcoatAlpha);
        attenuation = new Vector3(fresnel * g1L);

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
    /// Samples a visible microfacet normal from the GGX distribution using
    /// the Heitz 2018 algorithm ("Sampling the GGX Distribution of Visible
    /// Normals", JCGT vol. 7 no. 4).
    ///
    /// Unlike the classic Walter 2007 NDF sampling (which samples H from
    /// D(H)·NdotH without regard to the viewing direction), VNDF samples
    /// only the portion of the microfacet distribution that is actually
    /// visible from V. This eliminates "wasted" samples in masked regions
    /// and drives the BRDF/pdf ratio to a well-behaved closed form
    ///   weight = F · G1(L)   (separable Smith)
    /// that is bounded in [0, |F|] and does NOT require the clamp-to-1
    /// firefly guards the old NDF sampling needed.
    ///
    /// Algorithm (isotropic case):
    ///   1. Build an orthonormal frame around N and express V in it.
    ///   2. Stretch V along the z-axis by the GGX α to map the ellipsoidal
    ///      distribution onto a hemisphere.
    ///   3. Sample a point on the visible disk of that hemisphere using
    ///      the trapezoid-based reparameterisation from Heitz 2018 §3.2.
    ///   4. Project to the hemisphere and unstretch to recover the
    ///      world-space half-vector.
    /// </summary>
    private static Vector3 SampleGgxVndf(Vector3 N, Vector3 V, float alpha)
    {
        // Build a tangent frame (T, B) around N
        BuildTangentFrame(N, out Vector3 T, out Vector3 B);

        // Express V in tangent space (z-up = shading normal)
        Vector3 Vl = new(Vector3.Dot(V, T), Vector3.Dot(V, B), Vector3.Dot(V, N));

        // Stretch V by the GGX α (isotropic: αx = αy = α)
        Vector3 Vh = Vector3.Normalize(new Vector3(alpha * Vl.X, alpha * Vl.Y, Vl.Z));

        // Orthonormal basis spanning the plane perpendicular to Vh
        float lenSq = Vh.X * Vh.X + Vh.Y * Vh.Y;
        Vector3 T1 = lenSq > 0f
            ? new Vector3(-Vh.Y, Vh.X, 0f) / MathF.Sqrt(lenSq)
            : new Vector3(1f, 0f, 0f);
        Vector3 T2 = Vector3.Cross(Vh, T1);

        // Sample a point on the visible disk (Heitz 2018 eq. 5)
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();
        float r  = MathF.Sqrt(u1);
        float phi = 2f * MathF.PI * u2;
        float t1 = r * MathF.Cos(phi);
        float t2 = r * MathF.Sin(phi);
        float s  = 0.5f * (1f + Vh.Z);
        t2 = (1f - s) * MathF.Sqrt(MathF.Max(0f, 1f - t1 * t1)) + s * t2;

        // Project onto the hemisphere — the visible-normal half-vector in
        // stretched space.
        Vector3 Nh = t1 * T1 + t2 * T2
                   + MathF.Sqrt(MathF.Max(0f, 1f - t1 * t1 - t2 * t2)) * Vh;

        // Unstretch by dividing through by α and re-normalise. Clamp the z
        // component to zero to keep H on the upper hemisphere (numerically
        // robust at grazing incidence where Nh.Z may fall slightly below 0).
        Vector3 Hl = Vector3.Normalize(new Vector3(
            alpha * Nh.X,
            alpha * Nh.Y,
            MathF.Max(0f, Nh.Z)));

        // Back to world space
        Vector3 result = Hl.X * T + Hl.Y * B + Hl.Z * N;
        float resultLenSq = result.LengthSquared();
        return resultLenSq > 1e-8f ? result / MathF.Sqrt(resultLenSq) : N;
    }

    /// <summary>
    /// Constructs an orthonormal tangent frame (T, B) around N using
    /// Frisvad's method, with a safe branch for near-south-pole normals
    /// where the 1/(1+N.Z) term would explode.
    /// </summary>
    private static void BuildTangentFrame(Vector3 N, out Vector3 T, out Vector3 B)
    {
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
    }

    /// <summary>
    /// Computes the Fresnel reflectance at normal incidence (F0).
    /// Metals use baseColor directly; dielectrics use IOR-derived value
    /// optionally tinted towards baseColor via specularTint.
    /// </summary>
    private static Vector3 ComputeF0(Vector3 baseCol, in ShadingParams sp)
    {
        // Dielectric F0 from IOR
        float r = (sp.Ior - 1f) / (sp.Ior + 1f);
        float f0d = r * r;

        // Disney's specular parameter scales F0 (0.5 → standard, 1.0 → 2× brighter)
        float scaledF0 = f0d * 2f * sp.Specular;

        // Tint towards baseColor if specularTint > 0
        float lum = MathUtils.Luminance(baseCol);
        Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
        Vector3 dielectricF0 = Vector3.Lerp(new Vector3(scaledF0), scaledF0 * tintCol, sp.SpecularTint);

        // Blend between dielectric and metallic F0
        return Vector3.Lerp(dielectricF0, baseCol, sp.Metallic);
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
