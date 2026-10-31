using System.Buffers.Binary;

namespace RayTracer.Imaging;

/// <summary>
/// Truevision TGA reader: colour-mapped, true-colour and grey images, raw or
/// RLE, 8/15/16/24/32 bits, any origin. A 32-bit image is read as RGBA, a
/// 16-bit grey one as grey + alpha.
/// </summary>
internal static class Tga
{
    private readonly record struct Header(int IdLength, int MapType, int Type, int MapFirst, int MapLength,
                                          int MapEntryBits, int Width, int Height, int Bpp, int Descriptor)
    {
        public bool Grey => (Type & 7) == 3;
        public bool Mapped => (Type & 7) == 1;
        public bool Rle => Type >= 9;
        public int PixelBits => Mapped ? MapEntryBits : Bpp;
        public int Channels => Grey ? (Bpp == 16 ? 2 : 1) : PixelBits == 32 ? 4 : 3;
    }

    private static Header ParseHeader(ReadOnlySpan<byte> d)
    {
        if (d.Length < 18) throw new InvalidDataException("TGA: truncated header.");
        var h = new Header(d[0], d[1], d[2], BinaryPrimitives.ReadUInt16LittleEndian(d[3..]),
                           BinaryPrimitives.ReadUInt16LittleEndian(d[5..]), d[7],
                           BinaryPrimitives.ReadUInt16LittleEndian(d[12..]),
                           BinaryPrimitives.ReadUInt16LittleEndian(d[14..]), d[16], d[17]);
        if (h.Type is not (1 or 2 or 3 or 9 or 10 or 11)) throw new InvalidDataException($"TGA: unsupported image type {h.Type}.");
        if (h.Width == 0 || h.Height == 0) throw new InvalidDataException("TGA: invalid size.");
        bool ok = h.Grey ? h.Bpp is 8 or 16
                : h.Mapped ? h.Bpp is 8 or 16 && h.MapType == 1 && h.MapEntryBits is 15 or 16 or 24 or 32
                : h.Bpp is 15 or 16 or 24 or 32;
        if (!ok) throw new InvalidDataException($"TGA: unsupported pixel format ({h.Bpp} bits, type {h.Type}).");
        return h;
    }

    /// <summary>A loose check for a file with no magic number: a known type
    /// and a plausible pixel size.</summary>
    public static bool LooksLikeTga(ReadOnlySpan<byte> d)
    {
        try { ParseHeader(d); return true; }
        catch (InvalidDataException) { return false; }
    }

    public static ImageInfo Identify(ReadOnlySpan<byte> d)
    {
        var h = ParseHeader(d);
        return new ImageInfo(h.Width, h.Height, h.Channels, 8);
    }

    public static RasterImage Decode(ReadOnlySpan<byte> d)
    {
        var h = ParseHeader(d);
        int p = 18 + h.IdLength;
        int entryBytes = (h.MapEntryBits + 7) / 8;
        int mapStart = p;
        if (h.MapType == 1) p += h.MapLength * entryBytes;

        var image = new RasterImage(h.Width, h.Height, h.Channels, 8);
        var s = image.Samples;
        int bytesPer = (h.Bpp + 7) / 8;
        bool topDown = (h.Descriptor & 0x20) != 0, rightToLeft = (h.Descriptor & 0x10) != 0;
        int n = h.Width * h.Height;
        Span<byte> px = stackalloc byte[4];
        int run = 0;
        bool repeat = false;
        for (int i = 0; i < n; i++)
        {
            if (h.Rle)
            {
                if (run == 0)
                {
                    if (p >= d.Length) throw new InvalidDataException("TGA: truncated RLE data.");
                    int header = d[p++];
                    run = (header & 0x7F) + 1;
                    repeat = (header & 0x80) != 0;
                    if (repeat) { Read(d, ref p, px, bytesPer); }
                }
                if (!repeat) Read(d, ref p, px, bytesPer);
                run--;
            }
            else Read(d, ref p, px, bytesPer);

            int fx = i % h.Width, fy = i / h.Width;
            int x = rightToLeft ? h.Width - 1 - fx : fx;
            int y = topDown ? fy : h.Height - 1 - fy;
            int o = (y * h.Width + x) * image.Channels;
            if (h.Grey)
            {
                s[o] = px[0];
                if (image.Channels == 2) s[o + 1] = px[1];
                continue;
            }
            ReadOnlySpan<byte> c = px;
            int bits = h.Bpp;
            if (h.Mapped)
            {
                int idx = (bytesPer == 2 ? px[0] | px[1] << 8 : px[0]) - h.MapFirst;
                if (idx < 0 || idx >= h.MapLength) throw new InvalidDataException("TGA: colour-map index out of range.");
                c = d.Slice(mapStart + idx * entryBytes, entryBytes);
                bits = h.MapEntryBits;
            }
            if (bits <= 16)
            {
                int v = c[0] | c[1] << 8;
                s[o] = (ushort)(((v >> 10) & 31) * 255 / 31);
                s[o + 1] = (ushort)(((v >> 5) & 31) * 255 / 31);
                s[o + 2] = (ushort)((v & 31) * 255 / 31);
            }
            else
            {
                s[o] = c[2];
                s[o + 1] = c[1];
                s[o + 2] = c[0];
                if (image.Channels == 4) s[o + 3] = c[3];
            }
        }
        return image;
    }

    private static void Read(ReadOnlySpan<byte> d, ref int p, Span<byte> px, int n)
    {
        if (p + n > d.Length) throw new InvalidDataException("TGA: truncated pixel data.");
        d.Slice(p, n).CopyTo(px);
        p += n;
    }
}
