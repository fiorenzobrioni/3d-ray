using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Hierarchical min/max acceleration pyramid over a regular height sample grid
/// (Tevs/Ihrke/Seidel 2008 — "Maximum Mipmaps for Fast, Accurate, and Scalable
/// Dynamic Height Field Rendering"). The grid is interpreted as
/// <c>(N+1) × (N+1)</c> height samples on a uniform XZ lattice; consecutive
/// samples define <c>N × N</c> bilinear patches ("cells"). For each cell at
/// level 0 the min/max of the four corner heights bounds the bilinear patch
/// exactly (a convex combination cannot exceed its corners). Higher levels
/// aggregate 2×2 children up to a 1×1 root.
///
/// Ray traversal is a depth-first quadtree walk near→far: at every cell visit
/// the ray is slab-tested against the cell's XZ extent and the per-cell
/// <c>[minH, maxH]</c> Y range; misses prune entire subtrees, hits descend.
/// At level 0 the caller bisects the bilinear patch with the heightmap
/// sampler.
/// </summary>
public sealed class MinMaxMipmap
{
    private readonly float[][] _min;
    private readonly float[][] _max;
    private readonly int[] _size;
    private readonly int _levels;

    public int CellsX { get; }
    public int CellsZ { get; }
    public float XMin { get; }
    public float ZMin { get; }
    public float XMax { get; }
    public float ZMax { get; }

    private readonly float _cellSizeX;
    private readonly float _cellSizeZ;

