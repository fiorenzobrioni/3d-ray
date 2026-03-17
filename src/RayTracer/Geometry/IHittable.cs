using RayTracer.Core;

namespace RayTracer.Geometry;

public interface IHittable
{
    int Seed { get; set; }
    bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec);
    AABB BoundingBox();
}
