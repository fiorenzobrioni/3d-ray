using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Exponential height fog:
///
///   σ(y) = σ₀ · exp(−(y − y0) / H)
///
/// σ₀ at the reference altitude y0, falling off with the scale height H. The
/// optical depth along a ray has a closed form, and so has its inverse, so
/// distances are sampled exactly. The arithmetic is in double precision with
/// the series near a horizontal ray: the float form overflowed to ∞ · 0 far
/// below y0 (PIANO-REVIEW.md §3.59).
/// </summary>
public sealed class HeightFogMedium : AnalyticMedium
{
    private readonly Vector3 _sigmaA0, _sigmaS0, _sigmaT0;
    private readonly float _y0;
    private readonly double _invH;

    public HeightFogMedium(Vector3 sigmaA0, Vector3 sigmaS0, float y0, float scaleHeight, IPhaseFunction phase)
        : base(phase)
    {
        _sigmaA0 = Vector3.Max(sigmaA0, Vector3.Zero);
        _sigmaS0 = Vector3.Max(sigmaS0, Vector3.Zero);
        _sigmaT0 = _sigmaA0 + _sigmaS0;
        _y0 = y0;
        _invH = 1.0 / Math.Max(1e-4, scaleHeight);
    }

    // The density factor along the ray: f(s) = f0 · exp(−b s).
    private double F0(Ray ray) => Math.Exp(Math.Min(-(ray.Origin.Y - _y0) * _invH, 700.0));
    private double B(Ray ray) => ray.Direction.Y * _invH;

    /// <summary>∫₀ᵗ exp(−b s) ds = (1 − exp(−b t)) / b.</summary>
    private static double Integral(double b, double t)
    {
        double x = b * t;
        if (Math.Abs(x) < 1e-5) return t * (1.0 - 0.5 * x + x * x / 6.0);
        return -ExpM1(-x) / b;
    }

    private static double ExpM1(double x) => Math.Abs(x) < 1e-5 ? x + 0.5 * x * x : Math.Exp(Math.Min(x, 700.0)) - 1.0;

    public override Vector3 OpticalDepth(Ray ray, float t)
    {
        double k = F0(ray) * Integral(B(ray), t);
        return Scale(_sigmaT0, k);
    }

    public override Vector3 SigmaT(Ray ray, float t) => Scale(_sigmaT0, Density(ray, t));

    private double Density(Ray ray, float t)
        => Math.Exp(Math.Min(-(ray.Origin.Y + (double)t * ray.Direction.Y - _y0) * _invH, 700.0));

    protected override float DistanceAtDepth(Ray ray, int c, float tau, float tMax)
    {
        double s = MediumMath.Channel(_sigmaT0, c) * F0(ray);
        if (!(s > 0.0)) return float.PositiveInfinity;
        double g = tau / s;              // ∫₀ᵗ exp(−b s) ds must reach g
        double b = B(ray);
        double x = b * g;
        double t;
        if (Math.Abs(x) < 1e-5) t = g * (1.0 + 0.5 * x + x * x / 3.0);
        else if (x >= 1.0) return float.PositiveInfinity;   // the whole ray up holds less
        else t = -Math.Log(1.0 - x) / b;
        return t < tMax ? (float)t : float.PositiveInfinity;
    }

    public override Vector3 InScattering(Ray ray, float t, Vector3 wo, Vector3 wi)
        => Scale(_sigmaS0, Density(ray, t)) * Phase.Evaluate(wo, wi);

    protected override (Vector3 SigmaS, IPhaseFunction Phase) Scattering(Ray ray, float t, Vector3 throughput)
        => (Scale(_sigmaS0, Density(ray, t)), Phase);

    private static Vector3 Scale(Vector3 v, double k)
    {
        // σ = 0 stays 0 even where the density overflows.
        return new Vector3(
            v.X > 0f ? (float)(v.X * k) : 0f,
            v.Y > 0f ? (float)(v.Y * k) : 0f,
            v.Z > 0f ? (float)(v.Z * k) : 0f);
    }
}
