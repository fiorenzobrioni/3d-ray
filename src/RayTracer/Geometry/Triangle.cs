using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Triangle primitive using the Möller–Trumbore intersection algorithm.
/// </summary>
public class Triangle : IHittable
{
    public Vector3 V0 { get; }
    public Vector3 V1 { get; }
    public Vector3 V2 { get; }
    public IMaterial Material { get; }
    private readonly Vector3 _normal;

    public Triangle(Vector3 v0, Vector3 v1, Vector3 v2, IMaterial material)
    {
        V0 = v0; V1 = v1; V2 = v2;
        Material = material;
        _normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        Vector3 edge1 = V1 - V0;
        Vector3 edge2 = V2 - V0;
        Vector3 h = Vector3.Cross(ray.Direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (MathF.Abs(a) < MathUtils.Epsilon) return false;

        float f = 1f / a;
        Vector3 s = ray.Origin - V0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.Direction, q);
        if (v < 0f || u + v > 1f) return false;

        float t = f * Vector3.Dot(edge2, q);
        if (t < tMin || t > tMax) return false;

        rec.T = t;
        rec.Point = ray.At(t);
        rec.SetFaceNormal(ray, _normal);
        rec.Material = Material;
        return true;
    }

    public AABB BoundingBox()
    {
        var min = Vector3.Min(Vector3.Min(V0, V1), V2) - new Vector3(MathUtils.Epsilon);
        var max = Vector3.Max(Vector3.Max(V0, V1), V2) + new Vector3(MathUtils.Epsilon);
        return new AABB(min, max);
    }
}
