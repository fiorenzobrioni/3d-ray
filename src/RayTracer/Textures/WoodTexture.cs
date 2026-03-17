using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

public class WoodTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly float _scale;
    private readonly ITexture _lightWoodColor;
    private readonly ITexture _darkWoodColor;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }
    public float NoiseStrength { get; set; } = 2.0f;

    public WoodTexture(float scale = 4f, float turbulenceStrength = 2f)
    {
        _noise = new Perlin();
        _scale = scale;
        NoiseStrength = turbulenceStrength;
        _lightWoodColor = new SolidColor(0.85f, 0.65f, 0.40f);
        _darkWoodColor = new SolidColor(0.60f, 0.40f, 0.20f);
    }

    public WoodTexture(float scale, float turbulenceStrength, Vector3 lightColor, Vector3 darkColor)
    {
        _noise = new Perlin();
        _scale = scale;
        NoiseStrength = turbulenceStrength;
        _lightWoodColor = new SolidColor(lightColor);
        _darkWoodColor = new SolidColor(darkColor);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        float dist = MathF.Sqrt(transformedP.X * transformedP.X + transformedP.Z * transformedP.Z);
        float distNoise = dist + NoiseStrength * _noise.Noise(transformedP);
        
        float ring = distNoise * _scale;
        float interpolationVal = ring - MathF.Floor(ring);

        Vector3 cLight = _lightWoodColor.Value(u, v, transformedP, objectSeed);
        Vector3 cDark = _darkWoodColor.Value(u, v, transformedP, objectSeed);

        return Vector3.Lerp(cDark, cLight, interpolationVal);
    }
}
