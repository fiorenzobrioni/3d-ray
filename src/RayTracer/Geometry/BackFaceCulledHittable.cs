using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and reports a hit only when the inner
/// surface is struck on its front face — back-face hits are forwarded as
/// misses so the BVH traversal continues past the wrapper to whatever lies
/// behind it.
///
/// <para><b>Why.</b> The visible emissive proxy that backs an
/// <see cref="Lights.AreaLight"/> (a <see cref="Quad"/>) only emits from one
/// side; the opposite face renders as a flat black rectangle and intrudes
/// into camera/specular paths that look at the panel from above. Production
/// renderers (Arnold "thin" quad lights, Cycles area lights with single-sided
/// emission, Renderman portal lights) handle this by culling intersections on
/// the dark side. Wrapping the proxy in this class delivers the same
/// behaviour without any change to the underlying primitive.</para>
///
/// <para>Front-face hits are forwarded unchanged, preserving NEE shadow-ray
/// occlusion against the lit side, the Veach-MIS BSDF-hit emission path
/// (<c>Renderer.WeightEmission</c>) and the AABB the BVH builds the proxy
/// into.</para>
/// </summary>
public class BackFaceCulledHittable : IHittable
{
    private readonly IHittable _inner;

    public BackFaceCulledHittable(IHittable inner) => _inner = inner;

    /// <summary>
    /// Mirrors the wrapped primitive's <c>Seed</c> so any procedural texture
    /// referencing <c>HitRecord.ObjectSeed</c> stays consistent with the
    /// inner hittable.
    /// </summary>
    public int Seed
    {
        get => _inner.Seed;
        set => _inner.Seed = value;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        var tempRec = new HitRecord();
        if (!_inner.Hit(ray, tMin, tMax, ref tempRec))
            return false;
        if (!tempRec.FrontFace)
            return false; // back face — pretend the surface isn't there
        rec = tempRec;
        return true;
    }

    public AABB BoundingBox() => _inner.BoundingBox();
}
