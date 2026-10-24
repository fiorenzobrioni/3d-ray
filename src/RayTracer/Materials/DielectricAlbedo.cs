namespace RayTracer.Materials;

/// <summary>
/// Directional albedo E_R(μ, α, η) of the reflection lobe of a rough
/// dielectric (GGX, height-correlated Smith, exact Fresnel) seen from the
/// side of the lower index, and the hemispherical Fresnel average E_F(η)
/// (PIANO-REVIEW.md, Phase 5). The OpenPBR layering weighs what lies under a
/// specular interface by 1 − E_R, so that interface plus a white substrate
/// reflect everything. Integrated offline by deterministic quadrature
/// (<c>validation/gen_dielectric_albedo.py</c>); the IOR axis is
/// s = (η − 1)/(η + 1) = √F0, from an invisible interface (s = 0) to a
/// perfect reflector (s = 1).
/// </summary>
internal static partial class DielectricAlbedo
{
    private static float SOf(float eta) => eta <= 1f ? 0f : (eta - 1f) / (eta + 1f);

    /// <summary>E_R for view cosine <paramref name="mu"/>, GGX α and relative
    /// IOR <paramref name="eta"/> = n_below / n_above ≥ 1.</summary>
    public static float E(float mu, float alpha, float eta)
    {
        float fm = Math.Clamp(mu, 0f, 1f) * (MuRes - 1);
        float fr = Math.Clamp(MathF.Sqrt(MathF.Max(alpha, 0f)), 0f, 1f) * (RoughRes - 1);
        float fs = Math.Clamp(SOf(eta), 0f, 1f) * (SRes - 1);
        int i = Math.Min((int)fm, MuRes - 2), j = Math.Min((int)fr, RoughRes - 2), k = Math.Min((int)fs, SRes - 2);
        float tm = fm - i, tr = fr - j, ts = fs - k;

        float Lerp2(int ii, int jj)
        {
            int b = (ii * RoughRes + jj) * SRes + k;
            return Table[b] + (Table[b + 1] - Table[b]) * ts;
        }
        float e00 = Lerp2(i, j), e01 = Lerp2(i, j + 1), e10 = Lerp2(i + 1, j), e11 = Lerp2(i + 1, j + 1);
        float e0 = e00 + (e01 - e00) * tr, e1 = e10 + (e11 - e10) * tr;
        return e0 + (e1 - e0) * tm;
    }

    // Ē_R(α, η) = 2 ∫ E_R(μ) μ dμ on the table's own nodes (Simpson, 32
    // intervals). Built on first use: the table is a field of the other half of
    // this partial class, whose initialisation order relative to this one is
    // not defined.
    private static float[]? _average;
    private static float[] Average => _average ??= BuildAverage();

    private static float[] BuildAverage()
    {
        var avg = new float[RoughRes * SRes];
        for (int j = 0; j < RoughRes; j++)
        for (int k = 0; k < SRes; k++)
        {
            double sum = 0;
            for (int i = 0; i < MuRes; i++)
            {
                double w = i == 0 || i == MuRes - 1 ? 1 : (i % 2 == 1 ? 4 : 2);
                sum += w * Table[(i * RoughRes + j) * SRes + k] * i / (MuRes - 1.0);
            }
            avg[j * SRes + k] = (float)(2 * sum / (3.0 * (MuRes - 1)));
        }
        return avg;
    }

    /// <summary>Ē_R(α, η), the cosine-weighted average of <see cref="E"/>.</summary>
    public static float Avg(float alpha, float eta)
    {
        float fr = Math.Clamp(MathF.Sqrt(MathF.Max(alpha, 0f)), 0f, 1f) * (RoughRes - 1);
        float fs = Math.Clamp(SOf(eta), 0f, 1f) * (SRes - 1);
        int j = Math.Min((int)fr, RoughRes - 2), k = Math.Min((int)fs, SRes - 2);
        float tr = fr - j, ts = fs - k;
        float a0 = Average[j * SRes + k] + (Average[j * SRes + k + 1] - Average[j * SRes + k]) * ts;
        float a1 = Average[(j + 1) * SRes + k] + (Average[(j + 1) * SRes + k + 1] - Average[(j + 1) * SRes + k]) * ts;
        return a0 + (a1 - a0) * tr;
    }

    /// <summary>E_F(η) = 2 ∫ F(μ, η) μ dμ for any relative IOR; below 1 by
    /// the reciprocity relation E_F(η) = 1 − η² (1 − E_F(1/η)).</summary>
    public static float FresnelAvg(float eta)
    {
        if (eta < 1f) return 1f - eta * eta * (1f - FresnelAvg(1f / eta));
        float fs = Math.Clamp(SOf(eta), 0f, 1f) * (FavgRes - 1);
        int k = Math.Min((int)fs, FavgRes - 2);
        float t = fs - k;
        return FresnelAverage[k] + (FresnelAverage[k + 1] - FresnelAverage[k]) * t;
    }
}
