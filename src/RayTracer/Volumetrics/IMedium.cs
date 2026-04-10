using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

public interface IMedium
{
    /// <summary>Beer-Lambert transmittance along ray for distance tMax.</summary>
    Vector3 Transmittance(Ray ray, float tMax);
    /// <summary>Free-path sample. If a medium scatter happens before tMax,
    /// returns scattered=true with the event position and Tr/pdf weight in beta.</summary>
    bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered);
    IPhaseFunction Phase { get; }
}
