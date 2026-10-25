using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// What a free flight through a medium ends with: a scattering event at
/// <see cref="T"/> or no event before the end of the segment. <see cref="Weight"/>
/// multiplies the path throughput either way: at a scatter it already holds
/// σ_s (the light the event sends on, over the density of sampling it), at
/// the end of the segment the transmittance over the probability of getting
/// there. A zero weight means the light was absorbed. <see cref="Phase"/> is
/// the phase function of the event (a medium with several species, air and
/// dust, picks one per event).
/// </summary>
public readonly record struct MediumEvent(bool Scattered, float T, Vector3 Weight, IPhaseFunction Phase);

/// <summary>
/// A participating medium (PIANO-REVIEW.md Phase 8). Every estimate is
/// unbiased: analytic media (homogeneous, height fog, atmosphere) sample the
/// distance exactly, the others track it through null collisions against a
/// majorant (spectral tracking for distances, ratio tracking for shadow rays,
/// Kutz et al. 2017, Novák et al. 2014).
/// </summary>
public interface IMedium
{
    /// <summary>The medium's phase function (the event's own one is in <see cref="MediumEvent"/>).</summary>
    IPhaseFunction Phase { get; }

    /// <summary>
    /// Free flight along <paramref name="ray"/> over [0, <paramref name="tMax"/>).
    /// <paramref name="throughput"/> is the path's throughput at the start of
    /// the segment: chromatic media pick their colour channel, and weigh
    /// their events, by it, so the channels that still carry light decide.
    /// </summary>
    MediumEvent Sample(Ray ray, float tMax, Vector3 throughput);

    /// <summary>Transmittance over [0, <paramref name="tMax"/>): exact, or an unbiased estimate.</summary>
    Vector3 Transmittance(Ray ray, float tMax);
}

/// <summary>Helpers shared by the media.</summary>
internal static class MediumMath
{
    /// <summary>
    /// Channel probabilities proportional to the throughput (the "hero"
    /// channel of spectral MIS): a channel that carries no light is never
    /// chosen, and an unusable throughput falls back to equal shares.
    /// </summary>
    public static Vector3 ChannelProbabilities(Vector3 throughput)
    {
        Vector3 b = Vector3.Max(throughput, Vector3.Zero);
        float sum = b.X + b.Y + b.Z;
        if (!(sum > 0f) || float.IsInfinity(sum)) return new Vector3(1f / 3f);
        return b / sum;
    }

    /// <summary>Picks a channel with probabilities <paramref name="q"/>.</summary>
    public static int PickChannel(Vector3 q, float u)
        => u < q.X ? 0 : u < q.X + q.Y ? 1 : 2;

    public static float Channel(Vector3 v, int c) => c == 0 ? v.X : c == 1 ? v.Y : v.Z;

    public static Vector3 Exp(Vector3 minusTau) => new(MathF.Exp(minusTau.X), MathF.Exp(minusTau.Y), MathF.Exp(minusTau.Z));

    public static float Sum(Vector3 v) => v.X + v.Y + v.Z;

    public static float Max(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));

    /// <summary>A rate-1 exponential variate from a uniform one.</summary>
    public static float Exponential(float u) => -MathF.Log(1f - MathF.Min(u, 0.99999994f));
}
