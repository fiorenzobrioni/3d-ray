using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// A wrapper that applies a 4x4 transformation matrix to any IHittable.
/// Handles ray transformation to Object Space and normal transformation back to World Space.
///
/// LocalPoint is deliberately preserved in object-local space so that procedural
/// textures (marble, wood, noise, checker) tile consistently regardless of how
/// the object is placed in the world. World-space position is in rec.Point.
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
            _inverse = Matrix4x4.Identity;

        // Normal matrix: transpose of the inverse — handles non-uniform scaling correctly
        _normalMatrix = Matrix4x4.Transpose(_inverse);
    }

    public int Seed
    {
        get => _object.Seed;
        set => _object.Seed = value;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Transform ray into object space
        Vector3 origin    = Vector3.Transform(ray.Origin, _inverse);
        Vector3 direction = Vector3.TransformNormal(ray.Direction, _inverse);
        var localRay = new Ray(origin, direction);

        if (!_object.Hit(localRay, tMin, tMax, ref rec))
            return false;

        // rec.LocalPoint is already in object-local space (set by the wrapped primitive).
        // Leave it as-is — this is intentional. Procedural textures sample LocalPoint,
        // so they tile in the object's own coordinate system regardless of world transforms.
        // rec.LocalPoint = rec.LocalPoint;  // <-- purposely NOT transformed

        // Transform the hit point back to world space
        rec.Point = Vector3.Transform(rec.Point, _transform);

        // Transform the normal using the normal matrix (handles non-uniform scale)
        Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(rec.Normal, _normalMatrix));
        rec.SetFaceNormal(ray, worldNormal);

        // Tangent and bitangent are direction vectors, they transform with the forward matrix
        rec.Tangent = Vector3.Normalize(Vector3.TransformNormal(rec.Tangent, _transform));
        rec.Bitangent = Vector3.Normalize(Vector3.TransformNormal(rec.Bitangent, _transform));

        return true;
    }

    public AABB BoundingBox()
    {
        AABB bbox = _object.BoundingBox();
        Vector3 min = bbox.Min;
        Vector3 max = bbox.Max;

        // Transform all 8 corners of the AABB to find the new world-space AABB
        Span<Vector3> corners = stackalloc Vector3[]
        {
            new(min.X, min.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, max.Y, max.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, min.Z),
            new(max.X, max.Y, max.Z)
        };

        Vector3 newMin = new(float.MaxValue);
        Vector3 newMax = new(float.MinValue);

        foreach (var c in corners)
        {
            Vector3 tc = Vector3.Transform(c, _transform);
            newMin = Vector3.Min(newMin, tc);
            newMax = Vector3.Max(newMax, tc);
        }

        return new AABB(newMin, newMax);
    }
}
