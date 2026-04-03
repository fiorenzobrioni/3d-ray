using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Capsule (stadium solid) aligned to the Y axis: a cylinder capped by two
/// hemispheres. Also known as a "swept sphere" or "sphylinder".
///
/// Defined by a center (base of the cylindrical body), a radius, and a height
/// (length of the cylindrical section — total capsule height = Height + 2×Radius).
///
/// Unlike a CSG union of Sphere+Cylinder+Sphere, the Capsule is a single
/// primitive with continuous UV mapping across the entire surface, no seam
/// artifacts at the hemisphere-cylinder junctions, and a single Hit() call.
///
/// <b>UV mapping:</b>
///   The UV layout stitches together seamlessly:
///     V ∈ [0.00, 0.25]  — bottom hemisphere (south pole at V=0)
///     V ∈ [0.25, 0.75]  — cylindrical body
///     V ∈ [0.75, 1.00]  — top hemisphere (north pole at V=1)
///   U = θ / 2π  (azimuthal angle around Y, same as Sphere and Cylinder)
///
/// <b>TBN basis:</b>
///   Tangent = azimuthal direction (∂P/∂θ), same formula everywhere.
///   Bitangent = cross(Normal, Tangent) — points in the direction of increasing V.
///
/// Implements ISamplable for use as an emissive area light with NEE.
/// </summary>
public class Capsule : IHittable, ISamplable
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public float Height { get; }
    public IMaterial Material { get; }

    // Centers of the two hemispheres
    private readonly Vector3 _bottomCenter; // = Center
    private readonly Vector3 _topCenter;    // = Center + (0, Height, 0)
    private readonly float _yMin;           // = Center.Y
    private readonly float _yMax;           // = Center.Y + Height

    public Capsule(Vector3 center, float radius, float height, IMaterial material)
    {
        Center = center;
        Radius = radius;
        Height = height;
        Material = material;
        _bottomCenter = center;
        _topCenter = center + new Vector3(0, height, 0);
        _yMin = center.Y;
        _yMax = center.Y + height;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        bool hitAnything = false;

        // ═══════════════════════════════════════════════════════════════════
        // 1. Cylindrical body (same as Cylinder, but no caps)
        // ═══════════════════════════════════════════════════════════════════
        {
            float dx = ray.Direction.X;
            float dz = ray.Direction.Z;
            float ox = ray.Origin.X - Center.X;
            float oz = ray.Origin.Z - Center.Z;

            float a = dx * dx + dz * dz;
            float halfB = ox * dx + oz * dz;
            float c = ox * ox + oz * oz - Radius * Radius;

            float discriminant = halfB * halfB - a * c;
            if (discriminant >= 0 && a > 1e-10f)
            {
                float sqrtD = MathF.Sqrt(discriminant);
                for (int i = 0; i < 2; i++)
                {
                    float t = (-halfB + (i == 0 ? -sqrtD : sqrtD)) / a;
                    if (t < tMin || t > tMax) continue;

                    float y = ray.Origin.Y + t * ray.Direction.Y;
                    if (y < _yMin || y > _yMax) continue;

                    if (!hitAnything || t < rec.T)
                    {
                        Vector3 point = ray.At(t);
                        Vector3 outwardNormal = new Vector3(point.X - Center.X, 0, point.Z - Center.Z) / Radius;

                        rec.T = t;
                        rec.Point = point;
                        rec.LocalPoint = point;
                        rec.SetFaceNormal(ray, outwardNormal);

                        // UV: body occupies V ∈ [0.25, 0.75]
                        float theta = MathF.Atan2(point.Z - Center.Z, point.X - Center.X);
                        rec.U = (theta + MathF.PI) / (2f * MathF.PI);
                        rec.V = 0.25f + 0.5f * (y - _yMin) / Height;

                        SetTBN(ref rec, outwardNormal);
                        hitAnything = true;
                        tMax = t;
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // 2. Bottom hemisphere (center at _bottomCenter, below y = _yMin)
        // ═══════════════════════════════════════════════════════════════════
        HitHemisphere(ray, _bottomCenter, false, ref tMin, ref tMax, ref rec, ref hitAnything);

        // ═══════════════════════════════════════════════════════════════════
        // 3. Top hemisphere (center at _topCenter, above y = _yMax)
        // ═══════════════════════════════════════════════════════════════════
        HitHemisphere(ray, _topCenter, true, ref tMin, ref tMax, ref rec, ref hitAnything);

        return hitAnything;
    }

    /// <summary>
    /// Tests intersection with one hemisphere of the capsule.
    /// </summary>
    /// <param name="isTop">True for the top hemisphere (y ≥ hemiCenter.Y), false for bottom (y ≤ hemiCenter.Y).</param>
    private void HitHemisphere(Ray ray, Vector3 hemiCenter, bool isTop,
                                ref float tMin, ref float tMax,
                                ref HitRecord rec, ref bool hitAnything)
    {
        Vector3 oc = ray.Origin - hemiCenter;
        float a = ray.Direction.LengthSquared();
        float halfB = Vector3.Dot(oc, ray.Direction);
        float c = oc.LengthSquared() - Radius * Radius;
        float discriminant = halfB * halfB - a * c;

        if (discriminant < 0) return;

        float sqrtD = MathF.Sqrt(discriminant);
        for (int i = 0; i < 2; i++)
        {
            float t = (-halfB + (i == 0 ? -sqrtD : sqrtD)) / a;
            if (t < tMin || t > tMax) continue;

            Vector3 point = ray.At(t);
            float y = point.Y;

            // Clip: top hemisphere only above hemiCenter.Y, bottom only below
            if (isTop && y < hemiCenter.Y) continue;
            if (!isTop && y > hemiCenter.Y) continue;

            if (!hitAnything || t < rec.T)
            {
                Vector3 outwardNormal = (point - hemiCenter) / Radius;

                rec.T = t;
                rec.Point = point;
                rec.LocalPoint = point;
                rec.SetFaceNormal(ray, outwardNormal);

                // UV: hemispheres use spherical coordinates mapped to the
                // appropriate V range.
                float theta = MathF.Atan2(point.Z - hemiCenter.Z, point.X - hemiCenter.X);
                rec.U = (theta + MathF.PI) / (2f * MathF.PI);

                // Polar angle from the hemisphere axis
                float localY = (point.Y - hemiCenter.Y) / Radius; // [-1, 0] bottom, [0, 1] top
                localY = Math.Clamp(localY, -1f, 1f);

                if (isTop)
                {
                    // Top hemisphere: V goes from 0.75 (equator) to 1.0 (north pole)
                    // localY goes from 0 (equator) to 1 (pole)
                    rec.V = 0.75f + 0.25f * localY;
                }
                else
                {
                    // Bottom hemisphere: V goes from 0.0 (south pole) to 0.25 (equator)
                    // localY goes from -1 (pole) to 0 (equator)
                    rec.V = 0.25f * (localY + 1f);
                }

                SetTBN(ref rec, outwardNormal);
                hitAnything = true;
                tMax = t;
            }
        }
    }

    /// <summary>
    /// Sets the TBN basis using the same azimuthal tangent convention as Sphere/Cylinder.
    /// </summary>
    private void SetTBN(ref HitRecord rec, Vector3 outwardNormal)
    {
        // Tangent = azimuthal direction (cross Y × normal)
        Vector3 tDir = Vector3.Cross(Vector3.UnitY, outwardNormal);
        if (tDir.LengthSquared() < 1e-4f) tDir = Vector3.UnitX;
        rec.Tangent = Vector3.Normalize(tDir);
        rec.Bitangent = Vector3.Normalize(Vector3.Cross(outwardNormal, rec.Tangent));
        rec.ObjectSeed = Seed;
        rec.Material = Material;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISamplable — NEE support for emissive capsules
    //
    // Total area = cylinder lateral (2πRH) + full sphere (4πR²)
    // The two hemispheres together form one complete sphere.
    // ═════════════════════════════════════════════════════════════════════════

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        float cylinderArea = 2f * MathF.PI * Radius * Height;
        float sphereArea = 4f * MathF.PI * Radius * Radius;
        float totalArea = cylinderArea + sphereArea;

        float r = MathUtils.RandomFloat() * totalArea;

        if (r < cylinderArea)
        {
            // Sample on cylindrical body
            float theta = MathUtils.RandomFloat() * 2f * MathF.PI;
            float h = MathUtils.RandomFloat() * Height;
            float cosT = MathF.Cos(theta);
            float sinT = MathF.Sin(theta);

            Vector3 point = new(
                Center.X + Radius * cosT,
                _yMin + h,
                Center.Z + Radius * sinT);
            Vector3 normal = new(cosT, 0, sinT);
            return (point, normal, totalArea);
        }
        else
        {
            // Sample on the sphere (split into hemispheres)
            Vector3 randDir = MathUtils.RandomUnitVector();
            bool top = r < cylinderArea + sphereArea / 2f;

            // Force the direction into the correct hemisphere
            if (top && randDir.Y < 0) randDir.Y = -randDir.Y;
            if (!top && randDir.Y > 0) randDir.Y = -randDir.Y;

            Vector3 hemiCenter = top ? _topCenter : _bottomCenter;
            Vector3 point = hemiCenter + randDir * Radius;
            return (point, randDir, totalArea);
        }
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        var r = new Vector3(Radius);
        Vector3 min = Vector3.Min(_bottomCenter - r, _topCenter - r);
        Vector3 max = Vector3.Max(_bottomCenter + r, _topCenter + r);
        return new AABB(min, max);
    }
}
