using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Schlick phase function (Blasi-Le Saec-Schlick, 1993) — a rational
/// approximation of Henyey-Greenstein that is cheaper to evaluate (no sqrt).
///
///   p(cosθ) = (1 - k²) / (4π (1 - k·cosθ)²),   k ∈ (-1, 1)
///
/// Convention matches <see cref="HenyeyGreensteinPhase"/>: positive k = forward
/// scatter (wi ≈ wo), negative k = back scatter. The constructor takes the HG
/// anisotropy <c>g</c> and maps it via <c>k = 1.55·g - 0.55·g³</c> — the
/// standard best-fit curve used by RenderMan and Cycles fast-scatter mode.
/// </summary>
public sealed class SchlickPhase : IPhaseFunction
{
    private readonly float _k;

    public SchlickPhase(float g)
    {
        float gc = MathF.Max(-0.999f, MathF.Min(0.999f, g));
        float k = 1.55f * gc - 0.55f * gc * gc * gc;
        _k = MathF.Max(-0.999f, MathF.Min(0.999f, k));
    }

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        float mu = Vector3.Dot(wo, wi);
        float denom = 1f - _k * mu;
        return MathUtils.Inv4Pi * (1f - _k * _k) / (denom * denom);
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();

        // Closed-form inverse CDF: μ = (2u - 1 + k) / (1 + k(2u - 1)).
        // k = 0 collapses to μ = 2u - 1 (isotropic).
        float s = 2f * u1 - 1f;
        float cosTheta = (s + _k) / (1f + _k * s);
        cosTheta = MathF.Max(-1f, MathF.Min(1f, cosTheta));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;

        Vector3 w = wo;
        Vector3 a = MathF.Abs(w.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v = Vector3.Normalize(Vector3.Cross(w, a));
        Vector3 u = Vector3.Cross(w, v);
        Vector3 wi = sinTheta * MathF.Cos(phi) * u + sinTheta * MathF.Sin(phi) * v + cosTheta * w;
        wi = Vector3.Normalize(wi);
        return (wi, Evaluate(wo, wi));
    }
}
