using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

public class Lambertian : IMaterial
{
    public Vector3 Albedo { get; }

    public Lambertian(Vector3 albedo)
    {
        Albedo = albedo;
    }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var scatterDirection = rec.Normal + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDirection))
            scatterDirection = rec.Normal;

        scattered = new Ray(rec.Point, scatterDirection);
        attenuation = Albedo;
        return true;
    }
}
