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
public class Mesh : IHittable, ISamplable, IManifoldCaster, INeighborSeedCaster
{
    private readonly IHittable _bvh;
    private readonly List<IHittable> _triangles;
    private int _seed;

    // Caustic edge-crossing (neighbor-seed tier): facet adjacency, built once
    // when the mesh is registered as a caustic caster. _facetIndex maps a
    // triangle (the chart a seed lives on) to its slot; _facetNeighbors[slot]
    // lists the edge-adjacent triangle slots. Null until PrepareCausticAdjacency.
    private Dictionary<IHittable, int>? _facetIndex;
    private int[][]? _facetNeighbors;

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

        _bvh = triangles.Count > 2
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

    public bool Hit(Ray ray, float tMin, float tMax, ref HitRecord rec)
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

    // ── IManifoldCaster (caustic seeding) ────────────────────────────────────

    /// <summary>
    /// True when the mesh carries per-vertex normals (its triangles are
    /// <see cref="SmoothTriangle"/>): only smooth meshes have the across-facet
    /// normal variation that focuses light, so the loader registers only those
    /// as caustic casters. Flat-faceted meshes are skipped with a warning.
    /// </summary>
    public bool HasVertexNormals => _triangles.Count > 0 && _triangles[0] is SmoothTriangle;

    public bool SeedManifold(Vector3 x, Vector3 y, in CausticInterface ci,
                             Span<ManifoldSeed> seeds, out int k)
    {
        k = 0;
        if (ci.IsTransmissive)
        {
            // Ray-cast the straight segment through the internal BVH; each hit
            // triangle is a per-vertex chart (recovered from rec.HitPrimitive).
            Vector3 d = y - x;
            float len = d.Length();
            if (len < 1e-9f) return false;
            Vector3 dir = d / len;
            float tStart = 1e-4f;
            while (k < seeds.Length)
            {
                var rec = new HitRecord();
                if (!Hit(new Ray(x, dir), tStart, len - 1e-4f, ref rec)) break;
                if (TryMakeSeed(in rec, out ManifoldSeed seed)) seeds[k++] = seed;
                tStart = rec.T + 1e-3f;
            }
            return k >= 1;
        }

        // Reflection: the straight ray misses the mirror, so scan the faces for
        // the triangle centroid whose normal best bisects x and y.
        if (!SeedReflectionMesh(x, y, out ManifoldSeed rseed)) return false;
        seeds[0] = rseed;
        k = 1;
        return true;
    }

    private static bool TryMakeSeed(in HitRecord rec, out ManifoldSeed seed)
    {
        switch (rec.HitPrimitive)
        {
            case SmoothTriangle st:
                st.Barycentric(rec.Point, out float su, out float sv);
                seed = new ManifoldSeed(st, new Vector2(su, sv));
                return true;
            case Triangle t:
                t.Barycentric(rec.Point, out float fu, out float fv);
                seed = new ManifoldSeed(t, new Vector2(fu, fv));
                return true;
            default:
                seed = default;
                return false;
        }
    }

    // ── INeighborSeedCaster (caustic edge-crossing, neighbor-seed tier) ──────

    /// <summary>
    /// Builds the facet adjacency used by the caustic neighbor-seed retry. Called
    /// once (single-threaded) by <see cref="Rendering.CausticCasterRegistry"/> when
    /// this mesh is registered as a caster, so <see cref="FacetNeighbors"/> can run
    /// lock-free on the parallel render path. Idempotent.
    /// </summary>
    public void PrepareCausticAdjacency()
    {
        if (_facetNeighbors != null) return;

        int n = _triangles.Count;
        var index = new Dictionary<IHittable, int>(n);
        for (int i = 0; i < n; i++) index[_triangles[i]] = i;

        // Weld vertices by quantized position so triangles that share an edge are
        // detected even when their corner positions are distinct float instances.
        var vid = new Dictionary<(long, long, long), int>();
        int VidOf(Vector3 p)
        {
            const float q = 1e5f; // 1e-5 world-unit weld grid
            var key = ((long)MathF.Round(p.X * q), (long)MathF.Round(p.Y * q), (long)MathF.Round(p.Z * q));
            if (!vid.TryGetValue(key, out int id)) { id = vid.Count; vid[key] = id; }
            return id;
        }

        // edge (sorted vid pair) → triangles sharing it.
        var edgeTris = new Dictionary<(int, int), List<int>>();
        var triVids = new (int, int, int)[n];
        for (int i = 0; i < n; i++)
        {
            if (!TriangleVertices(_triangles[i], out Vector3 a, out Vector3 b, out Vector3 c)) continue;
            int va = VidOf(a), vb = VidOf(b), vc = VidOf(c);
            triVids[i] = (va, vb, vc);
            AddEdge(edgeTris, va, vb, i);
            AddEdge(edgeTris, vb, vc, i);
            AddEdge(edgeTris, vc, va, i);
        }

        var neigh = new int[n][];
        var acc = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            acc.Clear();
            var (va, vb, vc) = triVids[i];
            CollectNeighbors(edgeTris, va, vb, i, acc);
            CollectNeighbors(edgeTris, vb, vc, i, acc);
            CollectNeighbors(edgeTris, vc, va, i, acc);
            neigh[i] = acc.Count == 0 ? Array.Empty<int>() : new List<int>(acc).ToArray();
        }

