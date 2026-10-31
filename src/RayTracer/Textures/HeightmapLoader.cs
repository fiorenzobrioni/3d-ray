using RayTracer.Imaging;

namespace RayTracer.Textures;

/// <summary>
/// Loads a grayscale heightmap (PNG, or any format <see cref="ImageFile"/>
/// reads) into a flat row-major array of normalised heights in <c>[0, 1]</c>.
/// PNG-16 is the standard industry export format (Unreal, Unity, World
/// Machine) and is loaded with full 16-bit precision. An 8-bit file is
/// accepted with reduced precision (256 quantisation levels: visible
/// terracing on smooth terrains). A colour file is read through its first
/// channel.
/// </summary>
public static class HeightmapLoader
{
    /// <summary>
    /// Returns the heights in row-major order with <c>row j</c> at index
    /// <c>i + j * width</c>. Image row 0 is mapped to <c>j = 0</c>; callers that
    /// want a different Z convention should flip on consume.
    /// </summary>
    public static float[] Load(string path, out int width, out int height)
    {
        var image = ImageFile.Load(path);
        width = image.Width;
        height = image.Height;
        var data = new float[width * height];
        float inv = 1f / image.MaxValue;
        var s = image.Samples;
        int ch = image.Channels;
        for (int i = 0; i < data.Length; i++)
            data[i] = s[i * ch] * inv;
        return data;
    }

    /// <summary>
    /// True when the file at <paramref name="path"/> stores 16 bits per sample.
    /// Used to surface a deferred precision-loss warning when the user
    /// supplies an 8-bit heightmap.
    /// </summary>
    public static bool IsHighPrecision(string path)
    {
        try
        {
            return ImageFile.Identify(path).BitDepth >= 16;
        }
        catch
        {
            return false;
        }
    }
}
