using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Two-lobe Henyey-Greenstein phase function used to approximate cloud
/// scattering (strong forward lobe + softer backward/side lobe).
///
///   p(θ) = w · HG(g₁, θ) + (1 - w) · HG(g₂, θ),   w ∈ [0, 1]
///
/// Typical cumulus settings: g₁ ≈ 0.85 (forward), g₂ ≈ -0.3 (silver lining /
/// glory), w ≈ 0.5 — the configuration popularised by Guerrilla Games' Nubis
/// cloud system and also used by Arnold for cumulus rendering. Falls back to
/// a single HG lobe when w = 1 or w = 0.
/// </summary>
public sealed class DoubleHenyeyGreensteinPhase : IPhaseFunction
{
    private readonly HenyeyGreensteinPhase _hg1;
    private readonly HenyeyGreensteinPhase _hg2;
    private readonly float _w;

    public DoubleHenyeyGreensteinPhase(float g1, float g2, float w)
    {
        _hg1 = new HenyeyGreensteinPhase(g1);
        _hg2 = new HenyeyGreensteinPhase(g2);
        _w = MathF.Max(0f, MathF.Min(1f, w));
    }

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        return _w * _hg1.Evaluate(wo, wi) + (1f - _w) * _hg2.Evaluate(wo, wi);
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        // Stochastic lobe selection with probability _w: the resulting sample
        // density is exactly the mixture p(wi), so PDF = Evaluate(wo, wi).
        Vector3 wi = MathUtils.RandomFloat() < _w
            ? _hg1.Sample(wo).Wi
            : _hg2.Sample(wo).Wi;
        return (wi, Evaluate(wo, wi));
    }
}
