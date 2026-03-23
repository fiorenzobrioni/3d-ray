using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RayTracer.Textures;

/// <summary>
/// A texture that samples color from an image file using bilinear filtering.
///
/// UV coordinates are expected in [0,1] range (as produced by the UV mapping
/// of all primitives). Values outside [0,1] are wrapped via frac() for
/// seamless tiling.
///
/// The image is loaded once at construction time and kept in memory as a
/// float RGB array for fast, allocation-free sampling during rendering.
///
/// Supports all formats handled by ImageSharp: PNG, JPEG, BMP, GIF, TIFF, WebP.
///
/// In YAML:
///   texture:
///     type: "image"
///     path: "textures/brick_wall.png"      # Relative to the scene YAML file
///     scale: [1, 1]                        # Optional UV scale (tiling)
/// </summary>
public class ImageTexture : ITexture
{
    private readonly float[] _pixels;  // Flat RGB array: [r0, g0, b0, r1, g1, b1, ...]
    private readonly int _width;
    private readonly int _height;
    private readonly float _scaleU;
    private readonly float _scaleV;

    /// <summary>
    /// Loads an image from disk and converts it to a linear RGB float buffer.
    /// </summary>
    /// <param name="imagePath">Absolute or relative path to the image file.</param>
    /// <param name="scaleU">UV tiling factor on U axis (default 1.0).</param>
    /// <param name="scaleV">UV tiling factor on V axis (default 1.0).</param>
    public ImageTexture(string imagePath, float scaleU = 1f, float scaleV = 1f)
    {
        _scaleU = scaleU;
        _scaleV = scaleV;

        using var image = Image.Load<Rgba32>(imagePath);
        _width = image.Width;
        _height = image.Height;
        _pixels = new float[_width * _height * 3];

        // Convert to linear RGB float array (sRGB → linear approximation via pow 2.2)
        const float inv255 = 1f / 255f;
        const float gamma = 2.2f;

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
                    // Convert sRGB → linear for physically correct rendering
                    _pixels[idx]     = MathF.Pow(pixel.R * inv255, gamma);
                    _pixels[idx + 1] = MathF.Pow(pixel.G * inv255, gamma);
                    _pixels[idx + 2] = MathF.Pow(pixel.B * inv255, gamma);
                }
            }
        });
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // Apply UV tiling scale
        u *= _scaleU;
        v *= _scaleV;

        // Wrap to [0, 1) for seamless tiling
        u = u - MathF.Floor(u);
        v = v - MathF.Floor(v);

        // Flip V: image convention is top-left origin, UV convention is bottom-left
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

        // Bilinear interpolation
        Vector3 top = Vector3.Lerp(c00, c10, fx);
        Vector3 bot = Vector3.Lerp(c01, c11, fx);
        return Vector3.Lerp(top, bot, fy);
    }

    private Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]);
    }
}
