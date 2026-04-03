using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// Torus (donut / ring) primitive centered at the origin, lying in the XZ plane.
/// The hole axis is Y. Use the Transform wrapper to position, rotate, and scale.
///
/// <b>Parameters:</b>
///   <c>MajorRadius</c> (R) — distance from the torus center to the center of the tube.
///   <c>MinorRadius</c> (r) — radius of the tube itself.
///   When R > r the torus has a visible hole (ring torus).
///   When R = r the hole vanishes (horn torus).
///   When R &lt; r the torus self-intersects (spindle torus).
///
/// <b>Implicit equation:</b>
///   (x² + y² + z² + R² - r²)² = 4R²(x² + z²)
///
/// <b>Ray intersection:</b>
///   Substituting the ray P(t) = O + tD into the implicit equation yields a
///   quartic (degree 4) polynomial in t, solved analytically via Ferrari's method
///   (<see cref="QuarticSolver"/>). A torus can produce 0, 2, or 4 intersections
///   per ray.
///
/// <b>UV mapping (toroidal coordinates):</b>
///   U = φ / 2π  — azimuthal angle around the Y axis (major circle)
///   V = θ / 2π  — poloidal angle around the tube cross-section
///   Both mapped from [0, 1]. This gives a natural "latitude/longitude" on the
///   torus surface, ideal for texturing rings, tires, and pipes.
///
/// <b>TBN basis:</b>
///   Tangent  = ∂P/∂φ (direction of increasing U, around the major circle)
///   Bitangent = ∂P/∂θ (direction of increasing V, around the tube)
///   Normal = outward surface normal, perpendicular to both.
///
/// <b>CSG compatibility:</b>
///   The torus is non-convex and can produce up to 4 surface intersections per
///   ray. The CSG engine's CollectAllHits (MaxHitsPerChild = 16) handles this
///   correctly — no special cases needed.
///
/// Implements ISamplable for use as an emissive area light with NEE.
/// Surface area = 4π²Rr.
/// </summary>
public class Torus : IHittable, ISamplable
{
    /// <summary>Distance from the torus center to the center of the tube.</summary>
    public float MajorRadius { get; }

    /// <summary>Radius of the tube.</summary>
    public float MinorRadius { get; }

    public IMaterial Material { get; }

    // Precomputed squares for the intersection math
    private readonly double _R2;
    private readonly double _r2;
    private readonly double _R;
    private readonly double _r;

