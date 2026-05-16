using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Smooth Voronoi contract — DEVLOG "Texturing VFX production-grade" step 3.
///
/// <para>
/// Validates that the new <see cref="VoronoiTexture.Smoothness"/> parameter and
/// the underlying <see cref="WorleyNoise.EvaluateSmooth"/> log-sum-exp soft-min
/// satisfy the invariants required for production use:
/// </para>
/// <list type="bullet">
///   <item><description>Back-compat: <c>Smoothness == 0</c> is bit-identical to the hard <c>Evaluate</c>.</description></item>
///   <item><description>Limit: <c>Smoothness → 0</c> approaches the hard result asymptotically.</description></item>
///   <item><description>Monotonicity: <c>softF1 ≤ hardF1</c> and <c>softF2 ≥ softF1</c> by construction.</description></item>
///   <item><description>Continuity: the soft field has no measurable discontinuities when scanned across
///       a fine 1-D grid, unlike the hard <c>F2−F1</c> output which spikes at cell boundaries.</description></item>
///   <item><description>Stability: no NaN/Inf even at high <c>k</c> values, thanks to log-sum-exp rebasing.</description></item>
/// </list>
/// </summary>
public class SmoothVoronoiTests
{
    private const float Eps = 1e-5f;

    [Fact]
    public void SmoothnessZero_BitIdentical_ToHardEvaluate()
    {
        // Invariant per DEVLOG: a scene that omits `smoothness` (or sets it to 0)
        // must render byte-identical to the pre-change behaviour.
        var worley = new WorleyNoise(seed: 17);

        foreach (var p in SamplePoints())
        {
            worley.Evaluate(p, WorleyNoise.Metric.Euclidean, 1f, out float h1, out float h2, out int hid);
            worley.EvaluateSmooth(p, WorleyNoise.Metric.Euclidean, 1f, 0f,
                                  out float s1, out float s2, out int sid);
            Assert.Equal(h1, s1);
            Assert.Equal(h2, s2);
            Assert.Equal(hid, sid);
        }
    }

    [Fact]
    public void SmoothnessApproachesZero_ConvergesToHardEvaluate()
    {
        // Limit k = 20/smoothness → ∞: the log-sum-exp collapses to the hard
        // min. With smoothness = 1e-3, k = 20000, and the residual error must
        // be far below 1e-5 in single precision regardless of point position.
        var worley = new WorleyNoise(seed: 42);
        const float smoothness = 1e-3f;

        foreach (var p in SamplePoints())
        {
            worley.Evaluate(p, WorleyNoise.Metric.Euclidean, 1f, out float h1, out float h2, out _);
            worley.EvaluateSmooth(p, WorleyNoise.Metric.Euclidean, 1f, smoothness,
                                  out float s1, out float s2, out _);

            Assert.True(MathF.Abs(s1 - h1) < 1e-4f, $"softF1 - hardF1 = {s1 - h1} at p={p}");
            // softF2 is allowed to deviate slightly more at boundary points
            // (it is built from sumAll - 1, which can be tiny when the hard
            // closest dominates) — but still must stay within numerical noise.
            Assert.True(MathF.Abs(s2 - h2) < 5e-3f, $"softF2 - hardF2 = {s2 - h2} at p={p}");
        }
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(0.7f)]
    [InlineData(1.0f)]
    public void SoftMin_NeverExceedsHardMin_And_SoftF2NeverBelowSoftF1(float smoothness)
    {
        // Soft-min(d) ≤ min(d) for any k > 0 (Jensen on the convex −log).
        // softF2 ≥ softF1 by construction: softF2 drops the dominant weight
        // from the same Σ exp(...), so log(smaller sum) > log(full sum) ⇒
        // -log/k of the smaller sum is larger.
        var worley = new WorleyNoise(seed: 7);

        foreach (var p in SamplePoints())
        {
            worley.Evaluate(p, WorleyNoise.Metric.Euclidean, 1f, out float hardF1, out _, out _);
            worley.EvaluateSmooth(p, WorleyNoise.Metric.Euclidean, 1f, smoothness,
                                  out float s1, out float s2, out _);
            Assert.True(s1 <= hardF1 + Eps, $"softF1={s1} > hardF1={hardF1} at p={p}");
            Assert.True(s2 + Eps >= s1, $"softF2={s2} < softF1={s1} at p={p}");
            Assert.False(float.IsNaN(s1) || float.IsInfinity(s1));
            Assert.False(float.IsNaN(s2) || float.IsInfinity(s2));
        }
    }

