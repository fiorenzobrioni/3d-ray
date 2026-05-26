using System.Text;
using System.Text.RegularExpressions;

namespace MigrateFakeSss;

/// <summary>
/// Strips the legacy "fake SSS" Disney YAML knobs from every <c>*.yaml</c>
/// under a directory:
///
///   - <c>subsurface:</c>
///   - <c>subsurface_color:</c>
///   - <c>subsurface_radius:</c>
///   - <c>flatness:</c>
///   - <c>subsurface_texture:</c>      (full block, indented children too)
///   - <c>subsurface_color_texture:</c>
///   - <c>flatness_texture:</c>
///
/// These parameters drove the Hanrahan-Krueger flat-blend approximation on the
/// Disney diffuse lobe — a per-shading-point hack that has been removed in
/// favour of physically-based Random Walk subsurface scattering bound at the
/// entity level via <c>interior_medium</c> (see
/// <c>docs/plans/mediuminterface-and-random-walk-sss.md</c>).
///
/// Behaviour:
///   - Scalar fields (<c>subsurface: 0.5</c>) → single line removed.
///   - List fields (<c>subsurface_color: [0.9, 0.7, 0.6]</c>) → single line.
///   - Block fields (<c>subsurface_texture:</c> followed by indented YAML
///     children) → the header line plus every following line at a deeper
///     indent are removed.
///
/// A short report is printed listing every file touched and the legacy fields
/// stripped from it. The original file is overwritten in place — pass
/// <c>--dry-run</c> to preview without writing.
/// </summary>
public static class Program
{
    private static readonly string[] ScalarKeys =
    {
        "subsurface",
        "subsurface_color",
        "subsurface_radius",
        "flatness",
    };

    private static readonly string[] BlockKeys =
    {
        "subsurface_texture",
        "subsurface_color_texture",
        "flatness_texture",
    };

    public static int Main(string[] args)
    {
        bool dryRun = args.Contains("--dry-run");
        string? root = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (root == null)
        {
            Console.Error.WriteLine(
                "usage: dotnet run --project src/Tools/MigrateFakeSss -- <scenes-dir> [--dry-run]");
            return 1;
        }
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"error: directory not found: {root}");
            return 1;
        }

        int filesScanned = 0;
        int filesChanged = 0;
        int totalStripped = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
        {
            filesScanned++;
            var original = File.ReadAllLines(path);
            var (cleaned, removedLines, hits) = Strip(original);
            if (hits.Count == 0) continue;

            filesChanged++;
            totalStripped += removedLines;

            string rel = Path.GetRelativePath(root, path);
            Console.WriteLine($"  {rel}");
            foreach (var hit in hits)
                Console.WriteLine($"      − {hit}");

            if (!dryRun)
                File.WriteAllLines(path, cleaned, new UTF8Encoding(false));
        }

        Console.WriteLine();
        Console.WriteLine($"Scanned {filesScanned} YAML files; modified {filesChanged}; stripped {totalStripped} lines.");
        if (dryRun)
            Console.WriteLine("(dry-run: no files were written)");
        return 0;
    }

    private static (List<string> cleaned, int removed, List<string> hits) Strip(string[] lines)
    {
        var cleaned = new List<string>(lines.Length);
        var hits = new List<string>();
        int removed = 0;

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            // Block field: <indent>key:<EOL or whitespace> followed by deeper-indented children.
            string? blockKey = MatchedKey(trimmed, BlockKeys, allowBlock: true);
            if (blockKey != null)
            {
                int headerIndent = line.Length - trimmed.Length;
                hits.Add(blockKey);
                removed++;
                i++;
                while (i < lines.Length)
                {
                    string next = lines[i];
                    if (next.TrimEnd().Length == 0)
                    {
                        // Blank line inside a block — drop too, keep parsing.
                        i++; removed++;
                        continue;
                    }
                    int nextIndent = next.Length - next.TrimStart().Length;
                    if (nextIndent <= headerIndent) break;
                    i++; removed++;
                }
                continue;
            }

            // Scalar field.
            string? scalarKey = MatchedKey(trimmed, ScalarKeys, allowBlock: false);
            if (scalarKey != null)
            {
                hits.Add($"{scalarKey}:");
                removed++;
                i++;
                continue;
            }

            cleaned.Add(line);
            i++;
        }

        return (cleaned, removed, hits);
    }

    /// <summary>
    /// Returns the matched key when <paramref name="trimmed"/> is a YAML
    /// mapping key (<c>key:</c>) for one of <paramref name="keys"/>. When
    /// <paramref name="allowBlock"/> is false, the value must be inline on
    /// the same line (anything after the colon).
    /// </summary>
    private static string? MatchedKey(string trimmed, string[] keys, bool allowBlock)
    {
        foreach (var key in keys)
        {
            var pattern = allowBlock
                ? $"^{Regex.Escape(key)}\\s*:\\s*$"
                : $"^{Regex.Escape(key)}\\s*:\\s*\\S";
            if (Regex.IsMatch(trimmed, pattern))
                return key;
        }
        return null;
    }
}
