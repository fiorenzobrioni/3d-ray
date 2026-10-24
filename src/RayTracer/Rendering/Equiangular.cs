using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

/// <summary>
/// Equiangular sampling (Kulla and Fajardo 2012, PIANO-REVIEW.md Phase 8):
/// the light a point or spot light sends into a medium along a ray segment,
/// sampled at distances distributed as the angle the light sees, so the
/// glow round the light and the beam of a spot in fog converge quickly where
/// free-flight sampling, blind to the light, wastes most of its samples.
///
/// <para>Two estimators of the same single scattering, combined by the balance
/// heuristic over the distance: (A) the path's own free flight, with
/// next-event estimation at its scattering point, and (C) an equiangular
/// distance, with a light sample at it. A light no phase-sampled ray can reach
/// (a point, a spot) has no third strategy, so the two weights partition
/// unity: A's NEE is weighted by p_ff / (p_ff + p_eq) (with the light pick
/// probabilities on both sides) and C by the complement. Only in analytic
/// media, where p_ff, the density the free flight scatters with, is known
/// exactly.</para>
/// </summary>
public partial class Renderer
{
    // The free-flight segment the current medium vertex was sampled on, for
    // the weight of its NEE (see EquiangularNeeWeight).
    [ThreadStatic] private static bool t_eqOn;
    [ThreadStatic] private static Ray t_eqRay;
    [ThreadStatic] private static float t_eqTMax, t_eqT, t_eqPff;

    /// <summary>Equiangular sampling on (the default) or off (for comparisons).</summary>
    public bool Equiangular { get; set; } = true;

    /// <summary>Lights with a position that no phase-sampled ray can reach.</summary>
    private bool EquiangularLight(ILight light, out Vector3 position)
    {
        position = default;
        if (_mis.PairsWithBsdf(light)) return false;
        switch (light)
        {
            case PointLight p: position = p.Position; return true;
            case SpotLight s: position = s.Position; return true;
            default: return false;
        }
    }

    /// <summary>
    /// The equiangular parametrisation of [0, tMax) toward <paramref name="c"/>:
    /// the foot of the perpendicular at <paramref name="delta"/>, its length
    /// <paramref name="dist"/>, and the angles of the segment's ends.
    /// </summary>
    private static bool EquiangularFrame(in Ray ray, float tMax, Vector3 c,
                                         out float delta, out float dist, out float thA, out float thB)
    {
        delta = Vector3.Dot(c - ray.Origin, ray.Direction);
        dist = (ray.Origin + ray.Direction * delta - c).Length();
        thA = MathF.Atan2(-delta, dist);
        thB = MathF.Atan2(MathF.Min(tMax, 1e30f) - delta, dist);
        return dist > 1e-6f && thB - thA > 1e-7f;
    }

    private static float EquiangularPdf(float t, float delta, float dist, float thA, float thB)
    {
        float x = t - delta;
        return dist / ((thB - thA) * (dist * dist + x * x));
    }

    /// <summary>Probability that strategy C, at the segment's start, picks <paramref name="light"/>.</summary>
    private float EquiangularPick(ILight light, in Ray ray) => _mis.PicksOneLight ? _mis.Pick(light, ray.Origin) : 1f;

    /// <summary>Probability that NEE at <paramref name="p"/> picks <paramref name="light"/>.</summary>
    private float NeePick(ILight light, Vector3 p) => _mis.PicksOneLight ? _mis.Pick(light, p) : 1f;

    /// <summary>
    /// Strategy C over the segment [0, tMax) of an analytic medium: the light
    /// of the point and spot lights scattered into the ray, relative to the
    /// throughput at the segment's start.
    /// </summary>
    private Vector3 EquiangularDirect(in Ray ray, float tMax, AnalyticMedium medium, Vector3 throughput,
                                      in MediumStack media)
    {
        if (!_hasEquiangularLights || !medium.ActsOn(tMax)) return Vector3.Zero;
        Vector3 sum = Vector3.Zero;
        if (_mis.PicksOneLight)
        {
            var (idx, pick) = _mis.Choose(ray.Origin, MathUtils.RandomFloat());
            if (idx >= 0 && pick > 0f)
                sum += EquiangularLightSample(ray, tMax, medium, throughput, media, _lights[idx], pick);
        }
        else
        {
            foreach (ILight light in _lights)
                sum += EquiangularLightSample(ray, tMax, medium, throughput, media, light, 1f);
        }
        return sum;
    }

    private Vector3 EquiangularLightSample(in Ray ray, float tMax, AnalyticMedium medium, Vector3 throughput,
                                           in MediumStack media, ILight light, float pick)
    {
        if (!EquiangularLight(light, out Vector3 c)) return Vector3.Zero;
        if (!EquiangularFrame(ray, tMax, c, out float delta, out float dist, out float thA, out float thB))
            return Vector3.Zero;
        float u = MathUtils.RandomFloat();
        float t = delta + dist * MathF.Tan(thA + u * (thB - thA));
        if (!(t >= 0f && t < tMax)) return Vector3.Zero;
        float pdfEq = EquiangularPdf(t, delta, dist, thA, thB);
        if (!(pdfEq > 0f) || !float.IsFinite(pdfEq)) return Vector3.Zero;

        Vector3 p = ray.Origin + ray.Direction * t;
        Vector3 lightSum = Vector3.Zero;
        ShadowRay.BeginMedia(in media, _globalMedium);
        try
        {
            for (int s = 0; s < light.ShadowSamples; s++)
            {
                var (inShadow, color, dirToLight, _) = SampleLight(light, p, Vector3.Zero, s, ray.Time);
                if (inShadow) continue;
                lightSum += color * medium.InScattering(ray, t, ray.Direction, dirToLight);
            }
        }
        finally
        {
            ShadowRay.EndMedia();
        }
        if (lightSum == Vector3.Zero) return Vector3.Zero;

        float pff = medium.FreeFlightPdf(ray, t, throughput) * NeePick(light, p);
        float wC = pdfEq * pick / (pdfEq * pick + pff);
        return medium.Transmittance(ray, t) * lightSum * (wC / (pdfEq * pick));
    }

    /// <summary>
    /// Strategy A's weight for <paramref name="light"/> at the medium vertex
    /// being shaded (1 outside an equiangular segment or for other lights).
    /// </summary>
    private float EquiangularNeeWeight(ILight light, float pickA)
    {
        if (!t_eqOn || !EquiangularLight(light, out Vector3 c)) return 1f;
        if (!EquiangularFrame(t_eqRay, t_eqTMax, c, out float delta, out float dist, out float thA, out float thB))
            return 1f;
        float pdfEq = EquiangularPdf(t_eqT, delta, dist, thA, thB) * EquiangularPick(light, t_eqRay);
        float pff = t_eqPff * pickA;
        return pff / (pff + pdfEq);
    }
}
