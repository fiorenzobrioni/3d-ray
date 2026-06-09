using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A unit cube centered at the origin (from -0.5 to 0.5 on all axes).
/// Use the Transform wrapper to scale, rotate and move it.
///
/// Implements ISamplable for use as an emissive area light with NEE.
/// Samples uniformly across all 6 faces (each face has equal area on
/// the unit cube; non-uniform scaling is handled by the Transform wrapper's
/// Jacobian-based area correction).
/// </summary>
public class Box : IHittable, ISamplable
{
    private static readonly Vector3 Min = new(-0.5f);
    private static readonly Vector3 Max = new(0.5f);
    
    public IMaterial Material { get; }

    public Box(IMaterial material)
    {
        Material = material;
    }

    private static float AxisComponent(Vector3 v, int axis)
        => axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Branchless SIMD slab test against the unit cube, using the ray's
        // precomputed InvDirection (no per-axis division or component switch).
        // InvDirection encodes ±∞ for axis-parallel rays, which Min/Max resolve
        // correctly — same trick as AABB.Hit — so no explicit near-zero-direction
        // branch is needed.
        Vector3 o = ray.Origin;
        Vector3 invD = ray.InvDirection;

        Vector3 t0v = (Min - o) * invD;
        Vector3 t1v = (Max - o) * invD;
        Vector3 tSmall = Vector3.Min(t0v, t1v);
        Vector3 tLarge = Vector3.Max(t0v, t1v);

        float tNear = MathF.Max(tSmall.X, MathF.Max(tSmall.Y, tSmall.Z));
        float tFar  = MathF.Min(tLarge.X, MathF.Min(tLarge.Y, tLarge.Z));

        if (tFar <= tNear) return false;

        float tResult;
        int hitAxis;
        bool hitNeg;

        if (tNear >= tMin && tNear <= tMax)
        {
            tResult = tNear;
            // Entry face: the axis whose entry-t equals tNear (ties → first axis,
            // matching the previous strict-`>` accumulation). The ray crosses the
            // negative (Min) face first when it travels in +axis (invD > 0).
            hitAxis = tNear == tSmall.X ? 0 : (tNear == tSmall.Y ? 1 : 2);
            hitNeg  = AxisComponent(invD, hitAxis) > 0f;
        }
        else if (tFar >= tMin && tFar <= tMax)
        {
            tResult = tFar;
            // Exit face: opposite sign convention to the entry.
            hitAxis = tFar == tLarge.X ? 0 : (tFar == tLarge.Y ? 1 : 2);
            hitNeg  = AxisComponent(invD, hitAxis) < 0f;
        }
        else
        {
            return false;
        }

        rec.T = tResult;
        rec.Point = ray.At(tResult);
        rec.LocalPoint = rec.Point;

        // Compute outward normal based on which axis slab was hit
        Vector3 outwardNormal = hitAxis switch
        {
            0 => hitNeg ? -Vector3.UnitX : Vector3.UnitX,
            1 => hitNeg ? -Vector3.UnitY : Vector3.UnitY,
            _ => hitNeg ? -Vector3.UnitZ : Vector3.UnitZ
        };

        rec.SetFaceNormal(ray, outwardNormal);

