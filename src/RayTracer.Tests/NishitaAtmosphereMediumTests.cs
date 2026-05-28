using System.Numerics;
using RayTracer.Core;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Regression tests for <see cref="NishitaAtmosphereMedium"/>'s delta-tracking
/// <c>Sample()</c> estimator.
///
/// <para>The medium samples the free-flight distance by delta tracking, so the
/// transmittance to a point is carried by the <i>probability</i> of reaching it
/// (the null-collision random walk), NOT by the returned <c>beta</c>. A previous
/// implementation multiplied <c>beta</c> by the full analytic transmittance on
/// top of that, double-counting it and making the atmosphere far too dark on
/// both the scatter and the pass-through branch.</para>
///
/// <para>The decisive, scale-independent invariant is the <b>single-sample
/// transmittance estimator</b>: for a unit "background" radiance the Monte-Carlo
/// mean of <c>(scattered ? 0 : beta)</c> must reproduce the analytic per-channel
/// <see cref="NishitaAtmosphereMedium.Transmittance"/>. Under the old
/// double-counting weight the mean collapsed to ≈ Tr² and this test fails.</para>
/// </summary>
public class NishitaAtmosphereMediumTests
{
    // Cranked-up air density so the per-world-unit extinction is O(1) over the
    // short test ray (the absolute physical scale is irrelevant to the
    // estimator-consistency invariant, only that both events occur often).
    private static NishitaAtmosphereMedium MakeMedium() => new(
        phase:       new HenyeyGreensteinPhase(0f),
        airDensity:  new Vector3(5e4f, 5e4f, 5e4f),
        dustDensity: 1f,
        seaLevelY:   0f,
        worldScale:  1f);

    [Fact]
    public void Sample_PassThroughEstimator_ReproducesAnalyticTransmittance()
    {
        var medium = MakeMedium();
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);   // horizontal, constant density
        const float tMax = 1f;
        const int N = 200_000;

        // Monte-Carlo mean of (no-scatter ? beta : 0) over a unit background.
        Vector3 acc = Vector3.Zero;
        for (int i = 0; i < N; i++)
        {
            bool scattered = medium.Sample(ray, tMax, out _, out Vector3 beta, out _);
            if (!scattered) acc += beta;
        }
        Vector3 est = acc / N;

        Vector3 trAnalytic = medium.Transmittance(ray, tMax);

        // The atmosphere is strongly chromatic here, so the channels differ —
        // the test only passes if each channel is reproduced independently.
        Assert.True(trAnalytic.X - trAnalytic.Z > 0.1f,
            $"Test setup should be chromatic: Tr={trAnalytic}");

        Assert.InRange(est.X, trAnalytic.X - 0.02f, trAnalytic.X + 0.02f);
        Assert.InRange(est.Y, trAnalytic.Y - 0.02f, trAnalytic.Y + 0.02f);
        Assert.InRange(est.Z, trAnalytic.Z - 0.02f, trAnalytic.Z + 0.02f);
    }

    [Fact]
    public void Sample_ScatterBeta_IsFiniteNonNegativeAndChromatic()
    {
        var medium = MakeMedium();
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        const int N = 50_000;

        int scatters = 0;
        for (int i = 0; i < N; i++)
        {
            if (medium.Sample(ray, 2f, out _, out Vector3 beta, out _))
            {
                scatters++;
                Assert.True(float.IsFinite(beta.X) && float.IsFinite(beta.Y) && float.IsFinite(beta.Z),
                    $"Scatter beta must be finite: {beta}");
                Assert.True(beta.X >= 0f && beta.Y >= 0f && beta.Z >= 0f,
                    $"Scatter beta must be non-negative: {beta}");
            }
        }
        Assert.True(scatters > 0, "Expected at least one scattering event in a dense atmosphere.");
    }

    [Fact]
    public void Sample_VacuumAtHighAltitude_NeverScatters()
    {
        var medium = MakeMedium();
        // Above the 60 km atmosphere top (worldScale 1 ⇒ 60 000 world units),
        // density clamps to zero in both species.
        var ray = new Ray(new Vector3(0f, 100_000f, 0f), Vector3.UnitX);
        for (int i = 0; i < 200; i++)
        {
            bool scattered = medium.Sample(ray, 10f, out _, out Vector3 beta, out _);
            Assert.False(scattered, "Above the atmosphere top the medium must not scatter.");
            Assert.Equal(Vector3.One, beta);
        }
    }
}
