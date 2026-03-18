using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A flat disk primitive defined by a center, a normal, and a radius.
/// </summary>
public class Disk : IHittable
{
    public Vector3 Center { get; }
    public Vector3 Normal { get; }
    public float Radius { get; }
    public IMaterial Material { get; }

    public Disk(Vector3 center, Vector3 normal, float radius, IMaterial material)
    {
        Center = center;
        Normal = Vector3.Normalize(normal);
        Radius = radius;
        Material = material;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float denom = Vector3.Dot(Normal, ray.Direction);
        if (MathF.Abs(denom) < 1e-8f)
            return false;

        float t = Vector3.Dot(Center - ray.Origin, Normal) / denom;
        if (t < tMin || t > tMax)
            return false;

        Vector3 p = ray.At(t);
        Vector3 v = p - Center;
        float distSq = v.LengthSquared();

        if (distSq > Radius * Radius)
            return false;

        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p;
        rec.SetFaceNormal(ray, Normal);
        rec.Material = Material;
        rec.ObjectSeed = Seed;

        // Simple UV mapping for Disk: projecting to local polar coordinates
        // We'd need a local coordinate system to do this properly, 
        // for now we use a simpler approach or skip it if not critical.
        // Let's implement a basic one.
        Vector3 uAxis = MathF.Abs(Normal.Y) < 0.999f ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY)) : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(Normal, uAxis);
        
        float x = Vector3.Dot(v, uAxis) / Radius;
        float y = Vector3.Dot(v, vAxis) / Radius;
        rec.U = (x + 1) / 2;
        rec.V = (y + 1) / 2;

        return true;
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        Vector3 r = new Vector3(Radius);
        // This is a loose AABB, could be tighter by looking at normal
        return new AABB(Center - r, Center + r);
    }
}
