using System.Diagnostics;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using RayTracer.Rendering;
using RayTracer.Scene;

namespace RayTracer;

class Program
{
    static void Main(string[] args)
    {
        // Parse CLI arguments
        string inputPath = GetArg(args, "--input", "-i") ?? "scenes/sample.yaml";
        string outputPath = GetArg(args, "--output", "-o") ?? "render.png";
        int width = int.TryParse(GetArg(args, "--width", null), out var w) ? w : 0;
        int height = int.TryParse(GetArg(args, "--height", null), out var h) ? h : 0;
        int samples = int.TryParse(GetArg(args, "--samples", "-s"), out var s) ? s : 16;
        int depth = int.TryParse(GetArg(args, "--depth", "-d"), out var d) ? d : 50;

        // If resolution not specified via CLI, fallback
        if (width <= 0) width = 1280;
        if (height <= 0) height = 720;

        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║       RayTracer .NET 10 Engine           ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Scene:       {inputPath}");
        Console.WriteLine($"  Output:      {outputPath}");
        Console.WriteLine($"  Resolution:  {width} x {height}");
        Console.WriteLine($"  Samples/px:  {samples}");
        Console.WriteLine($"  Max depth:   {depth}");
        Console.WriteLine();

        // Load scene
        Console.Write("Loading scene... ");
        var sw = Stopwatch.StartNew();
        var (world, camera, lights, ambientLight, background) =
            SceneLoader.Load(inputPath, width, height);
        Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"  Lights: {lights.Count}");
        Console.WriteLine();

        // Render
        var renderer = new Renderer(world, camera, lights, ambientLight, background, samples, depth);
        sw.Restart();
        var pixels = renderer.Render(width, height);
        var elapsed = sw.Elapsed;
        Console.WriteLine($"Render completed in {elapsed.TotalSeconds:F2}s");

        // Save image
        Console.Write($"Saving {outputPath}... ");
        SaveImage(pixels, width, height, outputPath);
        Console.WriteLine("done!");
        Console.WriteLine();
        Console.WriteLine($"Output saved to: {Path.GetFullPath(outputPath)}");
    }

    static void SaveImage(Vector3[,] pixels, int width, int height, string path)
    {
        using var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y, x];
                byte r = (byte)(Math.Clamp(c.X, 0f, 1f) * 255.99f);
                byte g = (byte)(Math.Clamp(c.Y, 0f, 1f) * 255.99f);
                byte b = (byte)(Math.Clamp(c.Z, 0f, 1f) * 255.99f);
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        // Determine format from extension
        string ext = Path.GetExtension(path).ToLowerInvariant();
        using var stream = File.Create(path);
        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                image.SaveAsJpeg(stream);
                break;
            case ".bmp":
                image.SaveAsBmp(stream);
                break;
            default:
                image.SaveAsPng(stream);
                break;
        }
    }

    static string? GetArg(string[] args, string longName, string? shortName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == longName || (shortName != null && args[i] == shortName))
                return args[i + 1];
        }
        return null;
    }
}
