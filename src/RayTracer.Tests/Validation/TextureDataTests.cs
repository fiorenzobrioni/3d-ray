using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Scene;
using RayTracer.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Image data as the renderer reads it (PIANO-REVIEW.md, Phase 4): colour
/// images are decoded from sRGB, data maps are used as stored (§3.3), 16-bit
/// sources keep their precision, and the HDR formats (Radiance RGBE, OpenEXR
/// half and float) decode to the values written.
/// </summary>
[Collection("SceneLoader")]
public class TextureDataTests
{
    private static string TempPng<TPixel>(TPixel pixel) where TPixel : unmanaged, IPixel<TPixel>
    {
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.png");
        using var img = new Image<TPixel>(4, 4, pixel);
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void EightBitColour_IsDecodedFromSrgb_DataIsNot()
    {
        string path = TempPng(new Rgba32(128, 64, 10, 255));
        try
        {
            var srgb = new ImageTexture(path, colorSpace: TextureColorSpace.Srgb).Value(0.5f, 0.5f, Vector3.Zero, 0);
            var raw = new ImageTexture(path, colorSpace: TextureColorSpace.Linear).Value(0.5f, 0.5f, Vector3.Zero, 0);
            Assert.Equal(Srgb.Decode(128 / 255f), srgb.X, 5);
            Assert.Equal(Srgb.Decode(10 / 255f), srgb.Z, 5);
            Assert.Equal(128 / 255f, raw.X, 5);
            Assert.Equal(10 / 255f, raw.Z, 5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SixteenBitData_KeepsItsPrecision()
    {
        // 30001 / 65535: an 8-bit read would land on 117 / 255, 4e-4 away.
        string path = TempPng(new Rgba64(30001, 30001, 30001, 65535));
        try
        {
            var v = new ImageTexture(path, colorSpace: TextureColorSpace.Linear).Value(0.5f, 0.5f, Vector3.Zero, 0);
            Assert.Equal(30001 / 65535f, v.X, 6);
        }
        finally { File.Delete(path); }
    }

    private static string WriteHdr(string resolution, byte[] rgbe)
    {
        // Flat (non-RLE) RGBE scanlines.
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.hdr");
        using var f = File.Create(path);
        f.Write(System.Text.Encoding.ASCII.GetBytes($"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n{resolution}\n"));
        f.Write(rgbe);
        return path;
    }

    [Fact]
    public void RadianceHdr_DecodesTheSharedExponent()
    {
        // value = (mantissa + 0.5) × 2^(exponent − 136): the writer truncated
        // the mantissa, so the middle of its interval is the unbiased value.
        string path = WriteHdr("-Y 1 +X 2", new byte[] { 128, 64, 192, 130, 200, 100, 0, 140 });
        try
        {
            var (px, w, h) = HdrLoader.Load(path);
            Assert.Equal((2, 1), (w, h));
            Assert.Equal(128.5f / 64f, px[0], 6);
            Assert.Equal(64.5f / 64f, px[1], 6);
            Assert.Equal(192.5f / 64f, px[2], 6);
            Assert.Equal(200.5f * 16f, px[3], 3);
            Assert.Equal(0.5f * 16f, px[5], 5);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("-Y 2 +X 2", new[] { 1, 2, 3, 4 })]   // rows top first, left to right
    [InlineData("+Y 2 +X 2", new[] { 3, 4, 1, 2 })]   // rows bottom first
    [InlineData("-Y 2 -X 2", new[] { 2, 1, 4, 3 })]   // each row right to left
    public void RadianceHdr_ReturnsTopRowFirst_WhateverTheFileOrder(string resolution, int[] expected)
    {
        // Pixel k of the file holds the value k (mantissa 64·k at exponent 130).
        var bytes = new List<byte>();
        for (int k = 1; k <= 4; k++) bytes.AddRange(new byte[] { (byte)(64 * k - 1), 0, 0, 130 });
        string path = WriteHdr(resolution, bytes.ToArray());
        try
        {
            var (px, _, _) = HdrLoader.Load(path);
            for (int i = 0; i < 4; i++)
                Assert.Equal(expected[i], px[i * 3], 1);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OpenExr_GreyscaleY_FillsAllThreeChannels_AndDataKeepsItsSign()
    {
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.exr");
        var plane = new float[16];
        Array.Fill(plane, -0.25f);
        ExrImage.Write(path, 4, 4, new[] { new ExrImage.Channel("Y", ExrPixelType.Float, plane) });
        try
        {
            var data = new ImageTexture(path, colorSpace: TextureColorSpace.Linear).Value(0.5f, 0.5f, Vector3.Zero, 0);
            Assert.Equal(new Vector3(-0.25f), data);
            var colour = new ImageTexture(path, colorSpace: TextureColorSpace.Srgb).Value(0.5f, 0.5f, Vector3.Zero, 0);
            Assert.Equal(Vector3.Zero, colour);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OpenExr_RleBlock_DecodesToTheRawScanline()
    {
        // A scanline of 2 float channels × 8 pixels, with runs and literals,
        // encoded the way the format stores it: predictor + interleave, then
        // runs (count + 1 repeats) and literal spans (−count bytes).
        var raw = new byte[64];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i < 40 ? 7 : i * 13);
        byte[] pre = ExrImage.ZipDeconstruct(raw);
        var rle = new List<byte>();
        int pos = 0;
        while (pos < pre.Length)
        {
            int run = 1;
            while (pos + run < pre.Length && pre[pos + run] == pre[pos] && run < 128) run++;
            if (run >= 3) { rle.Add((byte)(run - 1)); rle.Add(pre[pos]); pos += run; continue; }
            int lit = 1;
            while (pos + lit < pre.Length && lit < 127
                   && !(pos + lit + 2 < pre.Length && pre[pos + lit] == pre[pos + lit + 1] && pre[pos + lit] == pre[pos + lit + 2]))
                lit++;
            rle.Add(unchecked((byte)(sbyte)(-lit)));
            for (int k = 0; k < lit; k++) rle.Add(pre[pos + k]);
            pos += lit;
        }
        Assert.Equal(raw, ExrLoader.DecompressRle(rle.ToArray(), raw.Length));
    }

    [Theory]
    [InlineData(ExrPixelType.Half)]
    [InlineData(ExrPixelType.Float)]
    public void OpenExr_AsAnImageTexture_IsLinearAndUnclamped(ExrPixelType type)
    {
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.exr");
        const int w = 4, h = 4;
        float[] Plane(float v) { var p = new float[w * h]; Array.Fill(p, v); return p; }
        ExrImage.Write(path, w, h, new[]
        {
            new ExrImage.Channel("B", type, Plane(0.125f)),
            new ExrImage.Channel("G", type, Plane(0.3333f)),
            new ExrImage.Channel("R", type, Plane(7.5f)),
        });
        try
        {
            // An HDR file holds linear light whatever colour space is asked for.
            var tex = new ImageTexture(path, colorSpace: TextureColorSpace.Srgb);
            Assert.Equal(TextureColorSpace.Linear, tex.ColorSpace);
            var v = tex.Value(0.5f, 0.5f, Vector3.Zero, 0);
            float tol = type == ExrPixelType.Half ? 2e-3f : 1e-6f;
            Assert.InRange(v.X, 7.5f * (1 - tol), 7.5f * (1 + tol));
            Assert.InRange(v.Y, 0.3333f * (1 - tol), 0.3333f * (1 + tol));
            Assert.InRange(v.Z, 0.125f * (1 - tol), 0.125f * (1 + tol));
        }
        finally { File.Delete(path); }
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 3)]
    [InlineData(1000, 7)]
    [InlineData(64, 64)]
    public void MipPyramid_KeepsTheMean_ForAnySize(int w, int h)
    {
        // The coarsest level is the mean of the image: an odd size used to
        // drop its last row or column at every level.
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.png");
        var rng = new Random(w * 31 + h);
        double sum = 0;
        using (var img = new Image<Rgba64>(w, h))
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    ushort g = (ushort)rng.Next(65536);
                    img[x, y] = new Rgba64(g, g, g, 65535);
                    sum += g / 65535.0;
                }
            img.SaveAsPng(path);
        }
        try
        {
            var tex = new ImageTexture(path, colorSpace: TextureColorSpace.Linear);
            var huge = new FilterFootprint(Vector3.One, Vector3.One, 1e3f, 0f, 0f, 1e3f);
            var v = tex.Value(0.3f, 0.6f, Vector3.Zero, 0, huge);
            Assert.Equal(sum / (w * h), v.X, 4);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NonSquareImage_PicksItsLevelInTexels()
    {
        // 4 × 256, columns alternating black and white. The footprint is a
        // quarter of a texel across u and two texels along v (where the image
        // is constant): level 0 is the right one and a column keeps its value.
        // Measuring u in units of the long side saw 64 texels across u and
        // blurred the columns to grey.
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.png");
        using (var img = new Image<Rgba64>(4, 256))
        {
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 4; x++)
                    img[x, y] = x % 2 == 1 ? new Rgba64(65535, 65535, 65535, 65535) : new Rgba64(0, 0, 0, 65535);
            img.SaveAsPng(path);
        }
        try
        {
            var tex = new ImageTexture(path, colorSpace: TextureColorSpace.Linear);
            var fp = new FilterFootprint(Vector3.UnitX, Vector3.UnitY, 1f / 16f, 0f, 0f, 1f / 128f);
            var v = tex.Value(0.375f, 0.5f, Vector3.Zero, 0, fp);   // centre of column 1
            Assert.InRange(v.X, 0.99f, 1.0001f);
        }
        finally { File.Delete(path); }
    }

    // ── The loader: colour space by role ─────────────────────────────────────

    private static OpenPbrParameters LoadOpenPbr(string materialBody, string imagePath)
    {
        string dir = Path.GetDirectoryName(imagePath)!;
        string yaml = $@"
materials:
  - id: ""m""
    type: ""openpbr""
{materialBody.Replace("IMG", Path.GetFileName(imagePath))}
entities:
  - {{ type: ""sphere"", center: [0, 0, -3], radius: 1.0, material: ""m"" }}
format_version: 2
cameras:
  - position: [0, 0, 0]
    look_at: [0, 0, -1]
";
        string path = Path.Combine(dir, $"3dray-scene-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var (world, _, _, _, _, _) = SceneLoader.Load(path, 16, 16);
            var rec = new HitRecord();
            Assert.True(world.Hit(new Ray(Vector3.Zero, new Vector3(0, 0, -1)), MathUtils.Epsilon, MathUtils.Infinity, ref rec));
            return ((OpenPbr)rec.Material!).Parameters;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Loader_ReadsColourAsSrgb_AndDataAsRaw_UnlessTold()
    {
        string img = TempPng(new Rgba32(128, 128, 128, 255));
        try
        {
            var rec = new HitRecord { U = 0.5f, V = 0.5f };
            var p = LoadOpenPbr(@"    base_color: { type: image, path: IMG }
    specular_roughness: { type: image, path: IMG }
    coat_weight: { type: image, path: IMG, color_space: srgb }", img);
            Assert.Equal(Srgb.Decode(128 / 255f), p.BaseColor.Value(in rec).X, 5);       // colour: sRGB
            Assert.Equal(128 / 255f, p.SpecularRoughness.Value(in rec), 5);               // data: raw
            Assert.Equal(Srgb.Decode(128 / 255f), p.CoatWeight.Value(in rec), 5);        // told: sRGB
        }
        finally { File.Delete(img); }
    }
}
