using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Uniform sampling of the solid angle a rectangle subtends at a point
/// (Ureña, Fajardo, King 2013, "An Area-Preserving Parametrization for
/// Spherical Rectangles"; the form of PBRT-v4's <c>SampleSphericalRectangle</c>).
///
/// <para>
/// Sampling a light's area uniformly gives each sample the weight
/// A cos θ_l / d²: near the light, where that factor swings by orders of
/// magnitude over the rectangle, the estimate is noisy. Sampling its solid
/// angle uniformly gives every sample the same weight Ω, whatever the
/// distance; only the receiver's cosine and the BSDF are left to vary.
/// </para>
///
/// <para>
/// A rectangle that subtends less than <see cref="MinSolidAngle"/> is sampled
/// by area instead: there the two densities agree to a fraction of a percent,
/// and the spherical formulas lose their precision in single precision.
/// <see cref="Pdf"/> reports the density of whichever method
/// <see cref="TrySample"/> used for that point, so a light's estimator and its
/// MIS weights stay consistent.
/// </para>
/// </summary>
public static class SphericalRectangle
{
    /// <summary>Below this solid angle (sr) the rectangle is sampled by area.</summary>
    public const float MinSolidAngle = 1e-3f;

    /// <summary>
    /// True when <paramref name="ex"/> and <paramref name="ey"/> span a
    /// rectangle (orthogonal edges), which is what this sampler requires.
    /// </summary>
    public static bool IsRectangle(Vector3 ex, Vector3 ey)
    {
        float lx = ex.Length(), ly = ey.Length();
        return lx > 0f && ly > 0f && MathF.Abs(Vector3.Dot(ex, ey)) <= 1e-4f * lx * ly;
    }

    /// <summary>
    /// Solid angle the rectangle (corner <paramref name="s"/>, orthogonal
    /// edges <paramref name="ex"/>, <paramref name="ey"/>) subtends at
    /// <paramref name="p"/>, from either side.
    /// </summary>
    public static float SolidAngle(Vector3 p, Vector3 s, Vector3 ex, Vector3 ey)
        => (float)Setup(p, s, ex, ey).SolidAngle;

    /// <summary>
    /// A point of the rectangle with uniform density over the solid angle it
    /// subtends at <paramref name="p"/>, from the square sample (u0, u1).
    /// Returns false when the rectangle is too small (or degenerate) for it:
    /// the caller then samples by area.
    /// </summary>
    public static bool TrySample(Vector3 p, Vector3 s, Vector3 ex, Vector3 ey, float u0, float u1,
                                 out Vector3 point, out float pdfSolidAngle)
    {
        var g = Setup(p, s, ex, ey);
        point = default;
        pdfSolidAngle = 0f;
        if (!(g.SolidAngle >= MinSolidAngle)) return false;

        double au = u0 * (g.G0 + g.G1 - 2 * Math.PI) + (u0 - 1) * (g.G2 + g.G3);
        double fu = (Math.Cos(au) * g.B0 - g.B1) / Math.Sin(au);
        double cu = Math.CopySign(1 / Math.Sqrt(fu * fu + g.B0 * g.B0), fu);
        cu = Math.Clamp(cu, -0.9999999, 0.9999999);
        double xu = -(cu * g.Z0) / Math.Sqrt(Math.Max(0, 1 - cu * cu));
        xu = Math.Clamp(xu, g.X0, g.X1);
        double dd = Math.Sqrt(xu * xu + g.Z0 * g.Z0);
        double h0 = g.Y0 / Math.Sqrt(dd * dd + g.Y0 * g.Y0);
        double h1 = g.Y1 / Math.Sqrt(dd * dd + g.Y1 * g.Y1);
        double hv = h0 + u1 * (h1 - h0), hvsq = hv * hv;
        double yv = hvsq < 1 - 1e-12 ? hv * dd / Math.Sqrt(1 - hvsq) : g.Y1;
        yv = Math.Clamp(yv, g.Y0, g.Y1);

        point = p + (float)xu * g.X + (float)yv * g.Y + (float)g.Z0 * g.Z;
        pdfSolidAngle = (float)(1 / g.SolidAngle);
        return true;
    }

