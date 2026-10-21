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

    [Fact]
    public void RadianceHdr_DecodesTheSharedExponent()
    {
        // Flat (non-RLE) RGBE: value = mantissa × 2^(exponent − 136).
        string path = Path.Combine(Path.GetTempPath(), $"3dray-tex-{Guid.NewGuid():N}.hdr");
        using (var f = File.Create(path))
        {
            var header = System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 2\n");
            f.Write(header);
            f.Write(new byte[] { 128, 64, 192, 130, 200, 100, 50, 140 });
        }
        try
        {
            var (px, w, h) = HdrLoader.Load(path);
            Assert.Equal((2, 1), (w, h));
            Assert.InRange(px[0], 1.99f, 2.02f);
            Assert.InRange(px[1], 0.99f, 1.01f);
            Assert.InRange(px[2], 2.99f, 3.02f);
            Assert.InRange(px[3], 200f * 16f * 0.995f, 200f * 16f * 1.005f);
        }
        finally { File.Delete(path); }
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
camera:
  position: [0, 0, 0]
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
