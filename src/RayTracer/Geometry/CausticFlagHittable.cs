using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and stamps the MNEE caustic flags
/// (<c>rec.CausticCaster</c> / <c>rec.CausticReceiver</c>) on every reported
/// hit, mirroring the <see cref="CameraInvisibleHittable"/> pattern.
///
/// <para><b>Caster.</b> A <c>caustic_caster</c> is a smooth specular/transmissive
/// interface (glass sphere, lens, mirror) through which
/// <see cref="Rendering.ManifoldWalker"/> focuses light. The wrapped inner
/// geometry is also collected into the renderer's
/// <see cref="Rendering.CausticCasterRegistry"/> so the manifold walk can run
/// Newton iterations against it directly (not the whole BVH). Stamping the hit
/// additionally lets the transparent-shadow-ray walker
/// (<see cref="ShadowRay"/>) suppress the straight transmitted contribution
/// through this surface, so MNEE does not double-count it.</para>
///
/// <para><b>Receiver.</b> A <c>caustic_receiver</c> is a surface on which focused
/// caustics should appear. MNEE is attempted only for shading points carrying
/// this flag, bounding the extra cost to where it is wanted.</para>
///
/// <para>Both flags are independent and may be combined (a glass object that is
/// itself lit by caustics). <c>BoundingBox</c> and <c>Seed</c> pass through
/// unchanged, preserving BVH partitioning and procedural-texture seeds.</para>
/// </summary>
public sealed class CausticFlagHittable : IHittable
{
    public IHittable Inner => _inner;

    private readonly IHittable _inner;
    private readonly bool _caster;
    private readonly bool _receiver;

    public CausticFlagHittable(IHittable inner, bool caster, bool receiver)
    {
        _inner    = inner;
        _caster   = caster;
        _receiver = receiver;
    }

    public int Seed
    {
        get => _inner.Seed;
        set => _inner.Seed = value;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (!_inner.Hit(ray, tMin, tMax, ref rec))
            return false;
        if (_caster)   rec.CausticCaster   = true;
        if (_receiver) rec.CausticReceiver = true;
        return true;
    }

    public AABB BoundingBox() => _inner.BoundingBox();
}
