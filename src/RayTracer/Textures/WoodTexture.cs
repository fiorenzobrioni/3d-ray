using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural wood — concentric annual rings perturbed by fractal noise grain.
///
/// <para>
/// The ring radius is measured perpendicular to <see cref="RingAxis"/>: the
/// rings form circles in the plane orthogonal to the axis, so a tree trunk
/// uses <c>ring_axis: [0, 1, 0]</c> (rings appear on cross-cut), a plank uses
/// <c>ring_axis: [0, 0, 1]</c>, etc. Default Y matches RenderMan/Arnold's
/// default wood orientation.
/// </para>
///
/// <para>
/// Pro features over the legacy implementation:
/// <list type="bullet">
///   <item><description>fBm grain (configurable octaves/lacunarity/gain), not single-octave Perlin.</description></item>
///   <item><description>Configurable ring axis (any direction, not hard-coded XZ).</description></item>
///   <item><description><c>RingSharpness</c>: latewood/earlywood transition exponent — 1 = soft, ≥3 = hard ring lines.</description></item>
///   <item><description>Optional axial grain via <c>AxialGrain</c> — long-wave noise along the trunk axis.</description></item>
///   <item><description>Domain warp via <c>Distortion</c> for knots / waved figure.</description></item>
/// </list>
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "wood"
///   scale: 4.0
///   noise_strength: 2.0
///   colors: [[0.85,0.65,0.40], [0.60,0.40,0.20]]
///   ring_axis: [0, 1, 0]       # axis of the trunk; rings ⊥ axis
///   ring_sharpness: 2.0        # 1=soft (legacy), 3-6=defined latewood
///   axial_grain: 0.0           # long-wave noise along the axis
///   octaves: 4                 # fBm octaves on the grain
///   lacunarity: 2.0
///   gain: 0.5
///   distortion: 0.0            # 0=clean rings, ~0.5=knots/waves
/// </code>
/// </summary>
public class WoodTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly ITexture _lightWoodColor;
    private readonly ITexture _darkWoodColor;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }
    public float NoiseStrength { get; set; } = 2f;

    public Vector3 RingAxis { get; set; } = Vector3.UnitY;
    public float RingSharpness { get; set; } = 1f;
    public float AxialGrain { get; set; } = 0f;
    public int Octaves { get; set; } = 1;
    public float Lacunarity { get; set; } = 2f;
    public float Gain { get; set; } = 0.5f;
    public float Distortion { get; set; } = 0f;

    public WoodTexture(float scale = 4f, float turbulenceStrength = 2f)
        : this(scale, turbulenceStrength,
               new Vector3(0.85f, 0.65f, 0.40f),
               new Vector3(0.60f, 0.40f, 0.20f)) { }

    public WoodTexture(float scale, float turbulenceStrength, Vector3 lightColor, Vector3 darkColor)
    {
        _noise = new Perlin();
        _scale = scale;
        NoiseStrength = turbulenceStrength;
        _lightWoodColor = new SolidColor(lightColor);
        _darkWoodColor = new SolidColor(darkColor);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);
        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        Vector3 q = transformedP;
        if (Distortion > 0f)
        {
            q += Distortion * noise.NoiseVector(q + new Vector3(3.1f, 7.7f, 1.9f));
        }

        // Distance from the ring axis (radial coordinate in the plane ⊥ axis).
        Vector3 axis = RingAxis.LengthSquared() > 1e-12f ? Vector3.Normalize(RingAxis) : Vector3.UnitY;
        float along = Vector3.Dot(q, axis);
        Vector3 radial = q - along * axis;
        float dist = radial.Length();

        // Grain noise: fBm if octaves > 1, else single-octave Perlin (legacy parity).
        float grain = Octaves <= 1
            ? noise.Noise(q)
            : noise.Fbm(q, Octaves, Lacunarity, Gain, signed: true);

        // Optional long-wave variation along the trunk axis (gentle waves on planks).
        if (AxialGrain > 0f)
        {
            grain += AxialGrain * noise.Noise(new Vector3(along * 0.5f, 0f, 0f));
        }

        float ring = (dist + NoiseStrength * grain) * _scale;
        float t = ring - MathF.Floor(ring);

        if (RingSharpness != 1f && RingSharpness > 0f)
        {
            // Smoothstep-like sharpening on a triangular wave centred at 0.5
            // pulls the latewood band into a narrow dark line, matching what
            // Arnold's "wood" and RenderMan's PxrWood produce by default.
            float tri = 1f - MathF.Abs(t * 2f - 1f);
            tri = MathF.Pow(tri, RingSharpness);
            t = tri;
        }

        Vector3 cLight = _lightWoodColor.Value(u, v, transformedP, objectSeed);
        Vector3 cDark = _darkWoodColor.Value(u, v, transformedP, objectSeed);
        return Vector3.Lerp(cDark, cLight, t);
    }
}