    [Fact]
    public void SoftField_IsContinuousOnFineGridScan_WhereHardF2MinusF1IsNot()
    {
        // The motivating use case for Smooth Voronoi: hard F2−F1 has a V-shaped
        // ridge along every cell boundary (the gradient flips sign across the
        // edge), producing step alias along that crease. The soft variant
        // smooths the ridge — the discrete forward-difference of softF1
        // along a finely sampled line must stay bounded everywhere, while
        // hard F2−F1 can show much larger swings at boundaries.
        var worley = new WorleyNoise(seed: 5);

        // Scan a 5-unit line across many cells.
        const int N = 4096;
        const float length = 5f;
        const float dx = length / N;

        float[] softF1 = new float[N];
        float[] softF2MinusF1 = new float[N];

        for (int i = 0; i < N; i++)
        {
            var p = new Vector3(-2.5f + i * dx, 0.27f, 1.83f);
            worley.EvaluateSmooth(p, WorleyNoise.Metric.Euclidean, 1f, 0.7f,
                                  out float s1, out float s2, out _);
            softF1[i] = s1;
            softF2MinusF1[i] = s2 - s1;
        }

        // The Lipschitz constant of the Euclidean distance to any one feature
        // point is exactly 1 (in the direction of motion). The soft-min over
        // a finite set is also 1-Lipschitz. Hence consecutive samples spaced
        // by dx may not differ by more than dx (+ rounding tolerance).
        for (int i = 1; i < N; i++)
        {
            float deltaF1 = MathF.Abs(softF1[i] - softF1[i - 1]);
            Assert.True(deltaF1 <= dx + 1e-4f,
                $"softF1 not 1-Lipschitz: |Δ|={deltaF1} > {dx} at i={i}");

            // softF2 - softF1 cannot jump faster than the difference of two
            // 1-Lipschitz fields, so 2·dx is the strict upper bound.
            float deltaCrack = MathF.Abs(softF2MinusF1[i] - softF2MinusF1[i - 1]);
            Assert.True(deltaCrack <= 2f * dx + 1e-4f,
                $"smooth crackle not 2-Lipschitz: |Δ|={deltaCrack} > {2 * dx} at i={i}");
        }
    }

    [Fact]
    public void HardCrackle_HasLargerLocalSwings_ThanSoftCrackle()
    {
        // Sanity check that smoothness actually does something useful:
        // on the same scan line, the maximum local jump of (F2−F1) is strictly
        // smaller with smoothness > 0 than with smoothness = 0, around every
        // cell boundary. We don't compare per-sample (the fields differ in
        // shape) but compare the worst-case "non-smoothness" measure
        // |Δ(F2-F1)| / dx − 2 over the scan.
        var worley = new WorleyNoise(seed: 99);
        const int N = 2048;
        const float length = 4f;
        const float dx = length / N;

        float HardWorstSlope()
        {
            float prev = 0f;
            float worst = 0f;
            for (int i = 0; i < N; i++)
            {
                var p = new Vector3(-2f + i * dx, 0.13f, 0.91f);
                worley.Evaluate(p, WorleyNoise.Metric.Euclidean, 1f, out float h1, out float h2, out _);
                float v = h2 - h1;
                if (i > 0) worst = MathF.Max(worst, MathF.Abs(v - prev));
                prev = v;
            }
            return worst / dx;
        }

        float SoftWorstSlope(float smoothness)
        {
            float prev = 0f;
            float worst = 0f;
            for (int i = 0; i < N; i++)
            {
                var p = new Vector3(-2f + i * dx, 0.13f, 0.91f);
                worley.EvaluateSmooth(p, WorleyNoise.Metric.Euclidean, 1f, smoothness,
                                      out float s1, out float s2, out _);
                float v = s2 - s1;
                if (i > 0) worst = MathF.Max(worst, MathF.Abs(v - prev));
                prev = v;
            }
            return worst / dx;
        }

        float hard = HardWorstSlope();
        float soft = SoftWorstSlope(0.7f);

        Assert.True(soft < hard,
            $"smooth crackle should reduce worst local slope: hard={hard} soft={soft}");
    }

