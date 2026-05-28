using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Earth-atmosphere participating medium — aerial perspective driven by the
/// same Rayleigh + Mie scattering coefficients as
/// <see cref="Rendering.Sky.NishitaSky"/>. Adds the haze-coloured distance
/// attenuation that turns far-away mountains blue, sunsets red through the
/// scene, and produces the characteristic depth-of-air look offline renderers
/// get from Arnold <c>atmosphere_volume</c> + sun, or Cycles "Volume Scatter"
/// + sky.
///
/// <para><b>Two species, exponential profiles</b> — sums Rayleigh
/// (wavelength-dependent, 8 km scale height) and Mie (grey, 1.2 km scale
/// height) into a single per-channel σ_t. Both species share the same
/// <see cref="IPhaseFunction"/> for sampling — the default is Henyey-Greenstein
/// g = 0.76 (Mie-dominated forward scattering). For physically-faithful
/// in-scattering the caller can pass an explicit phase function; the medium
/// itself doesn't model multi-species phase blending (a future refinement is
/// a <c>RayleighMiePhase</c> that weights by per-species local σ_s).</para>
///
/// <para><b>World-to-atmosphere mapping.</b> Real atmosphere extends 60 km
/// vertically; most scenes occupy a few-dozen world units. The
/// <c>worldScale</c> parameter maps world units to metres (default 1000 m =
/// "1 world unit = 1 km") so a Rayleigh scale height of 8 km becomes 8 world
/// units at scale 1000. <c>seaLevelY</c> sets the world-Y of altitude 0.
/// Density above the atmosphere top is clamped to 0; below sea level it is
/// clamped to sea-level values (avoids fog inside underground caverns).</para>
///
/// <para><b>Optical depth.</b> The integral of a sum of two exponentials
/// along an axis-aligned ray has a closed form (the same closed-form
/// <see cref="HeightFogMedium"/> uses, applied twice). No delta tracking
/// needed in the optical-depth path — variance stays at homogeneous-medium
/// levels.</para>
///
/// <para><b>Reference.</b> Same physical constants as
/// <see cref="Rendering.Sky.NishitaSky"/>: Bruneton &amp; Neyret 2008,
/// Cycles <c>intern/cycles/kernel/svm/sky.h</c>.</para>
/// </summary>
public sealed class NishitaAtmosphereMedium : IMedium
{
    // ── Earth atmosphere constants (per metre at sea level) ──────────────────
    // Match Rendering.Sky.NishitaSky values so the medium and the sky agree.
    private static readonly Vector3 RayleighSigmaS = new(5.802e-6f, 1.358e-5f, 3.310e-5f);
    private static readonly Vector3 MieSigmaS      = new(3.996e-6f, 3.996e-6f, 3.996e-6f);
    private static readonly Vector3 MieSigmaA      = MieSigmaS * 0.11f;
    private const float RayleighScaleHm = 8_000f;   // 8 km in metres
    private const float MieScaleHm      = 1_200f;   // 1.2 km in metres

    // ── Public parameters ───────────────────────────────────────────────────
    public Vector3 AirDensity { get; }   // RGB multiplier — usually scalar but kept Vec3 for parity
    public float DustDensity { get; }
    public float SeaLevelY { get; }
    /// <summary>Metres per world unit. 1000 ≈ "1 world unit = 1 km".</summary>
    public float WorldScale { get; }

    public IPhaseFunction Phase { get; }

    /// <summary>Local σ_s (Rayleigh + Mie) at the world point — public for testing.</summary>
    public Vector3 SigmaS_AtY(float y) => RayleighAt(y) + MieScattAt(y);
    /// <summary>Local σ_t (Rayleigh + Mie absorption + Mie scattering) at the world point.</summary>
    public Vector3 SigmaT_AtY(float y) => RayleighAt(y) + (MieSigmaS + MieSigmaA) * MieDensityWorld(y) * DustDensity;

