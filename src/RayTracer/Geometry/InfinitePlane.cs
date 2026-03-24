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
        rec.LocalPoint = rec.Point;
        rec.SetFaceNormal(ray, Normal);
        
        // Robust UV mapping using local orthonormal basis
        Vector3 uAxis = MathF.Abs(Normal.Y) < 0.99f ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY)) : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(Normal, uAxis);

        Vector3 p = rec.Point - Point;
        rec.U = Vector3.Dot(p, uAxis);
        rec.V = Vector3.Dot(p, vAxis);
        
        // Frac for tiling
        rec.U -= MathF.Floor(rec.U);
        rec.V -= MathF.Floor(rec.V);

        rec.Tangent = uAxis;
        rec.Bitangent = vAxis;

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        // Infinite plane has no finite bounding box — use a very large one
        const float big = 1e6f;
        return new AABB(new Vector3(-big), new Vector3(big));
    }
}
