using System.Numerics;
using RayTracer.Materials;

namespace RayTracer.Geometry;

/// <summary>
/// One seed for the <see cref="Rendering.ManifoldWalker"/>: the parametric chart
/// (<see cref="IManifoldSurface"/>) a specular vertex lives on, plus the (u, v)
/// starting guess on that chart. For a single-chart caster (an analytic curved
/// primitive) every seed shares the same <see cref="Chart"/>; for a mesh or CSG
/// caster the two interfaces of a refractive path may land on <em>different</em>
/// charts (different triangles / different child primitives), which is exactly
/// why the walk carries one chart per vertex.
/// </summary>
public readonly struct ManifoldSeed
{
    public readonly IManifoldSurface Chart;
    public readonly Vector2 Uv;

    public ManifoldSeed(IManifoldSurface chart, Vector2 uv)
    {
        Chart = chart;
        Uv    = uv;
    }
}

/// <summary>
/// A caustic caster that can produce starting vertices ("seeds") for a manifold
/// walk connecting a receiver point <c>x</c> to an emitter point <c>y</c>.
///
/// <para>Seeding is the only geometry-specific part of MNEE/SMS: once the walk
/// has a chart + (u, v) per specular vertex, the Newton solve, throughput and
/// geometric term are identical for every caster. Implementations:</para>
/// <list type="bullet">
///   <item><see cref="Rendering.AnalyticManifoldCaster"/> — a single-chart curved
///   primitive (sphere, cylinder, cone, capsule, torus), optionally wrapped in a
///   <see cref="Transform"/>. Every seed shares the one chart.</item>
///   <item><see cref="Mesh"/> — ray-casts the segment through its internal BVH and
///   uses the hit triangle as the per-vertex chart (per-triangle clamp).</item>
///   <item><see cref="CsgObject"/> — ray-casts through the boolean solid and uses
///   the underlying curved primitive as the chart, clamped to CSG membership.</item>
/// </list>
/// </summary>
public interface IManifoldCaster
{
    /// <summary>
    /// Fills <paramref name="seeds"/> with up to <c>seeds.Length</c> specular
    /// seeds for the connection <paramref name="x"/> → caster → <paramref name="y"/>.
    /// For a transmissive interface the seeds are the straight-segment crossings
    /// (1 for a single interface, 2 for a solid); for a reflective interface a
    /// single law-of-reflection seed is produced. Returns false (with
    /// <paramref name="k"/> = 0) when no admissible seed exists.
    /// </summary>
    bool SeedManifold(Vector3 x, Vector3 y, in CausticInterface ci,
                      Span<ManifoldSeed> seeds, out int k);
}

/// <summary>
/// A caustic caster that can offer ALTERNATIVE per-vertex seed charts when the
/// primary manifold solve is rejected by the per-triangle clamp — the first
/// ("neighbor-seed") tier of mesh edge-crossing.
///
/// <para>The straight-segment seed lands the Newton walk on the facet the chord
/// crosses, but the true specular vertex may sit on an <em>edge-adjacent</em>
/// facet, so the converged point fails <see cref="IClampedChart.Accept"/> and the
/// connection is dropped. Instead of dropping it, the <see cref="Rendering.ManifoldWalker"/>
/// re-seeds the offending vertex on each edge-neighbour of its facet and re-runs
/// the <em>same</em> solve. This is a caller-side retry that leaves the shared
/// Newton solver untouched, so analytic single-chart primitives and CSG solids —
/// which do not implement this interface — take the identical (bit-for-bit) path
/// they did before.</para>
///
/// <para>It only recovers vertices ONE facet away from the seed; a vertex several
/// facets off (a strongly curved, coarsely tessellated caster) is still missed.
/// Full in-solve edge walking across the adjacency graph is a later phase
/// (tracked in PLANNING.md) that can build on the adjacency this tier introduces.</para>
/// </summary>
public interface INeighborSeedCaster
{
    /// <summary>
    /// Fills <paramref name="neighbors"/> with the edge-adjacent facet charts of
    /// the facet that produced <paramref name="seed"/> (each seeded at its
    /// centroid), and returns how many were written. Returns 0 when the seed's
    /// chart is not one of this caster's facets or it has no recorded neighbours.
    /// </summary>
    int FacetNeighbors(in ManifoldSeed seed, Span<ManifoldSeed> neighbors);
}

/// <summary>
/// A chart that imposes an extra membership clamp on a converged manifold vertex.
/// Implemented by the CSG chart (a child primitive of a boolean solid): a Newton
/// solution lies on the child's surface but may fall in a region the boolean
/// operation removed, so it is only admissible if it still lies on the result's
/// boundary. The <see cref="Rendering.ManifoldWalker"/> calls <see cref="Accept"/>
/// once per vertex AFTER convergence (the test is a few inside-solid probes, too
/// costly to run every Newton step), keeping the inner solve cheap. Charts that
/// do not implement this interface are always accepted.
/// </summary>
public interface IClampedChart
{
    bool Accept(in ManifoldPoint pt);
}
