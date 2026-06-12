using System.IO.Compression;
using System.Text;
using RayTracer.Rendering;

namespace RayTracer.Textures;

/// <summary>
/// Minimal OpenEXR loader. Supports the subset of the format that covers the
/// overwhelming majority of artist-authored HDRIs and everything
/// <see cref="ExrImage"/> writes:
/// <list type="bullet">
///   <item><description>Scanline-stored images (the most common variant).</description></item>
///   <item><description>Compression: <c>NO_COMPRESSION</c>, <c>ZIP</c>, <c>ZIPS</c>,
///   including the spec's raw-block fallback (a block whose stored size equals
///   its uncompressed size is stored raw).</description></item>
///   <item><description>Float32 and half-float channels, any names —
///   <see cref="LoadChannels"/> returns every channel plane unclamped, so
///   multilayer AOV files round-trip exactly.</description></item>
/// </list>
///
/// Tiles, deep data, multi-part files, PIZ / DWAA / DWAB / B44 / PXR24
/// compression, and chroma-subsampled Y/RY/BY layouts are <b>not</b>
/// supported. Files using them produce a clear exception so the caller can
/// fall back to a sentinel HDRI rather than ship corrupt pixels.
///
/// <para>Spec reference: <c>openexr.com/en/latest/OpenEXRFileLayout.html</c>.</para>
/// </summary>
public static class ExrLoader
{
    /// <summary>One decoded channel plane (W·H floats, row 0 = top), unclamped.</summary>
    public sealed record ExrChannelData(string Name, ExrPixelType Type, float[] Plane);

    /// <summary>
    /// Loads an RGB EXR as interleaved pixels for use as an environment map.
    /// Negative samples are clamped to zero — radiance below zero is sensor /
    /// encoder noise an HDRI should never contribute. For an exact, unclamped
    /// read of arbitrary channels use <see cref="LoadChannels"/>.
    /// </summary>
    public static (float[] Pixels, int Width, int Height) Load(string path)
    {
        var (channels, width, height) = LoadChannels(path);

        var chR = channels.FirstOrDefault(c => c.Name == "R");
        var chG = channels.FirstOrDefault(c => c.Name == "G");
        var chB = channels.FirstOrDefault(c => c.Name == "B");
        if (chR is null || chG is null || chB is null)
            throw new InvalidDataException("EXR: missing R/G/B channels (only RGB EXR supported).");

        int planeSize = width * height;
        var pixels = new float[planeSize * 3];
        for (int c = 0; c < 3; c++)
        {
            float[] plane = (c == 0 ? chR : c == 1 ? chG : chB).Plane;
            for (int i = 0; i < planeSize; i++)
                pixels[i * 3 + c] = MathF.Max(0f, plane[i]);
        }
        return (pixels, width, height);
    }

