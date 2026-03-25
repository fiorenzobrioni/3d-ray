using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

public class Sphere : IHittable, ISamplable
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public IMaterial Material { get; }

    public Sphere(Vector3 center, float radius, IMaterial material)
    {
        Center = center;
        Radius = radius;
        Material = material;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        Vector3 oc = ray.Origin - Center;
        float a = ray.Direction.LengthSquared();
        float halfB = Vector3.Dot(oc, ray.Direction);
        float c = oc.LengthSquared() - Radius * Radius;
        float discriminant = halfB * halfB - a * c;

        if (discriminant < 0) return false;

        float sqrtD = MathF.Sqrt(discriminant);
        float root = (-halfB - sqrtD) / a;
        if (root < tMin || root > tMax)
        {
            root = (-halfB + sqrtD) / a;
            if (root < tMin || root > tMax)
                return false;
        }

        rec.T = root;
        rec.Point = ray.At(rec.T);
        rec.LocalPoint = rec.Point;
        Vector3 outwardNormal = (rec.Point - Center) / Radius;
        rec.SetFaceNormal(ray, outwardNormal);
        
        var (u, v) = GetSphereUV(outwardNormal);
        rec.U = u;
        rec.V = v;

        // Tangent points in direction of increasing U (phi).
        Vector3 tDir = Vector3.Cross(Vector3.UnitY, outwardNormal);
        if (tDir.LengthSquared() < 1e-4f) tDir = Vector3.UnitX;
        rec.Tangent = Vector3.Normalize(tDir);
        // Bitangent points in direction of increasing V (theta, downwards)
        rec.Bitangent = Vector3.Normalize(Vector3.Cross(outwardNormal, rec.Tangent));

        rec.ObjectSeed = Seed;

        rec.Material = Material;
        return true;
    }

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        Vector3 p = MathUtils.RandomUnitVector();
        float area = 4f * MathF.PI * Radius * Radius;
        return (Center + p * Radius, p, area);
    }

    public int Seed { get; set; }

    private static (float U, float V) GetSphereUV(Vector3 p)
    {
        // p: un punto sulla sfera unitaria centrata nell'origine
        float theta = MathF.Acos(-p.Y);
        float phi = MathF.Atan2(-p.Z, p.X) + MathF.PI;

        float u = phi / (2 * MathF.PI);
        float v = theta / MathF.PI;
        return (u, v);
    }

    public AABB BoundingBox()
    {
        var r = new Vector3(Radius);
        return new AABB(Center - r, Center + r);
    }
}
