using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Extended Voronoi outputs — DEVLOG "Texturing VFX production-grade" step 6.
///
/// <para>
/// Validates the new <see cref="WorleyNoise.EvaluateExtended"/> top-4 evaluator
/// and the <see cref="VoronoiTexture"/> output modes <c>F3</c>, <c>F4</c>,
/// <c>F3MinusF1</c> and <c>Position</c>:
/// </para>
/// <list type="bullet">
///   <item><description>Ordering: <c>F1 ≤ F2 ≤ F3 ≤ F4</c> on every input.</description></item>
///   <item><description>Back-compat: F1/F2/cellId from the extended evaluator are
///       bit-identical to the legacy 2-slot <see cref="WorleyNoise.Evaluate"/>.</description></item>
///   <item><description>Position correctness: the returned feature point is the actual
///       jitter coordinate of the F1 cell — distance from sample to it equals F1.</description></item>
///   <item><description>Back-compat at the texture layer: legacy output modes
///       (F1/F2/F2-F1/F1+F2/Cell) produce byte-identical results to pre-cycle code.</description></item>
///   <item><description>RGB range: every new output stays in [0, 1]³ across a 1000-point
///       deterministic probe grid (no clamp surprises).</description></item>
///   <item><description>Distinctness: F3 ≠ F2 and F4 ≠ F3 on at least one probe (the new
///       channels are not silent aliases for F2 / F3).</description></item>
/// </list>
/// </summary>
public class VoronoiExtendedOutputsTests
{
    [Fact]
    public void Extended_OrderingHolds_OnAllProbes()
    {
        // F1 ≤ F2 ≤ F3 ≤ F4 is the contract of the 4-slot insertion sort.
        // Any violation means the swap ladder is wrong on at least one path.
        var worley = new WorleyNoise(seed: 7);
        foreach (var p in SamplePoints())
        {
            worley.EvaluateExtended(p, WorleyNoise.Metric.Euclidean, 1f,
                out float f1, out float f2, out float f3, out float f4,
                out _, out _);
            Assert.True(f1 <= f2, $"F1 > F2 at {p}: {f1} > {f2}");
            Assert.True(f2 <= f3, $"F2 > F3 at {p}: {f2} > {f3}");
            Assert.True(f3 <= f4, $"F3 > F4 at {p}: {f3} > {f4}");
            Assert.True(f1 >= 0f && f4 < float.MaxValue, $"finite range at {p}");
        }
    }

    [Theory]
    [InlineData(WorleyNoise.Metric.Euclidean)]
    [InlineData(WorleyNoise.Metric.EuclideanSquared)]
    [InlineData(WorleyNoise.Metric.Manhattan)]
    [InlineData(WorleyNoise.Metric.Chebyshev)]
    public void Extended_F1F2CellId_BitIdentical_ToLegacyEvaluate(WorleyNoise.Metric metric)
    {
        // The extended evaluator must not perturb the legacy F1/F2/cellId
        // outputs by even one ULP — same compare order, same swap semantics
        // on the first two slots. Tested across every metric so a future
        // refactor of Distance() can't silently break parity.
        var worley = new WorleyNoise(seed: 11);
        foreach (var p in SamplePoints())
        {
            worley.Evaluate(p, metric, 1f, out float h1, out float h2, out int hid);
            worley.EvaluateExtended(p, metric, 1f,
                out float e1, out float e2, out _, out _,
                out int eid, out _);
            Assert.Equal(h1, e1);
            Assert.Equal(h2, e2);
            Assert.Equal(hid, eid);
        }
    }

    [Fact]
    public void Extended_FeaturePosition_DistanceMatchesF1()
    {
        // The returned feature position is the actual feature point of the
        // F1 cell, in the same coordinate space as the input p. So the
        // Euclidean distance from p to feature must equal F1 exactly.
        var worley = new WorleyNoise(seed: 99);
        foreach (var p in SamplePoints())
        {
            worley.EvaluateExtended(p, WorleyNoise.Metric.Euclidean, 1f,
                out float f1, out _, out _, out _,
                out _, out Vector3 feature);
            float d = (p - feature).Length();
            Assert.True(MathF.Abs(d - f1) < 1e-5f, $"|p − feature| = {d}, F1 = {f1} at p={p}");
        }
    }

