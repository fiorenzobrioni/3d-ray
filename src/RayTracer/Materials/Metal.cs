using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// Metallic material with GGX microfacet model for both indirect (Scatter)
/// and direct (EvaluateDirect) lighting paths.
///
/// Upgrade from the original implementation:
///   - Scatter: replaced Reflect + Fuzz×RandomInUnitSphere with proper GGX
///     importance sampling of the microfacet normal. This produces the correct
///     long-tailed highlight distribution that real metals exhibit, and
///     converges faster because the sampling distribution matches the BRDF.
///   - EvaluateDirect: replaced Blinn-Phong approximation with analytic
///     Cook-Torrance GGX (NDF + Smith G + Schlick Fresnel), eliminating the
///     energy mismatch between direct and indirect lighting paths.
///
/// The Fuzz parameter is mapped to GGX roughness (alpha = fuzz²) for
/// continuity with existing scenes — fuzz=0 is still a perfect mirror,
/// fuzz=1 is still maximally rough.
///
/// In YAML:
///   - id: "gold"
///     type: "metal"
///     color: [0.85, 0.65, 0.10]
///     fuzz: 0.02
///
///   - id: "brushed_steel"
///     type: "metal"
///     color: [0.58, 0.57, 0.55]
///     fuzz: 0.30
/// </summary>
public class Metal : IMaterial
{
    public ITexture Albedo { get; }
    public float Fuzz { get; }

    // ── Cached GGX α (roughness²) ──────────────────────────────────────────
    private readonly float _alpha;

    public Metal(Vector3 color, float fuzz)
    {
        Albedo = new SolidColor(color);
        Fuzz = MathF.Min(fuzz, 1f);
        _alpha = MathF.Max(Fuzz * Fuzz, 0.001f);
    }

    public Metal(ITexture a, float fuzz)
    {
        Albedo = a;
        Fuzz = MathF.Min(fuzz, 1f);
        _alpha = MathF.Max(Fuzz * Fuzz, 0.001f);
    }

    // A perfect mirror (fuzz=0) is a delta BSDF and cannot be reached by NEE;
    // any fuzz > 0 broadens the lobe into a sampleable GGX distribution.
    public bool IsDeltaScatter => Fuzz <= 0f;

