using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// A wrapper that applies a 4x4 transformation matrix to any IHittable.
/// Handles ray transformation to Object Space and normal transformation back to World Space.
///
/// LocalPoint is deliberately preserved in object-local space so that procedural
/// textures (marble, wood, noise, checker) tile consistently regardless of how
/// the object is placed in the world. World-space position is in rec.Point.
///
/// All built-in primitives now set rec.LocalPoint in the entity's own frame
/// (centered on the primitive's Center / anchor — Sphere/Cylinder/Cone/Disk/
/// Annulus/Capsule subtract Center, Quad subtracts Q, InfinitePlane subtracts
/// Point, Box/Torus/Lathe are already at origin). This gives parity with
/// Arnold's `space: object`, Cycles' "Texture Coordinate → Object" and
/// RenderMan's Pref workflow: procedural texture tiles per-entity by default.
///
/// Implements ISamplable when the wrapped object is itself ISamplable, enabling
/// GeometryLight (NEE) to work correctly on transformed emissive primitives.
/// The Sample() method transforms both point and normal to world space and
/// computes the correct world-space area using the surface-element Jacobian:
///   area_world = area_obj × |det(M₃ₓ₃)| × |M⁻ᵀ × n̂_obj|
/// This is exact for any TRS (or general affine) matrix.
/// </summary>
public class Transform : IHittable, ISamplable, IManifoldSurface
{
    private readonly IHittable _object;
    private readonly Matrix4x4 _transform;
    private readonly Matrix4x4 _inverse;
    private readonly Matrix4x4 _normalMatrix; // Transpose of the inverse

    // Precomputed absolute determinant of the 3×3 linear sub-matrix.
    // Used in Sample() to convert object-space area to world-space area.
    private readonly float _absDetM;

    // Cached average |M⁻ᵀ · n̂| over the three canonical axis normals.
    // Used by SurfaceArea as a fast representative for the surface-element Jacobian.
    // Since _normalMatrix is constant after construction, we precompute this once.
    private readonly float _avgNormalLen;

    // Cached world-space AABB. Computed once at construction since both the
    // wrapped object's bbox and the matrix are immutable. Avoids the 8-corner
    // re-projection on every BVH build/sort comparison (called O(N log N) times
    // by BvhNode for Transform-wrapped primitives).
    private readonly AABB _worldBox;

    /// <summary>
    /// The wrapped IHittable (in object space). Used by SceneLoader.IsInfinitePlane()
    /// to detect Transform-wrapped InfinitePlane instances (BUG-02 fix), and by
    /// ExtractGeometryLightsRecursive() to detect Groups inside Transforms.
    /// </summary>
    public IHittable Inner => _object;

