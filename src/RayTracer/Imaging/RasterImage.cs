namespace RayTracer.Imaging;

/// <summary>
/// A decoded (or to-be-encoded) raster: samples interleaved per pixel, rows
/// from top to bottom, at the file's own precision. <see cref="BitDepth"/> 8
/// holds 0-255, 16 holds 0-65535; files with fewer bits per sample (1, 2 and
/// 4-bit PNG, 5-bit BMP/TGA channels) are scaled up to 8 bits exactly, a
/// palette is expanded to its colours.
/// </summary>
public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>1 grey, 2 grey + alpha, 3 RGB, 4 RGBA.</summary>
    public int Channels { get; }

    /// <summary>Bits per sample: 8 or 16.</summary>
    public int BitDepth { get; }

    /// <summary><c>Width × Height × Channels</c> samples, row-major, top row first.</summary>
    public ushort[] Samples { get; }

    public RasterImage(int width, int height, int channels, int bitDepth)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"Invalid image size {width}×{height}.");
        if (channels is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(channels));
        if (bitDepth is not (8 or 16)) throw new ArgumentOutOfRangeException(nameof(bitDepth));
        Width = width;
        Height = height;
        Channels = channels;
        BitDepth = bitDepth;
        Samples = new ushort[checked(width * height * channels)];
    }

    /// <summary>Largest sample value: 255 or 65535.</summary>
    public int MaxValue => (1 << BitDepth) - 1;

    public bool HasAlpha => Channels is 2 or 4;

    public bool IsGrey => Channels <= 2;

    public ushort this[int x, int y, int c]
    {
        get => Samples[(y * Width + x) * Channels + c];
        set => Samples[(y * Width + x) * Channels + c] = value;
    }

    /// <summary>
    /// The pixel as RGBA at <see cref="BitDepth"/> (grey copied into R, G
    /// and B; alpha <see cref="MaxValue"/> when the image has none).
    /// </summary>
    public void GetRgba(int x, int y, out int r, out int g, out int b, out int a)
    {
        int i = (y * Width + x) * Channels;
        var s = Samples;
        switch (Channels)
        {
            case 1: r = g = b = s[i]; a = MaxValue; break;
            case 2: r = g = b = s[i]; a = s[i + 1]; break;
            case 3: r = s[i]; g = s[i + 1]; b = s[i + 2]; a = MaxValue; break;
            default: r = s[i]; g = s[i + 1]; b = s[i + 2]; a = s[i + 3]; break;
        }
    }
}

/// <summary>Size and format of an image file, read from its header.</summary>
public readonly record struct ImageInfo(int Width, int Height, int Channels, int BitDepth)
{
    public bool HasAlpha => Channels is 2 or 4;
}
