using RayTracer.Imaging;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The engine's own image codecs (PIANO-REVIEW.md Phase 11, which replaced
/// ImageSharp). Fixtures in <c>TestImages/</c> are written by other encoders
/// (pypng, libjpeg through PIL, ImageMagick; <c>generate.py</c>) with the
/// decode each must give: lossless formats exactly, JPEG within a few levels
/// of libjpeg (a different IDCT and bilinear chroma upsampling).
/// </summary>
public class ImageCodecTests
{
    private static string Fixture(string file) => Path.Combine(AppContext.BaseDirectory, "TestImages", file);

    private static ushort[] Expected(string name)
    {
        var bytes = File.ReadAllBytes(Fixture(name + ".expect"));
        var e = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, e, 0, bytes.Length);
        return e;
    }

    [Theory]
    [InlineData("png-g2-i.png", 1, 8)]          // 2-bit grey, Adam7
    [InlineData("png-g16.png", 1, 16)]
    [InlineData("png-ga8.png", 2, 8)]
    [InlineData("png-rgba16-i.png", 4, 16)]     // 16-bit RGBA, Adam7
    [InlineData("png-pal4-i.png", 4, 8)]        // 4-bit palette with tRNS alpha, Adam7
    [InlineData("png-rgbkey.png", 4, 8)]        // RGB with a tRNS colour key
    [InlineData("bmp-rle4.bmp", 3, 8)]
    [InlineData("bmp-32a.bmp", 4, 8)]           // v5 header, alpha mask
    [InlineData("tga-rgba-rle-top.tga", 4, 8)]  // RLE, top-left origin
    public void LosslessFixture_DecodesExactly(string file, int channels, int bitDepth)
    {
        var img = ImageFile.Load(Fixture(file));
        Assert.Equal(channels, img.Channels);
        Assert.Equal(bitDepth, img.BitDepth);
        var e = Expected(Path.GetFileNameWithoutExtension(file));
        Assert.Equal(e.Length, img.Width * img.Height * 4);
        int scale = 65535 / img.MaxValue;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                img.GetRgba(x, y, out int r, out int g, out int b, out int a);
                int i = (y * img.Width + x) * 4;
                Assert.Equal(e[i], r * scale);
                Assert.Equal(e[i + 1], g * scale);
                Assert.Equal(e[i + 2], b * scale);
                Assert.Equal(e[i + 3], a * scale);
            }
        var info = ImageFile.Identify(Fixture(file));
        Assert.Equal((img.Width, img.Height, img.Channels, img.BitDepth), (info.Width, info.Height, info.Channels, info.BitDepth));
    }

    [Theory]
    [InlineData("jpg-422.jpg", 3)]
    [InlineData("jpg-420-progressive.jpg", 3)]
    [InlineData("jpg-restart-progressive.jpg", 3)] // restart markers, 2×1 sampling
    [InlineData("jpg-grey.jpg", 1)]
    public void JpegFixture_MatchesLibjpeg(string file, int channels)
    {
        var img = ImageFile.Load(Fixture(file));
        Assert.Equal(channels, img.Channels);
        var e = Expected(Path.GetFileNameWithoutExtension(file));
        Assert.Equal(e.Length, img.Width * img.Height * 4);
        double max = 0, sum = 0;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                img.GetRgba(x, y, out int r, out int g, out int b, out _);
                int i = (y * img.Width + x) * 4;
                foreach (var (v, c) in new[] { (r, 0), (g, 1), (b, 2) })
                {
                    double d = Math.Abs(v - e[i + c] / 257.0);
                    max = Math.Max(max, d);
                    sum += d;
                }
            }
        Assert.InRange(max, 0, 4);
        Assert.InRange(sum / (3.0 * img.Width * img.Height), 0, 0.6);
    }

    [Theory]
    [InlineData(1, 8)] [InlineData(2, 8)] [InlineData(3, 8)] [InlineData(4, 8)]
    [InlineData(1, 16)] [InlineData(2, 16)] [InlineData(3, 16)] [InlineData(4, 16)]
    public void Png_RoundTrips(int channels, int bitDepth)
    {
        var img = new RasterImage(53, 31, channels, bitDepth);
        var rng = new Random(channels * 100 + bitDepth);
        for (int i = 0; i < img.Samples.Length; i++)
            img.Samples[i] = (ushort)(i % 5 == 0 ? rng.Next(img.MaxValue + 1) : i * 37 / channels % (img.MaxValue + 1));
        var ms = new MemoryStream();
        Png.Encode(ms, img, srgb: true);
        var back = ImageFile.Decode(ms.ToArray());
        Assert.Equal(channels, back.Channels);
        Assert.Equal(bitDepth, back.BitDepth);
        Assert.Equal(img.Samples, back.Samples);
    }

    [Fact]
    public void Bmp_RoundTripsExactly_AndJpeg_IsCloseAtQuality95()
    {
        var img = new RasterImage(37, 21, 3, 8);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                img[x, y, 0] = (ushort)(127 + 120 * Math.Sin(x / 5.0));
                img[x, y, 1] = (ushort)(127 + 120 * Math.Cos(y / 4.0));
                img[x, y, 2] = (ushort)((x + y) * 255 / 58);
            }
        string dir = Path.Combine(Path.GetTempPath(), $"3dray-codec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ImageFile.Save(Path.Combine(dir, "a.bmp"), img);
            Assert.Equal(img.Samples, ImageFile.Load(Path.Combine(dir, "a.bmp")).Samples);

            ImageFile.Save(Path.Combine(dir, "a.jpg"), img);
            var j = ImageFile.Load(Path.Combine(dir, "a.jpg"));
            double se = 0;
            for (int i = 0; i < j.Samples.Length; i++) se += Math.Pow(j.Samples[i] - img.Samples[i], 2);
            double psnr = 10 * Math.Log10(255.0 * 255.0 / (se / j.Samples.Length));
            Assert.True(psnr > 40, $"PSNR {psnr:F1} dB");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void JpegEncoder_TablesCoverEverySymbol()
    {
        // EOB, ZRL and every (run 0-15, size 1-10) pair must have a code.
        foreach (var t in new[] { JpegEncoder.AcLuma, JpegEncoder.AcChroma })
        {
            Assert.NotEqual(0, t.Length[0x00]);
            Assert.NotEqual(0, t.Length[0xF0]);
            for (int run = 0; run < 16; run++)
                for (int size = 1; size <= 10; size++)
                    Assert.NotEqual(0, t.Length[run << 4 | size]);
        }
        foreach (var t in new[] { JpegEncoder.DcLuma, JpegEncoder.DcChroma })
            for (int size = 0; size <= 11; size++)
                Assert.NotEqual(0, t.Length[size]);
    }

    [Fact]
    public void Png_CorruptChunk_IsRefused()
    {
        var bytes = File.ReadAllBytes(Fixture("png-ga8.png"));
        bytes[20] ^= 0x40; // inside IHDR: its CRC no longer matches
        var e = Assert.Throws<InvalidDataException>(() => ImageFile.Decode(bytes));
        Assert.Contains("CRC", e.Message);
    }

    [Fact]
    public void UnsupportedFormat_SaysWhatToUse()
    {
        var gif = "GIF89a"u8.ToArray().Concat(new byte[32]).ToArray();
        var e = Assert.Throws<InvalidDataException>(() => ImageFile.Decode(gif, ".gif"));
        Assert.Contains("GIF", e.Message);
        Assert.Contains("PNG", e.Message);
    }
}
