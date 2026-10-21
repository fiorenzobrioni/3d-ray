using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// A texture computed per point from one or two others (a colour's hue, a
/// scaled weight): the translation of one material model's inputs into
/// another's keeps their textures instead of freezing them to a constant.
/// </summary>
public sealed class DerivedTexture : ITexture
{
    private readonly ITexture _a;
    private readonly ITexture? _b;
    private readonly Func<Vector3, Vector3, Vector3> _f;

    public DerivedTexture(ITexture a, Func<Vector3, Vector3> f)
    {
        _a = a;
        _f = (x, _) => f(x);
    }

    public DerivedTexture(ITexture a, ITexture b, Func<Vector3, Vector3, Vector3> f)
    {
        _a = a;
        _b = b;
        _f = f;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed) =>
        _f(_a.Value(u, v, p, objectSeed), _b?.Value(u, v, p, objectSeed) ?? Vector3.Zero);

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint) =>
        _f(_a.Value(u, v, p, objectSeed, footprint), _b?.Value(u, v, p, objectSeed, footprint) ?? Vector3.Zero);

    public Vector3 Value(in HitRecord rec) =>
        _f(_a.Value(in rec), _b?.Value(in rec) ?? Vector3.Zero);
}
