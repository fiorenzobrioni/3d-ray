using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// A medium whose optical depth along a ray has a closed form (homogeneous,
/// height fog, layered atmosphere): the free-flight distance is sampled
/// exactly, by inverting the optical depth of one colour channel, and no
/// null collision is needed.
///
/// <para>Chromatic media use one-sample spectral MIS (the "hero" channel):
/// the channel is picked in proportion to the path's throughput and the
/// weight divides by the mixture of the three channels' densities, so every
/// channel is estimated without bias and the ones that still carry light
/// drive the sampling. The old samplers picked a channel uniformly and gave
/// a weight of 1 when its extinction was zero, which was biased for a medium
/// that does not attenuate one channel.</para>
/// </summary>
public abstract class AnalyticMedium : IMedium
{
    public IPhaseFunction Phase { get; }

    protected AnalyticMedium(IPhaseFunction phase) => Phase = phase;

    /// <summary>Optical depth τ(t) = ∫₀ᵗ σ_t ds per channel (a unit-length direction is assumed).</summary>
    public abstract Vector3 OpticalDepth(Ray ray, float t);

    /// <summary>Extinction σ_t at distance t.</summary>
    public abstract Vector3 SigmaT(Ray ray, float t);

    /// <summary>
    /// Distance at which channel <paramref name="c"/>'s optical depth reaches
    /// <paramref name="tau"/>, or +∞ when it does not before <paramref name="tMax"/>.
    /// </summary>
    protected abstract float DistanceAtDepth(Ray ray, int c, float tau, float tMax);

    /// <summary>
    /// The scattering at distance t, as the factor of the event's weight and
    /// the event's phase function. A medium with one species returns σ_s and
    /// its phase function; one with several picks a species (see
    /// <see cref="NishitaAtmosphereMedium"/>).
    /// </summary>
    protected abstract (Vector3 SigmaS, IPhaseFunction Phase) Scattering(Ray ray, float t, Vector3 throughput);

    /// <summary>
    /// The light scattered at distance t from direction <paramref name="wi"/>
    /// into the ray (travelling along <paramref name="wo"/>): Σ σ_s,k p_k, over
    /// the medium's species. What an equiangular sample gathers.
    /// </summary>
    public abstract Vector3 InScattering(Ray ray, float t, Vector3 wo, Vector3 wi);

    /// <summary>
    /// Whether a free flight over [0, tMax) can scatter at all (the atmosphere
    /// leaves the stretches that run to the sky to the sky model).
    /// </summary>
    public virtual bool ActsOn(float tMax) => true;

    /// <summary>
    /// Density with which <see cref="Sample"/> scatters at t (per unit
    /// length), for the MIS of other distance samplers against it.
    /// </summary>
    public float FreeFlightPdf(Ray ray, float t, Vector3 throughput)
    {
        Vector3 q = MediumMath.ChannelProbabilities(throughput);
        return MediumMath.Sum(q * SigmaT(ray, t) * Transmittance(ray, t));
    }

    public Vector3 Transmittance(Ray ray, float tMax)
        => MediumMath.Exp(-OpticalDepth(ray, MathF.Min(MathF.Max(tMax, 0f), 1e30f)));

    public MediumEvent Sample(Ray ray, float tMax, Vector3 throughput)
    {
        tMax = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        var (u1, u2) = MathUtils.Random2D();
        Vector3 q = MediumMath.ChannelProbabilities(throughput);
        int c = MediumMath.PickChannel(q, u1);
        float t = DistanceAtDepth(ray, c, MediumMath.Exponential(u2), tMax);

        if (t < tMax)
        {
            Vector3 tr = Transmittance(ray, t);
            // Density of scattering at t: Σ q_c σ_t,c Tr_c(t).
            float pdf = MediumMath.Sum(q * SigmaT(ray, t) * tr);
            if (pdf > 0f && float.IsFinite(pdf))
            {
                var (sigmaS, phase) = Scattering(ray, t, throughput * tr);
                return new MediumEvent(true, t, sigmaS * tr / pdf, phase);
            }
        }

        // Probability of no event before tMax: Σ q_c Tr_c(tMax).
        Vector3 trMax = Transmittance(ray, tMax);
        float pass = MediumMath.Sum(q * trMax);
        return new MediumEvent(false, tMax, pass > 0f ? trMax / pass : Vector3.Zero, Phase);
    }
}