        _facetIndex = index;
        _facetNeighbors = neigh;
    }

    private static void AddEdge(Dictionary<(int, int), List<int>> map, int p, int q, int tri)
    {
        var key = p < q ? (p, q) : (q, p);
        if (!map.TryGetValue(key, out var list)) { list = new List<int>(2); map[key] = list; }
        list.Add(tri);
    }

    private static void CollectNeighbors(Dictionary<(int, int), List<int>> map, int p, int q, int self, HashSet<int> acc)
    {
        var key = p < q ? (p, q) : (q, p);
        if (!map.TryGetValue(key, out var list)) return;
        foreach (int t in list) if (t != self) acc.Add(t);
    }

    private static bool TriangleVertices(IHittable tri, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        switch (tri)
        {
            case SmoothTriangle s: a = s.V0; b = s.V1; c = s.V2; return true;
            case Triangle t:       a = t.V0; b = t.V1; c = t.V2; return true;
            default:               a = b = c = default;          return false;
        }
    }

    /// <inheritdoc/>
    public int FacetNeighbors(in ManifoldSeed seed, Span<ManifoldSeed> neighbors)
    {
        if (_facetNeighbors == null || _facetIndex == null) return 0;
        if (seed.Chart is not IHittable chart) return 0;
        if (!_facetIndex.TryGetValue(chart, out int idx)) return 0;

        // Re-seed each edge-neighbour at its centroid (the Newton walk slides it to
        // the true vertex from there); the neighbour's own clamp then accepts or
        // rejects it. Barycentric centroid is (1/3, 1/3) in the Möller–Trumbore
        // parameterisation EvaluateManifold expects.
        var centroid = new Vector2(1f / 3f, 1f / 3f);
        int[] ns = _facetNeighbors[idx];
        int count = 0;
        for (int i = 0; i < ns.Length && count < neighbors.Length; i++)
            if (_triangles[ns[i]] is IManifoldSurface surf)
                neighbors[count++] = new ManifoldSeed(surf, centroid);
        return count;
    }

    private bool SeedReflectionMesh(Vector3 x, Vector3 y, out ManifoldSeed seed)
    {
        seed = default;
        float best = float.MaxValue;
        bool found = false;
        const float third = 1f / 3f;
        for (int i = 0; i < _triangles.Count; i++)
        {
            if (_triangles[i] is not IManifoldSurface surf) continue;
            if (!surf.EvaluateManifold(third, third, out var pt)) continue;
            Vector3 wa = Vector3.Normalize(x - pt.P);
            Vector3 wb = Vector3.Normalize(y - pt.P);
            if (Vector3.Dot(wa, pt.N) <= 0f || Vector3.Dot(wb, pt.N) <= 0f) continue;
            Vector3 h = Vector3.Normalize(wa + wb);
            float resid = 1f - MathF.Abs(Vector3.Dot(h, pt.N));
            if (resid < best) { best = resid; seed = new ManifoldSeed(surf, new Vector2(third, third)); found = true; }
        }
        return found;
    }

    /// <summary>
    /// Returns a copy of this mesh with every vertex/normal mapped to world space
    /// by <paramref name="m"/> / <paramref name="normalMatrix"/>, so the baked
    /// triangles are world-space charts a <see cref="Transform"/>-wrapped mesh can
    /// seed against directly (no per-call wrapper). Done once at registration.
    /// </summary>
    public IManifoldCaster BakeWorldSpace(Matrix4x4 m, Matrix4x4 normalMatrix)
    {
        var baked = new List<IHittable>(_triangles.Count);
        foreach (var tri in _triangles)
        {
            switch (tri)
            {
                case SmoothTriangle st:
                    baked.Add(new SmoothTriangle(
                        Vector3.Transform(st.V0, m), Vector3.Transform(st.V1, m), Vector3.Transform(st.V2, m),
                        Vector3.TransformNormal(st.N0, normalMatrix),
                        Vector3.TransformNormal(st.N1, normalMatrix),
                        Vector3.TransformNormal(st.N2, normalMatrix),
                        st.UV0, st.UV1, st.UV2, st.Material));
                    break;
                case Triangle t:
                    baked.Add(new Triangle(
                        Vector3.Transform(t.V0, m), Vector3.Transform(t.V1, m), Vector3.Transform(t.V2, m), t.Material));
                    break;
            }
        }
        return new Mesh(baked, Material, VertexCount);
    }

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
