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

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        Vector3 oc = ray.Origin - Center;
        float a = ray.Direction.LengthSquared();
        float halfB = Vector3.Dot(oc, ray.Direction);
        float c = oc.LengthSquared() - Radius * Radius;
        float discriminant = halfB * halfB - a * c;

        if (discriminant < 0) return false;

        float sqrtD = MathF.Sqrt(discriminant);
        float root = (-halfB - sqrtD) / a;
        if (root < tMin || root > tMax)
        {
            root = (-halfB + sqrtD) / a;
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

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        float inv = 1f / sqrtSamples;
        int su = sampleIndex % sqrtSamples;
        int sv = sampleIndex / sqrtSamples;

        // Stratified sampling in (cosTheta, phi) space — uniform on the sphere.
        // cosTheta ∈ [-1, 1], phi ∈ [0, 2π]
        float cosTheta = 1f - 2f * (su + MathUtils.RandomFloat()) * inv;
        float phi = 2f * MathF.PI * (sv + MathUtils.RandomFloat()) * inv;

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
        => SampleSolidAngleCore(from, MathUtils.RandomFloat(), MathUtils.RandomFloat());

    /// <inheritdoc/>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float SolidAnglePdf)
        SampleSolidAngleStratified(Vector3 from, int sampleIndex, int sqrtSamples)
    {
        float inv = 1f / sqrtSamples;
        int su = sampleIndex % sqrtSamples;
        int sv = sampleIndex / sqrtSamples;
        float xi1 = (su + MathUtils.RandomFloat()) * inv;
        float xi2 = (sv + MathUtils.RandomFloat()) * inv;
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

        // Uniform sampling inside the cone (solid-angle measure)
        float cosTheta = 1f - xi1 * (1f - cosThetaMax);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * xi2;

        Vector3 local = new(
            sinTheta * MathF.Cos(phi),
            sinTheta * MathF.Sin(phi),
            cosTheta);
        Vector3 dirWorld = LocalToWorld(local, wDir);

        // Intersect ray (from, dirWorld) with the sphere.
        Vector3 oc = from - Center;
        float b = Vector3.Dot(oc, dirWorld);
        float c = oc.LengthSquared() - rSq;
        float disc = b * b - c;

        float t;
        if (disc <= 0f)
        {
            // Tangent ray — project observer → surface along dirWorld.
            // Numerically rare; clamp to the foot of the perpendicular.
            t = -b;
        }
        else
        {
            float sqrtD = MathF.Sqrt(disc);
            t = -b - sqrtD;                              // near hit
            if (t < MathUtils.Epsilon) t = -b + sqrtD;   // far hit (observer inside fallback)
        }

        Vector3 samplePoint = from + t * dirWorld;
        Vector3 normal = Vector3.Normalize(samplePoint - Center);
        var (u, v) = GetSphereUV(normal);

        float solidAngle = 2f * MathF.PI * (1f - cosThetaMax);
        float pdf = solidAngle > MathUtils.Epsilon ? 1f / solidAngle : 0f;

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

        float solidAngle = 2f * MathF.PI * (1f - cosThetaMax);
        return solidAngle > MathUtils.Epsilon ? 1f / solidAngle : 0f;
    }

    /// <summary>
    /// Builds a right-handed orthonormal basis with <paramref name="w"/> as the
    /// Z axis (Frisvad's branch-free construction, with south-pole guard) and
    /// transforms <paramref name="local"/> into world space. Kept in sync with
    /// the equivalent helper in <see cref="Lights.SphereLight"/>.
    /// </summary>
    private static Vector3 LocalToWorld(Vector3 local, Vector3 w)
    {
        Vector3 u, v;
        if (w.Z < -0.999f)
        {
            u = new Vector3(0f, -1f, 0f);
            v = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + w.Z);
            float b = -w.X * w.Y * a;
            u = new Vector3(1f - w.X * w.X * a, b, -w.X);
            v = new Vector3(b, 1f - w.Y * w.Y * a, -w.Y);
        }

        Vector3 result = local.X * u + local.Y * v + local.Z * w;
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