    /// <summary>
    /// Decodes every channel of a scanline EXR into per-channel planes
    /// (<c>Plane[y·W + x]</c>, row 0 = top), in ordinal name order, with no
    /// value clamping (negative normal components, depth sentinels and HDR
    /// radiance all survive bit-exactly for float channels).
    /// </summary>
    public static (List<ExrChannelData> Channels, int Width, int Height) LoadChannels(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        // ── Magic + version ─────────────────────────────────────────────────
        uint magic = br.ReadUInt32();
        if (magic != 0x01312F76u)
            throw new InvalidDataException($"EXR: bad magic 0x{magic:X8} in {path}");
        int versionAndFlags = br.ReadInt32();
        int version = versionAndFlags & 0xFF;
        int flags = versionAndFlags & ~0xFF;
        if (version != 2)
            throw new InvalidDataException($"EXR: unsupported version {version}");
        // Reject tile / deep / multipart flags (0x200 tiled, 0x800 deep, 0x1000 multipart)
        if ((flags & 0x200) != 0)  throw new NotSupportedException("EXR: tiled images not supported");
        if ((flags & 0x800) != 0)  throw new NotSupportedException("EXR: deep images not supported");
        if ((flags & 0x1000) != 0) throw new NotSupportedException("EXR: multi-part files not supported");

        // ── Header attributes ───────────────────────────────────────────────
        var channels = new List<ExrChannel>();
        int dataWindowMinX = 0, dataWindowMinY = 0;
        int dataWindowMaxX = 0, dataWindowMaxY = 0;
        ExrCompression compression = ExrCompression.None;

        while (true)
        {
            string? name = ReadNullString(br);
            if (string.IsNullOrEmpty(name)) break;   // end-of-header
            string type = ReadNullString(br) ?? "";
            int size = br.ReadInt32();
            byte[] payload = br.ReadBytes(size);

            switch (name)
            {
                case "channels":
                    channels = ParseChannels(payload);
                    break;
                case "dataWindow":
                    dataWindowMinX = BitConverter.ToInt32(payload, 0);
                    dataWindowMinY = BitConverter.ToInt32(payload, 4);
                    dataWindowMaxX = BitConverter.ToInt32(payload, 8);
                    dataWindowMaxY = BitConverter.ToInt32(payload, 12);
                    break;
                case "compression":
                    compression = (ExrCompression)payload[0];
                    break;
                // Other attributes (displayWindow, pixelAspectRatio, ...) are tolerated.
            }
        }

        if (compression != ExrCompression.None &&
            compression != ExrCompression.Zips  &&
            compression != ExrCompression.Zip)
        {
            throw new NotSupportedException(
                $"EXR: compression {compression} not supported (only None / ZIP / ZIPS).");
        }

        int width  = dataWindowMaxX - dataWindowMinX + 1;
        int height = dataWindowMaxY - dataWindowMinY + 1;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("EXR: invalid data window.");
        if (channels.Count == 0)
            throw new InvalidDataException("EXR: no channels declared.");

        foreach (var ch in channels)
        {
            if (ch.PixelType != ExrPixelType.Float && ch.PixelType != ExrPixelType.Half)
                throw new NotSupportedException($"EXR: pixel type of channel '{ch.Name}' not supported (only half / float).");
            if (ch.XSampling != 1 || ch.YSampling != 1)
                throw new NotSupportedException("EXR: chroma-subsampled channels not supported.");
        }

        // ── Scanline offset table ───────────────────────────────────────────
        // The offset table is `height` entries when ZIPS / NoCompression, or
        // `ceil(height / linesPerBlock)` when ZIP (16 lines/block).
        int linesPerBlock = compression == ExrCompression.Zip ? 16 : 1;
        int blockCount = (height + linesPerBlock - 1) / linesPerBlock;
        var blockOffsets = new long[blockCount];
        for (int i = 0; i < blockCount; i++)
            blockOffsets[i] = br.ReadInt64();

        // ── Channels sorted alphabetically per scanline (EXR spec) ──────────
        var sortedChannels = channels.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        int bytesPerPixel = sortedChannels.Sum(c => c.BytesPerSample);
        int scanlineBytes = width * bytesPerPixel;

        int planeSize = width * height;
        var planes = new float[sortedChannels.Count][];
        for (int c = 0; c < planes.Length; c++)
            planes[c] = new float[planeSize];

        for (int b = 0; b < blockCount; b++)
        {
            fs.Position = blockOffsets[b];
            int yStart = br.ReadInt32();          // y of first line in block
            int dataSize = br.ReadInt32();        // size of stored pixel data
            byte[] blockData = br.ReadBytes(dataSize);

            int linesInBlock = Math.Min(linesPerBlock, height - (yStart - dataWindowMinY));
            int expectedRawSize = linesInBlock * scanlineBytes;

            // Spec: a block whose stored size equals its raw size is stored
            // uncompressed (the writer falls back when zlib does not shrink).
            byte[] decoded = dataSize == expectedRawSize
                ? blockData
                : compression switch
                {
                    ExrCompression.Zip or ExrCompression.Zips => DecompressZip(blockData),
                    _ => throw new InvalidDataException("EXR: uncompressed block with unexpected size."),
                };
            if (decoded.Length != expectedRawSize)
                throw new InvalidDataException("EXR: block decompressed to an unexpected size.");

            for (int ly = 0; ly < linesInBlock; ly++)
            {
                int y = (yStart - dataWindowMinY) + ly;
                int channelByteCursor = ly * scanlineBytes;
                for (int c = 0; c < sortedChannels.Count; c++)
                {
                    var ch = sortedChannels[c];
                    int sampleSize = ch.BytesPerSample;
                    float[] plane = planes[c];
                    for (int x = 0; x < width; x++)
                    {
                        int bytePos = channelByteCursor + x * sampleSize;
                        plane[y * width + x] = ch.PixelType == ExrPixelType.Float
                            ? BitConverter.ToSingle(decoded, bytePos)
                            : (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(decoded, bytePos));
                    }
                    channelByteCursor += width * sampleSize;
                }
            }
        }

        var result = new List<ExrChannelData>(sortedChannels.Count);
        for (int c = 0; c < sortedChannels.Count; c++)
            result.Add(new ExrChannelData(sortedChannels[c].Name, sortedChannels[c].PixelType, planes[c]));
        return (result, width, height);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static string? ReadNullString(BinaryReader br)
    {
        var sb = new StringBuilder();
        while (true)
        {
            byte b = br.ReadByte();
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private enum ExrCompression : byte
    {
        None = 0, Rle = 1, Zips = 2, Zip = 3, Piz = 4, Pxr24 = 5, B44 = 6, B44a = 7,
        Dwaa = 8, Dwab = 9,
    }

    private record ExrChannel(string Name, ExrPixelType PixelType, int XSampling, int YSampling)
    {
        public int BytesPerSample => PixelType == ExrPixelType.Half ? 2 : 4;
    }

    private static List<ExrChannel> ParseChannels(byte[] payload)
    {
        var result = new List<ExrChannel>();
        int pos = 0;
        while (pos < payload.Length - 1)
        {
            int nameStart = pos;
            while (pos < payload.Length && payload[pos] != 0) pos++;
            if (pos >= payload.Length || pos == nameStart) break;
            string name = Encoding.ASCII.GetString(payload, nameStart, pos - nameStart);
            pos++;  // skip null
            if (pos + 16 > payload.Length) break;
            int pixelTypeRaw = BitConverter.ToInt32(payload, pos);
            // skip pLinear (1 byte) + 3 reserved
            int xSampling = BitConverter.ToInt32(payload, pos + 8);
            int ySampling = BitConverter.ToInt32(payload, pos + 12);
            pos += 16;
            result.Add(new ExrChannel(name, (ExrPixelType)pixelTypeRaw, xSampling, ySampling));
        }
        return result;
    }

    private static byte[] DecompressZip(byte[] compressed)
    {
        // EXR ZIP framing: a zlib stream (RFC 1950, 2-byte header + adler32).
        using var input = new MemoryStream(compressed);
        // Skip 2-byte zlib header
        input.ReadByte(); input.ReadByte();
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);

        // Undo the EXR ZIP pre-processing (predictor, then interleave) —
        // shared with the writer so both sides stay provably symmetric.
        return ExrImage.ZipReconstruct(output.ToArray());
    }
}
