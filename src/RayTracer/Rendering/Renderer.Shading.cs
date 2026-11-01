using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

// Renderer, part: Normal and bump maps.
public partial class Renderer
{
    /// <summary>
    /// Perturbs the shading normal using a tangent-space normal map.
    ///
    /// The normal map stores normals in tangent space where (0, 0, 1)
    /// means "unperturbed — same as the geometric normal". The TBN matrix
    /// transforms this into world space:
    ///
    ///   worldNormal = T * mapNormal.X + B * mapNormal.Y + N * mapNormal.Z
    ///
    /// The result replaces rec.Normal, affecting all subsequent shading.
    /// </summary>
    private static void ApplyNormalMap(ref HitRecord rec, NormalMapTexture normalMap)
    {
        // Sample the normal map at the hit UV
        Vector3 tsNormal = normalMap.SampleNormal(rec.U, rec.V);
 
        Vector3 T = rec.Tangent;
        Vector3 B = rec.Bitangent;
        Vector3 N = rec.Normal;

        // Gram-Schmidt orthogonalization to ensure T is exactly perpendicular to N.
        Vector3 tOrt = T - Vector3.Dot(T, N) * N;
        if (tOrt.LengthSquared() > 1e-8f)
            T = Vector3.Normalize(tOrt);
        
        // Orthogonalize B against N and T to preserve its original intended 
        // parametric direction while making it purely orthogonal to the basis.
        Vector3 bOrt = B - Vector3.Dot(B, N) * N - Vector3.Dot(B, T) * T;
        if (bOrt.LengthSquared() > 1e-8f)
            B = Vector3.Normalize(bOrt);

        // If the normal N was flipped because we hit a backface (inside of the object),
        // we must also flip T and B to prevent the tangent space from changing handedness,
        // which would otherwise visually invert the normal map bumps.
        if (!rec.FrontFace)
        {
            T = -T;
            B = -B;
        }
 
        Vector3 perturbedNormal = Vector3.Normalize(
            T * tsNormal.X + B * tsNormal.Y + N * tsNormal.Z
        );

        rec.Normal = perturbedNormal;
    }

    /// <summary>
    /// How a unit step along the world tangent <paramref name="t"/> and
    /// bitangent <paramref name="b"/> moves a texture's inputs at this hit:
    /// (u, v) through the inverse of the surface's ∂P/∂u, ∂P/∂v (least
    /// squares on the tangent plane), and the 3D point through the inverse of
    /// the rotation the transforms took it out of (<see cref="HitRecord.LocalToWorld"/>).
    /// A primitive without ∂P/∂u reads as one UV unit per unit of length.
    /// </summary>
    internal static BumpFrame BumpFrameAt(in HitRecord rec, Vector3 t, Vector3 b)
    {
        Vector2 uvT, uvB;
        Vector3 dpdu = rec.DpDu, dpdv = rec.DpDv;
        float a00 = Vector3.Dot(dpdu, dpdu), a01 = Vector3.Dot(dpdu, dpdv), a11 = Vector3.Dot(dpdv, dpdv);
        float det = a00 * a11 - a01 * a01;
        if (det > 1e-12f * MathF.Max(a00 * a11, 1e-30f) && a00 > 0f && a11 > 0f)
        {
            float inv = 1f / det;
            Vector2 Solve(Vector3 x)
            {
                float r0 = Vector3.Dot(dpdu, x), r1 = Vector3.Dot(dpdv, x);
                return new Vector2((a11 * r0 - a01 * r1) * inv, (a00 * r1 - a01 * r0) * inv);
            }
            uvT = Solve(t);
            uvB = Solve(b);
        }
        else
        {
            float side = rec.FrontFace ? 1f : -1f;
            uvT = new Vector2(side, 0f);
            uvB = new Vector2(0f, side);
        }

        Vector3 pT = t, pB = b;
        if (rec.HasLocalFrame)
        {
            Quaternion toLocal = Quaternion.Conjugate(rec.LocalToWorld);
            pT = Vector3.Transform(t, toLocal);
            pB = Vector3.Transform(b, toLocal);
        }
        return new BumpFrame { UvT = uvT, UvB = uvB, PointT = pT, PointB = pB };
    }

    /// <summary>
    /// Bump-map perturbation. Mirrors <see cref="ApplyNormalMap"/> but the
    /// tangent-space normal comes from finite-difference gradients of a
    /// luminance height field rather than an RGB-encoded normal texture.
    ///
    /// Runs AFTER <see cref="ApplyNormalMap"/> when both are present:
    /// the TBN is re-orthogonalised against the already-perturbed
    /// <c>rec.Normal</c> so bump details sit on top of the normal-map
    /// medium-frequency relief (Arnold/Cycles composition order).
    /// </summary>
    private static void ApplyBumpMap(ref HitRecord rec, IBumpMap bump)
    {
        Vector3 T = rec.Tangent;
        Vector3 B = rec.Bitangent;
        Vector3 N = rec.Normal;

        // Gram-Schmidt re-orthogonalisation against the (possibly normal-
        // mapped) shading normal. Identical to ApplyNormalMap.
        Vector3 tOrt = T - Vector3.Dot(T, N) * N;
        if (tOrt.LengthSquared() > 1e-8f)
            T = Vector3.Normalize(tOrt);

        Vector3 bOrt = B - Vector3.Dot(B, N) * N - Vector3.Dot(B, T) * T;
        if (bOrt.LengthSquared() > 1e-8f)
            B = Vector3.Normalize(bOrt);

        // Preserve tangent-space handedness on backfaces.
        if (!rec.FrontFace)
        {
            T = -T;
            B = -B;
        }

        Vector3 tsNormal = bump.SampleTangentNormal(
            rec.U, rec.V, rec.LocalPoint, BumpFrameAt(in rec, T, B), rec.ObjectSeed);

        Vector3 perturbedNormal = Vector3.Normalize(
            T * tsNormal.X + B * tsNormal.Y + N * tsNormal.Z
        );

        rec.Normal = perturbedNormal;
    }
}
