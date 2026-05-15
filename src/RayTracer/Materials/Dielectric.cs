using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public sealed class Dielectric : IMaterial
{
    public float RefractionIndex { get; }
    public ITexture Albedo { get; }

    public Dielectric(float refractionIndex)
    {
        RefractionIndex = refractionIndex;
        Albedo = new SolidColor(Vector3.One);
    }

    public Dielectric(float refractionIndex, ITexture albedo)
    {
        RefractionIndex = refractionIndex;
        Albedo = albedo;
    }

    // Glass is a pair of delta lobes (perfect reflection + perfect refraction).
    // NEE cannot reach a delta BSDF, so direct lighting contributes zero and
    // the renderer must preserve emission weight across the bounce.
    public bool IsDeltaScatter => true;
    public NormalMapTexture? NormalMap { get; set; }
    public BumpMapTexture? BumpMap { get; set; }

    // Transparent shadow rays: report (1 - Fresnel) · albedo as the per-hit
    // straight-through transmission. The shadow ray is not refracted — Snell-
    // bent light contributes to caustics through the indirect Scatter path
    // (and would need MNEE/photon mapping to NEE-sample directly). Each
    // dielectric interface attenuates by 1−F; a glass sphere crosses two
    // interfaces, so the receiver still sees the proper Fresnel-squared
    // shadowing at grazing angles.
    public Vector3 ShadowTransmittance(Vector3 wi, HitRecord rec)
    {
        float cosTheta = MathF.Min(MathF.Abs(Vector3.Dot(wi, rec.Normal)), 1f);
        float eta = rec.FrontFace ? (1f / RefractionIndex) : RefractionIndex;
        float fr = MathUtils.FresnelDielectric(cosTheta, eta);
        Vector3 albedo = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        return (1f - fr) * albedo;
    }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        float ri = rec.FrontFace ? (1f / RefractionIndex) : RefractionIndex;

        Vector3 unitDirection = Vector3.Normalize(rayIn.Direction);
        Vector3 normal = rec.Normal;

        float cosTheta = MathF.Min(Vector3.Dot(-unitDirection, normal), 1f);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        bool cannotRefract = ri * sinTheta > 1f;
        Vector3 direction;

        if (cannotRefract || MathUtils.Schlick(cosTheta, ri) > MathUtils.RandomFloat())
            direction = MathUtils.Reflect(unitDirection, normal);
        else
            direction = MathUtils.Refract(unitDirection, normal, ri);

        // Offset the ray origin onto the side of the surface that matches the
        // chosen direction — reflection sits on the outgoing-normal side,
        // refraction on the opposite. Without the offset, raw rec.Point can
        // self-intersect at grazing angles, producing black firefly speckle on
        // high-IOR classic dielectrics. Mirrors the same pattern the renderer
        // uses for non-delta BSDF samples in ShadeSampleBounce.
        Vector3 offsetDir = Vector3.Dot(direction, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
        scattered = new Ray(MathUtils.OffsetOrigin(rec.Point, offsetDir), direction);
        return true;
    }
}
