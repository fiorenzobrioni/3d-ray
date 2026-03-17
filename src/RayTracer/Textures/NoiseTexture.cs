using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

public class NoiseTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly float _scale;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    public NoiseTexture(float scale = 1f)
    {
        _noise = new Perlin();
        _scale = scale;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        Vector3 transformedP = TextureTransform.Apply(p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);
        
        float noiseVal = _noise.Noise(_scale * transformedP);
        float mappedNoise = (noiseVal + 1f) * 0.5f;
        
        return Vector3.One * mappedNoise;
    }
}
