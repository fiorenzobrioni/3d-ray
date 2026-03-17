using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Axis-Aligned Bounding Box primitive using the slab method for intersection.
/// Internally composed of 6 faces but uses the efficient slab test.
/// </summary>
public class Box : IHittable
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public IMaterial Material { get; }

    public Box(Vector3 min, Vector3 max, IMaterial material)
    {
        Min = min;
        Max = max;
        Material = material;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float tNear = tMin;
        float tFar = tMax;
        int hitAxis = -1;
        bool hitNeg = false;

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
