using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// Glass and other clear dielectrics. Smooth (roughness 0): a pair of delta
/// lobes, Fresnel reflection and Snell refraction. Rough: the microfacet BSDF
/// of Walter et al. 2007 (frosted glass), GGX with height-correlated Smith,
/// reflection and transmission evaluated, sampled from the visible normals
/// and MIS-weighted like any other glossy lobe (PIANO-REVIEW.md, Phase 5).
/// The colour tints both lobes; refraction carries the (η_i/η_t)² radiance
/// factor (§3.35).
/// </summary>
public sealed class Dielectric : IMaterial
{
    public float RefractionIndex { get; }
    public ITexture Albedo { get; }
    public float Roughness { get; }

    private readonly float _alpha;

    public Dielectric(float refractionIndex) : this(refractionIndex, new SolidColor(Vector3.One)) { }

    public Dielectric(float refractionIndex, ITexture albedo, float roughness = 0f)
    {
        RefractionIndex = refractionIndex;
        Albedo = albedo;
        Roughness = Math.Clamp(roughness, 0f, 1f);
        _alpha = MathF.Max(Roughness * Roughness, 1e-4f);
    }

    private bool IsSmooth => Roughness <= 0f;

    public bool HasTransmission(in HitRecord rec) => !IsSmooth;

    // Smooth glass is a pair of delta lobes: NEE cannot reach it, and the
    // renderer must keep the emission weight across the bounce.
    public bool IsDeltaScatter => IsSmooth;

    /// <summary>Denoiser/AOV albedo guide: the glass tint. Used only when a
    /// path ends while still inside the specular chain (depth exhaustion);
    /// normally the chain's tint accumulates via the delta-bounce attenuation
    /// and the albedo comes from the first non-delta surface beyond.</summary>
    public Vector3 AovAlbedo(in HitRecord rec) => Albedo.Value(in rec);

    public NormalMapTexture? NormalMap { get; set; }
    public BumpMapTexture? BumpMap { get; set; }
    public MaterialDisplacement? Displacement { get; set; }

    // Transparent shadow rays: report (1 - Fresnel) · albedo as the per-hit
    // straight-through transmission. The shadow ray is not refracted — Snell-
    // bent light contributes to focused lighting only through the sampled
    // bounce (BDPT/VCM connect and merge around it). Each
    // dielectric interface attenuates by 1−F; a glass sphere crosses two
    // interfaces, so the receiver still sees the proper Fresnel-squared
    // shadowing at grazing angles.
    public Vector3 ShadowTransmittance(Vector3 wi, in HitRecord rec)
    {
        float cosTheta = MathF.Min(MathF.Abs(Vector3.Dot(wi, rec.Normal)), 1f);
        // Straight-through shadow rays are not refracted, so the Fresnel is
        // read from the less dense side. Using eta = RefractionIndex on back-face hits
        // would spuriously trigger TIR past the critical angle and block
        // every soft shadow steeper than ~37° through a flat slab.
        float eta = 1f / RefractionIndex;
        float fr = MathUtils.FresnelDielectric(cosTheta, eta);
        Vector3 albedo = Albedo.Value(in rec);
        return (1f - fr) * albedo;
    }

    // IOR 1: no Fresnel reflection, no bending, only the tint (and no
    // microfacet scattering, whatever the roughness: the interface is invisible).
    public bool TransmitsStraight(in HitRecord rec) => MathF.Abs(RefractionIndex - 1f) < 1e-4f;

    // A solid dielectric enters/exits a nested-IOR medium on refraction, so it
    // participates in the renderer's IorStack (relative-IOR tracking).
    public bool TryGetDielectricIor(in HitRecord rec, out float ior)
    {
        ior = RefractionIndex;
        return true;
    }

    /// <summary>η_i/η_t for a ray arriving on the side the shading normal faces.</summary>
    private float RelativeEta(in HitRecord rec) => rec.RelativeEta > 0f
        ? rec.RelativeEta
        : (rec.FrontFace ? (1f / RefractionIndex) : RefractionIndex);

    // ── Rough dielectric (Walter et al. 2007, as in PBRT-v4) ─────────────────
    // V is on the side the normal faces (the incoming side), η = 1/ri the
    // ratio below/above. Reflection half-vector V + L, transmission η L + V.

    private Vector3 HalfVector(Vector3 V, Vector3 L, Vector3 N, float eta, out bool reflect)
    {
        reflect = Vector3.Dot(N, L) > 0f;
        Vector3 h = reflect ? V + L : V + eta * L;
        if (h.LengthSquared() < 1e-14f) return Vector3.Zero;
        h = Vector3.Normalize(h);
        return Vector3.Dot(h, N) < 0f ? -h : h;
    }

