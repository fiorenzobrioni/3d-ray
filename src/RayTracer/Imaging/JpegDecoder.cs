namespace RayTracer.Imaging;

/// <summary>
/// JPEG reader (ITU-T T.81): baseline and extended sequential Huffman,
/// progressive Huffman (spectral selection and successive approximation),
/// 8-bit samples, any sampling factors, restart intervals. Grey, YCbCr, RGB
/// (Adobe transform 0) and Adobe CMYK/YCCK. Chroma is upsampled with a
/// centred bilinear filter (libjpeg's "fancy" upsampling at 2×) and converted
/// with the JFIF matrix. Lossless, hierarchical, arithmetic-coded and 12-bit
/// files are refused with a clear message.
/// </summary>
internal sealed class JpegDecoder
{
    // Natural (row-major) index of the k-th coefficient in zig-zag order, with
    // 16 extra entries so a corrupt run past 63 stays inside the array.
    private static readonly byte[] ZigZag =
    [
         0,  1,  8, 16,  9,  2,  3, 10, 17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63,
    ];

    // IDCT basis: Basis[x * 8 + u] = C(u)/2 · cos((2x + 1)uπ/16).
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

    private sealed class Component
    {
        public int Id, H, V, Tq;
        public int BlocksPerLine, BlocksPerColumn; // padded to whole MCUs
        public int UsedBlocksX, UsedBlocksY;       // blocks covering the component itself
        public int Width, Height;                  // component size in samples
        public short[] Coeffs = [];
        public int DcTable, AcTable, Pred;
    }

    private sealed class Huffman
    {
        public readonly byte[] FastLen = new byte[1 << FastBits];
        public readonly byte[] FastVal = new byte[1 << FastBits];
        public readonly int[] MaxCode = new int[18];
        public readonly int[] ValPtr = new int[17];
        public readonly int[] MinCode = new int[17];
        public byte[] Values = [];
        public const int FastBits = 9;

        public Huffman(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
        {
            Values = symbols.ToArray();
            int code = 0, k = 0;
            for (int len = 1; len <= 16; len++)
            {
                int n = counts[len - 1];
                ValPtr[len] = k;
                MinCode[len] = code;
                code += n;
                k += n;
                MaxCode[len] = n > 0 ? code - 1 : -1;
                if (code > 1 << len) throw new InvalidDataException("JPEG: invalid Huffman table.");
                code <<= 1;
            }
            MaxCode[17] = int.MaxValue;
            // Fast table for the codes up to FastBits long.
            for (int len = 1; len <= FastBits; len++)
                for (int i = 0; i < counts[len - 1]; i++)
                {
                    int c = MinCode[len] + i;
                    int first = c << (FastBits - len), n = 1 << (FastBits - len);
                    for (int j = 0; j < n; j++)
                    {
                        FastLen[first + j] = (byte)len;
                        FastVal[first + j] = Values[ValPtr[len] + i];
                    }
                }
        }
    }

    private readonly byte[] _data;
    private int _pos;
    private readonly ushort[]?[] _qt = new ushort[]?[4];
    private readonly Huffman?[] _dc = new Huffman?[4], _ac = new Huffman?[4];
    private readonly List<Component> _comps = new();
    private int _width, _height, _hmax, _vmax, _mcusX, _mcusY, _restartInterval;
    private bool _progressive, _jfif, _frameSeen;
    private int _adobeTransform = -1;
    private int _eobrun;

    // Entropy-coded bit reader.
    private uint _bits;
    private int _bitCount;
    private bool _marker;

    private JpegDecoder(byte[] data) => _data = data;

    public static bool IsJpeg(ReadOnlySpan<byte> d) => d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF;

    public static RasterImage Decode(byte[] data)
    {
        var dec = new JpegDecoder(data);
        dec.ReadAll(headerOnly: false);
        return dec.Output();
    }

    public static ImageInfo Identify(byte[] data)
    {
        var dec = new JpegDecoder(data);
        dec.ReadAll(headerOnly: true);
        return new ImageInfo(dec._width, dec._height, dec._comps.Count == 1 ? 1 : 3, 8);
    }

    // ───────────────────────────── markers ─────────────────────────────

