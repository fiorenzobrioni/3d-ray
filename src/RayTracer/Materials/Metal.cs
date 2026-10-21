using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// Conductor (PIANO-REVIEW.md, Phase 5): GGX microfacet reflection with
/// height-correlated Smith masking-shadowing, visible-normal sampling, a
/// conductor Fresnel term and the Kulla-Conty multiple-scattering lobe.
///
/// <para>Fresnel: either a measured complex IOR (<see cref="Ior"/>, from a
/// <c>preset</c> or explicit <c>eta</c>/<c>k</c>), or the F82 model with the
/// normal-incidence colour <see cref="Albedo"/> and the grazing tint
/// <see cref="EdgeColor"/> (white = Schlick).</para>
///
/// <para>Single scattering between microfacets loses the light that bounces
/// more than once: a white metal at roughness 1 reflected only 32-51% of it.
/// The Kulla-Conty lobe adds it back, f_ms = F_ms (1 − E(μo)) (1 − E(μi)) /
/// (π (1 − E_avg)), so a white metal is white at every roughness. The
/// previous version had instead a Lambertian term in the direct lighting only
/// (PIANO-REVIEW.md §3.7): +37% of light against Mitsuba, and two different
/// materials for the two halves of the estimator.</para>
///
/// <para><see cref="Fuzz"/> is the roughness, α = fuzz²; 0 is a perfect mirror.</para>
/// </summary>
public sealed class Metal : IMaterial
{
    public ITexture Albedo { get; }
    public ITexture? EdgeColor { get; }
    public ConductorIor? Ior { get; }
    public float Fuzz { get; }
    public bool MultipleScattering { get; }

    private readonly float _alpha;
    private readonly Vector3 _iorAverage;   // F_avg of the complex Fresnel

    public Metal(Vector3 color, float fuzz) : this(new SolidColor(color), fuzz) { }

    public Metal(ITexture albedo, float fuzz, ITexture? edgeColor = null, ConductorIor? ior = null,
                 bool multipleScattering = true)
    {
        Albedo = albedo;
        EdgeColor = edgeColor;
        Ior = ior;
        Fuzz = Math.Clamp(fuzz, 0f, 1f);
        MultipleScattering = multipleScattering;
        _alpha = MathF.Max(Fuzz * Fuzz, 1e-4f);
        if (ior is ConductorIor c) _iorAverage = ConductorFresnel.ComplexAverage(c.Eta, c.K);
    }

    // A perfect mirror (fuzz=0) is a delta BSDF and cannot be reached by NEE.
    public bool IsDeltaScatter => Fuzz <= 0f;

    public NormalMapTexture? NormalMap { get; set; }
    public BumpMapTexture? BumpMap { get; set; }
    public MaterialDisplacement? Displacement { get; set; }

    /// <summary>Denoiser/AOV albedo guide: the reflectance at normal incidence.</summary>
    public Vector3 AovAlbedo(in HitRecord rec) =>
        Ior is ConductorIor c ? ConductorFresnel.Complex(1f, c.Eta, c.K) : Albedo.Value(in rec);

    private Vector3 Fresnel(float cosTheta, in HitRecord rec)
    {
        if (Ior is ConductorIor c) return ConductorFresnel.Complex(cosTheta, c.Eta, c.K);
        Vector3 tint = EdgeColor?.Value(in rec) ?? Vector3.One;
        return ConductorFresnel.F82(cosTheta, Albedo.Value(in rec), tint);
    }

    private Vector3 FresnelAverage(in HitRecord rec)
    {
        if (Ior is ConductorIor) return _iorAverage;
        Vector3 tint = EdgeColor?.Value(in rec) ?? Vector3.One;
        return ConductorFresnel.F82Average(Albedo.Value(in rec), tint);
    }

    /// <summary>Probability of sampling the single-scattering lobe: its share
    /// of a white metal's energy, E(μo); the rest goes to the cosine lobe.</summary>
    private float SpecularProbability(float NdotV) =>
        MultipleScattering ? Math.Clamp(MicrofacetAlbedo.E(NdotV, _alpha), 0.05f, 1f) : 1f;

