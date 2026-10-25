using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// Directional albedo of a white single-scattering anisotropic GGX reflector
/// (height-correlated Smith), indexed by the OpenPBR roughness r and anisotropy
/// a and by the direction in the tangent frame (PIANO-REVIEW.md, Phase 5). Read
/// at both directions it keeps the Kulla-Conty lobe reciprocal and exact for
/// anisotropic lobes, which the isotropic table read at the projected
/// roughness is not (+6.6% of energy at a = 0.72). Integrated offline
/// (<c>validation/gen_aniso_albedo.py</c>).
/// </summary>
internal static partial class AnisotropicAlbedo
{
    /// <summary>E at the direction <paramref name="local"/> (x along the
    /// tangent, z along the normal).</summary>
    public static float E(Vector3 local, float r, float a)
    {
        float mu = Math.Clamp(local.Z, 0f, 1f);
        float phi = MathF.Atan2(MathF.Abs(local.Y), MathF.Abs(local.X));
        float fr = Math.Clamp(r, 0f, 1f) * (RoughRes - 1);
        float fa = Math.Clamp(a, 0f, 1f) * (AnisoRes - 1);
        float fm = mu * (MuRes - 1);
        float fp = Math.Clamp(phi / (0.5f * MathF.PI), 0f, 1f) * (PhiRes - 1);
        int j = Math.Min((int)fr, RoughRes - 2), l = Math.Min((int)fa, AnisoRes - 2);
        int i = Math.Min((int)fm, MuRes - 2), k = Math.Min((int)fp, PhiRes - 2);
        float tr = fr - j, ta = fa - l, tm = fm - i, tp = fp - k;

        float result = 0f;
        for (int dj = 0; dj < 2; dj++)
        for (int dl = 0; dl < 2; dl++)
        for (int di = 0; di < 2; di++)
        for (int dk = 0; dk < 2; dk++)
        {
            float w = (dj == 0 ? 1f - tr : tr) * (dl == 0 ? 1f - ta : ta)
                    * (di == 0 ? 1f - tm : tm) * (dk == 0 ? 1f - tp : tp);
            if (w == 0f) continue;
            result += w * Table[(((j + dj) * AnisoRes + l + dl) * MuRes + i + di) * PhiRes + k + dk];
        }
        return result;
    }

    // E_avg(r, a) = (2/π) ∫ dφ 2 ∫ E(μ, φ) μ dμ on the table's nodes (Simpson in
    // both). Built on first use: the table lives in the other half of this
    // partial class.
    private static float[]? _average;
    private static float[] Average => _average ??= BuildAverage();

    private static float[] BuildAverage()
    {
        var avg = new float[RoughRes * AnisoRes];
        static double Simpson(int n, int i) => i == 0 || i == n - 1 ? 1 : (i % 2 == 1 ? 4 : 2);
        for (int j = 0; j < RoughRes; j++)
        for (int l = 0; l < AnisoRes; l++)
        {
            double sum = 0;
            for (int i = 0; i < MuRes; i++)
            for (int k = 0; k < PhiRes; k++)
            {
                double mu = i / (MuRes - 1.0);
                sum += Simpson(MuRes, i) * Simpson(PhiRes, k) * mu
                     * Table[((j * AnisoRes + l) * MuRes + i) * PhiRes + k];
            }
            // Simpson weights: h/3 per axis, h_mu = 1/(MuRes-1), h_phi spans the unit interval.
            avg[j * AnisoRes + l] = (float)(2 * sum / (3.0 * (MuRes - 1)) / (3.0 * (PhiRes - 1)));
        }
        return avg;
    }

    /// <summary>E_avg(r, a), the cosine-weighted average over the hemisphere.</summary>
    public static float Avg(float r, float a)
    {
        float fr = Math.Clamp(r, 0f, 1f) * (RoughRes - 1);
        float fa = Math.Clamp(a, 0f, 1f) * (AnisoRes - 1);
        int j = Math.Min((int)fr, RoughRes - 2), l = Math.Min((int)fa, AnisoRes - 2);
        float tr = fr - j, ta = fa - l;
        float a0 = Average[j * AnisoRes + l] + (Average[j * AnisoRes + l + 1] - Average[j * AnisoRes + l]) * ta;
        float a1 = Average[(j + 1) * AnisoRes + l] + (Average[(j + 1) * AnisoRes + l + 1] - Average[(j + 1) * AnisoRes + l]) * ta;
        return a0 + (a1 - a0) * tr;
    }
}
