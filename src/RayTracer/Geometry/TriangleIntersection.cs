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
    /// The per-ray half of the test: the axis permutation and the shear that
    /// turns the ray into +z. A mesh computes it once per ray and reuses it on
    /// every triangle (<see cref="TriangleBvh"/>); the values, and so every
    /// result, are the same as computed inside <see cref="Intersect(in Ray, Vector3, Vector3, Vector3, float, float, out float, out float, out float)"/>.
    /// </summary>
    public readonly struct Shear
    {
        public readonly Vector3 Origin;
        public readonly int Kx, Ky, Kz;
        public readonly float Sx, Sy, Sz;
        public readonly bool Valid;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Shear(in Ray ray)
        {
            Vector3 d = ray.Direction;
            Origin = ray.Origin;
            // The axis the ray runs most along becomes z.
            Vector3 ad = Vector3.Abs(d);
            Kz = ad.X > ad.Y ? (ad.X > ad.Z ? 0 : 2) : (ad.Y > ad.Z ? 1 : 2);
            Kx = Kz == 2 ? 0 : Kz + 1;
            Ky = Kx == 2 ? 0 : Kx + 1;
            float dz = Comp(d, Kz);
            Valid = dz != 0f;
            Sx = -Comp(d, Kx) / dz;
            Sy = -Comp(d, Ky) / dz;
            Sz = 1f / dz;
        }
    }

    /// <summary>
    /// Intersects <paramref name="ray"/> with the triangle (p0, p1, p2) over
    /// (tMin, tMax]. On a hit returns the distance and the barycentric weights
    /// of p1 and p2 (the weight of p0 is 1 − b1 − b2).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Intersect(in Ray ray, Vector3 p0, Vector3 p1, Vector3 p2, float tMin, float tMax,
                                 out float t, out float b1, out float b2)
    {
        var shear = new Shear(ray);
        return Intersect(shear, p0, p1, p2, tMin, tMax, out t, out b1, out b2);
    }

    /// <summary>The test with the ray's shear already computed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Intersect(in Shear s, Vector3 p0, Vector3 p1, Vector3 p2, float tMin, float tMax,
                                 out float t, out float b1, out float b2)
    {
        t = b1 = b2 = 0f;
        if (!s.Valid) return false;

        // Vertices relative to the origin, permuted.
        Vector3 a = p0 - s.Origin, b = p1 - s.Origin, c = p2 - s.Origin;
        float ax = Comp(a, s.Kx), ay = Comp(a, s.Ky), az = Comp(a, s.Kz);
        float bx = Comp(b, s.Kx), by = Comp(b, s.Ky), bz = Comp(b, s.Kz);
        float cx = Comp(c, s.Kx), cy = Comp(c, s.Ky), cz = Comp(c, s.Kz);

        // Shear so the ray runs along +z.
        ax += s.Sx * az; ay += s.Sy * az;
        bx += s.Sx * bz; by += s.Sy * bz;
        cx += s.Sx * cz; cy += s.Sy * cz;

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
        az *= s.Sz; bz *= s.Sz; cz *= s.Sz;
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