    public Vector3 Evaluate(Vector3 V, Vector3 L, in HitRecord rec)
    {
        if (Fuzz <= 0f) return Vector3.Zero;   // delta lobe: sampled via Sample only
        Vector3 N = rec.Normal;
        float NdotV = Vector3.Dot(N, V), NdotL = Vector3.Dot(N, L);
        if (NdotV <= 0f || NdotL <= 0f) return Vector3.Zero;

        Vector3 f = Vector3.Zero;
        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() > 1e-14f)
        {
            Vector3 H = Vector3.Normalize(Hraw);
            Microfacet.BuildTangentFrame(N, out Vector3 T, out Vector3 B);
            Vector3 Vl = ToLocal(V, T, B, N), Ll = ToLocal(L, T, B, N), Hl = ToLocal(H, T, B, N);
            float D = Microfacet.DGgxAniso(Hl, _alpha, _alpha);
            float G = Microfacet.G2GgxAniso(Vl, Ll, _alpha, _alpha);
            f = Fresnel(Vector3.Dot(V, H), in rec) * (D * G / (4f * NdotV * NdotL));
        }

        if (MultipleScattering)
        {
            float eAvg = MicrofacetAlbedo.Avg(_alpha);
            if (eAvg < 0.9999f)
            {
                Vector3 fAvg = FresnelAverage(in rec);
                Vector3 fMs = fAvg * fAvg * eAvg / (Vector3.One - fAvg * (1f - eAvg));
                float shape = (1f - MicrofacetAlbedo.E(NdotV, _alpha)) * (1f - MicrofacetAlbedo.E(NdotL, _alpha))
                            / (MathF.PI * (1f - eAvg));
                f += fMs * shape;
            }
        }
        return f;
    }

    public float Pdf(Vector3 V, Vector3 L, in HitRecord rec)
    {
        if (Fuzz <= 0f) return 0f;
        Vector3 N = rec.Normal;
        float NdotV = Vector3.Dot(N, V), NdotL = Vector3.Dot(N, L);
        if (NdotV <= 0f || NdotL <= 0f) return 0f;
        float pSpec = SpecularProbability(NdotV);
        return pSpec * SpecularPdf(V, L, N, NdotV) + (1f - pSpec) * NdotL / MathF.PI;
    }

    /// <summary>VNDF pdf of the reflected direction: G1(V) D(H) / (4 N·V).</summary>
    private float SpecularPdf(Vector3 V, Vector3 L, Vector3 N, float NdotV)
    {
        Vector3 Hraw = V + L;
        if (Hraw.LengthSquared() < 1e-14f) return 0f;
        Vector3 H = Vector3.Normalize(Hraw);
        Microfacet.BuildTangentFrame(N, out Vector3 T, out Vector3 B);
        float D = Microfacet.DGgxAniso(ToLocal(H, T, B, N), _alpha, _alpha);
        float g1 = Microfacet.G1GgxAniso(ToLocal(V, T, B, N), _alpha, _alpha);
        return g1 * D / (4f * NdotV);
    }

    public BsdfSample? Sample(Vector3 V, in HitRecord rec)
    {
        Vector3 N = rec.Normal;
        float NdotV = Vector3.Dot(N, V);
        if (NdotV <= 0f) return null;

        if (Fuzz <= 0f)
        {
            // Perfect mirror: the Fresnel of the incidence angle is the whole weight.
            Vector3 mirror = MathUtils.Reflect(-V, N);
            return new BsdfSample(mirror, Fresnel(NdotV, in rec), 1f, isDelta: true);
        }

        Vector3 L;
        float pSpec = SpecularProbability(NdotV);
        if (pSpec >= 1f || MathUtils.RandomFloat() < pSpec)
        {
            Microfacet.BuildTangentFrame(N, out Vector3 T, out Vector3 B);
            var (u1, u2) = MathUtils.Random2D();
            Vector3 Hl = Microfacet.SampleGgxVndfAniso(ToLocal(V, T, B, N), _alpha, _alpha, u1, u2);
            Vector3 H = Hl.X * T + Hl.Y * B + Hl.Z * N;
            L = MathUtils.Reflect(-V, H);
        }
        else
        {
            L = Vector3.Normalize(N + MathUtils.RandomUnitVector());
        }
        if (Vector3.Dot(N, L) <= 0f) return null;

        float pdf = Pdf(V, L, in rec);
        if (pdf <= 0f) return null;
        return new BsdfSample(L, Evaluate(V, L, in rec), pdf, isDelta: false);
    }

    /// <summary>Direct lighting integrand f(V, L) · cos θ_L: the same BSDF as
    /// <see cref="Evaluate"/>, so light and BSDF sampling estimate one material.</summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, in HitRecord rec)
    {
        float NdotL = Vector3.Dot(normal, toLight);
        return NdotL > 0f ? Evaluate(toEye, toLight, in rec) * NdotL : Vector3.Zero;
    }

    private static Vector3 ToLocal(Vector3 v, Vector3 t, Vector3 b, Vector3 n) =>
        new(Vector3.Dot(v, t), Vector3.Dot(v, b), Vector3.Dot(v, n));
}
