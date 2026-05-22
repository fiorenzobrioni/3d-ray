using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace RayTracer.Textures;

/// <summary>
/// Minimal OpenEXR loader. Supports the subset of the format that covers the
/// overwhelming majority of artist-authored HDRIs:
/// <list type="bullet">
///   <item><description>Scanline-stored images (the most common variant).</description></item>
///   <item><description>Compression: <c>NO_COMPRESSION</c>, <c>ZIP</c>, <c>ZIPS</c>.</description></item>
///   <item><description>Float32 (R32G32B32) channels named R/G/B (case-sensitive per spec).</description></item>
///   <item><description>Half-float (R/G/B at 16-bit) channels.</description></item>
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
    public static (float[] Pixels, int Width, int Height) Load(string path)
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

        // Find R/G/B channels — required for our use as an environment map.
        var chR = channels.FirstOrDefault(c => c.Name == "R");
        var chG = channels.FirstOrDefault(c => c.Name == "G");
        var chB = channels.FirstOrDefault(c => c.Name == "B");
        if (chR is null || chG is null || chB is null)
            throw new InvalidDataException("EXR: missing R/G/B channels (only RGB EXR supported).");
        if (chR.PixelType != ExrPixelType.Float && chR.PixelType != ExrPixelType.Half)
            throw new NotSupportedException($"EXR: pixel type {chR.PixelType} not supported.");

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

        var pixels = new float[width * height * 3];

        for (int b = 0; b < blockCount; b++)
        {
            fs.Position = blockOffsets[b];
            int yStart = br.ReadInt32();          // y of first line in block
            int dataSize = br.ReadInt32();        // size of pixel data after decompression header
            byte[] blockData = br.ReadBytes(dataSize);

            byte[] decoded = compression switch
            {
                ExrCompression.None => blockData,
                ExrCompression.Zip or ExrCompression.Zips => DecompressZip(blockData),
                _ => throw new NotSupportedException(),
            };

            int linesInBlock = Math.Min(linesPerBlock, height - (yStart - dataWindowMinY));
            int decodedScanline = scanlineBytes;
            for (int ly = 0; ly < linesInBlock; ly++)
            {
                int y = (yStart - dataWindowMinY) + ly;
                int rowOffset = ly * decodedScanline;
                int channelByteCursor = rowOffset;
                foreach (var ch in sortedChannels)
                {
                    int sampleSize = ch.BytesPerSample;
                    if (ch.Name == "R" || ch.Name == "G" || ch.Name == "B")
                    {
                        int outChannelOffset = ch.Name == "R" ? 0 : ch.Name == "G" ? 1 : 2;
                        for (int x = 0; x < width; x++)
                        {
                            int bytePos = channelByteCursor + x * sampleSize;
                            float v = ch.PixelType == ExrPixelType.Float
                                ? BitConverter.ToSingle(decoded, bytePos)
                                : HalfToFloat(BitConverter.ToUInt16(decoded, bytePos));
                            pixels[(y * width + x) * 3 + outChannelOffset] = MathF.Max(0f, v);
                        }
                    }
                    channelByteCursor += width * sampleSize;
                }
            }
        }

        return (pixels, width, height);
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

    private enum ExrPixelType { Uint = 0, Half = 1, Float = 2 }

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
        // EXR ZIP framing: first the deflate-compressed stream (RFC 1950 zlib header).
        using var input = new MemoryStream(compressed);
        // Skip 2-byte zlib header
        input.ReadByte(); input.ReadByte();
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        byte[] decompressed = output.ToArray();

        // EXR ZIP post-processing:
        //  1) Reorder bytes: interleave even/odd halves.
        //  2) Reverse a per-byte delta predictor.
        byte[] reordered = new byte[decompressed.Length];
        int half = (decompressed.Length + 1) / 2;
        int t1 = 0, t2 = half, s = 0;
        while (s < decompressed.Length)
        {
            if (s < decompressed.Length) reordered[s++] = decompressed[t1++];
            if (s < decompressed.Length) reordered[s++] = decompressed[t2++];
        }
        for (int i = 1; i < reordered.Length; i++)
            reordered[i] = (byte)(reordered[i] + reordered[i - 1] - 128);
        return reordered;
    }

    /// <summary>IEEE half-precision (binary16) to float32 conversion.</summary>
    private static float HalfToFloat(ushort h)
    {
        int sign = (h >> 15) & 0x1;
        int exp  = (h >> 10) & 0x1F;
        int mant = h & 0x3FF;

        int fSign = sign << 31;
        int fExp, fMant;
        if (exp == 0)
        {
            if (mant == 0) { fExp = 0; fMant = 0; }
            else
            {
                // subnormal — renormalise
                while ((mant & 0x400) == 0) { mant <<= 1; exp--; }
                exp++; mant &= 0x3FF;
                fExp = (exp + 112) << 23;
                fMant = mant << 13;
            }
        }
        else if (exp == 31)
        {
            fExp = 0xFF << 23;
            fMant = mant << 13;
        }
        else
        {
            fExp = (exp + 112) << 23;
            fMant = mant << 13;
        }
        int bits = fSign | fExp | fMant;
        return BitConverter.Int32BitsToSingle(bits);
    }
}
