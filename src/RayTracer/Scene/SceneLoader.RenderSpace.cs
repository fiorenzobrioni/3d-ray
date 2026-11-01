using System.Numerics;
using RayTracer.Geometry;

namespace RayTracer.Scene;

/// <summary>A point in double precision (scene positions before the render-space shift).</summary>
public readonly record struct Double3(double X, double Y, double Z)
{
    public static readonly Double3 Zero = new(0, 0, 0);

    public static Double3 operator -(Double3 a, Double3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public bool IsZero => X == 0 && Y == 0 && Z == 0;

    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

    public static Double3 From(Vector3 v) => new(v.X, v.Y, v.Z);

    public static Double3? From(List<double>? list) =>
        list == null || list.Count < 3 ? null : new Double3(list[0], list[1], list[2]);
}

// SceneLoader, part: the render space (PIANO-REVIEW.md Phase 11).
//
// The scene is built in a render space: the world translated so the camera
// sits at the origin (as the "camera-world" space of production renderers).
// A hit point carries a float error proportional to its coordinates, so the
// ray offsets grow with them (MathUtils.RelativeOffset); with the camera at the
// origin the geometry it sees has small coordinates and full precision however
// far from the world origin the scene is. Positions are read in double
// (List<double> in the scene classes) and shifted before they become float,
// in one place per entity:
//  - an entity with a transform (or motion, or an instance, or a box) keeps its
//    own coordinates and has its translation shifted;
//  - an entity without one has its own coordinates shifted (centre, corner,
//    vertices, bounds, a mesh's vertices); the same rule applies to the
//    children of a group or CSG without a transform.
// Nothing that depends on where things are changes: a texture reads the point
// of the original space (the world list adds the origin back to the local
// point of a hit without a local frame, a geometry light to its samples),
// height fog, atmosphere and procedural media read the original altitude and
// position, the adaptive subdivision measures in the original space.
public partial class SceneLoader
{
    // The camera position, in world units: render space = world − origin.
    // Per thread, like a load: two scenes loaded at once do not share it.
    [ThreadStatic] private static Double3 _renderOrigin;

    // Subtracted from the positions read in the current context: the origin
    // at world level, zero inside an entity with a transform.
    [ThreadStatic] private static Double3 _shift;

    /// <summary>The origin of the render space of the last scene loaded on
    /// this thread (the camera position, world units): a world point p is
    /// p − origin in the scene built.</summary>
    public static Vector3 LastRenderOrigin => _renderOrigin.ToVector3();

    /// <summary>
    /// A position of the current context in render space; <paramref name="fallback"/>
    /// (a world position) when the key is absent.
    /// </summary>
    private static Vector3 Pos(List<double>? p, Vector3 fallback) =>
        ((Double3.From(p) ?? Double3.From(fallback)) - _shift).ToVector3();

    /// <summary>A position of the current context in render space, or null when absent.</summary>
    private static Vector3? Pos(List<double>? p) =>
        Double3.From(p) is { } d ? (d - _shift).ToVector3() : null;

    /// <summary>A position of the world as written (no shift): the adaptive
    /// subdivision measures in the original space.</summary>
    private static Vector3? WorldPos(List<double>? p) => Double3.From(p)?.ToVector3();

    /// <summary>
    /// True when the entity is placed by a transform: then its own coordinates
    /// are local (built with no shift) and the shift goes into its translation.
    /// A box and an instance have no position of their own, so they are always
    /// placed by one.
    /// </summary>
    private static bool PlacedByTransform(EntityData e, bool topLevel) =>
        (topLevel && e.Motion is { Count: > 0 }) || e.Type is "instance" or "box"
        || ComputeTransformMatrix(e) != Matrix4x4.Identity;

    /// <summary>Builds the contents of an entity placed by a transform: in its
    /// own coordinates, with no shift.</summary>
    private static T BuildLocal<T>(Func<T> build)
    {
        var saved = _shift;
        _shift = Double3.Zero;
        try { return build(); }
        finally { _shift = saved; }
    }

    /// <summary>
    /// Builds an entity in the current context and places it: built locally
    /// and wrapped in its transform when it has one (the translation shifted),
    /// built with its own coordinates shifted otherwise. Motion keys are left
    /// to the caller; only a top-level entity has them (a child's are ignored).
    /// </summary>
    private static IHittable? BuildPlaced(EntityData e, Func<IHittable?> build, bool topLevel = false)
    {
        if (!PlacedByTransform(e, topLevel)) return build();
        var hittable = BuildLocal(build);
        if (hittable == null || (topLevel && e.Motion is { Count: > 0 })) return hittable;
        var m = ComputeTransformMatrix(e, _shift);
        return m == Matrix4x4.Identity ? hittable : new Transform(hittable, m);
    }
}
