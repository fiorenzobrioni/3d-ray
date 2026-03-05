using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

public class InfinitePlane : IHittable
{
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public IMaterial Material { get; }

    public InfinitePlane(Vector3 point, Vector3 normal, IMaterial material)
    {
        Point = point;
        Normal = Vector3.Normalize(normal);
        Material = material;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float denom = Vector3.Dot(Normal, ray.Direction);
        if (MathF.Abs(denom) < MathUtils.Epsilon) return false;

        float t = Vector3.Dot(Point - ray.Origin, Normal) / denom;
        if (t < tMin || t > tMax) return false;

        rec.T = t;
        rec.Point = ray.At(t);
        rec.SetFaceNormal(ray, Normal);
        rec.Material = Material;
        return true;
    }

    public AABB BoundingBox()
    {
        // Infinite plane has no finite bounding box — use a very large one
        const float big = 1e6f;
        return new AABB(new Vector3(-big), new Vector3(big));
    }
}
