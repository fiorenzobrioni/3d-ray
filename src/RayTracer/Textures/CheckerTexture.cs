using System.Numerics;

namespace RayTracer.Textures;

public class CheckerTexture : ITexture
{
    private readonly float _invScale;
    private readonly ITexture _even;
    private readonly ITexture _odd;

    public CheckerTexture(float scale, ITexture even, ITexture odd)
    {
        _invScale = 1.0f / scale;
        _even = even;
        _odd = odd;
    }

    public CheckerTexture(float scale, Vector3 even, Vector3 odd)
    {
        _invScale = 1.0f / scale;
        _even = new SolidColor(even);
        _odd = new SolidColor(odd);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // Aggiungiamo il piccolo bias a tutti gli assi per evitare salti di precisione a 0.0
        var xInteger = (int)MathF.Floor(_invScale * p.X + 0.00001f);
        var yInteger = (int)MathF.Floor(_invScale * p.Y + 0.00001f);
        var zInteger = (int)MathF.Floor(_invScale * p.Z + 0.00001f);

        bool isEven = (xInteger + yInteger + zInteger) % 2 == 0;

        return isEven ? _even.Value(u, v, p, objectSeed) : _odd.Value(u, v, p, objectSeed);
    }
}
