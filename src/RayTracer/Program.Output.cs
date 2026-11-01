using System.Diagnostics;
using System.Numerics;
using RayTracer.Imaging;
using RayTracer.Core.Sampling;
using RayTracer.Denoising;
using RayTracer.Rendering;
using RayTracer.Rendering.Guiding;
using RayTracer.Scene;
using RayTracer.Volumetrics;

namespace RayTracer;

// Program, part: Writing the image, the EXR beauty and the AOVs.
partial class Program
{
    /// <summary>
    /// Writes the image (display, or linear EXR with the AOVs as layers) and
    /// the AOV files, creating the output folder.
    /// </summary>
    static void SaveOutputs(RunSettings s, RenderResult result, Vector3[,] pixels, FrameBuffer? denoisedBeauty)
    {
        string? outputDir = Path.GetDirectoryName(s.OutputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        if (s.ExrOutput)
        {
            var beauty = denoisedBeauty ?? result.Buffers!.Beauty;
            int embedded = SaveExrBeauty(s.OutputPath, beauty, result.Buffers!, s.EmbedAovs ? s.Aovs : null);
            Console.WriteLine();
            Console.WriteLine($"  \u2713 Saved: {Path.GetFullPath(s.OutputPath)} " +
                              $"(linear EXR{(embedded > 0 ? $", +{embedded} AOV layers" : "")})");
        }
        else
        {
            int bitDepth = s.BitDepth;
            string ext = Path.GetExtension(s.OutputPath).ToLowerInvariant();
            if (bitDepth == 16 && ext is not (".png" or ""))
            {
                Console.WriteLine($"  Warning: --bit-depth 16 needs a .png output; writing 8-bit {ext}.");
                bitDepth = 8;
            }
            SaveImage(pixels, s.Width, s.Height, s.OutputPath, bitDepth, s.Dither);
            Console.WriteLine();
            Console.WriteLine($"  \u2713 Saved: {Path.GetFullPath(s.OutputPath)}");
        }

        if (s.Aovs.Count > 0 && !s.EmbedAovs)
            SaveAovs(result.Buffers!, s.Aovs, s.OutputPath, s.AovFormat ?? "pfm", denoisedBeauty);
    }

    /// <summary>
    /// Formats a TimeSpan as a human-readable string:
    /// under 60s  → "42.18s"
    /// under 60m  → "5m 42.18s"
    /// 60m+       → "1h 05m 42s"
    /// </summary>
    static string FormatElapsed(TimeSpan t)
    {
        if (t.TotalMinutes < 1)
            return $"{t.TotalSeconds:F2}s";
        if (t.TotalHours < 1)
            return $"{(int)t.TotalMinutes}m {t.Seconds + t.Milliseconds / 1000.0:F2}s";
        return $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds}s";
    }

    /// <summary>
    /// Writes the requested AOV planes as one file each next to the main
    /// output: <c>renders/foo.png</c> → <c>renders/foo.albedo.pfm</c> (or
    /// <c>.exr</c>) etc. All values are linear, scene-referred (pre-exposure,
    /// pre-tonemap); the beauty AOV is the denoised buffer when a denoiser is
    /// active.
    /// </summary>
    static void SaveAovs(RenderBuffers buffers, HashSet<string> aovs, string outputPath,
                         string format, FrameBuffer? denoisedBeauty = null)
    {
        string stem = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? "",
            Path.GetFileNameWithoutExtension(outputPath));

