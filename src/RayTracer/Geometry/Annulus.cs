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

    public Annulus(Vector3 center, Vector3 normal, float outerRadius, float innerRadius, IMaterial material)
    {
        Center = center;
        Normal = Vector3.Normalize(normal);
        OuterRadius = outerRadius;
        InnerRadius = Math.Clamp(innerRadius, 0f, outerRadius);
        Material = material;
        _outerRadiusSq = outerRadius * outerRadius;
        _innerRadiusSq = InnerRadius * InnerRadius;
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

        // Outside the outer circle or inside the inner circle → miss
        if (distSq > _outerRadiusSq || distSq < _innerRadiusSq)
            return false;

        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p;
        rec.SetFaceNormal(ray, Normal);
        rec.Material = Material;
        rec.ObjectSeed = Seed;

        // UV mapping — same planar projection as Disk
        Vector3 uAxis = MathF.Abs(Normal.Y) < 0.999f
            ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY))
            : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(Normal, uAxis);

        float x = Vector3.Dot(v, uAxis) / OuterRadius;
        float y = Vector3.Dot(v, vAxis) / OuterRadius;
        rec.U = (x + 1f) / 2f;
        rec.V = (y + 1f) / 2f;

        rec.Tangent = uAxis;
        rec.Bitangent = vAxis;

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

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        float area = MathF.PI * (_outerRadiusSq - _innerRadiusSq);

        // Uniform area sampling on an annulus:
        //   r = sqrt(lerp(r_inner², r_outer², u))
        // This is the CDF inversion for P(r) ∝ r on [r_inner, r_outer].
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();
        float r = MathF.Sqrt(_innerRadiusSq + u1 * (_outerRadiusSq - _innerRadiusSq));
        float theta = u2 * 2f * MathF.PI;

        Vector3 uAxis = MathF.Abs(Normal.Y) < 0.999f
            ? Vector3.Normalize(Vector3.Cross(Normal, Vector3.UnitY))
            : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(Normal, uAxis);

        Vector3 point = Center + r * MathF.Cos(theta) * uAxis + r * MathF.Sin(theta) * vAxis;
        return (point, Normal, area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        // Same as Disk — loose AABB based on outer radius
        Vector3 r = new Vector3(OuterRadius);
        return new AABB(Center - r, Center + r);
    }
}
