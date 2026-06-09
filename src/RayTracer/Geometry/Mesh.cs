using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Geometry;

/// <summary>
/// A triangle mesh with an internal BVH for O(log N) ray intersection.
///
/// The mesh owns a list of <see cref="SmoothTriangle"/> (or flat <see cref="Triangle"/>)
/// and wraps them in a <see cref="BvhNode"/> built at construction time.
/// From the outside it behaves like a single <see cref="IHittable"/> — the scene's
/// top-level BVH sees it as one leaf node with a single AABB, and the internal
/// BVH handles efficient traversal of the mesh's faces.
///
/// <b>ISamplable:</b>
/// Implements area-weighted random sampling across all faces for NEE support.
/// Each face's contribution to the total area is precomputed at construction.
///
/// <b>Material:</b>
/// All triangles share the mesh-level material. Per-face or per-group materials
/// (MTL) can be added in a future extension by storing material references
/// per triangle.
///
/// <b>Statistics:</b>
/// After construction, <see cref="FaceCount"/> and <see cref="VertexCount"/>
/// report the mesh complexity. These are logged by SceneLoader for the user.
/// </summary>
public class Mesh : IHittable, ISamplable
{
    private readonly IHittable _bvh;
    private readonly List<IHittable> _triangles;
    private int _seed;

    // Precomputed for ISamplable: cumulative area distribution
    private readonly float[] _cumulativeAreas;
    private readonly float _totalArea;

    /// <summary>Number of triangular faces in the mesh.</summary>
    public int FaceCount { get; }

    /// <summary>Number of unique vertices used (informational).</summary>
    public int VertexCount { get; }

    public IMaterial Material { get; }

    /// <summary>
    /// Optional residual bump map derived from the mesh's
    /// <c>displacement.texture</c> when the entity opted in via
    /// <c>displacement.autobump: true</c> (DEVLOG surface-displacement
    /// step 5). Pushed into <see cref="HitRecord.AutoBump"/> on every
    /// successful primary hit so the renderer can perturb the shading
    /// normal independently of (and on top of) the material-level
    /// <see cref="IMaterial.BumpMap"/>. Living on the mesh — not the
    /// material — preserves material sharing across entities: only the
    /// meshes that asked for an autobump get one. Mirrors Arnold's
    /// <c>autobump_visibility</c> flag on <c>polymesh</c>.
    /// </summary>
    public IBumpMap? AutoBump { get; set; }

    /// <summary>
    /// Constructs a Mesh from a list of triangles and builds the internal BVH.
    /// </summary>
    /// <param name="triangles">The triangles (SmoothTriangle or Triangle) forming the mesh.</param>
    /// <param name="material">Shared material for the entire mesh.</param>
    /// <param name="vertexCount">Number of unique vertices (for stats reporting).</param>
    /// <param name="leafBoundsInflation">
    ///   Optional positive padding added to every BVH leaf AABB at build time.
    ///   Used by the scalar-displacement pipeline (<c>displacement_bound</c>)
    ///   to mirror Arnold/RenderMan's <c>disp_padding</c>/<c>dispBound</c>
    ///   safety margin. 0 (default) is the legacy no-padding path.
    /// </param>
    public Mesh(List<IHittable> triangles, IMaterial material, int vertexCount = 0,
                float leafBoundsInflation = 0f)
    {
        _triangles = triangles;
        Material = material;
        FaceCount = triangles.Count;
        VertexCount = vertexCount;

        // Build internal BVH. When the caller asks for leaf-AABB inflation
        // (scalar displacement's safety margin) we wrap each triangle in a
        // BoundsInflatedHittable so the BVH builder sees the padded box.
        var bvhInput = leafBoundsInflation > 0f
            ? WrapInflated(triangles, leafBoundsInflation)
            : new List<IHittable>(triangles);

        // BVH threshold = 4, matching Group and SceneLoader (CLAUDE.md invariant).
        // Below that a BvhNode's root fat-leaf would hold every triangle anyway,
        // so a flat HittableList is cheaper and has identical behaviour.
        const int BvhThreshold = 4;
        _bvh = triangles.Count > BvhThreshold
            ? new BvhNode(bvhInput)
            : new HittableList(bvhInput);

        // Precompute cumulative area distribution for ISamplable
        _cumulativeAreas = new float[triangles.Count];
        float sum = 0;
        for (int i = 0; i < triangles.Count; i++)
        {
            float area = ComputeTriangleArea(triangles[i]);
            sum += area;
            _cumulativeAreas[i] = sum;
        }
        _totalArea = sum;
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        bool hit = _bvh.Hit(ray, tMin, tMax, ref rec);
        if (hit && AutoBump != null)
            rec.AutoBump = AutoBump;
        return hit;
    }

