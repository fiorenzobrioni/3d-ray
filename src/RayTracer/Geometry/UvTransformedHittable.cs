using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Wraps an <see cref="IHittable"/> and remaps the <c>(U, V)</c> coordinates
/// reported on every hit by an affine UV transform — scale, then offset, then
/// rotation around <c>(0.5, 0.5)</c>. Used by the <c>world.ground</c> block
/// to expose per-axis tiling, panning and rotation without forcing the user
/// to author a dedicated material with custom UV scaling.
///
/// <para><b>Pipeline.</b>
/// <code>
/// u' = ((u · scale.x + offset.x) − 0.5) · cosθ
///      + ((v · scale.y + offset.y) − 0.5) · sinθ + 0.5
/// v' = ((v · scale.y + offset.y) − 0.5) · cosθ
///      − ((u · scale.x + offset.x) − 0.5) · sinθ + 0.5
/// </code>
/// The inverse rotation is applied so a positive <c>uv_rotation</c>
/// rotates the texture counter-clockwise as viewed from above the surface,
/// matching Photoshop / Substance UV preview conventions.</para>
///
/// <para><b>Footprint &amp; partials.</b> The transform also scales
/// <see cref="HitRecord.DpDu"/> / <see cref="HitRecord.DpDv"/> (inverse of UV
/// scale; rotated by the same θ) so texture-filter footprints and procedural
/// derivative-aware textures (checker, brick) continue to LOD correctly.
/// Tangents/bitangents are likewise rotated in the surface plane so normal
/// maps remain TBN-consistent.</para>
/// </summary>
public sealed class UvTransformedHittable : IHittable
{
    public IHittable Inner => _inner;
    private readonly IHittable _inner;
    private readonly float _scaleU;
    private readonly float _scaleV;
    private readonly float _offsetU;
    private readonly float _offsetV;
    private readonly float _cosTheta;
    private readonly float _sinTheta;
    private readonly bool _hasRotation;

    public UvTransformedHittable(IHittable inner,
                                 float scaleU = 1f, float scaleV = 1f,
                                 float offsetU = 0f, float offsetV = 0f,
                                 float rotationDeg = 0f)
    {
        _inner = inner;
        _scaleU = scaleU != 0f ? scaleU : 1f;
        _scaleV = scaleV != 0f ? scaleV : 1f;
        _offsetU = offsetU;
        _offsetV = offsetV;
        float rad = rotationDeg * MathF.PI / 180f;
        _cosTheta = MathF.Cos(rad);
        _sinTheta = MathF.Sin(rad);
        _hasRotation = MathF.Abs(rotationDeg) > 1e-6f;
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

        // Scale + offset.
        float u = rec.U * _scaleU + _offsetU;
        float v = rec.V * _scaleV + _offsetV;

        // Rotate around (0.5, 0.5). The wrap into [0, 1) happens after the
        // rotation because rotating a fractional UV before the wrap would
        // tear textures along the periodicity seam.
        if (_hasRotation)
        {
            float du = u - 0.5f;
            float dv = v - 0.5f;
            u = du * _cosTheta + dv * _sinTheta + 0.5f;
            v = dv * _cosTheta - du * _sinTheta + 0.5f;
        }

        rec.U = u;
        rec.V = v;

        // Partials: scale inversely (a 10× tile means each UV unit covers 1/10
        // the world distance) and rotate by the same angle so that texture
        // footprint estimation stays correct under combined tile + rotation.
        if (rec.DpDu != Vector3.Zero || rec.DpDv != Vector3.Zero)
        {
            Vector3 dpdu = rec.DpDu / _scaleU;
            Vector3 dpdv = rec.DpDv / _scaleV;
            if (_hasRotation)
            {
                Vector3 newDpDu = dpdu * _cosTheta - dpdv * _sinTheta;
                Vector3 newDpDv = dpdu * _sinTheta + dpdv * _cosTheta;
                dpdu = newDpDu;
                dpdv = newDpDv;
            }
            rec.DpDu = dpdu;
            rec.DpDv = dpdv;
        }

        // Tangent / bitangent rotate the same way (they lie in the UV frame).
        if (_hasRotation && (rec.Tangent != Vector3.Zero || rec.Bitangent != Vector3.Zero))
        {
            Vector3 newT = rec.Tangent * _cosTheta - rec.Bitangent * _sinTheta;
            Vector3 newB = rec.Tangent * _sinTheta + rec.Bitangent * _cosTheta;
            rec.Tangent = newT;
            rec.Bitangent = newB;
        }

        return true;
    }

    public AABB BoundingBox() => _inner.BoundingBox();
}
