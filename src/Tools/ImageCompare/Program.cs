using System.Globalization;
using System.Text.Json;
using RayTracer.Rendering;
using RayTracer.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageCompare;

/// <summary>
/// Compares a linear-HDR render against a reference (PIANO-REVIEW.md, Phase 0).
/// Inputs are scene-linear PFM or EXR. The metrics are the ones the validation
/// literature uses for Monte Carlo renders:
///
///   mean ratio   mean luminance of test / reference: a global energy bias
///   relMSE       mean of (t − r)² / (r² + ε), ε = 0.01 (Rousselle et al. 2011)
///   RMSE         root mean squared error
///   SMAPE        symmetric mean absolute percentage error
///   bad pixels   share of pixels whose relative luminance error exceeds 10%
///
/// Optionally writes a false-colour map of the signed relative luminance error
/// (blue = test darker, red = test brighter, white = equal).
/// </summary>
internal static class Program
{
    private const float RelMseEpsilon = 0.01f;

    private static int Main(string[] args)
    {
        if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("Usage: ImageCompare <test.(pfm|exr)> <reference.(pfm|exr)> [options]");
            Console.WriteLine("  --diff <out.png>     false-colour map of the signed relative luminance error");
            Console.WriteLine("  --diff-scale <f>     relative error mapped to full colour (default 0.25)");
            Console.WriteLine("  --crop x,y,w,h       compare only this window");
            Console.WriteLine("  --json               print the metrics as one JSON object");
            return args.Length < 2 ? 1 : 0;
        }

        string? diffPath = Option(args, "--diff");
        float diffScale = float.Parse(Option(args, "--diff-scale") ?? "0.25", CultureInfo.InvariantCulture);
        string? crop = Option(args, "--crop");
        bool json = args.Contains("--json");

        var test = Load(args[0]);
        var reference = Load(args[1]);
        if (test.Width != reference.Width || test.Height != reference.Height)
        {
            Console.Error.WriteLine($"Size mismatch: {test.Width}x{test.Height} vs {reference.Width}x{reference.Height}");
            return 2;
        }

        int x0 = 0, y0 = 0, w = test.Width, h = test.Height;
        if (crop != null)
        {
            var p = crop.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            (x0, y0, w, h) = (p[0], p[1], p[2], p[3]);
        }

        var m = Compare(test, reference, x0, y0, w, h);

        if (diffPath != null)
            WriteDiff(test, reference, x0, y0, w, h, diffScale, diffPath);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(m));
        }
        else
        {
            Console.WriteLine($"  mean test    {m.MeanTest:0.000000}   mean reference {m.MeanReference:0.000000}");
            Console.WriteLine($"  mean ratio   {m.MeanRatio:0.0000}");
            Console.WriteLine($"  relMSE       {m.RelMse:0.000e0}");
            Console.WriteLine($"  RMSE         {m.Rmse:0.000e0}");
            Console.WriteLine($"  SMAPE        {m.Smape:0.0000}");
            Console.WriteLine($"  bad pixels   {m.BadPixelFraction:P2} (|rel err| > 10%)");
            if (m.NonFinitePixels > 0)
                Console.WriteLine($"  non-finite   {m.NonFinitePixels} pixels in the test image");
        }
        return 0;
    }

    private sealed record Image(float[] Planes, int Width, int Height)
    {
        public float Get(int c, int x, int y) => Planes[c * Width * Height + y * Width + x];
        public float Lum(int x, int y) => 0.2126f * Get(0, x, y) + 0.7152f * Get(1, x, y) + 0.0722f * Get(2, x, y);
    }

    public sealed record Metrics(
        double MeanTest, double MeanReference, double MeanRatio,
        double RelMse, double Rmse, double Smape, double BadPixelFraction, int NonFinitePixels);

    private static Image Load(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pfm")
        {
            var (planes, width, height, channels) = PfmImage.Read(path);
            if (channels == 1)
            {
                var rgb = new float[width * height * 3];
                for (int c = 0; c < 3; c++) Array.Copy(planes, 0, rgb, c * width * height, width * height);
                planes = rgb;
            }
            return new Image(planes, width, height);
        }
        if (ext == ".exr")
        {
            var (channels, width, height) = ExrLoader.LoadChannels(path);
            var planes = new float[width * height * 3];
            string[] names = { "R", "G", "B" };
            for (int c = 0; c < 3; c++)
            {
                var ch = channels.FirstOrDefault(k => k.Name == names[c])
                         ?? throw new InvalidDataException($"{path}: missing channel {names[c]}");
                Array.Copy(ch.Plane, 0, planes, c * width * height, width * height);
            }
            return new Image(planes, width, height);
        }
        throw new InvalidDataException($"Unsupported format '{ext}' (pfm or exr expected).");
    }

    private static Metrics Compare(Image t, Image r, int x0, int y0, int w, int h)
    {
        double sumT = 0, sumR = 0, relMse = 0, mse = 0, smape = 0;
        long n = 0, bad = 0;
        int nonFinite = 0;
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
        {
            float lt = t.Lum(x, y), lr = r.Lum(x, y);
            if (!float.IsFinite(lt)) { nonFinite++; continue; }
            sumT += lt; sumR += lr;
            for (int c = 0; c < 3; c++)
            {
                double a = t.Get(c, x, y), b = r.Get(c, x, y), d = a - b;
                relMse += d * d / (b * b + RelMseEpsilon);
                mse += d * d;
                double denom = Math.Abs(a) + Math.Abs(b);
                if (denom > 1e-8) smape += Math.Abs(d) / denom;
            }
            if (Math.Abs(lt - lr) > 0.1 * Math.Max(lr, 1e-3)) bad++;
            n++;
        }
        double channels = 3.0 * n;
        return new Metrics(
            sumT / n, sumR / n, sumR > 0 ? sumT / sumR : double.NaN,
            relMse / channels, Math.Sqrt(mse / channels), smape / channels,
            (double)bad / n, nonFinite);
    }

    private static void WriteDiff(Image t, Image r, int x0, int y0, int w, int h, float scale, string path)
    {
        using var img = new Image<Rgb24>(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float lt = t.Lum(x0 + x, y0 + y), lr = r.Lum(x0 + x, y0 + y);
            float rel = (lt - lr) / MathF.Max(lr, 1e-3f);
            float k = Math.Clamp(MathF.Abs(rel) / scale, 0f, 1f);
            // White at zero error, saturating to red (brighter) or blue (darker).
            byte fade = (byte)(255 * (1f - k));
            img[x, y] = rel >= 0 ? new Rgb24(255, fade, fade) : new Rgb24(fade, fade, 255);
        }
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        img.SaveAsPng(path);
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
