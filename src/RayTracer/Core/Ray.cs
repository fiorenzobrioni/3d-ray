using System.Numerics;

namespace RayTracer.Core;

public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    public Vector3 At(float t) => Origin + t * Direction;
}
