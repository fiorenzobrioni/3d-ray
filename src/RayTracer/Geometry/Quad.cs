using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A quadrilateral (parallelogram) primitive defined by an origin Q and two vectors U, V.
/// </summary>
public class Quad : IHittable
{
    public Vector3 Q { get; }
    public Vector3 U { get; }
    public Vector3 V { get; }
    public IMaterial Material { get; }

    private readonly Vector3 _normal;
    private readonly float _d;
    private readonly Vector3 _w;

    public Quad(Vector3 q, Vector3 u, Vector3 v, IMaterial material)
    {
        Q = q;
        U = u;
        V = v;
        Material = material;

        var n = Vector3.Cross(U, V);
        _normal = Vector3.Normalize(n);
        _d = Vector3.Dot(_normal, Q);
        _w = n / Vector3.Dot(n, n);
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float denom = Vector3.Dot(_normal, ray.Direction);

        // Ray is parallel to the plane
        if (MathF.Abs(denom) < 1e-8f)
            return false;

        float t = (_d - Vector3.Dot(_normal, ray.Origin)) / denom;
        if (t < tMin || t > tMax)
            return false;

        Vector3 intersection = ray.At(t);
        Vector3 planarHitptVector = intersection - Q;
        
        float alpha = Vector3.Dot(_w, Vector3.Cross(planarHitptVector, V));
        float beta = Vector3.Dot(_w, Vector3.Cross(U, planarHitptVector));

        if (alpha < 0 || alpha > 1 || beta < 0 || beta > 1)
            return false;

        rec.U = alpha;
        rec.V = beta;
        rec.T = t;
        rec.Point = intersection;
        rec.LocalPoint = rec.Point;
        rec.SetFaceNormal(ray, _normal);
        
        rec.Tangent = Vector3.Normalize(U);
        rec.Bitangent = Vector3.Normalize(V);

        rec.Material = Material;
        rec.ObjectSeed = Seed;

        return true;
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        var bbox = new AABB(Q, Q + U + V);
        // Expand slightly to avoid zero-thickness boxes
        return new AABB(
            Vector3.Min(bbox.Min, Vector3.Min(Q + U, Q + V)) - new Vector3(0.0001f),
            Vector3.Max(bbox.Max, Vector3.Max(Q + U, Q + V)) + new Vector3(0.0001f));
    }
}
