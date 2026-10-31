namespace RayTracer.Imaging;

/// <summary>
/// Reads and writes the engine's 8/16-bit raster formats: PNG, JPEG, BMP and
/// TGA, recognised by their content (TGA, which has no signature, by its
/// extension or header). High dynamic range files (<c>.hdr</c>, <c>.exr</c>,
/// <c>.pfm</c>) have their own readers.
/// </summary>
public static class ImageFile
{
    public static RasterImage Load(string path) => Decode(File.ReadAllBytes(path), Path.GetExtension(path));

    /// <summary>Decodes an image held in memory (a texture embedded in a glTF file).</summary>
    public static RasterImage Decode(byte[] data, string? extension = null)
    {
        if (Png.IsPng(data)) return Png.Decode(data);
        if (JpegDecoder.IsJpeg(data)) return JpegDecoder.Decode(data);
        if (Bmp.IsBmp(data)) return Bmp.Decode(data);
        if (IsTga(data, extension)) return Tga.Decode(data);
        throw new InvalidDataException(Unsupported(data, extension));
    }

    /// <summary>Size and layout of an image file from its header.</summary>
    public static ImageInfo Identify(string path)
    {
        var data = File.ReadAllBytes(path);
        string ext = Path.GetExtension(path);
        if (Png.IsPng(data)) return Png.Identify(data);
        if (JpegDecoder.IsJpeg(data)) return JpegDecoder.Identify(data);
        if (Bmp.IsBmp(data)) return Bmp.Identify(data);
        if (IsTga(data, ext)) return Tga.Identify(data);
        throw new InvalidDataException(Unsupported(data, ext));
    }

    /// <summary>
    /// Writes <paramref name="image"/> in the format of the extension:
    /// <c>.jpg</c>/<c>.jpeg</c> (quality 95, 4:4:4), <c>.bmp</c> (24-bit),
    /// anything else PNG. <paramref name="srgb"/> marks a PNG as sRGB-encoded
    /// display output.
    /// </summary>
    public static void Save(string path, RasterImage image, bool srgb = false)
    {
        using var stream = new BufferedStream(File.Create(path), 1 << 16);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                JpegEncoder.Encode(stream, image);
                break;
            case ".bmp":
                Bmp.Encode(stream, image);
                break;
            default:
                Png.Encode(stream, image, srgb);
                break;
        }
    }

    private static bool IsTga(ReadOnlySpan<byte> data, string? extension)
    {
        string ext = (extension ?? "").ToLowerInvariant();
        if (ext is ".tga" or ".tpic" or ".vda" or ".icb" or ".vst") return true;
        // TGA 2.0 files end with a signature.
        return data.Length >= 18 && data[^18..^2].SequenceEqual("TRUEVISION-XFILE"u8) && Tga.LooksLikeTga(data);
    }

    private static string Unsupported(ReadOnlySpan<byte> d, string? extension)
    {
        string? kind =
            d.Length >= 4 && d[..4].SequenceEqual("GIF8"u8) ? "GIF" :
            d.Length >= 4 && (d[..4].SequenceEqual("II*\0"u8) || d[..4].SequenceEqual("MM\0*"u8)) ? "TIFF" :
            d.Length >= 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8) ? "WebP" :
            d.Length >= 4 && d[..4].SequenceEqual("qoif"u8) ? "QOI" :
            d.Length >= 4 && d[..4].SequenceEqual("DDS "u8) ? "DDS" :
            d.Length >= 12 && d[1] == 'K' && d[2] == 'T' && d[3] == 'X' ? "KTX" :
            null;
        string what = kind != null ? $"{kind} images are" : $"the format of this file{(string.IsNullOrEmpty(extension) ? "" : $" ({extension})")} is";
        return $"Unsupported image: {what} not supported. Use PNG (16-bit for data maps), JPEG, BMP or TGA, "
             + "or EXR/HDR for high dynamic range.";
    }
}
