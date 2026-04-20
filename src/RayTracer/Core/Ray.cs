using System.Numerics;

namespace RayTracer.Core;

public readonly struct Ray
{
    public Vector3 Origin { get; }
    public Vector3 Direction { get; }

    // Component-wise reciprocal of Direction, precomputed once per ray.
    // A single ray hits many AABBs during BVH traversal, so caching 1/dir here
    // replaces N divisions (N = AABB tests) with 3 divisions (at construction).
    // For axis-aligned rays a zero component becomes ±infinity, which is the
    // well-known slab-test trick: the per-axis comparison still rejects/accepts
    // correctly under IEEE 754 semantics.
    public Vector3 InvDirection { get; }

    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = direction;
        InvDirection = Vector3.One / direction;
    }

    public Vector3 At(float t) => Origin + t * Direction;
}
