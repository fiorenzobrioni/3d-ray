using System.Numerics;

namespace RayTracer.Core;

public readonly struct AABB
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public bool Hit(Ray ray, float tMin, float tMax)
    {
        for (int a = 0; a < 3; a++)
        {
            float origin = a switch { 0 => ray.Origin.X, 1 => ray.Origin.Y, _ => ray.Origin.Z };
            float dir = a switch { 0 => ray.Direction.X, 1 => ray.Direction.Y, _ => ray.Direction.Z };
            float min = a switch { 0 => Min.X, 1 => Min.Y, _ => Min.Z };
            float max = a switch { 0 => Max.X, 1 => Max.Y, _ => Max.Z };

            float invD = 1f / dir;
            float t0 = (min - origin) * invD;
            float t1 = (max - origin) * invD;

            if (invD < 0f)
                (t0, t1) = (t1, t0);

            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;

            if (tMax <= tMin)
                return false;
        }
        return true;
    }

    public static AABB SurroundingBox(AABB a, AABB b)
    {
        var min = Vector3.Min(a.Min, b.Min);
        var max = Vector3.Max(a.Max, b.Max);
        return new AABB(min, max);
    }

    public static AABB Empty => new(
        new Vector3(float.MaxValue), new Vector3(float.MinValue));
}
