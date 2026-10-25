using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Watertight ray-triangle intersection (Woop, Benthin and Wald 2013,
/// "Watertight Ray/Triangle Intersection"; PBRT-v4 §6.5.3).
///
/// <para>The ray is moved to the origin and sheared so it runs along +z; the
/// triangle's vertices go through the same transform and the test becomes a
/// 2D point-in-triangle test at the origin with three edge functions. Two
/// triangles sharing an edge compute the same edge function for it (with
/// opposite sign), so a ray through the edge, or through a shared vertex, is
/// inside at least one of them: no ray slips between the triangles of a mesh.
/// An edge function that rounds to exactly zero is recomputed in double. No
/// epsilon on the determinant: a triangle of any size is found (Möller-Trumbore
/// with a fixed 1e-7 threshold lost every triangle below ~1e-4 units, and let
/// rays through the edges and vertices of meshes).</para>
/// </summary>
internal static class TriangleIntersection
{
    /// <summary>
    /// Intersects <paramref name="ray"/> with the triangle (p0, p1, p2) over
    /// (tMin, tMax]. On a hit returns the distance and the barycentric weights
    /// of p1 and p2 (the weight of p0 is 1 − b1 − b2).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Intersect(in Ray ray, Vector3 p0, Vector3 p1, Vector3 p2, float tMin, float tMax,
                                 out float t, out float b1, out float b2)
    {
        t = b1 = b2 = 0f;
        Vector3 d = ray.Direction;

        // The axis the ray runs most along becomes z.
        Vector3 ad = Vector3.Abs(d);
        int kz = ad.X > ad.Y ? (ad.X > ad.Z ? 0 : 2) : (ad.Y > ad.Z ? 1 : 2);
        int kx = kz == 2 ? 0 : kz + 1;
        int ky = kx == 2 ? 0 : kx + 1;
        float dz = Comp(d, kz);
        if (dz == 0f) return false;

        // Vertices relative to the origin, permuted.
        Vector3 a = p0 - ray.Origin, b = p1 - ray.Origin, c = p2 - ray.Origin;
        float ax = Comp(a, kx), ay = Comp(a, ky), az = Comp(a, kz);
        float bx = Comp(b, kx), by = Comp(b, ky), bz = Comp(b, kz);
        float cx = Comp(c, kx), cy = Comp(c, ky), cz = Comp(c, kz);

        // Shear so the ray runs along +z.
        float sx = -Comp(d, kx) / dz, sy = -Comp(d, ky) / dz, sz = 1f / dz;
        ax += sx * az; ay += sy * az;
        bx += sx * bz; by += sy * bz;
        cx += sx * cz; cy += sy * cz;

        // Edge functions; exactly zero is ambiguous in float, decide it in double.
        float e0 = bx * cy - by * cx;
        float e1 = cx * ay - cy * ax;
        float e2 = ax * by - ay * bx;
        if (e0 == 0f || e1 == 0f || e2 == 0f)
        {
            e0 = (float)((double)bx * cy - (double)by * cx);
            e1 = (float)((double)cx * ay - (double)cy * ax);
            e2 = (float)((double)ax * by - (double)ay * bx);
        }
        if ((e0 < 0f || e1 < 0f || e2 < 0f) && (e0 > 0f || e1 > 0f || e2 > 0f)) return false;
        float det = e0 + e1 + e2;
        if (det == 0f) return false;

        // Distance, scaled by det, kept unscaled until the range test passes.
        az *= sz; bz *= sz; cz *= sz;
        float tScaled = e0 * az + e1 * bz + e2 * cz;
        if (det < 0f ? (tScaled > tMin * det || tScaled < tMax * det)
                     : (tScaled < tMin * det || tScaled > tMax * det))
            return false;

        float invDet = 1f / det;
        t = tScaled * invDet;
        b1 = e1 * invDet;
        b2 = e2 * invDet;
        return t >= tMin && t <= tMax;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Comp(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
