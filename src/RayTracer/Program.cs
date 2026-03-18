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
        // Show help if no arguments or help requested
        if (args.Length == 0 || GetArg(args, "--help", "-h") != null)
        {
            ShowHelp();
            return;
        }

        // Parse CLI arguments
        string? inputPath = GetArg(args, "--input", "-i");
        string outputPath = GetArg(args, "--output", "-o") ?? "render.png";
        
        bool wParsed = int.TryParse(GetArg(args, "--width", null), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var width);
        bool hParsed = int.TryParse(GetArg(args, "--height", null), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var height);
        bool sParsed = int.TryParse(GetArg(args, "--samples", "-s"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var samples);
        bool dParsed = int.TryParse(GetArg(args, "--depth", "-d"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var depth);

        // Required argument check
        if (string.IsNullOrEmpty(inputPath))
        {
            Console.WriteLine("Error: Missing required argument --input (-i)");
            Console.WriteLine();
            ShowHelp();
            return;
        }

        // File existence check
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: Scene file not found: {inputPath}");
            return;
        }

        // Default values and validation
        if (!wParsed || width <= 0) width = 1200;
        if (!hParsed || height <= 0) height = 800;
        if (!sParsed || samples <= 0) samples = 16;
        if (!dParsed || depth <= 0) depth = 50;

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
        try 
        {
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
        catch (Exception ex)
        {
            Console.WriteLine("failed!");
            Console.WriteLine($"Error loading or rendering scene: {ex.Message}");
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("Usage: RayTracer --input <file> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <file>    Path to the YAML scene file (Required)");
        Console.WriteLine("  -o, --output <file>   Path to the output image (Default: render.png)");
        Console.WriteLine("  --width <int>         Image width (Default: 1200)");
        Console.WriteLine("  --height <int>        Image height (Default: 800)");
        Console.WriteLine("  -s, --samples <int>   Samples per pixel (Default: 16)");
        Console.WriteLine("  -d, --depth <int>     Maximum ray recursion depth (Default: 50)");
        Console.WriteLine("  -h, --help            Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  RayTracer -i scenes/chess.yaml -o my_render.png --width 1920 --height 1080 -s 64");
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