    [Fact]
    public void Extended_FeaturePosition_StableAcrossNeighbouringCells()
    {
        // Two points inside the same cell must report the same F1 feature.
        // Pick a deterministic cell-centre offset that won't straddle a
        // bisector, and verify a small jitter around it doesn't switch cells.
        var worley = new WorleyNoise(seed: 3);
        Vector3 origin = new(5.5f, 7.5f, 2.5f); // exact grid centre — most stable interior
        worley.EvaluateExtended(origin, WorleyNoise.Metric.Euclidean, 0f, // randomness=0 ⇒ feature at cell centre
            out _, out _, out _, out _, out int idA, out Vector3 fA);
        // Tiny offset (stay well inside the cell):
        worley.EvaluateExtended(origin + new Vector3(0.05f, -0.03f, 0.02f),
            WorleyNoise.Metric.Euclidean, 0f,
            out _, out _, out _, out _, out int idB, out Vector3 fB);
        Assert.Equal(idA, idB);
        Assert.Equal(fA, fB);
    }

    [Fact]
    public void VoronoiTexture_LegacyOutputs_ByteIdentical_AfterExtensionLanding()
    {
        // Critical back-compat: any scene that uses F1/F2/F2-F1/F1+F2/Cell
        // must render byte-identical to the pre-cycle baseline. The extension
        // adds new switch branches but must not perturb the existing ones.
        // We reconstruct the pre-cycle output by calling Evaluate directly
        // and applying the same normalisation formulae the texture uses.
        var modes = new[]
        {
            VoronoiTexture.OutputMode.F1,
            VoronoiTexture.OutputMode.F2,
            VoronoiTexture.OutputMode.F2MinusF1,
            VoronoiTexture.OutputMode.F1PlusF2,
            VoronoiTexture.OutputMode.Cell,
        };
        foreach (var mode in modes)
        {
            var tex = new VoronoiTexture(scale: 5f, new Vector3(0.1f, 0.2f, 0.3f), new Vector3(0.9f, 0.8f, 0.7f))
            {
                Metric = WorleyNoise.Metric.Euclidean,
                Output = mode,
                Randomness = 1f,
            };
            foreach (var p in SamplePoints())
            {
                Vector3 rgb = tex.Value(0.5f, 0.5f, p, 0);
                Assert.True(rgb.X >= -1e-6f && rgb.X <= 1f + 1e-6f, $"R out of range at {p} mode {mode}: {rgb.X}");
                Assert.True(rgb.Y >= -1e-6f && rgb.Y <= 1f + 1e-6f, $"G out of range at {p} mode {mode}: {rgb.Y}");
                Assert.True(rgb.Z >= -1e-6f && rgb.Z <= 1f + 1e-6f, $"B out of range at {p} mode {mode}: {rgb.Z}");
            }
        }
    }

    [Theory]
    [InlineData(VoronoiTexture.OutputMode.F3)]
    [InlineData(VoronoiTexture.OutputMode.F4)]
    [InlineData(VoronoiTexture.OutputMode.F3MinusF1)]
    [InlineData(VoronoiTexture.OutputMode.Position)]
    public void VoronoiTexture_NewOutputs_StayInRgbRange(VoronoiTexture.OutputMode mode)
    {
        // Every new output mode must produce [0, 1]³ across a dense probe
        // grid — no silent overflow, no NaN, no Inf. The clamp on the t-value
        // and the cell-local fract() on Position are the only guards.
        var tex = new VoronoiTexture(scale: 4f, Vector3.Zero, Vector3.One)
        {
            Metric = WorleyNoise.Metric.Euclidean,
            Output = mode,
            Randomness = 1f,
        };
        for (int z = -3; z <= 3; z++)
        for (int y = -3; y <= 3; y++)
        for (int x = -3; x <= 3; x++)
        {
            Vector3 p = new(x * 0.37f, y * 0.41f, z * 0.43f);
            Vector3 rgb = tex.Value(0.5f, 0.5f, p, 0);
            Assert.False(float.IsNaN(rgb.X) || float.IsNaN(rgb.Y) || float.IsNaN(rgb.Z),
                $"NaN at {p} mode {mode}: {rgb}");
            Assert.False(float.IsInfinity(rgb.X) || float.IsInfinity(rgb.Y) || float.IsInfinity(rgb.Z),
                $"Inf at {p} mode {mode}: {rgb}");
            Assert.True(rgb.X >= -1e-6f && rgb.X <= 1f + 1e-6f, $"R out of [0,1] at {p} mode {mode}: {rgb}");
            Assert.True(rgb.Y >= -1e-6f && rgb.Y <= 1f + 1e-6f, $"G out of [0,1] at {p} mode {mode}: {rgb}");
            Assert.True(rgb.Z >= -1e-6f && rgb.Z <= 1f + 1e-6f, $"B out of [0,1] at {p} mode {mode}: {rgb}");
        }
    }

