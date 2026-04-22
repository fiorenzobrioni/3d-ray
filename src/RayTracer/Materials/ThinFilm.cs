using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// Belcour-Barla 2017 "A Practical Extension to Microfacet Theory for the
/// Modeling of Varying Iridescence" — production-grade thin-film
/// interference for dielectric/metallic surfaces, evaluated per-RGB
/// channel via the Fourier-domain sensitivity-function projection that
/// short-circuits the full spectral integral.
///
/// Implementation follows the Khronos glTF KHR_materials_iridescence
/// reference shader, which is the same algorithm shipped by Filament,
/// Cycles, Arnold's standard_surface "thin_film_*" parameters and
/// USDPreviewSurface. Inputs are colourimetric Rec.709 RGB; outputs are
/// the iridescent reflectance that replaces the Schlick Fresnel term in
/// the host BSDF.
///
/// Why Belcour-Barla and not e.g. Hosek-Wilkie or per-wavelength dispersion:
/// (a) closed-form RGB output — no spectral resampling pass; (b) C∞ in
/// thickness, so animated thickness fields don't strobe; (c) handles the
/// thickness → 0 limit by smoothly degenerating to plain Fresnel through
/// a `mix(outsideIor, filmIor, smoothstep(0, 30 nm, thickness))` blend
/// — the only known formula that doesn't pop at zero.
/// </summary>
internal static class ThinFilm
{
    /// <summary>
    /// Evaluates the thin-film iridescent Fresnel for unpolarised light
    /// at the given microfacet half-angle cosine.
    /// </summary>
    /// <param name="cosTheta1">cos(θ) at the air→film interface (= VdotH).</param>
    /// <param name="filmIor">Film index of refraction (η₂).</param>
    /// <param name="filmThicknessNm">Film thickness in nanometres.</param>
    /// <param name="baseF0">Substrate F0 at normal incidence (R, G, B).
    /// For metals this is baseColor; for dielectrics it's the IOR-derived
    /// reflectance optionally tinted by specular_tint.</param>
    /// <returns>Iridescent reflectance at the requested incidence,
    /// replacing the Schlick Fresnel evaluation in the host BSDF.</returns>
    public static Vector3 Evaluate(float cosTheta1, float filmIor, float filmThicknessNm, Vector3 baseF0)
    {
        // Air is the outside medium for every supported viewing scenario;
        // entering a transmissive medium would require a stack-aware
        // outsideIor and is not handled here.
        const float outsideIor = 1f;

        // Smooth degeneracy at thickness → 0: blend the film IOR back to
        // outside IOR over the first 30 nm so a zero-thickness film
        // collapses cleanly to the substrate's plain Fresnel rather than
        // producing a 0-frequency Airy fringe (which would read as a
        // tinted highlight even when the artist set thickness = 0).
        float thickness = MathF.Max(filmThicknessNm, 0f);
        float t = SmoothStep(0f, 30f, thickness);
        float eta2 = MathF.Max(outsideIor + (filmIor - outsideIor) * t, 1.0001f);

        // Snell to the film interior. cosTheta1 is clamped to [0, 1] so
        // the back-hemisphere case (which the caller already excludes
        // for reflection lobes) stays defined.
        float c1 = Math.Clamp(cosTheta1, 0f, 1f);
        float etaRatio = outsideIor / eta2;
        float sinTheta2Sq = etaRatio * etaRatio * (1f - c1 * c1);
        if (sinTheta2Sq >= 1f)
        {
            // Total internal reflection within the film — every
            // wavelength reflects, and the layered transmission terms
            // collapse to 1.
            return Vector3.One;
        }
        float cosTheta2 = MathF.Sqrt(1f - sinTheta2Sq);

        // First interface (air→film): scalar Schlick on the Fresnel0
        // derived from the IOR contrast.
        float r0_12 = IorToFresnel0(eta2, outsideIor);
        float r12   = FresnelSchlick(r0_12, c1);
        float r21   = r12;
        float t121  = 1f - r12;
        // Phase shift on reflection at the top interface: π if the film is
        // optically less dense than the surrounding medium (here always
        // false for filmIor ≥ outsideIor), so phi12 = 0 in the supported
        // regime. Kept explicit in case future work allows immersion.
        float phi12 = (eta2 < outsideIor) ? MathF.PI : 0f;
        float phi21 = MathF.PI - phi12;

        // Second interface (film→base): per-channel Schlick on the
        // substrate F0. baseF0 ∈ [0, 0.9999) — the upper clamp keeps the
        // Fresnel0→IOR inversion finite.
        Vector3 baseF0c = Vector3.Clamp(baseF0,
            new Vector3(0f), new Vector3(0.9999f));
        Vector3 baseIor = Fresnel0ToIor(baseF0c);

        Vector3 r0_23 = IorToFresnel0(baseIor, new Vector3(eta2));
        Vector3 r23 = new(
            FresnelSchlick(r0_23.X, cosTheta2),
            FresnelSchlick(r0_23.Y, cosTheta2),
            FresnelSchlick(r0_23.Z, cosTheta2));

        // Per-channel phase shift at the bottom interface: π wherever the
        // base IOR is below the film IOR (i.e. wave reflects off a less
        // dense medium). Drives the wavelength-dependent colour split.
        Vector3 phi23 = new(
            baseIor.X < eta2 ? MathF.PI : 0f,
            baseIor.Y < eta2 ? MathF.PI : 0f,
            baseIor.Z < eta2 ? MathF.PI : 0f);

        // Optical Path Difference inside the film, in nanometres. The 2×
        // accounts for the round trip down and back up through the film.
        float OPD = 2f * eta2 * thickness * cosTheta2;
        Vector3 phi = new Vector3(phi21) + phi23;

        // Compound terms for the Airy summation. All three components must
        // stay in the open interval to keep r123 well-defined and avoid
        // dividing by ~0 in the geometric series below.
        Vector3 R123 = Vector3.Clamp(new Vector3(r12) * r23,
            new Vector3(1e-5f), new Vector3(0.9999f));
        Vector3 r123 = new(MathF.Sqrt(R123.X), MathF.Sqrt(R123.Y), MathF.Sqrt(R123.Z));

        // Constant (DC) component of the geometric series — survives even
        // when sensitivity-function coefficients vanish.
        Vector3 Rs = (t121 * t121) * r23 / (Vector3.One - R123);
        Vector3 C0 = new Vector3(r12) + Rs;
        Vector3 I = C0;

        // Two harmonic terms — the Belcour-Barla paper shows that
        // truncating after m = 2 holds the L²-norm error below 1% across
        // the visible band, well under perceptual JND. Each term is
        // projected to RGB by the spectral sensitivity function.
        Vector3 Cm = Rs - new Vector3(t121);
        for (int m = 1; m <= 2; m++)
        {
            Cm *= r123;
            Vector3 Sm = 2f * EvalSensitivity(m * OPD, m * phi);
            I += Cm * Sm;
        }

        // The geometric series can produce small negative values where the
        // sensitivity function dips below zero — those represent fringe
        // attenuation, but a negative "Fresnel" would feed energy into
        // the Cook-Torrance numerator backwards. Floor at zero.
        return Vector3.Max(I, Vector3.Zero);
    }

