using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TerrainGen;

internal static class Cli
{
    public sealed class ParseResult
    {
        public GenerationConfig? Config;
        public bool ShowHelp;
        public string? Error;
    }

    public static ParseResult Parse(string[] args, string defaultOutputDir)
    {
        string? name = null;
        string outputDir = defaultOutputDir;
        TerrainType type = TerrainType.Collina;
        WaterFeatures include = WaterFeatures.None;
        Season season = Season.Estate;
        float size = 100f;
        int resolution = 256;
        int? seed = null;
        Style style = Style.Realistic;
        bool erosionEnabled = true;
        int erosionIters = 50_000;
        float? seaLevel = null;
        bool withCameras = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h":
                case "--help":
                    return new ParseResult { ShowHelp = true };

                case "--name":
                    name = Next(args, ref i, a);
                    break;
                case "--output":
                case "-o":
                    outputDir = Next(args, ref i, a);
                    break;
                case "--type":
                    if (!TryParseEnum<TerrainType>(Next(args, ref i, a), out type))
                        return Err($"Invalid --type. Use one of: pianura, collina, montagna.");
                    break;
                case "--include":
                    if (!TryParseInclude(Next(args, ref i, a), out include))
                        return Err($"Invalid --include. Use a CSV subset of: fiumi, laghi, mare, isole.");
                    break;
                case "--season":
                    if (!TryParseEnum<Season>(Next(args, ref i, a), out season))
                        return Err($"Invalid --season. Use one of: primavera, estate, autunno, inverno.");
                    break;
                case "--size":
                    if (!TryParseFloat(Next(args, ref i, a), out size) || size <= 0)
                        return Err("Invalid --size. Provide a positive number (world units).");
                    break;
                case "--resolution":
                    if (!int.TryParse(Next(args, ref i, a), NumberStyles.Integer, CultureInfo.InvariantCulture, out resolution)
                        || resolution < 8 || resolution > 1024)
                        return Err("Invalid --resolution. Provide an integer in [8, 1024].");
                    break;
                case "--seed":
                    if (!int.TryParse(Next(args, ref i, a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                        return Err("Invalid --seed. Provide an integer.");
                    seed = s;
                    break;
                case "--style":
                    if (!TryParseEnum<Style>(Next(args, ref i, a), out style))
                        return Err("Invalid --style. Use one of: realistic, minecraft, lowpoly.");
                    break;
                case "--no-erosion":
                    erosionEnabled = false;
                    break;
                case "--erosion-iters":
                    if (!int.TryParse(Next(args, ref i, a), NumberStyles.Integer, CultureInfo.InvariantCulture, out erosionIters)
                        || erosionIters < 0)
                        return Err("Invalid --erosion-iters. Provide a non-negative integer.");
                    break;
                case "--sea-level":
                    if (!TryParseFloat(Next(args, ref i, a), out float sl) || sl < 0 || sl > 1)
                        return Err("Invalid --sea-level. Provide a number in [0, 1].");
                    seaLevel = sl;
                    break;
                case "--with-cameras":
                    withCameras = true;
                    break;
                default:
                    return Err($"Unknown argument: {a}. Use --help for usage.");
            }
        }

        if (string.IsNullOrWhiteSpace(name))
            return Err("Missing required --name <stem> argument. Use --help for usage.");

        if (!IsSafeStem(name!))
            return Err("Invalid --name. Use letters, digits, '-', '_' only.");

        float effectiveSeaLevel = seaLevel ?? ((include & WaterFeatures.Mare) != 0 ? 0.30f : -1f);
        int effectiveSeed = seed ?? Environment.TickCount;

        return new ParseResult
        {
            Config = new GenerationConfig
            {
                Name = name!,
                OutputDir = outputDir,
                Type = type,
                Include = include,
                Season = season,
                Size = size,
                Resolution = resolution,
                Seed = effectiveSeed,
                Style = style,
                ErosionEnabled = erosionEnabled,
                ErosionIters = erosionIters,
                SeaLevel = effectiveSeaLevel,
                WithCameras = withCameras,
            }
        };
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value after {flag}");
        return args[++i];
    }

    private static bool TryParseFloat(string s, out float value)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseEnum<T>(string s, out T value) where T : struct, Enum
        => Enum.TryParse(s, ignoreCase: true, out value) && Enum.IsDefined(value);

    private static bool TryParseInclude(string s, out WaterFeatures features)
    {
        features = WaterFeatures.None;
        foreach (var raw in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "fiumi": features |= WaterFeatures.Fiumi; break;
                case "laghi": features |= WaterFeatures.Laghi; break;
                case "mare":  features |= WaterFeatures.Mare;  break;
                case "isole": features |= WaterFeatures.Isole; break;
                default: return false;
            }
        }
        return true;
    }

    private static bool IsSafeStem(string s)
    {
        foreach (char c in s)
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return false;
        return s.Length > 0 && s.Length <= 80;
    }

    private static ParseResult Err(string msg) => new ParseResult { Error = msg };

    public static void PrintHelp(TextWriter w)
    {
        w.WriteLine("TerrainGen — procedural terrain generator for 3D-Ray");
        w.WriteLine();
        w.WriteLine("USAGE:");
        w.WriteLine("  dotnet run --project src/Tools/TerrainGen -- --name <stem> [options]");
        w.WriteLine();
        w.WriteLine("REQUIRED:");
        w.WriteLine("  --name <stem>            Output filename stem (a-z, 0-9, -, _).");
        w.WriteLine();
        w.WriteLine("TERRAIN:");
        w.WriteLine("  --type <t>               pianura | collina | montagna           (default: collina)");
        w.WriteLine("  --include <csv>          fiumi,laghi,mare,isole                 (default: none)");
        w.WriteLine("  --season <s>             primavera | estate | autunno | inverno (default: estate)");
        w.WriteLine("  --style <s>              realistic | minecraft | lowpoly        (default: realistic)");
        w.WriteLine();
        w.WriteLine("GEOMETRY:");
        w.WriteLine("  --size <units>           Side length in world units             (default: 100)");
        w.WriteLine("  --resolution <N>         Heightmap cells per side, 8..1024      (default: 256)");
        w.WriteLine("  --seed <int>             Deterministic seed                     (default: random)");
        w.WriteLine();
        w.WriteLine("EROSION & WATER:");
        w.WriteLine("  --no-erosion             Disable hydraulic + thermal erosion    (default: on)");
        w.WriteLine("  --erosion-iters <N>      Hydraulic drop count                   (default: 50000)");
        w.WriteLine("  --sea-level <0..1>       Normalised sea level                   (default: 0.30 if --include mare)");
        w.WriteLine();
        w.WriteLine("OUTPUT:");
        w.WriteLine("  --output <dir>           Output directory                       (default: scenes/libraries/terrain)");
        w.WriteLine("  --with-cameras           Also emit a preview scene with cameras (default: off)");
        w.WriteLine("  -h, --help               Show this help and exit");
        w.WriteLine();
        w.WriteLine("EXAMPLES:");
        w.WriteLine("  dotnet run --project src/Tools/TerrainGen -- \\");
        w.WriteLine("    --name autumn-alps --type montagna --include fiumi,laghi,mare \\");
        w.WriteLine("    --season autunno --size 200 --resolution 256 --seed 1729 --with-cameras");
    }
}
