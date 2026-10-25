using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

public class Sphere : IHittable, ISamplable, ISolidAngleSamplable
{
    public Vector3 Center { get; }
    public float Radius { get; }
    public IMaterial Material { get; }

    public Sphere(Vector3 center, float radius, IMaterial material)
    {
        Center = center;
        Radius = radius;
        Material = material;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        Vector3 oc = ray.Origin - Center;
        float a = ray.Direction.LengthSquared();
        float halfB = Vector3.Dot(oc, ray.Direction);
        float rSq = Radius * Radius;
        float c = oc.LengthSquared() - rSq;

        // Discriminant from the ray's perpendicular distance to the centre,
        // r² − |l|², instead of b² − ac: for a small or distant sphere b² and
        // ac are nearly equal and their float difference loses every digit
        // (t off by ~5e-5 at R = 0.01 seen from 3 units, enough for a sphere
        // light to shadow itself). Ray Tracing Gems ch. 7, PBRT-v4.
        Vector3 l = oc - (halfB / a) * ray.Direction;
        float discriminant = a * (rSq - l.LengthSquared());
        if (discriminant < 0) return false;

        // Stable roots: never subtract two nearly equal numbers.
        float sqrtD = MathF.Sqrt(discriminant);
        float q = halfB > 0f ? -halfB - sqrtD : -halfB + sqrtD;
        float tA = q / a;
        float tB = q != 0f ? c / q : tA;
        float near = MathF.Min(tA, tB), far = MathF.Max(tA, tB);

        float root = near;
        if (root < tMin || root > tMax)
        {
            root = far;
            if (root < tMin || root > tMax)
                return false;
        }

        rec.T = root;
        rec.Point = ray.At(rec.T);
        // Object-local frame: origin at sphere centre, axes world-aligned (the
        // canonical wood/marble/noise sampling space, parity with Arnold's
        // `space: object`, Cycles' "Texture Coordinate → Object", RenderMan
        // Pref). Lets every procedural that depends on radial distance / axis
        // (wood, marble, gradient, coordinate) tile per-entity instead of
        // collapsing into the world-axis pattern.
        rec.LocalPoint = rec.Point - Center;
        Vector3 outwardNormal = (rec.Point - Center) / Radius;
        rec.SetFaceNormal(ray, outwardNormal);
        
        var (u, v) = GetSphereUV(outwardNormal);
        rec.U = u;
        rec.V = v;

        // Tangent points in direction of increasing U (phi).
        Vector3 tDir = Vector3.Cross(Vector3.UnitY, outwardNormal);
        if (tDir.LengthSquared() < 1e-4f) tDir = Vector3.UnitX;
        rec.Tangent = Vector3.Normalize(tDir);
        // Bitangent points in direction of increasing V (theta, downwards)
        rec.Bitangent = Vector3.Normalize(Vector3.Cross(outwardNormal, rec.Tangent));

        // ∂P/∂u, ∂P/∂v for the sphere parametrization phi = 2πu, theta = πv.
        // |∂P/∂u| = 2πR·sinθ collapses to zero at the poles; |∂P/∂v| = πR
        // is constant. The renderer's footprint solver handles the polar
        // degeneracy by falling back to the unit Tangent when the magnitudes
        // become singular.
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - outwardNormal.Y * outwardNormal.Y));
        rec.DpDu = rec.Tangent * (2f * MathF.PI * Radius * sinTheta);
        rec.DpDv = rec.Bitangent * (MathF.PI * Radius);

        rec.ObjectSeed = Seed;

        rec.Material = Material;
        return true;
    }

    /// <inheritdoc/>
    public float SurfaceArea => 4f * MathF.PI * Radius * Radius;

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        Vector3 p = MathUtils.RandomUnitVector();
        float area = 4f * MathF.PI * Radius * Radius;
        var (u, v) = GetSphereUV(p);
        return (Center + p * Radius, p, new Vector2(u, v), area);
    }

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sampleCount)
    {

        // Stratified sampling in (cosTheta, phi) space — uniform on the sphere.
        // cosTheta ∈ [-1, 1], phi ∈ [0, 2π]
        var (xs1, xs2) = MathUtils.Stratum2D(sampleIndex, sampleCount);
        float cosTheta = 1f - 2f * xs1;
        float phi = 2f * MathF.PI * xs2;

        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        Vector3 p = new(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);

        float area = 4f * MathF.PI * Radius * Radius;
        var (u, v) = GetSphereUV(p);
        return (Center + p * Radius, p, new Vector2(u, v), area);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ISolidAngleSamplable — cone sampling for efficient NEE on emissive spheres
    //
    // Strategy (Shirley 1996 / PBRT §14.2.2): when the observer is outside the
    // sphere, sample uniformly in solid angle within the cone of half-angle
    // θ_max subtended by the sphere. This concentrates all samples on the
    // visible cap, matching what SphereLight does, and gives an order-of-
    // magnitude variance reduction for small / distant emitters compared to
    // uniform area sampling + back-hemisphere rejection.
    //
    // PDF (solid-angle measure, w.r.t. the observer):
    //    1 / (2π · (1 − cos θ_max))      (outside)
    //    1 / (4π)                         (inside — fallback to uniform sphere)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float SolidAnglePdf)
        SampleSolidAngle(Vector3 from)
    {
        var (r2u, r2v) = MathUtils.Random2D();
        return SampleSolidAngleCore(from, r2u, r2v);
    }

    /// <inheritdoc/>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float SolidAnglePdf)
        SampleSolidAngleStratified(Vector3 from, int sampleIndex, int sampleCount)
    {
        var (xs1, xs2) = MathUtils.Stratum2D(sampleIndex, sampleCount);
        float xi1 = xs1;
        float xi2 = xs2;
        return SampleSolidAngleCore(from, xi1, xi2);
    }

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float SolidAnglePdf)
        SampleSolidAngleCore(Vector3 from, float xi1, float xi2)
    {
        Vector3 toCenter = Center - from;
        float distSq = toCenter.LengthSquared();
        float rSq = Radius * Radius;

        // Observer inside (or exactly on) the sphere — full sphere is visible.
        // Fall back to uniform sphere sampling with the corresponding
        // solid-angle pdf 1/(4π).
        if (distSq <= rSq)
        {
            Vector3 pUnit = MathUtils.RandomUnitVector();
            var (ufu, vfu) = GetSphereUV(pUnit);
            return (Center + pUnit * Radius, pUnit, new Vector2(ufu, vfu),
                    1f / (4f * MathF.PI));
        }

        float dist = MathF.Sqrt(distSq);
        Vector3 wDir = toCenter / dist; // direction from observer to centre

        // Cone half-angle
        float sinThetaMaxSq = rSq / distSq;
        float cosThetaMax = MathF.Sqrt(MathF.Max(0f, 1f - sinThetaMaxSq));

        // Sample the visible cap geometrically (PBRT-v4 Sphere::Sample): θ is
        // uniform in solid angle inside the cone and the surface point is placed
        // through the angle α it subtends at the centre, instead of intersecting
        // a ray with the sphere, which misses near the rim in single precision.
        // Below sin²θ_max ≈ sin²(1.5°) the small-angle forms keep 1 − cos θ_max
        // from underflowing (PIANO-REVIEW.md §3.19).
        float sinThetaMax = MathF.Sqrt(sinThetaMaxSq);
        float cosTheta, sin2Theta, oneMinusCosThetaMax;
        if (sinThetaMaxSq < SmallConeSin2)
        {
            sin2Theta = sinThetaMaxSq * xi1;
            cosTheta = MathF.Sqrt(1f - sin2Theta);
            oneMinusCosThetaMax = 0.5f * sinThetaMaxSq;
        }
        else
        {
            cosTheta = 1f - xi1 * (1f - cosThetaMax);
            sin2Theta = MathF.Max(0f, 1f - cosTheta * cosTheta);
            oneMinusCosThetaMax = 1f - cosThetaMax;
        }
        float cosAlpha = sin2Theta / sinThetaMax
                       + cosTheta * MathF.Sqrt(MathF.Max(0f, 1f - sin2Theta / sinThetaMaxSq));
        float sinAlpha = MathF.Sqrt(MathF.Max(0f, 1f - cosAlpha * cosAlpha));
        float phi = 2f * MathF.PI * xi2;
        // α is measured from the centre toward the observer (−wDir).
        Vector3 normal = LocalToWorld(
            new Vector3(sinAlpha * MathF.Cos(phi), sinAlpha * MathF.Sin(phi), cosAlpha), -wDir);
        Vector3 samplePoint = Center + Radius * normal;
        var (u, v) = GetSphereUV(normal);

        float solidAngle = 2f * MathF.PI * oneMinusCosThetaMax;
        float pdf = solidAngle > 0f ? 1f / solidAngle : 0f;

        return (samplePoint, normal, new Vector2(u, v), pdf);
    }

    /// <inheritdoc/>
    public float SolidAnglePdf(Vector3 from, Vector3 wi)
    {
        Vector3 toCenter = Center - from;
        float distSq = toCenter.LengthSquared();
        float rSq = Radius * Radius;

        if (distSq <= rSq)
            return 1f / (4f * MathF.PI); // observer inside — uniform sphere

        float sinThetaMaxSq = rSq / distSq;
        float cosThetaMax = MathF.Sqrt(MathF.Max(0f, 1f - sinThetaMaxSq));

        // wi must lie inside the cone around (Center - from).
        float dist = MathF.Sqrt(distSq);
        Vector3 wDir = toCenter / dist;
        float wiLen = wi.Length();
        if (wiLen < MathUtils.Epsilon) return 0f;
        float cosTheta = Vector3.Dot(wi, wDir) / wiLen;

        if (cosTheta < cosThetaMax) return 0f;

        float oneMinusCosThetaMax = sinThetaMaxSq < SmallConeSin2
            ? 0.5f * sinThetaMaxSq
            : 1f - cosThetaMax;
        float solidAngle = 2f * MathF.PI * oneMinusCosThetaMax;
        return solidAngle > 0f ? 1f / solidAngle : 0f;
    }

    // sin²(1.5°): below it 1 − cos θ_max underflows in single precision.
    private const float SmallConeSin2 = 0.00068523f;

    /// <summary>
    /// Builds a right-handed orthonormal basis with <paramref name="w"/> as the
    /// Z axis (<see cref="MathUtils.OrthonormalBasis"/>) and transforms
    /// <paramref name="local"/> into world space.
    /// </summary>
    private static Vector3 LocalToWorld(Vector3 local, Vector3 w)
    {
        Vector3 result = MathUtils.LocalToWorld(local, w);
        float lenSq = result.LengthSquared();
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : w;
    }

    public int Seed { get; set; }

    private static (float U, float V) GetSphereUV(Vector3 p)
    {
        // p: un punto sulla sfera unitaria centrata nell'origine
        float theta = MathF.Acos(-p.Y);
        float phi = MathF.Atan2(-p.Z, p.X) + MathF.PI;

        float u = phi / (2 * MathF.PI);
        float v = theta / MathF.PI;
        return (u, v);
    }

    public AABB BoundingBox()
    {
        var r = new Vector3(Radius);
        return new AABB(Center - r, Center + r);
    }
}
