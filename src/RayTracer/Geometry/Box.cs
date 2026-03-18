using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A unit cube centered at the origin (from -0.5 to 0.5 on all axes).
/// Use the Transform wrapper to scale, rotate and move it.
/// </summary>
public class Box : IHittable
{
    private static readonly Vector3 Min = new(-0.5f);
    private static readonly Vector3 Max = new(0.5f);
    
    public IMaterial Material { get; }

    public Box(IMaterial material)
    {
        Material = material;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float tNear = tMin;
        float tFar = tMax;
        int hitAxis = -1;
        bool hitNeg = false;

        // Slab method intersection
        for (int a = 0; a < 3; a++)
        {
            float origin = a switch { 0 => ray.Origin.X, 1 => ray.Origin.Y, _ => ray.Origin.Z };
            float dir = a switch { 0 => ray.Direction.X, 1 => ray.Direction.Y, _ => ray.Direction.Z };
            float bmin = a switch { 0 => Min.X, 1 => Min.Y, _ => Min.Z };
            float bmax = a switch { 0 => Max.X, 1 => Max.Y, _ => Max.Z };

            float invD = 1f / dir;
            float t0 = (bmin - origin) * invD;
            float t1 = (bmax - origin) * invD;

            bool swapped = false;
            if (invD < 0f)
            {
                (t0, t1) = (t1, t0);
                swapped = true;
            }

            if (t0 > tNear) { tNear = t0; hitAxis = a; hitNeg = !swapped; }
            if (t1 < tFar) tFar = t1;

            if (tFar <= tNear) return false;
        }

        rec.T = tNear;
        rec.Point = ray.At(tNear);
        rec.LocalPoint = rec.Point;

        // Compute outward normal based on which axis slab was hit
        Vector3 outwardNormal = hitAxis switch
        {
            0 => hitNeg ? -Vector3.UnitX : Vector3.UnitX,
            1 => hitNeg ? -Vector3.UnitY : Vector3.UnitY,
            _ => hitNeg ? -Vector3.UnitZ : Vector3.UnitZ
        };

        rec.SetFaceNormal(ray, outwardNormal);
        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    public int Seed { get; set; }

    public AABB BoundingBox() => new(Min, Max);
}