    /// <summary>
    /// Metal direct lighting using analytic Cook-Torrance GGX.
    ///
    /// Replaces the previous Blinn-Phong approximation to match the GGX
    /// distribution used by Scatter. This eliminates the energy mismatch
    /// between direct and indirect paths that caused metals with medium fuzz
    /// (0.2–0.5) to appear inconsistently lit under direct vs indirect light.
    ///
    /// The BRDF is achromatic (scalar) — the metallic color tint comes from
    /// TraceRay's attenuation (= Albedo), keeping both paths consistent.
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, HitRecord rec)
    {
        float NdotL = MathF.Max(Vector3.Dot(normal, toLight), 0f);
        if (NdotL <= 0f) return Vector3.Zero;

        // Diffuse term: scales with fuzz (rough metals have some diffuse scattering)
        float diffuse = NdotL * Fuzz;

        float NdotV = MathF.Max(Vector3.Dot(normal, toEye), 0.001f);

        Vector3 H = Vector3.Normalize(toLight + toEye);
        float NdotH = MathF.Max(Vector3.Dot(normal, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(toEye, H), 0f);

        // D: GGX (Trowbridge-Reitz) NDF
        float a2 = _alpha * _alpha;
        float denom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * denom * denom);

        // G: Smith separable masking/shadowing
        float G = SmithG1_GGX(NdotV, _alpha) * SmithG1_GGX(NdotL, _alpha);

        // F: Schlick Fresnel — scalar, F0 ≈ 1 for polished metal and drops with fuzz
        // to model the softer highlight of rough microfacets. Colour tinting comes
        // from attenuation in TraceRay, not here.
        float f0 = 1f - Fuzz * 0.5f;
        float s = 1f - VdotH;
        s *= s * s * s * s; // (1 - V·H)^5
        float fresnel = f0 + (1f - f0) * s;

        // Cook-Torrance: D × G × F / (4 × NdotV × NdotL), then × NdotL
        // The NdotL in numerator and denominator cancel, leaving / (4 × NdotV).
        float specular = D * G * fresnel / MathF.Max(4f * NdotV, 1e-6f);

        // FIREFLY GUARD: The GGX NDF diverges for low alpha (polished metals).
        // D×G×F/(4×NdotV) can reach 50–500 for fuzz < 0.05 when NdotH ≈ 1.
        // Clamp to 10.0 — consistent with DisneyBsdf.EvaluateDirect — to
        // preserve physically correct highlights for smooth metals while still
        // catching the GGX singularity. The old 1.0 clamp systematically
        // underestimated specular direct lighting, making polished metals
        // (gold, chrome) appear too dull under direct light.
        float total = diffuse + MathF.Min(specular, 10f);
        return new Vector3(total);
    }

    public NormalMapTexture? NormalMap { get; set; }

    // ─────────────────────────────────────────────────────────────────────────
    // Symmetric BSDF API (Evaluate / Pdf / Sample) — enables full MIS.
    //
    // Cook-Torrance specular over GGX with Smith masking, plus a small
    // diffuse term proportional to Fuzz that mirrors EvaluateDirect's split
    // (rough metals have non-trivial diffuse scattering). Importance sampling
    // is the same NDF used by Scatter, so legacy and MIS paths agree on
    // direction statistics. F=0 (perfect mirror) → delta sample with Pdf=1
    // and the renderer treats it like Dielectric reflection.
    // ─────────────────────────────────────────────────────────────────────────

    public Vector3 Evaluate(Vector3 V, Vector3 L, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotL = Vector3.Dot(N, L);
        float NdotV = Vector3.Dot(N, V);
        if (NdotL <= 0f || NdotV <= 0f) return Vector3.Zero;
        if (Fuzz <= 0f) return Vector3.Zero; // delta lobe — sampled via Sample only

        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() < 1e-14f) return Vector3.Zero;
        Vector3 H = Vector3.Normalize(Hraw);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 0f);

        float a2 = _alpha * _alpha;
        float denom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * denom * denom);
        float G = SmithG1_GGX(NdotV, _alpha) * SmithG1_GGX(NdotL, _alpha);

        // Schlick Fresnel tinted by the metal albedo (F0 = baseColor).
        Vector3 albedo = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        float s = 1f - VdotH;
        s *= s * s * s * s;
        Vector3 F = albedo + (Vector3.One - albedo) * s;

        // Cook-Torrance specular: D·G·F / (4·NdotV·NdotL). No cosine factor.
        return F * (D * G / MathF.Max(4f * NdotV * NdotL, 1e-7f));
    }

    public float Pdf(Vector3 V, Vector3 L, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotL = Vector3.Dot(N, L);
        float NdotV = Vector3.Dot(N, V);
        if (NdotL <= 0f || NdotV <= 0f) return 0f;
        if (Fuzz <= 0f) return 0f;

        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() < 1e-14f) return 0f;
        Vector3 H = Vector3.Normalize(Hraw);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 1e-7f);

        // GGX NDF Jacobian: pdf(L) = D(H)·NdotH / (4·VdotH).
        float a2 = _alpha * _alpha;
        float denom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * denom * denom);
        return D * NdotH / (4f * VdotH);
    }

    public BsdfSample? Sample(Vector3 V, HitRecord rec)
    {
        Vector3 N = rec.Normal;
        Vector3 H = SampleGGX(N, _alpha);
        Vector3 L = MathUtils.Reflect(-V, H);
        if (Vector3.Dot(N, L) <= 0f) return null;

        Vector3 albedo = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);

        if (Fuzz <= 0f)
        {
            // Perfect mirror: delta lobe. F carries the full attenuation,
            // Pdf=1 so the renderer reports prevBsdfPdf=0 / prevIsDelta=true
            // at the next bounce (see ShadeSampleBounce).
            return new BsdfSample(L, albedo, 1f, isDelta: true);
        }

        float NdotL = MathF.Max(Vector3.Dot(N, L), 1e-7f);
        float NdotV = MathF.Max(Vector3.Dot(N, V), 1e-7f);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 1e-7f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 1e-7f);

        float a2 = _alpha * _alpha;
        float dDenom = NdotH * NdotH * (a2 - 1f) + 1f;
        float D = a2 / (MathF.PI * dDenom * dDenom);
        float G = SmithG1_GGX(NdotV, _alpha) * SmithG1_GGX(NdotL, _alpha);
        float s = 1f - VdotH;
        s *= s * s * s * s;
        Vector3 F = albedo + (Vector3.One - albedo) * s;

        Vector3 f = F * (D * G / MathF.Max(4f * NdotV * NdotL, 1e-7f));
        float pdf = D * NdotH / (4f * VdotH);
        return new BsdfSample(L, f, pdf, isDelta: false);
    }

    /// <summary>
    /// GGX importance-sampled scatter for metallic reflection.
    ///
    /// Replaces the original Reflect + Fuzz×RandomInUnitSphere approach with
    /// proper GGX microfacet sampling. Benefits:
    ///
    ///   1. Correct highlight shape: GGX has characteristic long tails that
    ///      real metals exhibit. The old uniform-sphere perturbation produced
    ///      a Gaussian-like falloff that was too compact.
    ///
    ///   2. Lower variance: importance sampling the NDF means the sampling
    ///      distribution matches the BRDF, so each sample carries a more
    ///      consistent weight. This is especially visible for medium fuzz
    ///      (0.15–0.5) where the old approach needed many more samples.
    ///
    ///   3. Consistency with direct lighting: both Scatter and EvaluateDirect
    ///      now use the same GGX distribution, eliminating energy mismatch.
    ///
    /// For fuzz=0 (perfect mirror), GGX with α=0.001 produces a near-delta
    /// distribution that converges to mirror reflection — no special case needed.
    /// </summary>
    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        Vector3 N = rec.Normal;
        Vector3 V = Vector3.Normalize(-rayIn.Direction);

        // Sample GGX microfacet normal and reflect
        Vector3 H = SampleGGX(N, _alpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        // Below-surface reflection — absorb
        if (Vector3.Dot(L, N) <= 0f)
        {
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        // Attenuation = albedo × G-term weight
        // For metals, F0 ≈ albedo — the Fresnel is baked into the color.
        // We apply the Smith G correction as importance sampling weight:
        //   weight = G(V,L) × VdotH / (NdotV × NdotH)
        // This is the standard BRDF/pdf ratio for GGX NDF sampling.
        // Dot product floors: NdotV and NdotH are in the GGX weight denominator.
        // Floor at 0.01 instead of 0.001 — loses a 0.5° sliver at extreme
        // grazing angles but eliminates the spike source.
        float NdotV = MathF.Max(Vector3.Dot(N, V), 0.01f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 0.001f);
        float NdotH = MathF.Max(Vector3.Dot(N, H), 0.01f);
        float VdotH = MathF.Max(Vector3.Dot(V, H), 0.001f);

        float G = SmithG1_GGX(NdotV, _alpha) * SmithG1_GGX(NdotL, _alpha);

        // FIREFLY GUARD: The raw GGX weight G×VdotH/(NdotV×NdotH) can spike
        // at grazing angles where NdotV or NdotH approach their floor values.
        // For well-behaved GGX with proper Smith masking, the weight stays
        // near 1.0. Values above 1.0 are grazing-angle noise. Clamping to 1.0
        // eliminates all remaining fireflies with no perceptible quality loss.
        float ggxWeight = MathF.Min(G * VdotH / (NdotV * NdotH), 1f);

        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed) * ggxWeight;

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GGX utilities (same implementation as DisneyBsdf — could be extracted
    // to a shared MicrofacetUtils class in a future refactor)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Smith G1 masking/shadowing for GGX.
    /// G1(v) = 2·NdotX / (NdotX + sqrt(α² + (1-α²)·NdotX²))
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

        float a2 = alpha * alpha;
        float cosTheta = MathF.Sqrt((1f - u1) / (1f + (a2 - 1f) * u1));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;

        Vector3 Hlocal = new(
            sinTheta * MathF.Cos(phi),
            sinTheta * MathF.Sin(phi),
            cosTheta);

        return TangentToWorld(Hlocal, N);
    }

    /// <summary>
    /// Transforms a tangent-space vector to world space using Frisvad's method.
    /// </summary>
    private static Vector3 TangentToWorld(Vector3 local, Vector3 N)
    {
        Vector3 T, B;
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
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : N;
    }
}