        foreach (string aov in aovs)
        {
            string path = $"{stem}.{aov}.{format}";
            FrameBuffer fb = ResolveAov(buffers, aov, denoisedBeauty);
            if (format == "exr")
                ExrImage.Write(path, fb.Width, fb.Height, AovExrChannels(aov, fb, embedded: false));
            else
                fb.SavePfm(path);
            Console.WriteLine($"  ✓ Saved: {Path.GetFullPath(path)}");
        }
    }

    static FrameBuffer ResolveAov(RenderBuffers buffers, string aov, FrameBuffer? denoisedBeauty) =>
        aov switch
        {
            "beauty"   => denoisedBeauty ?? buffers.Beauty,
            "albedo"   => buffers.CombineHalves(buffers.AlbedoA!, buffers.AlbedoB!),
            "normal"   => buffers.CombineHalves(buffers.NormalA!, buffers.NormalB!),
            "depth"    => buffers.CombineDepthHalves(),
            "variance" => buffers.RawBeautyVariance(),
            _          => throw new InvalidOperationException($"unreachable AOV '{aov}'"),
        };

    /// <summary>
    /// Writes the scene-linear (pre-exposure, pre-tonemap) beauty as a
    /// half-float RGB EXR; when <paramref name="embedAovs"/> is non-null the
    /// requested AOVs become extra layers of the same multilayer file
    /// (<c>albedo.R…</c>, <c>normal.X…</c>, float <c>Z</c>,
    /// <c>variance.R…</c>). Returns the number of embedded AOV layers.
    /// </summary>
    static int SaveExrBeauty(string path, FrameBuffer beauty, RenderBuffers buffers,
                             HashSet<string>? embedAovs)
    {
        var channels = new List<ExrImage.Channel>(AovExrChannels("beauty", beauty, embedded: true));
        int embedded = 0;
        if (embedAovs != null)
        {
            foreach (string aov in embedAovs)
            {
                if (aov == "beauty")
                {
                    // The main R,G,B channels ARE the (denoised) beauty.
                    Console.WriteLine("  Note: --aov beauty is already the main EXR output — layer skipped.");
                    continue;
                }
                channels.AddRange(AovExrChannels(aov, ResolveAov(buffers, aov, null), embedded: true));
                embedded++;
            }
        }
        ExrImage.Write(path, beauty.Width, beauty.Height, channels);
        return embedded;
    }

    /// <summary>
    /// EXR channel layout for one AOV buffer. Embedded layers follow the
    /// <c>layer.channel</c> convention compositors group on (suffix R/G/B for
    /// colour data, X/Y/Z for vectors, a bare float <c>Z</c> for depth — the
    /// no-hit sentinel stays −1); standalone files use plain <c>R,G,B</c> so
    /// any viewer opens them without layer selection. Colour channels are
    /// half, depth is float32.
    /// </summary>
    static List<ExrImage.Channel> AovExrChannels(string aov, FrameBuffer fb, bool embedded)
    {
        int n = fb.Width * fb.Height;
        var data = fb.Data.AsMemory();
        if (aov == "depth")
            return new() { new ExrImage.Channel("Z", ExrPixelType.Float, data.Slice(0, n)) };

        string[] names = aov switch
        {
            "normal" when embedded => new[] { "normal.X", "normal.Y", "normal.Z" },
            "beauty"               => new[] { "R", "G", "B" },
            _ when embedded        => new[] { $"{aov}.R", $"{aov}.G", $"{aov}.B" },
            _                      => new[] { "R", "G", "B" },
        };
        return new()
        {
            new ExrImage.Channel(names[0], ExrPixelType.Half, data.Slice(0, n)),
            new ExrImage.Channel(names[1], ExrPixelType.Half, data.Slice(n, n)),
            new ExrImage.Channel(names[2], ExrPixelType.Half, data.Slice(2 * n, n)),
        };
    }

    static void SaveImage(Vector3[,] pixels, int width, int height, string path, int bitDepth, bool dither)
    {
        // 16 bits only for PNG (65536 levels do not band, no dithering);
        // JPEG and BMP hold 8.
        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool sixteen = bitDepth == 16 && ext is not (".jpg" or ".jpeg" or ".bmp");
        var image = new RasterImage(width, height, 3, sixteen ? 16 : 8);
        var s = image.Samples;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y, x];
                int o = (y * width + x) * 3;
                if (sixteen)
                {
                    s[o] = DisplayQuantizer.To16Bit(c.X);
                    s[o + 1] = DisplayQuantizer.To16Bit(c.Y);
                    s[o + 2] = DisplayQuantizer.To16Bit(c.Z);
                }
                else
                {
                    s[o] = DisplayQuantizer.To8Bit(c.X, x, y, 0, dither);
                    s[o + 1] = DisplayQuantizer.To8Bit(c.Y, x, y, 1, dither);
                    s[o + 2] = DisplayQuantizer.To8Bit(c.Z, x, y, 2, dither);
                }
            }
        ImageFile.Save(path, image, srgb: true);
    }
}
