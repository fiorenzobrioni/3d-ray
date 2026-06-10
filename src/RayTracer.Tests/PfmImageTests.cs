using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Round-trip and format-layout tests for the <see cref="PfmImage"/> HDR
/// writer/reader used by the <c>--aov</c> output path.
/// </summary>
public class PfmImageTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pfm-test-{Guid.NewGuid():N}.pfm");

    [Fact]
    public void ColorRoundTrip_PreservesHdrValues()
    {
        const int W = 7, H = 5;
        var rng = new Random(42);
        var planes = new float[W * H * 3];
        for (int i = 0; i < planes.Length; i++)
        {
            // Mix of HDR (>1), tiny, and negative values — PFM stores raw floats.
            planes[i] = (float)(rng.NextDouble() * 200.0 - 50.0);
            if (i % 7 == 0) planes[i] = 1.2345e-6f;
        }

        string path = TempPath();
        try
        {
            PfmImage.Write(path, planes, W, H, channels: 3);
            var (read, w, h, c) = PfmImage.Read(path);

            Assert.Equal(W, w);
            Assert.Equal(H, h);
            Assert.Equal(3, c);
            Assert.Equal(planes, read);   // bit-exact float round trip
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GrayscaleRoundTrip_PreservesValues()
    {
        const int W = 4, H = 6;
        var planes = new float[W * H];
        for (int i = 0; i < planes.Length; i++)
            planes[i] = i * 1.5f - 3f;

        string path = TempPath();
        try
        {
            PfmImage.Write(path, planes, W, H, channels: 1);
            var (read, w, h, c) = PfmImage.Read(path);

            Assert.Equal(W, w);
            Assert.Equal(H, h);
            Assert.Equal(1, c);
            Assert.Equal(planes, read);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Header_IsLittleEndianPfWithDimensions()
    {
        const int W = 3, H = 2;
        string path = TempPath();
        try
        {
            PfmImage.Write(path, new float[W * H * 3], W, H, channels: 3);
            byte[] bytes = File.ReadAllBytes(path);
            string header = System.Text.Encoding.ASCII.GetString(bytes, 0, 14);
            Assert.StartsWith("PF\n3 2\n-1.0\n", header);
            // Header (12 bytes) + 6 px × 3 ch × 4 B payload.
            Assert.Equal(12 + W * H * 3 * 4, bytes.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RowOrder_IsBottomToTop()
    {
        // 1×2 grayscale: top row = 1, bottom row = 2. The file must store the
        // bottom row first (PFM convention), i.e. payload = [2, 1].
        const int W = 1, H = 2;
        var planes = new float[] { 1f, 2f };   // row 0 (top) = 1, row 1 = 2

        string path = TempPath();
        try
        {
            PfmImage.Write(path, planes, W, H, channels: 1);
            byte[] bytes = File.ReadAllBytes(path);
            int headerLen = bytes.Length - W * H * 4;
            float first = BitConverter.ToSingle(bytes, headerLen);
            float second = BitConverter.ToSingle(bytes, headerLen + 4);
            Assert.Equal(2f, first);
            Assert.Equal(1f, second);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_RejectsNonPfmMagic()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, "P6\n1 1\n255\nxxx");
            Assert.Throws<InvalidDataException>(() => PfmImage.Read(path));
        }
        finally { File.Delete(path); }
    }
}
