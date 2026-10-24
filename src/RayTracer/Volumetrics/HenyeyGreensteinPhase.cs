using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Henyey-Greenstein phase function, g in (-1, 1): positive g scatters
/// forward (<c>wi</c> near <c>wo</c>, see <see cref="IPhaseFunction"/>).
/// Sampled by its exact inverse CDF.
/// </summary>
public sealed class HenyeyGreensteinPhase : IPhaseFunction
{
    private readonly float _g;
    public HenyeyGreensteinPhase(float g) => _g = Math.Clamp(g, -0.999f, 0.999f);

    public float Evaluate(Vector3 wo, Vector3 wi) => Value(_g, Vector3.Dot(wo, wi));

    internal static float Value(float g, float mu)
    {
        float denom = 1f + g * g - 2f * g * mu;
        return MathUtils.Inv4Pi * (1f - g * g) / (denom * MathF.Sqrt(denom));
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo, Vector2 u)
    {
        Vector3 wi = PhaseFrame.Around(wo, SampleCos(_g, u.X), 2f * MathF.PI * u.Y);
        return (wi, Evaluate(wo, wi));
    }

    /// <summary>Inverse CDF of cos θ; u = 1 is the forward direction.</summary>
    internal static float SampleCos(float g, float u)
    {
        if (MathF.Abs(g) < 1e-3f) return 1f - 2f * (1f - u);
        float sqr = (1f - g * g) / (1f - g + 2f * g * u);
        return (1f + g * g - sqr * sqr) / (2f * g);
    }
}
