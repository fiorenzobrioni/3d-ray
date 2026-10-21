using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// EON, the energy-preserving Oren-Nayar diffuse BRDF of OpenPBR (Portsmouth,
/// Kutz and Hill 2024; PIANO-REVIEW.md, Phase 5): Fujii's improved Oren-Nayar
/// single-scattering lobe plus a multiple-scattering lobe that restores the
/// light the rough facets trap, so a white surface reflects all of it at every
/// roughness, and roughness 0 is exactly Lambert.
///
/// <para>f = ρ/π · A (1 + σ s/t) + ρ_ms/π · (1 − Ê(μ_i))(1 − Ê(μ_o)) / (1 − ⟨Ê⟩),
/// with s = ω_i·ω_o − μ_i μ_o, t = max(μ_i, μ_o) when s &gt; 0 (else 1),
/// A = 1/(1 + (1/2 − 2/(3π)) σ), Ê the directional albedo of the Fujii lobe in
/// closed form, ⟨Ê⟩ its cosine-weighted average and
/// ρ_ms = ρ² ⟨Ê⟩ / (1 − ρ (1 − ⟨Ê⟩)). The lobe is sampled with the cosine
/// distribution, which it never strays far from.</para>
/// </summary>
internal static class Eon
{
    private const float C1 = 0.5f - 2f / (3f * MathF.PI);
    private const float C2 = 2f / 3f - 28f / (15f * MathF.PI);

    /// <summary>Ê(μ, σ), the directional albedo of the Fujii lobe with ρ = 1 (exact).</summary>
    public static float FujiiAlbedo(float mu, float sigma)
    {
        mu = Math.Clamp(mu, 1e-4f, 1f);
        float a = 1f / (1f + C1 * sigma);
        float si = MathF.Sqrt(MathF.Max(0f, 1f - mu * mu));
        float g = si * (MathF.Acos(mu) - si * mu)
                + 2f / 3f * ((si / mu) * (1f - si * si * si) - si);
        return a + sigma * a * g / MathF.PI;
    }

    /// <summary>⟨Ê⟩(σ) = 2 ∫ Ê(μ) μ dμ.</summary>
    public static float FujiiAverage(float sigma)
    {
        float a = 1f / (1f + C1 * sigma);
        return a * (1f + C2 * sigma);
    }

    private static Vector3 MultipleScatteringColor(Vector3 rho, float avg) =>
        rho * rho * avg / (Vector3.One - rho * MathF.Max(0f, 1f - avg));

    /// <summary>The BRDF (no cosine) for the cosines μ_i, μ_o of the two
    /// directions to the normal and their dot product.</summary>
    public static Vector3 Evaluate(Vector3 rho, float sigma, float muI, float muO, float dotIO)
    {
        if (muI <= 0f || muO <= 0f) return Vector3.Zero;
        float s = dotIO - muI * muO;
        float sOverT = s > 0f ? s / MathF.Max(muI, muO) : s;
        float a = 1f / (1f + C1 * sigma);
        Vector3 single = rho * (a * MathF.Max(0f, 1f + sigma * sOverT));
        if (sigma <= 0f) return single / MathF.PI;

        float avg = FujiiAverage(sigma);
        const float eps = 1e-7f;
        float shape = MathF.Max(eps, 1f - FujiiAlbedo(muI, sigma)) * MathF.Max(eps, 1f - FujiiAlbedo(muO, sigma))
                    / MathF.Max(eps, 1f - avg);
        return (single + MultipleScatteringColor(rho, avg) * shape) / MathF.PI;
    }

    /// <summary>Directional albedo of the whole lobe: lerp(ρ_ms, ρ, Ê(μ)).</summary>
    public static Vector3 Albedo(Vector3 rho, float sigma, float mu)
    {
        if (sigma <= 0f) return rho;
        float e = FujiiAlbedo(mu, sigma);
        Vector3 ms = MultipleScatteringColor(rho, FujiiAverage(sigma));
        return ms + (rho - ms) * e;
    }
}
