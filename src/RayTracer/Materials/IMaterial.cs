using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

public interface IMaterial
{
    bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered);
}
