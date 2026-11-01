using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Acceleration;

/// <summary>
/// BVH of a triangle mesh (PIANO-REVIEW.md Phase 11). The vertices are copied
/// into one flat array in leaf order, so a leaf's triangles sit next to each
/// other in memory; the ray's watertight shear (<see cref="TriangleIntersection.Shear"/>)
/// is computed once per ray instead of once per triangle; the traversal
/// finds only the distance of the closest triangle and the hit record is
/// filled once, by that triangle, at the end. Every result is the one the
/// triangles' own <c>Hit</c> gives: the same intersection arithmetic on the
/// same values.
/// </summary>
[SkipLocalsInit]
public sealed class TriangleBvh : IHittable
{
    private readonly BvhFlatNode[] _nodes;
    private readonly Vector3[] _vertices; // 3 per triangle, leaf order
    private readonly IHittable[] _triangles; // leaf order
    private readonly AABB _box;

    public int Seed { get; set; }

    /// <param name="triangles"><see cref="Triangle"/>s and <see cref="SmoothTriangle"/>s.</param>
    /// <param name="padding">Grows every triangle's box by this much (the
    /// displacement bound of a displaced mesh).</param>
    public TriangleBvh(IReadOnlyList<IHittable> triangles, float padding = 0f)
    {
        int n = triangles.Count;
        var verts = new Vector3[3 * n];
        var boxes = new AABB[n];
        var pad = new Vector3(MathF.Max(0f, padding));
        for (int i = 0; i < n; i++)
        {
            var (a, b, c) = Vertices(triangles[i]);
            verts[3 * i] = a;
            verts[3 * i + 1] = b;
            verts[3 * i + 2] = c;
            boxes[i] = new AABB(Vector3.Min(Vector3.Min(a, b), c) - pad, Vector3.Max(Vector3.Max(a, b), c) + pad);
        }
        (_nodes, int[] order) = BvhBuilder.Build(boxes);
        _vertices = new Vector3[3 * order.Length];
        _triangles = new IHittable[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            int o = order[i];
            _triangles[i] = triangles[o];
            _vertices[3 * i] = verts[3 * o];
            _vertices[3 * i + 1] = verts[3 * o + 1];
            _vertices[3 * i + 2] = verts[3 * o + 2];
        }
        _box = n > 0 ? new AABB(_nodes[0].Min, _nodes[0].Max) : AABB.Empty;
    }

    /// <summary>True when every element is a triangle this BVH can hold.</summary>
    public static bool Accepts(IReadOnlyList<IHittable> items)
    {
        foreach (var t in items)
            if (t is not (Triangle or SmoothTriangle)) return false;
        return true;
    }

    private static (Vector3, Vector3, Vector3) Vertices(IHittable t) => t switch
    {
        SmoothTriangle s => (s.V0, s.V1, s.V2),
        Triangle f => (f.V0, f.V1, f.V2),
        _ => throw new ArgumentException($"TriangleBvh holds triangles only, not {t.GetType().Name}."),
    };

    public int NodeCount => _nodes.Length;


    public AABB BoundingBox() => _box;

    public double SahCost() => _triangles.Length > 0 ? BvhBuilder.SahCost(_nodes) : double.NaN;

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        int best = Closest(ray, tMin, tMax);
        // The winner fills the record: the same test again, so the same hit.
        return best >= 0 && _triangles[best].Hit(ray, tMin, tMax, ref rec);
    }

    /// <summary>Index (leaf order) of the closest triangle hit in (tMin, tMax], or −1.</summary>
    private int Closest(in Ray ray, float tMin, float tMax)
    {
        if (_triangles.Length == 0) return -1;
        var r = new BvhRay(ray);
        var nodes = _nodes;
        if (!r.Hit(nodes[0], tMin, tMax, out _)) return -1;
        var shear = new TriangleIntersection.Shear(ray);
        if (!shear.Valid) return -1;
        var v = _vertices;

        Span<int> stack = stackalloc int[BvhBuilder.MaxStack];
        Span<float> enter = stackalloc float[BvhBuilder.MaxStack];
        int sp = 0, node = 0, best = -1;
        float closest = tMax;
        while (true)
        {
            ref readonly var n = ref nodes[node];
            if (n.IsLeaf)
            {
                int end = n.Offset + n.Count;
                for (int i = n.Offset; i < end; i++)
                    if (TriangleIntersection.Intersect(shear, v[3 * i], v[3 * i + 1], v[3 * i + 2], tMin, closest,
                                                       out float t, out _, out _))
                    {
                        best = i;
                        closest = t;
                    }
            }
            else
            {
                int c = n.Offset;
                bool h0 = r.Hit(nodes[c], tMin, closest, out float t0);
                bool h1 = r.Hit(nodes[c + 1], tMin, closest, out float t1);
                if (h0 && h1)
                {
                    if (t1 < t0) { stack[sp] = c; enter[sp++] = t0; node = c + 1; }
                    else { stack[sp] = c + 1; enter[sp++] = t1; node = c; }
                    continue;
                }
                if (h0) { node = c; continue; }
                if (h1) { node = c + 1; continue; }
            }
            do
            {
                if (sp == 0) return best;
                node = stack[--sp];
            } while (!BvhRay.StillOpen(enter[sp], closest));
        }
    }
}
