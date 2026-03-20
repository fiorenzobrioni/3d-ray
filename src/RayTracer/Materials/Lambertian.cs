using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public class Lambertian : IMaterial
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

    // ── Direct lighting properties ──────────────────────────────────────────
    // Fully diffuse: all direct light goes through Lambert N·L.
    // No specular highlight — the material is perfectly matte.
    public float DiffuseWeight => 1f;
    public float SpecularExponent => 0f;
    public float SpecularStrength => 0f;

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var scatterDirection = rec.Normal + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDirection))
            scatterDirection = rec.Normal;

        scattered = new Ray(rec.Point, scatterDirection);
        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        return true;
    }
}