    private void ReadAll(bool headerOnly)
    {
        if (!IsJpeg(_data)) throw new InvalidDataException("Not a JPEG file.");
        _pos = 2;
        while (true)
        {
            int m = NextMarker();
            if (m < 0 || m == 0xD9) break; // end of data or EOI
            if (m is >= 0xD0 and <= 0xD7 or 0x01) continue; // stray RST / TEM: no length
            int len = U16(_pos);
            if (len < 2 || _pos + len > _data.Length) throw new InvalidDataException("JPEG: truncated segment.");
            int body = _pos + 2, end = _pos + len;
            switch (m)
            {
                case 0xC0: case 0xC1: case 0xC2:
                    ReadFrame(body, end, progressive: m == 0xC2);
                    if (headerOnly) return;
                    break;
                case 0xC3: case 0xC5: case 0xC6: case 0xC7: case 0xC9: case 0xCA:
                case 0xCB: case 0xCD: case 0xCE: case 0xCF:
                    throw new InvalidDataException(
                        "JPEG: lossless, hierarchical and arithmetic-coded files are not supported.");
                case 0xC4: ReadHuffman(body, end); break;
                case 0xDB: ReadQuant(body, end); break;
                case 0xDD: _restartInterval = U16(body); break;
                case 0xE0:
                    if (end - body >= 5 && _data.AsSpan(body, 5).SequenceEqual("JFIF\0"u8)) _jfif = true;
                    break;
                case 0xEE:
                    if (end - body >= 12 && _data.AsSpan(body, 5).SequenceEqual("Adobe"u8))
                        _adobeTransform = _data[body + 11];
                    break;
                case 0xDA:
                    _pos = end;
                    ReadScan(body);
                    continue; // _pos now sits after the entropy-coded data
            }
            _pos = end;
        }
        if (!_frameSeen) throw new InvalidDataException("JPEG: no frame header.");
    }

    private int U16(int p) => _data[p] << 8 | _data[p + 1];

    /// <summary>The next marker code, skipping fill bytes; -1 at end of data.</summary>
    private int NextMarker()
    {
        while (_pos < _data.Length)
        {
            if (_data[_pos] != 0xFF) { _pos++; continue; }
            while (_pos < _data.Length && _data[_pos] == 0xFF) _pos++;
            if (_pos >= _data.Length) return -1;
            int m = _data[_pos++];
            if (m != 0) return m;
        }
        return -1;
    }

    private void ReadFrame(int p, int end, bool progressive)
    {
        if (_frameSeen) throw new InvalidDataException("JPEG: more than one frame.");
        _frameSeen = true;
        _progressive = progressive;
        if (_data[p] != 8) throw new InvalidDataException($"JPEG: {_data[p]}-bit samples are not supported.");
        _height = U16(p + 1);
        _width = U16(p + 3);
        int n = _data[p + 5];
        if (_width == 0 || _height == 0)
            throw new InvalidDataException("JPEG: zero image size (DNL marker) is not supported.");
        if (n is not (1 or 3 or 4) || p + 6 + 3 * n > end)
            throw new InvalidDataException($"JPEG: {n} components are not supported.");
        for (int i = 0; i < n; i++)
        {
            int q = p + 6 + 3 * i;
            var c = new Component { Id = _data[q], H = _data[q + 1] >> 4, V = _data[q + 1] & 15, Tq = _data[q + 2] & 3 };
            if (c.H is < 1 or > 4 || c.V is < 1 or > 4) throw new InvalidDataException("JPEG: invalid sampling factors.");
            _comps.Add(c);
        }
        _hmax = _comps.Max(c => c.H);
        _vmax = _comps.Max(c => c.V);
        _mcusX = (_width + 8 * _hmax - 1) / (8 * _hmax);
        _mcusY = (_height + 8 * _vmax - 1) / (8 * _vmax);
        foreach (var c in _comps)
        {
            c.BlocksPerLine = _mcusX * c.H;
            c.BlocksPerColumn = _mcusY * c.V;
            c.Width = (_width * c.H + _hmax - 1) / _hmax;
            c.Height = (_height * c.V + _vmax - 1) / _vmax;
            c.UsedBlocksX = (c.Width + 7) / 8;
            c.UsedBlocksY = (c.Height + 7) / 8;
        }
    }

