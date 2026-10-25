using System.Globalization;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Lights;
using YamlDotNet.RepresentationModel;
using static SceneMigrate.YamlEdits;

namespace SceneMigrate;

/// <summary>
/// Converts a scene of format 1 into format 2 (PIANO-REVIEW.md Phase 10) so
/// that it renders as it did: renamed keys, the Disney material written as
/// OpenPBR, the lights' unitless intensity turned into their photometric
/// quantity, the relative physical skies into an exposure, ... Keys format 1
/// ignored are commented out (they had no effect). Comments and layout are
/// kept. What cannot be carried over exactly is reported as a note.
/// </summary>
public sealed partial class Migrator
{
    private readonly YamlEdits _ed;
    private readonly YamlMappingNode _root;
    private readonly List<string> _notes = new();
    private readonly string _file;
    private float _mpu = 1f;
    private YamlMappingNode? _materials;
    private readonly List<string> _extraMaterials = new();

    public IReadOnlyList<string> Notes => _notes;

    public Migrator(string file, string text)
    {
        _file = file;
        _ed = new YamlEdits(text);
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        _root = stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode m
            ? m : throw new InvalidOperationException("not a scene: the root is not a mapping");
    }

    private int End(YamlNode n) => _ed.End(n);

    private void Note(YamlNode at, string message) => _notes.Add($"{Path.GetFileName(_file)}:{at.Start.Line}: {message}");

    // ── Node helpers ─────────────────────────────────────────────────────────

    private static YamlNode? Key(YamlMappingNode m, string key) =>
        m.Children.Keys.FirstOrDefault(k => k is YamlScalarNode { Value: var v } && v == key);

    private static YamlNode? Val(YamlMappingNode m, string key) => Key(m, key) is { } k ? m.Children[k] : null;

    private static string? Str(YamlMappingNode m, string key) => (Val(m, key) as YamlScalarNode)?.Value;

    private static double? NumOf(YamlNode? n) =>
        n is YamlScalarNode { Value: var s } && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static double? NumOf(YamlMappingNode m, string key) => NumOf(Val(m, key));

    private static double[]? Nums(YamlNode? n)
    {
        if (n is YamlScalarNode) return NumOf(n) is double d ? [d] : null;
        if (n is not YamlSequenceNode q) return null;
        var list = new List<double>();
        foreach (var c in q.Children) { if (NumOf(c) is double d) list.Add(d); else return null; }
        return list.ToArray();
    }

    private static double[]? Nums(YamlMappingNode m, string key) => Nums(Val(m, key));

    private static bool? Bool(YamlMappingNode m, string key) => Str(m, key) switch
    {
        "true" => true, "false" => false, _ => null,
    };

    private static Vector3? Vec3(double[]? v) => v is { Length: >= 3 } ? new Vector3((float)v[0], (float)v[1], (float)v[2])
                                               : v is { Length: 1 } ? new Vector3((float)v[0]) : null;

    private static IEnumerable<(string Key, YamlNode KeyNode, YamlNode Value)> Pairs(YamlMappingNode m) =>
        m.Children.Where(kv => kv.Key is YamlScalarNode).Select(kv => (((YamlScalarNode)kv.Key).Value!, kv.Key, kv.Value)).ToList();

    private void Set(YamlMappingNode m, string key, string value)
    {
        if (Val(m, key) is { } v) _ed.Replace(v, value);
    }

    private void Remove(YamlMappingNode m, string key)
    {
        if (Key(m, key) is { } k && !_ed.IsRemoved(k)) _ed.RemovePair(m, k);
    }

    private void Add(YamlMappingNode m, string key, string value) => _ed.AddPair(m, key, value);

    /// <summary>Renames <paramref name="key"/> (and sets its value), or adds it when absent.</summary>
    private void Put(YamlMappingNode m, string key, string newKey, string value)
    {
        if (Key(m, key) is { } k && !_ed.IsRemoved(k))
        {
            if (newKey != key) RenameTracked(m, k, newKey);
            if (!SameNumbers(value, m.Children[k])) _ed.Replace(m.Children[k], value);
        }
        else Add(m, newKey, value);
    }

