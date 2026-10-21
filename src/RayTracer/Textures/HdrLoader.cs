using System.Numerics;
using System.Text;

namespace RayTracer.Textures;

/// <summary>
/// Parser for Radiance HDR (.hdr / .pic) image files.
///
/// The Radiance RGBE format stores each pixel as 4 bytes (R, G, B, E) where
/// E is a shared exponent. This allows high dynamic range (HDR) values to be
/// stored efficiently. The file may use run-length encoding (RLE) for compression.
///
/// Reference: Greg Ward, "Real Pixels" — The Radiance Picture File Format.
/// </summary>
public static class HdrLoader
{
    // RGBE → float scale lookup. The exponent byte takes only 256 possible
    // values, so the per-pixel MathF.Pow(2, e - 136) collapses to a 1 KB
    // table indexed by `e`. e == 0 is reserved for "all-black pixel".
    private static readonly float[] _rgbeScale = BuildRgbeScale();

    private static float[] BuildRgbeScale()
    {
        var t = new float[256];
        t[0] = 0f;   // black, handled by the decode loop
        for (int e = 1; e < 256; e++)
            t[e] = MathF.Pow(2f, e - 128 - 8);
        return t;
    }

    /// <summary>
    /// Loads a Radiance .hdr file and returns the pixel data as a flat float
    /// array in row-major order: [R0, G0, B0, R1, G1, B1, ...].
    /// Pixel values are in linear HDR space (may exceed 1.0).
    /// </summary>
    public static (float[] Pixels, int Width, int Height) Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        // ── Parse header ────────────────────────────────────────────────
        ReadHeader(reader);

        // ── Parse resolution string ─────────────────────────────────────
        string resLine = ReadLine(reader);
        var (width, height, bottomUp, rightToLeft) = ParseResolution(resLine);

        // ── Read pixel data ─────────────────────────────────────────────
        var pixels = new float[width * height * 3];
        var scanline = new byte[width * 4]; // RGBE for one scanline

        for (int y = 0; y < height; y++)
        {
            ReadScanline(reader, scanline, width);

            // Rows are returned top first, columns left first, whatever order
            // the file stores them in.
            int row = bottomUp ? height - 1 - y : y;
            int pixelBase = row * width * 3;
            for (int x = 0; x < width; x++)
            {
                byte r = scanline[x * 4];
                byte g = scanline[x * 4 + 1];
                byte b = scanline[x * 4 + 2];
                byte e = scanline[x * 4 + 3];

                int idx = pixelBase + (rightToLeft ? width - 1 - x : x) * 3;
                // RGBE → float: (mantissa + 0.5) · 2^(exponent - 136), the
                // Radiance decode: writers truncate the mantissa, so the
                // centre of its interval is the unbiased value (without the
                // half, an HDRI reads 0.2-0.8% dark). e == 0 is black.
                if (e == 0)
                {
                    pixels[idx] = pixels[idx + 1] = pixels[idx + 2] = 0f;
                    continue;
                }
                float scale = _rgbeScale[e];
                pixels[idx]     = (r + 0.5f) * scale;
                pixels[idx + 1] = (g + 0.5f) * scale;
                pixels[idx + 2] = (b + 0.5f) * scale;
            }
        }

        return (pixels, width, height);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Header parsing
    // ─────────────────────────────────────────────────────────────────────────

    private static void ReadHeader(BinaryReader reader)
    {
        // First line must contain the magic "#?" signature
        string firstLine = ReadLine(reader);
        if (!firstLine.StartsWith("#?"))
            throw new FormatException($"Not a Radiance HDR file (magic: '{firstLine}')");

        // Read header lines until empty line
        while (true)
        {
            string line = ReadLine(reader);
            if (string.IsNullOrWhiteSpace(line))
                break;
            // We could parse FORMAT=32-bit_rle_rgbe here, but we accept any RGBE
        }
    }

    private static (int Width, int Height, bool BottomUp, bool RightToLeft) ParseResolution(string resLine)
    {
        // "-Y <height> +X <width>": rows top to bottom, each left to right
        // (the standard layout). +Y stores the rows bottom first, -X each row
        // right to left. A line that names X first stores columns, not rows.
        var parts = resLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || parts[0] is not ("-Y" or "+Y") || parts[2] is not ("+X" or "-X")
            || !int.TryParse(parts[1], out int height) || !int.TryParse(parts[3], out int width))
        {
            if (parts.Length == 4 && parts[0] is ("-X" or "+X"))
                throw new NotSupportedException($"Radiance HDR: column-major layout '{resLine}' not supported.");
            throw new FormatException($"Invalid resolution line: '{resLine}'");
        }

        if (width <= 0 || height <= 0)
            throw new FormatException($"Invalid resolution: {width}x{height} from '{resLine}'");

        return (width, height, parts[0] == "+Y", parts[2] == "-X");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Scanline reading (handles both old and new RLE formats)
    // ─────────────────────────────────────────────────────────────────────────