    private void Allocate()
    {
        foreach (var c in _comps)
            if (c.Coeffs.Length == 0)
                c.Coeffs = new short[checked(c.BlocksPerLine * c.BlocksPerColumn * 64)];
    }

    private void ReadHuffman(int p, int end)
    {
        while (p < end)
        {
            int tc = _data[p] >> 4, th = _data[p] & 15;
            if (tc > 1 || th > 3) throw new InvalidDataException("JPEG: invalid Huffman table id.");
            var counts = _data.AsSpan(p + 1, 16);
            int total = 0;
            foreach (byte b in counts) total += b;
            if (p + 17 + total > end) throw new InvalidDataException("JPEG: truncated Huffman table.");
            var h = new Huffman(counts, _data.AsSpan(p + 17, total));
            if (tc == 0) _dc[th] = h; else _ac[th] = h;
            p += 17 + total;
        }
    }

    private void ReadQuant(int p, int end)
    {
        while (p < end)
        {
            int pq = _data[p] >> 4, tq = _data[p] & 15;
            if (tq > 3) throw new InvalidDataException("JPEG: invalid quantisation table id.");
            var table = new ushort[64];
            for (int k = 0; k < 64; k++)
                table[ZigZag[k]] = pq == 0 ? _data[p + 1 + k] : (ushort)U16(p + 1 + 2 * k);
            _qt[tq] = table;
            p += 1 + (pq == 0 ? 64 : 128);
        }
    }

    // ───────────────────────────── scans ─────────────────────────────

    private void ReadScan(int p)
    {
        if (!_frameSeen) throw new InvalidDataException("JPEG: scan before the frame header.");
        Allocate();
        int ns = _data[p];
        var comps = new Component[ns];
        for (int i = 0; i < ns; i++)
        {
            int id = _data[p + 1 + 2 * i], tables = _data[p + 2 + 2 * i];
            comps[i] = _comps.Find(c => c.Id == id) ?? throw new InvalidDataException("JPEG: scan names an unknown component.");
            comps[i].DcTable = tables >> 4 & 3;
            comps[i].AcTable = tables & 3;
        }
        int q = p + 1 + 2 * ns;
        int ss = _data[q], se = _data[q + 1], ah = _data[q + 2] >> 4, al = _data[q + 2] & 15;
        if (!_progressive) { ss = 0; se = 63; ah = al = 0; }
        else if (ss > se || se > 63 || (ss > 0 && ns != 1))
            throw new InvalidDataException("JPEG: invalid progressive scan.");

        foreach (var c in comps)
        {
            bool needDc = ss == 0 && ah == 0, needAc = se > 0;
            if (needDc && _dc[c.DcTable] == null) throw new InvalidDataException("JPEG: missing DC Huffman table.");
            if (needAc && _ac[c.AcTable] == null) throw new InvalidDataException("JPEG: missing AC Huffman table.");
            c.Pred = 0;
        }
        ResetBits();
        _eobrun = 0;

        int restartsLeft = _restartInterval;
        if (ns == 1)
        {
            var c = comps[0];
            int total = c.UsedBlocksX * c.UsedBlocksY;
            for (int i = 0; i < total; i++)
            {
                int bx = i % c.UsedBlocksX, by = i / c.UsedBlocksX;
                DecodeBlock(c, (by * c.BlocksPerLine + bx) * 64, ss, se, ah, al);
                if (_restartInterval > 0 && --restartsLeft == 0 && i + 1 < total)
                {
                    Restart(comps);
                    restartsLeft = _restartInterval;
                }
            }
        }
        else
        {
            int total = _mcusX * _mcusY;
            for (int m = 0; m < total; m++)
            {
                int mx = m % _mcusX, my = m / _mcusX;
                foreach (var c in comps)
                    for (int v = 0; v < c.V; v++)
                        for (int h = 0; h < c.H; h++)
                        {
                            int bx = mx * c.H + h, by = my * c.V + v;
                            DecodeBlock(c, (by * c.BlocksPerLine + bx) * 64, ss, se, ah, al);
                        }
                if (_restartInterval > 0 && --restartsLeft == 0 && m + 1 < total)
                {
                    Restart(comps);
                    restartsLeft = _restartInterval;
                }
            }
        }
        // Leave _pos on the marker that ends the entropy-coded data.
        if (!_marker) SkipToMarker();
    }