    public Torus(float majorRadius, float minorRadius, IMaterial material)
    {
        MajorRadius = majorRadius;
        MinorRadius = minorRadius;
        Material = material;
        _R = majorRadius;
        _r = minorRadius;
        _R2 = (double)majorRadius * majorRadius;
        _r2 = (double)minorRadius * minorRadius;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // ═══════════════════════════════════════════════════════════════════
        // Build the quartic coefficients
        //
        // The torus implicit equation:
        //   (x² + y² + z² + R² − r²)² = 4R²(x² + z²)
        //
        // Substituting P(t) = O + tD gives a quartic in t:
        //   c4·t⁴ + c3·t³ + c2·t² + c1·t + c0 = 0
        // ═══════════════════════════════════════════════════════════════════

        double ox = ray.Origin.X, oy = ray.Origin.Y, oz = ray.Origin.Z;
        double dx = ray.Direction.X, dy = ray.Direction.Y, dz = ray.Direction.Z;

        // D·D, O·D, O·O
        double dd = dx * dx + dy * dy + dz * dz;
        double od = ox * dx + oy * dy + oz * dz;
        double oo = ox * ox + oy * oy + oz * oz;

        // XZ-plane components only (for the 4R² term)
        double dxz2 = dx * dx + dz * dz;
        double odxz = ox * dx + oz * dz;
        double oxz2 = ox * ox + oz * oz;

        // K = O·O + R² − r²
        double K = oo + _R2 - _r2;

        // Quartic coefficients (not yet monic)
        double c4 = dd * dd;
        double c3 = 4.0 * dd * od;
        double c2 = 4.0 * od * od + 2.0 * dd * K - 4.0 * _R2 * dxz2;
        double c1 = 4.0 * od * K - 8.0 * _R2 * odxz;
        double c0 = K * K - 4.0 * _R2 * oxz2;

        // Solve the quartic
        Span<double> roots = stackalloc double[4];
        int count = QuarticSolver.SolveQuartic(c4, c3, c2, c1, c0, roots, tMin, tMax);

        if (count == 0)
            return false;

        // The roots are sorted ascending — the first valid root is the closest hit
        // Try each root (QuarticSolver may return roots slightly outside [tMin,tMax]
        // due to floating point, but we've already filtered in the solver)
        for (int i = 0; i < count; i++)
        {
            float t = (float)roots[i];
            if (t < tMin || t > tMax)
                continue;

            Vector3 point = ray.At(t);
            FillHitRecord(ref rec, ray, point, t);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fills the HitRecord with normal, UV, and TBN for a hit point on the torus.
    /// </summary>
    private void FillHitRecord(ref HitRecord rec, Ray ray, Vector3 point, float t)
    {
        rec.T = t;
        rec.Point = point;
        rec.LocalPoint = point;

        float px = point.X, py = point.Y, pz = point.Z;

        // ─────────────────────────────────────────────────────────────────
        // Normal: computed geometrically from the tube center
        //
        // The major circle lies in the XZ plane at distance R from origin.
        // The closest point on the ring to P is:
        //   Q = R · normalize(px, 0, pz)
        // Then normal = normalize(P - Q).
        // ─────────────────────────────────────────────────────────────────
        float dxz = MathF.Sqrt(px * px + pz * pz);
        float invDxz = dxz > 1e-8f ? 1f / dxz : 0f;

        // Direction from origin to P projected onto XZ (unit radial vector)
        float radX = px * invDxz;
        float radZ = pz * invDxz;

        // Tube center for this cross-section
        float qx = MajorRadius * radX;
        float qz = MajorRadius * radZ;

        // Vector from tube center to surface point
        float nx = px - qx;
        float ny = py;
        float nz = pz - qz;
        Vector3 outwardNormal = Vector3.Normalize(new Vector3(nx, ny, nz));

        rec.SetFaceNormal(ray, outwardNormal);

        // ─────────────────────────────────────────────────────────────────
        // UV mapping (toroidal coordinates)
        //
        //   φ = atan2(pz, px)         — azimuthal angle (around Y axis)
        //   θ = atan2(py, dxz - R)    — poloidal angle (around tube)
        //
        //   U = (φ + π) / 2π    ∈ [0, 1]
        //   V = (θ + π) / 2π    ∈ [0, 1]
        // ─────────────────────────────────────────────────────────────────
        float phi = MathF.Atan2(pz, px);
        float theta = MathF.Atan2(py, dxz - MajorRadius);

        rec.U = (phi + MathF.PI) / (2f * MathF.PI);
        rec.V = (theta + MathF.PI) / (2f * MathF.PI);

        // ─────────────────────────────────────────────────────────────────
        // TBN basis
        //
        // Tangent = ∂P/∂φ direction (around the major circle, increasing U)
        //   = (-sin φ, 0, cos φ)   (same as cylinder/sphere azimuthal)
        //
        // Bitangent = ∂P/∂θ direction (around the tube, increasing V)
        //   = (-cos φ · sin θ, cos θ, -sin φ · sin θ)
        // ─────────────────────────────────────────────────────────────────
        float sinPhi = MathF.Sin(phi);
        float cosPhi = MathF.Cos(phi);
        float sinTheta = MathF.Sin(theta);
        float cosTheta = MathF.Cos(theta);

        rec.Tangent = Vector3.Normalize(new Vector3(-sinPhi, 0, cosPhi));
        rec.Bitangent = Vector3.Normalize(new Vector3(
            -cosPhi * sinTheta,
            cosTheta,
            -sinPhi * sinTheta));

        rec.ObjectSeed = Seed;
        rec.Material = Material;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISamplable — NEE support for emissive tori
    //
    // Total surface area = 4π²Rr
    // Uniform sampling: pick φ and θ uniformly in [0, 2π), compute point
    // and normal from toroidal coordinates.
    // ═════════════════════════════════════════════════════════════════════════

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        float phi = MathUtils.RandomFloat() * 2f * MathF.PI;
        float theta = MathUtils.RandomFloat() * 2f * MathF.PI;

        float cosPhi = MathF.Cos(phi);
        float sinPhi = MathF.Sin(phi);
        float cosTheta = MathF.Cos(theta);
        float sinTheta = MathF.Sin(theta);

        float rr = MajorRadius + MinorRadius * cosTheta;

        Vector3 point = new(
            rr * cosPhi,
            MinorRadius * sinTheta,
            rr * sinPhi);

        Vector3 normal = new(
            cosTheta * cosPhi,
            sinTheta,
            cosTheta * sinPhi);

        float area = 4f * MathF.PI * MathF.PI * MajorRadius * MinorRadius;

        return (point, normal, area);
    }

    public int Seed { get; set; }

    public AABB BoundingBox()
    {
        // The torus extends from -(R+r) to +(R+r) in X and Z,
        // and from -r to +r in Y.
        float extent = MajorRadius + MinorRadius;
        return new AABB(
            new Vector3(-extent, -MinorRadius, -extent),
            new Vector3(extent, MinorRadius, extent));
    }
}
