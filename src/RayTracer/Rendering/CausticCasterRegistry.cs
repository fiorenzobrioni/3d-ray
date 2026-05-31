using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Rendering;

/// <summary>
/// The set of smooth specular caustic casters (entities flagged
/// <c>caustic_caster</c> in YAML) the renderer focuses light through with
/// Manifold Next Event Estimation. Built once in the <see cref="Renderer"/>
/// constructor from the casters <see cref="Scene.SceneLoader"/> collected while
/// walking the scene graph.
///
/// <para>Only casters whose geometry is an <see cref="IManifoldSurface"/> (so
/// the manifold walk can evaluate the surface parametrically) are retained;
/// the loader emits a warning for any flagged entity that is not. Each entry
/// keeps both the <see cref="IHittable"/> (for the straight-ray seeding
/// intersection, which also yields the material and the seed (u, v)) and the
/// cached world-space AABB used for the cheap per-connection cull.</para>
///
/// <para>When the registry is empty the renderer skips the entire MNEE branch,
/// so scenes without caustic casters pay exactly zero overhead.</para>
/// </summary>
public sealed class CausticCasterRegistry
{
    public readonly struct Caster
    {
        /// <summary>Geometry for the seeding ray intersection (carries material + U/V).</summary>
        public readonly IHittable Hittable;
        /// <summary>Per-vertex seed producer for the Newton manifold walk.</summary>
        public readonly IManifoldCaster Seeder;
        /// <summary>Cached world-space bounds for the segment cull.</summary>
        public readonly AABB Box;

        public Caster(IHittable hittable, IManifoldCaster seeder, AABB box)
        {
            Hittable = hittable;
            Seeder   = seeder;
            Box      = box;
        }
    }

    private readonly Caster[] _casters;

    public int Count => _casters.Length;

    public static readonly CausticCasterRegistry Empty = new(System.Array.Empty<IHittable>());

    public CausticCasterRegistry(IReadOnlyList<IHittable> casters)
    {
        var list = new List<Caster>(casters.Count);
        foreach (var h in casters)
        {
            var seeder = BuildSeeder(h);
            if (seeder != null)
            {
                // Mesh casters need their facet adjacency built once here, on the
                // single-threaded registration path, so the caustic neighbor-seed
                // retry runs lock-free during the parallel render.
                if (seeder is Mesh mesh) mesh.PrepareCausticAdjacency();
                list.Add(new Caster(h, seeder, h.BoundingBox()));
            }
        }
        _casters = list.ToArray();
    }

    /// <summary>
    /// Selects the seeding strategy for a flagged caster: a geometry that already
    /// knows how to seed itself (mesh, CSG) is used directly; a <see cref="Transform"/>
    /// picks analytic-vs-baked-mesh internally; any other single-chart
    /// <see cref="IManifoldSurface"/> (the curved primitives) is wrapped in an
    /// <see cref="AnalyticManifoldCaster"/>. Returns null when the geometry cannot
    /// focus light, so the loader's gate and this builder agree.
    /// </summary>
    internal static IManifoldCaster? BuildSeeder(IHittable h) => h switch
    {
        Transform tr            => tr.CreateManifoldCaster(),
        IManifoldCaster mc      => mc,
        IManifoldSurface surf   => new AnalyticManifoldCaster(h, surf),
        _                       => null,
    };

    public ReadOnlySpan<Caster> Casters => _casters;

    /// <summary>
    /// Cheap relevance test: a caster can refract light from the segment
    /// <paramref name="from"/> → <paramref name="to"/> onto the receiver only if
    /// the straight segment actually passes through (or grazes) its bounds. The
    /// AABB is inflated slightly so a caster the seed ray skims is still tried.
    /// </summary>
    public static bool SegmentIntersectsBox(Vector3 from, Vector3 to, in AABB box)
    {
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 1e-12f) return false;
        Vector3 dir = d / len;
        var ray = new Ray(from, dir);
        // AABB.Hit consumes a HitRecord-free slab test via the (ray, tMin, tMax)
        // overload used across the BVH; reuse it for the cull.
        return box.Hit(ray, 1e-4f, len - 1e-4f);
    }
}