    private void Restart(Component[] comps)
    {
        ResetBits();
        SkipToMarker();
        if (_pos + 1 < _data.Length && _data[_pos + 1] is >= 0xD0 and <= 0xD7) _pos += 2;
        foreach (var c in comps) c.Pred = 0;
        _eobrun = 0;
    }

    private void SkipToMarker()
    {
        while (_pos + 1 < _data.Length && !(_data[_pos] == 0xFF && _data[_pos + 1] != 0x00 && _data[_pos + 1] != 0xFF))
            _pos++;
        if (_pos + 1 >= _data.Length) _pos = _data.Length;
    }

    private void DecodeBlock(Component c, int o, int ss, int se, int ah, int al)
    {
        var b = c.Coeffs.AsSpan(o, 64);
        if (!_progressive) { DecodeBaseline(c, b); return; }
        if (ss == 0)
        {
            if (ah == 0)
            {
                int t = Decode(_dc[c.DcTable]!);
                int diff = t == 0 ? 0 : Extend(Receive(t), t);
                c.Pred += diff;
                b[0] = (short)(c.Pred * (1 << al));
            }
            else if (Receive(1) != 0)
                b[0] |= (short)(1 << al);
            return;
        }
        if (ah == 0) DecodeAcFirst(c, b, ss, se, al);
        else DecodeAcRefine(c, b, ss, se, al);
    }

    private void DecodeBaseline(Component c, Span<short> b)
    {
        int t = Decode(_dc[c.DcTable]!);
        int diff = t == 0 ? 0 : Extend(Receive(t), t);
        c.Pred += diff;
        b[0] = (short)c.Pred;
        var ac = _ac[c.AcTable]!;
        for (int k = 1; k < 64;)
        {
            int rs = Decode(ac), r = rs >> 4, s = rs & 15;
            if (s == 0)
            {
                if (r != 15) break; // end of block
                k += 16;
                continue;
            }
            k += r;
            if (k > 63) break;
            b[ZigZag[k]] = (short)Extend(Receive(s), s);
            k++;
        }
    }

    private void DecodeAcFirst(Component c, Span<short> b, int ss, int se, int al)
    {
        if (_eobrun > 0) { _eobrun--; return; }
        var ac = _ac[c.AcTable]!;
        for (int k = ss; k <= se;)
        {
            int rs = Decode(ac), r = rs >> 4, s = rs & 15;
            if (s == 0)
            {
                if (r < 15)
                {
                    _eobrun = (1 << r) - 1;
                    if (r > 0) _eobrun += Receive(r);
                    break;
                }
                k += 16;
                continue;
            }
            k += r;
            if (k > 63) break;
            b[ZigZag[k]] = (short)(Extend(Receive(s), s) * (1 << al));
            k++;
        }
    }

    // Successive-approximation refinement of AC coefficients (T.81 G.1.2.3,
    // libjpeg's decode_mcu_AC_refine).
    private void DecodeAcRefine(Component c, Span<short> b, int ss, int se, int al)
    {
        int p1 = 1 << al, m1 = -1 << al;
        int k = ss;
        if (_eobrun == 0)
        {
            var ac = _ac[c.AcTable]!;
            for (; k <= se; k++)
            {
                int rs = Decode(ac), r = rs >> 4, s = rs & 15;
                int value = 0;
                if (s != 0)
                    value = Receive(1) != 0 ? p1 : m1;
                else if (r != 15)
                {
                    _eobrun = 1 << r;
                    if (r > 0) _eobrun += Receive(r);
                    break;
                }
                // Skip r zero coefficients (refining the non-zero ones met on
                // the way); the new coefficient, if any, lands on the next zero.
                while (k <= se)
                {
                    ref short coef = ref b[ZigZag[k]];
                    if (coef != 0)
                        Refine(ref coef, p1, m1);
                    else
                    {
                        if (r == 0) break;
                        r--;
                    }
                    k++;
                }
                if (value != 0 && k <= 63) b[ZigZag[k]] = (short)value;
            }
        }
        if (_eobrun > 0)
        {
            for (; k <= se; k++)
            {
                ref short coef = ref b[ZigZag[k]];
                if (coef != 0) Refine(ref coef, p1, m1);
            }
            _eobrun--;
        }
    }

