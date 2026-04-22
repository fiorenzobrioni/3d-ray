using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// Shared anisotropic GGX microfacet utilities: tangent-frame construction,
/// NDF, Smith masking/shadowing, and visible-normal sampling (Heitz 2018).
///
/// Extracted out of <see cref="DisneyBsdf"/> so the energy-compensation LUT
/// builder (<see cref="EnergyCompensationLut"/>) and any future BSDF that
/// needs GGX sampling can share one implementation. Passing αx = αy recovers
/// the isotropic formulae exactly, so isotropic and anisotropic call sites
/// use the same entry points.
/// </summary>
internal static class Microfacet
{
    /// <summary>
    /// Frisvad orthonormal tangent frame around N, with the south-pole
    /// branch guarded so near-(-Z) normals don't explode through the
    /// 1/(1+N.Z) term.
    /// </summary>
    public static void BuildTangentFrame(Vector3 N, out Vector3 T, out Vector3 B)
    {
        if (N.Z < -0.999f)
        {
            T = new Vector3(0f, -1f, 0f);
            B = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + N.Z);
            float b = -N.X * N.Y * a;
            T = new Vector3(1f - N.X * N.X * a, b, -N.X);
            B = new Vector3(b, 1f - N.Y * N.Y * a, -N.Y);
        }
    }

    /// <summary>
    /// Anisotropic GGX (Trowbridge-Reitz) NDF in tangent space.
    ///   D(H) = 1 / (π · αx · αy · [(Hx/αx)² + (Hy/αy)² + Hz²]²)
    /// Collapses to the isotropic form α² / (π · ((α² - 1) · Hz² + 1)²) when αx = αy.
    /// </summary>
    public static float DGgxAniso(Vector3 Hloc, float ax, float ay)
    {
        float hx = Hloc.X / ax;
        float hy = Hloc.Y / ay;
        float d  = hx * hx + hy * hy + Hloc.Z * Hloc.Z;
        return 1f / MathF.Max(MathF.PI * ax * ay * d * d, 1e-20f);
    }

    /// <summary>
    /// Smith Λ for anisotropic GGX (Heitz 2014 eq. 86).
    ///   Λ(ω) = (-1 + sqrt(1 + (αx²·ωx² + αy²·ωy²) / ωz²)) / 2
    /// </summary>
    public static float LambdaGgxAniso(Vector3 wloc, float ax, float ay)
    {
        float cos2 = wloc.Z * wloc.Z;
        if (cos2 < 1e-14f) return 1e10f;
        float num = (ax * wloc.X) * (ax * wloc.X) + (ay * wloc.Y) * (ay * wloc.Y);
        return 0.5f * (-1f + MathF.Sqrt(1f + num / cos2));
    }

    /// <summary>
    /// Smith G1 masking/shadowing for anisotropic GGX. Separable form
    /// G(V, L) = G1(V) · G1(L) is standard for Disney-style BSDFs.
    /// </summary>
    public static float G1GgxAniso(Vector3 wloc, float ax, float ay)
        => 1f / (1f + LambdaGgxAniso(wloc, ax, ay));

    /// <summary>
    /// Isotropic Smith G1 closed form — avoids allocating a Vector3 when
    /// only the z-component is relevant (e.g. Disney's isotropic clearcoat).
    ///   G1(NdotX) = 2·NdotX / (NdotX + sqrt(α² + (1-α²)·NdotX²))
    /// </summary>
    public static float SmithG1(float NdotX, float alpha)
    {
        float a2 = alpha * alpha;
        float NdotX2 = NdotX * NdotX;
        float denom = NdotX + MathF.Sqrt(a2 + (1f - a2) * NdotX2);
        return 2f * NdotX / MathF.Max(denom, 1e-7f);
    }

    /// <summary>
    /// Samples a visible microfacet normal from the anisotropic GGX
    /// distribution (Heitz 2018 §3.2, JCGT vol. 7 no. 4). Inputs and
    /// outputs are in tangent space — the caller owns the local↔world
    /// transform.
    ///
    /// Algorithm:
    ///   1. Stretch V by (αx, αy, 1) so anisotropic GGX becomes isotropic on
    ///      a hemisphere.
    ///   2. Build an orthonormal basis (T1, T2) in the plane perpendicular
    ///      to the stretched V.
    ///   3. Sample a point on the unit disk via the trapezoid
    ///      reparameterisation (Heitz 2018 eq. 6) — warps the disk so the
    ///      resulting half-vector weight is already uniform (no rejection).
    ///   4. Project onto the hemisphere and unstretch, clamping Hz to zero
    ///      for numerical robustness at grazing.
    /// </summary>
    public static Vector3 SampleGgxVndfAniso(Vector3 Vloc, float ax, float ay,
                                             float u1, float u2)
    {
        Vector3 Vh = Vector3.Normalize(new Vector3(ax * Vloc.X, ay * Vloc.Y, Vloc.Z));
        float lenSq = Vh.X * Vh.X + Vh.Y * Vh.Y;
        Vector3 T1 = lenSq > 0f
            ? new Vector3(-Vh.Y, Vh.X, 0f) / MathF.Sqrt(lenSq)
            : new Vector3(1f, 0f, 0f);
        Vector3 T2 = Vector3.Cross(Vh, T1);

        float r   = MathF.Sqrt(u1);
        float phi = 2f * MathF.PI * u2;
        float t1  = r * MathF.Cos(phi);
        float t2  = r * MathF.Sin(phi);
        float s   = 0.5f * (1f + Vh.Z);
        t2 = (1f - s) * MathF.Sqrt(MathF.Max(0f, 1f - t1 * t1)) + s * t2;

        Vector3 Nh = t1 * T1 + t2 * T2
                   + MathF.Sqrt(MathF.Max(0f, 1f - t1 * t1 - t2 * t2)) * Vh;

        return Vector3.Normalize(new Vector3(
            ax * Nh.X,
            ay * Nh.Y,
            MathF.Max(0f, Nh.Z)));
    }
}
