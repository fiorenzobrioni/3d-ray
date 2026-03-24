using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RayTracer.Textures;

/// <summary>
/// A texture that loads a normal map image and returns tangent-space normals.
///
/// Unlike ImageTexture, this does NOT apply sRGB→linear gamma correction
/// because normal maps store directional data, not color data.
///
/// The RGB channels are mapped from [0, 1] to [-1, 1]:
///   normalXYZ = RGB * 2.0 - 1.0
/// The result is then normalized.
///
/// Convention: OpenGL-style normal maps (Y+ points up in tangent space).
/// If you have DirectX-style maps (Y inverted), set <c>flipY: true</c>.
///
/// In YAML:
///   normal_map:
///     path: "textures/brick_normal.png"
///     strength: 1.0
///     uv_scale: [1, 1]
/// </summary>
public class NormalMapTexture
{
    private readonly float[] _pixels;  // Flat RGB array (LINEAR, no gamma)
    private readonly int _width;
    private readonly int _height;
    private readonly float _scaleU;
    private readonly float _scaleV;
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
        _scaleU = scaleU;
        _scaleV = scaleV;
        _flipY = flipY;

        using var image = Image.Load<Rgba32>(imagePath);
        _width = image.Width;
        _height = image.Height;
        _pixels = new float[_width * _height * 3];

        // Store as linear float — NO gamma correction (normal maps are linear data)
        const float inv255 = 1f / 255f;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < _height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int baseIdx = y * _width * 3;
                for (int x = 0; x < _width; x++)
                {
                    var pixel = row[x];
                    int idx = baseIdx + x * 3;
                    _pixels[idx]     = pixel.R * inv255;
                    _pixels[idx + 1] = pixel.G * inv255;
                    _pixels[idx + 2] = pixel.B * inv255;
                }
            }
        });
    }

    /// <summary>
    /// Samples the normal map at (u, v) and returns a tangent-space normal vector.
    /// The returned vector is normalized and ready to be transformed by the TBN matrix.
    /// When strength &lt; 1, the normal is lerped toward (0, 0, 1) (unperturbed).
    /// </summary>
    public Vector3 SampleNormal(float u, float v)
    {
        // Apply UV tiling
        u *= _scaleU;
        v *= _scaleV;

        // Wrap to [0, 1)
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        // Flip V (image top-left origin → UV bottom-left)
        v = 1f - v;

        // Map to pixel coordinates (continuous)
        float px = u * (_width - 1);
        float py = v * (_height - 1);

        // Bilinear filtering
        int x0 = (int)px;
        int y0 = (int)py;
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);

        float fx = px - x0;
        float fy = py - y0;

        Vector3 c00 = GetPixel(x0, y0);
        Vector3 c10 = GetPixel(x1, y0);
        Vector3 c01 = GetPixel(x0, y1);
        Vector3 c11 = GetPixel(x1, y1);

        Vector3 top = Vector3.Lerp(c00, c10, fx);
        Vector3 bot = Vector3.Lerp(c01, c11, fx);
        Vector3 rgb = Vector3.Lerp(top, bot, fy);

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
        
        return Vector3.Normalize(normal);
    }

    private Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]);
    }
}
