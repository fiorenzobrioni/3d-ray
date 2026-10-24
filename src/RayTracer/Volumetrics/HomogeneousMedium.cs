using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>A medium with the same coefficients everywhere: Tr = exp(−σ_t d).</summary>
public sealed class HomogeneousMedium : AnalyticMedium
{
    /// <summary>Absorption coefficient σ_a per RGB channel.</summary>
    public Vector3 SigmaA { get; }

    /// <summary>Scattering coefficient σ_s per RGB channel. The random-walk
    /// SSS integrator activates only when at least one channel is &gt; 0.</summary>
    public Vector3 SigmaS { get; }

    /// <summary>Extinction σ_t = σ_a + σ_s.</summary>
    public Vector3 SigmaTotal { get; }

    public HomogeneousMedium(Vector3 sigmaA, Vector3 sigmaS, IPhaseFunction phase) : base(phase)
    {
        // A negative σ would turn Beer-Lambert into exp(+…).
        SigmaA = Vector3.Max(sigmaA, Vector3.Zero);
        SigmaS = Vector3.Max(sigmaS, Vector3.Zero);
        SigmaTotal = SigmaA + SigmaS;
    }

    public override Vector3 OpticalDepth(Ray ray, float t) => SigmaTotal * t;

    public override Vector3 SigmaT(Ray ray, float t) => SigmaTotal;

    protected override float DistanceAtDepth(Ray ray, int c, float tau, float tMax)
    {
        float s = MediumMath.Channel(SigmaTotal, c);
        return s > 0f ? tau / s : float.PositiveInfinity;
    }

    public override Vector3 InScattering(Ray ray, float t, Vector3 wo, Vector3 wi) => SigmaS * Phase.Evaluate(wo, wi);

    protected override (Vector3 SigmaS, IPhaseFunction Phase) Scattering(Ray ray, float t, Vector3 throughput)
        => (SigmaS, Phase);
}
