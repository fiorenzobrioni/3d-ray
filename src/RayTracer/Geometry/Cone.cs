using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Finite cone (or truncated cone / frustum) aligned to the Y axis, with disk caps.
///
/// The cone is defined by a base center, a base radius, a top radius, and a height.
/// When <c>TopRadius</c> is 0 the shape is a pointed cone; when positive it is a
/// frustum (truncated cone). When <c>TopRadius == Radius</c> it degenerates into
/// a cylinder — use the Cylinder class instead for that case.
///
/// The radius varies linearly from <c>Radius</c> at <c>center.Y</c> to
/// <c>TopRadius</c> at <c>center.Y + Height</c>:
///   r(y) = Radius + (TopRadius - Radius) * (y - yMin) / Height
///
/// UV mapping follows the same convention as Cylinder:
///   U = θ / 2π  (azimuthal angle around Y)
///   V = (y - yMin) / Height  (height fraction, 0 at base, 1 at top)
///
/// TBN basis: Tangent points in the direction of increasing U (azimuthal),
/// Bitangent points in the direction of increasing V along the surface slope.
/// The normal is perpendicular to the cone surface (slanted outward).
///
/// Implements ISamplable for use as an emissive area light with NEE.
/// </summary>
public class Cone : IHittable, ISamplable
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public float TopRadius { get; }
    public float Height { get; }
    public IMaterial Material { get; }

    private readonly float _yMin;
    private readonly float _yMax;

    // Precomputed slope parameters for the cone surface.
    // The cone can be described as: x² + z² = r(y)² where r(y) = _rBase + _slope * (y - _yMin)
    // _slope = (TopRadius - Radius) / Height
    private readonly float _slope;

    public Cone(Vector3 center, float radius, float topRadius, float height, IMaterial material)
    {
        Center = center;
        Radius = radius;
        TopRadius = topRadius;
        Height = height;
        Material = material;
        _yMin = center.Y;
        _yMax = center.Y + height;
        _slope = (topRadius - radius) / height;
    }

    /// <summary>Convenience constructor for a pointed cone (top_radius = 0).</summary>
    public Cone(Vector3 center, float radius, float height, IMaterial material)
        : this(center, radius, 0f, height, material) { }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // ═══════════════════════════════════════════════════════════════════
        // Cone body intersection
        //
        // The cone surface satisfies: (x - cx)² + (z - cz)² = r(y)²
        // where r(y) = R + slope * (y - yMin), slope = (TopR - R) / H
        //
        // Substituting ray P(t) = O + t*D and expanding gives a quadratic
        // in t: a*t² + 2*halfB*t + c = 0
        // ═══════════════════════════════════════════════════════════════════

        float dx = ray.Direction.X;
        float dy = ray.Direction.Y;
        float dz = ray.Direction.Z;
        float ox = ray.Origin.X - Center.X;
        float oy = ray.Origin.Y - _yMin;
        float oz = ray.Origin.Z - Center.Z;

        // r(y) at the ray origin's y
        float rAtO = Radius + _slope * oy;

        float a = dx * dx + dz * dz - _slope * _slope * dy * dy;
        float halfB = ox * dx + oz * dz - _slope * _slope * oy * dy - _slope * dy * Radius;
        float c = ox * ox + oz * oz - rAtO * rAtO;

        // Correction: expand properly.
        // Let s = _slope, R = Radius (base radius)
        // r(y) = R + s*(y - yMin)
        // r(y)² = (R + s*(oy + t*dy))² where oy = Oy - yMin
        //       = R² + 2*R*s*(oy + t*dy) + s²*(oy + t*dy)²
        //
        // LHS: (ox + t*dx)² + (oz + t*dz)²
        //     = ox² + 2*ox*dx*t + dx²*t² + oz² + 2*oz*dz*t + dz²*t²
        //     = (dx²+dz²)*t² + 2*(ox*dx+oz*dz)*t + (ox²+oz²)
        //
        // RHS: R² + 2*R*s*oy + s²*oy²
        //    + (2*R*s*dy + 2*s²*oy*dy)*t
        //    + (s²*dy²)*t²
        //
        // a    = (dx²+dz²) - s²*dy²
        // 2*hB = 2*(ox*dx+oz*dz) - 2*R*s*dy - 2*s²*oy*dy
        // c    = (ox²+oz²) - R² - 2*R*s*oy - s²*oy²
        //      = (ox²+oz²) - (R + s*oy)²  = (ox²+oz²) - rAtO²

        // Recalculate halfB correctly:
        float halfB2 = ox * dx + oz * dz - _slope * dy * (Radius + _slope * oy);

        bool hitAnything = false;

        float discriminant = halfB2 * halfB2 - a * c;
        if (discriminant >= 0 && MathF.Abs(a) > 1e-10f)
        {
            float sqrtD = MathF.Sqrt(discriminant);
            for (int i = 0; i < 2; i++)
            {
                float t = (-halfB2 + (i == 0 ? -sqrtD : sqrtD)) / a;
                if (t < tMin || t > tMax) continue;

                float y = ray.Origin.Y + t * dy;
                if (y < _yMin || y > _yMax) continue;

                // Additional check: for a pointed cone, exclude hits beyond the apex.
                // The apex is at y = yMin - Radius / slope (if slope < 0, apex is above).
                // This is already handled by the y clamp above.

                Vector3 point = ray.At(t);
                float px = point.X - Center.X;
                float pz = point.Z - Center.Z;

                // Outward normal on the cone surface.
                // The surface normal has a radial component (outward in XZ) and a
                // vertical component (downward for a cone that narrows upward).
                //
                // For a cone with r(y) = R + s*(y-yMin):
                //   F(x,y,z) = x² + z² - r(y)² = 0
                //   ∇F = (2x, -2*r(y)*s, 2z)
                //   Normalized: (x, -r(y)*s, z) / |...|
                float rAtY = Radius + _slope * (y - _yMin);
                Vector3 outwardNormal = Vector3.Normalize(
                    new Vector3(px, -rAtY * _slope, pz));

                if (!hitAnything || t < rec.T)
                {
                    rec.T = t;
                    rec.Point = point;
                    rec.LocalPoint = point;
                    rec.SetFaceNormal(ray, outwardNormal);

                    // UV mapping: same convention as Cylinder
                    float theta = MathF.Atan2(pz, px);
                    rec.U = (theta + MathF.PI) / (2f * MathF.PI);
                    rec.V = (y - _yMin) / Height;

                    // TBN: Tangent = ∂P/∂θ (azimuthal direction)
                    Vector3 tDir = new Vector3(-pz, 0, px);
                    if (tDir.LengthSquared() < 1e-8f) tDir = Vector3.UnitX;
                    rec.Tangent = Vector3.Normalize(tDir);
                    // Bitangent = along the cone surface in the V direction (upward slope)
                    rec.Bitangent = Vector3.Normalize(Vector3.Cross(outwardNormal, rec.Tangent));

                    rec.ObjectSeed = Seed;
                    rec.Material = Material;
                    hitAnything = true;
                    tMax = t;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Disk caps
        // ═══════════════════════════════════════════════════════════════════

        if (MathF.Abs(ray.Direction.Y) > 1e-8f)
        {
            // Bottom cap (radius = Radius)
            {
                float t = (_yMin - ray.Origin.Y) / ray.Direction.Y;
                if (t >= tMin && t <= tMax)
                {
                    Vector3 p = ray.At(t);
                    float distSq = (p.X - Center.X) * (p.X - Center.X) +
                                   (p.Z - Center.Z) * (p.Z - Center.Z);
                    if (distSq <= Radius * Radius)
                    {
                        if (!hitAnything || t < rec.T)
                        {
                            SetCapHit(ref rec, ray, p, t, -Vector3.UnitY, Radius, 0f);
                            hitAnything = true;
                            tMax = t;
                        }
                    }
                }
            }

            // Top cap (radius = TopRadius) — only if truncated (TopRadius > 0)
            if (TopRadius > 1e-6f)
            {
                float t = (_yMax - ray.Origin.Y) / ray.Direction.Y;
                if (t >= tMin && t <= tMax)
                {
                    Vector3 p = ray.At(t);
                    float distSq = (p.X - Center.X) * (p.X - Center.X) +
                                   (p.Z - Center.Z) * (p.Z - Center.Z);
                    if (distSq <= TopRadius * TopRadius)
                    {
                        if (!hitAnything || t < rec.T)
                        {
                            SetCapHit(ref rec, ray, p, t, Vector3.UnitY, TopRadius, 1f);
                            hitAnything = true;
                            tMax = t;
                        }
                    }
                }
            }
        }

        return hitAnything;
    }

    private void SetCapHit(ref HitRecord rec, Ray ray, Vector3 p, float t,
                           Vector3 normal, float capRadius, float vCoord)
    {
        rec.T = t;
        rec.Point = p;
        rec.LocalPoint = p;
        rec.SetFaceNormal(ray, normal);

        // Cap UV: planar projection (same as Cylinder caps)
        rec.U = (p.X - Center.X + capRadius) / (2f * capRadius);
        rec.V = (p.Z - Center.Z + capRadius) / (2f * capRadius);

        rec.Tangent = Vector3.UnitX;
        rec.Bitangent = Vector3.UnitZ;

        rec.ObjectSeed = Seed;
        rec.Material = Material;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISamplable — NEE support for emissive cones
    // ═════════════════════════════════════════════════════════════════════════

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        // Compute areas of each part
        float slantHeight = MathF.Sqrt(Height * Height + (Radius - TopRadius) * (Radius - TopRadius));

        // Lateral surface area of a frustum: π * (R + r) * slant
        float lateralArea = MathF.PI * (Radius + TopRadius) * slantHeight;
        float bottomArea = MathF.PI * Radius * Radius;
        float topArea = TopRadius > 1e-6f ? MathF.PI * TopRadius * TopRadius : 0f;
        float totalArea = lateralArea + bottomArea + topArea;

        float r = MathUtils.RandomFloat() * totalArea;

        if (r < lateralArea)
        {
            // Sample on the lateral surface
            // Uniform sampling on a frustum: pick height fraction, then angle
            // For uniform area sampling on a cone, the radius distribution is
            // weighted by the local circumference: sample v such that
            // the probability is proportional to r(v).
            // CDF inversion: v is sampled so that r(v) is linearly weighted.
            float u1 = MathUtils.RandomFloat();
            float u2 = MathUtils.RandomFloat();

            // Weighted sampling: P(v) ∝ r(v) = R + (TopR - R)*v
            // CDF: F(v) = (R*v + (TopR-R)*v²/2) / (R + (TopR-R)/2)
            // For simplicity, use rejection-free linear interpolation:
            // If R ≈ TopR, uniform is fine. Otherwise, use inverse CDF.
            float rBottom = Radius;
            float rTop = TopRadius;
            float v;
            if (MathF.Abs(rTop - rBottom) < 1e-6f)
            {
                v = u1;
            }
            else
            {
                // Inverse CDF for linear weight: v = (-R + sqrt(R² + u1*(TopR²-R²))) / (TopR - R)
                float r2B = rBottom * rBottom;
                float r2T = rTop * rTop;
                v = (MathF.Sqrt(r2B + u1 * (r2T - r2B)) - rBottom) / (rTop - rBottom);
            }

            float theta = u2 * 2f * MathF.PI;
            float rAtV = rBottom + (rTop - rBottom) * v;
            float y = _yMin + v * Height;

            float cosT = MathF.Cos(theta);
            float sinT = MathF.Sin(theta);
            Vector3 point = new(Center.X + rAtV * cosT, y, Center.Z + rAtV * sinT);

            // Normal on the cone surface
            Vector3 radial = new(cosT, 0, sinT);
            Vector3 normal = Vector3.Normalize(
                new Vector3(radial.X, -rAtV * _slope, radial.Z));

            return (point, normal, totalArea);
        }
        else if (r < lateralArea + bottomArea)
        {
            // Sample on bottom cap
            return SampleDisk(Center, -Vector3.UnitY, Radius, totalArea);
        }
        else
        {
            // Sample on top cap
            Vector3 topCenter = new(Center.X, _yMax, Center.Z);
            return SampleDisk(topCenter, Vector3.UnitY, TopRadius, totalArea);
        }
    }

    private static (Vector3 Point, Vector3 Normal, float Area) SampleDisk(
        Vector3 center, Vector3 normal, float radius, float totalArea)
    {
        float r1 = MathUtils.RandomFloat();
        float r2 = MathUtils.RandomFloat();
        float r = MathF.Sqrt(r1) * radius;
        float theta = r2 * 2f * MathF.PI;

        Vector3 uAxis = MathF.Abs(normal.Y) < 0.999f
            ? Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY))
            : Vector3.UnitX;
        Vector3 vAxis = Vector3.Cross(normal, uAxis);

        Vector3 point = center + r * MathF.Cos(theta) * uAxis + r * MathF.Sin(theta) * vAxis;
        return (point, normal, totalArea);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        float maxR = MathF.Max(Radius, TopRadius);
        return new AABB(
            new Vector3(Center.X - maxR, _yMin, Center.Z - maxR),
            new Vector3(Center.X + maxR, _yMax, Center.Z + maxR));
    }
}
