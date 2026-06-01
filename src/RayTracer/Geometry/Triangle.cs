using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Triangle primitive using the Möller–Trumbore intersection algorithm.
/// </summary>
public class Triangle : IHittable, ISamplable
{
    public Vector3 V0 { get; }
    public Vector3 V1 { get; }
    public Vector3 V2 { get; }
    public IMaterial Material { get; }
    private readonly Vector3 _normal;

    // Precomputed edges, area and tangent/bitangent. Triangles are immutable so
    // these never change at hit time. For a mesh with N triangles each ray that
    // touches the BVH leaf used to recompute V1-V0 / V2-V0 (and a Cross+Length
    // for the area inside SampleAt) on every probe — now they're constants.
    private readonly Vector3 _edge1;
    private readonly Vector3 _edge2;
    private readonly float _area;
    private readonly Vector3 _tangent;
    private readonly Vector3 _bitangent;

    // Möller–Trumbore parallelism cutoff. The determinant scales as
    // |edge1|·|edge2|·|sin(D, edge2)|·|cos(...)|; with edges ~1e-2 valid hits
    // already produce |a| ~ 1e-4, so the previous MathUtils.Epsilon (1e-4)
    // silently dropped intersections on small / scaled mesh triangles. 1e-7 is
    // the same value used by SmoothTriangle and matches PBRT/Embree practice.
    private const float ParallelEpsilon = 1e-7f;

    public Triangle(Vector3 v0, Vector3 v1, Vector3 v2, IMaterial material)
    {
        V0 = v0; V1 = v1; V2 = v2;
        Material = material;
        _edge1 = v1 - v0;
        _edge2 = v2 - v0;
        Vector3 cross = Vector3.Cross(_edge1, _edge2);
        float crossLen = cross.Length();
        _normal = crossLen > 0f ? cross / crossLen : Vector3.UnitY;
        _area = 0.5f * crossLen;
        _tangent = _edge1.LengthSquared() > 0f ? Vector3.Normalize(_edge1) : Vector3.UnitX;
        _bitangent = _edge2.LengthSquared() > 0f ? Vector3.Normalize(_edge2) : Vector3.UnitZ;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        Vector3 h = Vector3.Cross(ray.Direction, _edge2);
        float a = Vector3.Dot(_edge1, h);

        if (MathF.Abs(a) < ParallelEpsilon) return false;

        float f = 1f / a;
        Vector3 s = ray.Origin - V0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;

        Vector3 q = Vector3.Cross(s, _edge1);
        float v = f * Vector3.Dot(ray.Direction, q);
        if (v < 0f || u + v > 1f) return false;

        float t = f * Vector3.Dot(_edge2, q);
        if (t < tMin || t > tMax) return false;

        rec.T = t;
        rec.Point = ray.At(t);
        rec.LocalPoint = rec.Point;
        rec.SetFaceNormal(ray, _normal);
        rec.U = u;
        rec.V = v;

        // Tangent and bitangent are derived from the geometric edges, consistent with the
        // barycentric UV mapping (U along edge V0→V1, V along edge V0→V2).
        rec.Tangent = _tangent;
        rec.Bitangent = _bitangent;
        // ∂P/∂u, ∂P/∂v match the Möller-Trumbore barycentric parametrization
        // used to populate rec.U, rec.V: along (V0→V1) and (V0→V2) respectively,
        // with full edge magnitude. Texture filtering uses these to map screen
        // partials to texel partials at the correct rate for triangle UVs.
        rec.DpDu = _edge1;
        rec.DpDv = _edge2;

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    /// <summary>Möller–Trumbore barycentrics (u along V0→V1, v along V0→V2) of an in-plane point.</summary>
    public void Barycentric(Vector3 p, out float u, out float v)
    {
        Vector3 w = p - V0;
        float d00 = Vector3.Dot(_edge1, _edge1);
        float d01 = Vector3.Dot(_edge1, _edge2);
        float d11 = Vector3.Dot(_edge2, _edge2);
        float d20 = Vector3.Dot(w, _edge1);
        float d21 = Vector3.Dot(w, _edge2);
        float denom = d00 * d11 - d01 * d01;
        if (MathF.Abs(denom) < 1e-20f) { u = 0f; v = 0f; return; }
        float inv = 1f / denom;
        u = (d11 * d20 - d01 * d21) * inv;
        v = (d00 * d21 - d01 * d20) * inv;
    }

    /// <inheritdoc/>
    public float SurfaceArea => _area;

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
        => SampleAt(MathUtils.RandomFloat(), MathUtils.RandomFloat());

    /// <summary>
    /// Stratified sampling on the triangle via the unit-square → triangle
    /// warp <c>(xi1, xi2) → (1-√xi1, xi2·√xi1)</c>. Each cell of the
    /// <c>sqrtSamples × sqrtSamples</c> grid in (xi1, xi2) maps to a region
    /// of approximately equal area on the triangle, so jittered samples
    /// remain evenly distributed after the warp.
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
        float r1 = MathF.Sqrt(xi1);
        float u = 1f - r1;
        float v = xi2 * r1;
        Vector3 point = V0 + u * _edge1 + v * _edge2;
        // Triangle's Hit() stores the Möller-Trumbore (u, v) barycentric
        // coordinates directly into rec.U, rec.V — keep the sample UV
        // consistent with that convention.
        return (point, _normal, new Vector2(u, v), _area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        var min = Vector3.Min(Vector3.Min(V0, V1), V2) - new Vector3(MathUtils.Epsilon);
        var max = Vector3.Max(Vector3.Max(V0, V1), V2) + new Vector3(MathUtils.Epsilon);
        return new AABB(min, max);
    }
}
