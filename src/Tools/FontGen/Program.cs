//
// Generate font templates for 3d-ray from system fonts.
//
// Esempi di utilizzo:
//
// dotnet run --project src\Tools\FontGen\FontGen.csproj -c Release -- --font "Times New Roman"
// dotnet run --project src\Tools\FontGen\FontGen.csproj -c Release -- --font "Segoe UI" --height 0.2
// dotnet run --project src\Tools\FontGen\FontGen.csproj -c Release -- --font "Cascadia Code" --chars "ABC123"
// dotnet run --project src\Tools\FontGen\FontGen.csproj -c Release -- --font "Impact" --flatness 1.5
//
// Genereranno rispettivamente font-times_new_roman.yaml, font-segoe_ui.yaml, font-cascadia_code.yaml, font-impact.yaml in scenes\libraries\objects\
//
// Suggerimenti pratici:
// - Per estrusione 3D, i serif (Times/Cambria/Georgia) e i display pesanti (Impact, Bahnschrift Bold) rendono meglio: i sans-serif sottili (Calibri Light) generano profili meno cinematografici.
// - Se passi un nome non installato il tool stampa l'elenco delle prime 25 famiglie di sistema — utile per scoprire cosa è disponibile sulla macchina.
// - Puoi anche puntare a un file: --font "C:\Windows\Fonts\impact.ttf".
// - Per verificare l'elenco completo, o solo una tipologia, di font installati prima di lanciare:
//   dotnet run --project src\Tools\FontGen\FontGen.csproj -c Release -- --list-fonts
//   dotnet run --project src\Tools\FontGen\FontGen.csproj -c Release -- --list-fonts "Segoe"
//
// Su Windows 11 puoi passare al flag --font qualunque famiglia installata di sistema. Le più comuni preinstallate (nessuna licenza extra):
//
// Serif: "Times New Roman", "Cambria", "Georgia", "Constantia", "Palatino Linotype", "Book Antiqua"
// Sans-serif: "Arial", "Calibri", "Segoe UI", "Verdana", "Tahoma", "Trebuchet MS"
// Monospace: "Consolas", "Courier New", "Cascadia Code" / "Cascadia Mono" (terminale di Win 11), "Lucida Console"
// Display / decorativi: "Impact", "Comic Sans MS", "Bahnschrift", "Ink Free"
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using SixLabors.Fonts;

namespace FontGen;

internal static class Program
{
    private const string DefaultRelativeOutputDir = "scenes/libraries/objects";
    private const string DefaultChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        CliOptions opts;
        try { opts = CliOptions.Parse(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            PrintHelp(Console.Error);
            return 1;
        }

        if (opts.ListFonts) { ListAvailableFonts(Console.Out, true, opts.ListFontsFilter); return 0; }

        if (opts.ShowHelp || String.IsNullOrEmpty(opts.FontName)) { PrintHelp(Console.Out); return 0; }

        if (!TryLoadFontFamily(opts.FontName, out var family, out var loadError))
        {
            Console.Error.WriteLine($"error: {loadError}");
            ListAvailableFonts(Console.Error);
            return 1;
        }

        var font = family.CreateFont(opts.FontSize, FontStyle.Regular);
        string fontSlug = SlugifyFontName(family.Name);

        string outPath = ResolveOutputPath(opts.OutputPath, fontSlug);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var sw = new StringWriter();
        WriteHeader(sw, family.Name, fontSlug, opts.Height);

        int written = 0;
        var skipped = new List<char>();
        foreach (char c in opts.Chars)
        {
            var loops = ExtractCharLoops(font, c, opts.Flatness);
            if (loops.Outers.Count == 0) { skipped.Add(c); continue; }

            string tplName = TemplateNameFor(c, fontSlug);
            EmitTemplate(sw, tplName, loops, opts.Height);
            written++;
        }

        File.WriteAllText(outPath, sw.ToString());
        Console.WriteLine($"FontGen: {written} templates → {outPath}");
        if (skipped.Count > 0)
            Console.WriteLine($"  skipped (no glyph): {new string(skipped.ToArray())}");
        return 0;
    }