    private void Refine(ref short coef, int p1, int m1)
    {
        if (Receive(1) != 0 && (coef & p1) == 0)
            coef = (short)(coef >= 0 ? coef + p1 : coef + m1);
    }

    // ─────────────────────────── bit reader ───────────────────────────

    private void ResetBits()
    {
        _bits = 0;
        _bitCount = 0;
        _marker = false;
    }

    private void Fill()
    {
        while (_bitCount <= 24)
        {
            int b = 0;
            if (!_marker && _pos < _data.Length)
            {
                b = _data[_pos];
                if (b == 0xFF)
                {
                    int next = _pos + 1 < _data.Length ? _data[_pos + 1] : 0xD9;
                    if (next == 0x00) _pos += 2;
                    else { _marker = true; b = 0; } // a marker ends the data: feed zeros
                }
                else _pos++;
            }
            else _marker = true;
            _bits |= (uint)b << (24 - _bitCount);
            _bitCount += 8;
        }
    }

    private int Receive(int n)
    {
        if (n == 0) return 0;
        if (_bitCount < n) Fill();
        int v = (int)(_bits >> (32 - n));
        _bits <<= n;
        _bitCount -= n;
        return v;
    }

    private static int Extend(int v, int n) => v < 1 << (n - 1) ? v - (1 << n) + 1 : v;

    private int Decode(Huffman h)
    {
        if (_bitCount < 16) Fill();
        int peek = (int)(_bits >> (32 - Huffman.FastBits));
        int len = h.FastLen[peek];
        if (len != 0)
        {
            _bits <<= len;
            _bitCount -= len;
            return h.FastVal[peek];
        }
        for (len = Huffman.FastBits + 1; len <= 16; len++)
        {
            int code = (int)(_bits >> (32 - len));
            if (code <= h.MaxCode[len])
            {
                _bits <<= len;
                _bitCount -= len;
                int idx = h.ValPtr[len] + code - h.MinCode[len];
                return idx < h.Values.Length ? h.Values[idx] : 0;
            }
        }
        // Corrupt data: consume a bit so decoding cannot stall.
        _bits <<= 1;
        _bitCount--;
        return 0;
    }

    // ─────────────────────────── reconstruction ───────────────────────────

    private RasterImage Output()
    {
        if (_comps.Count == 0) throw new InvalidDataException("JPEG: no frame header.");
        Allocate();
        // Each component to full resolution.
        var planes = new byte[_comps.Count][];
        for (int i = 0; i < _comps.Count; i++)
            planes[i] = Upsample(_comps[i], Reconstruct(_comps[i]));

        int n = _width * _height;
        int channels = _comps.Count == 1 ? 1 : 3;
        var image = new RasterImage(_width, _height, channels, 8);
        var s = image.Samples;
        if (channels == 1)
        {
            for (int i = 0; i < n; i++) s[i] = planes[0][i];
            return image;
        }

        bool ycc = _comps.Count == 3
            ? _adobeTransform != 0 && !(_adobeTransform < 0 && !_jfif
                                        && _comps[0].Id == 'R' && _comps[1].Id == 'G' && _comps[2].Id == 'B')
            : _adobeTransform == 2;
        for (int i = 0; i < n; i++)
        {
            float a = planes[0][i], b = planes[1][i], c = planes[2][i];
            float r, g, bl;
            if (ycc)
            {
                float cb = b - 128f, cr = c - 128f;
                r = a + 1.402f * cr;
                g = a - 0.344136f * cb - 0.714136f * cr;
                bl = a + 1.772f * cb;
            }
            else { r = a; g = b; bl = c; }
            if (_comps.Count == 4)
            {
                // Adobe CMYK is stored inverted (255 = no ink); YCCK carries
                // the inverted CMY through the YCbCr transform.
                float k = planes[3][i] / 255f;
                if (ycc) { r = 255f - r; g = 255f - g; bl = 255f - bl; }
                r = Math.Clamp(r, 0f, 255f) * k;
                g = Math.Clamp(g, 0f, 255f) * k;
                bl = Math.Clamp(bl, 0f, 255f) * k;
            }
            s[3 * i] = Clamp8(r);
            s[3 * i + 1] = Clamp8(g);
            s[3 * i + 2] = Clamp8(bl);
        }
        return image;
    }