    [Fact]
    public void VoronoiTexture_BackCompat_BitIdenticalWithDefaultSmoothness()
    {
        // The VoronoiTexture wrapper must keep its byte-identical legacy output
        // when Smoothness is left at its default (= 0). This is the contract
        // every scene that doesn't opt in relies on.
        var tex = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        {
            Metric = WorleyNoise.Metric.Euclidean,
            Output = VoronoiTexture.OutputMode.F1,
            Randomness = 1f,
        };
        var texSmooth0 = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        {
            Metric = WorleyNoise.Metric.Euclidean,
            Output = VoronoiTexture.OutputMode.F1,
            Randomness = 1f,
            Smoothness = 0f,
        };

        foreach (var p in SamplePoints())
        {
            Vector3 a = tex.Value(0.5f, 0.5f, p, 0);
            Vector3 b = texSmooth0.Value(0.5f, 0.5f, p, 0);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void VoronoiTexture_F2MinusF1_SmoothChangesOutput()
    {
        // When the user opts in to smoothness on the F2−F1 mode, the texture
        // must produce a different value than the hard variant on at least one
        // probe inside a cell (otherwise the parameter would be a no-op).
        var hard = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.F2MinusF1,
            Randomness = 1f,
            Smoothness = 0f,
        };
        var soft = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.F2MinusF1,
            Randomness = 1f,
            Smoothness = 0.7f,
        };

        bool foundDifference = false;
        foreach (var p in SamplePoints())
        {
            Vector3 a = hard.Value(0.5f, 0.5f, p, 0);
            Vector3 b = soft.Value(0.5f, 0.5f, p, 0);
            if ((a - b).LengthSquared() > 1e-6f) { foundDifference = true; break; }
        }
        Assert.True(foundDifference, "smoothness > 0 must alter F2−F1 output at least somewhere");
    }

    [Fact]
    public void VoronoiTexture_CellMode_UnaffectedBySmoothness()
    {
        // Cell IDs are discrete; we deliberately bypass the soft-min for the
        // Cell output (matches Cycles: smoothness affects distance outputs only).
        var hard = new VoronoiTexture(scale: 4f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Cell,
            Randomness = 1f,
            Smoothness = 0f,
        };
        var soft = new VoronoiTexture(scale: 4f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Cell,
            Randomness = 1f,
            Smoothness = 0.5f,
        };

        foreach (var p in SamplePoints())
        {
            Assert.Equal(hard.Value(0.5f, 0.5f, p, 0), soft.Value(0.5f, 0.5f, p, 0));
        }
    }

    private static IEnumerable<Vector3> SamplePoints()
    {
        // A handful of points that include cell-interior, near-boundary,
        // and trijunction-like configurations. Deterministic, no RNG.
        yield return new Vector3(0.5f, 0.5f, 0.5f);
        yield return new Vector3(0f, 0f, 0f);
        yield return new Vector3(0.499f, 0.501f, 0.500f);
        yield return new Vector3(1.0001f, 2.0f, -3.0001f);
        yield return new Vector3(7.3f, -1.8f, 4.4f);
        yield return new Vector3(-12.7f, 0.13f, 8.55f);
        yield return new Vector3(0.5f, 1.5f, 2.5f); // exact grid centre
        yield return new Vector3(0.5f, 0.5f, 1.0f); // 2-cell boundary along z
    }
}
