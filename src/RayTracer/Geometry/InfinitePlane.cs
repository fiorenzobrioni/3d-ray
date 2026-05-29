using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

public class InfinitePlane : IHittable
{
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public IMaterial Material { get; }

    // Plane UV basis — depends only on the immutable normal, precomputed once
    // rather than rebuilt (cross + normalize) on every Hit.
    private readonly Vector3 _uAxis;
    private readonly Vector3 _vAxis;

    public InfinitePlane(Vector3 point, Vector3 normal, IMaterial material)
    {
        Point = point;
        Normal = Vector3.Normalize(normal);
        Material = material;

        _uAxis = MathF.Abs(Normal.Y) < 0.99f ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY)) : Vector3.UnitX;
        _vAxis = Vector3.Cross(Normal, _uAxis);
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float denom = Vector3.Dot(Normal, ray.Direction);
        // BUG-03 fix: use a dedicated parallelism epsilon (1e-6f) instead of
        // MathUtils.Epsilon (1e-4f, intended for shadow-ray offset).
        // At 1e-4f, rays with denom ~0.0002 (nearly parallel but not quite)
        // were incorrectly rejected, causing visual artifacts at grazing angles.
        const float parallelEpsilon = 1e-6f;
        if (MathF.Abs(denom) < parallelEpsilon) return false;

        float t = Vector3.Dot(Point - ray.Origin, Normal) / denom;
        if (t < tMin || t > tMax) return false;

        rec.T = t;
        rec.Point = ray.At(t);
        // Object-local frame: origin at the plane anchor `Point`. For the
        // ubiquitous y=0 ground plane (Point = Vector3.Zero) this is a no-op,
        // preserving the look of every existing floor/wall scene. For
        // displaced anchors it correctly recenters per-plane.
        rec.LocalPoint = rec.Point - Point;
        rec.SetFaceNormal(ray, Normal);
        
        // Robust UV mapping using the precomputed local orthonormal basis
        Vector3 p = rec.Point - Point;
        rec.U = Vector3.Dot(p, _uAxis);
        rec.V = Vector3.Dot(p, _vAxis);

        // Frac for tiling
        rec.U -= MathF.Floor(rec.U);
        rec.V -= MathF.Floor(rec.V);

        rec.Tangent = _uAxis;
        rec.Bitangent = _vAxis;

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        // Infinite plane has no finite bounding box — use a very large one
        const float big = 1e6f;
        return new AABB(new Vector3(-big), new Vector3(big));
    }
}