    public Vector3 Evaluate(Vector3 V, Vector3 L, in HitRecord rec)
    {
        if (IsSmooth) return Vector3.Zero;
        Vector3 N = rec.Normal;
        float cosV = Vector3.Dot(N, V), cosL = Vector3.Dot(N, L);
        if (cosV <= 0f || cosL == 0f) return Vector3.Zero;
        float ri = RelativeEta(in rec), eta = 1f / ri;
        Vector3 h = HalfVector(V, L, N, eta, out bool reflect);
        float vh = Vector3.Dot(V, h), lh = Vector3.Dot(L, h);
        // Discard microfacets seen from behind by either direction.
        if (h == Vector3.Zero || vh <= 0f || lh * cosL <= 0f) return Vector3.Zero;

        Microfacet.BuildTangentFrame(N, out Vector3 T, out Vector3 B);
        Vector3 Vl = Local(V, T, B, N), Ll = Local(L, T, B, N), Hl = Local(h, T, B, N);
        Ll.Z = MathF.Abs(Ll.Z);
        float D = Microfacet.DGgxAniso(Hl, _alpha, _alpha);
        float G = Microfacet.G2GgxAniso(Vl, Ll, _alpha, _alpha);
        float F = MathUtils.FresnelDielectric(vh, ri);
        Vector3 tint = Albedo.Value(in rec);

        if (reflect)
            return tint * (F * D * G / (4f * cosV * cosL));

        float denom = lh + vh / eta;
        float ft = (1f - F) * D * G * MathF.Abs(lh * vh / (denom * denom * cosL * cosV));
        return tint * (ft * ri * ri);    // radiance: (η_i/η_t)²
    }

    public float Pdf(Vector3 V, Vector3 L, in HitRecord rec)
    {
        if (IsSmooth) return 0f;
        Vector3 N = rec.Normal;
        float cosV = Vector3.Dot(N, V), cosL = Vector3.Dot(N, L);
        if (cosV <= 0f || cosL == 0f) return 0f;
        float ri = RelativeEta(in rec), eta = 1f / ri;
        Vector3 h = HalfVector(V, L, N, eta, out bool reflect);
        float vh = Vector3.Dot(V, h), lh = Vector3.Dot(L, h);
        if (h == Vector3.Zero || vh <= 0f || lh * cosL <= 0f) return 0f;

        Microfacet.BuildTangentFrame(N, out Vector3 T, out Vector3 B);
        float D = Microfacet.DGgxAniso(Local(h, T, B, N), _alpha, _alpha);
        float g1 = Microfacet.G1GgxAniso(Local(V, T, B, N), _alpha, _alpha);
        float pdfH = g1 * vh * D / cosV;                // visible normals
        float F = MathUtils.FresnelDielectric(vh, ri);
        if (reflect)
            return F * pdfH / (4f * vh);
        float denom = lh + vh / eta;
        return (1f - F) * pdfH * MathF.Abs(lh) / (denom * denom);
    }

    public BsdfSample? Sample(Vector3 V, in HitRecord rec)
    {
        if (IsSmooth) return SampleSmooth(V, in rec);
        Vector3 N = rec.Normal;
        float cosV = Vector3.Dot(N, V);
        if (cosV <= 0f) return null;
        float ri = RelativeEta(in rec);

        Microfacet.BuildTangentFrame(N, out Vector3 T, out Vector3 B);
        var (u1, u2) = MathUtils.Random2D();
        Vector3 Hl = Microfacet.SampleGgxVndfAniso(Local(V, T, B, N), _alpha, _alpha, u1, u2);
        Vector3 h = Hl.X * T + Hl.Y * B + Hl.Z * N;
        float vh = Vector3.Dot(V, h);
        if (vh <= 0f) return null;

        float F = MathUtils.FresnelDielectric(vh, ri);
        Vector3 L = Vector3.Zero;
        if (MathUtils.RandomFloat() >= F)
            L = MathUtils.Refract(-V, h, ri);
        bool refracted = !MathUtils.NearZero(L);
        if (!refracted)
            L = MathUtils.Reflect(-V, h);
        L = Vector3.Normalize(L);
        float cosL = Vector3.Dot(N, L);
        if (cosL == 0f || (cosL > 0f) == refracted) return null;    // left on the wrong side

        float pdf = Pdf(V, L, in rec);
        if (pdf <= 0f) return null;
        MediumTransition transition = refracted
            ? (rec.FrontFace ? MediumTransition.Enter : MediumTransition.Exit)
            : MediumTransition.None;
        return new BsdfSample(L, Evaluate(V, L, in rec), pdf, isDelta: false, transition: transition,
                              lobe: refracted ? BsdfLobe.Transmission : BsdfLobe.Glossy,
                              eta: refracted ? 1f / ri : 1f);
    }

    /// <summary>
    /// Smooth glass: Fresnel picks reflection or refraction, each a delta lobe
    /// whose weight is the tint (and the (η_i/η_t)² radiance factor on
    /// refraction, §3.35). Refraction enters or leaves the object's medium.
    /// </summary>
    private BsdfSample? SampleSmooth(Vector3 V, in HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float cosTheta = MathF.Min(Vector3.Dot(V, N), 1f);
        if (cosTheta <= 0f) return null;
        float ri = RelativeEta(in rec);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        Vector3 tint = Albedo.Value(in rec);

        // Exact unpolarised Fresnel (§3.24); no random number under TIR.
        Vector3 L = Vector3.Zero;
        if (ri * sinTheta <= 1f && MathUtils.FresnelDielectric(cosTheta, ri) <= MathUtils.RandomFloat())
            L = MathUtils.Refract(-V, N, ri);
        if (MathUtils.NearZero(L))
            return new BsdfSample(MathUtils.Reflect(-V, N), tint, 1f, isDelta: true);

        var transition = rec.FrontFace ? MediumTransition.Enter : MediumTransition.Exit;
        return new BsdfSample(Vector3.Normalize(L), tint * (ri * ri), 1f, isDelta: true, transition: transition,
                              lobe: BsdfLobe.Transmission, eta: 1f / ri);
    }

    private static Vector3 Local(Vector3 v, Vector3 t, Vector3 b, Vector3 n) =>
        new(Vector3.Dot(v, t), Vector3.Dot(v, b), Vector3.Dot(v, n));

}
