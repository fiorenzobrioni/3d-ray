using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Rayleigh phase function for scattering off particles much smaller than the
/// wavelength of light (gas molecules in the atmosphere).
///
///   p(cosθ) = (3 / 16π) · (1 + cos²θ)
///
/// Symmetric forward/backward (cos²θ), peaks at θ = 0 and θ = π, minimum at
/// θ = π/2. Used by sky models (Bruneton, Hosek-Wilkie) and Arnold's
/// atmospheric shaders for the molecular scattering component of air.
/// </summary>
public sealed class RayleighPhase : IPhaseFunction
{
    private const float ThreeOver16Pi = 3f / (16f * MathF.PI);

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        float mu = Vector3.Dot(wo, wi);
        return ThreeOver16Pi * (1f + mu * mu);
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        // Closed-form inverse CDF of μ ∈ [-1, 1] under p_μ(μ) = (3/8)(1 + μ²).
        // Solving μ³ + 3μ = 8u - 4 via the trigonometric/hyperbolic Cardano form
        // with substitution μ = 2·sinh(t): sinh(3t) = 4u - 2.
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();

        float v = 4f * u1 - 2f;
        float t = MathF.Asinh(v) / 3f;
        float cosTheta = 2f * MathF.Sinh(t);
        cosTheta = MathF.Max(-1f, MathF.Min(1f, cosTheta));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;

        // ONB around wo so cosTheta = 1 ⇒ wi = wo (shared with HG convention).
        Vector3 w = wo;
        Vector3 a = MathF.Abs(w.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v1 = Vector3.Normalize(Vector3.Cross(w, a));
        Vector3 u = Vector3.Cross(w, v1);
        Vector3 wi = sinTheta * MathF.Cos(phi) * u + sinTheta * MathF.Sin(phi) * v1 + cosTheta * w;
        wi = Vector3.Normalize(wi);
        return (wi, Evaluate(wo, wi));
    }
}
