using System.Numerics;

namespace RayTracer.Volumetrics;

/// <summary>
/// Two-lobe Henyey-Greenstein phase function for clouds (a strong forward
/// lobe and a softer back lobe):
///
///   p(θ) = w · HG(g₁, θ) + (1 - w) · HG(g₂, θ),   w ∈ [0, 1]
///
/// Typical cumulus: g₁ ≈ 0.85, g₂ ≈ -0.3, w ≈ 0.5. The lobe is picked with the
/// first coordinate of the pair, rescaled, so the pair stays stratified and
/// the density sampled is the mixture.
/// </summary>
public sealed class DoubleHenyeyGreensteinPhase : IPhaseFunction
{
    private readonly float _g1, _g2, _w;

    public DoubleHenyeyGreensteinPhase(float g1, float g2, float w)
    {
        _g1 = Math.Clamp(g1, -0.999f, 0.999f);
        _g2 = Math.Clamp(g2, -0.999f, 0.999f);
        _w = Math.Clamp(w, 0f, 1f);
    }

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        float mu = Vector3.Dot(wo, wi);
        return _w * HenyeyGreensteinPhase.Value(_g1, mu) + (1f - _w) * HenyeyGreensteinPhase.Value(_g2, mu);
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo, Vector2 u)
    {
        float g, x;
        if (u.X < _w) { g = _g1; x = u.X / _w; }
        else { g = _g2; x = (u.X - _w) / (1f - _w); }
        x = MathF.Min(x, 0.99999994f);
        Vector3 wi = PhaseFrame.Around(wo, HenyeyGreensteinPhase.SampleCos(g, x), 2f * MathF.PI * u.Y);
        return (wi, Evaluate(wo, wi));
    }
}
