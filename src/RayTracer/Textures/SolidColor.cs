using System.Numerics;

namespace RayTracer.Textures;

public class SolidColor : ITexture
{
    private readonly Vector3 _colorValue;

    public SolidColor(Vector3 color)
    {
        _colorValue = color;
    }

    public SolidColor(float red, float green, float blue)
    {
        _colorValue = new Vector3(red, green, blue);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        return _colorValue;
    }
}
