using System.Numerics;
using RayTracer.Materials;

namespace RayTracer.Core;

public struct HitRecord
{
    public Vector3 Point;
    public Vector3 Normal;
    public float T;
    public bool FrontFace;
    public IMaterial? Material;

    public void SetFaceNormal(Ray ray, Vector3 outwardNormal)
    {
        FrontFace = Vector3.Dot(ray.Direction, outwardNormal) < 0;
        Normal = FrontFace ? outwardNormal : -outwardNormal;
    }
}
