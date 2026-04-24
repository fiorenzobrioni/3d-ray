using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural Perlin noise texture.
///
/// <para>
/// When <see cref="NoiseStrength"/> is 0 (default) the texture samples smooth
/// Perlin noise directly, producing soft blobs and gradients.
/// </para>
/// <para>
/// When <see cref="NoiseStrength"/> is greater than 0, the raw Perlin value is
/// replaced with <c>Turbulence * NoiseStrength</c>, producing a more chaotic,
/// high-frequency pattern suitable for rough surfaces, smoke or fire effects.
/// </para>
/// <para>
/// By default the noise value is mapped to greyscale (black → white). When two
/// colors are supplied via the constructor (or the YAML <c>colors</c> field),
/// the output is <c>Lerp(colorA, colorB, noiseVal)</c> instead.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "noise"
///   scale: 5.0
///   noise_strength: 0.0    # 0 = smooth (default), >0 = turbulent
///   colors: [[0, 0, 0], [1, 1, 1]]   # optional: two RGB triplets
///   offset: [0, 0, 0]
///   rotation: [0, 0, 0]
///   randomize_offset: false
///   randomize_rotation: false
/// </code>
/// </summary>
public class NoiseTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly Vector3 _colorA;
    private readonly Vector3 _colorB;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    /// <summary>
    /// Turbulence weight.
    /// <list type="bullet">
    ///   <item><description><b>0.0</b> (default) — smooth Perlin noise, same as before this property existed.</description></item>
    ///   <item><description><b>&gt; 0.0</b> — turbulent noise: higher values produce a rougher, more chaotic pattern.</description></item>
    /// </list>
    /// Corresponds to the YAML field <c>noise_strength</c>.
    /// </summary>
    public float NoiseStrength { get; set; } = 0f;

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
    {
        Vector3 transformedP = TextureTransform.Apply(
            p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        // Pick a deterministic Perlin instance per object seed so the procedural
        // pattern is reproducible across renders. objectSeed == 0 falls back to
        // the canonical default instance (also seeded deterministically).
        Perlin noise = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;

        float noiseVal;
        if (NoiseStrength > 0f)
        {
            // Turbulent mode: use summed octaves for a rougher appearance.
            noiseVal = noise.Turbulence(_scale * transformedP) * NoiseStrength;
            // Clamp to [0, 1] since turbulence output * strength can exceed 1.
            noiseVal = Math.Clamp(noiseVal, 0f, 1f);
        }
        else
        {
            // Smooth mode: remap Perlin output from [-1, 1] to [0, 1].
            noiseVal = (noise.Noise(_scale * transformedP) + 1f) * 0.5f;
        }

        return Vector3.Lerp(_colorA, _colorB, noiseVal);
    }
}
