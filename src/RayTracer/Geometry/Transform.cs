using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// A wrapper that applies a 4x4 transformation matrix to any IHittable.
/// Handles ray transformation to Object Space and normal transformation back to World Space.
///
/// LocalPoint is deliberately preserved in object-local space so that procedural
/// textures (marble, wood, noise, checker) tile consistently regardless of how
/// the object is placed in the world. World-space position is in rec.Point.
///
/// Implements ISamplable when the wrapped object is itself ISamplable, enabling
/// GeometryLight (NEE) to work correctly on transformed emissive primitives.
/// The Sample() method transforms both point and normal to world space and
/// computes the correct world-space area using the surface-element Jacobian:
///   area_world = area_obj × |det(M₃ₓ₃)| × |M⁻ᵀ × n̂_obj|
/// This is exact for any TRS (or general affine) matrix.
/// </summary>
public class Transform : IHittable, ISamplable
{
    private readonly IHittable _object;
    private readonly Matrix4x4 _transform;
    private readonly Matrix4x4 _inverse;
    private readonly Matrix4x4 _normalMatrix; // Transpose of the inverse

    // Precomputed absolute determinant of the 3×3 linear sub-matrix.
    // Used in Sample() to convert object-space area to world-space area.
    private readonly float _absDetM;

    /// <summary>
    /// The wrapped IHittable (in object space). Used by SceneLoader.IsInfinitePlane()
    /// to detect Transform-wrapped InfinitePlane instances (BUG-02 fix).
    /// </summary>
    public IHittable Inner => _object;

    public Transform(IHittable hittable, Matrix4x4 matrix)
    {
        _object = hittable;
        _transform = matrix;

        if (!Matrix4x4.Invert(_transform, out _inverse))
            _inverse = Matrix4x4.Identity;

        // Normal matrix: transpose of the inverse — handles non-uniform scaling correctly
        _normalMatrix = Matrix4x4.Transpose(_inverse);

        // |det(M₃ₓ₃)| — Sarrus / cofactor expansion along the first row.
        // For a TRS matrix this equals sx × sy × sz (product of scale factors).
        // Used in Sample() as the volume-scaling factor for area conversion.
        float det = _transform.M11 * (_transform.M22 * _transform.M33 - _transform.M23 * _transform.M32)
                  - _transform.M12 * (_transform.M21 * _transform.M33 - _transform.M23 * _transform.M31)
                  + _transform.M13 * (_transform.M21 * _transform.M32 - _transform.M22 * _transform.M31);
        _absDetM = MathF.Abs(det);
    }

    public int Seed
    {
        get => _object.Seed;
        set => _object.Seed = value;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Transform ray into object space
        Vector3 origin    = Vector3.Transform(ray.Origin, _inverse);
        Vector3 direction = Vector3.TransformNormal(ray.Direction, _inverse);
        var localRay = new Ray(origin, direction);

        if (!_object.Hit(localRay, tMin, tMax, ref rec))
            return false;

        // rec.LocalPoint is already in object-local space (set by the wrapped primitive).
        // Leave it as-is — this is intentional. Procedural textures sample LocalPoint,
        // so they tile in the object's own coordinate system regardless of world transforms.
        // rec.LocalPoint = rec.LocalPoint;  // <-- purposely NOT transformed

        // Transform the hit point back to world space
        rec.Point = Vector3.Transform(rec.Point, _transform);

        // Transform the normal using the normal matrix (handles non-uniform scale)
        Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(rec.Normal, _normalMatrix));
        rec.SetFaceNormal(ray, worldNormal);

        // Tangent and bitangent are direction vectors, they transform with the forward matrix
        rec.Tangent   = Vector3.Normalize(Vector3.TransformNormal(rec.Tangent,   _transform));
        rec.Bitangent = Vector3.Normalize(Vector3.TransformNormal(rec.Bitangent, _transform));

        return true;
    }

    public AABB BoundingBox()
    {
        AABB bbox = _object.BoundingBox();
        Vector3 min = bbox.Min;
        Vector3 max = bbox.Max;

        // Transform all 8 corners of the AABB to find the new world-space AABB
        Span<Vector3> corners = stackalloc Vector3[]
        {
            new(min.X, min.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, max.Y, max.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, min.Z),
            new(max.X, max.Y, max.Z)
        };

        Vector3 newMin = new(float.MaxValue);
        Vector3 newMax = new(float.MinValue);

        foreach (var c in corners)
        {
            Vector3 tc = Vector3.Transform(c, _transform);
            newMin = Vector3.Min(newMin, tc);
            newMax = Vector3.Max(newMax, tc);
        }

        return new AABB(newMin, newMax);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ISamplable — direct lighting (NEE) support for transformed emissives
    //
    // Delegates to the inner primitive's Sample(), then maps the result to
    // world space. The world-space area is derived from the surface-element
    // transformation formula:
    //
    //   dA_world = |det(M)| × |M⁻ᵀ × n̂_obj| × dA_obj
    //
    // Derivation: a surface element spanned by tangents (∂p/∂u, ∂p/∂v) in
    // object space maps to (M·∂p/∂u, M·∂p/∂v) in world space. Using the
    // vector area identity (M·a)×(M·b) = det(M)·M⁻ᵀ·(a×b) gives the
    // formula above. The _normalMatrix field (already M⁻ᵀ) and _absDetM
    // are precomputed in the constructor to avoid per-sample overhead.
    //
    // Returns (Point=Zero, Normal=UnitY, Area=0) if the inner object does not
    // implement ISamplable. SceneLoader never registers such a Transform as a
    // GeometryLight, so this path should never be reached in practice.
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        if (_object is not ISamplable inner)
            return (Vector3.Zero, Vector3.UnitY, 0f); // guard — should not happen

        var (pointObj, normalObj, areaObj) = inner.Sample();

        // Transform sample point to world space
        Vector3 worldPoint = Vector3.Transform(pointObj, _transform);

        // Transform normal via M⁻ᵀ (correct for non-uniform scale)
        Vector3 normalRaw = Vector3.TransformNormal(normalObj, _normalMatrix);
        float normalLen = normalRaw.Length();
        if (normalLen < 1e-6f)
            return (worldPoint, normalObj, areaObj); // degenerate transform — return unchanged

        Vector3 worldNormal = normalRaw / normalLen;

        // World-space area: areaObj × |det(M)| × |M⁻ᵀ · n̂_obj|
        // The normalLen term = |M⁻ᵀ · n̂_obj| accounts for the directional
        // change of the surface element; _absDetM accounts for volume scaling.
        float worldArea = areaObj * _absDetM * normalLen;

        return (worldPoint, worldNormal, worldArea);
    }
}
