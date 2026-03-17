using System.Numerics;

namespace RayTracer.Textures;

public interface ITexture
{
    Vector3 Value(float u, float v, Vector3 p, int objectSeed);
}
