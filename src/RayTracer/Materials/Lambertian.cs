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

    /// <summary>
    /// Lambertian direct lighting (NEE integrand): full BRDF · cosθ.
    ///   f(V, L) = albedo / π    (Lambertian BRDF)
    ///   integrand = f · max(N·L, 0) = albedo · max(N·L, 0) / π
    ///
    /// Conforms to the PBRT/Arnold convention: the renderer adds this
    /// contribution to the radiance estimator without multiplying by the
    /// indirect scatter attenuation (which would over-count the albedo).
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, HitRecord rec)
    {
        float NdotL = MathF.Max(Vector3.Dot(normal, toLight), 0f);
        if (NdotL <= 0f) return Vector3.Zero;
        Vector3 albedo = Albedo.Value(in rec);
        return albedo * (NdotL / MathF.PI);
    }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var scatterDirection = rec.Normal + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDirection))
            scatterDirection = rec.Normal;

        scattered = new Ray(rec.Point, scatterDirection);
        attenuation = Albedo.Value(in rec);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Symmetric BSDF API (Evaluate / Pdf / Sample) — enables full MIS.
    //
    // The Lambertian BRDF is f(V, L) = albedo / π in the upper hemisphere and
    // zero in the back hemisphere. Sampling is cosine-weighted, so the PDF is
    // max(N·L, 0) / π. With this contract the renderer's MIS path computes
    //   attenuation = F · NdotWo / Pdf = (albedo/π) · cosθ / (cosθ/π) = albedo
    // which is energetically identical to Scatter() above — so the legacy
    // path is preserved bit-for-bit while emission can now be weighted by the
    // balance/power heuristic when a Lambertian-sampled ray hits a registered
    // emitter.
    // ─────────────────────────────────────────────────────────────────────────

    public Vector3 Evaluate(Vector3 V, Vector3 L, HitRecord rec)
    {
        if (Vector3.Dot(rec.Normal, L) <= 0f) return Vector3.Zero;
        return Albedo.Value(in rec) * (1f / MathF.PI);
    }

    public float Pdf(Vector3 V, Vector3 L, HitRecord rec)
    {
        float cos = Vector3.Dot(rec.Normal, L);
        return cos > 0f ? cos * (1f / MathF.PI) : 0f;
    }

    public BsdfSample? Sample(Vector3 V, HitRecord rec)
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
        return new BsdfSample(wo, f, pdf, isDelta: false);
    }
}