    private static ushort Clamp8(float v) => (ushort)Math.Clamp((int)MathF.Round(v), 0, 255);

    /// <summary>Dequantises and inverse-transforms every block of the component
    /// into a plane of BlocksPerLine·8 × BlocksPerColumn·8 samples.</summary>
    private byte[] Reconstruct(Component c)
    {
        var q = _qt[c.Tq] ?? throw new InvalidDataException("JPEG: missing quantisation table.");
        int stride = c.BlocksPerLine * 8;
        var plane = new byte[stride * c.BlocksPerColumn * 8];
        Span<float> f = stackalloc float[64];
        Span<float> t = stackalloc float[64];
        var basis = Basis;
        for (int by = 0; by < c.UsedBlocksY; by++)
            for (int bx = 0; bx < c.UsedBlocksX; bx++)
            {
                var coef = c.Coeffs.AsSpan((by * c.BlocksPerLine + bx) * 64, 64);
                for (int i = 0; i < 64; i++) f[i] = coef[i] * q[i];
                // Rows: horizontal 1D IDCT of each row of frequencies.
                for (int v = 0; v < 8; v++)
                {
                    var row = f.Slice(v * 8, 8);
                    bool zero = true;
                    for (int u = 1; u < 8; u++) if (row[u] != 0f) { zero = false; break; }
                    if (zero)
                    {
                        float dc = row[0] * basis[0];
                        for (int x = 0; x < 8; x++) t[v * 8 + x] = dc;
                        continue;
                    }
                    for (int x = 0; x < 8; x++)
                    {
                        float sum = 0f;
                        for (int u = 0; u < 8; u++) sum += row[u] * basis[x * 8 + u];
                        t[v * 8 + x] = sum;
                    }
                }
                // Columns.
                int o = by * 8 * stride + bx * 8;
                for (int x = 0; x < 8; x++)
                    for (int y = 0; y < 8; y++)
                    {
                        float sum = 0f;
                        for (int v = 0; v < 8; v++) sum += t[v * 8 + x] * basis[y * 8 + v];
                        plane[o + y * stride + x] = (byte)Math.Clamp((int)MathF.Round(sum + 128f), 0, 255);
                    }
            }
        return plane;
    }

    /// <summary>The component at full image resolution: centred bilinear
    /// interpolation, edge samples replicated.</summary>
    private byte[] Upsample(Component c, byte[] plane)
    {
        int stride = c.BlocksPerLine * 8;
        var output = new byte[_width * _height];
        if (c.H == _hmax && c.V == _vmax)
        {
            for (int y = 0; y < _height; y++)
                Array.Copy(plane, y * stride, output, y * _width, _width);
            return output;
        }
        float sx = (float)c.H / _hmax, sy = (float)c.V / _vmax;
        var x0 = new int[_width];
        var x1 = new int[_width];
        var wx = new float[_width];
        for (int x = 0; x < _width; x++)
        {
            float fx = Math.Max((x + 0.5f) * sx - 0.5f, 0f);
            int i = (int)fx;
            x0[x] = Math.Min(i, c.Width - 1);
            x1[x] = Math.Min(i + 1, c.Width - 1);
            wx[x] = fx - i;
        }
        for (int y = 0; y < _height; y++)
        {
            float fy = Math.Max((y + 0.5f) * sy - 0.5f, 0f);
            int j = (int)fy;
            int r0 = Math.Min(j, c.Height - 1) * stride, r1 = Math.Min(j + 1, c.Height - 1) * stride;
            float wy = fy - j;
            for (int x = 0; x < _width; x++)
            {
                float top = plane[r0 + x0[x]] + (plane[r0 + x1[x]] - plane[r0 + x0[x]]) * wx[x];
                float bot = plane[r1 + x0[x]] + (plane[r1 + x1[x]] - plane[r1 + x0[x]]) * wx[x];
                output[y * _width + x] = (byte)Math.Clamp((int)MathF.Round(top + (bot - top) * wy), 0, 255);
            }
        }
        return output;
    }
}
