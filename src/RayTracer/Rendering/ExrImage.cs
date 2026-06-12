using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RayTracer.Rendering;

/// <summary>EXR channel pixel type; numeric values match the on-disk encoding.</summary>
public enum ExrPixelType
{
    Half = 1,
    Float = 2,
}

/// <summary>
/// Minimal OpenEXR writer for scene-linear HDR output. Produces single-part
/// scanline files (version 2, no flags) with ZIP compression — the lossless
/// half/float layout production pipelines exchange by default — readable by
/// any conformant OpenEXR implementation:
/// <list type="bullet">
///   <item><description>Arbitrary named channels, half or float per channel
///   (e.g. <c>R,G,B</c> beauty plus <c>albedo.R</c>… layers and a float
///   <c>Z</c> depth in one multilayer file).</description></item>
///   <item><description>ZIP compression (16-scanline blocks, zlib framing)
///   with the spec-mandated raw fallback when a block does not
///   shrink.</description></item>
///   <item><description>Rec.709 <c>chromaticities</c> so strict readers know
///   the primaries of the linear radiance.</description></item>
/// </list>
///
/// Half conversion clamps finite overflow to ±65504 (Half.MaxValue) instead
/// of producing ±Inf — variance spikes stay finite — and propagates NaN
/// unchanged; rounding is the IEEE round-to-nearest-even of the
/// <see cref="System.Half"/> cast.
///
/// <para>Spec reference: <c>openexr.com/en/latest/OpenEXRFileLayout.html</c>;
/// the ZIP pre-processing transform mirrors the reference
/// <c>ImfZip.cpp</c>.</para>
/// </summary>
public static class ExrImage
{
    private const int LinesPerBlock = 16;          // ZIP block height per spec
    private const float HalfMax = 65504f;          // largest finite Half

    /// <summary>One named channel plane (W·H floats, row 0 = top).</summary>
    public sealed record Channel(string Name, ExrPixelType Type, ReadOnlyMemory<float> Plane);

    /// <summary>
    /// Writes a single-part scanline OpenEXR v2 file (ZIP, increasing-Y).
    /// Channel planes are W·H floats with row 0 = top, matching
    /// <see cref="FrameBuffer"/> plane layout.
    /// </summary>
    public static void Write(string path, int width, int height, IReadOnlyList<Channel> channels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Invalid EXR dimensions {width}×{height}.");
        if (channels.Count == 0)
            throw new ArgumentException("EXR requires at least one channel.", nameof(channels));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in channels)
        {
            // 31 chars is the longest channel name readable without the
            // long-names flag (which older readers reject).
            if (string.IsNullOrEmpty(ch.Name) || ch.Name.Length > 31 ||
                ch.Name.Any(c => c <= ' ' || c > '~'))
                throw new ArgumentException($"Invalid EXR channel name '{ch.Name}'.", nameof(channels));
            if (!seen.Add(ch.Name))
                throw new ArgumentException($"Duplicate EXR channel name '{ch.Name}'.", nameof(channels));
            if (ch.Plane.Length != (long)width * height)
                throw new ArgumentException(
                    $"Channel '{ch.Name}' plane has {ch.Plane.Length} floats, expected {width * height}.",
                    nameof(channels));
        }

        // Scanline storage interleaves channels in ordinal name order; the
        // header chlist must list them in the same order.
        var sorted = channels.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
        int bytesPerPixel = sorted.Sum(BytesPerSample);
        int scanlineBytes = width * bytesPerPixel;

