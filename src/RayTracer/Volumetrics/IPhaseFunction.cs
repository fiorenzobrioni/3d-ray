using System.Numerics;

namespace RayTracer.Volumetrics;

public interface IPhaseFunction
{
    float Evaluate(Vector3 wo, Vector3 wi);
    (Vector3 Wi, float Pdf) Sample(Vector3 wo);

    // For all phase functions in 3D-Ray the sampling distribution matches
    // Evaluate() exactly (Sample returns Evaluate(wo,wi) as Pdf), so the
    // default implementation re-uses Evaluate. Implementations whose
    // sampler diverges from the analytic phase must override.
    float Pdf(Vector3 wo, Vector3 wi) => Evaluate(wo, wi);
}
