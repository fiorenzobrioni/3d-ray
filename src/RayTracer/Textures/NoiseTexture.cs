using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural noise texture with pro-grade controls.
///
/// <para>
/// Three noise families are exposed through <see cref="NoiseType"/>:
/// <list type="bullet">
///   <item><description><b>perlin</b> — smooth gradient noise, signed remap to [0,1].</description></item>
///   <item><description><b>fbm</b> — fractional Brownian motion (Arnold/Cycles/RenderMan style).</description></item>
///   <item><description><b>turbulence</b> — classic |Σ noise/2^i|, sharp & cloud-like.</description></item>
///   <item><description><b>ridged</b> — Musgrave ridged multifractal, sharp ridges (rocks, veins).</description></item>
///   <item><description><b>billow</b> — Σ|noise| octaves, puffy / clumpy.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="Octaves"/>, <see cref="Lacunarity"/> and <see cref="Gain"/>
/// configure the fractal sum. <see cref="Distortion"/> domain-warps the input
/// position with a secondary Perlin sample (Inigo Quilez style) for organic
/// shapes that real Perlin can't produce on its own. Backward-compat default:
/// when no new parameters are set and <c>noise_strength == 0</c>, output is
/// identical to the legacy implementation.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "noise"
///   noise_type: "fbm"        # perlin | fbm | turbulence | ridged | billow
///   scale: 5.0
///   octaves: 5               # 1..16 (default 5 for fbm/ridged/billow, 7 for legacy turbulence)
///   lacunarity: 2.0          # frequency multiplier between octaves
///   gain: 0.5                # amplitude decay between octaves
///   distortion: 0.0          # 0 = no warp, ~1 = strong organic warp
///   noise_strength: 0.0      # legacy: 0=smooth perlin, >0=turbulent (overridden by noise_type if set)
///   colors: [[0,0,0], [1,1,1]]
///   offset: [0,0,0]
///   rotation: [0,0,0]
///   randomize_offset: false
///   randomize_rotation: false
/// </code>
/// </summary>
public class NoiseTexture : ITexture
{
    public enum NoiseKind
    {
        /// <summary>Auto-select from legacy <c>noise_strength</c>: 0 → Perlin, &gt;0 → Turbulence.</summary>
        Auto,
        Perlin,
        Fbm,
        Turbulence,
        Ridged,
        Billow,
    }

    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly Vector3 _colorA;
    private readonly Vector3 _colorB;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    /// <summary>
    /// Legacy turbulence weight. Kept for backward compatibility with scenes
    /// written before the multi-mode upgrade. When <see cref="NoiseType"/> is
    /// left as <see cref="NoiseKind.Auto"/>, a positive value selects classic
    /// turbulence; otherwise it scales the chosen noise output.
    /// </summary>
    public float NoiseStrength { get; set; } = 0f;

    public NoiseKind NoiseType { get; set; } = NoiseKind.Auto;
    public int Octaves { get; set; } = 5;
    public float Lacunarity { get; set; } = 2f;
    public float Gain { get; set; } = 0.5f;

    /// <summary>
    /// Domain-warp amplitude. The input position is offset by a vector-valued
    /// Perlin sample scaled by this factor, producing organic, non-axis-aligned
    /// patterns (the Inigo Quilez technique used by Cycles' Noise Distortion).
    /// </summary>
    public float Distortion { get; set; } = 0f;

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the final scalar value
    /// <c>n ∈ [0, 1]</c> is looked up on the ramp instead of being linearly
    /// blended between the two constructor colours.
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    public NoiseTexture(float scale = 1f)
        : this(scale, Vector3.Zero, Vector3.One) { }