    private static void WriteHeader(StringWriter sw, string fontName, string fontSlug, float height)
    {
        sw.WriteLine("# ============================================================================");
        sw.WriteLine($"# Libreria Oggetti — Font 3D \"{fontName}\" (Alfabeto e Numeri)");
        sw.WriteLine("# ============================================================================");
        sw.WriteLine("#");
        sw.WriteLine("# Generato da src/Tools/FontGen.");
        sw.WriteLine("# Ogni carattere è un template centrato nell'origine in XZ, estruso lungo Y.");
        sw.WriteLine($"# Altezza default: {height.ToString("0.###", CultureInfo.InvariantCulture)}.");
        sw.WriteLine("# Materiale di default (font_material) da sovrascrivere nella scena.");
        sw.WriteLine($"# Naming: lettera_<char>_<maiusc|minusc>_{fontSlug}, numero_<digit>_{fontSlug}.");
        sw.WriteLine("#");
        sw.WriteLine("templates:");
    }

    private static bool TryLoadFontFamily(string nameOrPath, out FontFamily family, out string error)
    {
        family = default;
        error = "";
        if (File.Exists(nameOrPath))
        {
            try
            {
                var fc = new FontCollection();
                family = fc.Add(nameOrPath);
                return true;
            }
            catch (Exception ex)
            {
                error = $"failed to load font file '{nameOrPath}': {ex.Message}";
                return false;
            }
        }
        if (SystemFonts.Collection.TryGet(nameOrPath, out family))
            return true;
        error = $"font '{nameOrPath}' not found in system fonts and is not an existing file path.";
        return false;
    }

    private static void ListAvailableFonts(TextWriter w, bool showAll = false, string filter = "")
    {
        var fonts = SystemFonts.Collection.Families;
        if (!string.IsNullOrEmpty(filter))
            fonts = fonts.Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        else if (!showAll)
            fonts = fonts.Take(25);
        
        w.WriteLine("available system fonts include:");
        foreach (var f in fonts)
            w.WriteLine($"  - {f.Name}");
    }

    private static string ResolveOutputPath(string? explicitPath, string fontSlug)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);
        string repoRoot = ResolveRepoRoot();
        return Path.Combine(repoRoot, DefaultRelativeOutputDir, $"font-{fontSlug}.yaml");
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "3d-ray.slnx")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string SlugifyFontName(string name)
    {
        var sb = new StringBuilder();
        bool prevUnderscore = false;
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                prevUnderscore = false;
            }
            else if (sb.Length > 0 && !prevUnderscore)
            {
                sb.Append('_');
                prevUnderscore = true;
            }
        }
        var slug = sb.ToString().TrimEnd('_');
        return slug.Length == 0 ? "font" : slug;
    }

    private static string TemplateNameFor(char c, string slug)
    {
        string sfx = slug.Length > 0 ? "_" + slug : "";
        if (char.IsDigit(c))
            return $"numero_{c}{sfx}";
        if (char.IsUpper(c))
            return $"lettera_{char.ToLowerInvariant(c)}_maiusc{sfx}";
        if (char.IsLower(c))
            return $"lettera_{c}_minusc{sfx}";
        return $"char_{(int)c:x4}{sfx}";
    }

    private static GlyphLoops ExtractCharLoops(Font font, char c, float flatness)
    {
        var renderer = new GlyphOutlineExtractor(flatness);
        var options = new TextOptions(font);
        TextRenderer.RenderTextTo(renderer, c.ToString(), options);

        var raw = renderer.Figures.Where(f => f.Count > 2).ToList();
        if (raw.Count == 0) return GlyphLoops.Empty;

        float minX = raw.Min(f => f.Min(p => p.X));
        float maxX = raw.Max(f => f.Max(p => p.X));
        float minY = raw.Min(f => f.Min(p => p.Y));
        float maxY = raw.Max(f => f.Max(p => p.Y));
        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float h = MathF.Max(maxY - minY, 1e-6f);

        var loops = raw.Select(f =>
        {
            var pts = f.Select(p => new Vector2((p.X - cx) / h, -(p.Y - cy) / h)).ToList();
            if (pts.Count > 1 && Vector2.DistanceSquared(pts[0], pts[^1]) < 1e-8f)
                pts.RemoveAt(pts.Count - 1);
            return (Pts: pts, Area: SignedArea(pts));
        })
        .Where(l => l.Pts.Count > 2 && MathF.Abs(l.Area) > 1e-4f)
        .OrderByDescending(l => MathF.Abs(l.Area))
        .ToList();

        if (loops.Count == 0) return GlyphLoops.Empty;

        bool outerSign = loops[0].Area > 0;
        var outers = loops.Where(l => (l.Area > 0) == outerSign).Select(l => l.Pts).ToList();
        var inners = loops.Where(l => (l.Area > 0) != outerSign).Select(l => l.Pts).ToList();
        return new GlyphLoops(outers, inners);
    }

    private static float SignedArea(List<Vector2> pts)
    {
        float area = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            var p1 = pts[i];
            var p2 = pts[(i + 1) % pts.Count];
            area += p1.X * p2.Y - p2.X * p1.Y;
        }
        return area * 0.5f;
    }

    private static void EmitTemplate(StringWriter w, string name, GlyphLoops loops, float height)
    {
        w.WriteLine($"  - name: \"{name}\"");
        w.WriteLine("    children:");

        if (loops.Inners.Count == 0)
        {
            foreach (var outer in loops.Outers)
                WriteExtrusionListItem(w, outer, listIndent: 6, isHole: false, height);
        }
        else
        {
            const int listIndent = 6;
            const int csgBodyIndent = 8;
            string li = new string(' ', listIndent);
            string body = new string(' ', csgBodyIndent);
            w.WriteLine($"{li}- type: \"csg\"");
            w.WriteLine($"{body}operation: \"subtraction\"");
            w.WriteLine($"{body}left:");
            WriteUnionOrSingle(w, loops.Outers, csgBodyIndent + 2, isHole: false, height);
            w.WriteLine($"{body}right:");
            WriteUnionOrSingle(w, loops.Inners, csgBodyIndent + 2, isHole: true, height);
        }
        w.WriteLine();
    }

    private static void WriteUnionOrSingle(StringWriter w, List<List<Vector2>> loops, int indent, bool isHole, float height)
    {
        if (loops.Count == 1)
        {
            WriteExtrusionMapping(w, loops[0], indent, isHole, height);
            return;
        }
        string ind = new string(' ', indent);
        w.WriteLine($"{ind}type: \"csg\"");
        w.WriteLine($"{ind}operation: \"union\"");
        w.WriteLine($"{ind}left:");
        WriteExtrusionMapping(w, loops[0], indent + 2, isHole, height);
        w.WriteLine($"{ind}right:");
        var rest = loops.Skip(1).ToList();
        WriteUnionOrSingle(w, rest, indent + 2, isHole, height);
    }

    private static void WriteExtrusionListItem(StringWriter w, List<Vector2> profile, int listIndent, bool isHole, float height)
    {
        string li = new string(' ', listIndent);
        string body = new string(' ', listIndent + 2);
        w.WriteLine($"{li}- type: \"extrusion\"");
        WriteExtrusionBody(w, profile, body, isHole, height);
    }

    private static void WriteExtrusionMapping(StringWriter w, List<Vector2> profile, int indent, bool isHole, float height)
    {
        string ind = new string(' ', indent);
        w.WriteLine($"{ind}type: \"extrusion\"");
        WriteExtrusionBody(w, profile, ind, isHole, height);
    }

    private static void WriteExtrusionBody(StringWriter w, List<Vector2> profile, string ind, bool isHole, float height)
    {
        w.WriteLine($"{ind}profile_type: \"linear\"");
        if (isHole)
        {
            float h = height + 0.02f;
            w.WriteLine($"{ind}height: {h.ToString("0.###", CultureInfo.InvariantCulture)}");
            w.WriteLine($"{ind}translate: [0, -0.01, 0]");
        }
        else
        {
            w.WriteLine($"{ind}height: {height.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
        w.WriteLine($"{ind}caps: \"both\"");
        w.WriteLine($"{ind}material: \"font_material\"");
        w.WriteLine($"{ind}profile:");
        foreach (var p in profile)
        {
            w.WriteLine($"{ind}  - [{p.X.ToString("0.000", CultureInfo.InvariantCulture),7}, {p.Y.ToString("0.000", CultureInfo.InvariantCulture),7}]");
        }
    }

    private static void PrintHelp(TextWriter w)
    {
        w.WriteLine("FontGen — Generate a 3D-Ray font library (extruded glyph templates) from a system font.");
        w.WriteLine();
        w.WriteLine("Usage:");
        w.WriteLine("  dotnet run --project src/Tools/FontGen/FontGen.csproj -- [options]");
        w.WriteLine();
        w.WriteLine("Options:");
        w.WriteLine("  -f, --font <name|path>    Font family name or .ttf/.otf path.");
        w.WriteLine("  -c, --chars <string>      Characters to emit (default: A-Z a-z 0-9).");
        w.WriteLine("  -o, --out <path>          Output yaml path (default: scenes/libraries/objects/font-<slug>.yaml).");
        w.WriteLine("      --height <float>      Extrusion height along Y (default: 0.15).");
        w.WriteLine("      --font-size <float>   Internal rasterisation size in pt (default: 100).");
        w.WriteLine("      --flatness <float>    Bezier flattening tolerance in font units (default: 1.0).");
        w.WriteLine("  -l, --list-fonts <string> List available system fonts (default: empty string show all fonts).");
        w.WriteLine("  -h, --help                Show this help.");
        w.WriteLine();
        w.WriteLine("Notes:");
        w.WriteLine("  - Template names are suffixed with the font slug, e.g. lettera_a_maiusc_dejavu_serif.");
        w.WriteLine("  - The output filename uses the font slug, e.g. font-dejavu_serif.yaml — this prevents");
        w.WriteLine("    collisions when generating multiple fonts into the same library directory.");
    }
}

internal sealed record GlyphLoops(List<List<Vector2>> Outers, List<List<Vector2>> Inners)
{
    public static GlyphLoops Empty { get; } = new(new(), new());
}

internal sealed class CliOptions
{
    public string FontName = "";
    public string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    public string? OutputPath;
    public float Height = 0.15f;
    public float FontSize = 100f;
    public float Flatness = 1.0f;
    public bool ListFonts;
    public string ListFontsFilter = "";
    public bool ShowHelp;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-h":
                case "--help":
                    o.ShowHelp = true;
                    break;
                case "-l":
                case "--list-fonts":
                    o.ListFonts = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        i++;
                        o.ListFontsFilter = args[i];
                    }
                    break;
                case "-f":
                case "--font":
                    o.FontName = RequireValue(args, ref i, a);
                    break;
                case "-c":
                case "--chars":
                    o.Chars = RequireValue(args, ref i, a);
                    break;
                case "-o":
                case "--out":
                    o.OutputPath = RequireValue(args, ref i, a);
                    break;
                case "--height":
                    o.Height = float.Parse(RequireValue(args, ref i, a), CultureInfo.InvariantCulture);
                    break;
                case "--font-size":
                    o.FontSize = float.Parse(RequireValue(args, ref i, a), CultureInfo.InvariantCulture);
                    break;
                case "--flatness":
                    o.Flatness = float.Parse(RequireValue(args, ref i, a), CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"unknown option '{a}'");
            }
        }
        if (string.IsNullOrEmpty(o.Chars))
            throw new ArgumentException("--chars must be non-empty");
        if (o.Height <= 0f)
            throw new ArgumentException("--height must be > 0");
        if (o.FontSize <= 0f)
            throw new ArgumentException("--font-size must be > 0");
        if (o.Flatness <= 0f)
            throw new ArgumentException("--flatness must be > 0");
        return o;
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"missing value for {flag}");
        return args[++i];
    }
}

