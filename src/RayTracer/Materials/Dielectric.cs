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
    // Glass and transparent materials do NOT scatter light diffusely.
    // All their illumination comes from the refracted/reflected rays traced
    // recursively by the path tracer. Applying Lambert N·L would make glass
    // look like an opaque white surface lit from one side — completely wrong.
    //
    // A subtle specular highlight is added to simulate the Fresnel glint that
    // appears on glass surfaces facing point lights. This is the "sparkle"
    // you see on a wine glass under a candle. The exponent is very high
    // (tight highlight) and strength is moderate.
    //
    public float DiffuseWeight => 0f;
    public float SpecularExponent => 512f;
    public float SpecularStrength => 0.6f;
    public NormalMapTexture? NormalMap { get; set; }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        float ri = rec.FrontFace ? (1f / RefractionIndex) : RefractionIndex;

        Vector3 unitDirection = Vector3.Normalize(rayIn.Direction);
        float cosTheta = MathF.Min(Vector3.Dot(-unitDirection, rec.Normal), 1f);
        float sinTheta = MathF.Sqrt(1f - cosTheta * cosTheta);

        bool cannotRefract = ri * sinTheta > 1f;
        Vector3 direction;

        if (cannotRefract || MathUtils.Schlick(cosTheta, ri) > MathUtils.RandomFloat())
            direction = MathUtils.Reflect(unitDirection, rec.Normal);
        else
            direction = MathUtils.Refract(unitDirection, rec.Normal, ri);

        scattered = new Ray(rec.Point, direction);
        return true;
    }
}
