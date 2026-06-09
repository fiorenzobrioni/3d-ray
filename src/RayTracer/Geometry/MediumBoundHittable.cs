using RayTracer.Core;
using RayTracer.Volumetrics;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and stamps every reported hit with a
/// <see cref="MediumInterface"/> describing the participating media on the
/// two sides of the surface. Created by the loader when the entity binds an
/// <c>interior_medium</c> / <c>exterior_medium</c> in YAML.
///
/// <para>Why a wrapper. The medium binding lives on the entity, not on the
/// geometry primitive or the material — two instances of the same Disney
/// material can share a marble surface but each be filled with a different
/// volume (a white marble bust and a pink one). Wrapping after material
/// resolution lets the binding ride along the standard <see cref="HitRecord"/>
/// without touching every concrete primitive. Mirrors the pattern of
/// <see cref="CameraInvisibleHittable"/>.</para>
///
/// <para><see cref="Inner"/> is exposed so the random-walk SSS integrator
/// (Phase 3) can re-issue intersections restricted to the bound entity, to
/// avoid leaking the walk into other geometry through the global BVH.</para>
/// </summary>
public class MediumBoundHittable : IHittable
{
    public IHittable Inner => _inner;

    public MediumInterface MediumIface => _mi;

    private readonly IHittable _inner;
    private readonly MediumInterface _mi;

    public MediumBoundHittable(IHittable inner, MediumInterface mi)
    {
        _inner = inner;
        _mi = mi;
    }

    public int Seed
    {
        get => _inner.Seed;
        set => _inner.Seed = value;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (!_inner.Hit(ray, tMin, tMax, ref rec))
            return false;
        rec.MediumIface = _mi;
        // Stamp the entity root so the random-walk SSS integrator (Phase 3)
        // can re-issue boundary intersections against the bound geometry
        // alone. Without this, the walk would have to query the world BVH —
        // adjacent geometry sharing a surface (a marble bust resting on a
        // floor, a glass cup with a liquid inside) would let the walk leak
        // into the wrong volume topology.
        rec.EntityRoot = _inner;
        return true;
    }

    public AABB BoundingBox() => _inner.BoundingBox();
}
