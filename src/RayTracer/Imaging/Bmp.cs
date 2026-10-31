using System.Buffers.Binary;

namespace RayTracer.Imaging;

/// <summary>
/// Windows BMP reader and writer. Reads the OS/2 core header and the
/// BITMAPINFOHEADER family (v1 to v5): 1, 4 and 8-bit palettes (plain or
/// RLE), 16 and 32-bit with default or explicit channel masks (alpha only
/// when a mask declares it), 24-bit, bottom-up or top-down. Writes 24-bit.
/// </summary>
internal static class Bmp
{
    public static bool IsBmp(ReadOnlySpan<byte> d) => d.Length >= 26 && d[0] == 'B' && d[1] == 'M';

    private readonly record struct Header(int Width, int Height, bool TopDown, int Bpp, int Compression,
                                          int HeaderSize, int PaletteOffset, int PaletteCount, int PaletteEntry,
                                          uint RMask, uint GMask, uint BMask, uint AMask, int DataOffset);

    private static Header ParseHeader(ReadOnlySpan<byte> d)
    {
        if (!IsBmp(d)) throw new InvalidDataException("Not a BMP file.");
        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(d[10..]);
        int hs = BinaryPrimitives.ReadInt32LittleEndian(d[14..]);
        int w, h, bpp, comp = 0, colors = 0;
        uint rm = 0, gm = 0, bm = 0, am = 0;
        int paletteEntry = 4;
        if (hs == 12)
        {
            w = BinaryPrimitives.ReadInt16LittleEndian(d[18..]);
            h = BinaryPrimitives.ReadInt16LittleEndian(d[20..]);
            bpp = BinaryPrimitives.ReadInt16LittleEndian(d[24..]);
            paletteEntry = 3;
        }
        else if (hs >= 40 && d.Length >= 14 + hs)
        {
            w = BinaryPrimitives.ReadInt32LittleEndian(d[18..]);
            h = BinaryPrimitives.ReadInt32LittleEndian(d[22..]);
            bpp = BinaryPrimitives.ReadInt16LittleEndian(d[28..]);
            comp = BinaryPrimitives.ReadInt32LittleEndian(d[30..]);
            colors = BinaryPrimitives.ReadInt32LittleEndian(d[46..]);
            if (comp is 3 or 6)
            {
                // Masks inside a v2+ header, or right after a 40-byte one.
                int m = 54;
                rm = BinaryPrimitives.ReadUInt32LittleEndian(d[m..]);
                gm = BinaryPrimitives.ReadUInt32LittleEndian(d[(m + 4)..]);
                bm = BinaryPrimitives.ReadUInt32LittleEndian(d[(m + 8)..]);
                if (hs >= 56 || comp == 6) am = BinaryPrimitives.ReadUInt32LittleEndian(d[(m + 12)..]);
            }
        }
        else throw new InvalidDataException($"BMP: unsupported header size {hs}.");

        if (comp is not (0 or 1 or 2 or 3 or 6))
            throw new InvalidDataException("BMP: JPEG/PNG-compressed bitmaps are not supported.");
        if (bpp is not (1 or 4 or 8 or 16 or 24 or 32))
            throw new InvalidDataException($"BMP: {bpp} bits per pixel are not supported.");
        if (w <= 0 || h == 0) throw new InvalidDataException("BMP: invalid size.");
        if (comp == 0 && bpp == 16) { rm = 0x7C00; gm = 0x03E0; bm = 0x001F; }
        if (comp == 0 && bpp == 32) { rm = 0x00FF0000; gm = 0x0000FF00; bm = 0x000000FF; }
        int extraMasks = hs == 40 && comp == 3 ? 12 : hs == 40 && comp == 6 ? 16 : 0;
        int palCount = bpp <= 8 ? (colors > 0 ? colors : 1 << bpp) : 0;
        return new Header(w, Math.Abs(h), h < 0, bpp, comp, hs, 14 + hs + extraMasks, palCount, paletteEntry,
                          rm, gm, bm, am, dataOffset);
    }

    public static ImageInfo Identify(ReadOnlySpan<byte> d)
    {
        var h = ParseHeader(d);
        return new ImageInfo(h.Width, h.Height, h.AMask != 0 ? 4 : 3, 8);
    }