        // Each chunk record (y, dataSize, payload) is independent — assemble
        // and compress them in parallel, then stream out sequentially.
        int blockCount = (height + LinesPerBlock - 1) / LinesPerBlock;
        var chunks = new byte[blockCount][];
        Parallel.For(0, blockCount, b =>
        {
            int yStart = b * LinesPerBlock;
            int lines = Math.Min(LinesPerBlock, height - yStart);
            byte[] raw = new byte[lines * scanlineBytes];
            int pos = 0;
            for (int ly = 0; ly < lines; ly++)
            {
                int rowBase = (yStart + ly) * width;
                foreach (var ch in sorted)
                {
                    var plane = ch.Plane.Span;
                    if (ch.Type == ExrPixelType.Half)
                    {
                        for (int x = 0; x < width; x++, pos += 2)
                            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(pos), ToHalfBits(plane[rowBase + x]));
                    }
                    else
                    {
                        for (int x = 0; x < width; x++, pos += 4)
                            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(pos), plane[rowBase + x]);
                    }
                }
            }

            // Spec: readers detect an uncompressed block by dataSize equal to
            // the raw size, so fall back to raw whenever zlib does not shrink.
            byte[] zipped = CompressZip(raw);
            byte[] payload = zipped.Length < raw.Length ? zipped : raw;

