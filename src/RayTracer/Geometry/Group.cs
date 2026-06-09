using System.Numerics;
using RayTracer.Acceleration;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// A hierarchical container that groups multiple <see cref="IHittable"/> children
/// under a shared transform. Enables scene-graph composition: a Group can contain
/// primitives, CSG objects, meshes, and other Groups for arbitrary nesting depth.
///
/// <b>Transform inheritance:</b>
/// The Group's own transform (applied externally by SceneLoader via the standard
/// Transform wrapper) propagates to all children. Each child may additionally have
/// its own local transform applied before the group's. The resulting chain is:
///   childLocal → groupTransform → parentGroupTransform → ...
/// which is exactly how professional renderers (PBRT, Mitsuba) handle scene graphs.
///
/// <b>BVH:</b>
/// When the group contains more than <see cref="BvhThreshold"/> finite children,
/// an internal BVH is built for O(log N) traversal. Below the threshold, a flat
/// HittableList avoids construction overhead.
///
/// <b>Geometry lights:</b>
/// SceneLoader.ExtractGeometryLights() recurses into Groups to find emissive
/// primitives, so emissive children participate in NEE correctly (each wrapped
/// in the cumulative Transform chain).
///
/// <b>Seed propagation:</b>
/// A Group is treated as a single logical object: setting Seed propagates the
/// same value to every child, so procedural textures within the group share a
/// uniform deterministic pattern (as if the group were one solid).
///
/// YAML example:
/// <code>
///   - name: "lamppost"
///     type: "group"
///     translate: [5, 0, 0]
///     rotate: [0, 45, 0]
///     material: "iron"          # fallback for children without own material
///     children:
///       - type: "cylinder"
///         center: [0, 0, 0]
///         radius: 0.1
///         height: 3.0
///       - type: "sphere"
///         center: [0, 3.2, 0]
///         radius: 0.3
///         material: "glass"     # overrides group fallback
/// </code>
/// </summary>
public class Group : IHittable
{
    /// <summary>
    /// Minimum children count before BVH construction is beneficial.
    /// Matches the threshold used by the top-level scene BVH in SceneLoader.
    /// </summary>
    private const int BvhThreshold = 4;

    private readonly IHittable _root;
    private readonly List<IHittable> _children;
    private int _seed;

    /// <summary>Number of direct children in this group.</summary>
    public int ChildCount => _children.Count;

    /// <summary>
    /// Read-only access to the children list. Used by SceneLoader to extract
    /// geometry lights and detect infinite planes inside groups.
    /// </summary>
    public IReadOnlyList<IHittable> Children => _children;

    /// <summary>
    /// Constructs a Group from a list of children.
    /// Infinite planes are kept in a flat list; finite objects go into a BVH
    /// when the count exceeds the threshold.
    /// </summary>
    public Group(List<IHittable> children)
    {
        _children = children;

        if (children.Count == 0)
        {
            _root = new HittableList();
            return;
        }

        // Separate infinite planes from finite objects (same logic as SceneLoader)
        var finiteObjects   = new List<IHittable>();
        var infiniteObjects = new List<IHittable>();

        foreach (var child in children)
        {
            if (IsInfinitePlane(child))
                infiniteObjects.Add(child);
            else
                finiteObjects.Add(child);
        }

        var rootObjects = new List<IHittable>();

        if (finiteObjects.Count > BvhThreshold)
            rootObjects.Add(new BvhNode(finiteObjects));
        else
            rootObjects.AddRange(finiteObjects);

        rootObjects.AddRange(infiniteObjects);

        _root = rootObjects.Count == 1
            ? rootObjects[0]
            : new HittableList(rootObjects);
    }

    public bool Hit(in Ray ray, float tMin, float tMax, ref HitRecord rec)
    {
        return _root.Hit(ray, tMin, tMax, ref rec);
    }

    public AABB BoundingBox()
    {
        return _root.BoundingBox();
    }

    public int Seed
    {
        get => _seed;
        set
        {
            // Propagate the SAME seed to all children. A Group is one logical
            // object, so every child shares the same deterministic seed and
            // procedural textures appear uniform across the whole group.
            _seed = value;
            for (int i = 0; i < _children.Count; i++)
                _children[i].Seed = value;
        }
    }

    /// <summary>
    /// Detects InfinitePlane instances, including those wrapped in Transform chains.
    /// Mirrors the logic in SceneLoader.IsInfinitePlane().
    /// </summary>
    private static bool IsInfinitePlane(IHittable obj) => obj switch
    {
        InfinitePlane => true,
        Transform t   => IsInfinitePlane(t.Inner),
        _             => false
    };
}
