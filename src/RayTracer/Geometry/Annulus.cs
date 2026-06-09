using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Annulus (ring disk / washer) — a flat disk with a concentric circular hole.
///
/// Defined by a center, a normal, an outer radius, and an inner radius.
/// When <c>InnerRadius</c> is 0 the shape degenerates to a regular Disk.
///
/// The annulus is the 2D equivalent of a tube cross-section: the region
/// between two concentric circles on a plane.
///
/// <b>UV mapping:</b>
///   Uses the same planar projection as Disk: a local orthonormal basis
///   derived from the normal, with the hit point projected onto it.
///   U and V range from [0, 1] across the full outer diameter.
///   The hole region (distSq &lt; InnerRadius²) is simply rejected.
///
/// <b>CSG replacement:</b>
///   Previously, a ring shape required a CSG subtraction (Disk − Disk or
///   Cylinder − Cylinder). The Annulus is much cheaper: a single plane
///   intersection + two distance checks, vs two full primitive intersections
///   + Boolean classification.
///
/// Implements ISamplable for use as an emissive area light with NEE.
/// Area = π(R² - r²).
/// </summary>
public class Annulus : IHittable, ISamplable
{
    public Vector3 Center { get; }
    public Vector3 Normal { get; }
    public float OuterRadius { get; }
    public float InnerRadius { get; }
    public IMaterial Material { get; }

    // Precomputed squared radii for the hit test
    private readonly float _outerRadiusSq;
    private readonly float _innerRadiusSq;

    // Precomputed UV basis (function of Normal only — immutable after construction)
    private readonly Vector3 _uAxis;
    private readonly Vector3 _vAxis;

    // Precomputed tight AABB half-extents (see BoundingBox for derivation)
    private readonly AABB _boundingBox;

    public Annulus(Vector3 center, Vector3 normal, float outerRadius, float innerRadius, IMaterial material)
    {
        Center = center;
        Normal = Vector3.Normalize(normal);
        OuterRadius = outerRadius;
        InnerRadius = Math.Clamp(innerRadius, 0f, outerRadius);
        Material = material;
        _outerRadiusSq = outerRadius * outerRadius;
        _innerRadiusSq = InnerRadius * InnerRadius;

        // UV basis — same convention as Hit() previously computed inline
        _uAxis = MathF.Abs(Normal.Y) < 0.999f
            ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY))
            : Vector3.UnitX;
        _vAxis = Vector3.Cross(Normal, _uAxis);

        // Tight AABB: for a disk with unit normal N, the extent on world-axis i is
        //   R × √(1 − Nᵢ²)
        // Derivation: P = C + R(cosθ·u + sinθ·v); max|Pᵢ−Cᵢ| = R√(uᵢ²+vᵢ²) = R√(1−Nᵢ²)
        // (identity: uᵢ²+vᵢ²+Nᵢ² = 1 for any orthonormal basis).
        // A padding floor of 1e-4 prevents zero-extent AABBs on axis-aligned annuli,
        // which would make AABB.Hit return tExit==tEnter (never strictly greater).
        const float pad = 1e-4f;
        float ex = MathF.Max(pad, OuterRadius * MathF.Sqrt(MathF.Max(0f, 1f - Normal.X * Normal.X)));
        float ey = MathF.Max(pad, OuterRadius * MathF.Sqrt(MathF.Max(0f, 1f - Normal.Y * Normal.Y)));
        float ez = MathF.Max(pad, OuterRadius * MathF.Sqrt(MathF.Max(0f, 1f - Normal.Z * Normal.Z)));
        _boundingBox = new AABB(Center - new Vector3(ex, ey, ez), Center + new Vector3(ex, ey, ez));
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
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

        // Outside the outer circle or inside the inner circle → miss
        if (distSq > _outerRadiusSq || distSq < _innerRadiusSq)
            return false;

        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p - Center;
        rec.SetFaceNormal(ray, Normal);
        rec.Material = Material;
        rec.ObjectSeed = Seed;

        // UV mapping — same planar projection as Disk
        float x = Vector3.Dot(v, _uAxis) / OuterRadius;
        float y = Vector3.Dot(v, _vAxis) / OuterRadius;
        rec.U = (x + 1f) / 2f;
        rec.V = (y + 1f) / 2f;

        rec.Tangent = _uAxis;
        rec.Bitangent = _vAxis;

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISamplable — NEE support for emissive annuli
    //
    // Area = π(R² - r²)
    // Uniform sampling on the annular region: generate a random radius
    // between InnerRadius and OuterRadius with proper area weighting
    // (CDF inversion for uniform-in-area on a ring).
    // ═════════════════════════════════════════════════════════════════════════

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
        => SampleAt(MathUtils.RandomFloat(), MathUtils.RandomFloat());

    /// <summary>
    /// Stratified version: jitters (r², θ) on a <c>sqrtSamples × sqrtSamples</c>
    /// grid. Using r² keeps the uniform-area property of the sampling.
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

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleAt(float u1, float u2)
    {
        float area = MathF.PI * (_outerRadiusSq - _innerRadiusSq);

        // Uniform area sampling on an annulus:
        //   r = sqrt(lerp(r_inner², r_outer², u))
        // This is the CDF inversion for P(r) ∝ r on [r_inner, r_outer].
        float r = MathF.Sqrt(_innerRadiusSq + u1 * (_outerRadiusSq - _innerRadiusSq));
        float theta = u2 * 2f * MathF.PI;

        Vector3 point = Center + r * MathF.Cos(theta) * _uAxis + r * MathF.Sin(theta) * _vAxis;
        return (point, Normal, new Vector2(u1, u2), area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox() => _boundingBox;
}
