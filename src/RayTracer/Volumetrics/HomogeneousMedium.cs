using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

public sealed class HomogeneousMedium : IMedium
{
    private readonly Vector3 _sigmaA, _sigmaS, _sigmaT;
    public IPhaseFunction Phase { get; }

    public HomogeneousMedium(Vector3 sigmaA, Vector3 sigmaS, IPhaseFunction phase)
    { _sigmaA = sigmaA; _sigmaS = sigmaS; _sigmaT = sigmaA + sigmaS; Phase = phase; }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        float d = MathF.Min(tMax, 1e30f);
        return new Vector3(MathF.Exp(-_sigmaT.X * d), MathF.Exp(-_sigmaT.Y * d), MathF.Exp(-_sigmaT.Z * d));
    }

    public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
    {
        // Pick a channel uniformly for distance sampling
        int ch = MathF.Min(2f, MathUtils.RandomFloat() * 3f) is var f ? (int)f : 0;
        float sigmaT = ch == 0 ? _sigmaT.X : ch == 1 ? _sigmaT.Y : _sigmaT.Z;
        if (sigmaT <= 0f) { t = tMax; beta = Vector3.One; scattered = false; return false; }
        float dist = -MathF.Log(1f - MathUtils.RandomFloat()) / sigmaT;
        scattered = dist < tMax;
        t = scattered ? dist : tMax;
        Vector3 Tr = Transmittance(ray, t);
        Vector3 density = scattered ? _sigmaT * Tr : Tr;
        float pdf = (density.X + density.Y + density.Z) / 3f;
        if (pdf < 1e-20f) pdf = 1f;
        beta = scattered ? (Tr * _sigmaS / pdf) : (Tr / pdf);
        return scattered;
    }
}
