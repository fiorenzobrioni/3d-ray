using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// Heterogeneous participating medium with density driven by 3D Perlin fBm —
/// conceptually analogous to Arnold <c>standard_volume</c> with a procedural
/// density input or RenderMan <c>PxrVolume</c> procedural noise mode.
///
///   d(p) ∈ [0, 1]  = fBm(p · frequency, octaves)
///   σ_T(p) = σ_base_T · d(p),   σ_S(p) = σ_base_S · d(p)
///
/// Because σ is no longer constant along the ray, free-path sampling needs
/// **delta tracking** (Woodcock) — a rejection algorithm that steps with a
/// majorant σ_maj bounding σ_T from above, then accepts a step as a real
/// scatter with probability σ_T(p)/σ_maj (otherwise it is a null event and
/// the walk continues). Transmittance uses **ratio tracking** on the same
/// majorant.
/// </summary>
public sealed class HeterogeneousProceduralMedium : IMedium
{
    private readonly Vector3 _sigmaBaseA;
    private readonly Vector3 _sigmaBaseS;
    private readonly Vector3 _sigmaBaseT;
    private readonly float _frequency;
    private readonly int _octaves;
    private readonly float _lacunarity;
    private readonly float _gain;
    private readonly PerlinNoise _noise;

    // Safety cap on null-event iterations. With a healthy majorant the
    // expected count is σ_maj·tMax, so for fog-like σ (≤ ~1) and scenes
    // of ~100 world units this is orders of magnitude below the cap.
    private const int MaxIterations = 4096;

    public IPhaseFunction Phase { get; }

    public HeterogeneousProceduralMedium(
        Vector3 sigmaBaseA, Vector3 sigmaBaseS,
        float frequency, int octaves, float lacunarity, float gain,
        int seed, IPhaseFunction phase)
    {
        _sigmaBaseA = Vector3.Max(sigmaBaseA, Vector3.Zero);
        _sigmaBaseS = Vector3.Max(sigmaBaseS, Vector3.Zero);
        _sigmaBaseT = _sigmaBaseA + _sigmaBaseS;
        _frequency = MathF.Max(1e-6f, frequency);
        _octaves = Math.Clamp(octaves, 1, 8);
        _lacunarity = MathF.Max(1f, lacunarity);
        _gain = MathF.Max(0.01f, MathF.Min(0.99f, gain));
        _noise = new PerlinNoise(seed);
        Phase = phase;
    }

    /// <summary>
    /// Scalar density in [0, 1] at point p. By construction
    /// <c>σ_T(p) = σ_base_T · Density(p) ≤ σ_base_T</c>, so σ_base_T is a
    /// valid (and tight) majorant.
    /// </summary>
    private float Density(Vector3 p)
    {
        float d = _noise.FbmUnit(p * _frequency, _octaves, _lacunarity, _gain);
        if (d < 0f) d = 0f;
        if (d > 1f) d = 1f;
        return d;
    }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        float tEnd = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        float sigmaMaj = MathF.Max(MathF.Max(_sigmaBaseT.X, _sigmaBaseT.Y), _sigmaBaseT.Z);
        if (sigmaMaj <= 0f) return Vector3.One;

        // Ratio tracking estimator for Tr: Tr ← Tr · (1 - σ_T(p)/σ_maj) at each
        // proposed free-flight point. Unbiased and bounded (product of values in [0,1]).
        Vector3 Tr = Vector3.One;
        float t = 0f;
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            float dt = -MathF.Log(1f - MathUtils.RandomFloat()) / sigmaMaj;
            t += dt;
            if (t >= tEnd) break;

            Vector3 p = ray.Origin + ray.Direction * t;
            float d = Density(p);
            Vector3 sigmaT = _sigmaBaseT * d;

            Tr *= Vector3.One - sigmaT / sigmaMaj;

            if (Tr.X < 1e-5f && Tr.Y < 1e-5f && Tr.Z < 1e-5f) break;
        }
        return Tr;
    }

    public bool Sample(Ray ray, float tMax, out float t, out Vector3 beta, out bool scattered)
    {
        // Channel-selected delta tracking, consistent with HomogeneousMedium.
        int ch = (int)(MathUtils.RandomFloat() * 3f);
        if (ch > 2) ch = 2;
        float sigmaMajCh = ch == 0 ? _sigmaBaseT.X : ch == 1 ? _sigmaBaseT.Y : _sigmaBaseT.Z;

        if (sigmaMajCh <= 0f)
        {
            t = tMax;
            beta = Vector3.One;
            scattered = false;
            return false;
        }

        float tCur = 0f;
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            float dt = -MathF.Log(1f - MathUtils.RandomFloat()) / sigmaMajCh;
            tCur += dt;
            if (tCur >= tMax) break;

            Vector3 p = ray.Origin + ray.Direction * tCur;
            float d = Density(p);
            float sigmaTch = sigmaMajCh * d;

            if (MathUtils.RandomFloat() * sigmaMajCh < sigmaTch)
            {
                // Real scatter event. Analog estimator: no explicit Tr factor
                // (null events have already "absorbed" it), so beta reduces to
                // σ_s/σ_T on the chosen channel — applied as a Vector3 for
                // colored media.
                t = tCur;
                scattered = true;
                Vector3 sigmaS = _sigmaBaseS * d;
                float denom = MathF.Max(sigmaTch, 1e-20f);
                beta = sigmaS / denom;
                return true;
            }
            // Null event → continue.
        }

        // Reached tMax (or the safety cap) with no real event.
        t = tMax;
        beta = Vector3.One;
        scattered = false;
        return false;
    }
}
