using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural Perlin noise texture.
///
/// <para>
/// When <see cref="NoiseStrength"/> is 0 (default) the texture samples smooth
/// Perlin noise directly, producing soft greyscale blobs and gradients.
/// </para>
/// <para>
/// When <see cref="NoiseStrength"/> is greater than 0, the raw Perlin value is
/// replaced with <c>Turbulence * NoiseStrength</c>, producing a more chaotic,
/// high-frequency pattern suitable for rough surfaces, smoke or fire effects.
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "noise"
///   scale: 5.0
///   noise_strength: 0.0    # 0 = smooth (default), >0 = turbulent
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
    {
        _noise = new Perlin();
        _scale = scale;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(
            p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        float noiseVal;
        if (NoiseStrength > 0f)
        {
            // Turbulent mode: use summed octaves for a rougher appearance.
            noiseVal = _noise.Turbulence(_scale * transformedP) * NoiseStrength;
            // Clamp to [0, 1] since turbulence output * strength can exceed 1.
            noiseVal = Math.Clamp(noiseVal, 0f, 1f);
        }
        else
        {
            // Smooth mode: remap Perlin output from [-1, 1] to [0, 1].
            noiseVal = (_noise.Noise(_scale * transformedP) + 1f) * 0.5f;
        }

        return Vector3.One * noiseVal;
    }
}
