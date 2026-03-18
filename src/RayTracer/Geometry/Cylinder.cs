using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Finite cylinder aligned to the Y axis, with optional disk caps.
/// </summary>
public class Cylinder : IHittable
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public float Height { get; }
    public IMaterial Material { get; }

    private readonly float _yMin;
    private readonly float _yMax;

    public Cylinder(Vector3 center, float radius, float height, IMaterial material)
    {
        Center = center;
        Radius = radius;
        Height = height;
        Material = material;
        _yMin = center.Y;
        _yMax = center.Y + height;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Test cylinder body (infinite cylinder in XZ plane)
        float dx = ray.Direction.X;
        float dz = ray.Direction.Z;
        float ox = ray.Origin.X - Center.X;
        float oz = ray.Origin.Z - Center.Z;

        float a = dx * dx + dz * dz;
        float halfB = ox * dx + oz * dz;
        float c = ox * ox + oz * oz - Radius * Radius;

        bool hitAnything = false;

        // Body intersection
        float discriminant = halfB * halfB - a * c;
        if (discriminant >= 0 && a > MathUtils.Epsilon)
        {
            float sqrtD = MathF.Sqrt(discriminant);
            for (int i = 0; i < 2; i++)
            {
                float t = (-halfB + (i == 0 ? -sqrtD : sqrtD)) / a;
                if (t < tMin || t > tMax) continue;

                float y = ray.Origin.Y + t * ray.Direction.Y;
                if (y < _yMin || y > _yMax) continue;

                Vector3 point = ray.At(t);
                Vector3 outwardNormal = new Vector3(point.X - Center.X, 0, point.Z - Center.Z) / Radius;

                if (!hitAnything || t < rec.T)
                {
                    rec.T = t;
                    rec.Point = point;
                    rec.LocalPoint = point;
                    rec.SetFaceNormal(ray, outwardNormal);
                    rec.ObjectSeed = Seed;
                    rec.Material = Material;
                    hitAnything = true;
                    tMax = t;
                }
            }
        }

        // Disk caps
        if (MathF.Abs(ray.Direction.Y) > MathUtils.Epsilon)
        {
            for (int i = 0; i < 2; i++)
            {
                float capY = i == 0 ? _yMin : _yMax;
                float t = (capY - ray.Origin.Y) / ray.Direction.Y;
                if (t < tMin || t > tMax) continue;

                Vector3 p = ray.At(t);
                float distSq = (p.X - Center.X) * (p.X - Center.X) + (p.Z - Center.Z) * (p.Z - Center.Z);
                if (distSq > Radius * Radius) continue;

                Vector3 normal = i == 0 ? -Vector3.UnitY : Vector3.UnitY;

                if (!hitAnything || t < rec.T)
                {
                    rec.T = t;
                    rec.Point = p;
                    rec.LocalPoint = p;
                    rec.SetFaceNormal(ray, normal);
                    rec.ObjectSeed = Seed;
                    rec.Material = Material;
                    hitAnything = true;
                    tMax = t;
                }
            }
        }

        return hitAnything;
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        return new AABB(
            new Vector3(Center.X - Radius, _yMin, Center.Z - Radius),
            new Vector3(Center.X + Radius, _yMax, Center.Z + Radius));
    }
}
