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

    /// <summary>
    /// Optional auxiliary "differential rays" through the +x and +y neighbour
    /// pixels (PBRT §10.1). Present on primary camera rays when texture
    /// filtering is enabled; absent on shadow rays, NEE rays, BSDF bounces.
    /// Consumed at the surface hit to derive the analytic filter footprint
    /// that drives texture anti-aliasing.
    /// </summary>
    public RayDifferential Differentials { get; }
    public bool HasDifferentials { get; }

    /// <summary>
    /// Shutter time of the ray, normalized to the scene's animation range
    /// [0, 1]. Sampled once per camera ray when motion blur is active and
    /// inherited unchanged by every secondary ray of the path (shadow/NEE,
    /// BSDF bounce, medium scatter, SSS walk) — a path samples the scene at
    /// one frozen instant. The renderer owns the propagation: materials build
    /// scattered rays without a time and the renderer re-stamps them via
    /// <see cref="WithTime"/>. 0 when motion blur is inactive.
    /// </summary>
    public float Time { get; }

    public Ray(Vector3 origin, Vector3 direction, float time = 0f)
    {
        Origin = origin;
        Direction = direction;
        InvDirection = Vector3.One / direction;
        Differentials = default;
        HasDifferentials = false;
        Time = time;
    }

    public Ray(Vector3 origin, Vector3 direction, RayDifferential differentials, float time = 0f)
    {
        Origin = origin;
        Direction = direction;
        InvDirection = Vector3.One / direction;
        Differentials = differentials;
        HasDifferentials = true;
        Time = time;
    }

    private Ray(in Ray source, float time)
    {
        Origin = source.Origin;
        Direction = source.Direction;
        InvDirection = source.InvDirection;
        Differentials = source.Differentials;
        HasDifferentials = source.HasDifferentials;
        Time = time;
    }

    /// <summary>
    /// Copy of this ray carrying <paramref name="time"/>. Cheaper than the
    /// public constructors (no re-division for <see cref="InvDirection"/>);
    /// used by the renderer to stamp the path time onto rays built elsewhere.
    /// </summary>
    public Ray WithTime(float time) => new(this, time);

    public Vector3 At(float t) => Origin + t * Direction;
}
