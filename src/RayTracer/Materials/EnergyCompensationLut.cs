using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// Kulla-Conty 2017 / Turquin 2019 multi-scattering energy-compensation
/// tables for GGX microfacet BSDFs.
///
/// The Smith single-scattering microfacet model only integrates the first
/// bounce between facets and therefore drops energy at high roughness: a
/// white rough (α = 0.9) metal loses ~30-40% of its incident energy that
/// way, which shows up as rough metals going dark instead of retaining
/// their baseColor. Kulla-Conty's fix is an additive compensation lobe
///   f_ms(V, L) = F_ms · (1-E(μo)) · (1-E(μi)) / (π · (1-E_avg))
/// with F_ms = F̄² · E_avg / (1 - F̄·(1-E_avg)).
///
/// The directional albedo of a white single-scatter GGX BRDF
///   E(μ, α)  = ∫ f_ss(V, L; F = 1) · cos(L) dω
/// and its μ-weighted average
///   E_avg(α) = 2 ∫ E(μ, α) · μ dμ
/// are pre-tabulated here. They depend only on (μ, α) and can be
/// Monte-Carlo-built once at first access — no serialised asset.
///
/// Build strategy: for each (μ, α) bin, draw <see cref="SamplesPerBin"/>
/// VNDF half-vectors via <see cref="Microfacet.SampleGgxVndfAniso"/> with
/// αx = αy = α, reflect V, and accumulate G1(L). Because VNDF sampling
/// gives pdf_L = G1(V)·D/(4·NdotV) and the integrand (with F = 1) is
/// D·G/(4·NdotV·NdotL)·NdotL = D·G2/(4·NdotV), the per-sample estimator
/// reduces to G2/G1(V) = G1(L) (separable Smith) — no division risk and
/// no rejection.
/// </summary>
internal static class EnergyCompensationLut
{
    private const int MuRes = 32;
    private const int AlphaRes = 32;
    private const int SamplesPerBin = 2048;

    private static readonly float[] E = new float[MuRes * AlphaRes];
    private static readonly float[] EAvg = new float[AlphaRes];

    // Sequential — deliberately NOT Parallel.For. The static ctor may fire
    // from inside Renderer.Render's outer Parallel.For if the first Disney
    // hit happens on a worker thread; spawning a nested Parallel.For there
    // starves the shared ThreadPool and deadlocks (first worker holds the
    // static-ctor lock, all other workers block on that lock, nested tasks
    // have no free workers to run). Callers that care about cold-start cost
    // should invoke <see cref="Prewarm"/> once from the main thread before
    // any rendering begins.
    static EnergyCompensationLut()
    {
        for (int ai = 0; ai < AlphaRes; ai++)
        {
            float alpha = AlphaValue(ai);
            for (int mi = 0; mi < MuRes; mi++)
            {
                float mu = MuValue(mi);
                // Distinct seed per (mi, ai) pair so MC noise isn't correlated
                // across bins — the table is read via bilinear interpolation
                // and correlated noise would show up as coherent artefacts.
                uint seed = unchecked((uint)((mi * 0x9E3779B9) ^ (ai * 0xB5297A4D) ^ 0xC2B2AE3D));
                E[mi * AlphaRes + ai] = IntegrateE(mu, alpha, seed);
            }

            // E_avg(α) = 2 ∫₀¹ E(μ, α) · μ dμ. Rectangle rule on μ bin-centres
            // (Σ 2·E(μi)·μi / N) — the trapezoid correction is negligible at
            // 32 bins given the MC noise floor on E itself.
            float avg = 0f;
            for (int mi = 0; mi < MuRes; mi++)
                avg += 2f * E[mi * AlphaRes + ai] * MuValue(mi);
            EAvg[ai] = Math.Clamp(avg / MuRes, 0f, 1f);
        }
    }

    /// <summary>
    /// Forces the static constructor to run on the current thread. Call once
    /// before rendering so the (~few-hundred-ms) MC build cost is paid on the
    /// main thread rather than inside the parallel render loop, where it would
    /// serialise one worker while the rest wait on the static-ctor lock.
    /// </summary>
    public static void Prewarm()
    {
        // Touch both tables so readers on any axis trigger the static ctor.
        _ = SampleE(0.5f, 0.5f);
        _ = SampleEAvg(0.5f);
    }

