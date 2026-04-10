using System.Numerics;

namespace RayTracer.Volumetrics;

public interface IPhaseFunction
{
    float Evaluate(Vector3 wo, Vector3 wi);
    (Vector3 Wi, float Pdf) Sample(Vector3 wo);
}
