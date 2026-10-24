using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public sealed class Lambertian : IMaterial
{
    public ITexture Albedo { get; }

    public Lambertian(Vector3 color)
    {
        Albedo = new SolidColor(color);
    }

    public Lambertian(ITexture a)
    {
        Albedo = a;
    }

    public NormalMapTexture? NormalMap { get; set; }
    public BumpMapTexture? BumpMap { get; set; }
    public MaterialDisplacement? Displacement { get; set; }

    /// <summary>Denoiser/AOV albedo guide: the diffuse albedo texture.</summary>
    public Vector3 AovAlbedo(in HitRecord rec) => Albedo.Value(in rec);

    // ─────────────────────────────────────────────────────────────────────────
    // Symmetric BSDF API (Evaluate / Pdf / Sample) — enables full MIS.
    //
    // The Lambertian BRDF is f(V, L) = albedo / π in the upper hemisphere and
    // zero in the back hemisphere. Sampling is cosine-weighted, so the PDF is
    // max(N·L, 0) / π. With this contract the renderer's MIS path computes
    //   attenuation = F · NdotWo / Pdf = (albedo/π) · cosθ / (cosθ/π) = albedo
    // so emission reached by a Lambertian-sampled ray is weighted by the
    // balance/power heuristic when a Lambertian-sampled ray hits a registered
    // emitter.
    // ─────────────────────────────────────────────────────────────────────────

    public Vector3 Evaluate(Vector3 V, Vector3 L, in HitRecord rec)
    {
        if (Vector3.Dot(rec.Normal, L) <= 0f) return Vector3.Zero;
        return Albedo.Value(in rec) * (1f / MathF.PI);
    }

    public float Pdf(Vector3 V, Vector3 L, in HitRecord rec)
    {
        float cos = Vector3.Dot(rec.Normal, L);
        return cos > 0f ? cos * (1f / MathF.PI) : 0f;
    }

    public BsdfSample? Sample(Vector3 V, in HitRecord rec)
    {
        Vector3 N = rec.Normal;
        Vector3 dir = N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(dir))
            dir = N;
        Vector3 wo = Vector3.Normalize(dir);

        float NdotWo = Vector3.Dot(N, wo);
        if (NdotWo <= 0f) return null;

        Vector3 albedo = Albedo.Value(in rec);
        Vector3 f = albedo * (1f / MathF.PI);
        float pdf = NdotWo * (1f / MathF.PI);
        return new BsdfSample(wo, f, pdf, isDelta: false, lobe: BsdfLobe.Diffuse);
    }
}