    /// <summary>
    /// Solid-angle density with which a light that samples this rectangle
    /// (spherical sampling, or by area below <see cref="MinSolidAngle"/>)
    /// produces direction <paramref name="dir"/> from <paramref name="p"/>;
    /// 0 when the direction misses the rectangle.
    /// </summary>
    public static float Pdf(Vector3 p, Vector3 s, Vector3 ex, Vector3 ey, Vector3 dir)
    {
        Vector3 n = Vector3.Cross(ex, ey);
        float area = n.Length();
        if (area <= 0f) return 0f;
        n /= area;
        float denom = Vector3.Dot(n, dir);
        if (MathF.Abs(denom) < 1e-7f) return 0f;
        float t = Vector3.Dot(s - p, n) / denom;
        if (!(t > 0f)) return 0f;
        Vector3 local = p + t * dir - s;
        float a = Vector3.Dot(local, ex) / ex.LengthSquared();
        float b = Vector3.Dot(local, ey) / ey.LengthSquared();
        if (a < 0f || a > 1f || b < 0f || b > 1f) return 0f;

        double omega = Setup(p, s, ex, ey).SolidAngle;
        if (omega >= MinSolidAngle) return (float)(1 / omega);
        float len = dir.Length();
        return t * t * len * len / (area * MathF.Abs(denom) / len);
    }

    private readonly record struct Geometry(
        Vector3 X, Vector3 Y, Vector3 Z, double X0, double X1, double Y0, double Y1, double Z0,
        double G0, double G1, double G2, double G3, double B0, double B1, double SolidAngle);

    private static Geometry Setup(Vector3 p, Vector3 s, Vector3 ex, Vector3 ey)
    {
        float exl = ex.Length(), eyl = ey.Length();
        if (exl <= 0f || eyl <= 0f) return default;
        Vector3 x = ex / exl, y = ey / eyl, z = Vector3.Cross(x, y);
        Vector3 d = s - p;
        double x0 = Vector3.Dot(d, x), y0 = Vector3.Dot(d, y), z0 = Vector3.Dot(d, z);
        // Seen from p the rectangle lies at z0 < 0 in its frame.
        if (z0 > 0) { z = -z; z0 = -z0; }
        if (z0 == 0) return default;
        double x1 = x0 + exl, y1 = y0 + eyl;

        var v00 = new Vector3D(x0, y0, z0); var v01 = new Vector3D(x0, y1, z0);
        var v10 = new Vector3D(x1, y0, z0); var v11 = new Vector3D(x1, y1, z0);
        var n0 = Vector3D.Cross(v00, v10).Normalized();
        var n1 = Vector3D.Cross(v10, v11).Normalized();
        var n2 = Vector3D.Cross(v11, v01).Normalized();
        var n3 = Vector3D.Cross(v01, v00).Normalized();
        double g0 = Angle(-n0, n1), g1 = Angle(-n1, n2), g2 = Angle(-n2, n3), g3 = Angle(-n3, n0);
        double omega = g0 + g1 + g2 + g3 - 2 * Math.PI;
        return new Geometry(x, y, z, x0, x1, y0, y1, z0, g0, g1, g2, g3, n0.Z, n2.Z, omega);
    }

    /// <summary>Angle between unit vectors, accurate near 0 and π.</summary>
    private static double Angle(Vector3D a, Vector3D b)
        => Vector3D.Dot(a, b) < 0
            ? Math.PI - 2 * Math.Asin(Math.Min(1, (a + b).Length() / 2))
            : 2 * Math.Asin(Math.Min(1, (b - a).Length() / 2));

    private readonly record struct Vector3D(double X, double Y, double Z)
    {
        public static Vector3D operator -(Vector3D a) => new(-a.X, -a.Y, -a.Z);
        public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3D Cross(Vector3D a, Vector3D b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length() => Math.Sqrt(Dot(this, this));
        public Vector3D Normalized() { double l = Length(); return new(X / l, Y / l, Z / l); }
    }
}