    public NoiseTexture(float scale, Vector3 colorA, Vector3 colorB)
    {
        _noise = new Perlin();
        _scale = scale;
        _colorA = colorA;
        _colorB = colorB;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
        => ValueCore(u, v, p, objectSeed, octaveOverride: -1);

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint)
    {
        if (!footprint.HasFootprint) return ValueCore(u, v, p, objectSeed, octaveOverride: -1);

        // ── Analytic octave clamp (Heidrich-Slusallek 1998 §4) ──────────────
        // Perlin noise at frequency f aliases when the footprint exceeds the
        // Nyquist period 1/(2f). After the texture's _scale and per-octave
        // lacunarity, octave i has frequency f_i = scale × lacunarity^i, so
        // its period in the same units as the footprint is 1/f_i. Drop any
        // octave whose period is below 2 × footprint extent — its energy
        // would alias into the visible image instead of contributing detail.
        //
        // The footprint is in the same space as p (object/LocalPoint after
        // Transform inverse-mapped the aux rays, or world for transform-free
        // primitives). The renderer's _scale multiplies p into "noise units"
        // identically to the noise sampler below.
        float footprintExtent = footprint.MaxWorldAxis();
        int octClamped = ComputeMaxOctaves(footprintExtent);

        return ValueCore(u, v, p, objectSeed, octaveOverride: octClamped);
    }

    /// <summary>
    /// Per-frequency Nyquist criterion: the smallest octave index whose
    /// period <c>1/(scale·lacunarity^i)</c> falls below twice the footprint
    /// extent is the first to alias and must be dropped.
    /// </summary>
    private int ComputeMaxOctaves(float footprintExtent)
    {
        if (footprintExtent <= 0f) return Octaves;
        float lac = Lacunarity > 1f ? Lacunarity : 2f;
        // Period of octave 0 in noise space (after _scale):
        //   T_0 = 1 / _scale   (footprint is in p-space, so multiply by _scale to compare)
        // Octave i aliases when:
        //   T_i = 1 / (_scale · lac^i) < 2 · footprintExtent
        //   ⟹ i > log_lac (1 / (2 · _scale · footprintExtent))
        float denom = 2f * MathF.Max(_scale, 1e-12f) * footprintExtent;
        if (denom <= 0f || float.IsInfinity(denom)) return 1;
        float maxOct = MathF.Log(1f / denom) / MathF.Log(lac);
        int oct = (int)MathF.Floor(maxOct) + 1; // include the last sub-Nyquist octave
        return Math.Clamp(oct, 1, Octaves);
    }

    private Vector3 ValueCore(float u, float v, Vector3 p, int objectSeed, int octaveOverride)
    {
        Vector3 transformedP = TextureTransform.Apply(
            p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        Vector3 q = _scale * transformedP;
        if (Distortion > 0f)
        {
            q += Distortion * noise.NoiseVector(q + new Vector3(13.7f, 91.3f, 27.1f));
        }

        NoiseKind kind = NoiseType;
        if (kind == NoiseKind.Auto)
        {
            kind = NoiseStrength > 0f ? NoiseKind.Turbulence : NoiseKind.Perlin;
        }

        int effOct = octaveOverride > 0 ? octaveOverride : Octaves;
        int legacyTurbulenceOct = octaveOverride > 0
            ? octaveOverride
            : (NoiseType == NoiseKind.Auto ? 7 : Octaves);

        float n = kind switch
        {
            NoiseKind.Perlin     => (noise.Noise(q) + 1f) * 0.5f,
            NoiseKind.Fbm        => noise.Fbm(q, effOct, Lacunarity, Gain),
            NoiseKind.Ridged     => noise.Ridged(q, effOct, Lacunarity, Gain),
            NoiseKind.Billow     => noise.Billow(q, effOct, Lacunarity, Gain),
            // Legacy turbulence keeps its historical 7-octave default when the
            // user hasn't explicitly overridden `octaves`, matching the old
            // visual output exactly.
            NoiseKind.Turbulence => noise.Turbulence(q, legacyTurbulenceOct),
            _                    => (noise.Noise(q) + 1f) * 0.5f,
        };

        // Apply legacy noise_strength as an amplitude multiplier for Auto-Turbulence,
        // preserving the pre-upgrade visual output bit-for-bit when neither
        // noise_type nor the new fractal params are set.
        if (NoiseType == NoiseKind.Auto && NoiseStrength > 0f)
        {
            n = Math.Clamp(n * NoiseStrength, 0f, 1f);
        }
        else
        {
            n = Math.Clamp(n, 0f, 1f);
        }

        return ColorRamp is { } ramp ? ramp.Sample(n) : Vector3.Lerp(_colorA, _colorB, n);
    }
}
