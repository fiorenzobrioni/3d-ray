using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Schlick phase function (Blasi, Le Saec, Schlick 1993), a rational
/// approximation of Henyey-Greenstein without the square root:
///
///   p(cosθ) = (1 - k²) / (4π (1 - k·cosθ)²),   k ∈ (-1, 1)
///
/// The constructor takes the Henyey-Greenstein g and maps it with the usual
/// fit k = 1.55 g - 0.55 g³. Sampled by its exact inverse CDF.
/// </summary>
public sealed class SchlickPhase : IPhaseFunction
{
    private readonly float _k;

    public SchlickPhase(float g)
    {
        float gc = Math.Clamp(g, -0.999f, 0.999f);
        _k = Math.Clamp(1.55f * gc - 0.55f * gc * gc * gc, -0.999f, 0.999f);
    }

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        float mu = Vector3.Dot(wo, wi);
        float denom = 1f - _k * mu;
        return MathUtils.Inv4Pi * (1f - _k * _k) / (denom * denom);
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo, Vector2 u)
    {
        float s = 2f * u.X - 1f;
        Vector3 wi = PhaseFrame.Around(wo, (s + _k) / (1f + _k * s), 2f * MathF.PI * u.Y);
        return (wi, Evaluate(wo, wi));
    }
}
