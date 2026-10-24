namespace RayTracer.Materials;

/// <summary>
/// Directional albedo of a white single-scattering GGX reflector with
/// height-correlated Smith masking, E(μ, α), and its cosine-weighted average
/// E_avg(α): the inputs of the Kulla-Conty multiple-scattering lobe
/// (PIANO-REVIEW.md, Phase 5). The table is integrated offline by
/// deterministic quadrature over the visible-normal parametrisation
/// (<c>validation/gen_microfacet_albedo.py</c>) and indexed by
/// roughness = √α, which spends the resolution where E changes fastest.
/// </summary>
internal static partial class MicrofacetAlbedo
{
    /// <summary>E(μ, α) by bilinear interpolation; μ = cos θ of the view direction.</summary>
    public static float E(float mu, float alpha)
    {
        float fm = Math.Clamp(mu, 0f, 1f) * (MuRes - 1);
        float fr = Math.Clamp(MathF.Sqrt(MathF.Max(alpha, 0f)), 0f, 1f) * (RoughRes - 1);
        int i0 = Math.Min((int)fm, MuRes - 2), j0 = Math.Min((int)fr, RoughRes - 2);
        float tm = fm - i0, tr = fr - j0;
        float e00 = Table[i0 * RoughRes + j0], e01 = Table[i0 * RoughRes + j0 + 1];
        float e10 = Table[(i0 + 1) * RoughRes + j0], e11 = Table[(i0 + 1) * RoughRes + j0 + 1];
        float e0 = e00 + (e01 - e00) * tr, e1 = e10 + (e11 - e10) * tr;
        return e0 + (e1 - e0) * tm;
    }

    /// <summary>E_avg(α) = 2 ∫ E(μ, α) μ dμ, by linear interpolation.</summary>
    public static float Avg(float alpha)
    {
        float fr = Math.Clamp(MathF.Sqrt(MathF.Max(alpha, 0f)), 0f, 1f) * (RoughRes - 1);
        int j0 = Math.Min((int)fr, RoughRes - 2);
        float t = fr - j0;
        return Average[j0] + (Average[j0 + 1] - Average[j0]) * t;
    }
}
