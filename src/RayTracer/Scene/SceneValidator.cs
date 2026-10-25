using System.Globalization;
using System.Reflection;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace RayTracer.Scene;

/// <summary>A scene file that does not follow the format: every problem found, one per line.</summary>
public sealed class SceneFormatException(string file, IReadOnlyList<string> errors)
    : Exception($"{file}: {errors.Count} error(s) in the scene file:\n  " + string.Join("\n  ", errors))
{
    public string File { get; } = file;
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Checks a YAML scene against the format-2 classes of <see cref="SceneData"/>
/// before it is deserialised (PIANO-REVIEW.md Phase 10, §3.23): an unknown key,
/// a value of the wrong kind, an unknown <c>type</c> or enum value is an error
/// (all of them are collected, with line and column, and a suggestion); a key
/// that has no effect on the block's <c>type</c> is a warning.
/// </summary>
public static class SceneValidator
{
    /// <summary>The format version this engine reads.</summary>
    public const int FormatVersion = 2;

    public readonly record struct Issue(bool IsError, int Line, int Column, string Message)
    {
        public string Format(string file) => $"{Path.GetFileName(file)}:{Line}:{Column}: {Message}";
    }

    /// <summary>The format-1 keys and values that changed, with their format-2 form.</summary>
    private static readonly Dictionary<string, string> FormerKeys = new()
    {
        ["camera"] = "a format-1 key: cameras are a list, 'cameras:'",
        ["vup"] = "a format-1 key, now 'up'",
        ["focal_dist"] = "a format-1 key, now 'focus_distance'",
        ["focal_pos"] = "a format-1 key, now 'focus_point'",
        ["shutter"] = "a format-1 key, now 'shutter_interval'",
        ["meters_per_unit"] = "a format-1 key: the scene's 'units: { length }'",
        ["world_scale"] = "a format-1 key: the scene's 'units: { length }'",
        ["visible_to_camera"] = "a format-1 key, now 'visibility: { camera: false }'",
        ["refraction_index"] = "a format-1 key, now 'ior'",
        ["fuzz"] = "a format-1 key, now 'roughness'",
        ["texture"] = "a format-1 key: every colour input takes a texture block ('color: { type: image, ... }')",
        ["mask"] = "a format-1 key: 'blend' takes a number or a texture",
        ["candela"] = "a format-1 key: 'intensity' is in candela",
        ["normalize"] = "a format-1 key: use 'power'",
        ["anchor"] = "a format-1 key, now 'corner'",
        ["rotation"] = "a format-1 key: the sky turns with 'rotate: [x, y, z]'",
        ["orientation"] = "a format-1 key: the sky turns with 'rotate: [x, y, z]'",
        ["size"] = "a format-1 key: the sun's half-angle is 'angular_radius'",
        ["units"] = "a format-1 key: physical skies are always in nits (set the camera's 'ev100')",
        ["noise_basis"] = "a format-1 key: the improved Perlin basis is the only one",
        ["grain_strength"] = "a format-1 key, now 'noise_strength'",
        ["twist_degrees"] = "a format-1 key, now 'twist'",
        ["min_slope_deg"] = "a format-1 key, now 'min_slope'",
        ["max_slope_deg"] = "a format-1 key, now 'max_slope'",
        ["displacement_method"] = "a format-1 key, now 'method'",
        ["displacement_bound"] = "a format-1 key: 'bound' in the material's 'displacement:'",
        ["y"] = "a format-1 key, now 'point: [0, y, 0]'",
        ["albedo"] = "not a key: the colour is 'color'",
    };

    private sealed class ClassInfo
    {
        public required Type Type { get; init; }
        public required Dictionary<string, PropertyInfo> Keys { get; init; }
        public string[]? Types { get; init; }
    }

    private static readonly Dictionary<Type, ClassInfo> Classes = new();

    private static ClassInfo Info(Type t)
    {
        lock (Classes)
        {
            if (Classes.TryGetValue(t, out var ci)) return ci;
            var keys = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite) continue;
                string? alias = p.GetCustomAttribute<YamlMemberAttribute>()?.Alias;
                keys[alias ?? YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance.Apply(p.Name)] = p;
            }
            ci = new ClassInfo { Type = t, Keys = keys, Types = t.GetCustomAttribute<SchemaTypesAttribute>()?.Types };
            Classes[t] = ci;
            return ci;
        }
    }

    /// <summary>The YAML keys of a format class (for the schema and the tests).</summary>
    public static IReadOnlyDictionary<string, PropertyInfo> KeysOf(Type t) => Info(t).Keys;

    /// <summary>Validates a scene's text. Errors and warnings in file order.</summary>
    public static List<Issue> Validate(string yaml)
    {
        var issues = new List<Issue>();
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            issues.Add(new Issue(true, (int)ex.Start.Line, (int)ex.Start.Column, $"not valid YAML: {ex.InnerException?.Message ?? ex.Message}"));
            return issues;
        }
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            issues.Add(new Issue(true, 1, 1, "a scene file is a mapping of sections (format_version, cameras, materials, ...)"));
            return issues;
        }

        var version = root.Children.FirstOrDefault(kv => kv.Key is YamlScalarNode { Value: "format_version" });
        if (version.Key == null)
            issues.Add(new Issue(true, 1, 1,
                "no 'format_version': a scene of format 1? Convert it with SceneMigrate " +
                "(dotnet run --project src/Tools/SceneMigrate -- <scene.yaml>), or add 'format_version: 2'"));
        else if (version.Value is not YamlScalarNode { Value: var v } || v != FormatVersion.ToString(CultureInfo.InvariantCulture))
            issues.Add(At(version.Value, true,
                $"format_version {(version.Value as YamlScalarNode)?.Value}: this engine reads format {FormatVersion}"));

        Walk(root, typeof(SceneData), "scene", issues, impliedType: null);
        issues.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));
        return issues;
    }

    private static Issue At(YamlNode node, bool error, string message) =>
        new(error, (int)node.Start.Line, (int)node.Start.Column, message);

    private static void Walk(YamlNode node, Type type, string what, List<Issue> issues, string? impliedType)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
        {
            if (node is not YamlScalarNode) issues.Add(At(node, true, $"{what}: expected a text value"));
            return;
        }
        if (type == typeof(int) || type == typeof(float) || type == typeof(bool))
        {
            if (node is not YamlScalarNode s) { issues.Add(At(node, true, $"{what}: expected {KindName(type)}")); return; }
            bool ok = type == typeof(bool)
                ? s.Value is "true" or "false"
                : type == typeof(int)
                    ? int.TryParse(s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    : float.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            if (!ok) issues.Add(At(node, true, $"{what}: '{s.Value}' is not {KindName(type)}"));
            return;
        }
        if (type == typeof(List<float>))
        {
            CheckNumbers(node, what, issues, allowHex: true);
            return;
        }
        if (type == typeof(object))
        {
            // A number or a list of numbers (transform and texture 'scale').
            CheckNumbers(node, what, issues, allowHex: false);
            return;
        }
        if (type == typeof(MaterialParam))
        {
            if (node is YamlMappingNode) Walk(node, typeof(TextureData), what, issues, null);
            else CheckNumbers(node, what, issues, allowHex: true);
            return;
        }
        if (type == typeof(Dictionary<string, string>))
        {
            if (node is not YamlMappingNode dm) { issues.Add(At(node, true, $"{what}: expected a mapping of names")); return; }
            foreach (var kv in dm.Children)
                if (kv.Value is not YamlScalarNode) issues.Add(At(kv.Value, true, $"{what}: expected a name"));
            return;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elem = type.GetGenericArguments()[0];
            if (node is not YamlSequenceNode seq) { issues.Add(At(node, true, $"{what}: expected a list")); return; }
            foreach (var item in seq.Children)
                Walk(item, elem, what, issues, impliedType);
            return;
        }

        // A format class: a mapping of keys.
        if (node is not YamlMappingNode map)
        {
            issues.Add(At(node, true, $"{what}: expected a block of keys"));
            return;
        }
        var info = Info(type);
        string block = BlockName(type);
        string? blockType = impliedType;
        if (info.Types != null)
        {
            var typeNode = map.Children.FirstOrDefault(kv => kv.Key is YamlScalarNode { Value: "type" });
            if (typeNode.Value is YamlScalarNode { Value: { } tv })
            {
                if (Array.IndexOf(info.Types, tv) < 0)
                    issues.Add(At(typeNode.Value, true,
                        $"unknown {block} type '{tv}'{Suggest(tv, info.Types)} (types: {string.Join(", ", info.Types)})"));
                else
                    blockType = tv;
            }
            else if (typeNode.Key == null && impliedType == null && !TypeIsOptional(type))
            {
                issues.Add(At(map, true, $"{block} without 'type' (types: {string.Join(", ", info.Types)})"));
            }
        }

        foreach (var kv in map.Children)
        {
            if (kv.Key is not YamlScalarNode { Value: { } key })
            {
                issues.Add(At(kv.Key, true, $"{block}: a key must be a name"));
                continue;
            }
            if (!info.Keys.TryGetValue(key, out var prop))
            {
                string hint = FormerKeys.TryGetValue(key, out var former) ? $" ({former})" : Suggest(key, info.Keys.Keys);
                issues.Add(At(kv.Key, true, $"unknown key '{key}' in {block}{hint}"));
                continue;
            }
            if (prop.GetCustomAttribute<SchemaFreeFormAttribute>() != null) continue;

            var only = prop.GetCustomAttribute<OnlyForAttribute>();
            if (only != null && blockType != null && Array.IndexOf(only.Types, blockType) < 0)
                issues.Add(At(kv.Key, false, $"'{key}' has no effect on a {blockType} {block} (it applies to {string.Join(", ", only.Types)})"));

            var en = prop.GetCustomAttribute<SchemaEnumAttribute>();
            if (en != null && kv.Value is YamlScalarNode { Value: { } ev } && Array.IndexOf(en.Values, ev) < 0)
            {
                issues.Add(At(kv.Value, true, $"'{key}': unknown value '{ev}'{Suggest(ev, en.Values)} (values: {string.Join(", ", en.Values)})"));
                continue;
            }

            // A template is a group without a type.
            string? childImplied = type == typeof(SceneData) && key == "templates" ? "group" : null;
            Walk(kv.Value, prop.PropertyType, $"'{key}'", issues, childImplied);
        }
    }

    /// <summary>Blocks whose type has a default (ground: infinite_plane).</summary>
    private static bool TypeIsOptional(Type t) => t == typeof(GroundData);

    private static string BlockName(Type t) => t.Name switch
    {
        nameof(SceneData) => "the scene",
        nameof(MaterialData) => "material",
        nameof(LightData) => "light",
        nameof(EntityData) => "entity",
        nameof(MediumData) => "medium",
        nameof(TextureData) => "texture",
        nameof(SkyData) => "sky",
        nameof(GroundData) => "ground",
        nameof(CameraData) => "camera",
        nameof(RenderData) => "render",
        nameof(SunData) => "sun",
        _ => t.Name.EndsWith("Data") ? Snake(t.Name[..^4]) : Snake(t.Name),
    };

    private static string Snake(string s) =>
        YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance.Apply(s).Replace('_', ' ');

    private static string KindName(Type t) =>
        t == typeof(bool) ? "true or false" : t == typeof(int) ? "an integer" : "a number";

    private static void CheckNumbers(YamlNode node, string what, List<Issue> issues, bool allowHex)
    {
        if (node is YamlScalarNode s)
        {
            if (allowHex && s.Value is { } h && h.StartsWith('#'))
            {
                if (!MaterialParamConverter.TryParseHex(h, out _))
                    issues.Add(At(node, true, $"{what}: '{h}' is not a colour (#rrggbb)"));
                return;
            }
            if (!float.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                issues.Add(At(node, true, $"{what}: '{s.Value}' is not a number"));
            return;
        }
        if (node is YamlSequenceNode seq)
        {
            foreach (var item in seq.Children)
                if (item is not YamlScalarNode { Value: var iv }
                    || !float.TryParse(iv, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    issues.Add(At(item, true, $"{what}: expected numbers"));
            return;
        }
        issues.Add(At(node, true, $"{what}: expected a number or a list of numbers"));
    }

    private static string Suggest(string key, IEnumerable<string> known)
    {
        string? best = null;
        int bestD = int.MaxValue;
        foreach (var k in known)
        {
            int d = Levenshtein(key, k);
            if (d < bestD) { bestD = d; best = k; }
        }
        int limit = Math.Max(1, key.Length / 3);
        return best != null && bestD <= limit ? $" (did you mean '{best}'?)" : "";
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
