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
/// <b>Studio-quality secondary wave.</b> When <see cref="SecondaryStrength"/>
/// &gt; 0 a second sinusoid along <see cref="SecondaryAxis"/> is added to the
/// vein term: <c>sin(wave1) + strength · sin(wave2)</c>, renormalised. This
/// breaks the rigid unidirectionality of single-axis marble and produces
/// the cross-veining of Statuario, Calacatta and Arabescato — slabs where
/// veins run along two non-parallel directions. Default secondary axis is
/// orthogonalised against the primary axis so artists can set just the
/// strength and get a sane visual immediately.
/// </para>
///
/// <para>
/// Backward-compat default: <c>vein_axis = Z</c>, <c>vein_frequency = 1</c>,
/// <c>vein_sharpness = 1</c>, <c>octaves = 7</c> with classic
/// <c>turbulence</c> and <c>secondary_strength = 0</c> reproduces the
/// legacy implementation exactly.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "marble"
///   scale: 4.0
///   noise_strength: 10.0
///   colors: [[0.9,0.9,0.9], [0.1,0.1,0.1]]
///   vein_axis: [0, 0, 1]      # primary vein propagation direction
///   vein_frequency: 1.0        # multiplier on the sine term frequency
///   vein_sharpness: 1.0        # 1=soft (default), 2-8=thin sharp veins
///   octaves: 7                 # fBm octaves used by the turbulence term
///   lacunarity: 2.0
///   gain: 0.5
///   distortion: 0.0            # domain warp amplitude
///   noise_type: "turbulence"   # turbulence | fbm | ridged
///   secondary_wave:            # optional — Statuario / Calacatta cross-veins
///     axis: [1, 0, 0]          # second vein direction (auto-orthogonalised)
///     frequency: 0.7           # independent of primary frequency
///     strength: 0.5            # 0 = disabled (back-compat), ≤1 typical
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
    /// Optional second vein direction. When <see cref="SecondaryStrength"/>
    /// is 0 (default) the secondary wave is disabled and the texture is
    /// byte-identical to the single-axis legacy output. When &gt; 0 the
    /// secondary sine is added to the primary sine before sharpening,
    /// unlocking Statuario / Calacatta / Arabescato cross-veining looks.
    /// The axis is internally projected against the primary axis so the
    /// effective second direction is always at least partly perpendicular
    /// — picking an axis collinear with the primary still yields a useful
    /// off-axis component.
    /// </summary>
    public Vector3 SecondaryAxis { get; set; } = Vector3.UnitX;

    /// <summary>
    /// Frequency multiplier on the secondary sine term — independent from
    /// <see cref="VeinFrequency"/>. Setting it to a non-integer ratio of the
    /// primary frequency (e.g. 0.7, 1.3) produces aperiodic moiré-free
    /// secondary veining; setting them equal gives a regular cross hatch.
    /// </summary>
    public float SecondaryFrequency { get; set; } = 1f;

    /// <summary>
    /// Amplitude weight of the secondary sine in the combined vein term.
    /// 0 (default) ⇒ disabled. Typical values are 0.3–0.7; the combined
    /// signal is renormalised by <c>(1 + strength)</c> so the sine output
    /// stays in [-1, 1] and the sharpening curve is unaffected.
    /// </summary>
    public float SecondaryStrength { get; set; } = 0f;

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
        float wave1 = MathF.Sin(_scale * VeinFrequency * along + NoiseStrength * fractal);

        float sinVal;
        if (SecondaryStrength > 0f)
        {
            // Studio-quality cross-veining: a second sine along an axis
            // orthogonalised against the primary, so even if the user sets a
            // collinear axis they still get a perpendicular component (matches
            // how Arnold's `marble2` and RM PxrMarble blend layered slabs).
            Vector3 secAxisRaw = SecondaryAxis.LengthSquared() > 1e-12f
                ? Vector3.Normalize(SecondaryAxis) : Vector3.UnitX;
            Vector3 secAxisOrtho = secAxisRaw - Vector3.Dot(secAxisRaw, axis) * axis;
            // Fallback when the user picked an axis perfectly parallel to the
            // primary one — pick any vector in the perpendicular plane.
            if (secAxisOrtho.LengthSquared() < 1e-8f)
            {
                Vector3 helper = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
                secAxisOrtho = helper - Vector3.Dot(helper, axis) * axis;
            }
            secAxisOrtho = Vector3.Normalize(secAxisOrtho);
            float along2 = Vector3.Dot(q, secAxisOrtho);
            float wave2 = MathF.Sin(_scale * SecondaryFrequency * along2 + NoiseStrength * fractal);
            // Renormalise (1 + strength) so the combined signal stays in [-1, 1].
            sinVal = (wave1 + SecondaryStrength * wave2) / (1f + SecondaryStrength);
        }
        else
        {
            sinVal = wave1;
        }
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