    // Internal: density profile (unitless) at world Y.
    private float RayleighDensityWorld(float worldY)
    {
        float altM = (worldY - SeaLevelY) * WorldScale;
        if (altM < 0f) altM = 0f;
        if (altM > 60_000f) return 0f;
        return MathF.Exp(-altM / RayleighScaleHm) * AirDensity.X;  // air density assumed grey
    }
    private float MieDensityWorld(float worldY)
    {
        float altM = (worldY - SeaLevelY) * WorldScale;
        if (altM < 0f) altM = 0f;
        if (altM > 60_000f) return 0f;
        return MathF.Exp(-altM / MieScaleHm);
    }

    /// <summary>σ_s · ρ_r at world Y (Rayleigh species).</summary>
    private Vector3 RayleighAt(float worldY)  => RayleighSigmaS * RayleighDensityWorld(worldY);
    /// <summary>σ_s · ρ_m at world Y (Mie scattering only).</summary>
    private Vector3 MieScattAt(float worldY)  => MieSigmaS * (MieDensityWorld(worldY) * DustDensity);

    public NishitaAtmosphereMedium(IPhaseFunction phase,
                                    Vector3? airDensity = null,
                                    float dustDensity = 1f,
                                    float seaLevelY = 0f,
                                    float worldScale = 1000f)
    {
        Phase = phase;
        AirDensity = airDensity ?? Vector3.One;
        DustDensity = MathF.Max(0f, dustDensity);
        SeaLevelY = seaLevelY;
        WorldScale = MathF.Max(1e-3f, worldScale);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Optical depth (closed form for each species, summed)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// τ(t) = ∫₀^t σ_t(y(s)) ds. With y(s) = origin.y + s·dir.y and σ_t a sum
    /// of two exponentials in altitude, the integral has the same closed form
    /// the height fog uses, applied twice.
    /// </summary>
    private Vector3 OpticalDepth(Ray ray, float tMax)
    {
        float d = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        if (d <= 0f) return Vector3.Zero;

        Vector3 tau = SpeciesOpticalDepth(ray, d, RayleighSigmaS, RayleighScaleHm, /*useAirDensity*/ true);
        tau += SpeciesOpticalDepth(ray, d, MieSigmaS + MieSigmaA, MieScaleHm, /*useAirDensity*/ false);
        return tau;
    }

    private Vector3 SpeciesOpticalDepth(Ray ray, float d, Vector3 sigmaAtSeaLevel,
                                         float scaleHm, bool useAirDensity)
    {
        // Density at origin: exp(-altM₀ / H) × densityMul.
        float altM0 = (ray.Origin.Y - SeaLevelY) * WorldScale;
        if (altM0 < 0f) altM0 = 0f;
        if (altM0 > 60_000f) return Vector3.Zero;
        float rho0 = MathF.Exp(-altM0 / scaleHm);
        float densityMul = useAirDensity ? AirDensity.X : DustDensity;
        Vector3 A = sigmaAtSeaLevel * (rho0 * densityMul);

        // B = d(ln ρ)/dt = -worldScale · dir.y / scaleHm  (per world unit of t)
        float B = -ray.Direction.Y * WorldScale / scaleHm;

        if (MathF.Abs(B) < 1e-8f)
        {
            // Horizontal — uniform along ray at this altitude.
            return A * d;
        }

        // τ = A · (1 - exp(B·t)) / (-B) ... we want ∫ exp(B·s) ds = (exp(B·d) - 1)/B
        // density(s) = rho0 · exp(B·s) where B carries sign of (-dir.y).
        // ∫₀^d density(s) ds = (exp(B·d) - 1) / B
        float arg = B * d;
        if (arg > 50f) arg = 50f;
        if (arg < -50f) arg = -50f;
        float integral = (MathF.Exp(arg) - 1f) / B;
        return A * integral;
    }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        Vector3 tau = OpticalDepth(ray, tMax);
        return new Vector3(MathF.Exp(-tau.X), MathF.Exp(-tau.Y), MathF.Exp(-tau.Z));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Free-path sampling (delta tracking with majorant)
    // ─────────────────────────────────────────────────────────────────────────

    public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
    {
        // Majorant: density at the lower altitude end of the segment, since
        // both species decay with altitude. For downward rays the start is
        // higher and end is lower; for upward, the start is lower. Pick the
        // greater of (start, end) densities.
        float yEnd = ray.Origin.Y + ray.Direction.Y * tMax;
        float yLow = MathF.Min(ray.Origin.Y, yEnd);
        Vector3 sigmaTMaj = SigmaT_AtY(yLow);
        float sigmaTMajScalar = MathF.Max(sigmaTMaj.X, MathF.Max(sigmaTMaj.Y, sigmaTMaj.Z));

        if (sigmaTMajScalar < 1e-12f)
        {
            t = tMax;
            beta = Vector3.One;
            scattered = false;
            return false;
        }

        // Delta tracking — sample candidates from the majorant, accept with
        // probability σ_t(p) / σ_t_maj. The scalar control channel is the
        // average σ_t; the *scalar* transmittance it implies is carried by the
        // sampling process itself (the null-collision random walk reaches t
        // with probability Tr_scalar(t)). beta must therefore NOT re-apply the
        // scalar transmittance — only the chromatic ratio Tr_vec / Tr_scalar
        // that restores wavelength-dependent attenuation (blue sky / red
        // sunset) is multiplied in. Re-applying the full analytic transmittance
        // (the previous behaviour) double-counted it and made the atmosphere
        // far too dark on both the scatter and the pass-through branch.
        float tCur = 0f;
        const int MaxAttempts = 256;
        for (int i = 0; i < MaxAttempts; i++)
        {
            float u = MathUtils.RandomFloat();
            tCur += -MathF.Log(MathF.Max(1e-20f, 1f - u)) / sigmaTMajScalar;
            if (tCur >= tMax) break;
            Vector3 p = ray.Origin + ray.Direction * tCur;
            Vector3 sigmaTLocal = SigmaT_AtY(p.Y);
            float sigmaTLocalScalar = (sigmaTLocal.X + sigmaTLocal.Y + sigmaTLocal.Z) / 3f;
            if (MathUtils.RandomFloat() < sigmaTLocalScalar / sigmaTMajScalar)
            {
                // Real scattering event.
                t = tCur;
                Vector3 sigmaS = SigmaS_AtY(p.Y);
                float denomScalar = sigmaTLocalScalar;
                if (denomScalar < 1e-20f) denomScalar = 1f;
                beta = ChromaticTrRatio(ray, tCur) * sigmaS / denomScalar;
                scattered = true;
                return true;
            }
        }

        // No event before tMax: the pass-through transmittance is already
        // encoded in the probability of reaching tMax (Tr_scalar); weight only
        // by the chromatic ratio so the through-radiance keeps its true
        // per-channel attenuation.
        t = tMax;
        beta = ChromaticTrRatio(ray, tMax);
        scattered = false;
        return false;
    }

    /// <summary>
    /// Tr_vec(t) / Tr_scalar(t) = exp(-(τ_c - τ̄)) per channel, where τ̄ is the
    /// average optical depth (the scalar control channel of the delta tracker).
    /// Mean of the three channels is ≈ 1, so it recolours without changing the
    /// overall magnitude that the sampling process already accounts for.
    /// </summary>
    private Vector3 ChromaticTrRatio(Ray ray, float t)
    {
        Vector3 tau = OpticalDepth(ray, t);
        float tauScalar = (tau.X + tau.Y + tau.Z) / 3f;
        return new Vector3(
            MathF.Exp(tauScalar - tau.X),
            MathF.Exp(tauScalar - tau.Y),
            MathF.Exp(tauScalar - tau.Z));
    }
}