    /// <summary>Keys format 1 did not know were ignored: commented out, as they had no effect.</summary>
    private void DropUnknown(YamlMappingNode m, string v1Class, params string[] alsoKnown)
    {
        var known = V1Keys[v1Class];
        foreach (var (key, kn, _) in Pairs(m))
        {
            if (_ed.IsRemoved(kn) || known.Contains(key) || alsoKnown.Contains(key)) continue;
            string text = _ed.CommentOutPair(m, kn);
            Note(kn, $"'{key}' was not a format-1 key (ignored): commented out ({Short(text)})");
        }
    }

    private static string Short(string s)
    {
        s = s.Replace('\n', ' ');
        return s.Length > 60 ? s[..57] + "..." : s;
    }

    private static float Lum(Vector3 c) => MathUtils.Luminance(c);

    /// <summary>Format-1 colour of a light or emissive (colour times the black body).</summary>
    private static Vector3 Tinted(Vector3 color, double? kelvin) =>
        kelvin is double k ? color * LightUnits.Blackbody((float)k) : color;

    // ── Driver ───────────────────────────────────────────────────────────────

    public string Migrate(string? schemaComment)
    {
        if (Key(_root, "format_version") != null)
            throw new InvalidOperationException("already has a format_version");

        _mpu = SceneMetresPerUnit();
        _materials = Val(_root, "materials") as YamlMappingNode;

        foreach (var (key, kn, value) in Pairs(_root))
        {
            switch (key)
            {
                case "camera": MigrateSingleCamera(kn, value); break;
                case "cameras":
                    if (value is YamlSequenceNode cams)
                        foreach (var c in cams.Children.OfType<YamlMappingNode>()) MigrateCamera(c);
                    break;
                case "imports": MigrateImports(value); break;
                case "world": if (value is YamlMappingNode w) MigrateWorld(w); break;
                case "materials":
                    if (value is YamlSequenceNode mats)
                        foreach (var m in mats.Children.OfType<YamlMappingNode>()) MigrateMaterial(m);
                    break;
                case "mediums":
                    if (value is YamlSequenceNode meds)
                        foreach (var m in meds.Children.OfType<YamlMappingNode>()) MigrateMedium(m);
                    break;
                case "lights":
                    if (value is YamlSequenceNode lights)
                        foreach (var l in lights.Children.OfType<YamlMappingNode>()) MigrateLight(l);
                    break;
                case "entities":
                case "templates":
                    if (value is YamlSequenceNode ents)
                        foreach (var e in ents.Children.OfType<YamlMappingNode>()) MigrateEntity(e, template: key == "templates");
                    break;
            }
        }
        DropUnknown(_root, "SceneData", "validation");

        // A file with neither camera nor world is a library imported by scenes:
        // the defaults below belong to the scene that imports it.
        bool isScene = Key(_root, "camera") != null || Key(_root, "cameras") != null || Key(_root, "world") != null;
        if (isScene && Key(_root, "lights") == null && NeedsDefaultLights())
        {
            AppendRoot("# Format 1 added these two lights to a scene without 'lights:' and no other light.\n" +
                       "lights:\n" +
                       "  - type: directional\n    direction: [-1, -1, -1]\n    illuminance: 0.8\n" +
                       "  - type: point\n    position: [0, 10, -5]\n    intensity: " + Num(100.0 * _mpu * _mpu) + "\n");
            Note(_root, "no 'lights:': the two lights format 1 added by default are now written out");
        }
        // Format 1 rendered a scene without a sky against a flat (0.5, 0.7, 1).
        if (!isScene) { }
        else if (Val(_root, "world") is not YamlMappingNode world)
            AppendRoot("world:\n  sky: { type: flat, color: [0.5, 0.7, 1.0] }   # the format-1 default sky\n");
        else if (Key(world, "sky") == null)
            Add(world, "sky", "{ type: flat, color: [0.5, 0.7, 1.0] }   # the format-1 default sky");
        if (_extraMaterials.Count > 0)
        {
            if (Val(_root, "materials") is YamlSequenceNode ms && ms.Style != YamlDotNet.Core.Events.SequenceStyle.Flow && ms.Children.Count > 0)
            {
                int at = _ed.LastLineEnd(ms);
                var first = ms.Children[0];
                int col = Start(first) - _ed.LineStart(Start(first)) - 2;
                string ind = new(' ', Math.Max(col, 0));
                _ed.Insert(at, string.Concat(_extraMaterials.Select(m => "\n" + ind + "- " + m)));
            }
            else
                AppendRoot("materials:\n" + string.Concat(_extraMaterials.Select(m => "  - " + m + "\n")));
        }

        // format_version (and units) as the first keys of the file.
        var firstKey = _root.Children.Keys.First();
        string header = "format_version: 2\n";
        if (MathF.Abs(_mpu - 1f) > 1e-6f) header += $"units: {{ length: {Num(_mpu)} }}\n";
        int pos = _ed.LineStart(Start(firstKey));
        _ed.Insert(pos, header);
        string result = _ed.Apply();
        if (schemaComment != null && !result.StartsWith("# yaml-language-server"))
            result = schemaComment + "\n" + result;
        return result;
    }

