using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and reports an inflated AABB so the BVH
/// builder treats the leaf as conservatively larger than the geometry it
/// contains. <see cref="Hit"/> forwards to the inner primitive unchanged —
/// only the spatial classification of the BVH is affected.
///
/// <para>Used by the scalar displacement pipeline as a safety margin
/// (Arnold's <c>disp_padding</c>, RenderMan's <c>dispBound</c>): once the
/// mesh has been eagerly displaced the BVH leaf AABBs already reflect the
/// displaced positions, but inflating them by the artist-specified bound
/// absorbs any future shading-time bump perturbation that doesn't move
/// vertices and matches the contract production renderers expose to
/// downstream tooling.</para>
/// </summary>
public sealed class BoundsInflatedHittable : IHittable
{
    private readonly IHittable _inner;
    private readonly Vector3 _pad;

    public BoundsInflatedHittable(IHittable inner, float padding)
    {
        _inner = inner;
        float p = MathF.Max(0f, padding);
        _pad = new Vector3(p, p, p);
    }

    public int Seed
    {
        get => _inner.Seed;
        set => _inner.Seed = value;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
        => _inner.Hit(ray, tMin, tMax, ref rec);

    public AABB BoundingBox()
    {
        var b = _inner.BoundingBox();
        return new AABB(b.Min - _pad, b.Max + _pad);
    }
}
