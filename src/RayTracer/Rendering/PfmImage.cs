using System.Text;

namespace RayTracer.Rendering;

/// <summary>
/// Minimal Portable Float Map (PFM) reader/writer for HDR output.
///
/// PFM is the simplest portable HDR raster format: an ASCII header followed by
/// raw IEEE-754 floats. It needs no external encoder, round-trips the
/// renderer's linear radiance exactly, and is read by common image tools.
///
/// Layout:
///   "PF\n"  — colour, 3 interleaved channels per pixel ("Pf" = grayscale, 1)
///   "{width} {height}\n"
///   "{scale}\n" — sign encodes byte order: negative = little-endian
///   rows of raw floats, BOTTOM-TO-TOP, pixels left-to-right
///
/// The in-memory layout used by the engine is plane-major
/// (<see cref="FrameBuffer"/>: data[c·W·H + y·W + x], row 0 = top), so the
/// writer interleaves and flips vertically on the way out and the reader
/// undoes both on the way in.
/// </summary>
public static class PfmImage
{
    /// <summary>
    /// Writes a plane-major float buffer (1 or 3 channels, row 0 = top) as a
    /// little-endian PFM file.
    /// </summary>
    public static void Write(string path, float[] planes, int width, int height, int channels)
    {
        if (channels != 1 && channels != 3)
            throw new ArgumentException($"PFM supports 1 or 3 channels, got {channels}.", nameof(channels));
        if (planes.Length < (long)width * height * channels)
            throw new ArgumentException("Plane buffer smaller than width*height*channels.", nameof(planes));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        string magic = channels == 3 ? "PF" : "Pf";
        byte[] header = Encoding.ASCII.GetBytes($"{magic}\n{width} {height}\n-1.0\n");
        stream.Write(header, 0, header.Length);

        int planeSize = width * height;
        // One interleaved scanline reused across rows.
        float[] row = new float[width * channels];
        byte[] rowBytes = new byte[row.Length * sizeof(float)];
        for (int y = height - 1; y >= 0; y--)            // bottom-to-top
        {
            int rowBase = y * width;
            if (channels == 1)
            {
                Array.Copy(planes, rowBase, row, 0, width);
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    row[x * 3 + 0] = planes[rowBase + x];
                    row[x * 3 + 1] = planes[planeSize + rowBase + x];
                    row[x * 3 + 2] = planes[2 * planeSize + rowBase + x];
                }
            }
            Buffer.BlockCopy(row, 0, rowBytes, 0, rowBytes.Length);
            if (!BitConverter.IsLittleEndian) ReverseEndianness(rowBytes);
            stream.Write(rowBytes, 0, rowBytes.Length);
        }
    }

    /// <summary>
    /// Reads a PFM file into a plane-major float buffer (row 0 = top).
    /// Both byte orders are accepted; the result is host-endian.
    /// </summary>
    public static (float[] Planes, int Width, int Height, int Channels) Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);

        string magic = ReadToken(stream);
        int channels = magic switch
        {
            "PF" => 3,
            "Pf" => 1,
            _    => throw new InvalidDataException($"Not a PFM file (magic '{magic}')."),
        };
        if (!int.TryParse(ReadToken(stream), out int width) || width <= 0 ||
            !int.TryParse(ReadToken(stream), out int height) || height <= 0)
            throw new InvalidDataException("Invalid PFM dimensions.");
        if (!float.TryParse(ReadToken(stream),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float scale) || scale == 0f)
            throw new InvalidDataException("Invalid PFM scale.");
        bool fileLittleEndian = scale < 0f;

        int planeSize = width * height;
        var planes = new float[planeSize * channels];
        byte[] rowBytes = new byte[width * channels * sizeof(float)];
        float[] row = new float[width * channels];
        for (int y = height - 1; y >= 0; y--)            // file rows are bottom-to-top
        {
            stream.ReadExactly(rowBytes, 0, rowBytes.Length);
            if (fileLittleEndian != BitConverter.IsLittleEndian) ReverseEndianness(rowBytes);
            Buffer.BlockCopy(rowBytes, 0, row, 0, rowBytes.Length);
            int rowBase = y * width;
            if (channels == 1)
            {
                Array.Copy(row, 0, planes, rowBase, width);
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    planes[rowBase + x]                 = row[x * 3 + 0];
                    planes[planeSize + rowBase + x]     = row[x * 3 + 1];
                    planes[2 * planeSize + rowBase + x] = row[x * 3 + 2];
                }
            }
        }
        return (planes, width, height, channels);
    }

    /// <summary>Reads one whitespace-delimited ASCII token from the header.</summary>
    private static string ReadToken(Stream stream)
    {
        var sb = new StringBuilder(16);
        int b;
        // Skip leading whitespace.
        do
        {
            b = stream.ReadByte();
            if (b < 0) throw new InvalidDataException("Truncated PFM header.");
        } while (b == ' ' || b == '\n' || b == '\r' || b == '\t');
        // Accumulate until the single terminating whitespace byte.
        while (b >= 0 && b != ' ' && b != '\n' && b != '\r' && b != '\t')
        {
            sb.Append((char)b);
            b = stream.ReadByte();
        }
        return sb.ToString();
    }

    private static void ReverseEndianness(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 4)
        {
            (bytes[i], bytes[i + 3]) = (bytes[i + 3], bytes[i]);
            (bytes[i + 1], bytes[i + 2]) = (bytes[i + 2], bytes[i + 1]);
        }
    }
}
