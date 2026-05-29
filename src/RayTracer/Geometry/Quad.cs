using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A quadrilateral (parallelogram) primitive defined by an origin Q and two vectors U, V.
/// </summary>
public class Quad : IHittable, ISamplable
{
    public Vector3 Q { get; }
    public Vector3 U { get; }
    public Vector3 V { get; }
    public IMaterial Material { get; }

    private readonly Vector3 _normal;
    private readonly float _d;
    private readonly Vector3 _w;
    // Immutable per-quad constants — precomputed once instead of on every Hit
    // (tangent basis) and every area-light sample (area). Q, U, V never change.
    private readonly Vector3 _tangent;
    private readonly Vector3 _bitangent;
    private readonly float _area;

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
        _tangent = Vector3.Normalize(U);
        _bitangent = Vector3.Normalize(V);
        _area = n.Length(); // == Vector3.Cross(U, V).Length()
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
        // Object-local frame: origin at the quad's anchor corner Q, axes
        // world-aligned (NOT the parametric (U,V) plane — that's already
        // available in rec.U/rec.V). Keeps procedural texture tiling
        // per-quad, consistent with the rest of the primitive set.
        rec.LocalPoint = rec.Point - Q;
        rec.SetFaceNormal(ray, _normal);
        
        rec.Tangent = _tangent;
        rec.Bitangent = _bitangent;
        // ∂P/∂u = U, ∂P/∂v = V (the quad's parametric vectors map [0,1]²
        // onto the quad itself). Filtering uses these to convert the
        // screen-space footprint into UV partials at the correct magnitude.
        rec.DpDu = U;
        rec.DpDv = V;

        rec.Material = Material;
        rec.ObjectSeed = Seed;

        return true;
    }

    /// <inheritdoc/>
    public float SurfaceArea => _area;

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        float u = MathUtils.RandomFloat();
        float v = MathUtils.RandomFloat();
        Vector3 point = Q + u * U + v * V;
        return (point, _normal, new Vector2(u, v), _area);
    }

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        float inv = 1f / sqrtSamples;
        int su = sampleIndex % sqrtSamples;
        int sv = sampleIndex / sqrtSamples;

        float u = (su + MathUtils.RandomFloat()) * inv;
        float v = (sv + MathUtils.RandomFloat()) * inv;
        Vector3 point = Q + u * U + v * V;
        return (point, _normal, new Vector2(u, v), _area);
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
