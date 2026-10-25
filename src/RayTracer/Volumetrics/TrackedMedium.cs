using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;

namespace RayTracer.Volumetrics;

/// <summary>
/// One stretch of a ray over which σ̄ bounds the extinction of every channel.
/// </summary>
public struct MajorantSegment
{
    public float T0, T1, Sigma;
}

/// <summary>
/// A medium whose density has no closed-form integral (procedural noise,
/// voxel grids), sampled with null collisions (PIANO-REVIEW.md Phase 8):
/// candidate collisions are drawn against a majorant σ̄ ≥ σ_t, and at each one
/// the light either scatters or goes on through a "null" particle of density
/// σ_n = σ̄ − σ_t. The majorant may change along the ray (a grid of
/// majorants, <see cref="NextMajorant"/>), and is grey: one rate for the
/// three channels.
///
/// <para><b>Distances: spectral tracking</b> (Kutz, Habel, Li, Novák 2017).
/// The event at a collision is scattering with probability
/// P_s = |σ_s ⊙ h| / (|σ_s ⊙ h| + |σ_n ⊙ h|), where h is the throughput the
/// path carries so far, else null; the weight is σ_s / (σ̄ P_s) or
/// σ_n / (σ̄ P_n). Absorption is in the weights, never an event that ends a
/// path early. Every channel of a chromatic medium is estimated without
/// bias, which the old samplers, that tracked one channel and weighted with
/// that channel's σ_s / σ_t, were not.</para>
///
/// <para><b>Transmittance: ratio tracking</b> (Novák, Selle, Jarosz 2014):
/// the product of (1 − σ_t / σ̄) over the collisions, bounded by 1.</para>
///
/// <para>Neither has a cap on the number of collisions (the old 4096 cut
/// truncated long rays, biased): a Russian roulette on the weight ends a
/// walk that has nothing left to carry, without bias. The draws after the
/// first collision are loose (<see cref="Sampler.SampleLoose1D"/>): their
/// number varies, and must not shift the vertex's later decisions.</para>
/// </summary>
public abstract class TrackedMedium : IMedium
{
    public IPhaseFunction Phase { get; }

    protected TrackedMedium(IPhaseFunction phase) => Phase = phase;

    /// <summary>σ_a and σ_s at a point.</summary>
    protected abstract void Coefficients(Vector3 p, out Vector3 sigmaA, out Vector3 sigmaS);

    /// <summary>
    /// Opens the walk of the majorants along the ray over [0, tMax). The state
    /// is the medium's own (a grid's DDA).
    /// </summary>
    protected abstract MajorantWalk BeginMajorants(Ray ray, float tMax);

    /// <summary>The next segment of the walk, in increasing t; false past its end.</summary>
    protected abstract bool NextMajorant(Ray ray, ref MajorantWalk walk, out MajorantSegment segment);

    /// <summary>Majorant violations seen (σ_t above σ̄): zero for a correct medium.</summary>
    public static long Violations => s_violations;
    private static long s_violations;

    private const float RouletteThreshold = 0.05f;

    public MediumEvent Sample(Ray ray, float tMax, Vector3 throughput)
    {
        tMax = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        Vector3 w = Vector3.One;
        Vector3 h0 = Vector3.Max(throughput, Vector3.Zero);
        if (!(MediumMath.Max(h0) > 0f) || !float.IsFinite(MediumMath.Sum(h0))) h0 = Vector3.One;
        float hScale = 1f / MediumMath.Max(h0);

        // The first collision from the vertex's own stratified pair.
        var (u1, u2) = MathUtils.Random2D();
        bool first = true;

        MajorantWalk walk = BeginMajorants(ray, tMax);
        while (NextMajorant(ray, ref walk, out MajorantSegment seg))
        {
            if (!(seg.Sigma > 0f)) continue;
            float t = seg.T0;
            while (true)
            {
                float uDist = first ? u1 : Sampler.SampleLoose1D();
                float uEvent = first ? u2 : Sampler.SampleLoose1D();
                first = false;
                t += MediumMath.Exponential(uDist) / seg.Sigma;
                // Past the segment: the exponential is memoryless, start over
                // from its end with the next majorant (the draw is spent).
                if (t >= seg.T1) break;

                Coefficients(ray.Origin + ray.Direction * t, out Vector3 sa, out Vector3 ss);
                Vector3 st = sa + ss;
                Vector3 sn = new Vector3(seg.Sigma) - st;
                if (sn.X < 0f || sn.Y < 0f || sn.Z < 0f)
                {
                    Interlocked.Increment(ref s_violations);
                    sn = Vector3.Max(sn, Vector3.Zero);
                }

                Vector3 h = h0 * w * hScale;
                float ps = MediumMath.Sum(ss * h), pn = MediumMath.Sum(sn * h);
                if (!(ps + pn > 0f))
                    return new MediumEvent(false, tMax, Vector3.Zero, Phase);   // absorbed
                float pScatter = ps / (ps + pn);
                if (uEvent < pScatter)
                {
                    w *= ss / (seg.Sigma * pScatter);
                    return new MediumEvent(true, t, w, Phase);
                }
                w *= sn / (seg.Sigma * (1f - pScatter));

                // A walk with little left to carry plays a roulette.
                float m = MediumMath.Max(h0 * w) * hScale;
                if (m < RouletteThreshold)
                {
                    float survive = MathF.Max(m / RouletteThreshold, 0.05f);
                    if (Sampler.SampleLoose1D() >= survive)
                        return new MediumEvent(false, tMax, Vector3.Zero, Phase);
                    w /= survive;
                }
            }
        }
        return new MediumEvent(false, tMax, w, Phase);
    }

    public Vector3 Transmittance(Ray ray, float tMax)
    {
        tMax = MathF.Min(MathF.Max(tMax, 0f), 1e30f);
        Vector3 tr = Vector3.One;
        MajorantWalk walk = BeginMajorants(ray, tMax);
        while (NextMajorant(ray, ref walk, out MajorantSegment seg))
        {
            if (!(seg.Sigma > 0f)) continue;
            float t = seg.T0;
            while (true)
            {
                t += MediumMath.Exponential(Sampler.SampleLoose1D()) / seg.Sigma;
                if (t >= seg.T1) break;
                Coefficients(ray.Origin + ray.Direction * t, out Vector3 sa, out Vector3 ss);
                tr *= Vector3.Max(Vector3.One - (sa + ss) / seg.Sigma, Vector3.Zero);
                float m = MediumMath.Max(tr);
                if (m < RouletteThreshold)
                {
                    float survive = MathF.Max(m / RouletteThreshold, 0.05f);
                    if (!(m > 0f) || Sampler.SampleLoose1D() >= survive) return Vector3.Zero;
                    tr /= survive;
                }
            }
        }
        return tr;
    }
}

/// <summary>
/// State of a walk along a ray's majorants: generic fields a medium uses as
/// it needs (a single range, or a 3D DDA through a majorant grid).
/// </summary>
public struct MajorantWalk
{
    public float T, TEnd;
    public int X, Y, Z, StepX, StepY, StepZ;
    public float NextX, NextY, NextZ, DeltaX, DeltaY, DeltaZ;
    public bool Done;
}
