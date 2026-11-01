using System.Buffers.Binary;
using System.IO.Compression;

namespace RayTracer.Imaging;

/// <summary>
/// PNG reader and writer (ISO/IEC 15948). Reads every standard form: grey,
/// grey + alpha, RGB, RGBA and palette, 1 to 16 bits per sample, Adam7
/// interlacing, <c>tRNS</c> transparency (palette alpha or colour key). Writes
/// grey, grey + alpha, RGB or RGBA at 8 or 16 bits, choosing the scanline
/// filter per row (minimum sum of absolute differences, the usual heuristic).
/// Colour-management chunks are not interpreted: how the values relate to
/// light is the texture's role (<c>color_space</c>), not the file's.
/// </summary>
public static class Png
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const uint IHDR = 0x49484452, PLTE = 0x504C5445, IDAT = 0x49444154, IEND = 0x49454E44,
                       TRNS = 0x74524E53, SRGB = 0x73524742;

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    private readonly record struct Header(int Width, int Height, int BitDepth, int ColorType, bool Interlaced)
    {
        public int SamplesPerPixel => ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        public int BitsPerPixel => SamplesPerPixel * BitDepth;
        public int RowBytes(int width) => (int)(((long)width * BitsPerPixel + 7) / 8);
    }

    // ───────────────────────────── reading ─────────────────────────────

    /// <summary>Size and layout from the header, without decompressing.</summary>
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        Header? header = null;
        bool trns = false;
        foreach (var (type, body) in Chunks(data))
        {
            if (type == IHDR) header = ParseHeader(body);
            else if (type == TRNS) trns = true;
            else if (type is IDAT or IEND) break;
        }
        if (header is not { } h) throw new InvalidDataException("PNG: missing IHDR chunk.");
        return new ImageInfo(h.Width, h.Height, OutputChannels(h.ColorType, trns), h.BitDepth == 16 ? 16 : 8);
    }

    public static RasterImage Decode(ReadOnlySpan<byte> data)
    {
        Header? header = null;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        bool ended = false;
        foreach (var (type, body) in Chunks(data))
        {
            switch (type)
            {
                case IHDR: header = ParseHeader(body); break;
                case PLTE: palette = body.ToArray(); break;
                case TRNS: trns = body.ToArray(); break;
                case IDAT: idat.Write(body); break;
                case IEND: ended = true; break;
                default:
                    // An unknown critical chunk (first letter upper case) means
                    // the image cannot be read correctly without it.
                    if ((type & 0x20000000) == 0)
                        throw new InvalidDataException($"PNG: unsupported critical chunk '{ChunkName(type)}'.");
                    break;
            }
            if (ended) break;
        }
        if (header is not { } h) throw new InvalidDataException("PNG: missing IHDR chunk.");
        if (idat.Length == 0) throw new InvalidDataException("PNG: no image data.");
        if (h.ColorType == 3 && (palette == null || palette.Length % 3 != 0))
            throw new InvalidDataException("PNG: palette image without a valid PLTE chunk.");

        // The whole filtered stream: one filter byte plus the row per scanline,
        // pass after pass when interlaced.
        long expected = 0;
        foreach (var (_, _, pw, ph) in Passes(h))
            expected += (long)ph * (1 + h.RowBytes(pw));
        var raw = new byte[checked((int)expected)];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n == 0) throw new InvalidDataException("PNG: image data is truncated.");
                read += n;
            }
        }

        var image = new RasterImage(h.Width, h.Height, OutputChannels(h.ColorType, trns != null), h.BitDepth == 16 ? 16 : 8);
        int bpp = Math.Max(1, h.BitsPerPixel / 8);
        int offset = 0;
        int pass = 0;
        foreach (var (x0, y0, pw, ph) in Passes(h))
        {
            var (_, _, dx, dy) = Adam7(h.Interlaced, pass++);
            int rowBytes = h.RowBytes(pw);
            byte[] prev = new byte[rowBytes];
            byte[] cur = new byte[rowBytes];
            for (int r = 0; r < ph; r++)
            {
                int filter = raw[offset];
                raw.AsSpan(offset + 1, rowBytes).CopyTo(cur);
                offset += 1 + rowBytes;
                Unfilter(cur, prev, filter, bpp);
                StoreRow(image, h, cur, palette, trns, y0 + r * dy, x0, dx, pw);
                (prev, cur) = (cur, prev);
            }
        }
        return image;
    }

    private static int OutputChannels(int colorType, bool trns) => colorType switch
    {
        0 => trns ? 2 : 1,
        2 => trns ? 4 : 3,
        3 => trns ? 4 : 3,
        4 => 2,
        _ => 4,
    };

    private static Header ParseHeader(ReadOnlySpan<byte> b)
    {
        if (b.Length < 13) throw new InvalidDataException("PNG: IHDR chunk is too short.");
        uint w = BinaryPrimitives.ReadUInt32BigEndian(b), hgt = BinaryPrimitives.ReadUInt32BigEndian(b[4..]);
        int depth = b[8], type = b[9];
        if (w == 0 || hgt == 0 || w > int.MaxValue || hgt > int.MaxValue)
            throw new InvalidDataException($"PNG: invalid size {w}×{hgt}.");
        bool ok = type switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => depth is 8 or 16,
            _ => false,
        };
        if (!ok) throw new InvalidDataException($"PNG: invalid colour type {type} with bit depth {depth}.");
        if (b[10] != 0 || b[11] != 0) throw new InvalidDataException("PNG: unknown compression or filter method.");
        if (b[12] > 1) throw new InvalidDataException("PNG: unknown interlace method.");
        return new Header((int)w, (int)hgt, depth, type, b[12] == 1);
    }

    private static (int X0, int Y0, int Dx, int Dy) Adam7(bool interlaced, int pass)
    {
        if (!interlaced) return (0, 0, 1, 1);
        return pass switch
        {
            0 => (0, 0, 8, 8), 1 => (4, 0, 8, 8), 2 => (0, 4, 4, 8), 3 => (2, 0, 4, 4),
            4 => (0, 2, 2, 4), 5 => (1, 0, 2, 2), _ => (0, 1, 1, 2),
        };
    }

    /// <summary>Origin and size of each pass (one for a plain image, the seven
    /// Adam7 passes otherwise, the empty ones included with size 0).</summary>
    private static int Raw(ReadOnlySpan<byte> row, int depth, int mask, int i)
    {
        if (depth == 16) return row[2 * i] << 8 | row[2 * i + 1];
        if (depth == 8) return row[i];
        int bit = i * depth;
        return row[bit >> 3] >> (8 - depth - (bit & 7)) & mask;
    }

    private static IEnumerable<(int X0, int Y0, int Width, int Height)> Passes(Header h)
    {
        int count = h.Interlaced ? 7 : 1;
        for (int p = 0; p < count; p++)
        {
            var (x0, y0, dx, dy) = Adam7(h.Interlaced, p);
            int pw = h.Width > x0 ? (h.Width - x0 + dx - 1) / dx : 0;
            int ph = h.Height > y0 ? (h.Height - y0 + dy - 1) / dy : 0;
            if (pw == 0 || ph == 0) { yield return (x0, y0, 0, 0); continue; }
            yield return (x0, y0, pw, ph);
        }
    }

    private static void Unfilter(Span<byte> cur, ReadOnlySpan<byte> prev, int filter, int bpp)
    {
        int n = cur.Length;
        switch (filter)
        {
            case 0: break;
            case 1:
                for (int i = bpp; i < n; i++) cur[i] += cur[i - bpp];
                break;
            case 2:
                for (int i = 0; i < n; i++) cur[i] += prev[i];
                break;
            case 3:
                for (int i = 0; i < n; i++)
                    cur[i] += (byte)(((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1);
                break;
            case 4:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    cur[i] += (byte)Paeth(a, b, c);
                }
                break;
            default:
                throw new InvalidDataException($"PNG: invalid filter type {filter}.");
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void StoreRow(RasterImage image, Header h, ReadOnlySpan<byte> row, byte[]? palette,
                                 byte[]? trns, int y, int x0, int dx, int count)
    {
        var s = image.Samples;
        int ch = image.Channels;
        int depth = h.BitDepth;
        int spp = h.SamplesPerPixel;
        int mask = (1 << Math.Min(depth, 8)) - 1;

        for (int k = 0; k < count; k++)
        {
            int o = ((y * image.Width) + x0 + k * dx) * ch;
            switch (h.ColorType)
            {
                case 3:
                {
                    int idx = Raw(row, depth, mask, k);
                    if (3 * idx + 2 >= palette!.Length)
                        throw new InvalidDataException("PNG: palette index out of range.");
                    s[o] = palette[3 * idx];
                    s[o + 1] = palette[3 * idx + 1];
                    s[o + 2] = palette[3 * idx + 2];
                    if (ch == 4) s[o + 3] = idx < trns!.Length ? trns[idx] : (ushort)255;
                    break;
                }
                case 0:
                {
                    int v = Raw(row, depth, mask, k);
                    s[o] = (ushort)(depth < 8 ? v * 255 / mask : v);
                    if (ch == 2)
                    {
                        int key = trns!.Length >= 2 ? trns[0] << 8 | trns[1] : -1;
                        s[o + 1] = (ushort)(v == key ? 0 : image.MaxValue);
                    }
                    break;
                }
                case 2:
                {
                    int r = Raw(row, depth, mask, 3 * k), g = Raw(row, depth, mask, 3 * k + 1), b = Raw(row, depth, mask, 3 * k + 2);
                    s[o] = (ushort)r; s[o + 1] = (ushort)g; s[o + 2] = (ushort)b;
                    if (ch == 4)
                    {
                        bool key = trns!.Length >= 6
                                   && r == (trns[0] << 8 | trns[1])
                                   && g == (trns[2] << 8 | trns[3])
                                   && b == (trns[4] << 8 | trns[5]);
                        s[o + 3] = (ushort)(key ? 0 : image.MaxValue);
                    }
                    break;
                }
                default: // 4 (grey + alpha) and 6 (RGBA): samples as stored
                    for (int c = 0; c < spp; c++) s[o + c] = (ushort)Raw(row, depth, mask, spp * k + c);
                    break;
            }
        }
    }

    /// <summary>The chunks in file order; checks the CRC of the critical ones.</summary>
    private static ChunkEnumerable Chunks(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data)) throw new InvalidDataException("Not a PNG file.");
        return new ChunkEnumerable(data);
    }

    private readonly ref struct ChunkEnumerable(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        public ChunkEnumerator GetEnumerator() => new(_data);
    }

    private readonly ref struct Chunk(uint type, ReadOnlySpan<byte> body)
    {
        private readonly uint _type = type;
        private readonly ReadOnlySpan<byte> _body = body;

        public void Deconstruct(out uint type, out ReadOnlySpan<byte> body)
        {
            type = _type;
            body = _body;
        }
    }

    private ref struct ChunkEnumerator(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos = 8;
        public Chunk Current { get; private set; }

        public bool MoveNext()
        {
            // A file cut after its last IDAT (no IEND) is still read.
            if (_pos + 12 > _data.Length) return false;
            int len = BinaryPrimitives.ReadInt32BigEndian(_data[_pos..]);
            if (len < 0 || _pos + 12L + len > _data.Length) throw new InvalidDataException("PNG: truncated chunk.");
            uint type = BinaryPrimitives.ReadUInt32BigEndian(_data[(_pos + 4)..]);
            if ((type & 0x20000000) == 0)
            {
                uint crc = BinaryPrimitives.ReadUInt32BigEndian(_data[(_pos + 8 + len)..]);
                if (Crc32.Compute(_data.Slice(_pos + 4, len + 4)) != crc)
                    throw new InvalidDataException($"PNG: corrupt '{ChunkName(type)}' chunk (CRC mismatch).");
            }
            Current = new Chunk(type, _data.Slice(_pos + 8, len));
            _pos += 12 + len;
            return true;
        }
    }

    private static string ChunkName(uint type) =>
        new([(char)(type >> 24), (char)(type >> 16 & 0xFF), (char)(type >> 8 & 0xFF), (char)(type & 0xFF)]);

    // ───────────────────────────── writing ─────────────────────────────

    /// <summary>
    /// Writes <paramref name="image"/> as PNG. <paramref name="srgb"/> adds an
    /// <c>sRGB</c> chunk (display-referred output); data maps leave it out.
    /// </summary>
    public static void Encode(Stream stream, RasterImage image, bool srgb = false)
    {
        int ch = image.Channels, depth = image.BitDepth;
        int colorType = ch switch { 1 => 0, 2 => 4, 3 => 2, _ => 6 };
        int bpp = ch * depth / 8;
        int rowBytes = image.Width * bpp;

        stream.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)image.Height);
        ihdr[8] = (byte)depth;
        ihdr[9] = (byte)colorType;
        ihdr[10] = ihdr[11] = ihdr[12] = 0;
        WriteChunk(stream, IHDR, ihdr);
        if (srgb) WriteChunk(stream, SRGB, [0]); // perceptual rendering intent

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var prev = new byte[rowBytes];
            var cur = new byte[rowBytes];
            var best = new byte[rowBytes + 1];
            var trial = new byte[rowBytes + 1];
            var s = image.Samples;
            for (int y = 0; y < image.Height; y++)
            {
                int o = y * image.Width * ch;
                if (depth == 8)
                    for (int i = 0; i < rowBytes; i++) cur[i] = (byte)s[o + i];
                else
                    for (int i = 0; i < image.Width * ch; i++)
                    {
                        cur[2 * i] = (byte)(s[o + i] >> 8);
                        cur[2 * i + 1] = (byte)s[o + i];
                    }

                long bestScore = long.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    long score = Filter(cur, prev, f, bpp, trial);
                    if (score < bestScore) { bestScore = score; (best, trial) = (trial, best); }
                }
                z.Write(best);
                (prev, cur) = (cur, prev);
            }
        }
        // IDAT in chunks of at most 1 MiB, as encoders customarily do.
        var all = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        for (int i = 0; i < all.Length; i += 1 << 20)
            WriteChunk(stream, IDAT, all.Slice(i, Math.Min(1 << 20, all.Length - i)));
        WriteChunk(stream, IEND, ReadOnlySpan<byte>.Empty);
    }

    public static void Save(string path, RasterImage image, bool srgb = false)
    {
        using var f = File.Create(path);
        Encode(f, image, srgb);
    }

    // Filters the row into out[1..] with out[0] = filter type and returns the
    // sum of absolute values of the filtered bytes taken as signed.
    private static long Filter(ReadOnlySpan<byte> cur, ReadOnlySpan<byte> prev, int f, int bpp, Span<byte> output)
    {
        output[0] = (byte)f;
        long sum = 0;
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
            byte v = f switch
            {
                0 => cur[i],
                1 => (byte)(cur[i] - a),
                2 => (byte)(cur[i] - b),
                3 => (byte)(cur[i] - ((a + b) >> 1)),
                _ => (byte)(cur[i] - Paeth(a, b, c)),
            };
            output[i + 1] = v;
            int sv = (sbyte)v;
            sum += sv < 0 ? -sv : sv;
        }
        return sum;
    }

    private static void WriteChunk(Stream stream, uint type, ReadOnlySpan<byte> body)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, body.Length);
        BinaryPrimitives.WriteUInt32BigEndian(head[4..], type);
        stream.Write(head);
        stream.Write(body);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, head[4..]), body) ^ 0xFFFFFFFFu;
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        stream.Write(tail);
    }
}

/// <summary>CRC-32 (ISO 3309, polynomial 0xEDB88320) as PNG uses it.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;
}
