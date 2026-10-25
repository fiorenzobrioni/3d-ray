using System.Numerics;

namespace RayTracer.Volumetrics;

/// <summary>
/// Rayleigh phase function, for particles much smaller than the wavelength
/// (the molecules of air):
///
///   p(cosθ) = (3 / 16π) · (1 + cos²θ)
///
/// Sampled by the closed-form inverse of its CDF in cos θ.
/// </summary>
public sealed class RayleighPhase : IPhaseFunction
{
    private const float ThreeOver16Pi = 3f / (16f * MathF.PI);

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        float mu = Vector3.Dot(wo, wi);
        return ThreeOver16Pi * (1f + mu * mu);
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo, Vector2 u)
    {
        // CDF (μ³ + 3μ + 4)/8 = u: with μ = 2 sinh t, sinh 3t = 4u - 2.
        float t = MathF.Asinh(4f * u.X - 2f) / 3f;
        Vector3 wi = PhaseFrame.Around(wo, 2f * MathF.Sinh(t), 2f * MathF.PI * u.Y);
        return (wi, Evaluate(wo, wi));
    }
}
