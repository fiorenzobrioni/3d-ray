using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;
using RayTracer.Materials;

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

    // Precomputed for ISamplable: cumulative area distribution
    private readonly float[] _cumulativeAreas;
    private readonly float _totalArea;

    /// <summary>Number of triangular faces in the mesh.</summary>
    public int FaceCount { get; }

    /// <summary>Number of unique vertices used (informational).</summary>
    public int VertexCount { get; }

    public IMaterial Material { get; }

    /// <summary>
    /// Constructs a Mesh from a list of triangles and builds the internal BVH.
    /// </summary>
    /// <param name="triangles">The triangles (SmoothTriangle or Triangle) forming the mesh.</param>
    /// <param name="material">Shared material for the entire mesh.</param>
    /// <param name="vertexCount">Number of unique vertices (for stats reporting).</param>
    public Mesh(List<IHittable> triangles, IMaterial material, int vertexCount = 0)
    {
        _triangles = triangles;
        Material = material;
        FaceCount = triangles.Count;
        VertexCount = vertexCount;

        // Build internal BVH
        _bvh = triangles.Count > 2
            ? new BvhNode(new List<IHittable>(triangles))
            : new HittableList(triangles);

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

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        return _bvh.Hit(ray, tMin, tMax, ref rec);
    }

    public (Vector3 Point, Vector3 Normal, float Area) Sample()
    {
        if (_triangles.Count == 0)
            return (Vector3.Zero, Vector3.UnitY, 0f);

        // Area-weighted face selection via binary search on cumulative areas
        float target = MathUtils.RandomFloat() * _totalArea;
        int idx = Array.BinarySearch(_cumulativeAreas, target);
        if (idx < 0) idx = ~idx; // BinarySearch returns bitwise complement of insertion point
        idx = Math.Clamp(idx, 0, _triangles.Count - 1);

        if (_triangles[idx] is ISamplable samplable)
        {
            var (point, normal, _) = samplable.Sample();
            return (point, normal, _totalArea);
        }

        // Fallback for non-ISamplable triangles (shouldn't happen)
        return (Vector3.Zero, Vector3.UnitY, _totalArea);
    }

    public int Seed
    {
        get => 0;
        set
        {
            // Propagate seed to all triangles
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
}
