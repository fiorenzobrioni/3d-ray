using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

public sealed class HenyeyGreensteinPhase : IPhaseFunction
{
    private const float Inv4Pi = 0.0795774715f;
    private readonly float _g;
    public HenyeyGreensteinPhase(float g) => _g = MathF.Max(-0.999f, MathF.Min(0.999f, g));

    public float Evaluate(Vector3 wo, Vector3 wi)
    {
        float cosTheta = Vector3.Dot(-wo, wi);
        float denom = 1f + _g * _g + 2f * _g * cosTheta;
        return Inv4Pi * (1f - _g * _g) / (denom * MathF.Sqrt(denom));
    }

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        float u1 = MathUtils.RandomFloat(), u2 = MathUtils.RandomFloat();
        float cosTheta;
        if (MathF.Abs(_g) < 1e-3f) cosTheta = 1f - 2f * u1;
        else
        {
            float sqr = (1f - _g * _g) / (1f - _g + 2f * _g * u1);
            cosTheta = (1f + _g * _g - sqr * sqr) / (2f * _g);
        }
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;
        // Build basis around -wo (forward direction of incoming ray)
        Vector3 w = -wo;
        Vector3 a = MathF.Abs(w.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v = Vector3.Normalize(Vector3.Cross(w, a));
        Vector3 u = Vector3.Cross(w, v);
        Vector3 wi = sinTheta * MathF.Cos(phi) * u + sinTheta * MathF.Sin(phi) * v + cosTheta * w;
        return (Vector3.Normalize(wi), Evaluate(wo, wi));
    }
}
