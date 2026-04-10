using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

public sealed class HomogeneousMedium : IMedium
{
    private readonly Vector3 _sigmaA;
    private readonly Vector3 _sigmaS;
    private readonly Vector3 _sigmaT;

    public IPhaseFunction Phase { get; }

    public HomogeneousMedium(Vector3 sigmaA, Vector3 sigmaS, IPhaseFunction phase)
    {
        // Non-negative coefficients are a hard physical requirement: a negative σ
        // would turn Beer-Lambert into exp(+…) and blow up the radiance estimate.
        _sigmaA = Vector3.Max(sigmaA, Vector3.Zero);
        _sigmaS = Vector3.Max(sigmaS, Vector3.Zero);
        _sigmaT = _sigmaA + _sigmaS;
        Phase = phase;
    }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        // Clamp to a sane non-negative distance; tMax can be +∞ for sky rays.
        float d = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        return new Vector3(
            MathF.Exp(-_sigmaT.X * d),
            MathF.Exp(-_sigmaT.Y * d),
            MathF.Exp(-_sigmaT.Z * d));
    }

    public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
    {
        // Spectral free-path sampling with uniform channel selection (PBRT §15.2).
        // Picks one of the three σ_T channels uniformly to drive the exponential
        // distance sample, then weights with a MIS-style average pdf so the
        // estimator remains unbiased across channels.
        int ch = (int)(MathUtils.RandomFloat() * 3f);
        if (ch > 2) ch = 2; // guard against RandomFloat() returning exactly 1.0f
        float sigmaT = ch == 0 ? _sigmaT.X : ch == 1 ? _sigmaT.Y : _sigmaT.Z;

        if (sigmaT <= 0f)
        {
            // Vacuum on the chosen channel → no scatter event possible.
            t = tMax;
            beta = Vector3.One;
            scattered = false;
            return false;
        }

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
