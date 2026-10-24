using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

public sealed class IsotropicPhase : IPhaseFunction
{
    public float Evaluate(Vector3 wo, Vector3 wi) => MathUtils.Inv4Pi;

    public (Vector3 Wi, float Pdf) Sample(Vector3 wo, Vector2 u)
        => (PhaseFrame.Around(wo, 2f * u.X - 1f, 2f * MathF.PI * u.Y), MathUtils.Inv4Pi);
}
