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
    public ITexture BaseColor               { get; }
    public FloatTexture Metallic            { get; }
    public FloatTexture Roughness           { get; }
    public FloatTexture Subsurface          { get; }
    public FloatTexture Specular            { get; }
    public FloatTexture SpecularTint        { get; }
    public FloatTexture Sheen               { get; }
    public FloatTexture SheenTint           { get; }
    // Estevez-Kulla 2017 "Charlie" sheen roughness in (0, 1]. Lower values
    // give the thin, tightly grazing-angle look of velvet/satin; higher
    // values produce a broader, more dust-like sheen. Defaults to 0.3,
    // matching the Imageworks reference and Arnold's standard_surface
    // sheen_roughness default.
    public FloatTexture SheenRoughness      { get; }
    public FloatTexture Clearcoat           { get; }
    public FloatTexture ClearcoatGloss      { get; }
    public FloatTexture SpecTrans           { get; }
    public FloatTexture Ior                 { get; }
    public FloatTexture Anisotropic         { get; }
    public FloatTexture AnisotropicRotation { get; }

    // ── Glass / transmission volume ────────────────────────────────────────
    // TransmissionColor: tint the light acquires after travelling through
    //   TransmissionDepth scene units of the medium. When null (the default),
    //   the material keeps the legacy Disney 2012 approximation of
    //   sqrt(baseColor) applied at each refraction event — preserved for
    //   backward compatibility with scenes authored before Beer-Lambert.
    // TransmissionDepth: the reference distance (in world units) at which the
    //   transmitted colour equals TransmissionColor. Zero means "thin glass"
    //   — TransmissionColor is applied as a per-hit tint without volume
    //   tracking, matching a surface-only Disney glass. Positive depth
    //   activates Beer-Lambert: σ_a = -ln(TransmissionColor)/TransmissionDepth
    //   (per channel), applied along the next segment via the renderer's
    //   interior-medium tracker.
    public ITexture?    TransmissionColor   { get; }
    public FloatTexture TransmissionDepth   { get; }

    // ── Disney 2015 additions (thin-walled, foliage, subsurface tint) ──────
    // SubsurfaceColor: tints the Hanrahan-Krueger "flat" component of the
    //   diffuse lobe in place of BaseColor. Used to decouple the overall
    //   surface colour from the colour that light acquires as it scatters
    //   through the interior (skin, wax, milk). When null the legacy
    //   BaseColor-based tint is used for backward compatibility.
    // DiffTrans: fraction of the diffuse lobe that transmits through to the
    //   back hemisphere. Implements Disney 2015's thin-walled diffuse
    //   transmission used for foliage, paper, fabric. Sampled as a dedicated
    //   cosine-weighted back-hemisphere lobe.
    // Flatness: blends the regular Lambertian diffuse shape with the flatter
    //   HK subsurface approximation, independently of the Subsurface
    //   parameter. Useful on materials that need a "waxy" feel without the
    //   full subsurface blend.
    // ThinWalled: disables refraction on the transmission lobe so the
    //   transmitted ray continues in the incoming direction (no bending,
    //   no medium switch). The Fresnel split is preserved so the grazing-
    //   angle reflection of a thin slab still reads correctly.
    public ITexture?    SubsurfaceColor    { get; }
    public FloatTexture DiffTrans          { get; }
    public FloatTexture Flatness           { get; }
    public bool         ThinWalled         { get; }

    // ── Clearcoat (Arnold standard_surface "coat" parameters) ──────────────
    // Disney 2012 hard-coded F0 = 0.04 (IOR ≈ 1.5) and parameterised
    // sharpness through ClearcoatGloss (γ-clamped to [0.001, 0.1]). Arnold
    // and modern Disney 2015+ implementations expose:
    //   coat_ior:       index of refraction of the lacquer film. Defaults
    //                   to 1.5 (matches the legacy Disney 0.04). Higher
    //                   IOR → brighter Fresnel highlight (1.7-2.4 for
    //                   automotive paints, 2.4 for diamond-clear coat).
    //   coat_roughness: direct roughness control [0, 1] mapped to α via
    //                   roughness². Replaces the gloss-based mapping when
    //                   present; when null the legacy ClearcoatGloss path
    //                   is preserved for backwards compatibility.
    //   coat_normal:    optional dedicated normal map for the coat layer,
    //                   independent of the base surface normal map. Models
    //                   wave patterns or scratches in a clear lacquer that
    //                   sit on top of an otherwise smooth substrate.
    public FloatTexture     CoatIor       { get; }
    public FloatTexture?    CoatRoughness { get; }
    public NormalMapTexture? CoatNormal   { get; set; }

    // ── Thin-film iridescence (Belcour-Barla 2017) ─────────────────────────
    // ThinFilmThickness: film thickness in nanometres. 0 disables the
    //   iridescent Fresnel and the BSDF reverts to plain Schlick on F0.
    //   Useful range is roughly 100-800 nm — that's where one to two
    //   visible-band wavelengths fit a single round trip and the colour
    //   sweep through the spectrum is most pronounced (soap bubbles,
    //   beetle elytra, anti-reflection coatings, oil on water).
    // ThinFilmIor: index of refraction of the film itself (η₂). Defaults
    //   to 1.5 (an oily lacquer); 1.33 for water films, 2.0+ for highly
    //   refractive coatings. The substrate IOR is inferred per channel
    //   from the underlying F0 so the iridescence sits correctly on top
    //   of metals as well as dielectrics.
    public FloatTexture ThinFilmThickness { get; }
    public FloatTexture ThinFilmIor       { get; }

    // ── Normal map support ──────────────────────────────────────────────────
    public NormalMapTexture? NormalMap { get; set; }

    public DisneyBsdf(
        ITexture baseColor,
        FloatTexture? metallic            = null,
        FloatTexture? roughness           = null,
        FloatTexture? subsurface          = null,
        FloatTexture? specular            = null,
        FloatTexture? specularTint        = null,
        FloatTexture? sheen               = null,
        FloatTexture? sheenTint           = null,
        FloatTexture? sheenRoughness      = null,
        FloatTexture? clearcoat           = null,
        FloatTexture? clearcoatGloss      = null,
        FloatTexture? specTrans           = null,
        FloatTexture? ior                 = null,
        FloatTexture? anisotropic         = null,
        FloatTexture? anisotropicRotation = null,
        ITexture?     transmissionColor   = null,
        FloatTexture? transmissionDepth   = null,
        ITexture?     subsurfaceColor     = null,
        FloatTexture? diffTrans           = null,
        FloatTexture? flatness            = null,
        bool          thinWalled          = false,
        FloatTexture? coatIor             = null,
        FloatTexture? coatRoughness       = null,
        FloatTexture? thinFilmThickness   = null,
        FloatTexture? thinFilmIor         = null)
    {
        BaseColor           = baseColor;
        Metallic            = metallic            ?? new FloatTexture(0f);
        Roughness           = roughness           ?? new FloatTexture(0.5f);
        Subsurface          = subsurface          ?? new FloatTexture(0f);
        Specular            = specular            ?? new FloatTexture(0.5f);
        SpecularTint        = specularTint        ?? new FloatTexture(0f);
        Sheen               = sheen               ?? new FloatTexture(0f);
        SheenTint           = sheenTint           ?? new FloatTexture(0.5f);
        SheenRoughness      = sheenRoughness      ?? new FloatTexture(0.3f);
        Clearcoat           = clearcoat           ?? new FloatTexture(0f);
        ClearcoatGloss      = clearcoatGloss      ?? new FloatTexture(1f);
        SpecTrans           = specTrans           ?? new FloatTexture(0f);
        Ior                 = ior                 ?? new FloatTexture(1.5f);
        Anisotropic         = anisotropic         ?? new FloatTexture(0f);
        AnisotropicRotation = anisotropicRotation ?? new FloatTexture(0f);
        TransmissionColor   = transmissionColor;
        TransmissionDepth   = transmissionDepth   ?? new FloatTexture(0f);
        SubsurfaceColor     = subsurfaceColor;
        DiffTrans           = diffTrans           ?? new FloatTexture(0f);
        Flatness            = flatness            ?? new FloatTexture(0f);
        ThinWalled          = thinWalled;
        CoatIor             = coatIor             ?? new FloatTexture(1.5f);
        CoatRoughness       = coatRoughness;
        ThinFilmThickness   = thinFilmThickness   ?? new FloatTexture(0f);
        ThinFilmIor         = thinFilmIor         ?? new FloatTexture(1.5f);
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
        public readonly float SheenRoughness;     // Charlie NDF α ∈ (0, 1]
        public readonly float Clearcoat;
        public readonly float ClearcoatGloss;
        public readonly float SpecTrans;
        public readonly float Ior;
        public readonly float Anisotropic;          // 0 = isotropic, 1 = fully stretched along T
        public readonly float AnisotropicRotation;  // [0, 1], fraction of 2π around N
        public readonly float DiffTrans;            // [0, 1] fraction of diffuse that transmits
        public readonly float Flatness;             // [0, 1] Lambert → HK-flat blend
        public readonly float Alpha;                // roughness² (mean GGX α)
        public readonly float AlphaX;               // α along T (Burley 2012 §5.4)
        public readonly float AlphaY;               // α along B
        public readonly float ClearcoatAlpha;       // isotropic — clearcoat never anisotropic
        public readonly float ClearcoatF0;          // ((η-1)/(η+1))² for the coat layer
        public readonly float ThinFilmThicknessNm;  // 0 = disabled
        public readonly float ThinFilmIor;          // film η₂

        public ShadingParams(
            float metallic, float roughness, float subsurface,
            float specular, float specularTint,
            float sheen, float sheenTint, float sheenRoughness,
            float clearcoat, float clearcoatGloss,
            float specTrans, float ior,
            float anisotropic, float anisotropicRotation,
            float diffTrans, float flatness,
            float coatIor, float coatRoughness,
            float thinFilmThickness, float thinFilmIor)
        {
            Metallic            = Math.Clamp(metallic, 0f, 1f);
            Roughness           = Math.Clamp(roughness, 0f, 1f);
            Subsurface          = Math.Clamp(subsurface, 0f, 1f);
            Specular            = Math.Clamp(specular, 0f, 2f);
            SpecularTint        = Math.Clamp(specularTint, 0f, 1f);
            Sheen               = Math.Clamp(sheen, 0f, 1f);
            SheenTint           = Math.Clamp(sheenTint, 0f, 1f);
            SheenRoughness      = Math.Clamp(sheenRoughness, 0.04f, 1f);
            Clearcoat           = Math.Clamp(clearcoat, 0f, 1f);
            ClearcoatGloss      = Math.Clamp(clearcoatGloss, 0f, 1f);
            SpecTrans           = Math.Clamp(specTrans, 0f, 1f);
            Ior                 = MathF.Max(ior, 1.0001f);
            Anisotropic         = Math.Clamp(anisotropic, 0f, 1f);
            AnisotropicRotation = anisotropicRotation - MathF.Floor(anisotropicRotation); // [0, 1)
            DiffTrans           = Math.Clamp(diffTrans, 0f, 1f);
            Flatness            = Math.Clamp(flatness, 0f, 1f);

            Alpha          = MathF.Max(Roughness * Roughness, 0.001f);
            // Burley 2012 §5.4: aspect = sqrt(1 - 0.9·anisotropic), αx = α/aspect, αy = α·aspect.
            // Floor αx/αy at 0.001 independently to keep the anisotropic NDF well-defined at
            // the extremes (anisotropic=1, roughness≈0 would otherwise push αy to zero).
            float aspect = MathF.Sqrt(1f - 0.9f * Anisotropic);
            AlphaX         = MathF.Max(Alpha / aspect, 0.001f);
            AlphaY         = MathF.Max(Alpha * aspect, 0.001f);
            // Two clearcoat α paths: the legacy Disney "gloss" slider in
            // [0, 1] mapped to α ∈ [0.1, 0.001], or the modern direct
            // roughness used by Arnold standard_surface (α = roughness²).
            // A negative coatRoughness sentinel selects the legacy path so
            // existing scenes keep their look without an explicit override.
            ClearcoatAlpha = coatRoughness >= 0f
                ? MathF.Max(coatRoughness * coatRoughness, 0.001f)
                : Lerp(0.1f, 0.001f, ClearcoatGloss);
            // F0 for the coat layer derived from its IOR. Schlick's
            // approximation is exact at normal incidence and within ~1% of
            // the unpolarised dielectric Fresnel up to η ≈ 2.4 — adequate
            // for everything from default lacquer (η = 1.5, F0 = 0.04) to
            // automotive clear-coat with mica flakes (η = 2.4, F0 ≈ 0.17).
            float etaCoat = MathF.Max(coatIor, 1.0001f);
            float r0 = (etaCoat - 1f) / (etaCoat + 1f);
            ClearcoatF0 = r0 * r0;
            // Thin-film parameters: clamp thickness ≥ 0 (the smooth
            // degeneracy in ThinFilm.Evaluate handles t → 0); film IOR
            // floored at 1.0001 like the substrate IOR so Snell stays
            // well-defined when the artist accidentally enters 1.0.
            ThinFilmThicknessNm = MathF.Max(thinFilmThickness, 0f);
            ThinFilmIor         = MathF.Max(thinFilmIor, 1.0001f);
        }
    }

    private ShadingParams EvalParams(HitRecord rec)
    {
        float u = rec.U, v = rec.V;
        Vector3 p = rec.LocalPoint;
        int seed = rec.ObjectSeed;
        // CoatRoughness == null selects the legacy Disney 2012 gloss path via
        // a negative sentinel, so scenes without an explicit coat_roughness
        // value keep their previous appearance (α derived from ClearcoatGloss).
        float coatRough = CoatRoughness?.Value(u, v, p, seed) ?? -1f;
        return new ShadingParams(
            Metallic.Value(u, v, p, seed),
            Roughness.Value(u, v, p, seed),
            Subsurface.Value(u, v, p, seed),
            Specular.Value(u, v, p, seed),
            SpecularTint.Value(u, v, p, seed),
            Sheen.Value(u, v, p, seed),
            SheenTint.Value(u, v, p, seed),
            SheenRoughness.Value(u, v, p, seed),
            Clearcoat.Value(u, v, p, seed),
            ClearcoatGloss.Value(u, v, p, seed),
            SpecTrans.Value(u, v, p, seed),
            Ior.Value(u, v, p, seed),
            Anisotropic.Value(u, v, p, seed),
            AnisotropicRotation.Value(u, v, p, seed),
            DiffTrans.Value(u, v, p, seed),
            Flatness.Value(u, v, p, seed),
            CoatIor.Value(u, v, p, seed),
            coatRough,
            ThinFilmThickness.Value(u, v, p, seed),
            ThinFilmIor.Value(u, v, p, seed));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Direct lighting interface
    //
    // Disney materials always have at least one non-delta lobe reachable by
    // NEE (diffuse or GGX specular), and the transmission path is handled by
    // Sample() / BsdfSample.IsDelta — so we leave NeedsDirectLighting and
    // IsDeltaScatter at their interface defaults (true, false) and let the
    // Renderer decide per sample via BsdfSample.
    // ═════════════════════════════════════════════════════════════════════════

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

        // ── Disney diffuse lobe (forward hemisphere, post diff_trans split) ─
        float diffuseW = (1f - sp.Metallic) * (1f - sp.SpecTrans) * (1f - sp.DiffTrans);
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

        // ── Anisotropic GGX specular lobe ───────────────────────────────
        // Evaluated in tangent space so αx (along T) and αy (along B) apply
        // directly to the NDF and Smith Lambda. Reduces to the isotropic
        // form exactly when anisotropic = 0 (αx = αy = α).
        ShadingFrame frame = GetShadingFrame(rec, sp.AnisotropicRotation);
        Vector3 Vloc = frame.ToLocal(toEye);
        Vector3 Lloc = frame.ToLocal(toLight);
        Vector3 Hloc = frame.ToLocal(H);

        float D = Microfacet.DGgxAniso(Hloc, sp.AlphaX, sp.AlphaY);
        float G = Microfacet.G1GgxAniso(Vloc, sp.AlphaX, sp.AlphaY)
                * Microfacet.G1GgxAniso(Lloc, sp.AlphaX, sp.AlphaY);

        // F: Schlick Fresnel with continuous metallic→dielectric blend, or
        // Belcour-Barla 2017 thin-film Fresnel when ThinFilmThicknessNm > 0.
        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        Vector3 F0 = ComputeF0(baseCol, sp);
        Vector3 F = EvalFresnel(VdotH, F0, sp);

        // Cook-Torrance: D × G × F / (4 × NdotV × NdotL), then × NdotL
        Vector3 specular = D * G * F / MathF.Max(4f * NdotV, 1e-6f);

        // ── Clearcoat GGX lobe (isotropic — Disney convention) ──────────
        // Evaluated on its own shading normal (CoatNormal) so a scene with a
        // distinct coat bump map renders the coat highlight shape
        // independently of the substrate. CoatNormal == null inherits the
        // shaded surface normal — classic Disney behaviour.
        Vector3 clearcoat = Vector3.Zero;
        if (sp.Clearcoat > 0f)
        {
            Vector3 coatN = GetCoatNormal(rec);
            float ccNdotV = MathF.Max(Vector3.Dot(coatN, toEye), 0.001f);
            float ccNdotL = Vector3.Dot(coatN, toLight);
            if (ccNdotL > 0f)
            {
                float ccNdotH = MathF.Max(Vector3.Dot(coatN, H), 0f);
                float ccVdotH = MathF.Max(Vector3.Dot(toEye, H), 0f);

                float ca2 = sp.ClearcoatAlpha * sp.ClearcoatAlpha;
                float cDenom = ccNdotH * ccNdotH * (ca2 - 1f) + 1f;
                float cD = ca2 / (MathF.PI * cDenom * cDenom);
                float cG = Microfacet.SmithG1(ccNdotV, sp.ClearcoatAlpha) * Microfacet.SmithG1(ccNdotL, sp.ClearcoatAlpha);

                float cF = sp.ClearcoatF0 + (1f - sp.ClearcoatF0) * SchlickWeight(ccVdotH);

                // Returned shape is f · cos θᵢ, consistent with the specular
                // lobe above: the D·G/(4·ccNdotV·ccNdotL) BRDF is multiplied
                // by ccNdotL, which cancels the cosine in the denominator.
                clearcoat = new Vector3(sp.Clearcoat * 0.25f * cD * cG * cF
                            / MathF.Max(4f * ccNdotV, 1e-6f));
            }
        }

        // ── Sheen (Estevez-Kulla Charlie BRDF) ─────────────────────────────
        // Direct lighting was previously missing the sheen lobe entirely —
        // NEE on a velvet/fabric material would only see diffuse + specular,
        // killing the grazing-angle highlight that defines the look. The
        // Charlie BRDF is symmetric in V/L and was already wired through
        // Evaluate; mirroring it here closes the energy gap between direct
        // and indirect estimators.
        Vector3 sheen = Vector3.Zero;
        if (sp.Sheen > 0f && diffuseW > 0f)
        {
            float lum = MathUtils.Luminance(baseCol);
            Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
            Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, sp.SheenTint);
            float sheenBrdf = SheenCharlie.Brdf(NdotV, NdotL, NdotH, sp.SheenRoughness);
            sheen = sp.Sheen * sheenBrdf * sheenCol * NdotL;
        }

        // ── Multi-scatter compensation (Kulla-Conty) ───────────────────────
        // Returned shape is f · N·L (same convention as the rest of
        // EvaluateDirect). Shares the same LUT lookup used by Evaluate so
        // direct and indirect lighting stay energetically consistent.
        Vector3 multiscatter = EvaluateMultiscatter(F0, sp, NdotV, NdotL) * NdotL;

        // The global firefly clamp lives in the renderer (`--clamp`/`-C`) and
        // is the right place to cap the per-sample radiance if a scene still
        // produces outliers. The old 10.0 bias on the BRDF itself is gone
        // now that the indirect lobes use VNDF sampling and no longer
        // systematically overshoot the direct-light contribution.
        return diffuse + sheen + specular + multiscatter + clearcoat;
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
    // Back-hemisphere L is handled by the diff_trans lobe only (Disney 2015):
    // reflection and specular transmission are still excluded from the
    // analytical PDF. Specular transmission uses a delta sample path that
    // never invokes Evaluate/Pdf, so limiting the analytical form to the
    // cosine-weighted back-hemisphere lobe keeps MIS unbiased without
    // introducing a half-vector construction for refraction.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluates the multi-lobe Disney BSDF f(V, L) at the hit point, without
    /// the N·L cosine. Covers all reflection lobes and — for diff_trans > 0 —
    /// the back-hemisphere Lambertian diffuse-transmission lobe. Specular
    /// transmission (glass refraction) is excluded and handled by the delta
    /// sample path instead.
    /// </summary>
    public Vector3 Evaluate(Vector3 V, Vector3 L, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotL = Vector3.Dot(N, L);
        float NdotV = Vector3.Dot(N, V);
        if (NdotV <= 0f) return Vector3.Zero;

        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        ShadingParams sp = EvalParams(rec);

        // ── Back-hemisphere: diff_trans lobe only ──────────────────────────
        // Disney 2015 diffuse transmission is a Lambertian lobe in the
        // opposite hemisphere with the subsurface_color tint. All reflection
        // lobes contribute zero here.
        if (NdotL <= 0f)
        {
            float diffAll = (1f - sp.Metallic) * (1f - sp.SpecTrans);
            if (diffAll <= 0f || sp.DiffTrans <= 0f) return Vector3.Zero;
            Vector3 ssCol = ResolveSubsurfaceColor(rec, baseCol);
            return ssCol * (diffAll * sp.DiffTrans / MathF.PI);
        }

        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() < 1e-14f) return Vector3.Zero;
        Vector3 H = Vector3.Normalize(Hraw);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 0f);
        float LdotH = MathF.Max(Vector3.Dot(L, H), 0f);

        // ── Disney diffuse (retro-reflection Fresnel + HK flat blend) ──────
        // Forward lobe energy = diffAll · (1 - diffTrans). The Lambert vs
        // flat shape blend is driven by Subsurface and Flatness, and the
        // flat component uses SubsurfaceColor when present.
        float diffuseScalar = (1f - sp.Metallic) * (1f - sp.SpecTrans) * (1f - sp.DiffTrans);
        Vector3 diffuse = Vector3.Zero;
        if (diffuseScalar > 0f)
        {
            float fd90 = 0.5f + 2f * sp.Roughness * LdotH * LdotH;
            float fI = SchlickWeight(NdotV);
            float fO = SchlickWeight(NdotL);
            float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);

            // HK "flat" shape (Burley 2015) shared between the Subsurface
            // and Flatness blends. Grazing-angle denominator clamped to keep
            // the evaluation finite (matches ScatterDiffuse).
            float fss90 = sp.Roughness * LdotH * LdotH;
            float fssI = 1f + (fss90 - 1f) * fI;
            float fssO = 1f + (fss90 - 1f) * fO;
            float ssRaw = 1.25f * (fssI * fssO *
                                   (1f / (NdotV + NdotL + 0.001f) - 0.5f) + 0.5f);
            float ss = Math.Clamp(ssRaw, 0f, 2f);

            Vector3 ssCol = ResolveSubsurfaceColor(rec, baseCol);
            Vector3 lambert = baseCol * fd;
            Vector3 flat    = ssCol   * ss;
            Vector3 mixed   = Vector3.Lerp(lambert, flat, sp.Subsurface);
            Vector3 shaped  = Vector3.Lerp(mixed,   flat, sp.Flatness);
            diffuse = shaped * (diffuseScalar / MathF.PI);
        }

        // ── Sheen (Estevez-Kulla "Charlie" microfacet sheen) ───────────────
        // Cook-Torrance over the Charlie inverted-Gaussian NDF and Smith Λ
        // polynomial fit. The scalar Brdf is then tinted by the artist
        // sheen colour (1 → sheenTint·baseCol/luminance) so monochromatic
        // sheen and tinted sheen share one path. Multiplied by sp.Sheen at
        // the end so the slider remains a clean energy multiplier.
        Vector3 sheen = Vector3.Zero;
        if (sp.Sheen > 0f && diffuseScalar > 0f)
        {
            float lum = MathUtils.Luminance(baseCol);
            Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
            Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, sp.SheenTint);
            float sheenBrdf = SheenCharlie.Brdf(NdotV, NdotL, NdotH, sp.SheenRoughness);
            sheen = sp.Sheen * sheenBrdf * sheenCol;
        }

        // ── Anisotropic GGX specular (full Cook-Torrance in tangent space) ─
        ShadingFrame frame = GetShadingFrame(rec, sp.AnisotropicRotation);
        Vector3 Vloc = frame.ToLocal(V);
        Vector3 Lloc = frame.ToLocal(L);
        Vector3 Hloc = frame.ToLocal(H);
        float D = Microfacet.DGgxAniso(Hloc, sp.AlphaX, sp.AlphaY);
        float G = Microfacet.G1GgxAniso(Vloc, sp.AlphaX, sp.AlphaY)
                * Microfacet.G1GgxAniso(Lloc, sp.AlphaX, sp.AlphaY);
        Vector3 F0 = ComputeF0(baseCol, sp);
        Vector3 F = EvalFresnel(VdotH, F0, sp);
        Vector3 specular = D * G / MathF.Max(4f * NdotV * NdotL, 1e-7f) * F;

        // ── Clearcoat GGX (isotropic, evaluated on the coat normal) ────────
        // The coat sits in its own shading frame so a dedicated coat_normal
        // map perturbs the highlight without disturbing the base lobes. When
        // the coat normal differs from the base normal, ccNdotL can flip
        // sign (the base front-hemisphere L lands behind the coat) — in that
        // case the coat lobe contributes nothing, the rest of the BSDF still
        // does.
        Vector3 clearcoat = Vector3.Zero;
        if (sp.Clearcoat > 0f)
        {
            Vector3 coatN = GetCoatNormal(rec);
            float ccNdotV = Vector3.Dot(coatN, V);
            float ccNdotL = Vector3.Dot(coatN, L);
            if (ccNdotV > 0f && ccNdotL > 0f)
            {
                float ccNdotH = MathF.Max(Vector3.Dot(coatN, H), 0f);
                float ccVdotH = MathF.Max(Vector3.Dot(V, H), 0f);
                float ca2 = sp.ClearcoatAlpha * sp.ClearcoatAlpha;
                float cDenom = ccNdotH * ccNdotH * (ca2 - 1f) + 1f;
                float cD = ca2 / (MathF.PI * cDenom * cDenom);
                float cG = Microfacet.SmithG1(ccNdotV, sp.ClearcoatAlpha) * Microfacet.SmithG1(ccNdotL, sp.ClearcoatAlpha);
                float cF = sp.ClearcoatF0 + (1f - sp.ClearcoatF0) * SchlickWeight(ccVdotH);
                float cc = sp.Clearcoat * 0.25f * cD * cG * cF
                         / MathF.Max(4f * ccNdotV * ccNdotL, 1e-7f);
                clearcoat = new Vector3(cc);
            }
        }

        // ── Multi-scatter compensation (Kulla-Conty) ───────────────────────
        // Compensates the energy that Smith single-scatter drops at high α;
        // near-mirror surfaces see (1−E(μo))·(1−E(μi)) ≈ 0 so the term
        // vanishes automatically. Applied whether or not the surface is
        // metal — dielectrics carry the same multi-scatter deficit, scaled
        // by the smaller dielectric F̄.
        Vector3 multiscatter = EvaluateMultiscatter(F0, sp, NdotV, NdotL);

        return diffuse + sheen + specular + multiscatter + clearcoat;
    }

    /// <summary>
    /// Solid-angle PDF of sampling L from this BSDF's importance distribution,
    /// given the view direction V. Covers the same lobes as
    /// <see cref="Evaluate"/>: reflection lobes in the forward hemisphere,
    /// plus the cosine-weighted diff_trans lobe in the back hemisphere.
    /// Specular transmission is excluded (delta sample path only).
    ///
    /// Combined PDF = Σ_lobe p_lobe · pdf_lobe(L), matching the one-sample
    /// mixture estimator used in Scatter.
    /// </summary>
    public float Pdf(Vector3 V, Vector3 L, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotL = Vector3.Dot(N, L);
        float NdotV = Vector3.Dot(N, V);
        if (NdotV <= 0f) return 0f;

        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        ShadingParams sp = EvalParams(rec);
        LobeWeights w = ComputeLobeWeights(baseCol, sp);

        // Cosine-weighted lobes (diffuse, sheen, multiscatter) only sample
        // the upper hemisphere; diff_trans only samples the back hemisphere.
        float cosPdf       = NdotL > 0f ?  NdotL / MathF.PI : 0f;
        float diffTransPdf = NdotL < 0f ? -NdotL / MathF.PI : 0f;

        // VNDF lobes (specular, clearcoat) sample H from the visible
        // hemisphere of the microfacet NDF and reflect V; the resulting L
        // can land in either hemisphere of the macro normal (a microfacet
        // visible from V can still reflect into the macro-shadow zone).
        // Heitz 2018 / PBRT convention is to report the un-truncated VNDF
        // density over the full sphere of L — below-surface samples carry
        // zero BRDF mass via the G2 / G1 weight in ScatterSpecular, so MIS
        // stays unbiased without renormalising the PDF. Truncating the PDF
        // to the upper hemisphere here would under-report the lobe density
        // for back-hemisphere L by ~10-20% at α ≈ 0.4, breaking the
        // ∫ pdf dω = 1 invariant on the unit sphere.
        float specPdf = 0f;
        float ccPdf   = 0f;
        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() >= 1e-14f)
        {
            Vector3 H = Vector3.Normalize(Hraw);
            float NdotH = Vector3.Dot(N, H);
            // VNDF support requires H_z > 0; for back-hemisphere L far from
            // V the half-vector H = normalise(V+L) can dip below the macro
            // surface, in which case the microfacet NDF carries no mass.
            if (NdotH > 0f)
            {
                float safeNdotV = MathF.Max(NdotV, 1e-7f);
                ShadingFrame frame = GetShadingFrame(rec, sp.AnisotropicRotation);
                Vector3 Vloc = frame.ToLocal(V);
                Vector3 Hloc = frame.ToLocal(H);
                float D = Microfacet.DGgxAniso(Hloc, sp.AlphaX, sp.AlphaY);
                float g1V = Microfacet.G1GgxAniso(Vloc, sp.AlphaX, sp.AlphaY);
                specPdf = g1V * D / (4f * safeNdotV);
            }

            // Clearcoat lives in its own shading frame and uses its own
            // half-vector basis, so its VNDF density is computed against the
            // coat normal (not the base normal). Without this the coat PDF
            // mass would shift off the perturbed coat lobe whenever a coat
            // normal map is in play, breaking MIS reciprocity.
            Vector3 coatN = GetCoatNormal(rec);
            float ccNdotV = Vector3.Dot(coatN, V);
            float ccNdotH = Vector3.Dot(coatN, H);
            if (ccNdotV > 0f && ccNdotH > 0f)
            {
                float safeCcNdotV = MathF.Max(ccNdotV, 1e-7f);
                float ca2 = sp.ClearcoatAlpha * sp.ClearcoatAlpha;
                float cDenom = ccNdotH * ccNdotH * (ca2 - 1f) + 1f;
                float cD = ca2 / (MathF.PI * cDenom * cDenom);
                float cG1V = Microfacet.SmithG1(ccNdotV, sp.ClearcoatAlpha);
                ccPdf = cG1V * cD / (4f * safeCcNdotV);
            }
        }

        return w.PDiffuse      * cosPdf
             + w.PSheen        * cosPdf
             + w.PMultiscatter * cosPdf
             + w.PSpecular     * specPdf
             + w.PClearcoat    * ccPdf
             + w.PDiffTrans    * diffTransPdf;
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
        // attenuation directly (it already contains the Fresnel × per-hit
        // tint); delta samples in BsdfSample are interpreted by the renderer
        // as "attenuation = F" with no cos / pdf factor. The sample also
        // carries the medium-switch signal so the renderer can apply
        // Beer-Lambert absorption along the next interior segment (entering
        // glass → σ_a; exiting → vacuum).
        if (NdotWo <= 0f)
        {
            Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
            // Thin-walled surfaces never switch the interior medium (by
            // definition they have no interior bulk). Emit null so the
            // renderer preserves its current absorption state.
            Vector3? next;
            if (ThinWalled)
            {
                next = null;
            }
            else
            {
                (_, Vector3 sigma) = ResolveTransmission(rec, baseCol);
                // Non-null only when the segment actually crosses into a new
                // medium; emitting null lets the renderer keep whatever interior
                // state it was already tracking (harmless for vacuum↔vacuum).
                bool hasSigma = sigma.X > 0f || sigma.Y > 0f || sigma.Z > 0f;
                if (rec.FrontFace && hasSigma) next = sigma;
                else if (!rec.FrontFace) next = Vector3.Zero; // exiting → restore vacuum
                else next = null;
            }
            return new BsdfSample(wo, scatterAttn, 1f, isDelta: true,
                                  nextSegmentAbsorption: next);
        }

        Vector3 f = Evaluate(V, wo, rec);
        float pdf = Pdf(V, wo, rec);
        return new BsdfSample(wo, f, pdf, isDelta: false);
    }

    /// <summary>
    /// Resolves the per-hit transmission tint and the interior Beer-Lambert
    /// absorption coefficient σ_a from <see cref="TransmissionColor"/> and
    /// <see cref="TransmissionDepth"/>.
    ///
    /// Three regimes:
    ///   • TransmissionColor null (legacy): tint = sqrt(baseColor), σ_a = 0.
    ///     Matches the Disney-2012 approximation preserved for pre-Beer-Lambert
    ///     scenes.
    ///   • Explicit TransmissionColor, depth = 0: thin glass. tint =
    ///     TransmissionColor, σ_a = 0. Colour is applied once per refraction
    ///     event (entry and exit symmetrically when a ray crosses a glass slab).
    ///   • Explicit TransmissionColor, depth &gt; 0: Beer-Lambert. tint = 1,
    ///     σ_a = -ln(TransmissionColor) / TransmissionDepth per channel.
    ///     Colour accrues along the interior segment proportional to path
    ///     length — the physical behaviour measured in real glass/wine/ink.
    /// </summary>
    private (Vector3 tint, Vector3 sigma) ResolveTransmission(HitRecord rec, Vector3 baseCol)
    {
        float u = rec.U, v = rec.V;
        Vector3 p = rec.LocalPoint;
        int seed = rec.ObjectSeed;

        if (TransmissionColor == null)
        {
            Vector3 legacy = new(
                MathF.Sqrt(MathF.Max(baseCol.X, 0f)),
                MathF.Sqrt(MathF.Max(baseCol.Y, 0f)),
                MathF.Sqrt(MathF.Max(baseCol.Z, 0f)));
            return (legacy, Vector3.Zero);
        }

        Vector3 tColor = TransmissionColor.Value(u, v, p, seed);
        float tDepth = MathF.Max(TransmissionDepth.Value(u, v, p, seed), 0f);

        if (tDepth <= 0f)
            return (tColor, Vector3.Zero);

        // σ_a = -ln(C) / D. Floor each channel at a tiny positive value so
        // log() stays finite for the "perfectly absorbing" C = 0 limit.
        Vector3 c = new(
            MathF.Max(tColor.X, 1e-6f),
            MathF.Max(tColor.Y, 1e-6f),
            MathF.Max(tColor.Z, 1e-6f));
        float invD = 1f / tDepth;
        Vector3 sigma = new(
            -MathF.Log(c.X) * invD,
            -MathF.Log(c.Y) * invD,
            -MathF.Log(c.Z) * invD);
        return (Vector3.One, sigma);
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
        public readonly float Multiscatter;
        public readonly float DiffTrans;
        public readonly float Total;

        public LobeWeights(float d, float s, float t, float c, float sh, float ms, float dt)
        {
            Diffuse = d; Specular = s; Transmission = t;
            Clearcoat = c; Sheen = sh; Multiscatter = ms; DiffTrans = dt;
            float sum = d + s + t + c + sh + ms + dt;
            Total = sum < 1e-6f ? 1f : sum;
        }

        public float PDiffuse      => Diffuse / Total;
        public float PSpecular     => Specular / Total;
        public float PTransmission => Transmission / Total;
        public float PClearcoat    => Clearcoat / Total;
        public float PSheen        => Sheen / Total;
        public float PMultiscatter => Multiscatter / Total;
        public float PDiffTrans    => DiffTrans / Total;
    }

    private static LobeWeights ComputeLobeWeights(Vector3 baseCol, in ShadingParams sp)
    {
        float diffuseAll = (1f - sp.Metallic) * (1f - sp.SpecTrans);
        // Disney 2015: diffTrans splits the diffuse energy between the
        // forward (ScatterDiffuse) and back-hemisphere (ScatterDiffTrans)
        // lobes. Their selection probabilities scale accordingly so the
        // one-sample estimator stays unbiased.
        float diffuseW   = diffuseAll * (1f - sp.DiffTrans);
        float diffTransW = diffuseAll * sp.DiffTrans;
        float specF0    = sp.Metallic > 0.5f ? MathUtils.Luminance(baseCol)
                          : 0.04f * sp.Specular;
        float specFloor = 0.1f * (1f - sp.SpecTrans * 0.9f); // FIX #8e
        float specularW = MathF.Max(specFloor, Lerp(specF0, 1f, sp.Metallic));
        float transW    = (1f - sp.Metallic) * sp.SpecTrans;
        float clearW    = sp.Clearcoat * 0.04f;
        // Sheen stays proportional to the full diffuse pool (forward + back),
        // since grazing-angle sheen is present on both sides of a thin-walled
        // leaf or on the front of a foliage surface regardless of diffTrans.
        float sheenW    = sp.Sheen * 0.25f * diffuseAll;
        // Multi-scattering compensation lobe: scaled by the specular lobe's
        // expected energy deficit (1 - E_avg(α)). Near-mirror surfaces have
        // E_avg ≈ 1 and the lobe receives ~0% of samples; rough surfaces
        // (α ~ 1) push (1 - E_avg) toward 0.5 and shift samples into the
        // compensation path exactly where the single-scatter lobe is
        // leaking energy.
        float alphaIso   = MathF.Sqrt(sp.AlphaX * sp.AlphaY);
        float eAvg       = EnergyCompensationLut.SampleEAvg(alphaIso);
        float msW        = specularW * (1f - eAvg);
        return new LobeWeights(diffuseW, specularW, transW, clearW, sheenW, msW, diffTransW);
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
        float pDiffuse      = w.PDiffuse;
        float pSpecular     = w.PSpecular;
        float pTrans        = w.PTransmission;
        float pSheen        = w.PSheen;
        float pMultiscatter = w.PMultiscatter;
        float pDiffTrans    = w.PDiffTrans;
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
        else if ((rnd -= pSheen) < pMultiscatter)
        {
            result = ScatterMultiscatter(rec, baseCol, N, V, sp, pMultiscatter, out attenuation, out scattered);
        }
        else if ((rnd -= pMultiscatter) < pDiffTrans)
        {
            result = ScatterDiffTrans(rec, baseCol, N, V, sp, pDiffTrans, out attenuation, out scattered);
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

        // Disney 2015: the HK "flat" shape uses subsurface_color when the
        // artist supplied one, otherwise falls back to base_color — so
        // skin/milk/wax carry an internal colour that's decoupled from the
        // surface albedo. Flatness pushes toward the flat shape even when
        // Subsurface = 0 (e.g. matte paper, unfinished wood).
        Vector3 ssCol = ResolveSubsurfaceColor(rec, baseCol);
        Vector3 diffLambert = baseCol * fd;
        Vector3 diffFlat    = ssCol   * ss;
        Vector3 diffMixed   = Vector3.Lerp(diffLambert, diffFlat, sp.Subsurface);
        attenuation = Vector3.Lerp(diffMixed, diffFlat, sp.Flatness);

        // Disney 2015: diff_trans splits the diffuse lobe between forward
        // and back hemispheres. The forward lobe (this path) keeps only
        // (1 - diffTrans) of the energy; the remaining diffTrans is sampled
        // by ScatterDiffTrans. Leaves, paper, fabric: diffTrans ≈ 0.5-0.7.
        attenuation *= (1f - sp.DiffTrans);

        // Compensate for lobe selection probability (multi-lobe MIS).
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Resolves the per-hit subsurface tint: sampled from
    /// <see cref="SubsurfaceColor"/> when present, else a passthrough of
    /// <paramref name="baseCol"/>. Used by both the approximate subsurface
    /// shape in <see cref="ScatterDiffuse"/> and the diffuse-transmission
    /// lobe for foliage materials.
    /// </summary>
    private Vector3 ResolveSubsurfaceColor(HitRecord rec, Vector3 baseCol)
        => SubsurfaceColor == null
            ? baseCol
            : SubsurfaceColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);

    /// <summary>
    /// Disney 2015 diffuse transmission (diff_trans) lobe. Cosine-weighted
    /// sampling about the INVERTED normal so the scattered ray enters the
    /// back hemisphere — the physical behaviour of a translucent thin
    /// surface like a leaf, a paper lampshade, or a silk curtain: light
    /// that hits the front face is partially forward-scattered (Lambert)
    /// and partially transmitted and re-emitted from the back side.
    ///
    /// Value is <c>ssCol · (1/π)</c> (Lambertian in the back hemisphere).
    /// The per-lobe energy carried is <c>diffTrans</c>, and the estimator
    /// weight is <c>f · |NdotL| / pdf</c> = <c>ssCol · diffTrans</c> once the
    /// cosine and cosine-PDF cancel.
    /// </summary>
    private bool ScatterDiffTrans(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                                  in ShadingParams sp, float probability,
                                  out Vector3 attenuation, out Ray scattered)
    {
        // Cosine-weighted hemisphere about -N (back side).
        Vector3 scatterDir = -N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDir))
            scatterDir = -N;
        scatterDir = Vector3.Normalize(scatterDir);

        scattered = new Ray(rec.Point, scatterDir);

        Vector3 ssCol = ResolveSubsurfaceColor(rec, baseCol);
        attenuation = ssCol * sp.DiffTrans;

        float safeProbability = MathF.Max(probability, 0.05f);
        attenuation /= safeProbability;
        return true;
    }

    /// <summary>
    /// Dedicated sheen lobe using the Estevez-Kulla "Charlie" microfacet
    /// BRDF. Sampling stays cosine-weighted (the Charlie NDF has no closed-
    /// form inverse CDF, and every production renderer cited by Imageworks
    /// uses cosine sampling here) but the BRDF weight now uses the proper
    /// inverted-Gaussian D and Smith Λ — so the strength of the lobe at
    /// grazing angles is preserved instead of being washed out by the old
    /// SchlickWeight approximation, and the 1/π Lambertian normaliser is
    /// included so the lobe is correctly energy-conserving.
    ///
    /// Estimator weight: f · cos θ_l / pdf = f · π · cos θ_l / cos θ_l =
    /// π · f. We multiply f (the Charlie BRDF, no cosine) by π and then
    /// apply the per-channel sheen tint.
    /// </summary>
    private bool ScatterSheen(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                              in ShadingParams sp, float probability,
                              out Vector3 attenuation, out Ray scattered)
    {
        // Cosine-weighted hemisphere sampling — BRDF and pdf cancel cleanly
        // (see method-level comment above).
        Vector3 scatterDir = N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDir))
            scatterDir = N;
        scatterDir = Vector3.Normalize(scatterDir);

        scattered = new Ray(rec.Point, scatterDir);

        Vector3 L = scatterDir;
        float NdotV = MathF.Max(Vector3.Dot(N, V), 1e-4f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 1e-4f);

        Vector3 Hraw = V + L;
        float hLenSq = Hraw.LengthSquared();
        Vector3 H = hLenSq > 1e-7f ? Hraw / MathF.Sqrt(hLenSq) : N;
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);

        float sheenBrdf = SheenCharlie.Brdf(NdotV, NdotL, NdotH, sp.SheenRoughness);
        float lum = MathUtils.Luminance(baseCol);
        Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
        Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, sp.SheenTint);
        // π · BRDF · sheen_amount · tint  (cosine cancels with cosine pdf).
        attenuation = sp.Sheen * MathF.PI * sheenBrdf * sheenCol;

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
        // Sample a visible microfacet normal in the tangent frame so αx/αy
        // stretch the NDF correctly along T and B.
        ShadingFrame frame = GetShadingFrame(rec, sp.AnisotropicRotation);
        Vector3 Vloc = frame.ToLocal(V);
        Vector3 Hloc = Microfacet.SampleGgxVndfAniso(Vloc, sp.AlphaX, sp.AlphaY,
                                          MathUtils.RandomFloat(), MathUtils.RandomFloat());
        Vector3 H = frame.ToWorld(Hloc);
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

        float VdotH = MathF.Max(Vector3.Dot(V, H), 1e-4f);

        Vector3 F0 = ComputeF0(baseCol, sp);
        Vector3 fresnel = EvalFresnel(VdotH, F0, sp);

        // VNDF importance-sampling weight.
        //   BRDF            = F · D · G / (4 · NdotV · NdotL)
        //   VNDF pdf_L      = G1(V) · D / (4 · NdotV)
        //   BRDF·cos / pdf  = F · G / G1(V) = F · G1(L)   (separable Smith)
        //
        // G1(L) is inherently in [0, 1], so the old "clamp ggxWeight to 1"
        // firefly guard from Walter-2007 NDF sampling is no longer needed.
        Vector3 Lloc = frame.ToLocal(L);
        float g1L = Microfacet.G1GgxAniso(Lloc, sp.AlphaX, sp.AlphaY);
        attenuation = fresnel * g1L;

        // Compensate for lobe selection probability.
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Specular transmission for glass-like materials. Selects between
    /// Fresnel reflection and refraction stochastically using the exact
    /// unpolarised dielectric Fresnel equations (Schlick diverges from the
    /// true value by several percent at grazing incidence — enough to bias
    /// a converged glass render).
    ///
    /// Frosted glass (Roughness &gt; 0.01) samples the microfacet normal via
    /// visible-NDF sampling (Heitz 2018) and reduces to a G1(L) geometry
    /// weight in [0, 1] — the same closed form used by <see cref="ScatterSpecular"/>.
    /// Smooth glass reuses the geometric normal and needs no weight.
    ///
    /// Transmitted colour is driven by <see cref="TransmissionColor"/> and
    /// <see cref="TransmissionDepth"/>. With depth = 0 (the "thin glass"
    /// default) the colour is applied as a per-hit tint; with depth &gt; 0
    /// the hit produces a neutral attenuation and the renderer tracks
    /// Beer-Lambert absorption along the next interior ray segment via
    /// <see cref="BsdfSample.NextSegmentAbsorption"/>.
    ///
    /// When <see cref="ThinWalled"/> is set the surface is treated as a
    /// membrane with no interior (leaves, paper, lampshade fabric):
    /// refraction is disabled (transmitted direction stays parallel to the
    /// incoming ray), Fresnel is evaluated with the outside-to-inside
    /// η = 1/IOR regardless of face, and no medium-switch signal is emitted.
    /// Beer-Lambert absorption is also skipped — a thin wall has no bulk
    /// volume to absorb through.
    /// </summary>
    private bool ScatterTransmission(Ray rayIn, HitRecord rec, Vector3 baseCol,
                                     Vector3 N, Vector3 V, in ShadingParams sp,
                                     float probability,
                                     out Vector3 attenuation, out Ray scattered)
    {
        // Thin-walled geometry has no interior: both faces reflect/transmit
        // with the same 1/IOR Fresnel, so we don't flip η across faces.
        float eta = ThinWalled ? (1f / sp.Ior)
                              : (rec.FrontFace ? (1f / sp.Ior) : sp.Ior);
        Vector3 unitDir = Vector3.Normalize(rayIn.Direction);

        // For rough transmissive materials, sample a visible microfacet
        // normal (Heitz 2018 anisotropic VNDF) in the tangent frame. VNDF
        // samples are drawn from the subset of the GGX distribution that
        // is actually visible from V, which eliminates the masked-sample
        // variance that forced the old NDF-sampling path to clamp the
        // geometry weight to 1.
        ShadingFrame frame = GetShadingFrame(rec, sp.AnisotropicRotation);
        bool isRough = sp.Roughness > 0.01f;
        Vector3 Ht;
        if (isRough)
        {
            Vector3 Vloc = frame.ToLocal(V);
            Vector3 Hloc = Microfacet.SampleGgxVndfAniso(Vloc, sp.AlphaX, sp.AlphaY,
                                              MathUtils.RandomFloat(), MathUtils.RandomFloat());
            Ht = frame.ToWorld(Hloc);
        }
        else
        {
            Ht = N;
        }

        // Ensure the microfacet normal faces the incoming ray
        if (Vector3.Dot(Ht, unitDir) > 0f)
            Ht = -Ht;

        float cosTheta = MathF.Min(Vector3.Dot(-unitDir, Ht), 1f);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        // Thin-walled geometry never undergoes total internal reflection:
        // there is no "inside" for the ray to be trapped in.
        bool cannotRefract = !ThinWalled && eta * sinTheta > 1f;
        Vector3 direction;

        float fr = cannotRefract ? 1f : MathUtils.FresnelDielectric(cosTheta, eta);
        if (fr > MathUtils.RandomFloat())
        {
            // Total internal reflection or Fresnel reflection
            direction = MathUtils.Reflect(unitDir, Ht);
        }
        else if (ThinWalled)
        {
            // Thin-walled transmission: ray passes through without bending.
            // Matches Disney 2015's thin_walled_glass treatment and is also
            // what USDPreviewSurface produces when thinWalled = true.
            direction = unitDir;
        }
        else
        {
            direction = MathUtils.Refract(unitDir, Ht, eta);
        }

        // Guard: if refraction produced a degenerate direction, fall back
        if (MathUtils.NearZero(direction))
            direction = MathUtils.Reflect(unitDir, N);

        scattered = new Ray(rec.Point, Vector3.Normalize(direction));

        // Per-hit transmission tint. In Beer-Lambert mode (depth > 0) this is
        // white and the colour comes entirely from the interior-segment
        // absorption; in thin-glass mode (depth == 0) this carries the tint.
        (Vector3 tint, _) = ResolveTransmission(rec, baseCol);
        attenuation = tint;

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
            Vector3 Lloc = frame.ToLocal(L);
            // Smith Λ/G1 is parametrised by |cosθ| (the direction's sign is
            // absorbed by Λ's even dependence on wz); flipping Lloc.Z for the
            // transmission hemisphere keeps the lookup well-defined.
            Lloc.Z = MathF.Abs(Lloc.Z);
            float g1L = Microfacet.G1GgxAniso(Lloc, sp.AlphaX, sp.AlphaY);
            attenuation *= g1L;
        }

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Clearcoat: a secondary specular lobe parameterised by an Arnold-style
    /// coat IOR (defaults to 1.5 → F0 ≈ 0.04, classic Disney) and either an
    /// explicit coat_roughness or the legacy clearcoat_gloss slider. A
    /// dedicated coat_normal map perturbs the highlight independently of the
    /// substrate's NormalMap.
    ///
    /// Same VNDF sampling and F · G1(L) weight as <see cref="ScatterSpecular"/>,
    /// applied with <c>sp.ClearcoatAlpha</c> and <c>sp.ClearcoatF0</c>. The
    /// clearcoat intensity is encoded in the lobe selection probability, not
    /// in the attenuation — the 1/probability compensation keeps the
    /// estimator unbiased.
    /// </summary>
    private bool ScatterClearcoat(HitRecord rec, Vector3 N, Vector3 V,
                                  in ShadingParams sp, float probability,
                                  out Vector3 attenuation, out Ray scattered)
    {
        // Clearcoat is isotropic by convention, so a Frisvad ONB built on the
        // coat normal is sufficient (no need to align with the substrate's
        // anisotropic rotation, which only matters when αx ≠ αy). Sampling
        // happens in this dedicated coat frame so a coat_normal map shapes
        // the lobe correctly.
        ShadingFrame frame = GetClearcoatFrame(rec);
        Vector3 coatN = frame.N;
        // VNDF requires the view above the local hemisphere. When the coat
        // normal tilts past the silhouette of the substrate, V can dip below
        // the coat surface — bail out (no coat reflection from this view).
        if (Vector3.Dot(coatN, V) <= 0f)
        {
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        Vector3 Vloc = frame.ToLocal(V);
        Vector3 Hloc = Microfacet.SampleGgxVndfAniso(Vloc, sp.ClearcoatAlpha, sp.ClearcoatAlpha,
                                                     MathUtils.RandomFloat(), MathUtils.RandomFloat());
        Vector3 H = frame.ToWorld(Hloc);
        Vector3 L = MathUtils.Reflect(-V, H);

        // Reject below-surface samples against BOTH the macro normal and the
        // coat normal. A coat-frame upper-hemisphere L can still land below
        // the macro normal (and vice versa) when coatN tilts; the renderer
        // wouldn't see anything beyond the macro surface, so the sample is
        // wasted and the contribution must be zero.
        if (Vector3.Dot(L, N) <= 0f || Vector3.Dot(L, coatN) <= 0f)
        {
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        float ccNdotL = MathF.Max(Vector3.Dot(coatN, L), 1e-4f);
        float ccVdotH = MathF.Max(Vector3.Dot(V, H), 1e-4f);

        // Clearcoat Schlick Fresnel from the coat IOR (Arnold parity).
        float fresnel = sp.ClearcoatF0 + (1f - sp.ClearcoatF0) * SchlickWeight(ccVdotH);

        // VNDF weight: F · G1(L) (see ScatterSpecular for the derivation).
        float g1L = Microfacet.SmithG1(ccNdotL, sp.ClearcoatAlpha);
        attenuation = new Vector3(fresnel * g1L);

        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Kulla-Conty multi-scattering compensation lobe. Cosine-weighted
    /// hemisphere sampling (the f_ms integrand is nearly Lambertian — a
    /// product of two slowly-varying directional-albedo terms with a 1/π
    /// normalisation). The sample is carried by the symmetric f_ms so the
    /// estimator BRDF·cos / pdf_cos reduces to
    ///   F_ms · (1 − E(μo)) · (1 − E(μi)) / (1 − E_avg) / prob.
    /// </summary>
    private bool ScatterMultiscatter(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                                     in ShadingParams sp, float probability,
                                     out Vector3 attenuation, out Ray scattered)
    {
        Vector3 scatterDir = N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDir)) scatterDir = N;
        scatterDir = Vector3.Normalize(scatterDir);

        scattered = new Ray(rec.Point, scatterDir);
        Vector3 L = scatterDir;

        float NdotV = MathF.Max(Vector3.Dot(N, V), 1e-3f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 1e-3f);
        Vector3 F0 = ComputeF0(baseCol, sp);

        // f_ms · π cancels the 1/π inside f_ms. (cosPdf = NdotL/π → weight = f·π.)
        Vector3 weight = MultiscatterWeight(F0, sp, NdotV, NdotL);

        float safeProbability = MathF.Max(probability, 0.05f);
        attenuation = weight / safeProbability;
        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Shading frame
    //
    // GGX sampling, D, and Smith Λ/G1 now live in Microfacet.cs so the
    // energy-compensation LUT builder shares one implementation. Disney keeps
    // only the tangent-frame construction, which depends on HitRecord and
    // therefore isn't shareable as-is.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Orthonormal shading frame (T, B, N) used by the anisotropic lobes to
    /// express directions in tangent space. World → local via dot products;
    /// local → world via the linear combination of basis vectors.
    /// </summary>
    private readonly struct ShadingFrame
    {
        public readonly Vector3 T;
        public readonly Vector3 B;
        public readonly Vector3 N;

        public ShadingFrame(Vector3 t, Vector3 b, Vector3 n) { T = t; B = b; N = n; }

        public Vector3 ToLocal(Vector3 w)
            => new(Vector3.Dot(w, T), Vector3.Dot(w, B), Vector3.Dot(w, N));

        public Vector3 ToWorld(Vector3 w)
            => w.X * T + w.Y * B + w.Z * N;
    }

    /// <summary>
    /// Resolves the effective surface normal used by the clearcoat lobe.
    /// When <see cref="CoatNormal"/> is null the coat inherits the shaded
    /// surface normal (already perturbed by <see cref="NormalMap"/> upstream
    /// in the renderer), so a scene with no coat-specific normal map keeps
    /// the substrate's bumps under the coat. When a dedicated coat normal
    /// map is provided it's sampled in the same tangent frame as the base
    /// NM and layered on top of <c>rec.Normal</c>, modelling scratches or
    /// orange-peel effects in the lacquer that sit independently of the
    /// substrate. Falls back gracefully to <c>rec.Normal</c> when the
    /// geometry didn't populate a usable TBN.
    /// </summary>
    private Vector3 GetCoatNormal(HitRecord rec)
    {
        if (CoatNormal == null) return rec.Normal;
        if (rec.Tangent.LengthSquared() < 1e-10f || rec.Bitangent.LengthSquared() < 1e-10f)
            return rec.Normal;

        Vector3 N = rec.Normal;
        Vector3 T = rec.Tangent - Vector3.Dot(rec.Tangent, N) * N;
        float tLenSq = T.LengthSquared();
        if (tLenSq < 1e-10f) return rec.Normal;
        T /= MathF.Sqrt(tLenSq);
        Vector3 B = Vector3.Cross(N, T);
        // Preserve tangent-space handedness on backfaces (matches the base
        // NormalMap convention in Renderer.ApplyNormalMap).
        if (!rec.FrontFace) { T = -T; B = -B; }

        Vector3 ts = CoatNormal.SampleNormal(rec.U, rec.V);
        Vector3 perturbed = T * ts.X + B * ts.Y + N * ts.Z;
        float len = perturbed.LengthSquared();
        return len < 1e-10f ? rec.Normal : perturbed / MathF.Sqrt(len);
    }

    /// <summary>
    /// Isotropic shading frame for the clearcoat lobe. Coat is rotationally
    /// symmetric (αx = αy) so any orthonormal pair spanning the plane
    /// perpendicular to the coat normal works — we use Frisvad's branchless
    /// ONB directly on <see cref="GetCoatNormal"/>. Returning a frame keeps
    /// the VNDF sampling in <see cref="ScatterClearcoat"/> on the shared
    /// tangent-space path.
    /// </summary>
    private ShadingFrame GetClearcoatFrame(HitRecord rec)
    {
        Vector3 coatN = GetCoatNormal(rec);
        Microfacet.BuildTangentFrame(coatN, out Vector3 T, out Vector3 B);
        return new ShadingFrame(T, B, coatN);
    }

    /// <summary>
    /// Builds the shading frame for a hit, preferring the geometry's
    /// <see cref="HitRecord.Tangent"/> / <see cref="HitRecord.Bitangent"/>
    /// when they are valid (non-zero) so anisotropic highlights align with
    /// the surface's parameterisation (brushed-metal direction, wood grain,
    /// fabric weave). Falls back to Frisvad's branchless ONB when the
    /// geometry didn't populate a TBN.
    ///
    /// If <paramref name="rotation"/> is non-zero, rotates T toward B by an
    /// angle 2π·rotation, letting the artist control the anisotropic axis
    /// independently of the UV mapping.
    /// </summary>
    private static ShadingFrame GetShadingFrame(HitRecord rec, float rotation)
    {
        Vector3 N = rec.Normal;
        Vector3 T, B;
        if (rec.Tangent.LengthSquared() > 1e-10f && rec.Bitangent.LengthSquared() > 1e-10f)
        {
            // Re-orthogonalise against the shading normal (which may have been
            // perturbed by a normal map after the primitive set Tangent/Bitangent)
            // and re-derive B = N × T to guarantee a right-handed frame.
            T = rec.Tangent - Vector3.Dot(rec.Tangent, N) * N;
            float tLenSq = T.LengthSquared();
            if (tLenSq > 1e-10f)
            {
                T /= MathF.Sqrt(tLenSq);
                B = Vector3.Cross(N, T);
            }
            else
            {
                Microfacet.BuildTangentFrame(N, out T, out B);
            }
        }
        else
        {
            Microfacet.BuildTangentFrame(N, out T, out B);
        }

        if (rotation != 0f)
        {
            float angle = 2f * MathF.PI * rotation;
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            Vector3 Tr =  c * T + s * B;
            Vector3 Br = -s * T + c * B;
            T = Tr; B = Br;
        }

        return new ShadingFrame(T, B, N);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Multi-scattering energy compensation (Kulla-Conty 2017 / Turquin 2019)
    //
    // <see cref="EnergyCompensationLut"/> pre-tabulates E(μ, α) and E_avg(α)
    // for the white single-scatter GGX BRDF. The compensation lobe
    //   f_ms(V, L; F₀, α) = F_ms · (1 − E(μo, α)) · (1 − E(μi, α))
    //                       / (π · (1 − E_avg(α)))
    // with
    //   F̄    = F₀ + (1 − F₀) / 21           (Schlick-averaged F over μ)
    //   F_ms = F̄² · E_avg / (1 − F̄·(1 − E_avg))  (per-channel)
    // is added to the single-scatter lobe in Evaluate/EvaluateDirect and
    // sampled by <see cref="ScatterMultiscatter"/>. For a white metal this
    // makes the directional-hemispherical albedo tend to 1 at all α; for
    // coloured metals it retains hue at high roughness (avoiding the
    // classic "rough gold turns brown" artefact).
    //
    // Isotropic α — the LUT is built at αx = αy and we reduce anisotropic
    // surfaces to α_iso = √(αx·αy). Compensation is slightly less precise
    // under strong anisotropy, which is the standard trade-off accepted by
    // Arnold, Cycles and Renderman.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Per-channel multi-scatter BRDF value (without the cosine factor),
    /// divided by the compensation lobe's cosine PDF — i.e. the quantity
    /// returned as scatter attenuation or added to Evaluate after scaling.
    /// </summary>
    private static Vector3 MultiscatterWeight(Vector3 F0, in ShadingParams sp,
                                              float NdotV, float NdotL)
    {
        float alphaIso = MathF.Sqrt(sp.AlphaX * sp.AlphaY);
        float eOut  = EnergyCompensationLut.SampleE(NdotV, alphaIso);
        float eIn   = EnergyCompensationLut.SampleE(NdotL, alphaIso);
        float eAvg  = EnergyCompensationLut.SampleEAvg(alphaIso);
        float one_minus_eAvg = MathF.Max(1f - eAvg, 1e-6f);

        Vector3 fAvg = F0 + (Vector3.One - F0) * (1f / 21f);
        Vector3 denom = Vector3.One - fAvg * (1f - eAvg);
        // Componentwise floor — denom vanishes when F̄ = 1 AND E_avg = 1,
        // which never co-occurs in practice but the guard is cheap.
        denom = new Vector3(
            MathF.Max(denom.X, 1e-6f),
            MathF.Max(denom.Y, 1e-6f),
            MathF.Max(denom.Z, 1e-6f));
        Vector3 fMs = fAvg * fAvg * eAvg / denom;

        return fMs * ((1f - eOut) * (1f - eIn) / one_minus_eAvg);
    }

    /// <summary>
    /// Multi-scatter BRDF value f_ms(V, L) (no cosine), as added into
    /// <see cref="Evaluate"/>, <see cref="EvaluateDirect"/> and the analytic
    /// PDF path. Shares <see cref="MultiscatterWeight"/> and divides by π
    /// to recover the BRDF form (weight/π == f_ms).
    /// </summary>
    private static Vector3 EvaluateMultiscatter(Vector3 F0, in ShadingParams sp,
                                                float NdotV, float NdotL)
        => MultiscatterWeight(F0, sp, NdotV, NdotL) * (1f / MathF.PI);

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
    /// Fresnel evaluator that picks between Schlick (no thin film) and the
    /// Belcour-Barla 2017 iridescent Fresnel when ThinFilmThicknessNm > 0.
    /// All specular and metallic Fresnel evaluations in the BSDF route
    /// through this helper so iridescence applies uniformly across the
    /// reflection lobes (including the multi-scatter compensation, which
    /// uses the same F₀ basis).
    ///
    /// Clearcoat and the back-of-glass Fresnel intentionally bypass the
    /// thin-film path: the coat sits on top of the iridescent layer and
    /// has its own dielectric Fresnel; transmissive Fresnel needs the full
    /// wave-optics treatment for iridescent dielectrics, which exceeds
    /// the scope of an opaque-substrate Belcour-Barla projection.
    /// </summary>
    private static Vector3 EvalFresnel(float cosTheta, Vector3 f0, in ShadingParams sp)
    {
        if (sp.ThinFilmThicknessNm <= 0f)
            return FresnelSchlick(cosTheta, f0);
        return ThinFilm.Evaluate(cosTheta, sp.ThinFilmIor, sp.ThinFilmThicknessNm, f0);
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