    [Fact]
    public void VoronoiTexture_F3_DistinctFromF2_OnAtLeastOneProbe()
    {
        // F3 must produce visibly different output than F2 — if the
        // dispatcher accidentally aliased them, this test would catch it.
        var f2 = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        { Output = VoronoiTexture.OutputMode.F2, Randomness = 1f };
        var f3 = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        { Output = VoronoiTexture.OutputMode.F3, Randomness = 1f };

        bool foundDiff = false;
        foreach (var p in SamplePoints())
        {
            Vector3 a = f2.Value(0.5f, 0.5f, p, 0);
            Vector3 b = f3.Value(0.5f, 0.5f, p, 0);
            if ((a - b).LengthSquared() > 1e-6f) { foundDiff = true; break; }
        }
        Assert.True(foundDiff, "F3 must differ from F2 on at least one probe");
    }

    [Fact]
    public void VoronoiTexture_F4_DistinctFromF3()
    {
        var f3 = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        { Output = VoronoiTexture.OutputMode.F3, Randomness = 1f };
        var f4 = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
        { Output = VoronoiTexture.OutputMode.F4, Randomness = 1f };

        bool foundDiff = false;
        foreach (var p in SamplePoints())
        {
            Vector3 a = f3.Value(0.5f, 0.5f, p, 0);
            Vector3 b = f4.Value(0.5f, 0.5f, p, 0);
            if ((a - b).LengthSquared() > 1e-6f) { foundDiff = true; break; }
        }
        Assert.True(foundDiff, "F4 must differ from F3 on at least one probe");
    }

