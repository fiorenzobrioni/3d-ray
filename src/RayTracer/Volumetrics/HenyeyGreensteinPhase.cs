using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Henyey-Greenstein phase function.
///
/// Convention (shared with <see cref="IsotropicPhase"/> and the renderer):
///   <c>wo</c> is the direction of the ray travelling INTO the scattering event
///   (i.e. <c>ray.Direction</c>), and <c>wi</c> is the scattered direction.
///   With this convention, forward scattering (<c>g &gt; 0</c>) peaks at
///   <c>wi = wo</c> — i.e. the ray keeps going mostly forward.
///
/// Both <see cref="Evaluate"/> and <see cref="Sample"/> are self-consistent:
///   the sampled density matches <see cref="Evaluate"/>, so <c>phase / pdf = 1</c>.
/// </summary>
public sealed class HenyeyGreensteinPhase : IPhaseFunction
{
    private readonly float _g;
    public HenyeyGreensteinPhase(float g) => _g = MathF.Max(-0.999f, MathF.Min(0.999f, g));

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        // μ = cos(scatter angle) between the incoming travel direction and wi.
        // Forward scatter (wi ∥ wo) ⇒ μ = 1 ⇒ denom = (1-g)² ⇒ peak for g > 0.
        float mu = Vector3.Dot(wo, wi);
        float denom = 1f + _g * _g - 2f * _g * mu;
        return MathUtils.Inv4Pi * (1f - _g * _g) / (denom * MathF.Sqrt(denom));
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        float u1 = MathUtils.RandomFloat(), u2 = MathUtils.RandomFloat();
        // Inverse-CDF sampling of HG on μ ∈ [-1, 1]. u1 = 1 ⇒ μ = 1 (forward peak).
        float cosTheta;
        if (MathF.Abs(_g) < 1e-3f) cosTheta = 1f - 2f * u1;
        else
        {
            float sqr = (1f - _g * _g) / (1f - _g + 2f * _g * u1);
            cosTheta = (1f + _g * _g - sqr * sqr) / (2f * _g);
        }
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;

        // Build an ONB around wo so that cosTheta = 1 ⇒ wi = wo (forward scatter).
        Vector3 w = wo;
        Vector3 a = MathF.Abs(w.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v = Vector3.Normalize(Vector3.Cross(w, a));
        Vector3 u = Vector3.Cross(w, v);
        Vector3 wi = sinTheta * MathF.Cos(phi) * u + sinTheta * MathF.Sin(phi) * v + cosTheta * w;
        wi = Vector3.Normalize(wi);
        return (wi, Evaluate(wo, wi));
    }
}