    private void AppendRoot(string block)
    {
        string t = _ed.Text;
        _ed.Insert(t.Length, (t.EndsWith('\n') ? "\n" : "\n\n") + block);
    }

    /// <summary>
    /// Metres per scene unit: the atmosphere's world_scale (default 1000), else
    /// the camera's meters_per_unit (default 1).
    /// </summary>
    private float SceneMetresPerUnit()
    {
        IEnumerable<YamlMappingNode> media = new List<YamlMappingNode>();
        if (Val(_root, "world") is YamlMappingNode w && Val(w, "medium") is YamlMappingNode wm) media = media.Append(wm);
        if (Val(_root, "mediums") is YamlSequenceNode ms) media = media.Concat(ms.Children.OfType<YamlMappingNode>());
        foreach (var m in media)
            if (Str(m, "type") is "atmosphere" or "nishita" or "aerial_perspective")
                return (float)(NumOf(m, "world_scale") ?? 1000.0);
        var cams = new List<YamlMappingNode>();
        if (Val(_root, "camera") is YamlMappingNode c) cams.Add(c);
        if (Val(_root, "cameras") is YamlSequenceNode cs) cams.AddRange(cs.Children.OfType<YamlMappingNode>());
        foreach (var cam in cams)
            if (NumOf(cam, "meters_per_unit") is double mpu) return (float)mpu;
        return 1f;
    }

    private bool NeedsDefaultLights()
    {
        // Format 1 added them when no light at all came from the scene:
        // no emissive material and a sky that does not light (gradient
        // without a sun, black flat).
        if (Val(_root, "materials") is YamlSequenceNode ms)
            foreach (var m in ms.Children.OfType<YamlMappingNode>())
                if (Str(m, "type") == "emissive" || (NumOf(m, "emission_luminance") ?? 0) > 0) return false;
        var sky = Val(Val(_root, "world") as YamlMappingNode ?? new(), "sky") as YamlMappingNode;
        if (sky == null) return false;
        return Str(sky, "type") switch
        {
            "gradient" => Val(Val(sky, "sun") as YamlMappingNode ?? new(), "direction") == null,
            "flat" or null => Vec3(Nums(sky, "color")) is Vector3 c ? Lum(c) <= 1e-6f : false,
            _ => false,
        };
    }

    private void MigrateImports(YamlNode value)
    {
        if (value is not YamlSequenceNode q) return;
        foreach (var item in q.Children)
            if (item is YamlMappingNode m && Val(m, "path") is YamlScalarNode p)
                _ed.Replace(m, _ed.Flow(p));
    }
}
