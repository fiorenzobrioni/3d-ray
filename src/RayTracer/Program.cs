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
        if (args.Length == 0 || HasFlag(args, "--help", "-h"))
        {
            ShowHelp();
            return;
        }

        // Parse CLI arguments
        string? inputPath = GetArg(args, "--input", "-i");
        string? outputArg = GetArg(args, "--output", "-o");
        string outputPath;

        if (outputArg != null)
        {
            outputPath = outputArg;
        }
        else if (!string.IsNullOrEmpty(inputPath))
        {
            // Default to "output/render-<scene>.png"
            string sceneName = Path.GetFileNameWithoutExtension(inputPath);
            outputPath = Path.Combine("output", $"render-{sceneName}.png");
        }
        else
        {
            outputPath = Path.Combine("output", "render.png");
        }

        bool wParsed = int.TryParse(GetArg(args, "--width",   "-w"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var width);
        bool hParsed = int.TryParse(GetArg(args, "--height",  "-H"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var height);
        bool sParsed = int.TryParse(GetArg(args, "--samples", "-s"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var samples);
        bool dParsed = int.TryParse(GetArg(args, "--depth",   "-d"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var depth);

        // Shadow samples CLI override (null = use per-light YAML values)
        int? shadowSamplesOverride = null;
        if (int.TryParse(GetArg(args, "--shadow-samples", "-S"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ssOverride) && ssOverride > 0)
            shadowSamplesOverride = ssOverride;

        // Camera selector: name or zero-based index
        string? cameraSelector = GetArg(args, "--camera", "-c");

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

        // --list-cameras: print available cameras and exit
        if (HasFlag(args, "--list-cameras", null))
        {
            SceneLoader.TryListCameras(inputPath);
            return;
        }

        // Default values and validation
        if (!wParsed || width  <= 0) width   = 1200;
        if (!hParsed || height <= 0) height  = 800;
        if (!sParsed || samples <= 0) samples = 16;
        if (!dParsed || depth  <= 0) depth   = 50;

        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║       RayTracer .NET 10 Engine           ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  Scene:       {inputPath}");
        Console.WriteLine($"  Output:      {outputPath}");
        Console.WriteLine($"  Resolution:  {width} x {height}");
        Console.WriteLine($"  Samples/px:  {samples}");
        Console.WriteLine($"  Max depth:   {depth}");
        if (shadowSamplesOverride.HasValue)
            Console.WriteLine($"  Shadow smp:  {shadowSamplesOverride.Value} (CLI override)");
        if (cameraSelector != null)
            Console.WriteLine($"  Camera:      {cameraSelector}");
        Console.WriteLine();

        // Load scene
        Console.Write("Loading scene... ");
        var sw = Stopwatch.StartNew();
        try
        {
            var (world, camera, lights, ambientLight, sky) =
                SceneLoader.Load(inputPath, width, height, shadowSamplesOverride, cameraSelector);
            Console.WriteLine($"done ({sw.ElapsedMilliseconds} ms)");
            SceneLoader.FlushMessages();
            Console.WriteLine($"  Lights: {lights.Count}");
            string skyDesc = sky.Mode switch
            {
                SkySettings.SkyMode.Hdri     => "HDRI environment map",
                SkySettings.SkyMode.Gradient => "gradient" + (sky.HasSun ? " + sun disk" : ""),
                _                            => "flat"
            };
            Console.WriteLine($"  Sky:    {skyDesc}");
            Console.WriteLine();

            // Render
            var renderer = new Renderer(world, camera, lights, ambientLight, sky, samples, depth);
            sw.Restart();
            var pixels = renderer.Render(width, height);
            var elapsed = sw.Elapsed;
            Console.WriteLine($"Render completed in {elapsed.TotalSeconds:F2}s");

            // Save image
            Console.Write($"Saving {outputPath}... ");

            // Ensure output directory exists
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

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
        Console.WriteLine("RayTracer .NET 10 Engine");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/RayTracer/RayTracer.csproj -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input <path>           Scene YAML file (required)");
        Console.WriteLine("  -o, --output <path>          Output image (default: output/render-<scene>.png)");
        Console.WriteLine("  -w, --width <px>             Image width  (default: 1200)");
        Console.WriteLine("  -H, --height <px>            Image height (default: 800)");
        Console.WriteLine("  -s, --samples <n>            Samples per pixel (default: 16)");
        Console.WriteLine("  -d, --depth <n>              Max ray depth (default: 50)");
        Console.WriteLine("  -S, --shadow-samples <n>     Area light shadow samples override");
        Console.WriteLine("  -c, --camera <name|index>    Select camera by name or 0-based index");
        Console.WriteLine("      --list-cameras           List all cameras in the scene and exit");
        Console.WriteLine("  -h, --help                   Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  from the root of the project: ");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml --list-cameras");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml -c top -o top.png");
        Console.WriteLine("  dotnet run ... -- -i scenes/chess.yaml -c 2 -o cam2.png");
        Console.WriteLine("  from the bin/Debug/net10.0 folder: ");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml -o render.png -w 1920 -H 1080 -s 128");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml --list-cameras");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml -c top -o top.png");
        Console.WriteLine("  dotnet RayTracer.dll -i scenes/chess.yaml -c 2 -o cam2.png");
    }

    static void SaveImage(Vector3[,] pixels, int width, int height, string path)
    {
        using var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = pixels[y, x];
                // BUG-04 fix: cast via int with clamp avoids the silent byte-overflow
                // edge case that 255.99f could theoretically cause with Inf/NaN inputs.
                byte r = (byte)Math.Clamp((int)(Math.Clamp(c.X, 0f, 1f) * 256f), 0, 255);
                byte g = (byte)Math.Clamp((int)(Math.Clamp(c.Y, 0f, 1f) * 256f), 0, 255);
                byte b = (byte)Math.Clamp((int)(Math.Clamp(c.Z, 0f, 1f) * 256f), 0, 255);
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

    /// <summary>
    /// Returns the value of a CLI argument (the token following the flag).
    /// </summary>
    static string? GetArg(string[] args, string longName, string? shortName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == longName || (shortName != null && args[i] == shortName))
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Returns true if a boolean flag is present (no value expected).
    /// </summary>
    static bool HasFlag(string[] args, string longName, string? shortName)
    {
        foreach (var arg in args)
        {
            if (arg == longName || (shortName != null && arg == shortName))
                return true;
        }
        return false;
    }
}