    /// <summary>
    /// Builds the pyramid from a <c>(samplesX × samplesZ)</c> grid of heights.
    /// <paramref name="samples"/> is indexed <c>samples[i + j*samplesX]</c>
    /// where <c>i ∈ [0, samplesX)</c> spans X and <c>j ∈ [0, samplesZ)</c>
    /// spans Z. The XZ bounds describe the world-space rectangle covered by
    /// the corner samples (sample (0,0) sits at (XMin, ZMin); sample
    /// (samplesX-1, samplesZ-1) sits at (XMax, ZMax)).
    /// </summary>
    public MinMaxMipmap(float[] samples, int samplesX, int samplesZ,
                        float xMin, float zMin, float xMax, float zMax)
    {
        XMin = xMin; ZMin = zMin; XMax = xMax; ZMax = zMax;
        CellsX = samplesX - 1;
        CellsZ = samplesZ - 1;
        _cellSizeX = (xMax - xMin) / CellsX;
        _cellSizeZ = (zMax - zMin) / CellsZ;

        // ── Level 0: per-cell min/max of the four corner samples ────────
        int level0Cells = CellsX * CellsZ;
        var l0Min = new float[level0Cells];
        var l0Max = new float[level0Cells];
        for (int j = 0; j < CellsZ; j++)
        {
            for (int i = 0; i < CellsX; i++)
            {
                float h00 = samples[i     + j     * samplesX];
                float h10 = samples[i + 1 + j     * samplesX];
                float h01 = samples[i     + (j+1) * samplesX];
                float h11 = samples[i + 1 + (j+1) * samplesX];
                float mn = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11));
                float mx = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
                int idx = i + j * CellsX;
                l0Min[idx] = mn;
                l0Max[idx] = mx;
            }
        }

        // ── Higher levels: aggregate 2×2 children, stop at 1×1 ──────────
        int maxDim = Math.Max(CellsX, CellsZ);
        int levels = 1 + (int)MathF.Ceiling(MathF.Log2(MathF.Max(maxDim, 1)));
        _min = new float[levels][];
        _max = new float[levels][];
        _size = new int[levels];
        _min[0] = l0Min; _max[0] = l0Max; _size[0] = CellsX;
        // _size[k] is intentionally always the same as CellsX for level 0 only.
        // For higher levels we need per-level widths; store them in a parallel
        // pair of arrays so traversal can index correctly.
        var widths = new int[levels];
        var heights = new int[levels];
        widths[0] = CellsX; heights[0] = CellsZ;

        for (int k = 1; k < levels; k++)
        {
            int wPrev = widths[k - 1];
            int hPrev = heights[k - 1];
            int wCur = (wPrev + 1) / 2;
            int hCur = (hPrev + 1) / 2;
            var mn = new float[wCur * hCur];
            var mx = new float[wCur * hCur];
            var prevMin = _min[k - 1];
            var prevMax = _max[k - 1];
            for (int j = 0; j < hCur; j++)
            {
                for (int i = 0; i < wCur; i++)
                {
                    int i0 = i * 2;
                    int j0 = j * 2;
                    int i1 = Math.Min(i0 + 1, wPrev - 1);
                    int j1 = Math.Min(j0 + 1, hPrev - 1);
                    float a = prevMin[i0 + j0 * wPrev];
                    float b = prevMin[i1 + j0 * wPrev];
                    float c = prevMin[i0 + j1 * wPrev];
                    float d = prevMin[i1 + j1 * wPrev];
                    float A = prevMax[i0 + j0 * wPrev];
                    float B = prevMax[i1 + j0 * wPrev];
                    float C = prevMax[i0 + j1 * wPrev];
                    float D = prevMax[i1 + j1 * wPrev];
                    mn[i + j * wCur] = MathF.Min(MathF.Min(a, b), MathF.Min(c, d));
                    mx[i + j * wCur] = MathF.Max(MathF.Max(A, B), MathF.Max(C, D));
                }
            }
            _min[k] = mn; _max[k] = mx;
            widths[k] = wCur; heights[k] = hCur;
        }
        _widths = widths;
        _heights = heights;
        _levels = levels;
    }

    private readonly int[] _widths;
    private readonly int[] _heights;

    public int Levels => _levels;
    public int Width(int level) => _widths[level];
    public int Height(int level) => _heights[level];
    public float MinAt(int level, int i, int j) => _min[level][i + j * _widths[level]];
    public float MaxAt(int level, int i, int j) => _max[level][i + j * _widths[level]];

    /// <summary>
    /// Hierarchical near→far traversal. For every level-0 cell whose XZ extent
    /// and per-cell <c>[minH, maxH]</c> envelope are pierced by the ray within
    /// <c>[tMin, tMax]</c>, invokes <paramref name="onLeaf"/>. The callback
    /// returns <c>true</c> when it accepted a hit (and the new tMax it wants
    /// to enforce), <c>false</c> to keep searching. Cells are visited in
    /// front-to-back order so the first accepted hit is also the closest.
    /// </summary>
    public bool TraverseRay<TVisitor>(
        Ray ray, float tMin, float tMax,
        ref TVisitor onLeaf,
        ref float tMaxOut)
        where TVisitor : struct, ILeafVisitor
    {
        bool hit = false;
        float curTMax = tMax;
        VisitNode(_levels - 1, 0, 0, ray, tMin, ref curTMax, ref hit, ref onLeaf);
        tMaxOut = curTMax;
        return hit;
    }

    /// <summary>
    /// Leaf-cell callback. <see cref="Visit"/> returns true and updates
    /// <c>newTMax</c> when the leaf is accepted as a hit; the traversal then
    /// keeps <c>newTMax</c> as the new far bound so subsequent farther cells
    /// are pruned by the hierarchical slab test automatically.
    ///
    /// Implemented as a <c>struct</c> visitor (rather than a <c>delegate</c>)
    /// so that <see cref="HeightField.Hit"/> incurs no per-ray closure
    /// allocation — the visitor's mutable hit state is threaded by ref through
    /// the recursion.
    /// </summary>
    public interface ILeafVisitor
    {
        bool Visit(int cellX, int cellZ, float cellTEnter, float cellTExit, out float newTMax);
    }

    private void VisitNode<TVisitor>(int level, int i, int j,
                           Ray ray, float tMin, ref float tMax,
                           ref bool hit, ref TVisitor onLeaf)
        where TVisitor : struct, ILeafVisitor
    {
        if (i >= _widths[level] || j >= _heights[level]) return;

        // Cell footprint at this level — clamped at the heightmap boundary
        // so the slab test stays exact (a level-2 cell on the edge of a
        // 7×7 grid may cover only 3 of the 4 children's worth of area).
        int childW = 1 << level;
        int childH = 1 << level;
        int iLeaf = i * childW;
        int jLeaf = j * childH;
        int iLeafEnd = Math.Min(iLeaf + childW, CellsX);
        int jLeafEnd = Math.Min(jLeaf + childH, CellsZ);
        if (iLeaf >= CellsX || jLeaf >= CellsZ) return;

        float xLo = XMin + iLeaf * _cellSizeX;
        float xHi = XMin + iLeafEnd * _cellSizeX;
        float zLo = ZMin + jLeaf * _cellSizeZ;
        float zHi = ZMin + jLeafEnd * _cellSizeZ;

        float minH = _min[level][i + j * _widths[level]];
        float maxH = _max[level][i + j * _widths[level]];

        // Inflate Y slightly to absorb the bisection epsilon.
        const float eps = 1e-3f;
        var box = new AABB(
            new Vector3(xLo, minH - eps, zLo),
            new Vector3(xHi, maxH + eps, zHi));
        if (!ComputeSlabInterval(box, ray, tMin, tMax, out float tEnter, out float tExit))
            return;

        if (level == 0)
        {
            if (onLeaf.Visit(iLeaf, jLeaf, tEnter, tExit, out float newTMax))
            {
                hit = true;
                if (newTMax < tMax) tMax = newTMax;
            }
            return;
        }

        // ── Descend into the 4 children, front-to-back ──────────────────
        // Order by the t at which the ray crosses each child's centre plane
        // on the dominant horizontal axis. A simpler, equally correct ordering
        // is: pick the child closer to the ray origin along the ray direction.
        Span<(int ci, int cj, float t)> children = stackalloc (int, int, float)[4];
        int n = 0;
        for (int dj = 0; dj < 2; dj++)
        {
            for (int di = 0; di < 2; di++)
            {
                int ci = i * 2 + di;
                int cj = j * 2 + dj;
                if (ci >= _widths[level - 1] || cj >= _heights[level - 1]) continue;
                int subChildW = 1 << (level - 1);
                int subChildH = 1 << (level - 1);
                int ciLeaf = ci * subChildW;
                int cjLeaf = cj * subChildH;
                int ciLeafEnd = Math.Min(ciLeaf + subChildW, CellsX);
                int cjLeafEnd = Math.Min(cjLeaf + subChildH, CellsZ);
                if (ciLeaf >= CellsX || cjLeaf >= CellsZ) continue;
                float cxLo = XMin + ciLeaf * _cellSizeX;
                float cxHi = XMin + ciLeafEnd * _cellSizeX;
                float czLo = ZMin + cjLeaf * _cellSizeZ;
                float czHi = ZMin + cjLeafEnd * _cellSizeZ;
                float cMin = _min[level - 1][ci + cj * _widths[level - 1]];
                float cMax = _max[level - 1][ci + cj * _widths[level - 1]];
                var cBox = new AABB(
                    new Vector3(cxLo, cMin - eps, czLo),
                    new Vector3(cxHi, cMax + eps, czHi));
                if (ComputeSlabInterval(cBox, ray, tMin, tMax, out float ce, out float _))
                {
                    children[n++] = (ci, cj, ce);
                }
            }
        }
        // Tiny insertion sort by t (at most 4 entries).
        for (int a = 1; a < n; a++)
        {
            var key = children[a];
            int b = a - 1;
            while (b >= 0 && children[b].t > key.t) { children[b + 1] = children[b]; b--; }
            children[b + 1] = key;
        }
        for (int a = 0; a < n; a++)
        {
            VisitNode(level - 1, children[a].ci, children[a].cj,
                      ray, tMin, ref tMax, ref hit, ref onLeaf);
        }
    }

    private static bool ComputeSlabInterval(in AABB box, Ray ray, float tMin, float tMax,
                                            out float tEnter, out float tExit)
    {
        Vector3 t0 = (box.Min - ray.Origin) * ray.InvDirection;
        Vector3 t1 = (box.Max - ray.Origin) * ray.InvDirection;
        Vector3 tSmall = Vector3.Min(t0, t1);
        Vector3 tLarge = Vector3.Max(t0, t1);
        tEnter = MathF.Max(tMin, MathF.Max(tSmall.X, MathF.Max(tSmall.Y, tSmall.Z)));
        tExit  = MathF.Min(tMax, MathF.Min(tLarge.X, MathF.Min(tLarge.Y, tLarge.Z)));
        return tExit > tEnter;
    }
}