    public static RasterImage Decode(ReadOnlySpan<byte> d)
    {
        var h = ParseHeader(d);
        bool alpha = h.AMask != 0;
        var image = new RasterImage(h.Width, h.Height, alpha ? 4 : 3, 8);
        var s = image.Samples;

        var palette = new byte[256 * 3];
        for (int i = 0; i < Math.Min(h.PaletteCount, 256); i++)
        {
            int p = h.PaletteOffset + i * h.PaletteEntry;
            if (p + 3 > d.Length) break;
            palette[3 * i] = d[p + 2];
            palette[3 * i + 1] = d[p + 1];
            palette[3 * i + 2] = d[p];
        }

        // Palette indices per pixel (bottom-up file rows → image rows).
        byte[]? indices = null;
        if (h.Compression is 1 or 2) indices = DecodeRle(d[h.DataOffset..], h);

        int stride = ((h.Width * h.Bpp + 31) / 32) * 4;
        for (int fy = 0; fy < h.Height; fy++)
        {
            int y = h.TopDown ? fy : h.Height - 1 - fy;
            int row = h.DataOffset + fy * stride;
            for (int x = 0; x < h.Width; x++)
            {
                int o = (y * h.Width + x) * image.Channels;
                if (h.Bpp <= 8)
                {
                    int idx;
                    if (indices != null) idx = indices[fy * h.Width + x];
                    else
                    {
                        int bit = x * h.Bpp;
                        int p = row + (bit >> 3);
                        if (p >= d.Length) throw new InvalidDataException("BMP: truncated pixel data.");
                        idx = d[p] >> (8 - h.Bpp - (bit & 7)) & ((1 << h.Bpp) - 1);
                    }
                    s[o] = palette[3 * idx];
                    s[o + 1] = palette[3 * idx + 1];
                    s[o + 2] = palette[3 * idx + 2];
                    continue;
                }
                int q = row + x * (h.Bpp / 8);
                if (q + h.Bpp / 8 > d.Length) throw new InvalidDataException("BMP: truncated pixel data.");
                if (h.Bpp == 24)
                {
                    s[o] = d[q + 2];
                    s[o + 1] = d[q + 1];
                    s[o + 2] = d[q];
                    continue;
                }
                uint v = h.Bpp == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(d[q..])
                                     : BinaryPrimitives.ReadUInt32LittleEndian(d[q..]);
                s[o] = Masked(v, h.RMask);
                s[o + 1] = Masked(v, h.GMask);
                s[o + 2] = Masked(v, h.BMask);
                if (alpha) s[o + 3] = Masked(v, h.AMask);
            }
        }
        return image;
    }

    // The field under the mask, scaled to 0-255.
    private static ushort Masked(uint v, uint mask)
    {
        if (mask == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift;
        uint field = (v & mask) >> shift;
        return (ushort)((field * 255 + max / 2) / max);
    }

    private static byte[] DecodeRle(ReadOnlySpan<byte> d, Header h)
    {
        var idx = new byte[h.Width * h.Height];
        int x = 0, y = 0, p = 0;
        bool four = h.Compression == 2;
        void Put(int v)
        {
            if (x < h.Width && y < h.Height) idx[y * h.Width + x] = (byte)v;
            x++;
        }
        while (p + 1 < d.Length && y < h.Height)
        {
            int n = d[p], v = d[p + 1];
            p += 2;
            if (n > 0)
            {
                for (int i = 0; i < n; i++) Put(four ? (i % 2 == 0 ? v >> 4 : v & 15) : v);
                continue;
            }
            switch (v)
            {
                case 0: x = 0; y++; break;          // end of line
                case 1: return idx;                 // end of bitmap
                case 2:                             // delta
                    if (p + 1 >= d.Length) return idx;
                    x += d[p]; y += d[p + 1]; p += 2;
                    break;
                default:                            // absolute run of v indices
                    int bytes = four ? (v + 1) / 2 : v;
                    for (int i = 0; i < v; i++)
                    {
                        int b = p + (four ? i / 2 : i);
                        if (b >= d.Length) return idx;
                        Put(four ? (i % 2 == 0 ? d[b] >> 4 : d[b] & 15) : d[b]);
                    }
                    p += bytes + (bytes & 1); // runs are word-aligned
                    break;
            }
        }
        return idx;
    }

    /// <summary>Writes a 24-bit bottom-up BMP (alpha dropped).</summary>
    public static void Encode(Stream stream, RasterImage image)
    {
        if (image.BitDepth != 8) throw new ArgumentException("BMP output holds 8-bit samples only.");
        int stride = ((image.Width * 24 + 31) / 32) * 4;
        int dataSize = stride * image.Height;
        var head = new byte[54];
        head[0] = (byte)'B';
        head[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(2), 54 + dataSize);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(18), image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(22), image.Height);
        BinaryPrimitives.WriteInt16LittleEndian(head.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(head.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(34), dataSize);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(38), 2835); // 72 dpi
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(42), 2835);
        stream.Write(head);
        var row = new byte[stride];
        for (int y = image.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image.GetRgba(x, y, out int r, out int g, out int b, out _);
                row[3 * x] = (byte)b;
                row[3 * x + 1] = (byte)g;
                row[3 * x + 2] = (byte)r;
            }
            stream.Write(row);
        }
    }
}