    // Bin centres. μ ∈ (0, 1], α ∈ [0.001, 1] — α is floored to keep the
    // microfacet NDF well-defined at the mirror extreme.
    private static float MuValue(int i) => (i + 0.5f) / MuRes;
    private static float AlphaValue(int i) => MathF.Max((i + 0.5f) / AlphaRes, 0.001f);

    private static float IntegrateE(float mu, float alpha, uint seed)
    {
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - mu * mu));
        Vector3 V = new(sinTheta, 0f, mu);

        double sum = 0.0;
        uint state = seed == 0 ? 1u : seed;
        int hits = 0;
        for (int s = 0; s < SamplesPerBin; s++)
        {
            float u1 = NextFloat(ref state);
            float u2 = NextFloat(ref state);
            Vector3 H = Microfacet.SampleGgxVndfAniso(V, alpha, alpha, u1, u2);
            float vdh = Vector3.Dot(V, H);
            // Reflect V about H to obtain the sampled L.
            Vector3 L = 2f * vdh * H - V;
            if (L.Z <= 0f) continue;
            sum += Microfacet.G1GgxAniso(L, alpha, alpha);
            hits++;
        }
        // Divide by the total sample count (including below-surface ones) so
        // the estimator matches the full directional integral. hits is only
        // used as a guard against pathological early-exit loops.
        return hits == 0 ? 0f : (float)Math.Clamp(sum / SamplesPerBin, 0.0, 1.0);
    }

    /// <summary>
    /// xorshift32 — a cheap PRNG is adequate for MC LUT building; the per-
    /// bin sum is averaged over 2k samples and read back through bilinear
    /// interpolation, so low-order correlations wash out.
    /// </summary>
    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        // Upper 24 bits → [0, 1); keeps the result well under ½-ULP error.
        return (state & 0x00FFFFFFu) / (float)0x01000000u;
    }

    /// <summary>
    /// Bilinearly-interpolated E(μ, α). μ and α are clamped to the table
    /// domain so out-of-range inputs degenerate to the nearest edge value.
    /// </summary>
    public static float SampleE(float mu, float alpha)
    {
        mu    = Math.Clamp(mu,    0f,     1f);
        alpha = Math.Clamp(alpha, 0.001f, 1f);

        float fmu = mu * MuRes - 0.5f;
        float fal = alpha * AlphaRes - 0.5f;
        int mi0 = Math.Clamp((int)MathF.Floor(fmu), 0, MuRes - 1);
        int ai0 = Math.Clamp((int)MathF.Floor(fal), 0, AlphaRes - 1);
        int mi1 = Math.Min(mi0 + 1, MuRes - 1);
        int ai1 = Math.Min(ai0 + 1, AlphaRes - 1);
        float tu = Math.Clamp(fmu - mi0, 0f, 1f);
        float ta = Math.Clamp(fal - ai0, 0f, 1f);

        float e00 = E[mi0 * AlphaRes + ai0];
        float e01 = E[mi0 * AlphaRes + ai1];
        float e10 = E[mi1 * AlphaRes + ai0];
        float e11 = E[mi1 * AlphaRes + ai1];
        float e0 = e00 + (e01 - e00) * ta;
        float e1 = e10 + (e11 - e10) * ta;
        return e0 + (e1 - e0) * tu;
    }

    /// <summary>
    /// Linearly-interpolated E_avg(α), the μ-weighted average of
    /// <see cref="SampleE"/> across the upper hemisphere.
    /// </summary>
    public static float SampleEAvg(float alpha)
    {
        alpha = Math.Clamp(alpha, 0.001f, 1f);
        float fal = alpha * AlphaRes - 0.5f;
        int ai0 = Math.Clamp((int)MathF.Floor(fal), 0, AlphaRes - 1);
        int ai1 = Math.Min(ai0 + 1, AlphaRes - 1);
        float ta = Math.Clamp(fal - ai0, 0f, 1f);
        return EAvg[ai0] + (EAvg[ai1] - EAvg[ai0]) * ta;
    }
}
