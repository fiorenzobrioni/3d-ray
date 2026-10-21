using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// A texture that loads a normal map image and returns tangent-space normals.
///
/// The image is read as data, never decoded from sRGB, through
/// <see cref="ImageTexture"/>: 8 or 16 bits per channel (or EXR), bilinear,
/// texel-centred, wrapping at the edges so a tiled map has no seam.
///
/// The RGB channels are mapped from [0, 1] to [-1, 1]:
///   normalXYZ = RGB * 2.0 - 1.0
/// The result is then normalized.
///
/// Convention: OpenGL-style normal maps (Y+ points up in tangent space, along
/// +v). If you have DirectX-style maps (Y inverted), set <c>flipY: true</c>.
/// On a mesh the tangent frame is MikkTSpace's (<see cref="Geometry.MeshTangents"/>),
/// the one bakers write in.
///
/// In YAML:
///   normal_map:
///     path: "textures/brick_normal.png"
///     strength: 1.0
///     uv_scale: [1, 1]
/// </summary>
public class NormalMapTexture
{
    private readonly ImageTexture _image;
    private readonly float _strength;
    private readonly bool _flipY;

    /// <summary>
    /// Loads a normal map image from disk.
    /// </summary>
    /// <param name="imagePath">Path to the normal map file.</param>
    /// <param name="strength">
    ///   Normal perturbation strength. 1.0 = full effect, 0.0 = no effect.
    ///   Values above 1.0 amplify the XY tangent components before renormalization.
    ///   At 2.0 the maximum bump angle from the geometric normal is ≈70°.
    ///   At 3.0 (the cap) it reaches ≈77° — near-tangential normals may produce
    ///   dark halos at grazing incidence. Values above 1.5 are rarely needed.
    /// </param>
    /// <param name="scaleU">UV tiling factor on U axis.</param>
    /// <param name="scaleV">UV tiling factor on V axis.</param>
    /// <param name="flipY">If true, inverts the Y (green) channel for DirectX-style maps.</param>
    public NormalMapTexture(string imagePath, float strength = 1f,
                            float scaleU = 1f, float scaleV = 1f, bool flipY = false)
    {
        _strength = Math.Clamp(strength, 0f, 3f);
        _flipY = flipY;
        _image = new ImageTexture(imagePath, scaleU, scaleV, TextureColorSpace.Linear);
    }

    /// <summary>
    /// Samples the normal map at (u, v) and returns a tangent-space normal vector.
    /// The returned vector is normalized and ready to be transformed by the TBN matrix.
    /// When strength &lt; 1, the normal is lerped toward (0, 0, 1) (unperturbed).
    /// </summary>
    public Vector3 SampleNormal(float u, float v)
    {
        Vector3 rgb = _image.Value(u, v, Vector3.Zero, 0);

        // Convert [0, 1] → [-1, 1]
        Vector3 normal = rgb * 2f - Vector3.One;

        // Flip Y for DirectX-style normal maps
        if (_flipY) normal.Y = -normal.Y;

        // Apply strength: lerp between unperturbed (0,0,1) and the map normal
        if (_strength < 1f)
        {
            normal = Vector3.Lerp(Vector3.UnitZ, normal, _strength);
        }
        else if (_strength > 1f)
        {
            // Amplify the XY perturbation components; Z (the base normal direction)
            // is left unchanged. After renormalization this pushes the resulting normal
            // further away from N, deepening the apparent bumps.
            // At strength=2 with peak map values: max bump angle ≈ 70° from geometric N.
            // At strength=3 (the cap): ≈ 77° — use with care on glossy surfaces.
            normal.X *= _strength;
            normal.Y *= _strength;
        }

        // A black texel (or a 16-bit file's padding) decodes to no direction.
        return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
    }
}
