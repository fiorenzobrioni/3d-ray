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
