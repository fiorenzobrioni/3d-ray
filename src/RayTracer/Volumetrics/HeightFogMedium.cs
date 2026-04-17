using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Exponential height fog — the atmospheric model used by Arnold
/// <c>atmosphere_volume</c>, V-Ray <c>EnvironmentFog</c> and most "aerial
/// perspective" shaders:
///
///   σ_T(y) = σ_T0 · exp(-(y - y0) / H)
///
/// Density is σ_T0 at reference altitude y0 and falls off exponentially with
/// a scale height H. The optical depth along any ray has a closed form, so
/// there is no need for delta tracking: variance stays at homogeneous levels.
///
/// σ_a and σ_s are assumed to share the same height falloff (standard
/// assumption for density-scaled media).
/// </summary>
public sealed class HeightFogMedium : IMedium
{
    private readonly Vector3 _sigmaA0;
    private readonly Vector3 _sigmaS0;
    private readonly Vector3 _sigmaT0;
    private readonly float _y0;
    private readonly float _invH;   // 1 / scale height
    private const float ExpClamp = 50f; // caps exp() argument to avoid overflow

    public IPhaseFunction Phase { get; }

    public HeightFogMedium(Vector3 sigmaA0, Vector3 sigmaS0,
                           float y0, float scaleHeight,
                           IPhaseFunction phase)
    {
        _sigmaA0 = Vector3.Max(sigmaA0, Vector3.Zero);
        _sigmaS0 = Vector3.Max(sigmaS0, Vector3.Zero);
        _sigmaT0 = _sigmaA0 + _sigmaS0;
        _y0 = y0;
        _invH = 1f / MathF.Max(1e-4f, scaleHeight);
        Phase = phase;
    }

    /// <summary>
    /// Scalar altitude factor exp(-(y - y0)/H) at ray parameter t.
    /// Multiplied by σ_T0 / σ_S0 / σ_A0 gives local coefficients.
    /// </summary>
    private float AltitudeFactor(Ray ray, float t)
    {
        float k = -(ray.Origin.Y + t * ray.Direction.Y - _y0) * _invH;
        if (k > ExpClamp) k = ExpClamp;
        if (k < -ExpClamp) k = -ExpClamp;
        return MathF.Exp(k);
    }

    /// <summary>
    /// Analytic optical depth τ(tMax) = ∫₀^tMax σ_T(s) ds per channel.
    /// B = d.y/H. For |B|<ε the ray is effectively horizontal and τ = A·tMax.
    /// Otherwise τ = A · (1 - exp(-B·tMax)) / B.
    /// </summary>
    private Vector3 OpticalDepth(Ray ray, float tMax)
    {
        float d = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        Vector3 A = _sigmaT0 * AltitudeFactor(ray, 0f);
        float B = ray.Direction.Y * _invH;

        if (MathF.Abs(B) < 1e-6f)
            return A * d;

        float arg = -B * d;
        if (arg > ExpClamp) arg = ExpClamp;
        if (arg < -ExpClamp) arg = -ExpClamp;
        float falloff = (1f - MathF.Exp(arg)) / B;
        // Falloff can be negative when B<0 and arg>0: (1-e^+)/negative → positive. OK.
        return A * falloff;
    }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        Vector3 tau = OpticalDepth(ray, tMax);
        return new Vector3(
            MathF.Exp(-tau.X),
            MathF.Exp(-tau.Y),
            MathF.Exp(-tau.Z));
    }

    public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
    {
        // Spectral sampling with uniform channel selection (same strategy as
        // HomogeneousMedium, section PBRT §15.2).
        int ch = (int)(MathUtils.RandomFloat() * 3f);
        if (ch > 2) ch = 2;

        Vector3 A = _sigmaT0 * AltitudeFactor(ray, 0f);
        float A_ch = ch == 0 ? A.X : ch == 1 ? A.Y : A.Z;
        float B = ray.Direction.Y * _invH;

        if (A_ch <= 0f)
        {
            // Selected channel is empty at origin → no scattering possible.
            t = tMax;
            beta = Vector3.One;
            scattered = false;
            return false;
        }

        float uExp = -MathF.Log(1f - MathUtils.RandomFloat());  // rate-1 exponential

        float dist;
        if (MathF.Abs(B) < 1e-6f)
        {
            // Horizontal / near-horizontal ray: homogeneous at this altitude.
            dist = uExp / A_ch;
        }
        else
        {
            // τ(t) = A_ch · (1 - exp(-B·t)) / B.
            // Invert: 1 - exp(-B·t) = B · uExp / A_ch → t = -log(1 - B·uExp/A_ch) / B.
            // When B>0 and uExp ≥ A_ch/B the total τ is bounded and no event occurs.
            float X = 1f - B * uExp / A_ch;
            if (X <= 0f)
            {
                dist = float.PositiveInfinity;
            }
            else
            {
                dist = -MathF.Log(X) / B;
            }
        }

        scattered = dist < tMax;
        t = scattered ? dist : tMax;

        Vector3 Tr = Transmittance(ray, t);
        Vector3 localSigmaT = scattered ? _sigmaT0 * AltitudeFactor(ray, t) : Vector3.Zero;

        Vector3 density = scattered ? localSigmaT * Tr : Tr;
        float pdf = (density.X + density.Y + density.Z) / 3f;
        if (pdf < 1e-20f) pdf = 1f;

        if (scattered)
        {
            Vector3 localSigmaS = _sigmaS0 * AltitudeFactor(ray, t);
            beta = Tr * localSigmaS / pdf;
        }
        else
        {
            beta = Tr / pdf;
        }
        return scattered;
    }
}
