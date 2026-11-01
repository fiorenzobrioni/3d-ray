namespace RayTracer.Imaging;

/// <summary>
/// Baseline JPEG writer (ITU-T T.81, JFIF 1.01): grey or YCbCr without chroma
/// subsampling (4:4:4: a render's coloured edges stay sharp), the Annex K
/// quantisation tables scaled by the IJG quality formula and the Annex K
/// Huffman tables.
/// </summary>
internal static class JpegEncoder
{
    private static readonly byte[] ZigZag =
    [
         0,  1,  8, 16,  9,  2,  3, 10, 17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // Annex K.1, natural order.
    private static readonly byte[] LumaQuant =
    [
        16, 11, 10, 16, 24, 40, 51, 61,   12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,   14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static readonly byte[] ChromaQuant =
    [
        17, 18, 24, 47, 99, 99, 99, 99,   18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,   47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,   99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,   99, 99, 99, 99, 99, 99, 99, 99,
    ];

    // Annex K.3: code-length counts (1..16 bits) and symbols.
    private static readonly byte[] DcLumaBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DcChromaBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    private static readonly byte[] DcValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static readonly byte[] AcLumaBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d];
    private static readonly byte[] AcLumaValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
        0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
        0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa,
    ];

    private static readonly byte[] AcChromaBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];
    private static readonly byte[] AcChromaValues =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
        0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
        0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
        0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
        0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
        0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa,
    ];

    // FDCT basis, the same as the decoder's: C(u)/2 · cos((2x + 1)uπ/16).
    private static readonly float[] Basis = BuildBasis();

    private static float[] BuildBasis()
    {
        var b = new float[64];
        for (int x = 0; x < 8; x++)
            for (int u = 0; u < 8; u++)
                b[x * 8 + u] = (float)((u == 0 ? Math.Sqrt(0.5) : 1.0) * 0.5
                                       * Math.Cos((2 * x + 1) * u * Math.PI / 16));
        return b;
    }

    /// <summary>Huffman code and length per symbol, from Annex C.</summary>
    internal readonly struct Codes
    {
        public readonly ushort[] Code;
        public readonly byte[] Length;

        public Codes(byte[] bits, byte[] values)
        {
            Code = new ushort[256];
            Length = new byte[256];
            int code = 0, k = 0;
            for (int len = 1; len <= 16; len++)
            {
                for (int i = 0; i < bits[len - 1]; i++)
                {
                    Code[values[k]] = (ushort)code;
                    Length[values[k]] = (byte)len;
                    code++;
                    k++;
                }
                code <<= 1;
            }
        }
    }

    internal static readonly Codes DcLuma = new(DcLumaBits, DcValues), DcChroma = new(DcChromaBits, DcValues),
                                   AcLuma = new(AcLumaBits, AcLumaValues), AcChroma = new(AcChromaBits, AcChromaValues);

    /// <summary>Writes an 8-bit image (alpha dropped) at quality 1-100.</summary>
    public static void Encode(Stream stream, RasterImage image, int quality = 95)
    {
        if (image.BitDepth != 8) throw new ArgumentException("JPEG holds 8-bit samples only.");
        quality = Math.Clamp(quality, 1, 100);
        int scale = quality < 50 ? 5000 / quality : 200 - 2 * quality;
        var qLuma = ScaleQuant(LumaQuant, scale);
        var qChroma = ScaleQuant(ChromaQuant, scale);
        bool grey = image.IsGrey;
        int nc = grey ? 1 : 3;

        var w = new BitWriter(stream);
        w.Bytes(0xFF, 0xD8);
        // APP0 JFIF 1.01, aspect ratio 1:1.
        w.Bytes(0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0);
        WriteQuant(w, 0, qLuma);
        if (!grey) WriteQuant(w, 1, qChroma);
        // SOF0.
        w.Bytes(0xFF, 0xC0, 0, (byte)(8 + 3 * nc), 8,
                (byte)(image.Height >> 8), (byte)image.Height, (byte)(image.Width >> 8), (byte)image.Width, (byte)nc);
        for (int c = 0; c < nc; c++) w.Bytes((byte)(c + 1), 0x11, (byte)(c == 0 ? 0 : 1));
        WriteHuffman(w, 0x00, DcLumaBits, DcValues);
        WriteHuffman(w, 0x10, AcLumaBits, AcLumaValues);
        if (!grey)
        {
            WriteHuffman(w, 0x01, DcChromaBits, DcValues);
            WriteHuffman(w, 0x11, AcChromaBits, AcChromaValues);
        }
        // SOS.
        w.Bytes(0xFF, 0xDA, 0, (byte)(6 + 2 * nc), (byte)nc);
        for (int c = 0; c < nc; c++) w.Bytes((byte)(c + 1), (byte)(c == 0 ? 0x00 : 0x11));
        w.Bytes(0, 63, 0);

        Span<float> block = stackalloc float[64];
        Span<int> pred = stackalloc int[3];
        int bw = (image.Width + 7) / 8, bh = (image.Height + 7) / 8;
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
                for (int c = 0; c < nc; c++)
                {
                    LoadBlock(image, bx, by, c, grey, block);
                    EncodeBlock(w, block, c == 0 ? qLuma : qChroma, ref pred[c],
                                c == 0 ? DcLuma : DcChroma, c == 0 ? AcLuma : AcChroma);
                }
        w.Flush();
        w.Bytes(0xFF, 0xD9);
    }

    private static byte[] ScaleQuant(byte[] table, int scale)
    {
        var q = new byte[64];
        for (int i = 0; i < 64; i++) q[i] = (byte)Math.Clamp((table[i] * scale + 50) / 100, 1, 255);
        return q;
    }

    private static void WriteQuant(BitWriter w, int id, byte[] q)
    {
        w.Bytes(0xFF, 0xDB, 0, 67, (byte)id);
        for (int k = 0; k < 64; k++) w.Bytes(q[ZigZag[k]]);
    }

    private static void WriteHuffman(BitWriter w, byte classAndId, byte[] bits, byte[] values)
    {
        int len = 3 + 16 + values.Length;
        w.Bytes(0xFF, 0xC4, (byte)(len >> 8), (byte)len, classAndId);
        w.Bytes(bits);
        w.Bytes(values);
    }

    // The 8×8 block of channel c (level-shifted luma or chroma), edges replicated.
    private static void LoadBlock(RasterImage img, int bx, int by, int c, bool grey, Span<float> block)
    {
        var s = img.Samples;
        int ch = img.Channels;
        for (int y = 0; y < 8; y++)
        {
            int py = Math.Min(by * 8 + y, img.Height - 1);
            for (int x = 0; x < 8; x++)
            {
                int px = Math.Min(bx * 8 + x, img.Width - 1);
                int i = (py * img.Width + px) * ch;
                float v;
                if (grey) v = s[i];
                else
                {
                    float r = s[i], g = s[i + 1], b = s[i + 2];
                    v = c switch
                    {
                        0 => 0.299f * r + 0.587f * g + 0.114f * b,
                        1 => -0.168736f * r - 0.331264f * g + 0.5f * b + 128f,
                        _ => 0.5f * r - 0.418688f * g - 0.081312f * b + 128f,
                    };
                }
                block[y * 8 + x] = v - 128f;
            }
        }
    }

    private static void EncodeBlock(BitWriter w, Span<float> block, byte[] q, ref int pred, Codes dc, Codes ac)
    {
        // Forward DCT, rows then columns.
        Span<float> t = stackalloc float[64];
        for (int y = 0; y < 8; y++)
            for (int u = 0; u < 8; u++)
            {
                float sum = 0f;
                for (int x = 0; x < 8; x++) sum += block[y * 8 + x] * Basis[x * 8 + u];
                t[y * 8 + u] = sum;
            }
        Span<int> coef = stackalloc int[64];
        for (int u = 0; u < 8; u++)
            for (int v = 0; v < 8; v++)
            {
                float sum = 0f;
                for (int y = 0; y < 8; y++) sum += t[y * 8 + u] * Basis[y * 8 + v];
                int i = v * 8 + u;
                coef[i] = (int)MathF.Round(sum / q[i]);
            }

        int diff = coef[0] - pred;
        pred = coef[0];
        int size = Magnitude(diff);
        w.Bits(dc.Code[size], dc.Length[size]);
        if (size > 0) w.Bits(ValueBits(diff, size), size);

        int run = 0;
        for (int k = 1; k < 64; k++)
        {
            int v = coef[ZigZag[k]];
            if (v == 0) { run++; continue; }
            while (run > 15)
            {
                w.Bits(ac.Code[0xF0], ac.Length[0xF0]);
                run -= 16;
            }
            int s = Magnitude(v);
            int sym = run << 4 | s;
            w.Bits(ac.Code[sym], ac.Length[sym]);
            w.Bits(ValueBits(v, s), s);
            run = 0;
        }
        if (run > 0) w.Bits(ac.Code[0x00], ac.Length[0x00]);
    }

    private static int Magnitude(int v)
    {
        v = Math.Abs(v);
        int n = 0;
        while (v > 0) { n++; v >>= 1; }
        return n;
    }

    // A negative value is sent as v − 1 in its low bits (one's complement).
    private static int ValueBits(int v, int size) => (v < 0 ? v - 1 : v) & ((1 << size) - 1);

    private sealed class BitWriter(Stream stream)
    {
        private uint _acc;
        private int _count;

        public void Bytes(params ReadOnlySpan<byte> bytes) => stream.Write(bytes);

        public void Bits(int code, int length)
        {
            _acc = _acc << length | (uint)code & ((1u << length) - 1);
            _count += length;
            while (_count >= 8)
            {
                byte b = (byte)(_acc >> (_count - 8));
                stream.WriteByte(b);
                if (b == 0xFF) stream.WriteByte(0); // byte stuffing
                _count -= 8;
            }
        }

        /// <summary>Pads the last byte with 1 bits.</summary>
        public void Flush()
        {
            if (_count > 0) Bits((1 << (8 - _count)) - 1, 8 - _count);
        }
    }
}