    /// <inheritdoc/>
    public float SurfaceArea => _totalArea;

    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample()
    {
        if (_triangles.Count == 0)
            return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), 0f);

        // Area-weighted face selection via binary search on cumulative areas
        float target = MathUtils.RandomFloat() * _totalArea;
        int idx = PickTriangleByCdf(target);

        if (_triangles[idx] is ISamplable samplable)
        {
            var (point, normal, uv, _) = samplable.Sample();
            return (point, normal, uv, _totalArea);
        }

        // Fallback for non-ISamplable triangles (shouldn't happen)
        return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), _totalArea);
    }

    /// <summary>
    /// Stratified version: splits the area-weighted CDF into <c>sqrtSamples²</c>
    /// equal buckets and picks the triangle whose cumulative area contains the
    /// bucket centre (jittered). Inside that triangle the call is delegated to
    /// its own stratified sampler — for now each sample maps to cell 0 of a
    /// 1×1 internal grid (i.e. one jittered sample per bucket), which is the
    /// textbook "hierarchical stratification" pattern for composite surfaces.
    /// Compared with pure random sampling this evenly covers the whole mesh
    /// surface and dramatically reduces NEE variance for large or fragmented
    /// emissive meshes.
    /// </summary>
    public (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
    {
        if (_triangles.Count == 0)
            return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), 0f);

        int totalStrata = Math.Max(1, sqrtSamples * sqrtSamples);
        float stratum = 1f / totalStrata;
        float jittered = (sampleIndex + MathUtils.RandomFloat()) * stratum;
        float target = Math.Clamp(jittered, 0f, 0.9999999f) * _totalArea;
        int idx = PickTriangleByCdf(target);

        if (_triangles[idx] is ISamplable samplable)
        {
            // Inside the selected face use a single random sample — the outer
            // stratification already guarantees even coverage across faces.
            var (point, normal, uv, _) = samplable.Sample();
            return (point, normal, uv, _totalArea);
        }

        return (Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f), _totalArea);
    }

    private int PickTriangleByCdf(float target)
    {
        int idx = Array.BinarySearch(_cumulativeAreas, target);
        if (idx < 0) idx = ~idx; // BinarySearch returns bitwise complement of insertion point
        return Math.Clamp(idx, 0, _triangles.Count - 1);
    }

    public int Seed
    {
        get => _seed;
        set
        {
            // A Mesh is one logical object: every triangle shares the same seed
            // so procedural textures appear uniform across the whole surface.
            _seed = value;
            foreach (var tri in _triangles)
                tri.Seed = value;
        }
    }

    public AABB BoundingBox() => _bvh.BoundingBox();

    private static float ComputeTriangleArea(IHittable tri)
    {
        return tri switch
        {
            SmoothTriangle st => 0.5f * Vector3.Cross(st.V1 - st.V0, st.V2 - st.V0).Length(),
            Triangle t => 0.5f * Vector3.Cross(t.V1 - t.V0, t.V2 - t.V0).Length(),
            _ => 1f // Fallback
        };
    }

    private static List<IHittable> WrapInflated(List<IHittable> triangles, float padding)
    {
        var output = new List<IHittable>(triangles.Count);
        foreach (var t in triangles)
            output.Add(new BoundsInflatedHittable(t, padding));
        return output;
    }
}
