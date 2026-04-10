using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

public sealed class IsotropicPhase : IPhaseFunction
{
    private const float Inv4Pi = 0.0795774715f;
    public float Evaluate(Vector3 wo, Vector3 wi) => Inv4Pi;
    public (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        float u1 = MathUtils.RandomFloat(), u2 = MathUtils.RandomFloat();
        float z = 1f - 2f * u1;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        float phi = 2f * MathF.PI * u2;
        return (new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z), Inv4Pi);
    }
}
