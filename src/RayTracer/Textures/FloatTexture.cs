using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// Scalar-or-texture wrapper for material parameters that can be either a
/// constant float or a grayscale texture lookup (metallic, roughness, ior,
/// sheen, clearcoat, etc.).
///
/// When texture-backed, the RGB sample is reduced to a scalar via the
/// channel average (grayscale maps have R=G=B so any channel works; for
/// accidentally tinted maps the average degrades gracefully). Color-backed
/// parameters (baseColor, subsurfaceColor, transmissionColor) keep their
/// ITexture type directly — they are never reduced.
///
/// Implicit conversion from float preserves the old constructor ergonomics
/// so existing callsites and tests that pass plain floats compile unchanged.
/// </summary>
public sealed class FloatTexture
{
    private readonly ITexture? _texture;
    private readonly float _scalar;

    public FloatTexture(float scalar)
    {
        _scalar = scalar;
        _texture = null;
    }

    public FloatTexture(ITexture texture)
    {
        _texture = texture ?? throw new ArgumentNullException(nameof(texture));
    }

    /// <summary>
    /// Sample the parameter at the given shading point. Texture-backed
    /// samples are reduced to a scalar via channel average.
    /// </summary>
    public float Value(float u, float v, Vector3 p, int objectSeed)
    {
        if (_texture == null) return _scalar;
        Vector3 rgb = _texture.Value(u, v, p, objectSeed);
        return (rgb.X + rgb.Y + rgb.Z) * (1f / 3f);
    }

    /// <summary>True when the parameter is a plain scalar (no texture lookup).</summary>
    public bool IsConstant => _texture == null;

    /// <summary>
    /// The underlying scalar value when <see cref="IsConstant"/> is true.
    /// When texture-backed, returns 0 — callers should query <see cref="Value"/> instead.
    /// </summary>
    public float ConstantValue => _scalar;

    /// <summary>
    /// Returns a representative scalar — the constant when no texture, else the
    /// sample at (u=0, v=0, p=0, seed=0). Used by material-wide queries that
    /// have no hit record context (e.g. IMaterial.DiffuseWeight).
    /// </summary>
    public float RepresentativeValue =>
        _texture == null ? _scalar : Value(0f, 0f, Vector3.Zero, 0);

    public static implicit operator FloatTexture(float value) => new(value);
}
