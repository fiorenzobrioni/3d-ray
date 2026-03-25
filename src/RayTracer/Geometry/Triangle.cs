using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Triangle primitive using the Möller–Trumbore intersection algorithm.
/// </summary>
public class Triangle : IHittable, ISamplable
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
        rec.LocalPoint = rec.Point;
        rec.SetFaceNormal(ray, _normal);
        rec.U = u;
        rec.V = v;

        // Tangent and bitangent are derived from the geometric edges, consistent with the
        // barycentric UV mapping (U along edge V0→V1, V along edge V0→V2).
        // Limitation: these are NOT per-vertex artist UVs. Normal maps on a triangle tile
        // along the edge vectors and cannot be oriented independently from the geometry.
        // For mesh loading (OBJ/GLTF) with custom UV channels, per-vertex UVs and explicit
        // tangent vectors would be needed here.
        rec.Tangent = Vector3.Normalize(V1 - V0);
        rec.Bitangent = Vector3.Normalize(V2 - V0);

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        float r1 = MathF.Sqrt(MathUtils.RandomFloat());
        float r2 = MathUtils.RandomFloat();
        float u = 1f - r1;
        float v = r2 * r1;
        Vector3 point = V0 + u * (V1 - V0) + v * (V2 - V0);
        float area = 0.5f * Vector3.Cross(V1 - V0, V2 - V0).Length();
        return (point, _normal, area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        var min = Vector3.Min(Vector3.Min(V0, V1), V2) - new Vector3(MathUtils.Epsilon);
        var max = Vector3.Max(Vector3.Max(V0, V1), V2) + new Vector3(MathUtils.Epsilon);
        return new AABB(min, max);
    }
}