    /// <summary>
    /// The forward transformation matrix (object → world space).
    /// Exposed read-only for SceneLoader to compose transforms when extracting
    /// geometry lights from Groups wrapped in Transforms.
    /// </summary>
    public Matrix4x4 TransformMatrix => _transform;

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
        float det3x3 =
            matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32) -
            matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31) +
            matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);
        _absDetM = MathF.Abs(det3x3);

        float nx = Vector3.TransformNormal(Vector3.UnitX, _normalMatrix).Length();
        float ny = Vector3.TransformNormal(Vector3.UnitY, _normalMatrix).Length();
        float nz = Vector3.TransformNormal(Vector3.UnitZ, _normalMatrix).Length();
        _avgNormalLen = (nx + ny + nz) / 3f;

        _worldBox = ComputeWorldBox(_object.BoundingBox(), _transform);
    }

    public int Seed
    {
        get => _object.Seed;
        set => _object.Seed = value;
    }

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        // Transform the ray from world space to object space
        var localOrigin = Vector3.Transform(ray.Origin, _inverse);
        var localDir = Vector3.TransformNormal(ray.Direction, _inverse);

        // Differential propagation through an affine transform: the Jacobian
        // of (origin, direction) w.r.t. (x, y) is the inverse matrix itself
        // when going world → object, so each auxiliary ray gets the same
        // inverse-transform treatment as the primary (PBRT §6.2.3 / §10.1.1).
        Ray localRay;
        if (ray.HasDifferentials)
        {
            var d = ray.Differentials;
            var lox = Vector3.Transform(d.OriginX, _inverse);
            var ldx = Vector3.TransformNormal(d.DirectionX, _inverse);
            var loy = Vector3.Transform(d.OriginY, _inverse);
            var ldy = Vector3.TransformNormal(d.DirectionY, _inverse);
            localRay = new Ray(localOrigin, localDir, new RayDifferential(lox, ldx, loy, ldy));
        }
        else
        {
            localRay = new Ray(localOrigin, localDir);
        }

        if (!_object.Hit(localRay, tMin, tMax, ref rec))
            return false;

        // Leave it as-is — this is intentional. Procedural textures sample LocalPoint,
        // so they tile in the object's own coordinate system regardless of world transforms.
        // rec.LocalPoint = rec.LocalPoint;  // <-- purposely NOT transformed

        // Transform the hit point back to world space
        rec.Point = Vector3.Transform(rec.Point, _transform);

        // Transform the normal using the normal matrix (handles non-uniform scale).
        // We must NOT call SetFaceNormal again here: rec.Normal is already a shading
        // normal (against ray) and rec.FrontFace was set by the inner Hit(). Calling
        // SetFaceNormal a second time treats the shading normal as if it were the
        // geometric outward and recomputes FrontFace from scratch, which inverts the
        // flag on every back-face hit and on every CSG-flipped surface (where CSG
        // intentionally flips the normal so it stays co-directional with the ray).
        // Both operations preserve dot-product sign for any invertible transform —
        // (M⁻ᵀN)·(MD) = N·D — so FrontFace remains valid in world space without
        // recomputation.
        rec.Normal = Vector3.Normalize(Vector3.TransformNormal(rec.Normal, _normalMatrix));

        // Tangent and bitangent are direction vectors, they transform with the forward matrix
        rec.Tangent   = Vector3.Normalize(Vector3.TransformNormal(rec.Tangent,   _transform));
        rec.Bitangent = Vector3.Normalize(Vector3.TransformNormal(rec.Bitangent, _transform));

        // Parametric partials transform as ordinary direction vectors (they
        // span the surface tangent plane). UV partials are unaffected — the
        // primitive's parameter space is invariant to spatial transforms.
        if (rec.DpDu.LengthSquared() > 0f)
            rec.DpDu = Vector3.TransformNormal(rec.DpDu, _transform);
        if (rec.DpDv.LengthSquared() > 0f)
            rec.DpDv = Vector3.TransformNormal(rec.DpDv, _transform);

        // Normal partials (MNEE) transform with the normal matrix like Normal,
        // then are re-projected onto the tangent plane of the re-normalized
        // world normal so ∂N/∂· stays orthogonal to N (a unit normal's
        // derivative is tangent). Exact for rigid / uniform-scale transforms;
        // a first-order approximation under non-uniform scale, which only
        // affects the manifold-walk Jacobian (quasi-Newton tolerates it).
        if (rec.DnDu.LengthSquared() > 0f || rec.DnDv.LengthSquared() > 0f)
        {
            Vector3 nu = Vector3.TransformNormal(rec.DnDu, _normalMatrix);
            Vector3 nv = Vector3.TransformNormal(rec.DnDv, _normalMatrix);
            Vector3 n  = rec.Normal; // already normalized world normal
            rec.DnDu = nu - n * Vector3.Dot(n, nu);
            rec.DnDv = nv - n * Vector3.Dot(n, nv);
        }

        // Footprint dPdx/dPdy were computed in object space (LocalPoint-aligned)
        // by the inner primitive's Hit path. Procedural textures consume them
        // at LocalPoint, so we deliberately keep them in object space — image
        // textures use the UV partials which are space-independent. We do NOT
        // transform here.

        // Caustic chart recovery: the inner primitive stamped HitPrimitive with
        // its OBJECT-space chart (used by the CSG/mesh manifold seeder). Expose
        // THIS transform instead — it is the matching world-space IManifoldSurface
        // (EvaluateManifold maps the same object-space (u, v) to world). A null
        // HitPrimitive marks a flat, non-focusing region (e.g. a cylinder cap),
        // which we preserve so the seeder still skips it. Only meaningful when
        // caustics are enabled; otherwise the field is simply never read.
        if (rec.HitPrimitive != null)
            rec.HitPrimitive = this;

        return true;
    }

    // ── IManifoldSurface (MNEE) ─────────────────────────────────────────────
    // Delegate the parametric evaluation to the wrapped surface (in object
    // space) and map the result to world space: the point through the forward
    // matrix, the normal through the normal matrix (M⁻ᵀ). The (u, v) seed the
    // walker passes in came from a straight-ray Hit, which preserves the inner
    // primitive's U/V unchanged through this wrapper, so the parameterisations
    // line up exactly.
    public bool EvaluateManifold(float u, float v, out ManifoldPoint pt)
    {
        if (_object is IManifoldSurface inner && inner.EvaluateManifold(u, v, out var local))
        {
            Vector3 p = Vector3.Transform(local.P, _transform);
            Vector3 n = Vector3.Normalize(Vector3.TransformNormal(local.N, _normalMatrix));
            pt = new ManifoldPoint(p, n);
            return true;
        }
        pt = default;
        return false;
    }

    /// <summary>
    /// Builds the <see cref="IManifoldCaster"/> for this transformed geometry when
    /// it is flagged <c>caustic_caster</c>. A single-chart analytic primitive maps
    /// through this Transform (which is itself the <see cref="IManifoldSurface"/>
    /// chart), so the seeding rays still run against <c>this</c> in world space. A
    /// mesh is instead baked once into world space — its triangles become
    /// world-space charts directly, so no per-call wrapper is needed. A composite
    /// caster (a CSG solid) is wrapped in a <see cref="TransformedManifoldCaster"/>
    /// that maps the connection into object space, seeds the inner caster there,
    /// and lifts the resulting charts back to world space.
    /// </summary>
    public IManifoldCaster? CreateManifoldCaster()
    {
        // Flatten a nested Transform chain into one composed matrix + the innermost
        // payload, so the dispatch is on the TRUE geometry type. This matters for a
        // composite caster (CSG) several transforms deep — e.g. a group child with
        // its own local transform: Transform(Transform(CsgObject)). A Transform is
        // always an IManifoldSurface but can only evaluate parametrically when its
        // own inner is one (an analytic primitive); a CSG is not, so it must take
        // the object-space seeding wrapper, not the analytic single-chart path.
        Matrix4x4 m = _transform;
        IHittable payload = _object;
        while (payload is Transform t)
        {
            m = t._transform * m;      // payload-space → … → world (row-vector order)
            payload = t._object;
        }

        if (payload is Mesh mesh)
        {
            Matrix4x4 nm = Matrix4x4.Invert(m, out var mi) ? Matrix4x4.Transpose(mi) : _normalMatrix;
            return mesh.BakeWorldSpace(m, nm);
        }

        // The world-space chart / coordinate mapper for the flattened payload —
        // reuse `this` when there was no nesting to avoid an extra allocation.
        Transform composed = ReferenceEquals(payload, _object) ? this : new Transform(payload, m);

        if (payload is IManifoldSurface)       // analytic primitive
            return new Rendering.AnalyticManifoldCaster(composed, composed);
        if (payload is IManifoldCaster inner)  // composite caster (CSG)
            return new TransformedManifoldCaster(composed, inner);
        return null;
    }

    // Maps a world-space (u, v) chart point produced in this Transform's OBJECT
    // space to world space, and its normal through the normal matrix.
    private ManifoldPoint ChartToWorld(in ManifoldPoint local)
    {
        Vector3 p = Vector3.Transform(local.P, _transform);
        Vector3 n = Vector3.Normalize(Vector3.TransformNormal(local.N, _normalMatrix));
        return new ManifoldPoint(p, n);
    }

    // Maps a world-space point + normal back into this Transform's object space,
    // for the inner caster's post-convergence membership clamp (which operates in
    // object space). The normal uses (M)ᵀ = transpose of the forward matrix, the
    // inverse of the forward normal matrix (M⁻¹)ᵀ.
    private ManifoldPoint ChartToObject(in ManifoldPoint world)
    {
        Vector3 p = Vector3.Transform(world.P, _inverse);
        Vector3 n = Vector3.Normalize(Vector3.TransformNormal(world.N, Matrix4x4.Transpose(_transform)));
        return new ManifoldPoint(p, n);
    }

    /// <summary>
    /// <see cref="IManifoldCaster"/> for a composite caster (a <see cref="CsgObject"/>)
    /// wrapped in a <see cref="Transform"/>. Seeding runs in the inner caster's
    /// OBJECT space — the world endpoints are mapped through the inverse matrix —
    /// and each object-space chart is wrapped in a <see cref="TransformedChart"/>
    /// so the world-space manifold walk sees world-space points/normals while the
    /// inner per-chart membership clamp keeps running in object space.
    /// </summary>
    private sealed class TransformedManifoldCaster : IManifoldCaster
    {
        private readonly Transform _tr;
        private readonly IManifoldCaster _inner;

        public TransformedManifoldCaster(Transform tr, IManifoldCaster inner)
        {
            _tr    = tr;
            _inner = inner;
        }

        public bool SeedManifold(Vector3 x, Vector3 y, in CausticInterface ci,
                                 Span<ManifoldSeed> seeds, out int k)
        {
            Vector3 lx = Vector3.Transform(x, _tr._inverse);
            Vector3 ly = Vector3.Transform(y, _tr._inverse);
            if (!_inner.SeedManifold(lx, ly, ci, seeds, out k)) return false;
            for (int i = 0; i < k; i++)
                seeds[i] = new ManifoldSeed(new TransformedChart(_tr, seeds[i].Chart), seeds[i].Uv);
            return true;
        }
    }

    // Lifts an object-space chart to world space for the manifold walk. Forwards
    // the membership clamp (if any) by mapping the converged world vertex back to
    // object space, where the inner chart's inside-tests are defined.
    private sealed class TransformedChart : IManifoldSurface, IClampedChart
    {
        private readonly Transform _tr;
        private readonly IManifoldSurface _local;

        public TransformedChart(Transform tr, IManifoldSurface local)
        {
            _tr    = tr;
            _local = local;
        }

        public bool EvaluateManifold(float u, float v, out ManifoldPoint pt)
        {
            if (!_local.EvaluateManifold(u, v, out var lp)) { pt = default; return false; }
            pt = _tr.ChartToWorld(lp);
            return true;
        }

        public bool Accept(in ManifoldPoint pt)
            => _local is not IClampedChart cc || cc.Accept(_tr.ChartToObject(pt));
    }

    public AABB BoundingBox() => _worldBox;

    private static AABB ComputeWorldBox(AABB local, Matrix4x4 transform)
    {
        Vector3 min = local.Min;
        Vector3 max = local.Max;

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
            Vector3 tc = Vector3.Transform(c, transform);
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
    /// <remarks>
    /// Deterministic approximation: uses the average of the three axis-aligned
    /// normal scalings as a representative value for <c>|M⁻ᵀ · n̂|</c>.
    /// For a uniform scale <c>S</c> this is exact (= <c>1/S</c>), giving
    /// <c>worldArea = objArea × S³ × 1/S = objArea × S²</c>.
    /// For non-uniform scaling it is an approximation used only by the
    /// power-weighted light-importance-sampling heuristic, where accuracy is
    /// not critical.
    /// </remarks>
    public float SurfaceArea
    {
        get
        {
            if (_object is not ISamplable inner) return 0f;
            float innerArea = inner.SurfaceArea;
            // _avgNormalLen is the precomputed average |M⁻ᵀ · n̂| over the three
            // canonical axis normals — a representative of the surface-element
            // Jacobian. Computed once at construction since _normalMatrix is immutable.
            return innerArea * _absDetM * _avgNormalLen;
        }
    }

    /// <inheritdoc/>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        if (_object is not ISamplable inner)
            return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), 0f); // guard

        var (pointObj, normalObj, uvObj, areaObj) = inner.Sample();
        return TransformSample(pointObj, normalObj, uvObj, areaObj);
    }

    /// <inheritdoc/>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        if (_object is not ISamplable inner)
            return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), 0f);

        var (pointObj, normalObj, uvObj, areaObj) = inner.SampleStratified(sampleIndex, sqrtSamples);
        return TransformSample(pointObj, normalObj, uvObj, areaObj);
    }

    private (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) TransformSample(
        Vector3 pointObj, Vector3 normalObj, Vector2 uvObj, float areaObj)
    {
        // Transform sample point to world space
        Vector3 worldPoint = Vector3.Transform(pointObj, _transform);

        // Transform normal via M⁻ᵀ (correct for non-uniform scale)
        Vector3 normalRaw = Vector3.TransformNormal(normalObj, _normalMatrix);
        float normalLen = normalRaw.Length();
        if (normalLen < 1e-6f)
            return (worldPoint, normalObj, uvObj, areaObj); // degenerate transform — return unchanged

        Vector3 worldNormal = normalRaw / normalLen;

        // World-space area: areaObj × |det(M)| × |M⁻ᵀ · n̂_obj|
        // The normalLen term = |M⁻ᵀ · n̂_obj| accounts for the directional
        // change of the surface element; _absDetM accounts for volume scaling.
        float worldArea = areaObj * _absDetM * normalLen;

        // UV is in the inner primitive's texture space and is not affected
        // by the spatial transform (same as rec.LocalPoint — see Hit()).
        return (worldPoint, worldNormal, uvObj, worldArea);
    }
}
