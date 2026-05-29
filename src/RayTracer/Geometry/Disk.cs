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

    // Disk plane basis — depends only on the (immutable) normal, so precompute
    // once instead of rebuilding (cross + normalize) on every Hit and Sample.
    private readonly Vector3 _uAxis;
    private readonly Vector3 _vAxis;

    public Disk(Vector3 center, Vector3 normal, float radius, IMaterial material)
    {
        Center = center;
        Normal = Vector3.Normalize(normal);
        Radius = radius;
        Material = material;

        _uAxis = MathF.Abs(Normal.Y) < 0.999f ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY)) : Vector3.UnitX;
        _vAxis = Vector3.Cross(Normal, _uAxis);
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
        rec.LocalPoint = p - Center;
        rec.SetFaceNormal(ray, Normal);
        rec.Material = Material;
        rec.ObjectSeed = Seed;

        // Planar UV mapping using the precomputed local orthonormal basis
        // (uAxis = cross(Normal, UnitY); vAxis = cross(Normal, uAxis)). The hit
        // point is projected onto these axes, normalized from [-R, +R] to [0,1].
        float x = Vector3.Dot(v, _uAxis) / Radius;
        float y = Vector3.Dot(v, _vAxis) / Radius;
        rec.U = (x + 1) / 2;
        rec.V = (y + 1) / 2;

        rec.Tangent = _uAxis;
        rec.Bitangent = _vAxis;

        return true;
    }

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
        => SampleAt(MathUtils.RandomFloat(), MathUtils.RandomFloat());

    /// <inheritdoc/>
    public float SurfaceArea => MathF.PI * Radius * Radius;

    /// <summary>
    /// Stratified version: jitters (r², θ) independently on a
    /// <c>sqrtSamples × sqrtSamples</c> grid. Using r² rather than r
    /// preserves the uniform-area property of the sampling.
    /// </summary>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        float inv = 1f / sqrtSamples;
        int su = sampleIndex % sqrtSamples;
        int sv = sampleIndex / sqrtSamples;
        float xi1 = (su + MathUtils.RandomFloat()) * inv;
        float xi2 = (sv + MathUtils.RandomFloat()) * inv;
        return SampleAt(xi1, xi2);
    }

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleAt(float xi1, float xi2)
    {
        float r = MathF.Sqrt(xi1) * Radius;
        float theta = xi2 * 2f * MathF.PI;

        Vector3 point = Center + r * MathF.Cos(theta) * _uAxis + r * MathF.Sin(theta) * _vAxis;
        float area = MathF.PI * Radius * Radius;

        // UV matches Hit()'s planar convention: (r·cosθ/R + 1)/2, (r·sinθ/R + 1)/2
        float invR = Radius > 0f ? 1f / Radius : 0f;
        float u = (r * MathF.Cos(theta) * invR + 1f) * 0.5f;
        float v = (r * MathF.Sin(theta) * invR + 1f) * 0.5f;
        return (point, Normal, new Vector2(u, v), area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        Vector3 r = new Vector3(Radius);
        // This is a loose AABB, could be tighter by looking at normal
        return new AABB(Center - r, Center + r);
    }
}
