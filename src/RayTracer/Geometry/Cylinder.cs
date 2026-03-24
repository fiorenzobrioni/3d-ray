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

                    // Cylindrical UV: U = theta / 2π, V = height fraction
                    float theta = MathF.Atan2(point.Z - Center.Z, point.X - Center.X);
                    rec.U = (theta + MathF.PI) / (2f * MathF.PI);
                    rec.V = (y - _yMin) / Height;

                    // T = ∂P/∂θ (direction of increasing U).
                    // Cross(outwardNormal, UnitY) gives exactly (-sin θ, 0, cos θ) = ∂P/∂θ analytically.
                    // B = +UnitY = direction of increasing V.
                    //
                    // Note: this frame is geometrically left-handed (T × B = -N) because of cylindrical
                    // winding. Fixing it would require either flipping the visual direction of one axis
                    // (inverting bumps) or changing the UV convention. The TBN formula in ApplyNormalMap
                    // (worldN = T*tx + B*ty + N*tz) does not assume right-handedness, so the result is
                    // visually correct and consistent with every other primitive in the scene.
                    Vector3 tDir = Vector3.Cross(outwardNormal, Vector3.UnitY);
                    if (tDir.LengthSquared() < 1e-4f) tDir = Vector3.UnitX;
                    rec.Tangent = Vector3.Normalize(tDir);
                    // Bitangent points in direction of increasing V (height Y).
                    rec.Bitangent = Vector3.UnitY;

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

                    // Cap UV: planar projection
                    rec.U = (p.X - Center.X + Radius) / (2f * Radius);
                    rec.V = (p.Z - Center.Z + Radius) / (2f * Radius);

                    // For top/bottom caps, U increases with X, V increases with Z
                    rec.Tangent = Vector3.UnitX;
                    rec.Bitangent = Vector3.UnitZ;

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
