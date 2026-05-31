using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;

namespace RayTracer.Rendering;

/// <summary>
/// <see cref="IManifoldCaster"/> for a single-chart analytic primitive — a curved
/// caustic caster whose whole surface is one <see cref="IManifoldSurface"/>
/// (<see cref="Sphere"/>, <see cref="Cylinder"/>, <see cref="Cone"/>,
/// <see cref="Capsule"/>, <see cref="Torus"/>), possibly inside a
/// <see cref="Transform"/> (which is itself an <see cref="IManifoldSurface"/>
/// mapping the chart to world space). Every seed shares the single chart.
///
/// <para>This reproduces exactly the seeding the Phase-2/2b walker performed
/// inline for the sphere: refractive crossings come from ray-casting the straight
/// x→y segment against the primitive (the hit (u, v) is the chart parameter,
/// since the parameterisation matches <c>HitRecord.U/V</c> by contract); a
/// reflective seed comes from a coarse law-of-reflection scan over the chart.</para>
/// </summary>
public sealed class AnalyticManifoldCaster : IManifoldCaster
{
    private readonly IHittable _hittable;
    private readonly IManifoldSurface _surface;

    public AnalyticManifoldCaster(IHittable hittable, IManifoldSurface surface)
    {
        _hittable = hittable;
        _surface  = surface;
    }

    public bool SeedManifold(Vector3 x, Vector3 y, in CausticInterface ci,
                             Span<ManifoldSeed> seeds, out int k)
    {
        k = 0;
        if (ci.IsTransmissive)
        {
            // Refraction needs the straight ray to pass through the caster: 1
            // crossing (single interface) or 2 (solid glass, enter + exit).
            int n = SeedCrossings(_hittable, x, y, seeds);
            if (n < 1) return false;
            k = n;
            return true;
        }

        // Reflection: the straight x→y ray does not touch the mirror, so seed
        // from the surface point whose normal best bisects x and y instead.
        if (!SeedReflection(_surface, x, y, out Vector2 uv0)) return false;
        seeds[0] = new ManifoldSeed(_surface, uv0);
        k = 1;
        return true;
    }

    // Ray-cast the segment and record each crossing's (u, v); the chart is the
    // single analytic surface. Capped at seeds.Length (= the walk's MaxVertices).
    private int SeedCrossings(IHittable caster, Vector3 x, Vector3 y, Span<ManifoldSeed> seeds)
    {
        Vector3 d = y - x;
        float len = d.Length();
        if (len < 1e-9f) return 0;
        Vector3 dir = d / len;

        int count = 0;
        float tStart = 1e-4f;
        while (count < seeds.Length)
        {
            var rec = new HitRecord();
            if (!caster.Hit(new Ray(x, dir), tStart, len - 1e-4f, ref rec)) break;
            tStart = rec.T + 1e-3f;
            // Skip crossings on a flat, non-focusing region (e.g. a cylinder/cone
            // cap, marked by a null HitPrimitive): its planar (u, v) would mis-seed
            // the lateral chart. Only curved crossings seed a manifold vertex.
            if (rec.HitPrimitive == null) continue;
            seeds[count] = new ManifoldSeed(_surface, new Vector2(rec.U, rec.V));
            count++;
        }
        return count;
    }

    // Reflection seed: scan the surface for the (u, v) whose normal best bisects
    // x and y (the law-of-reflection seed), then let Newton refine. A coarse
    // 8×4 scan is plenty for a convex mirror and costs only arithmetic.
    private static bool SeedReflection(IManifoldSurface surf, Vector3 x, Vector3 y, out Vector2 bestUv)
    {
        bestUv = new Vector2(0.5f, 0.5f);
        float best = float.MaxValue;
        bool found = false;
        for (int iu = 0; iu < 8; iu++)
        for (int iv = 1; iv < 4; iv++)
        {
            float u = (iu + 0.5f) / 8f;
            float v = (iv + 0.5f) / 4f;
            if (!surf.EvaluateManifold(u, v, out var pt)) continue;
            Vector3 wa = Vector3.Normalize(x - pt.P);
            Vector3 wb = Vector3.Normalize(y - pt.P);
            // Both endpoints must be on the reflective (outward) side.
            if (Vector3.Dot(wa, pt.N) <= 0f || Vector3.Dot(wb, pt.N) <= 0f) continue;
            Vector3 h = Vector3.Normalize(wa + wb);
            float resid = 1f - MathF.Abs(Vector3.Dot(h, pt.N)); // 0 when h ∥ n
            if (resid < best) { best = resid; bestUv = new Vector2(u, v); found = true; }
        }
        return found;
    }
}
