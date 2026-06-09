using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and OR's a fixed
/// <see cref="HitVisibilityMask"/> into every reported hit. The renderer reads
/// the mask back in <see cref="Rendering.Renderer.TraceRay"/> /
/// <see cref="ShadowRay"/> and skips the surface for any ray category whose
/// bit is set — implementing the full Arnold <c>visibility.*</c> /
/// Cycles "Ray Visibility" matrix for surface primitives.
///
/// <para><b>Composition.</b> The mask is OR'd, never replaced, so wrapping a
/// hittable that already carries a mask (e.g. a <see cref="CameraInvisibleHittable"/>
/// applied earlier in the pipeline) only adds restrictions — never relaxes
/// them. This matches how Arnold composes visibility across instance
/// overrides.</para>
///
/// <para><b>Why a wrapper, not a Hit-time predicate.</b> Whether a ray belongs
/// to the Diffuse / Glossy / Transmission / Camera / Shadow category depends
/// on caller state (ray depth, previous BSDF lobe, NEE vs camera tracing).
/// The BVH traversal cannot make that distinction without changing the
/// <see cref="IHittable.Hit"/> signature, so the wrapper just annotates the
/// hit and lets the renderer apply the per-category policy in context — the
/// same pattern that <see cref="CameraInvisibleHittable"/> uses for camera
/// visibility alone.</para>
/// </summary>
public class VisibilityFilteredHittable : IHittable
{
    public IHittable Inner => _inner;
    public HitVisibilityMask HiddenFrom => _hiddenFrom;

    private readonly IHittable _inner;
    private readonly HitVisibilityMask _hiddenFrom;

    public VisibilityFilteredHittable(IHittable inner, HitVisibilityMask hiddenFrom)
    {
        _inner = inner;
        _hiddenFrom = hiddenFrom;
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
        rec.VisibilityMask |= _hiddenFrom;
        return true;
    }

    public AABB BoundingBox() => _inner.BoundingBox();
}