        // Per-face planar UV mapping on the unit cube.
        //
        // BUG-09 fix: Tangent and Bitangent are now chosen so that
        // Cross(T, B) is aligned with the outward normal for every face,
        // guaranteeing a right-handed TBN frame. Previously T and B were
        // fixed per axis without considering the face sign, producing a
        // left-handed frame on 3 out of 6 faces (+X, +Y, −Z). This caused
        // the Z component of normal maps (the "out of surface" direction)
        // to be inverted on those faces — bumps appeared as dents and
        // vice versa.
        //
        // Convention per face:
        //   +X: T = +Z, B = +Y  → Cross(+Z, +Y) = −X … flip B → −Y  ✓ Cross(+Z,−Y) = +X
        //   −X: T = −Z, B = +Y  → Cross(−Z, +Y) = +X … flip B → −Y  ✓ Cross(−Z,−Y) = −X
        //   +Y: T = +X, B = −Z  → Cross(+X,−Z) = +Y  ✓
        //   −Y: T = +X, B = +Z  → Cross(+X,+Z) = −Y  ✓
        //   +Z: T = +X, B = +Y  → Cross(+X,+Y) = +Z  ✓
        //   −Z: T = −X, B = +Y  → Cross(−X,+Y) = −Z  ✓
        switch (hitAxis)
        {
            case 0: // X faces
                rec.U = (rec.Point.Z - Min.Z) / (Max.Z - Min.Z);
                rec.V = (rec.Point.Y - Min.Y) / (Max.Y - Min.Y);
                if (!hitNeg) // +X face: outward = +X
                {
                    rec.Tangent   = Vector3.UnitZ;
                    rec.Bitangent = -Vector3.UnitY;
                }
                else // −X face: outward = −X
                {
                    rec.Tangent   = -Vector3.UnitZ;
                    rec.Bitangent = -Vector3.UnitY;
                }
                break;
            case 1: // Y faces
                rec.U = (rec.Point.X - Min.X) / (Max.X - Min.X);
                rec.V = (rec.Point.Z - Min.Z) / (Max.Z - Min.Z);
                if (!hitNeg) // +Y face: outward = +Y
                {
                    rec.Tangent   = Vector3.UnitX;
                    rec.Bitangent = -Vector3.UnitZ;
                }
                else // −Y face: outward = −Y
                {
                    rec.Tangent   = Vector3.UnitX;
                    rec.Bitangent = Vector3.UnitZ;
                }
                break;
            default: // Z faces
                rec.U = (rec.Point.X - Min.X) / (Max.X - Min.X);
                rec.V = (rec.Point.Y - Min.Y) / (Max.Y - Min.Y);
                if (!hitNeg) // +Z face: outward = +Z
                {
                    rec.Tangent   = Vector3.UnitX;
                    rec.Bitangent = Vector3.UnitY;
                }
                else // −Z face: outward = −Z
                {
                    rec.Tangent   = -Vector3.UnitX;
                    rec.Bitangent = Vector3.UnitY;
                }
                break;
        }

        rec.ObjectSeed = Seed;
        rec.Material = Material;
        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISamplable — NEE support for emissive boxes
    //
    // The unit cube has 6 faces, each of area 1×1 = 1, for a total of 6.
    // We pick a face uniformly at random, then sample a uniform point on it.
    // The Transform wrapper handles non-uniform scaling via the Jacobian.
    // ═════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    // Unit cube: 6 faces × (1×1) = 6. Non-uniform scaling is handled by the
    // Transform wrapper's Jacobian — see Transform.SurfaceArea.
    public float SurfaceArea => 6f;

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        int face = (int)(MathUtils.RandomFloat() * 6f);
        if (face > 5) face = 5; // Guard against RandomFloat() returning exactly 1.0
        return SampleFace(face, MathUtils.RandomFloat(), MathUtils.RandomFloat());
    }

    /// <summary>
    /// Stratified version: the 6 faces × sqrtSamples² cells form a uniform
    /// grid. The sample index is split into a face index (round-robin) and a
    /// jittered (u, v) within the face. Dividing the total strata across faces
    /// keeps each face's share of samples proportional to its area, and each
    /// face sees its own sub-grid of stratified points.
    /// </summary>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        int face = sampleIndex % 6;
        int within = sampleIndex / 6;
        int su = within % sqrtSamples;
        int sv = within / sqrtSamples;
        float inv = 1f / sqrtSamples;
        float xi1 = (su + MathUtils.RandomFloat()) * inv;
        float xi2 = (sv + MathUtils.RandomFloat()) * inv;
        return SampleFace(face, xi1, xi2);
    }

    private static (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleFace(int face, float xi1, float xi2)
    {
        // Total surface area of the unit cube: 6 faces × 1 = 6
        const float totalArea = 6f;

        float u = xi1 - 0.5f; // [-0.5, 0.5]
        float v = xi2 - 0.5f;

        Vector3 point = face switch
        {
            0 => new Vector3( 0.5f, u, v),     // +X
            1 => new Vector3(-0.5f, u, v),     // −X
            2 => new Vector3(u,  0.5f, v),     // +Y
            3 => new Vector3(u, -0.5f, v),     // −Y
            4 => new Vector3(u, v,  0.5f),     // +Z
            _ => new Vector3(u, v, -0.5f),     // −Z
        };

        Vector3 normal = face switch
        {
            0 =>  Vector3.UnitX,
            1 => -Vector3.UnitX,
            2 =>  Vector3.UnitY,
            3 => -Vector3.UnitY,
            4 =>  Vector3.UnitZ,
            _ => -Vector3.UnitZ,
        };

        // Uniform UV in [0,1]² over each face — a reasonable default for Box's
        // procedural/solid emissives. Box's Hit() does not define per-face UVs,
        // so this is not expected to coincide with any artist UV atlas.
        return (point, normal, new Vector2(xi1, xi2), totalArea);
    }

    public int Seed { get; set; }

    public AABB BoundingBox() => new(Min, Max);
}