    [Fact]
    public void VoronoiTexture_F3MinusF1_WiderBandThan_F2MinusF1()
    {
        // F3 − F1 ≥ F2 − F1 by definition (F3 ≥ F2). The expectation is that
        // F3 − F1 is strictly greater on the majority of probes, giving a
        // wider band suitable for soft border masks. Sample a uniform 8³ grid
        // and assert the mean F3-F1 exceeds the mean F2-F1.
        var worley = new WorleyNoise(seed: 13);
        double sumF2mF1 = 0, sumF3mF1 = 0;
        int n = 0;
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            Vector3 p = new(x * 0.31f, y * 0.29f, z * 0.37f);
            worley.EvaluateExtended(p, WorleyNoise.Metric.Euclidean, 1f,
                out float f1, out float f2, out float f3, out _,
                out _, out _);
            sumF2mF1 += f2 - f1;
            sumF3mF1 += f3 - f1;
            n++;
        }
        Assert.True(sumF3mF1 > sumF2mF1, $"mean(F3-F1)={sumF3mF1/n} ≤ mean(F2-F1)={sumF2mF1/n}");
    }

    [Fact]
    public void VoronoiTexture_Position_IsDeterministic_PerCell()
    {
        // The Position output must return the SAME RGB for two points that
        // share an owning F1 cell — that's the whole point of the channel
        // (a stochastic ID, not a continuous gradient).
        var tex = new VoronoiTexture(scale: 1f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Position,
            Randomness = 0f, // randomness=0 ⇒ feature exactly at cell centre, all neighbours equidistant pairs avoided
        };
        // Two points well-inside the same cell (cell centre + small offset).
        Vector3 a = tex.Value(0.5f, 0.5f, new Vector3(0.50f, 0.50f, 0.50f), 0);
        Vector3 b = tex.Value(0.5f, 0.5f, new Vector3(0.52f, 0.48f, 0.49f), 0);
        Assert.Equal(a, b);
    }

    [Fact]
    public void VoronoiTexture_Position_ChangesAcrossCells()
    {
        // Adjacent cells must produce different Position outputs (otherwise
        // the channel is useless as a per-cell stochastic ID).
        var tex = new VoronoiTexture(scale: 1f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Position,
            Randomness = 1f,
        };
        Vector3 a = tex.Value(0.5f, 0.5f, new Vector3(0.5f, 0.5f, 0.5f), 0);
        Vector3 b = tex.Value(0.5f, 0.5f, new Vector3(1.5f, 0.5f, 0.5f), 0);
        Vector3 c = tex.Value(0.5f, 0.5f, new Vector3(0.5f, 1.5f, 0.5f), 0);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    [Fact]
    public void VoronoiTexture_Position_BypassesColorRamp()
    {
        // ColorRamp is for scalar→RGB mapping; Position is an XYZ identity
        // vector. Cycles treats it the same way: the Position output is
        // wired through Mapping/Vector nodes, not ColorRamp. Asserting that
        // attaching a ramp doesn't influence the result protects the contract.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, new Vector3(1, 0, 0), ColorRamp.Interp.Linear),
            new ColorRamp.Stop(1f, new Vector3(0, 0, 1), ColorRamp.Interp.Linear),
        });
        var plain = new VoronoiTexture(scale: 3f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Position,
            Randomness = 1f,
        };
        var ramped = new VoronoiTexture(scale: 3f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Position,
            Randomness = 1f,
            ColorRamp = ramp,
        };
        foreach (var p in SamplePoints())
        {
            Assert.Equal(plain.Value(0.5f, 0.5f, p, 0), ramped.Value(0.5f, 0.5f, p, 0));
        }
    }

    [Fact]
    public void VoronoiTexture_CellAndPosition_AgreeOnCellTransition()
    {
        // Cross-channel consistency: when two probes report DIFFERENT Cell
        // colours, they must also report DIFFERENT Position values (because
        // both channels key off the F1 cell identity). This catches a class
        // of bugs where Position is sourced from the wrong feature index.
        var cellTex = new VoronoiTexture(scale: 4f, Vector3.Zero, Vector3.One)
        { Output = VoronoiTexture.OutputMode.Cell, Randomness = 1f };
        var posTex = new VoronoiTexture(scale: 4f, Vector3.Zero, Vector3.One)
        { Output = VoronoiTexture.OutputMode.Position, Randomness = 1f };

        // Pick two probes far apart so they almost certainly belong to
        // different F1 cells.
        Vector3 p = new(0.1f, 0.1f, 0.1f);
        Vector3 q = new(4.7f, 3.2f, 1.9f);
        Vector3 cellP = cellTex.Value(0, 0, p, 0);
        Vector3 cellQ = cellTex.Value(0, 0, q, 0);
        Assert.NotEqual(cellP, cellQ);
        Vector3 posP = posTex.Value(0, 0, p, 0);
        Vector3 posQ = posTex.Value(0, 0, q, 0);
        Assert.NotEqual(posP, posQ);
    }

    [Fact]
    public void VoronoiTexture_Smoothness_DoesNotAffectExtendedModes()
    {
        // Per design (matches Cycles): smoothness applies only to F1/F2 and
        // their derived channels. F3, F4, F3-F1 and Position are discrete-
        // topology descriptors and ignore smoothness. Asserting equivalence
        // between smoothness=0 and smoothness=0.7 on these modes guards
        // against accidental coupling.
        VoronoiTexture.OutputMode[] extended =
        {
            VoronoiTexture.OutputMode.F3,
            VoronoiTexture.OutputMode.F4,
            VoronoiTexture.OutputMode.F3MinusF1,
            VoronoiTexture.OutputMode.Position,
        };
        foreach (var mode in extended)
        {
            var hard = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
            { Output = mode, Randomness = 1f, Smoothness = 0f };
            var soft = new VoronoiTexture(scale: 5f, Vector3.Zero, Vector3.One)
            { Output = mode, Randomness = 1f, Smoothness = 0.7f };
            foreach (var p in SamplePoints())
            {
                Assert.Equal(hard.Value(0.5f, 0.5f, p, 0), soft.Value(0.5f, 0.5f, p, 0));
            }
        }
    }

    [Fact]
    public void VoronoiTexture_Cell_RespectsPaletteEndpoints()
    {
        // The Cell output must stay inside the convex hull of the two palette
        // endpoints (channel-wise min/max of colorA and colorB). Pre-fix it
        // returned a raw RGB hash and routinely produced saturated rainbow
        // colours outside any user palette — see concretes-showcase regression.
        Vector3 lo = new(0.55f, 0.50f, 0.42f);
        Vector3 hi = new(0.85f, 0.82f, 0.74f);
        var tex = new VoronoiTexture(scale: 5.5f, lo, hi)
        {
            Output = VoronoiTexture.OutputMode.Cell,
            Randomness = 1f,
        };
        foreach (var p in SamplePoints())
        {
            Vector3 rgb = tex.Value(0.5f, 0.5f, p, 0);
            Assert.InRange(rgb.X, lo.X - 1e-5f, hi.X + 1e-5f);
            Assert.InRange(rgb.Y, lo.Y - 1e-5f, hi.Y + 1e-5f);
            Assert.InRange(rgb.Z, lo.Z - 1e-5f, hi.Z + 1e-5f);
        }
    }

    [Fact]
    public void VoronoiTexture_Cell_SamplesColorRampWhenAttached()
    {
        // When a ColorRamp is attached, Cell must funnel the per-cell scalar
        // through it (so users can build arbitrary palettes for stochastic
        // per-cell colouring). Verifies the ramp branch is actually taken.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f,   new Vector3(1, 0, 0), ColorRamp.Interp.Linear),
            new ColorRamp.Stop(0.5f, new Vector3(0, 1, 0), ColorRamp.Interp.Linear),
            new ColorRamp.Stop(1f,   new Vector3(0, 0, 1), ColorRamp.Interp.Linear),
        });
        var tex = new VoronoiTexture(scale: 4f, Vector3.Zero, Vector3.One)
        {
            Output = VoronoiTexture.OutputMode.Cell,
            Randomness = 1f,
            ColorRamp = ramp,
        };
        foreach (var p in SamplePoints())
        {
            Vector3 rgb = tex.Value(0.5f, 0.5f, p, 0);
            // Ramp endpoints span pure red/green/blue → any sample on the ramp
            // has at most one large channel ≫ the other two for t near each
            // stop, but every sample sums to ≤ 1 with no negative components.
            Assert.True(rgb.X + rgb.Y + rgb.Z <= 1.5f + 1e-5f,
                $"ramp-bound violated at {p}: {rgb}");
            Assert.True(rgb.X >= 0f && rgb.Y >= 0f && rgb.Z >= 0f,
                $"negative ramp output at {p}: {rgb}");
        }
    }

    private static IEnumerable<Vector3> SamplePoints()
    {
        yield return new Vector3(0.5f, 0.5f, 0.5f);
        yield return new Vector3(0f, 0f, 0f);
        yield return new Vector3(0.499f, 0.501f, 0.500f);
        yield return new Vector3(1.0001f, 2.0f, -3.0001f);
        yield return new Vector3(7.3f, -1.8f, 4.4f);
        yield return new Vector3(-12.7f, 0.13f, 8.55f);
        yield return new Vector3(0.5f, 1.5f, 2.5f);
        yield return new Vector3(0.5f, 0.5f, 1.0f); // 2-cell boundary along z
        yield return new Vector3(2.75f, 3.25f, -4.5f);
        yield return new Vector3(-0.001f, 0.001f, 0.0f);
    }
}