            var chunk = new byte[8 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, yStart);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), payload.Length);
            payload.CopyTo(chunk, 8);
            chunks[b] = chunk;
        });

        byte[] header = BuildHeader(width, height, sorted);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(0x01312F76u);                       // magic
        w.Write(2);                                 // version 2, no flags
        w.Write(header);
        long offset = 8 + header.Length + blockCount * 8L;
        foreach (var chunk in chunks)
        {
            w.Write(offset);
            offset += chunk.Length;
        }
        foreach (var chunk in chunks)
            w.Write(chunk);
    }

    /// <summary>Convenience: a 3-channel linear buffer as half <c>R,G,B</c>.</summary>
    public static void WriteRgb(string path, FrameBuffer rgb)
    {
        if (rgb.Channels != 3)
            throw new ArgumentException($"WriteRgb expects a 3-channel buffer, got {rgb.Channels}.", nameof(rgb));
        int n = rgb.Width * rgb.Height;
        var data = rgb.Data.AsMemory();
        Write(path, rgb.Width, rgb.Height, new[]
        {
            new Channel("R", ExrPixelType.Half, data.Slice(0, n)),
            new Channel("G", ExrPixelType.Half, data.Slice(n, n)),
            new Channel("B", ExrPixelType.Half, data.Slice(2 * n, n)),
        });
    }

    // ────────────────────────────────────────────────────────────────────────
    // Header
    // ────────────────────────────────────────────────────────────────────────

    private static byte[] BuildHeader(int width, int height, Channel[] sorted)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII);

        // chlist: per channel (ordinal-sorted) name\0, int32 pixelType,
        // uint8 pLinear, 3 reserved bytes, int32 xSampling, int32 ySampling;
        // terminated by a single null byte.
        using var chMs = new MemoryStream();
        using (var chBw = new BinaryWriter(chMs, Encoding.ASCII, leaveOpen: true))
        {
            foreach (var ch in sorted)
            {
                WriteNullTerminated(chBw, ch.Name);
                chBw.Write((int)ch.Type);
                chBw.Write((byte)0);                            // pLinear
                chBw.Write((byte)0); chBw.Write((byte)0); chBw.Write((byte)0);
                chBw.Write(1);                                  // xSampling
                chBw.Write(1);                                  // ySampling
            }
            chBw.Write((byte)0);
        }

        // Rec.709 primaries + D65 white point: the renderer's radiance is
        // Rec.709-linear, and stating it keeps strict readers from guessing.
        using var crMs = new MemoryStream();
        using (var crBw = new BinaryWriter(crMs, Encoding.ASCII, leaveOpen: true))
        {
            foreach (float f in new[] { 0.6400f, 0.3300f, 0.3000f, 0.6000f,
                                        0.1500f, 0.0600f, 0.3127f, 0.3290f })
                crBw.Write(f);
        }

        using var boxMs = new MemoryStream();
        using (var boxBw = new BinaryWriter(boxMs, Encoding.ASCII, leaveOpen: true))
        {
            boxBw.Write(0); boxBw.Write(0);
            boxBw.Write(width - 1); boxBw.Write(height - 1);
        }
        byte[] box = boxMs.ToArray();

        // Required attributes in alphabetical order (OpenEXR convention).
        WriteAttribute(bw, "channels", "chlist", chMs.ToArray());
        WriteAttribute(bw, "chromaticities", "chromaticities", crMs.ToArray());
        WriteAttribute(bw, "compression", "compression", new byte[] { 3 });    // ZIP
        WriteAttribute(bw, "dataWindow", "box2i", box);
        WriteAttribute(bw, "displayWindow", "box2i", box);
        WriteAttribute(bw, "lineOrder", "lineOrder", new byte[] { 0 });        // INCREASING_Y
        WriteAttribute(bw, "pixelAspectRatio", "float", BitConverter.GetBytes(1.0f));
        WriteAttribute(bw, "screenWindowCenter", "v2f", new byte[8]);          // (0, 0)
        WriteAttribute(bw, "screenWindowWidth", "float", BitConverter.GetBytes(1.0f));
        bw.Write((byte)0);                                                     // end of header

        return ms.ToArray();
    }

    private static void WriteAttribute(BinaryWriter bw, string name, string type, byte[] payload)
    {
        WriteNullTerminated(bw, name);
        WriteNullTerminated(bw, type);
        bw.Write(payload.Length);
        bw.Write(payload);
    }

    private static void WriteNullTerminated(BinaryWriter bw, string s)
    {
        bw.Write(Encoding.ASCII.GetBytes(s));
        bw.Write((byte)0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Pixel encoding
    // ────────────────────────────────────────────────────────────────────────

    private static int BytesPerSample(Channel c) => c.Type == ExrPixelType.Half ? 2 : 4;

    private static ushort ToHalfBits(float v) =>
        // Math.Clamp maps ±Inf to ±HalfMax and passes NaN through unchanged.
        BitConverter.HalfToUInt16Bits((Half)Math.Clamp(v, -HalfMax, HalfMax));

    // ────────────────────────────────────────────────────────────────────────
    // ZIP block transform (reference ImfZip.cpp); internal so the loader can
    // reuse the inverse and tests can pin both against an independent oracle.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-deflate transform: split reorder (even-index bytes → first half,
    /// odd → second half) THEN delta predictor over the whole reordered
    /// buffer (each byte becomes <c>cur − prevOriginal + 128</c>).
    /// </summary>
    internal static byte[] ZipDeconstruct(ReadOnlySpan<byte> raw)
    {
        var t = new byte[raw.Length];
        int half = (raw.Length + 1) / 2;
        int i1 = 0, i2 = half;
        for (int s = 0; s < raw.Length;)
        {
            t[i1++] = raw[s++];
            if (s < raw.Length) t[i2++] = raw[s++];
        }
        int prev = t.Length > 0 ? t[0] : 0;
        for (int i = 1; i < t.Length; i++)
        {
            int cur = t[i];
            t[i] = (byte)(cur - prev + 128);
            prev = cur;
        }
        return t;
    }

    /// <summary>
    /// Post-inflate inverse: predictor undo (prefix sum
    /// <c>t[i] += t[i−1] − 128</c>) THEN interleave the two halves.
    /// </summary>
    internal static byte[] ZipReconstruct(ReadOnlySpan<byte> encoded)
    {
        var t = encoded.ToArray();
        for (int i = 1; i < t.Length; i++)
            t[i] = (byte)(t[i - 1] + t[i] - 128);

        var raw = new byte[t.Length];
        int half = (t.Length + 1) / 2;
        int i1 = 0, i2 = half;
        for (int s = 0; s < raw.Length;)
        {
            raw[s++] = t[i1++];
            if (s < raw.Length) raw[s++] = t[i2++];
        }
        return raw;
    }

    private static byte[] CompressZip(byte[] raw)
    {
        byte[] pre = ZipDeconstruct(raw);
        using var ms = new MemoryStream();
        // ZLibStream emits the RFC 1950 header + adler32 external readers
        // require (a bare deflate stream would not be a valid EXR ZIP block).
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(pre);
        return ms.ToArray();
    }
}
