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

    public NormalMapTexture? NormalMap { get; set; }

    /// <summary>
    /// Lambertian direct lighting: flat N·L cosine. The 1/π normalisation
    /// is absorbed by <see cref="Scatter"/>, which uses a cosine-weighted
    /// hemisphere sampler whose BRDF/pdf ratio equals the albedo — so keeping
    /// EvaluateDirect at plain N·L matches the indirect path's energy budget.
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, HitRecord rec)
        => new(MathF.Max(Vector3.Dot(normal, toLight), 0f));

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
