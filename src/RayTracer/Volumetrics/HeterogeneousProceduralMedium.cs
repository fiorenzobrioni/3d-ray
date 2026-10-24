using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Heterogeneous medium with density from 3D Perlin fBm:
///
///   d(p) ∈ [0, 1]  = fBm(p · frequency, octaves)
///   σ_t(p) = σ_base_t · d(p),   σ_s(p) = σ_base_s · d(p)
///
/// Tracked with null collisions (<see cref="TrackedMedium"/>) against the
/// majorant max(σ_base_t), tight since d reaches 1.
/// </summary>
public sealed class HeterogeneousProceduralMedium : TrackedMedium
{
    private readonly Vector3 _sigmaBaseA;
    private readonly Vector3 _sigmaBaseS;
    private readonly float _majorant;
    private readonly float _frequency;
    private readonly int _octaves;
    private readonly float _lacunarity;
    private readonly float _gain;
    private readonly PerlinNoise _noise;

    public HeterogeneousProceduralMedium(
        Vector3 sigmaBaseA, Vector3 sigmaBaseS,
        float frequency, int octaves, float lacunarity, float gain,
        int seed, IPhaseFunction phase) : base(phase)
    {
        _sigmaBaseA = Vector3.Max(sigmaBaseA, Vector3.Zero);
        _sigmaBaseS = Vector3.Max(sigmaBaseS, Vector3.Zero);
        _majorant = MediumMath.Max(_sigmaBaseA + _sigmaBaseS);
        _frequency = MathF.Max(1e-6f, frequency);
        _octaves = Math.Clamp(octaves, 1, 8);
        _lacunarity = MathF.Max(1f, lacunarity);
        _gain = MathF.Max(0.01f, MathF.Min(0.99f, gain));
        _noise = new PerlinNoise(seed);
    }

    /// <summary>Density in [0, 1] at p.</summary>
    private float Density(Vector3 p)
        => Math.Clamp(_noise.FbmUnit(p * _frequency, _octaves, _lacunarity, _gain), 0f, 1f);

    protected override void Coefficients(Vector3 p, out Vector3 sigmaA, out Vector3 sigmaS)
    {
        float d = Density(p);
        sigmaA = _sigmaBaseA * d;
        sigmaS = _sigmaBaseS * d;
    }

    protected override MajorantWalk BeginMajorants(Ray ray, float tMax)
        => new() { T = 0f, TEnd = tMax, Done = !(_majorant > 0f) };

    protected override bool NextMajorant(Ray ray, ref MajorantWalk walk, out MajorantSegment segment)
    {
        segment = new MajorantSegment { T0 = walk.T, T1 = walk.TEnd, Sigma = _majorant };
        if (walk.Done) return false;
        walk.Done = true;
        return true;
    }
}
