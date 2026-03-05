using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

public class Dielectric : IMaterial
{
    public float RefractionIndex { get; }

    public Dielectric(float refractionIndex)
    {
        RefractionIndex = refractionIndex;
    }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        attenuation = Vector3.One; // Glass doesn't absorb
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
