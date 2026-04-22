namespace RayTracer.Materials;

/// <summary>
/// Estevez-Kulla 2017 "Production-Friendly Microfacet Sheen BRDF"
/// — the inverted-Gaussian sheen NDF used by Arnold, USDPreviewSurface,
/// glTF KHR_materials_sheen, Filament, Cycles and PxrSurface. Replaces
/// the legacy Disney 2012 Schlick-weighted sheen, which under-represents
/// the grazing-angle response that defines fabrics, dust and velvet.
///
/// Why this and not Zeltner 2022 LTC: the LTC variant requires an
/// offline-fitted lookup table (32×32×4 floats) shipped with each
/// renderer, and the published table is encumbered by Imageworks
/// licensing. Estevez-Kulla's closed-form NDF + Smith Λ polynomial
/// reaches the same grazing-angle behaviour with no LUT dependency
/// and is the de-facto production standard adopted by every major
/// open renderer.
///
/// All inputs are scalar cosines (NdotV / NdotL / NdotH); the model
/// is rotationally symmetric so no tangent-space transform is needed.
/// Roughness convention: α = sheen_roughness ∈ (0, 1]; α → 0 gives an
/// extremely thin, tightly-grazing-angle response (think "metallic-
/// looking velvet"), α → 1 gives a broader, more matte sheen.
/// </summary>
internal static class SheenCharlie
{
    private const float MinAlpha = 0.04f;

    /// <summary>
    /// Charlie NDF: D(θ_h) = (2 + 1/α) · sin(θ_h)^(1/α) / (2π).
    /// Peaks at θ_h = π/2 (grazing) for any α &lt; 1, the defining
    /// feature of fabric sheen — single-scattering Lambert and Schlick
    /// peak at θ_h = 0 instead and so can't reproduce the look.
    /// </summary>
    public static float D(float NdotH, float alpha)
    {
        alpha = MathF.Max(alpha, MinAlpha);
        float invAlpha = 1f / alpha;
        float cos2 = NdotH * NdotH;
        float sin2 = MathF.Max(1f - cos2, 0f);
        // sin(θ_h)^(1/α) = (sin²)^(1/(2α)) — numerically more stable
        // than computing sqrt then pow at near-zero sin.
        float sinPow = MathF.Pow(sin2, 0.5f * invAlpha);
        return (2f + invAlpha) * sinPow / (2f * MathF.PI);
    }

    /// <summary>
    /// Smith Λ polynomial fit (Estevez-Kulla 2017, table 2). Two
    /// regimes blended on α: a smooth-cloth fit for α ≤ 0.25 and a
    /// rough-cloth fit for α &gt; 0.25, then linearly interpolated
    /// across the boundary so the result is C0-continuous.
    ///
    /// Returned form is the natural-log Λ value; callers exponentiate
    /// it. The fit is only defined for cosTheta in (0, 1]; the caller
    /// is responsible for clamping.
    /// </summary>
    private static float LambdaLog(float cosTheta, float alpha)
    {
        // Coefficient sets (a, b, c, d, e) from Imageworks supplemental.
        // Λ(cosθ) = a / (1 + b · cosθ^c) + d · cosθ + e
        float a, b, c, d, e;
        if (alpha < 0.25f)
        {
            a = 25.3245f;
            b = 3.32435f;
            c = 0.16801f;
            d = -1.27393f;
            e = -4.85967f;
        }
        else
        {
            a = 21.5473f;
            b = 3.82987f;
            c = 0.19823f;
            d = -1.97760f;
            e = -4.32054f;
        }
        return a / (1f + b * MathF.Pow(cosTheta, c)) + d * cosTheta + e;
    }

    /// <summary>
    /// Smith Λ for the Charlie NDF — exp of the polynomial fit.
    /// At α ≈ 0.25 (the regime split) we linearly interpolate the
    /// two fits over a ±0.05 window so the value is continuous.
    /// </summary>
    public static float Lambda(float cosTheta, float alpha)
    {
        cosTheta = Math.Clamp(cosTheta, 1e-4f, 1f);
        // Continuity blend across the regime split.
        const float split = 0.25f;
        const float window = 0.05f;
        if (alpha <= split - window)
            return MathF.Exp(LambdaLog(cosTheta, 0f));     // smooth fit
        if (alpha >= split + window)
            return MathF.Exp(LambdaLog(cosTheta, 1f));     // rough fit
        float t = (alpha - (split - window)) / (2f * window);
        float lo = MathF.Exp(LambdaLog(cosTheta, 0f));
        float hi = MathF.Exp(LambdaLog(cosTheta, 1f));
        return lo + (hi - lo) * t;
    }

    /// <summary>
    /// Smith uncorrelated G2 (Estevez-Kulla §4.1):
    ///   G(V, L) = 1 / ((1 + Λ(V)) · (1 + Λ(L)))
    /// Heitz et al. proved the height-correlated form gives noisier
    /// images at low spp; Imageworks ship the uncorrelated form and
    /// it's what every cited engine implements.
    /// </summary>
    public static float G(float NdotV, float NdotL, float alpha)
        => 1f / ((1f + Lambda(NdotV, alpha)) * (1f + Lambda(NdotL, alpha)));

    /// <summary>
    /// Cook-Torrance assembly: f = D · G / (4 · NdotV · NdotL).
    /// Scalar (sheen is monochromatic before the artist's tint) — the
    /// caller multiplies by the sheen colour.
    /// </summary>
    public static float Brdf(float NdotV, float NdotL, float NdotH, float alpha)
    {
        float denom = 4f * MathF.Max(NdotV, 1e-6f) * MathF.Max(NdotL, 1e-6f);
        return D(NdotH, alpha) * G(NdotV, NdotL, alpha) / denom;
    }
}
