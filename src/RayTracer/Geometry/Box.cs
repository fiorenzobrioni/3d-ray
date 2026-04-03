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

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        float tNear = float.NegativeInfinity;
        float tFar = float.PositiveInfinity;
        int nearAxis = -1;
        int farAxis = -1;
        bool nearNeg = false;
        bool farNeg = false;

        // Slab method intersection
        for (int a = 0; a < 3; a++)
        {
            float origin = a switch { 0 => ray.Origin.X, 1 => ray.Origin.Y, _ => ray.Origin.Z };
            float dir = a switch { 0 => ray.Direction.X, 1 => ray.Direction.Y, _ => ray.Direction.Z };
            float bmin = a switch { 0 => Min.X, 1 => Min.Y, _ => Min.Z };
            float bmax = a switch { 0 => Max.X, 1 => Max.Y, _ => Max.Z };

            if (MathF.Abs(dir) < 1e-8f)
            {
                if (origin < bmin || origin > bmax) return false;
            }
            else
            {
                float invD = 1f / dir;
                float t0 = (bmin - origin) * invD;
                float t1 = (bmax - origin) * invD;

                bool swapped = false;
                if (invD < 0f)
                {
                    (t0, t1) = (t1, t0);
                    swapped = true;
                }

                if (t0 > tNear) { tNear = t0; nearAxis = a; nearNeg = !swapped; }
                if (t1 < tFar) { tFar = t1; farAxis = a; farNeg = swapped; }

                if (tFar <= tNear) return false;
            }
        }

        float tResult;
        int hitAxis;
        bool hitNeg;

        if (tNear >= tMin && tNear <= tMax)
        {
            tResult = tNear;
            hitAxis = nearAxis;
            hitNeg = nearNeg;
        }
        else if (tFar >= tMin && tFar <= tMax)
        {
            tResult = tFar;
            hitAxis = farAxis;
            hitNeg = farNeg;
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

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        // Total surface area of the unit cube: 6 faces × 1 = 6
        const float totalArea = 6f;

        int face = (int)(MathUtils.RandomFloat() * 6f);
        if (face > 5) face = 5; // Guard against RandomFloat() returning exactly 1.0

        float u = MathUtils.RandomFloat() - 0.5f; // [-0.5, 0.5]
        float v = MathUtils.RandomFloat() - 0.5f;

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

        return (point, normal, totalArea);
    }

    public int Seed { get; set; }

    public AABB BoundingBox() => new(Min, Max);
}