    // ────────────────────────────────────────────────────────────────────
    // Belcour-Barla spectral-sensitivity function. Three Gaussian
    // primaries fitted to the CIE 1931 2° XYZ colour-matching curves,
    // multiplied by the Fourier kernel cos(2π · OPD · ν + φ) and
    // analytically integrated against a Gaussian, yielding a closed-form
    // RGB projection of the spectral reflectance. Coefficients are taken
    // verbatim from the published supplement and the Khronos reference.
    // ────────────────────────────────────────────────────────────────────
    private static Vector3 EvalSensitivity(float OPDNm, Vector3 shift)
    {
        // OPD is in nm; convert wavelength frequency to cycles per nm
        // (= 1/wavelength), so the Fourier kernel reads cos(2π · OPD/λ).
        // The sensitivity-function constants are expressed in 1/m, so
        // OPD must arrive in metres. 1 nm = 1e-9 m.
        float phase = 2f * MathF.PI * OPDNm * 1e-9f;

        Vector3 val = new(5.4856e-13f, 4.4201e-13f, 5.2481e-13f);
        Vector3 pos = new(1.6810e+06f, 1.7953e+06f, 2.2084e+06f);
        Vector3 var_ = new(4.3278e+09f, 9.3046e+09f, 6.6121e+09f);

        Vector3 sqrt2piVar = new(
            MathF.Sqrt(2f * MathF.PI * var_.X),
            MathF.Sqrt(2f * MathF.PI * var_.Y),
            MathF.Sqrt(2f * MathF.PI * var_.Z));
        Vector3 cosTerm = new(
            MathF.Cos(pos.X * phase + shift.X),
            MathF.Cos(pos.Y * phase + shift.Y),
            MathF.Cos(pos.Z * phase + shift.Z));
        float phaseSq = phase * phase;
        Vector3 expTerm = new(
            MathF.Exp(-phaseSq * var_.X),
            MathF.Exp(-phaseSq * var_.Y),
            MathF.Exp(-phaseSq * var_.Z));

        Vector3 xyz = val * sqrt2piVar * cosTerm * expTerm;
        // Second-lobe correction on the X (red) channel — the CIE x-bar
        // curve has a small secondary peak near 450 nm that one Gaussian
        // can't model.
        xyz.X += 9.7470e-14f * MathF.Sqrt(2f * MathF.PI * 4.5282e+09f)
              * MathF.Cos(2.2399e+06f * phase + shift.X)
              * MathF.Exp(-phaseSq * 4.5282e+09f);
        xyz /= 1.0685e-7f;

        // CIE XYZ → linear Rec.709 RGB. Standard sRGB primaries matrix.
        return XyzToRec709(xyz);
    }