/// <summary>
/// Captures glyph outlines from <see cref="IGlyphRenderer"/> callbacks. Bezier
/// segments are flattened to polylines using adaptive subdivision so the
/// downstream extrusion profile is purely linear.
/// </summary>
internal sealed class GlyphOutlineExtractor : IGlyphRenderer
{
    public List<List<Vector2>> Figures { get; } = new();
    private List<Vector2>? _current;
    private Vector2 _cursor;
    private readonly float _flatness;

    public GlyphOutlineExtractor(float flatness) { _flatness = flatness; }

    public TextDecorations EnabledDecorations() => TextDecorations.None;
    public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness) { }

    public void BeginText(in FontRectangle bounds) { }
    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters) => true;
    public void EndGlyph() { }
    public void EndText() { }

    public void BeginFigure() => _current = new List<Vector2>();

    public void EndFigure()
    {
        if (_current != null && _current.Count >= 3)
            Figures.Add(_current);
        _current = null;
    }

    public void MoveTo(Vector2 point)
    {
        AddPoint(point);
        _cursor = point;
    }

    public void LineTo(Vector2 point)
    {
        AddPoint(point);
        _cursor = point;
    }

    public void QuadraticBezierTo(Vector2 c, Vector2 end)
    {
        FlattenQuad(_cursor, c, end, 0);
        AddPoint(end);
        _cursor = end;
    }

    public void CubicBezierTo(Vector2 c1, Vector2 c2, Vector2 end)
    {
        FlattenCubic(_cursor, c1, c2, end, 0);
        AddPoint(end);
        _cursor = end;
    }

    private void AddPoint(Vector2 p)
    {
        if (_current == null) return;
        if (_current.Count > 0 && Vector2.DistanceSquared(_current[^1], p) < 1e-6f) return;
        _current.Add(p);
    }

    private void FlattenQuad(Vector2 a, Vector2 c, Vector2 b, int depth)
    {
        if (depth > 18) return;
        var mid = 0.5f * (a + b);
        if (Vector2.DistanceSquared(c, mid) <= _flatness * _flatness) return;
        var ac = 0.5f * (a + c);
        var cb = 0.5f * (c + b);
        var m = 0.5f * (ac + cb);
        FlattenQuad(a, ac, m, depth + 1);
        AddPoint(m);
        FlattenQuad(m, cb, b, depth + 1);
    }

    private void FlattenCubic(Vector2 a, Vector2 c1, Vector2 c2, Vector2 b, int depth)
    {
        if (depth > 18) return;
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float chord2 = dx * dx + dy * dy;
        if (chord2 > 0f)
        {
            float cross1 = MathF.Abs(dy * c1.X - dx * c1.Y + b.X * a.Y - b.Y * a.X);
            float cross2 = MathF.Abs(dy * c2.X - dx * c2.Y + b.X * a.Y - b.Y * a.X);
            float maxCross = MathF.Max(cross1, cross2);
            if (maxCross * maxCross <= _flatness * _flatness * chord2) return;
        }
        var ab = 0.5f * (a + c1);
        var bc = 0.5f * (c1 + c2);
        var cd = 0.5f * (c2 + b);
        var abc = 0.5f * (ab + bc);
        var bcd = 0.5f * (bc + cd);
        var mid = 0.5f * (abc + bcd);
        FlattenCubic(a, ab, abc, mid, depth + 1);
        AddPoint(mid);
        FlattenCubic(mid, bcd, cd, b, depth + 1);
    }
}
