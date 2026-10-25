using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RayTracer.Scene;

/// <summary>
/// The JSON Schema of the scene format (PIANO-REVIEW.md Phase 10), generated
/// from the classes of <see cref="SceneData"/>: every key with its kind, the
/// values of every <c>type</c> and enum, the keys each type takes, and the
/// classes' XML summaries as descriptions. Editors with a YAML language
/// server complete and check scenes against it; the copy in
/// <c>docs/reference/scene-schema.json</c> is kept current by a test.
/// </summary>
public static partial class SceneSchema
{
    public const string SchemaId = "https://github.com/fiorenzobrioni/3d-ray/docs/reference/scene-schema.json";

    /// <summary>The schema as indented JSON (descriptions from <paramref name="xmlDoc"/> when given).</summary>
    public static string Generate(string? xmlDoc = null)
    {
        var docs = LoadDocs(xmlDoc);
        var defs = new JsonObject();
        var pending = new Queue<Type>();
        var seen = new HashSet<Type>();
        string Ref(Type t)
        {
            if (seen.Add(t)) pending.Enqueue(t);
            return "#/definitions/" + t.Name;
        }

        var root = Object(typeof(SceneData), docs, Ref);
        root["required"] = new JsonArray("format_version");
        ((JsonObject)root["properties"]!)["format_version"] = new JsonObject { ["const"] = SceneValidator.FormatVersion,
            ["description"] = "Version of the scene format: 2. Required." };
        seen.Add(typeof(SceneData));
        while (pending.Count > 0)
        {
            var t = pending.Dequeue();
            defs[t.Name] = Object(t, docs, Ref);
        }

        var schema = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["$id"] = SchemaId,
            ["title"] = "3D-Ray scene, format 2",
        };
        foreach (var kv in root.ToList()) { root.Remove(kv.Key); schema[kv.Key] = kv.Value; }
        schema["definitions"] = new JsonObject(defs.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())));
        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
    }

    private static JsonObject Object(Type t, Dictionary<string, string> docs, Func<Type, string> refOf)
    {
        var props = new JsonObject();
        var keys = SceneValidator.KeysOf(t);
        foreach (var (key, p) in keys.OrderBy(k => k.Key == "type" ? "" : k.Key, StringComparer.Ordinal))
        {
            var s = Value(p, refOf);
            if (p.GetCustomAttribute<SchemaEnumAttribute>() is { } en)
                s["enum"] = new JsonArray(en.Values.Select(v => (JsonNode)v).ToArray());
            if (docs.TryGetValue($"P:{t.FullName}.{p.Name}", out var d)) s["description"] = d;
            props[key] = s;
        }
        var o = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = props,
        };
        if (docs.TryGetValue($"T:{t.FullName}", out var td)) o["description"] = td;

        if (t.GetCustomAttribute<SchemaTypesAttribute>() is { } types)
        {
            ((JsonObject)props["type"]!)["enum"] = new JsonArray(types.Types.Select(v => (JsonNode)v).ToArray());
            // The keys of each type: the common ones and those marked for it.
            var rules = new JsonArray();
            foreach (var ty in types.Types)
            {
                var allowed = keys.Where(k => k.Value.GetCustomAttribute<OnlyForAttribute>() is not { } only
                                              || only.Types.Contains(ty))
                                  .Select(k => (JsonNode)k.Key).ToArray();
                rules.Add(new JsonObject
                {
                    ["if"] = new JsonObject { ["properties"] = new JsonObject { ["type"] = new JsonObject { ["const"] = ty } },
                                              ["required"] = new JsonArray("type") },
                    ["then"] = new JsonObject { ["propertyNames"] = new JsonObject { ["enum"] = new JsonArray(allowed) } },
                });
            }
            o["allOf"] = rules;
        }
        return o;
    }

    private static readonly JsonObject Hex = new() { ["type"] = "string", ["pattern"] = "^#[0-9a-fA-F]{6}$" };

    private static JsonObject Value(PropertyInfo p, Func<Type, string> refOf)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (t == typeof(int)) return new JsonObject { ["type"] = "integer" };
        if (t == typeof(float)) return new JsonObject { ["type"] = "number" };
        if (t == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (t == typeof(List<float>))
            return new JsonObject { ["anyOf"] = new JsonArray(Numbers(), new JsonObject { ["type"] = "number" }, Hex.DeepClone()) };
        if (t == typeof(List<List<float>>)) return new JsonObject { ["type"] = "array", ["items"] = Numbers() };
        if (t == typeof(object))
            return new JsonObject { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "number" }, Numbers()) };
        if (t == typeof(MaterialParam))
            return new JsonObject
            {
                ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "number" }, Numbers(), Hex.DeepClone(),
                                          new JsonObject { ["$ref"] = refOf(typeof(TextureData)) }),
            };
        if (t == typeof(Dictionary<string, string>))
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "string" } };
        if (t == typeof(Dictionary<string, object>)) return new JsonObject { ["type"] = "object" };
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var e = t.GetGenericArguments()[0];
            return new JsonObject { ["type"] = "array", ["items"] = e == typeof(string)
                ? new JsonObject { ["type"] = "string" } : new JsonObject { ["$ref"] = refOf(e) } };
        }
        return new JsonObject { ["$ref"] = refOf(t) };
    }

    private static JsonObject Numbers() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" } };

    // ── XML documentation → one-line descriptions ───────────────────────────

    private static Dictionary<string, string> LoadDocs(string? xmlDoc)
    {
        var map = new Dictionary<string, string>();
        if (xmlDoc == null || !File.Exists(xmlDoc)) return map;
        foreach (var member in XDocument.Load(xmlDoc).Descendants("member"))
        {
            string? name = member.Attribute("name")?.Value;
            var summary = member.Element("summary");
            if (name == null || summary == null || !name.Contains(".Scene.")) continue;
            map[name] = Plain(summary);
        }
        return map;
    }

    private static string Plain(XElement summary)
    {
        string text = string.Concat(summary.Nodes().Select(n => n switch
        {
            XText x => x.Value,
            XElement { Name.LocalName: "c" } c => "'" + c.Value + "'",
            XElement { Name.LocalName: "see" } s => (s.Attribute("cref")?.Value ?? "").Split('.')[^1],
            XElement e => e.Value,
            _ => "",
        }));
        return Spaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
