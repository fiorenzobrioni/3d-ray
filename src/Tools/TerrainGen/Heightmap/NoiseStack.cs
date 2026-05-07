using System;
using System.Numerics;

namespace TerrainGen.Heightmap;

/// <summary>
/// Composes Perlin noise into terrain-shaped height fields with domain warping
/// and optional ridged stacks. Parameters are tuned per terrain type.
/// </summary>
public sealed class NoiseStack
{
    private readonly PerlinNoise _base;
    private readonly PerlinNoise _ridge;
    private readonly PerlinNoise _warp;

    public int Octaves { get; init; } = 6;
    public float Lacunarity { get; init; } = 2.1f;
    public float Gain { get; init; } = 0.5f;
    public float Frequency { get; init; } = 1.0f;
    public float RidgeMix { get; init; } = 0f;       // 0 = pure fBm, 1 = pure ridged
    public float WarpStrength { get; init; } = 0.6f; // amplitude of domain warp
    public float WarpFrequency { get; init; } = 0.5f;
    public float AmplitudeShape { get; init; } = 1.0f; // pow(n, shape) — <1 raises peaks, >1 flattens

    public NoiseStack(int seed)
    {
        _base  = new PerlinNoise(seed);
        _ridge = new PerlinNoise(seed ^ 0x5BD1E995);
        _warp  = new PerlinNoise(seed ^ 0x27D4EB2F);
    }

    /// <summary>Sample height in [0,1] at world-space (x,z). x,z in arbitrary units.</summary>
    public float Sample(float x, float z)
    {
        // Domain warping (IQ-style) gives meandering features non-aligned to grid.
        float wx = x + WarpStrength * _warp.Noise(new Vector3(x * WarpFrequency, 0f, z * WarpFrequency));
        float wz = z + WarpStrength * _warp.Noise(new Vector3(x * WarpFrequency + 17.3f, 0f, z * WarpFrequency - 9.2f));

        var p = new Vector3(wx * Frequency, 0f, wz * Frequency);

        float fbm = _base.FbmUnit(p, Octaves, Lacunarity, Gain);
        if (RidgeMix > 0f)
        {
            float ridge = _ridge.Ridged(p, Octaves, Lacunarity, Gain);
            fbm = (1f - RidgeMix) * fbm + RidgeMix * ridge;
        }

        // Amplitude shaping: pow(n, s). s<1 -> exalt peaks, s>1 -> flatten.
        if (Math.Abs(AmplitudeShape - 1f) > 1e-4f)
            fbm = MathF.Pow(Math.Clamp(fbm, 0f, 1f), AmplitudeShape);

        return fbm;
    }

    public static NoiseStack ForType(TerrainType type, int seed)
    {
        return type switch
        {
            TerrainType.Pianura => new NoiseStack(seed)
            {
                Octaves = 4,
                Lacunarity = 2.0f,
                Gain = 0.45f,
                Frequency = 0.012f,
                RidgeMix = 0f,
                WarpStrength = 8f,
                WarpFrequency = 0.008f,
                AmplitudeShape = 2.0f,   // flatten — pianure piatte
            },
            TerrainType.Collina => new NoiseStack(seed)
            {
                Octaves = 6,
                Lacunarity = 2.1f,
                Gain = 0.50f,
                Frequency = 0.015f,
                RidgeMix = 0f,
                WarpStrength = 12f,
                WarpFrequency = 0.012f,
                AmplitudeShape = 1.0f,   // linear
            },
            TerrainType.Montagna => new NoiseStack(seed)
            {
                Octaves = 8,
                Lacunarity = 2.3f,
                Gain = 0.55f,
                Frequency = 0.020f,
                RidgeMix = 0.55f,        // strong ridges
                WarpStrength = 16f,
                WarpFrequency = 0.014f,
                AmplitudeShape = 0.7f,   // exalt peaks
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
