using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

public class MarbleTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly ITexture _baseColor;
    private readonly ITexture _veinColor;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }
    public float NoiseStrength { get; set; } = 10f;

    public MarbleTexture(float scale = 4f)
    {
        _noise = new Perlin();
        _scale = scale;
        _baseColor = new SolidColor(0.9f, 0.9f, 0.9f);
        _veinColor = new SolidColor(0.1f, 0.1f, 0.1f);
    }

    public MarbleTexture(float scale, Vector3 baseColor, Vector3 veinColor)
    {
        _noise = new Perlin();
        _scale = scale;
        _baseColor = new SolidColor(baseColor);
        _veinColor = new SolidColor(veinColor);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        float noiseVal = _noise.Turbulence(transformedP);
        float sinVal = MathF.Sin(_scale * transformedP.Z + NoiseStrength * noiseVal);
        
        float interpolationVal = (sinVal + 1f) * 0.5f;

        Vector3 cBase = _baseColor.Value(u, v, transformedP, objectSeed);
        Vector3 cVein = _veinColor.Value(u, v, transformedP, objectSeed);

        return Vector3.Lerp(cVein, cBase, interpolationVal);
    }
}
