using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RayTracer.Textures;

/// <summary>
/// Loads a grayscale heightmap from a PNG file into a flat row-major array of
/// normalised heights in <c>[0, 1]</c>. PNG-16 (<see cref="L16"/>) is the
/// standard industry export format (Unreal, Unity, World Machine) and is
/// loaded with full 16-bit precision. PNG-8 is accepted as a fallback with
/// reduced precision (256 quantisation levels — visible terracing on smooth
/// terrains).
/// </summary>
public static class HeightmapLoader
{
    /// <summary>
    /// Returns the heights in row-major order with <c>row j</c> at index
    /// <c>i + j * width</c>. PNG row 0 is mapped to <c>j = 0</c>; callers that
    /// want a different Z convention should flip on consume.
    /// </summary>
    public static float[] Load(string path, out int width, out int height)
    {
        using var image = Image.Load(path);
        width = image.Width;
        height = image.Height;
        var data = new float[width * height];

        if (image is Image<L16> img16)
        {
            const float inv = 1f / 65535f;
            img16.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int baseIdx = y * accessor.Width;
                    for (int x = 0; x < accessor.Width; x++)
                        data[baseIdx + x] = row[x].PackedValue * inv;
                }
            });
            return data;
        }

        // Fallback: convert via Rgba32 (covers L8, Rgba32, Rgb24, etc.). Loses
        // precision relative to PNG-16; the caller may emit a deferred warning.
        using var rgba = image.CloneAs<Rgba32>();
        const float inv255 = 1f / 255f;
        rgba.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int baseIdx = y * accessor.Width;
                for (int x = 0; x < accessor.Width; x++)
                {
                    // Use the red channel — grayscale PNGs round-trip through
                    // it intact and colour heightmaps are non-standard anyway.
                    data[baseIdx + x] = row[x].R * inv255;
                }
            }
        });
        return data;
    }

    /// <summary>
    /// True when the file at <paramref name="path"/> is a 16-bit grayscale PNG
    /// (<see cref="L16"/>). Used to surface a deferred precision-loss warning
    /// when the user supplies an 8-bit heightmap.
    /// </summary>
    public static bool IsHighPrecision(string path)
    {
        try
        {
            var info = Image.Identify(path);
            return info?.PixelType?.BitsPerPixel >= 16;
        }
        catch
        {
            return false;
        }
    }
}
