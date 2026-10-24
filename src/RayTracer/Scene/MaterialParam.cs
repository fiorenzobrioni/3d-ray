using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace RayTracer.Scene;

/// <summary>
/// A material parameter written as a number (<c>0.4</c>), a colour
/// (<c>[0.8, 0.1, 0.1]</c>) or a texture block (<c>{ type: image, path: ... }</c>).
/// Used by the <c>openpbr</c> material, whose every input may be textured.
/// </summary>
public sealed class MaterialParam
{
    public List<float>? Values { get; init; }
    public TextureData? Texture { get; init; }
}

/// <summary>
/// YAML converters for <see cref="MaterialParam"/> and for <c>List&lt;float&gt;</c>,
/// which then also accepts a lone number as a one-element list.
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
            values.Add(ParseFloat(parser.Consume<Scalar>()));
        }
        return type == typeof(MaterialParam) ? new MaterialParam { Values = values } : values;
    }

    private static float ParseFloat(Scalar s)
    {
        if (float.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
        throw new YamlException(s.Start, s.End, $"'{s.Value}' is not a number");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotSupportedException();
}
