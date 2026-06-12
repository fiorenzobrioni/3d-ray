using System.Numerics;
using System.Text;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Round-trip, ZIP-transform and header-conformance tests for the
/// <see cref="ExrImage"/> writer and the <see cref="ExrLoader"/>
/// multi-channel reader behind the <c>-o *.exr</c> / <c>--aov-format exr</c>
/// output path. Half channels round-trip to exactly
/// <c>(float)(Half)written</c> (half→float widening is exact), float channels
/// bit-exactly.
/// </summary>
public class ExrImageTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"exr-test-{Guid.NewGuid():N}.exr");

    private static float HalfQuantize(float v) => (float)(Half)v;

    /// <summary>Random plane with HDR, tiny and negative values, all finite.</summary>
    private static float[] RandomPlane(int n, int seed, float scale = 100f)
    {
        var rng = new Random(seed);
        var plane = new float[n];
        for (int i = 0; i < n; i++)
            plane[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * scale;
        plane[0] = 1.2345e-6f;
        if (n > 1) plane[1] = -0.75f;
        return plane;
    }

    [Fact]
    public void HalfRgbRoundTrip_MatchesHalfQuantizedValues()
    {
        const int W = 13, H = 21;
        var r = RandomPlane(W * H, 1);
        var g = RandomPlane(W * H, 2);
        var b = RandomPlane(W * H, 3);

        string path = TempPath();
        try
        {
            ExrImage.Write(path, W, H, new[]
            {
                new ExrImage.Channel("R", ExrPixelType.Half, r),
                new ExrImage.Channel("G", ExrPixelType.Half, g),
                new ExrImage.Channel("B", ExrPixelType.Half, b),
            });
            var (channels, w, h) = ExrLoader.LoadChannels(path);

            Assert.Equal(W, w);
            Assert.Equal(H, h);
            Assert.Equal(new[] { "B", "G", "R" }, channels.Select(c => c.Name));
            Assert.Equal(b.Select(HalfQuantize), channels[0].Plane);
            Assert.Equal(g.Select(HalfQuantize), channels[1].Plane);
            Assert.Equal(r.Select(HalfQuantize), channels[2].Plane);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FloatChannelRoundTrip_IsBitExact()
    {
        const int W = 9, H = 33;                    // 3 ZIP blocks, last partial
        var z = RandomPlane(W * H, 7, scale: 5000f);
        z[5] = -1f;                                 // depth no-hit sentinel

        string path = TempPath();
        try
        {
            ExrImage.Write(path, W, H, new[] { new ExrImage.Channel("Z", ExrPixelType.Float, z) });
            var (channels, w, h) = ExrLoader.LoadChannels(path);

            Assert.Equal((W, H), (w, h));
            var ch = Assert.Single(channels);
            Assert.Equal("Z", ch.Name);
            Assert.Equal(ExrPixelType.Float, ch.Type);
            Assert.Equal(z, ch.Plane);              // bit-exact float round trip
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Multilayer_MixedTypes_AllChannelsSurviveUnclamped()
    {
        // The full multilayer layout `-o out.exr --aov ...` produces: beauty
        // R,G,B (half) + albedo.* (half) + normal.* (half, signed!) + Z (float).
        const int W = 8, H = 6;
        int n = W * H;
        var names = new[] { "R", "G", "B", "albedo.R", "albedo.G", "albedo.B",
                            "normal.X", "normal.Y", "normal.Z" };
        var planes = names.Select((_, i) => RandomPlane(n, 100 + i, scale: 1f)).ToArray();
        var depth = RandomPlane(n, 200, scale: 50f);

        var channels = names
            .Select((name, i) => new ExrImage.Channel(name, ExrPixelType.Half, planes[i]))
            .Append(new ExrImage.Channel("Z", ExrPixelType.Float, depth))
            .ToList();

        string path = TempPath();
        try
        {
            ExrImage.Write(path, W, H, channels);
            var (read, w, h) = ExrLoader.LoadChannels(path);

            Assert.Equal((W, H), (w, h));
            Assert.Equal(10, read.Count);
            foreach (var (name, i) in names.Select((name, i) => (name, i)))
            {
                var ch = read.Single(c => c.Name == name);
                Assert.Equal(ExrPixelType.Half, ch.Type);
                Assert.Equal(planes[i].Select(HalfQuantize), ch.Plane);
            }
            var zCh = read.Single(c => c.Name == "Z");
            Assert.Equal(depth, zCh.Plane);

            // Signed data must survive the generic channel path.
            Assert.Contains(read.Single(c => c.Name == "normal.X").Plane, v => v < 0f);
            Assert.Contains(zCh.Plane, v => v < 0f);
        }
        finally { File.Delete(path); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ZIP block transform — pinned against an independent oracle transcribed
    // from the reference implementation (ImfZip.cpp): split reorder THEN
    // delta predictor on encode; the inverse undoes the predictor first.
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] OracleDeconstruct(byte[] raw)
    {
        var t = new byte[raw.Length];
        int t1 = 0, t2 = (raw.Length + 1) / 2, s = 0;
        while (s < raw.Length)
        {
            t[t1++] = raw[s++];
            if (s < raw.Length) t[t2++] = raw[s++];
        }
        int p = raw.Length > 0 ? t[0] : 0;
        for (int i = 1; i < t.Length; i++)
        {
            int d = t[i] - p + (128 + 256);
            p = t[i];
            t[i] = (byte)d;
        }
        return t;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(256)]
    [InlineData(1023)]
    public void ZipTransform_MatchesReferenceOracle(int length)
    {
        var rng = new Random(length * 31 + 5);
        var raw = new byte[length];
        rng.NextBytes(raw);

        byte[] encoded = ExrImage.ZipDeconstruct(raw);
        Assert.Equal(OracleDeconstruct(raw), encoded);
        Assert.Equal(raw, ExrImage.ZipReconstruct(encoded));
    }

    [Fact]
    public void IncompressibleData_FallsBackToRawBlocks_AndRoundTrips()
    {
        // Near-uniform random half bit patterns defeat deflate, forcing the
        // spec's raw-block fallback (dataSize == uncompressed size).
        const int W = 40, H = 20;                   // 2 blocks, second partial
        var rng = new Random(99);
        var plane = new float[W * H];
        for (int i = 0; i < plane.Length; i++)
        {
            ushort bits;
            do { bits = (ushort)rng.Next(ushort.MaxValue + 1); }
            while (((bits >> 10) & 0x1F) == 0x1F);  // skip NaN/Inf patterns
            plane[i] = (float)BitConverter.UInt16BitsToHalf(bits);
        }

        string path = TempPath();
        try
        {
            ExrImage.Write(path, W, H, new[] { new ExrImage.Channel("R", ExrPixelType.Half, plane) });

            // At least one chunk must have taken the raw path.
            var chunks = ReadChunkSizes(path, out int scanlineBytes);
            Assert.Contains(chunks, c => c.DataSize == c.Lines * scanlineBytes);

            var (channels, _, _) = ExrLoader.LoadChannels(path);
            Assert.Equal(plane, Assert.Single(channels).Plane);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Header_ConformsToSpec()
    {
        const int W = 3, H = 37;                    // 3 ZIP blocks
        var plane = new float[W * H];
        string path = TempPath();
        try
        {
            ExrImage.Write(path, W, H, new[]
            {
                new ExrImage.Channel("R", ExrPixelType.Half, plane),
                new ExrImage.Channel("G", ExrPixelType.Half, plane),
                new ExrImage.Channel("B", ExrPixelType.Half, plane),
                new ExrImage.Channel("Z", ExrPixelType.Float, plane),
            });

            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            Assert.Equal(0x01312F76u, br.ReadUInt32());
            Assert.Equal(2, br.ReadInt32());        // version 2, no flags

            var attrs = new Dictionary<string, (string Type, byte[] Payload)>();
            while (true)
            {
                string name = ReadNullString(br);
                if (name.Length == 0) break;
                string type = ReadNullString(br);
                int size = br.ReadInt32();
                attrs[name] = (type, br.ReadBytes(size));
            }

            // The 8 spec-required attributes + chromaticities, correct types.
            Assert.Equal(
                new[] { "channels", "chromaticities", "compression", "dataWindow", "displayWindow",
                        "lineOrder", "pixelAspectRatio", "screenWindowCenter", "screenWindowWidth" },
                attrs.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal("chlist", attrs["channels"].Type);
            Assert.Equal("chromaticities", attrs["chromaticities"].Type);
            Assert.Equal(new byte[] { 3 }, attrs["compression"].Payload);   // ZIP
            Assert.Equal(new byte[] { 0 }, attrs["lineOrder"].Payload);     // INCREASING_Y
            Assert.Equal("box2i", attrs["dataWindow"].Type);
            Assert.Equal("box2i", attrs["displayWindow"].Type);
            Assert.Equal(1.0f, BitConverter.ToSingle(attrs["pixelAspectRatio"].Payload));
            Assert.Equal(1.0f, BitConverter.ToSingle(attrs["screenWindowWidth"].Payload));
            Assert.Equal(8, attrs["screenWindowCenter"].Payload.Length);

            byte[] dw = attrs["dataWindow"].Payload;
            Assert.Equal(new[] { 0, 0, W - 1, H - 1 },
                new[] { BitConverter.ToInt32(dw, 0), BitConverter.ToInt32(dw, 4),
                        BitConverter.ToInt32(dw, 8), BitConverter.ToInt32(dw, 12) });
            Assert.Equal(attrs["dataWindow"].Payload, attrs["displayWindow"].Payload);

            // chlist: ordinal-sorted names, correct pixel types, null-terminated.
            byte[] chlist = attrs["channels"].Payload;
            int pos = 0;
            foreach (var (name, type) in new[] { ("B", 1), ("G", 1), ("R", 1), ("Z", 2) })
            {
                int start = pos;
                while (chlist[pos] != 0) pos++;
                Assert.Equal(name, Encoding.ASCII.GetString(chlist, start, pos - start));
                pos++;
                Assert.Equal(type, BitConverter.ToInt32(chlist, pos));
                Assert.Equal(1, BitConverter.ToInt32(chlist, pos + 8));     // xSampling
                Assert.Equal(1, BitConverter.ToInt32(chlist, pos + 12));    // ySampling
                pos += 16;
            }
            Assert.Equal(0, chlist[pos]);
            Assert.Equal(pos + 1, chlist.Length);

            // Offset table: ceil(H/16) entries, each pointing at a chunk
            // whose y field is 16·i.
            int blockCount = (H + 15) / 16;
            var offsets = new long[blockCount];
            for (int i = 0; i < blockCount; i++)
                offsets[i] = br.ReadInt64();
            for (int i = 0; i < blockCount; i++)
            {
                fs.Position = offsets[i];
                Assert.Equal(16 * i, br.ReadInt32());
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 5)]
    [InlineData(5, 37)]
    [InlineData(33, 16)]
    public void GeometryEdges_RoundTripBitExact(int w, int h)
    {
        var plane = RandomPlane(w * h, w * 1000 + h, scale: 9f);
        string path = TempPath();
        try
        {
            ExrImage.Write(path, w, h, new[] { new ExrImage.Channel("Z", ExrPixelType.Float, plane) });
            var (channels, rw, rh) = ExrLoader.LoadChannels(path);
            Assert.Equal((w, h), (rw, rh));
            Assert.Equal(plane, Assert.Single(channels).Plane);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HalfConversion_ClampsOverflowToMaxHalf_AndPropagatesNaN()
    {
        var plane = new[] { 1e6f, float.PositiveInfinity, -1e6f, float.NaN, 70000f, 65504f };
        string path = TempPath();
        try
        {
            ExrImage.Write(path, plane.Length, 1, new[] { new ExrImage.Channel("R", ExrPixelType.Half, plane) });
            float[] read = Assert.Single(ExrLoader.LoadChannels(path).Channels).Plane;

            Assert.Equal(65504f, read[0]);
            Assert.Equal(65504f, read[1]);
            Assert.Equal(-65504f, read[2]);
            Assert.True(float.IsNaN(read[3]));
            Assert.Equal(65504f, read[4]);
            Assert.Equal(65504f, read[5]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ClampsNegativesForHdriUse_LoadChannelsDoesNot()
    {
        const int W = 4, H = 2;
        var plane = new float[W * H];
        for (int i = 0; i < plane.Length; i++) plane[i] = i % 2 == 0 ? -2f : 3f;

        string path = TempPath();
        try
        {
            ExrImage.Write(path, W, H, new[]
            {
                new ExrImage.Channel("R", ExrPixelType.Float, plane),
                new ExrImage.Channel("G", ExrPixelType.Float, plane),
                new ExrImage.Channel("B", ExrPixelType.Float, plane),
            });

            var (pixels, _, _) = ExrLoader.Load(path);          // HDRI contract
            Assert.All(pixels, v => Assert.True(v >= 0f));
            Assert.Equal(3f, pixels[3 + 1]);

            var (channels, _, _) = ExrLoader.LoadChannels(path); // exact contract
            Assert.Equal(plane, channels[0].Plane);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_RejectsInvalidChannelSets()
    {
        var plane = new float[4];
        Assert.Throws<ArgumentException>(() =>
            ExrImage.Write(TempPath(), 2, 2, Array.Empty<ExrImage.Channel>()));
        Assert.Throws<ArgumentException>(() =>
            ExrImage.Write(TempPath(), 2, 2, new[]
            {
                new ExrImage.Channel("R", ExrPixelType.Half, plane),
                new ExrImage.Channel("R", ExrPixelType.Half, plane),
            }));
        Assert.Throws<ArgumentException>(() =>
            ExrImage.Write(TempPath(), 2, 2, new[]
            {
                new ExrImage.Channel("R", ExrPixelType.Half, new float[3]),
            }));
        Assert.Throws<ArgumentException>(() =>
            ExrImage.Write(TempPath(), 2, 2, new[]
            {
                new ExrImage.Channel("", ExrPixelType.Half, plane),
            }));
    }

    /// <summary>Skips the header and returns each chunk's (lines, dataSize),
    /// plus the bytes one full scanline occupies.</summary>
    private static List<(int Lines, int DataSize)> ReadChunkSizes(string path, out int scanlineBytes)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        br.ReadUInt32(); br.ReadInt32();
        int width = 0, height = 0, bytesPerPixel = 0;
        while (true)
        {
            string name = ReadNullString(br);
            if (name.Length == 0) break;
            ReadNullString(br);
            int size = br.ReadInt32();
            byte[] payload = br.ReadBytes(size);
            if (name == "dataWindow")
            {
                width = BitConverter.ToInt32(payload, 8) + 1;
                height = BitConverter.ToInt32(payload, 12) + 1;
            }
            else if (name == "channels")
            {
                for (int pos = 0; payload[pos] != 0;)
                {
                    while (payload[pos] != 0) pos++;
                    pos++;
                    bytesPerPixel += BitConverter.ToInt32(payload, pos) == 1 ? 2 : 4;
                    pos += 16;
                }
            }
        }
        scanlineBytes = width * bytesPerPixel;

        int blockCount = (height + 15) / 16;
        var offsets = new long[blockCount];
        for (int i = 0; i < blockCount; i++) offsets[i] = br.ReadInt64();

        var chunks = new List<(int, int)>();
        for (int i = 0; i < blockCount; i++)
        {
            fs.Position = offsets[i];
            int y = br.ReadInt32();
            int dataSize = br.ReadInt32();
            chunks.Add((Math.Min(16, height - y), dataSize));
        }
        return chunks;
    }

    private static string ReadNullString(BinaryReader br)
    {
        var sb = new StringBuilder();
        byte b;
        while ((b = br.ReadByte()) != 0) sb.Append((char)b);
        return sb.ToString();
    }
}

/// <summary>
/// End-to-end: a captured linear beauty written via
/// <see cref="ExrImage.WriteRgb"/> reads back exactly half-quantized —
/// the `-o *.exr` output path preserves the renderer's scene-referred
/// radiance up to half precision.
/// </summary>
[Collection("SamplerExclusive")]
public class ExrRenderOutputTests
{
    [Fact]
    public void RenderedBeauty_WrittenAsExr_ReadsBackHalfQuantized()
    {
        Sampler.SetKind(SamplerKind.Sobol);
        const int W = 32, H = 32;

        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, new Lambertian(new Vector3(0.7f, 0.3f, 0.2f))),
            new Sphere(new Vector3(0f, 0.5f, 0f), 0.5f, new Metal(new Vector3(0.95f, 0.95f, 0.95f), fuzz: 0f)),
        });
        var light = new SphereLight(
            center: new Vector3(0f, 3f, 2f), radius: 0.3f,
            color: Vector3.One, intensity: 10f, shadowSamples: 1);
        var camera = new RayTracer.Camera.Camera(
            lookFrom: new Vector3(0f, 1.2f, 4f), lookAt: new Vector3(0f, 0.5f, 0f),
            vUp: Vector3.UnitY, vFovDeg: 45f, aspectRatio: (float)W / H,
            aperture: 0f, focusDist: 1f);
        var renderer = new Renderer(
            world, camera, new List<ILight> { light },
            new SkySettings(new Vector3(0.4f, 0.5f, 0.7f)),
            samplesPerPixel: 8, maxDepth: 6);

        var result = renderer.Render(W, H, new RenderCaptureOptions { CaptureBeautyHalves = true });
        var beauty = result.Buffers!.Beauty;

        string path = Path.Combine(Path.GetTempPath(), $"exr-render-{Guid.NewGuid():N}.exr");
        try
        {
            ExrImage.WriteRgb(path, beauty);
            var (channels, w, h) = ExrLoader.LoadChannels(path);

            Assert.Equal((W, H), (w, h));
            int n = W * H;
            foreach (var (name, c) in new[] { ("R", 0), ("G", 1), ("B", 2) })
            {
                float[] plane = channels.Single(ch => ch.Name == name).Plane;
                for (int i = 0; i < n; i++)
                    Assert.Equal((float)(Half)beauty.Data[c * n + i], plane[i]);
            }
        }
        finally { File.Delete(path); }
    }
}
