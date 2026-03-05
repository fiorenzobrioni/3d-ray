using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

public class Metal : IMaterial
{
    public Vector3 Albedo { get; }
    public float Fuzz { get; }

    public Metal(Vector3 albedo, float fuzz)
    {
        Albedo = albedo;
        Fuzz = MathF.Min(fuzz, 1f);
    }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var reflected = MathUtils.Reflect(Vector3.Normalize(rayIn.Direction), rec.Normal);
        reflected += Fuzz * MathUtils.RandomInUnitSphere();
        scattered = new Ray(rec.Point, reflected);
        attenuation = Albedo;
        return Vector3.Dot(scattered.Direction, rec.Normal) > 0;
    }
}
