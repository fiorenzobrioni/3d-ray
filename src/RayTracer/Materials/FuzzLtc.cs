using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

/// <summary>
/// The OpenPBR fuzz lobe: the multiple-scattering sheen of Zeltner, Burley and
/// Chiang 2022, a volumetric layer of fibre-like microflakes fitted with a
/// linearly transformed cosine (PIANO-REVIEW.md, Phase 5). With μ_o the view
/// cosine and r the roughness,
///
/// <para>f(ω_i, ω_o) μ_i = E(μ_o, r) · D(ω_i | ω_o, r),</para>
///
/// where D is a normalised distribution over the upper hemisphere (the cosine
/// lobe under M = [[a, 0, b], [0, a, 0], [0, 0, 1]]⁻¹ in a frame whose x axis
/// points towards the viewer) and E the lobe's albedo. Sampling draws D
/// exactly. The fits of E, a⁻¹ and b⁻¹ to the paper's tables are the rational
/// ones of the authors' reference code.
/// </summary>
internal static class FuzzLtc
{
    /// <summary>The fits hold on r ∈ [0.01, 1].</summary>
    private static float ClampRoughness(float r) => Math.Clamp(r, 0.01f, 1f);

    /// <summary>E(μ, r), the albedo of a white fuzz layer.</summary>
    public static float Albedo(float mu, float r)
    {
        float x = Math.Clamp(mu, 0f, 1f), y = ClampRoughness(r);
        float s = y * (0.0206607f + 1.58491f * y) / (0.0379424f + y * (1.32227f + y));
        float m = y * (-0.193854f + y * (-1.14885f + y * (1.7932f - 0.95943f * y * y))) / (0.046391f + y);
        float o = y * (0.000654023f + (-0.0207818f + 0.119681f * y) * y) / (1.26264f + y * (-1.92021f + y));
        float z = (x - m) / s;
        return Math.Clamp(MathF.Exp(-0.5f * z * z) / (s * MathF.Sqrt(2f * MathF.PI)) + o, 0f, 1f);
    }

    private static float AInv(float x, float y) =>
        (2.58126f * x + 0.813703f * y) * y / (1f + 0.310327f * x * x + 2.60994f * x * y);

    private static float BInv(float x, float y) =>
        MathF.Sqrt(1f - x) * (y - 1f) * y * y * y / (0.0000254053f + 1.71228f * x - 1.71506f * x * y + 1.34174f * y * y);

    /// <summary>The LTC frame: x towards V in the tangent plane, z = N.</summary>
    private static void Frame(Vector3 N, Vector3 V, float muV, out Vector3 X, out Vector3 Y)
    {
        Vector3 x = V - N * muV;
        float len2 = x.LengthSquared();
        if (len2 > 1e-12f)
        {
            X = x / MathF.Sqrt(len2);
            Y = Vector3.Cross(N, X);
        }
        else
        {
            Microfacet.BuildTangentFrame(N, out X, out Y);
        }
    }

    /// <summary>D(L | V), the solid-angle density of the lobe (and the pdf of
    /// <see cref="Sample"/>); zero below the surface.</summary>
    public static float Density(Vector3 N, Vector3 V, Vector3 L, float r)
    {
        float muV = Math.Clamp(Vector3.Dot(N, V), 0f, 1f);
        float muL = Vector3.Dot(N, L);
        if (muL <= 0f) return 0f;
        float y = ClampRoughness(r);
        Frame(N, V, muV, out Vector3 X, out Vector3 Y);
        float wx = Vector3.Dot(L, X), wy = Vector3.Dot(L, Y);
        float aInv = AInv(muV, y), bInv = BInv(muV, y);
        // D(w) = D_o(M⁻¹w / |M⁻¹w|) |M⁻¹| / |M⁻¹w|³ = D_o(M⁻¹w) a⁻² / |M⁻¹w|⁴.
        Vector3 wo = new(aInv * wx + bInv * muL, aInv * wy, muL);
        float len2 = wo.LengthSquared();
        float k = aInv / len2;
        return wo.Z / MathF.PI * k * k;
    }

    /// <summary>Draws L from D(· | V).</summary>
    public static Vector3 Sample(Vector3 N, Vector3 V, float r, float u1, float u2)
    {
        float muV = Math.Clamp(Vector3.Dot(N, V), 0f, 1f);
        float y = ClampRoughness(r);
        float aInv = AInv(muV, y), bInv = BInv(muV, y);
        float phi = 2f * MathF.PI * u1;
        float cosT = MathF.Sqrt(u2), sinT = MathF.Sqrt(1f - u2);
        Vector3 wo = new(MathF.Cos(phi) * sinT, MathF.Sin(phi) * sinT, cosT);
        // w = M wo: M = [[1/a⁻¹, 0, −b⁻¹/a⁻¹], [0, 1/a⁻¹, 0], [0, 0, 1]].
        Vector3 w = Vector3.Normalize(new Vector3((wo.X - wo.Z * bInv) / aInv, wo.Y / aInv, wo.Z));
        Frame(N, V, muV, out Vector3 X, out Vector3 Y);
        return w.X * X + w.Y * Y + w.Z * N;
    }
}
