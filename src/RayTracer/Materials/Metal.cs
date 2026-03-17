using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public class Metal : IMaterial
{
    public ITexture Albedo { get; }
    public float Fuzz { get; }

    public Metal(Vector3 color, float fuzz)
    {
        Albedo = new SolidColor(color);
        Fuzz = MathF.Min(fuzz, 1f);
    }

    public Metal(ITexture a, float fuzz)
    {
        Albedo = a;
        Fuzz = MathF.Min(fuzz, 1f);
    }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var reflected = MathUtils.Reflect(Vector3.Normalize(rayIn.Direction), rec.Normal);
        reflected += Fuzz * MathUtils.RandomInUnitSphere();
        scattered = new Ray(rec.Point, reflected);
        attenuation = Albedo.Value(rec.U, rec.V, rec.Point, rec.ObjectSeed);
        return Vector3.Dot(scattered.Direction, rec.Normal) > 0;
    }
}
