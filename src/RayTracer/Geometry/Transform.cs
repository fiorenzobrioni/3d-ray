using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A wrapper that applies a 4x4 transformation matrix to any IHittable.
/// Handles ray transformation to Object Space and normal transformation back to World Space.
/// </summary>
public class Transform : IHittable
{
    private readonly IHittable _object;
    private readonly Matrix4x4 _transform;
    private readonly Matrix4x4 _inverse;
    private readonly Matrix4x4 _normalMatrix; // Transpose of the inverse

    public Transform(IHittable hittable, Matrix4x4 matrix)
    {
        _object = hittable;
        _transform = matrix;
        
        if (!Matrix4x4.Invert(_transform, out _inverse))
        {
            _inverse = Matrix4x4.Identity;
        }

        // Normal matrix is the transpose of the inverse to handle non-uniform scaling correctly
        _normalMatrix = Matrix4x4.Transpose(_inverse);
    }

    public int Seed 
    { 
        get => _object.Seed; 
        set => _object.Seed = value; 
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Transform ray to object space
        Vector3 origin = Vector3.Transform(ray.Origin, _inverse);
        Vector3 direction = Vector3.TransformNormal(ray.Direction, _inverse);
        
        Ray localRay = new Ray(origin, direction);

        if (!_object.Hit(localRay, tMin, tMax, ref rec))
            return false;

        // Transform hit point back to world space
        rec.Point = Vector3.Transform(rec.Point, _transform);
        
        // Transform normal back to world space using the normal matrix
        Vector3 worldNormal = Vector3.TransformNormal(rec.Normal, _normalMatrix);
        worldNormal = Vector3.Normalize(worldNormal);
        rec.SetFaceNormal(ray, worldNormal);

        return true;
    }

    public AABB BoundingBox()
    {
        AABB bbox = _object.BoundingBox();
        Vector3 min = bbox.Min;
        Vector3 max = bbox.Max;

        // Transform the 8 corners of the object-space AABB to find the new world-space AABB
        Vector3[] corners = new[]
        {
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z)
        };

        Vector3 newMin = new Vector3(float.MaxValue);
        Vector3 newMax = new Vector3(float.MinValue);

        foreach (var c in corners)
        {
            Vector3 tc = Vector3.Transform(c, _transform);
            newMin = Vector3.Min(newMin, tc);
            newMax = Vector3.Max(newMax, tc);
        }

        return new AABB(newMin, newMax);
    }
}
