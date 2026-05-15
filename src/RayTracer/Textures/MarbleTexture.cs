using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural marble — directional veins modulated by fractal turbulence.
///
/// <para>
/// Algorithm: <c>vein(p) = sin(scale · (p · axis) · vein_freq + strength · fBm(p))</c>.
/// The result is sharpened by <see cref="VeinSharpness"/> (raised to that power
/// after normalisation) to produce thin, high-contrast veins like real Carrara
/// marble, matching the look of Arnold's <c>marble</c> and RenderMan's
/// <c>PxrMarble</c>.
/// </para>
///
/// <para>
/// Backward-compat default: <c>vein_axis = Z</c>, <c>vein_frequency = 1</c>,
/// <c>vein_sharpness = 1</c>, <c>octaves = 7</c> with classic
/// <c>turbulence</c> reproduces the legacy implementation exactly.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "marble"
///   scale: 4.0
///   noise_strength: 10.0
///   colors: [[0.9,0.9,0.9], [0.1,0.1,0.1]]
///   vein_axis: [0, 0, 1]      # vein propagation direction
///   vein_frequency: 1.0        # multiplier on the sine term frequency
///   vein_sharpness: 1.0        # 1=soft (default), 2-8=thin sharp veins
///   octaves: 7                 # fBm octaves used by the turbulence term
///   lacunarity: 2.0
///   gain: 0.5
///   distortion: 0.0            # domain warp amplitude
///   noise_type: "turbulence"   # turbulence | fbm | ridged
/// </code>
/// </summary>
public class MarbleTexture : ITexture
{
    public enum FractalKind { Turbulence, Fbm, Ridged }

    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly ITexture _baseColor;
    private readonly ITexture _veinColor;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }
    public float NoiseStrength { get; set; } = 10f;

    public Vector3 VeinAxis { get; set; } = Vector3.UnitZ;
    public float VeinFrequency { get; set; } = 1f;
    public float VeinSharpness { get; set; } = 1f;
    public int Octaves { get; set; } = 7;
    public float Lacunarity { get; set; } = 2f;
    public float Gain { get; set; } = 0.5f;
    public float Distortion { get; set; } = 0f;
    public FractalKind NoiseType { get; set; } = FractalKind.Turbulence;

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the sine-wave vein parameter
    /// <c>t ∈ [0, 1]</c> is looked up on the ramp instead of being linearly
    /// blended between vein and base colours — unlocks Statuario / Calacatta
    /// looks with 3+ tonal layers (vein → mid → base → undertone).
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    public MarbleTexture(float scale = 4f)
        : this(scale, new Vector3(0.9f), new Vector3(0.1f)) { }

    public MarbleTexture(float scale, Vector3 baseColor, Vector3 veinColor)
    {
        _noise = new Perlin();
        _scale = scale;
        _baseColor = new SolidColor(baseColor);
        _veinColor = new SolidColor(veinColor);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);
        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        Vector3 q = transformedP;
        if (Distortion > 0f)
        {
            q += Distortion * noise.NoiseVector(q + new Vector3(5.2f, 1.3f, 8.7f));
        }

        float fractal = NoiseType switch
        {
            FractalKind.Fbm    => noise.Fbm(q, Octaves, Lacunarity, Gain, signed: true),
            FractalKind.Ridged => noise.Ridged(q, Octaves, Lacunarity, Gain),
            _                  => noise.Turbulence(q, Octaves),
        };

        // Vein term: sine of (axis-projection × scale × vein_freq) + strength·fBm
        Vector3 axis = VeinAxis.LengthSquared() > 1e-12f ? Vector3.Normalize(VeinAxis) : Vector3.UnitZ;
        float along = Vector3.Dot(q, axis);
        float sinVal = MathF.Sin(_scale * VeinFrequency * along + NoiseStrength * fractal);
        float t = (sinVal + 1f) * 0.5f;

        if (VeinSharpness != 1f && VeinSharpness > 0f)
        {
            // sharpening: t^k pulls the gradient toward the vein color, producing
            // thin high-contrast veins as k grows (k = 4 ≈ Carrara marble).
            t = MathF.Pow(t, VeinSharpness);
        }

        if (ColorRamp is { } ramp)
        {
            // Ramp drives the colour directly: t = 0 → first stop (typically
            // the vein), t = 1 → last stop (typically the base). The two
            // constructor colours are ignored when a ramp is present.
            return ramp.Sample(t);
        }

        Vector3 cBase = _baseColor.Value(u, v, transformedP, objectSeed);
        Vector3 cVein = _veinColor.Value(u, v, transformedP, objectSeed);
        return Vector3.Lerp(cVein, cBase, t);
    }
}
