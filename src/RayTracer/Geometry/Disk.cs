using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A flat disk primitive defined by a center, a normal, and a radius.
/// </summary>
public class Disk : IHittable, ISamplable
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

        // Planar UV mapping using a local orthonormal basis derived from the disk normal.
        // uAxis = cross(Normal, UnitY), giving a stable "right" direction in the disk plane.
        // vAxis = cross(Normal, uAxis), completing the right-handed basis.
        // The hit point is projected onto these axes, normalized from [-Radius, +Radius] to [0, 1].
        Vector3 uAxis = MathF.Abs(Normal.Y) < 0.999f ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY)) : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(Normal, uAxis);
                
        float x = Vector3.Dot(v, uAxis) / Radius;
        float y = Vector3.Dot(v, vAxis) / Radius;
        rec.U = (x + 1) / 2;
        rec.V = (y + 1) / 2;
        
        rec.Tangent = uAxis;
        rec.Bitangent = vAxis;

        return true;
    }

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        float r1 = MathUtils.RandomFloat();
        float r2 = MathUtils.RandomFloat();
        float r = MathF.Sqrt(r1) * Radius;
        float theta = r2 * 2f * MathF.PI;

        Vector3 uAxis = MathF.Abs(Normal.Y) < 0.999f ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY)) : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(Normal, uAxis);

        Vector3 point = Center + r * MathF.Cos(theta) * uAxis + r * MathF.Sin(theta) * vAxis;
        float area = MathF.PI * Radius * Radius;
        
        return (point, Normal, area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        Vector3 r = new Vector3(Radius);
        // This is a loose AABB, could be tighter by looking at normal
        return new AABB(Center - r, Center + r);
    }
}
