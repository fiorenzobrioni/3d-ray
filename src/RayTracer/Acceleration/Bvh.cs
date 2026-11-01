using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Acceleration;

/// <summary>
/// Bounding volume hierarchy over arbitrary <see cref="IHittable"/>s: the
/// scene's top level, groups, and the light search of the MIS table. Built by
/// <see cref="BvhBuilder"/> (binned SAH) into a flat array of 32-byte nodes and
/// traversed without recursion: an explicit stack of node indices on the
/// thread's stack (<c>stackalloc</c>, so nested hierarchies such as a group
/// inside a transform inside the scene each keep their own), both children
/// tested together and the nearer visited first, the farther one skipped on
/// the way back if a closer hit was found meanwhile (PIANO-REVIEW.md Phase 11).
/// The input list is not modified.
/// </summary>
[SkipLocalsInit]
public sealed class Bvh : IHittable
{
    private readonly BvhFlatNode[] _nodes;
    private readonly IHittable[] _primitives; // in leaf order
    private readonly AABB _box;

    public int Seed { get; set; }

    public Bvh(IReadOnlyList<IHittable> objects)
    {
        var boxes = new AABB[objects.Count];
        for (int i = 0; i < boxes.Length; i++) boxes[i] = objects[i].BoundingBox();
        (_nodes, int[] order) = BvhBuilder.Build(boxes);
        _primitives = new IHittable[order.Length];
        for (int i = 0; i < order.Length; i++) _primitives[i] = objects[order[i]];
        _box = _primitives.Length > 0 ? new AABB(_nodes[0].Min, _nodes[0].Max) : AABB.Empty;
    }

    /// <summary>Number of nodes (a measure of the tree, for tests).</summary>
    public int NodeCount => _nodes.Length;

    public AABB BoundingBox() => _box;

    /// <summary>The tree's SAH cost (<see cref="BvhBuilder.SahCost"/>).</summary>
    public double SahCost() => _primitives.Length > 0 ? BvhBuilder.SahCost(_nodes) : double.NaN;

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        if (_primitives.Length == 0) return false;
        var r = new BvhRay(ray);
        var nodes = _nodes;
        if (!r.Hit(nodes[0], tMin, tMax, out _)) return false;

        Span<int> stack = stackalloc int[BvhBuilder.MaxStack];
        Span<float> enter = stackalloc float[BvhBuilder.MaxStack];
        int sp = 0;
        int node = 0;
        bool hit = false;
        float closest = tMax;
        while (true)
        {
            ref readonly var n = ref nodes[node];
            if (n.IsLeaf)
            {
                int end = n.Offset + n.Count;
                for (int i = n.Offset; i < end; i++)
                    if (_primitives[i].Hit(ray, tMin, closest, ref rec))
                    {
                        hit = true;
                        closest = rec.T;
                    }
            }
            else
            {
                int c = n.Offset;
                bool h0 = r.Hit(nodes[c], tMin, closest, out float t0);
                bool h1 = r.Hit(nodes[c + 1], tMin, closest, out float t1);
                if (h0 && h1)
                {
                    // Nearer first; the farther waits on the stack.
                    if (t1 < t0) { stack[sp] = c; enter[sp++] = t0; node = c + 1; }
                    else { stack[sp] = c + 1; enter[sp++] = t1; node = c; }
                    continue;
                }
                if (h0) { node = c; continue; }
                if (h1) { node = c + 1; continue; }
            }
            // Next pending node still in front of the closest hit.
            do
            {
                if (sp == 0) return hit;
                node = stack[--sp];
            } while (!BvhRay.StillOpen(enter[sp], closest));
        }
    }
}