    private static Vector3 XyzToRec709(Vector3 xyz)
    {
        // sRGB / Rec.709 D65 reference white.
        float r =  3.2404542f * xyz.X - 1.5371385f * xyz.Y - 0.4985314f * xyz.Z;
        float g = -0.9692660f * xyz.X + 1.8760108f * xyz.Y + 0.0415560f * xyz.Z;
        float b =  0.0556434f * xyz.X - 0.2040259f * xyz.Y + 1.0572252f * xyz.Z;
        return new Vector3(r, g, b);
    }

    private static float IorToFresnel0(float eta1, float eta2)
    {
        float r = (eta1 - eta2) / (eta1 + eta2);
        return r * r;
    }

    private static Vector3 IorToFresnel0(Vector3 eta1, Vector3 eta2)
    {
        Vector3 r = (eta1 - eta2) / (eta1 + eta2);
        return r * r;
    }

    /// <summary>
    /// Inverts F0 = ((η-1)/(η+1))² for η > 1. F0 must be clamped strictly
    /// below 1 by the caller; F0 = 1 corresponds to a perfect mirror
    /// (η = ∞) which has no finite IOR representation.
    /// </summary>
    private static Vector3 Fresnel0ToIor(Vector3 f0)
    {
        Vector3 sqrtF0 = new(MathF.Sqrt(f0.X), MathF.Sqrt(f0.Y), MathF.Sqrt(f0.Z));
        return (Vector3.One + sqrtF0) / (Vector3.One - sqrtF0);
    }

    private static float FresnelSchlick(float f0, float cosTheta)
    {
        float x = Math.Clamp(1f - cosTheta, 0f, 1f);
        float x5 = x * x; x5 *= x5 * x;
        return f0 + (1f - f0) * x5;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
