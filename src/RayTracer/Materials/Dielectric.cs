using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public class Dielectric : IMaterial
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

    // ── Direct lighting properties ──────────────────────────────────────────
    //
    // Glass is purely specular: its illumination comes from traced
    // reflected/refracted rays. A full-strength Blinn-Phong highlight
    // via NEE would double-count the Fresnel reflection energy.
    //
    // However, at practical sample counts the path-traced Fresnel
    // highlight is noisy (each sample stochastically reflects OR
    // refracts). A reduced NEE highlight acts as a low-variance
    // approximation of the Fresnel glint until MIS is implemented.
    //
    // SpecularStrength is set to 0.25 (was 0.6) — enough for the
    // visual "sparkle" on glass under point/spot lights, low enough
    // that the double-counted energy is negligible (~12% of a
    // typical Fresnel peak at normal incidence for IOR 1.5).
    //
    public float DiffuseWeight => 0f;
    public float SpecularExponent => 512f;
    public float SpecularStrength => 0.25f;
    public NormalMapTexture? NormalMap { get; set; }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        float ri = rec.FrontFace ? (1f / RefractionIndex) : RefractionIndex;

        Vector3 unitDirection = Vector3.Normalize(rayIn.Direction);

        // Ensure the shading normal faces against the incoming ray.
        // Normally SetFaceNormal guarantees this, but CSG subtraction flips
        // the normal on carved (B) surfaces to orient it toward the cavity.
        // That flip is correct for the solid's topology (and FrontFace for
        // IOR selection is also correct), but leaves the normal co-directional
        // with the ray — which breaks cosTheta, Schlick, and Refract.
        // Disney's ScatterTransmission already has this guard; Dielectric needs it too.
        Vector3 normal = rec.Normal;
        if (Vector3.Dot(normal, unitDirection) > 0f)
            normal = -normal;

        float cosTheta = MathF.Min(Vector3.Dot(-unitDirection, normal), 1f);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        bool cannotRefract = ri * sinTheta > 1f;
        Vector3 direction;

        if (cannotRefract || MathUtils.Schlick(cosTheta, ri) > MathUtils.RandomFloat())
            direction = MathUtils.Reflect(unitDirection, normal);
        else
            direction = MathUtils.Refract(unitDirection, normal, ri);

        scattered = new Ray(rec.Point, direction);
        return true;
    }
}
