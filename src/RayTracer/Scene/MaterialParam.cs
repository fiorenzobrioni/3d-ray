using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace RayTracer.Scene;

/// <summary>
/// A material parameter written as a number (<c>0.4</c>), a colour
/// (<c>[0.8, 0.1, 0.1]</c>, or <c>"#cc1a1a"</c>) or a texture block
/// (<c>{ type: image, path: ... }</c>).
/// </summary>
public sealed class MaterialParam
{
    public List<float>? Values { get; init; }
    public TextureData? Texture { get; init; }
}

/// <summary>
/// YAML converters for <see cref="MaterialParam"/> and for <c>List&lt;float&gt;</c>,
/// which then also accepts a lone number as a one-element list, and a colour
/// written <c>"#rrggbb"</c>: sRGB-encoded, decoded to linear (every
/// <c>[r, g, b]</c> in a scene is linear Rec.709).
/// </summary>
public sealed class MaterialParamConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(MaterialParam) || type == typeof(List<float>);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is MappingStart)
        {
            if (type != typeof(MaterialParam))
                throw new YamlException(parser.Current.Start, parser.Current.End, "expected a number or a list of numbers");
            return new MaterialParam { Texture = (TextureData?)rootDeserializer(typeof(TextureData)) };
        }

        var values = new List<float>();
        if (parser.TryConsume<SequenceStart>(out _))
        {
            while (!parser.TryConsume<SequenceEnd>(out _))
                values.Add(ParseFloat(parser.Consume<Scalar>()));
        }
        else
        {
            var scalar = parser.Consume<Scalar>();
            if (scalar.Value.StartsWith('#'))
            {
                if (!TryParseHex(scalar.Value, out var rgb))
                    throw new YamlException(scalar.Start, scalar.End, $"'{scalar.Value}' is not a colour (#rrggbb)");
                values.AddRange(rgb);
            }
            else
                values.Add(ParseFloat(scalar));
        }
        return type == typeof(MaterialParam) ? new MaterialParam { Values = values } : values;
    }

    /// <summary>Parses <c>#rrggbb</c> (sRGB) into linear RGB.</summary>
    public static bool TryParseHex(string s, out float[] rgb)
    {
        rgb = new float[3];
        if (s.Length != 7 || s[0] != '#') return false;
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(s.AsSpan(1 + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
                return false;
            rgb[i] = Rendering.Srgb.Decode(b / 255f);
        }
        return true;
    }

    private static float ParseFloat(Scalar s)
    {
        if (float.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
        throw new YamlException(s.Start, s.End, $"'{s.Value}' is not a number");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotSupportedException();
}
