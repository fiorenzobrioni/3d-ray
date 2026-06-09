using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and flags every reported hit with
/// <c>rec.CameraInvisible = true</c>. The renderer consults this flag on the
/// primary ray only (<c>depth == _maxDepth</c> inside
/// <see cref="Rendering.Renderer.TraceRay"/>) and advances the ray past the
/// hit when set — implementing Arnold's <c>camera</c> visibility flag and
/// Cycles' "Ray Visibility → Camera" semantics.
///
/// <para><b>Why a wrapper.</b> The "is this a camera ray?" predicate depends
/// on the ray state, which the BVH traversal does not see. Filtering inside
/// <c>Hit()</c> (the path <see cref="BackFaceCulledHittable"/> takes) would
/// hide the surface from every ray type — including specular reflections,
/// indirect bounces and NEE shadow tests — which is wrong: Arnold and Cycles
/// keep the emitter visible to all rays except the primary one, so a
/// camera-invisible light still appears in a polished mirror or through a
/// glass ball, and still illuminates the scene normally. Marking the hit
/// instead lets the renderer make the decision in context.</para>
///
/// <para>All forwarded hits are otherwise unchanged — <c>BoundingBox</c> and
/// <c>Seed</c> mirror the inner hittable, preserving BVH partitioning and
/// procedural-texture seed consistency.</para>
/// </summary>
public class CameraInvisibleHittable : IHittable
{
    public IHittable Inner => _inner;

    private readonly IHittable _inner;

    public CameraInvisibleHittable(IHittable inner) => _inner = inner;

    public int Seed
    {
        get => _inner.Seed;
        set => _inner.Seed = value;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (!_inner.Hit(ray, tMin, tMax, ref rec))
            return false;
        rec.CameraInvisible = true;
        return true;
    }

    public AABB BoundingBox() => _inner.BoundingBox();
}
