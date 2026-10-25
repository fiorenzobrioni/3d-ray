namespace RayTracer.Scene;

/// <summary>
/// The values the <c>type</c> key of a YAML block may take. The scene
/// validator rejects any other value, and the JSON Schema lists them as an
/// enum (PIANO-REVIEW.md Phase 10).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SchemaTypesAttribute(params string[] types) : Attribute
{
    public string[] Types { get; } = types;
}

/// <summary>
/// A key that only has an effect on the listed values of the block's
/// <c>type</c>; written on another type it is reported as a warning.
/// Keys without the attribute apply to every type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OnlyForAttribute(params string[] types) : Attribute
{
    public string[] Types { get; } = types;
}

/// <summary>
/// A string key whose value must be one of a fixed list (for example
/// <c>power_unit: watt | lumen</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SchemaEnumAttribute(params string[] values) : Attribute
{
    public string[] Values { get; } = values;
}

/// <summary>
/// A key whose value is free-form metadata the engine does not read (the
/// <c>validation:</c> block of the review scenes): it is not validated.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SchemaFreeFormAttribute : Attribute;

/// <summary>
/// One-line description of a key, copied into the JSON Schema so an editor
/// can show it while completing the key.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SchemaDescriptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
