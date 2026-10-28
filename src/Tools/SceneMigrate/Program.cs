using RayTracer.Scene;
using SceneMigrate;

// SceneMigrate: converts 3D-Ray scenes of format 1 into format 2 in place
// (PIANO-REVIEW.md Phase 10). See --help.

// --stdin-fragment: one piece of a scene (a documentation example) from
// standard input to standard output, without version header or defaults.
if (args.Contains("--stdin-fragment"))
{
    var fm = new Migrator("fragment", Console.In.ReadToEnd());
    Console.Out.Write(fm.Migrate(schemaComment: null, fragment: true));
    foreach (var n in fm.Notes) Console.Error.WriteLine(n);
    return 0;
}

var paths = args.Where(a => !a.StartsWith("--")).ToList();
bool dryRun = args.Contains("--dry-run");
bool quiet = args.Contains("--quiet");
if (paths.Count == 0 || args.Contains("--help"))
{
    Console.WriteLine("""
        SceneMigrate: converts 3D-Ray scenes from format 1 to format 2, in place.

        Usage: dotnet run --project src/Tools/SceneMigrate -- <scene.yaml | directory>... [--dry-run] [--quiet]

        A directory is searched for *.yaml recursively. Files that already have a
        format_version are left alone. Comments and layout are kept; keys format 1
        ignored are commented out. The result is checked with the engine's scene
        validator, and every value that could not be carried over exactly is
        reported as a note.

          --dry-run   print what would change, write nothing
          --quiet     only the files with notes or errors
        """);
    return paths.Count == 0 && !args.Contains("--help") ? 1 : 0;
}

var files = new List<string>();
foreach (var p in paths)
{
    if (Directory.Exists(p))
        files.AddRange(Directory.EnumerateFiles(p, "*.yaml", SearchOption.AllDirectories)
                                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                                .Order(StringComparer.Ordinal));
    else if (File.Exists(p)) files.Add(p);
    else { Console.Error.WriteLine($"not found: {p}"); return 1; }
}

int migrated = 0, skipped = 0, failed = 0;
foreach (var file in files)
{
    string text = File.ReadAllText(file);
    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?m)^format_version\s*:"))
    {
        skipped++;
        if (!quiet) Console.WriteLine($"  {file}: already format 2");
        continue;
    }
    string result;
    Migrator migrator;
    try
    {
        migrator = new Migrator(file, text);
        result = migrator.Migrate(SchemaComment(file));
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  {file}: NOT MIGRATED ({ex.Message})");
        continue;
    }

    var issues = SceneValidator.Validate(result);
    var errors = issues.Where(i => i.IsError).ToList();
    if (!quiet || errors.Count > 0 || migrator.Notes.Count > 0)
        Console.WriteLine($"  {file}: {(errors.Count > 0 ? $"{errors.Count} error(s) left" : "ok")}" +
                          (migrator.Notes.Count > 0 ? $", {migrator.Notes.Count} note(s)" : ""));
    foreach (var n in migrator.Notes) Console.WriteLine($"      note: {n}");
    foreach (var i in issues) Console.WriteLine($"      {(i.IsError ? "error" : "warning")}: {i.Format(file)}");
    if (errors.Count > 0) failed++;
    if (!dryRun) File.WriteAllText(file, result);
    migrated++;
}
Console.WriteLine($"{migrated} migrated, {skipped} already format 2, {failed} with errors");
return failed > 0 ? 1 : 0;

// An editor comment pointing at the JSON Schema, relative to the repository root.
static string? SchemaComment(string file)
{
    var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!);
    for (var d = dir; d != null; d = d.Parent)
    {
        string schema = Path.Combine(d.FullName, "docs", "reference", "scene-schema.json");
        if (File.Exists(Path.Combine(d.FullName, "3d-ray.slnx")))
            return "# yaml-language-server: $schema=" +
                   Path.GetRelativePath(dir.FullName, schema).Replace(Path.DirectorySeparatorChar, '/');
    }
    return null;
}