    private static void ReadScanline(BinaryReader reader, byte[] scanline, int width)
    {
        if (width < 8 || width > 0x7FFF)
        {
            // Old format: no RLE, read raw RGBE
            ReadRawScanline(reader, scanline, width);
            return;
        }

        // Peek at the first 4 bytes to determine format
        byte b0 = reader.ReadByte();
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();

        // New RLE format: starts with [2, 2, width_hi, width_lo]
        if (b0 == 2 && b1 == 2 && ((b2 << 8) | b3) == width)
        {
            ReadNewRleScanline(reader, scanline, width);
        }
        else
        {
            // Not new RLE — treat these 4 bytes as the first pixel and read the rest raw
            scanline[0] = b0;
            scanline[1] = b1;
            scanline[2] = b2;
            scanline[3] = b3;

            // Check for old-style RLE (R=1, G=1, B=1 means RLE repeat)
            if (b0 == 1 && b1 == 1 && b2 == 1)
            {
                ReadOldRleScanline(reader, scanline, width);
            }
            else
            {
                // Plain uncompressed — read remaining pixels
                for (int x = 1; x < width; x++)
                {
                    scanline[x * 4]     = reader.ReadByte();
                    scanline[x * 4 + 1] = reader.ReadByte();
                    scanline[x * 4 + 2] = reader.ReadByte();
                    scanline[x * 4 + 3] = reader.ReadByte();
                }
            }
        }
    }

    /// <summary>
    /// New-style adaptive RLE: each channel is encoded separately.
    /// For each of the 4 channels (R, G, B, E), read the channel data
    /// for the entire scanline using run-length encoding.
    /// </summary>
    private static void ReadNewRleScanline(BinaryReader reader, byte[] scanline, int width)
    {
        // Temporary buffer: channels stored separately [R0..Rn, G0..Gn, B0..Bn, E0..En]
        var channels = new byte[width * 4];

        for (int ch = 0; ch < 4; ch++)
        {
            int offset = ch * width;
            int x = 0;
            while (x < width)
            {
                byte code = reader.ReadByte();
                if (code > 128)
                {
                    // Run: repeat next byte (code - 128) times
                    int count = code - 128;
                    byte val = reader.ReadByte();
                    for (int i = 0; i < count && x < width; i++, x++)
                        channels[offset + x] = val;
                }
                else
                {
                    // Literal: read 'code' bytes
                    int count = code;
                    for (int i = 0; i < count && x < width; i++, x++)
                        channels[offset + x] = reader.ReadByte();
                }
            }
        }

        // Interleave channels back into RGBE pixel format
        for (int x = 0; x < width; x++)
        {
            scanline[x * 4]     = channels[x];               // R
            scanline[x * 4 + 1] = channels[width + x];       // G
            scanline[x * 4 + 2] = channels[width * 2 + x];   // B
            scanline[x * 4 + 3] = channels[width * 3 + x];   // E
        }
    }

    /// <summary>
    /// Old-style RLE: pixel [1,1,1,count] means repeat previous pixel 'count' times.
    /// </summary>
    private static void ReadOldRleScanline(BinaryReader reader, byte[] scanline, int width)
    {
        // First pixel already read (it's [1,1,1,E] which is an RLE marker)
        // For simplicity, treat the first pixel as the repeat source
        int x = 1;
        int repeatCount = scanline[3]; // E byte = count

        // Fill with previous pixel (but we don't have one — use black)
        for (int i = 0; i < repeatCount - 1 && x < width; i++, x++)
        {
            scanline[x * 4] = scanline[x * 4 + 1] = scanline[x * 4 + 2] = scanline[x * 4 + 3] = 0;
        }

        while (x < width)
        {
            byte r = reader.ReadByte();
            byte g = reader.ReadByte();
            byte b = reader.ReadByte();
            byte e = reader.ReadByte();

            if (r == 1 && g == 1 && b == 1)
            {
                // Repeat previous pixel 'e' times
                int prev = (x - 1) * 4;
                for (int i = 0; i < e && x < width; i++, x++)
                {
                    scanline[x * 4]     = scanline[prev];
                    scanline[x * 4 + 1] = scanline[prev + 1];
                    scanline[x * 4 + 2] = scanline[prev + 2];
                    scanline[x * 4 + 3] = scanline[prev + 3];
                }
            }
            else
            {
                scanline[x * 4]     = r;
                scanline[x * 4 + 1] = g;
                scanline[x * 4 + 2] = b;
                scanline[x * 4 + 3] = e;
                x++;
            }
        }
    }

    private static void ReadRawScanline(BinaryReader reader, byte[] scanline, int width)
    {
        for (int x = 0; x < width; x++)
        {
            scanline[x * 4]     = reader.ReadByte();
            scanline[x * 4 + 1] = reader.ReadByte();
            scanline[x * 4 + 2] = reader.ReadByte();
            scanline[x * 4 + 3] = reader.ReadByte();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static string ReadLine(BinaryReader reader)
    {
        var sb = new StringBuilder(128);
        while (true)
        {
            int b = reader.BaseStream.ReadByte();
            if (b < 0 || b == '\n') break;
            if (b == '\r') continue;
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}
