using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Helpers for projecting ray differentials onto a surface tangent plane and
/// solving for the (∂u/∂x, ∂v/∂x, ∂u/∂y, ∂v/∂y) UV partials at the hit
/// (PBRT §10.1.1, Igehy 1999).
///
/// The standard formulation:
/// <list type="number">
///   <item>Find the parameter <c>t_x</c> at which the +x auxiliary ray
///   intersects the tangent plane defined by hit point P and normal N:
///   <c>t_x = (P − O_x) · N / (D_x · N)</c>. Same for +y.</item>
///   <item>The auxiliary hit point is <c>P_x = O_x + t_x · D_x</c>; the
///   world-space surface footprint is <c>∂P/∂x = P_x − P</c>.</item>
///   <item>UV partials are the solution of the 3×2 linear system
///   <c>[dPdu | dPdv] · [du/dx ; dv/dx]ᵀ = ∂P/∂x</c>, solved by dropping the
///   row whose <c>(dPdu × dPdv)</c> component has the largest magnitude
///   (least singular 2×2 sub-system).</item>
/// </list>
///
/// When the primitive hasn't populated <c>DpDu</c>/<c>DpDv</c>, the renderer
/// falls back to the unit Tangent/Bitangent vectors — UV partials are then
/// in "unit-direction-per-pixel" units, which is a conservative under-
/// estimate that still drives Perlin octave clamping correctly but
/// under-blurs image textures whose UV scale is &gt;&gt; 1.
/// </summary>
public static class FootprintMath
{
    /// <summary>
    /// Projects ray differentials onto the tangent plane at the hit and
    /// solves for the UV partials. Returns <see cref="FilterFootprint.None"/>
    /// when the ray carries no differentials, when both axes are degenerate
    /// (rays parallel to the tangent plane), or when the dpdu/dpdv basis is
    /// singular.
    /// </summary>
    public static FilterFootprint Compute(
        in Ray ray, Vector3 hitPoint, Vector3 normal,
        Vector3 dpdu, Vector3 dpdv)
    {
        if (!ray.HasDifferentials) return FilterFootprint.None;
        var d = ray.Differentials;

        if (!IntersectTangentPlane(d.OriginX, d.DirectionX, hitPoint, normal, out Vector3 px)) return FilterFootprint.None;
        if (!IntersectTangentPlane(d.OriginY, d.DirectionY, hitPoint, normal, out Vector3 py)) return FilterFootprint.None;

        Vector3 dpdxV = px - hitPoint;
        Vector3 dpdyV = py - hitPoint;

        if (!SolveUvPartials(normal, dpdu, dpdv, dpdxV, dpdyV,
                             out float dudx, out float dvdx,
                             out float dudy, out float dvdy))
        {
            // Singular basis — return the 3D footprint only; UV partials
            // stay zero so 2D textures fall back to point sampling.
            return new FilterFootprint(dpdxV, dpdyV, 0f, 0f, 0f, 0f);
        }

        return new FilterFootprint(dpdxV, dpdyV, dudx, dvdx, dudy, dvdy);
    }

    private static bool IntersectTangentPlane(
        Vector3 origin, Vector3 dir, Vector3 planePoint, Vector3 planeNormal,
        out Vector3 hit)
    {
        float denom = Vector3.Dot(dir, planeNormal);
        if (MathF.Abs(denom) < 1e-12f)
        {
            hit = default;
            return false;
        }
        float t = Vector3.Dot(planePoint - origin, planeNormal) / denom;
        hit = origin + t * dir;
        return float.IsFinite(t);
    }

    /// <summary>
    /// Solves [dpdu | dpdv] · (du, dv)ᵀ = dPd{x,y} via the largest-magnitude
    /// 2×2 sub-system, which PBRT §10.1.1 proves to be the best-conditioned.
    /// </summary>
    private static bool SolveUvPartials(
        Vector3 normal, Vector3 dpdu, Vector3 dpdv,
        Vector3 dpdxV, Vector3 dpdyV,
        out float dudx, out float dvdx, out float dudy, out float dvdy)
    {
        // Pick the two rows whose 2×2 determinant has maximum magnitude.
        // Equivalent to dropping the axis closest to the normal.
        float ax = MathF.Abs(normal.X);
        float ay = MathF.Abs(normal.Y);
        float az = MathF.Abs(normal.Z);

        int dim0, dim1;
        if (ax > ay && ax > az) { dim0 = 1; dim1 = 2; }
        else if (ay > az)        { dim0 = 0; dim1 = 2; }
        else                     { dim0 = 0; dim1 = 1; }

        float a00 = GetComp(dpdu, dim0); float a01 = GetComp(dpdv, dim0);
        float a10 = GetComp(dpdu, dim1); float a11 = GetComp(dpdv, dim1);
        float det = a00 * a11 - a01 * a10;

        if (MathF.Abs(det) < 1e-20f)
        {
            dudx = dvdx = dudy = dvdy = 0f;
            return false;
        }
        float invDet = 1f / det;

        float bx0 = GetComp(dpdxV, dim0); float bx1 = GetComp(dpdxV, dim1);
        float by0 = GetComp(dpdyV, dim0); float by1 = GetComp(dpdyV, dim1);

        dudx = ( a11 * bx0 - a01 * bx1) * invDet;
        dvdx = (-a10 * bx0 + a00 * bx1) * invDet;
        dudy = ( a11 * by0 - a01 * by1) * invDet;
        dvdy = (-a10 * by0 + a00 * by1) * invDet;
        return true;
    }

    private static float GetComp(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
}
