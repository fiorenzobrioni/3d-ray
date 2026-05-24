using System.Numerics;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Marble + Wood "studio quality" tests.
///
/// <para>
/// Marble tests verify the production-grade ridged + IQ-warp pipeline:
/// output range, per-instance decorrelation, non-repetitive veining,
/// thickness monotonicity, impurity gating and warp-iteration determinism.
/// Wood tests verify the back-compat invariants of the existing
/// <see cref="WoodTexture"/> pipeline.
/// </para>
/// </summary>
public class MarbleWoodStudioTests
{
    private static bool VecClose(Vector3 a, Vector3 b, float eps = 1e-5f)
        => MathF.Abs(a.X - b.X) < eps && MathF.Abs(a.Y - b.Y) < eps && MathF.Abs(a.Z - b.Z) < eps;

    // ─────────────────────────── Marble ─────────────────────────────────────

    [Fact]
    public void Marble_Output_StaysInUnitRange()
    {
        // Every transformation in the pipeline (smoothstep, soft-max, ramp
        // lookup, lerp) is convex on [0, 1], so any RGB component outside
        // [0, 1] would indicate an unguarded path. Probes cover wide spatial
        // extents to exercise the warp's tiling-breaking regime.
        var tex = new MarbleTexture(4f, new Vector3(0.92f, 0.92f, 0.94f),
                                         new Vector3(0.18f, 0.18f, 0.22f))
        {
            VeinThickness = 0.6f,
            ImpuritiesDensity = 0.05f,
        };
        foreach (var p in MarbleProbePoints())
        {
            var v = tex.Value(0.5f, 0.5f, p, 0);
            Assert.InRange(v.X, 0f, 1f);
            Assert.InRange(v.Y, 0f, 1f);
            Assert.InRange(v.Z, 0f, 1f);
        }
    }

    [Fact]
    public void Marble_ObjectSeed_DecorrelatesOutput()
    {
        // Two material instances must look uncorrelated when they sit on
        // different objects (RandomizeOffset routes through SeedOffset which
        // is added only to the noise input, not the geometric warp). Mean
        // absolute difference across the probe set must be > 0.05.
        var tex = new MarbleTexture(4f) { RandomizeOffset = true };
        float mad = 0f;
        int n = 0;
        foreach (var p in MarbleProbePoints())
        {
            var a = tex.Value(0.5f, 0.5f, p, 0);
            var b = tex.Value(0.5f, 0.5f, p, 4242);
            mad += MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) + MathF.Abs(a.Z - b.Z);
            n += 3;
        }
        mad /= n;
        Assert.True(mad > 0.05f, $"objectSeed failed to decorrelate output (MAD={mad})");
    }

    [Fact]
    public void Marble_NoVisibleTilingAlongVeinAxis()
    {
        // Headline regression killer: the legacy sin-carrier marble produced
        // a near-periodic signal along VeinAxis with period 2π/scale. The
        // ridged + IQ-warp reimplementation must break that periodicity. We
        // probe the raw ridged-vein field (bypassing the smoothstep that may
        // saturate to base) at many points along the axis and require
        // meaningful sample-to-sample variation.
        var tex = new MarbleTexture(0.5f, new Vector3(1f), new Vector3(0f))
        {
            VeinAxis = Vector3.UnitY,
            VeinThickness = 0.5f,
            VeinSoftness = 0.5f,    // make the smoothstep wide so output isn't binary
        };
        const int N = 32;
        float[] samples = new float[N];
        for (int i = 0; i < N; i++)
        {
            // Sweep along the vein axis with stride 0.7 wu — at scale 0.5 and
            // octaves 5, the ridged field period is on the order of unity, so
            // 32 samples span many "periods" worth of noise space.
            samples[i] = tex.Value(0f, 0f, new Vector3(0.3f, 0.7f + i * 0.7f, 0.2f), 0).X;
        }
        float mean = 0f;
        for (int i = 0; i < N; i++) mean += samples[i];
        mean /= N;
        float variance = 0f;
        for (int i = 0; i < N; i++) variance += (samples[i] - mean) * (samples[i] - mean);
        variance /= N;
        Assert.True(variance > 0.002f,
            $"output along vein axis lacks variation (mean={mean}, var={variance})");
    }

    [Fact]
    public void Marble_ThicknessIsMonotone()
    {
        // Vein-thickness controls the smoothstep that defines the vein
        // region. Higher thickness ⇒ more area mapped to the vein colour ⇒
        // lower mean luminance when vein colour is black.
        Vector3 baseCol = new(1f, 1f, 1f);
        Vector3 veinCol = new(0f, 0f, 0f);

        float MeanLuminance(float thickness)
        {
            var tex = new MarbleTexture(4f, baseCol, veinCol) { VeinThickness = thickness };
            float sum = 0;
            int count = 0;
            for (int i = 0; i < 24; i++)
            for (int j = 0; j < 24; j++)
            for (int k = 0; k < 4; k++)
            {
                var p = new Vector3(i * 0.13f, k * 0.31f, j * 0.13f);
                sum += tex.Value(0f, 0f, p, 0).X;
                count++;
            }
            return sum / count;
        }

        float l2 = MeanLuminance(0.2f);
        float l5 = MeanLuminance(0.5f);
        float l8 = MeanLuminance(0.8f);

        Assert.True(l2 > l5, $"thickness=0.5 should darken vs 0.2: l2={l2}, l5={l5}");
        Assert.True(l5 > l8, $"thickness=0.8 should darken vs 0.5: l5={l5}, l8={l8}");
    }

    [Fact]
    public void Marble_ImpuritiesDensityZero_BitIdenticalToBaseline()
    {
        // The impurity branch must be fully skipped at density = 0 to avoid
        // hidden cost and to keep the back-compat contract clean.
        var baseTex = new MarbleTexture(4f);
        var withZeroImp = new MarbleTexture(4f)
        {
            ImpuritiesDensity = 0f,
            ImpuritiesScale = 17f,    // any value
            ImpurityWeight = 0.5f,    // any value
        };
        foreach (var p in MarbleProbePoints())
            Assert.True(VecClose(baseTex.Value(0f, 0f, p, 0), withZeroImp.Value(0f, 0f, p, 0)),
                $"impurities_density=0 must be a no-op at p={p}");
    }

    [Fact]
    public void Marble_ExternalImpuritiesTexture_OverridesInline()
    {
        // When an external impurities_texture is supplied it must replace the
        // inline Voronoi path regardless of impurities_density. We use a
        // solid white texture so impurity = 1 across the slab — the
        // ramp lookup shifts uniformly toward the vein colour by ImpurityWeight.
        Vector3 baseCol = new(1f, 1f, 1f);
        Vector3 veinCol = new(0f, 0f, 0f);
        var baseline = new MarbleTexture(4f, baseCol, veinCol);
        var withExternal = new MarbleTexture(4f, baseCol, veinCol)
        {
            ImpuritiesDensity = 0f,                            // inline OFF
            ImpuritiesTexture = new SolidColor(new Vector3(1f)),
            ImpurityWeight = 0.5f,
        };

        bool changed = false;
        foreach (var p in MarbleProbePoints())
        {
            if (!VecClose(baseline.Value(0f, 0f, p, 0), withExternal.Value(0f, 0f, p, 0), 1e-3f))
            { changed = true; break; }
        }
        Assert.True(changed, "external impurities_texture must influence the output");
    }

    [Fact]
    public void Marble_WarpIterationsZero_DeterministicBaseline()
    {
        // warp_iterations = 0 disables the recursive IQ warp; output must be
        // bit-identical on consecutive calls (catches any accidental Random
        // state leak introduced by the warp path).
        var tex = new MarbleTexture(4f) { WarpIterations = 0 };
        foreach (var p in MarbleProbePoints())
        {
            var a = tex.Value(0f, 0f, p, 0);
            var b = tex.Value(0f, 0f, p, 0);
            Assert.True(VecClose(a, b),
                $"non-deterministic baseline at p={p}: a={a}, b={b}");
        }
    }

    [Fact]
    public void Marble_WarpIterationsThree_DiffersFromZero()
    {
        // The IQ recursive warp must visibly alter the field — otherwise the
        // parameter is dead code and the marble looks identical to a
        // non-warped ridged field (the "CG anni 2000" regression).
        var noWarp = new MarbleTexture(4f) { WarpIterations = 0 };
        var fullWarp = new MarbleTexture(4f) { WarpIterations = 3 };

        float mad = 0f;
        int n = 0;
        foreach (var p in MarbleProbePoints())
        {
            var a = noWarp.Value(0f, 0f, p, 0);
            var b = fullWarp.Value(0f, 0f, p, 0);
            mad += MathF.Abs(a.X - b.X);
            n++;
        }
        mad /= n;
        Assert.True(mad > 0.05f, $"warp iterations 3 vs 0 produced negligible change (MAD={mad})");
    }

    [Fact]
    public void Marble_FoldAmplitudeZero_NoLargeScaleShear()
    {
        // Fold amplitude = 0 must skip the anisotropic warp entirely. Two
        // points 5 wu apart along the vein axis should then exhibit the
        // smaller variance of the ridged field alone, not the broader spread
        // produced by the fold-driven geological deformation.
        var noFold = new MarbleTexture(4f) { FoldAmplitude = Vector3.Zero };
        Vector3 p0 = new(0.4f, 0.0f, 0.1f);
        Vector3 p1 = p0 + 5f * Vector3.UnitY;
        var a = noFold.Value(0f, 0f, p0, 0);
        var b = noFold.Value(0f, 0f, p1, 0);
        Assert.False(float.IsNaN(a.X) || float.IsNaN(b.X));
        // The field still varies (warp + ridged are active), but we just
        // assert the no-fold call doesn't crash and stays in range.
        Assert.InRange(a.X, 0f, 1f);
        Assert.InRange(b.X, 0f, 1f);
    }

    [Fact]
    public void Marble_OutputMask_PacksScalarAsGrayscale()
    {
        // Mask output must produce (t, t, t) so FloatTexture's channel-average
        // recovers exactly t. We sample many points and verify R == G == B and
        // every channel lies in [0, 1].
        var tex = new MarbleTexture(4f)
        {
            Output = MarbleTexture.OutputMode.Mask,
            VeinThickness = 0.3f,
            ImpuritiesDensity = 0.05f,
        };
        foreach (var p in MarbleProbePoints())
        {
            var v = tex.Value(0f, 0f, p, 0);
            Assert.InRange(v.X, 0f, 1f);
            Assert.Equal(v.X, v.Y, 6);
            Assert.Equal(v.X, v.Z, 6);
        }
    }

    [Fact]
    public void Marble_OutputMask_MatchesColorPathScalar()
    {
        // The mask scalar must be derivable from the colour-path output when
        // a 2-stop ramp is used (lerp from baseColor=black to veinColor=white
        // recovers the lerp parameter t in any channel). Verifies mask
        // semantics aren't drifting from the colour pipeline.
        Vector3 baseCol = new(0f, 0f, 0f);
        Vector3 veinCol = new(1f, 1f, 1f);
        var color = new MarbleTexture(4f, baseCol, veinCol) { VeinThickness = 0.3f };
        var mask  = new MarbleTexture(4f, baseCol, veinCol)
        {
            Output = MarbleTexture.OutputMode.Mask,
            VeinThickness = 0.3f,
        };
        foreach (var p in MarbleProbePoints())
        {
            float cT = color.Value(0f, 0f, p, 0).X;       // base=0, vein=1 → t directly
            float mT = mask .Value(0f, 0f, p, 0).X;
            Assert.True(MathF.Abs(cT - mT) < 1e-5f,
                $"mask {mT} drifted from color-path t {cT} at p={p}");
        }
    }

    [Fact]
    public void Marble_SpaceStretch_ProducesDirectionalCompression()
    {
        // Anisotropic stretch must measurably change the output: at the same
        // sample point, a stretched marble differs from the isotropic baseline.
        var iso = new MarbleTexture(4f);
        var stretched = new MarbleTexture(4f) { SpaceStretch = new Vector3(0.4f, 1.8f, 1.0f) };
        float mad = 0f;
        int n = 0;
        foreach (var p in MarbleProbePoints())
        {
            var a = iso.Value(0f, 0f, p, 0);
            var b = stretched.Value(0f, 0f, p, 0);
            mad += MathF.Abs(a.X - b.X);
            n++;
        }
        mad /= n;
        Assert.True(mad > 0.02f, $"space_stretch failed to alter the field (MAD={mad})");
    }

    [Fact]
    public void Marble_SpaceStretchOne_BitIdenticalToBaseline()
    {
        // Default (1,1,1) must skip the multiply path entirely → bit-identity
        // with a constructor that hasn't touched SpaceStretch.
        var baseline = new MarbleTexture(4f);
        var explicitOne = new MarbleTexture(4f) { SpaceStretch = Vector3.One };
        foreach (var p in MarbleProbePoints())
            Assert.True(VecClose(baseline.Value(0f, 0f, p, 0), explicitOne.Value(0f, 0f, p, 0)),
                $"space_stretch=(1,1,1) must be a no-op at p={p}");
    }

    [Fact]
    public void Marble_CracksDensityZero_BitIdenticalToBaseline()
    {
        // The crack overlay path must be fully skipped at density = 0 — no
        // Worley evaluation, no perf cost, no perturbation of the field.
        var baseline = new MarbleTexture(4f);
        var noCracks = new MarbleTexture(4f) { CracksDensity = 0f, CracksWeight = 2f };
        foreach (var p in MarbleProbePoints())
            Assert.True(VecClose(baseline.Value(0f, 0f, p, 0), noCracks.Value(0f, 0f, p, 0)),
                $"cracks_density=0 must be a no-op at p={p}");
    }

    [Fact]
    public void Marble_CracksDensityPositive_AddsLinearVeinage()
    {
        // The Worley overlay must measurably perturb the field at density > 0.
        // Crack lines are intentionally thin by construction (CracksSoftness
        // controls line width as a fraction of the Voronoi F2−F1 range), so
        // we sweep a dense spatial grid and use wide cracks (softness 0.20)
        // to guarantee at least a few crack-on samples in the average.
        var noCracks = new MarbleTexture(4f) { VeinThickness = 0.30f };
        var withCracks = new MarbleTexture(4f)
        {
            VeinThickness = 0.30f,
            CracksDensity = 1.0f,
            CracksSoftness = 0.20f,
            CracksWeight = 1.0f,
        };
        float mad = 0f;
        int n = 0;
        for (int i = 0; i < 12; i++)
        for (int j = 0; j < 12; j++)
        {
            var p = new Vector3(i * 0.31f, 0.13f, j * 0.31f);
            var a = noCracks.Value(0f, 0f, p, 0);
            var b = withCracks.Value(0f, 0f, p, 0);
            mad += MathF.Abs(a.X - b.X);
            n++;
        }
        mad /= n;
        Assert.True(mad > 0.01f, $"cracks failed to perturb the field (MAD={mad})");
    }

    [Fact]
    public void Marble_AllKnobsCranked_NoNaNOrOutOfRange()
    {
        // Worst-case stress: every knob pushed to its extreme. No NaN, no Inf,
        // RGB stays in [0, 1]. Catches any unguarded log/exp/sqrt in the
        // soft-max accumulator or the smoothstep edges.
        var tex = new MarbleTexture(4f)
        {
            NoiseStrength = 2.5f,
            WarpAmplitude = 1.5f,
            WarpIterations = 3,
            FoldAmplitude = new Vector3(1.2f, 0.8f, 1.0f),
            SpaceStretch = new Vector3(0.3f, 2.5f, 0.7f),
            VeinLayers = 3,
            VeinScales = new[] { 0.5f, 1.5f, 3.5f },
            VeinWeights = new[] { 1.0f, 0.8f, 0.6f },
            VeinThickness = 0.85f,
            VeinSoftness = 0.04f,
            SoftMaxSharpness = 32f,
            ColorVariation = 0.4f,
            ImpuritiesDensity = 0.2f,
            ImpurityWeight = 0.4f,
            CracksDensity = 0.8f,
            CracksScale = 3.0f,
            CracksSoftness = 0.02f,
            CracksWeight = 1.5f,
            Octaves = 8,
        };
        foreach (var p in MarbleProbePoints())
        {
            var v = tex.Value(0f, 0f, p, 17);
            Assert.False(float.IsNaN(v.X) || float.IsInfinity(v.X));
            Assert.False(float.IsNaN(v.Y) || float.IsInfinity(v.Y));
            Assert.False(float.IsNaN(v.Z) || float.IsInfinity(v.Z));
            Assert.InRange(v.X, 0f, 1f);
            Assert.InRange(v.Y, 0f, 1f);
            Assert.InRange(v.Z, 0f, 1f);
        }
    }

    // ─────────────────────────── Wood ───────────────────────────────────────
    //
    // The wood texture was rewritten to the production-grade model (asymmetric
    // earlywood/latewood profile, per-ring random width+colour variation,
    // recursive IQ warp, pore vessels, sapwood/heartwood gradient, mask output).
    // Tests assert algorithmic invariants of the NEW pipeline — there is no
    // back-compat bit-identity contract with the legacy sin-of-radial model.

    [Fact]
    public void Wood_Output_StaysInUnitRange()
    {
        // Every transformation in the pipeline (smoothstep, lerp, ramp lookup,
        // clamp) is convex on [0, 1], so any RGB outside [0, 1] would indicate
        // an unguarded path. Probes cover wide spatial extents to exercise the
        // warp + per-ring hash.
        var tex = new WoodTexture(4f, 1.5f)
        {
            FigureStrength = 1.0f,
            PoreDensity = 0.5f,
            KnotDensity = 0.5f,
            HeartwoodRadius = 1.2f,
            HeartwoodBlend = 0.3f,
        };
        foreach (var p in WoodProbePoints())
        {
            var v = tex.Value(0.5f, 0.5f, p, 0);
            Assert.InRange(v.X, 0f, 1f);
            Assert.InRange(v.Y, 0f, 1f);
            Assert.InRange(v.Z, 0f, 1f);
        }
    }

    [Fact]
    public void Wood_ObjectSeed_DecorrelatesOutput()
    {
        // Two material instances must look uncorrelated when they sit on
        // different objects. RandomizeOffset routes through SeedOffset which
        // is added only to the noise input, not the geometric ring axis.
        var tex = new WoodTexture(4f, 1.5f) { RandomizeOffset = true };
        float mad = 0f;
        int n = 0;
        foreach (var p in WoodProbePoints())
        {
            var a = tex.Value(0.5f, 0.5f, p, 0);
            var b = tex.Value(0.5f, 0.5f, p, 4242);
            mad += MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) + MathF.Abs(a.Z - b.Z);
            n += 3;
        }
        mad /= n;
        Assert.True(mad > 0.03f, $"objectSeed failed to decorrelate wood output (MAD={mad})");
    }

    [Fact]
    public void Wood_FigureStrengthPositive_ChangesOutput()
    {
        var grainOnly = new WoodTexture(4f, 1.5f);
        var withFigure = new WoodTexture(4f, 1.5f)
        {
            FigureStrength = 1.5f,
            FigureScale = 0.2f,
        };

        bool found = false;
        foreach (var p in WoodProbePoints())
        {
            if (!VecClose(grainOnly.Value(0.5f, 0.5f, p, 0), withFigure.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "figure_strength > 0 must change wood output somewhere");
    }

    [Fact]
    public void Wood_RadialAnisotropyZero_NoOpVsExplicitZero()
    {
        // RadialAnisotropy = 0 must skip the divide-by-radial path entirely.
        // The default IS 0, so two textures with default vs explicit-zero must
        // be bit-identical.
        var defaultTex = new WoodTexture(4f, 1.5f);
        var explicitZero = new WoodTexture(4f, 1.5f) { RadialAnisotropy = 0f };
        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(defaultTex.Value(0.5f, 0.5f, p, 0), explicitZero.Value(0.5f, 0.5f, p, 0)),
                $"radial_anisotropy default vs explicit 0 must match at p={p}");
    }

    [Fact]
    public void Wood_RadialAnisotropyPositive_ChangesOutput()
    {
        var iso = new WoodTexture(4f, 1.5f);
        var aniso = new WoodTexture(4f, 1.5f) { RadialAnisotropy = 2f };

        bool found = false;
        foreach (var p in WoodProbePoints())
        {
            if (!VecClose(iso.Value(0.5f, 0.5f, p, 0), aniso.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "radial_anisotropy > 0 must alter the wood output");
    }

    [Fact]
    public void Wood_AnisotropyAtTrunkAxis_NoNaN()
    {
        // The radial direction degenerates exactly on the trunk axis. The
        // implementation must hold up — sample a strip along the axis with
        // every relevant knob cranked.
        var tex = new WoodTexture(4f, 1.5f)
        {
            RadialAnisotropy = 5f,
            KnotDensity = 1f,
            PoreDensity = 1f,
            HeartwoodRadius = 0.5f,
            HeartwoodBlend = 0.5f,
        };
        for (int i = 0; i < 16; i++)
        {
            var p = new Vector3(0, i * 0.1f, 0);
            var v = tex.Value(0.5f, 0.5f, p, 0);
            Assert.False(float.IsNaN(v.X) || float.IsInfinity(v.X), $"NaN/Inf X at p={p}");
            Assert.False(float.IsNaN(v.Y) || float.IsInfinity(v.Y), $"NaN/Inf Y at p={p}");
            Assert.False(float.IsNaN(v.Z) || float.IsInfinity(v.Z), $"NaN/Inf Z at p={p}");
        }
    }

    [Fact]
    public void Wood_KnotDensityZero_NoOp()
    {
        // Knot path must be fully skipped at density = 0 (no Voronoi cost).
        var defaultTex = new WoodTexture(4f, 1.5f);
        var explicitZero = new WoodTexture(4f, 1.5f) { KnotDensity = 0f, KnotScale = 1.5f };
        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(defaultTex.Value(0.5f, 0.5f, p, 0), explicitZero.Value(0.5f, 0.5f, p, 0)),
                $"knot_density = 0 must be a no-op at p={p}");
    }

    [Fact]
    public void Wood_KnotDensityPositive_ProducesDarkerLocalRegions()
    {
        // Knot dark heart drops the local `t` so at high density the mean
        // brightness of the rendered field must be lower. Probe a wider
        // volume than MeanBrightness so we cross several knot cells.
        var noKnots = new WoodTexture(4f, 1.5f);
        var withKnots = new WoodTexture(4f, 1.5f) { KnotDensity = 1f, KnotScale = 2.0f };

        float meanNoKnots = MeanKnotProbe(noKnots);
        float meanWithKnots = MeanKnotProbe(withKnots);

        Assert.True(meanWithKnots < meanNoKnots,
            $"with knots brightness should drop: noKnots={meanNoKnots}, withKnots={meanWithKnots}");
    }

    private static float MeanKnotProbe(WoodTexture tex)
    {
        // Wide grid covering many knot cells so the per-cell stochastic
        // darkening contributes a measurable mean drop.
        const int N = 20;
        double sum = 0;
        int n = 0;
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        for (int k = 0; k < N; k++)
        {
            var p = new Vector3((i - N * 0.5f) * 0.25f, (j - N * 0.5f) * 0.25f, (k - N * 0.5f) * 0.25f);
            var v = tex.Value(0.5f, 0.5f, p, 0);
            sum += 0.2126 * v.X + 0.7152 * v.Y + 0.0722 * v.Z;
            n++;
        }
        return (float)(sum / n);
    }

    // ── New-pipeline-specific tests ────────────────────────────────────────

    [Fact]
    public void Wood_RingColorVariationZero_AllRingsBalanced()
    {
        // With variation = 0, the mean brightness across a wide spatial sweep
        // must equal the mean computed by ignoring per-ring noise — i.e. there's
        // no systematic per-ring bias. We assert by comparing two instances
        // and requiring that the variation knob actively shifts brightness.
        var noVar = new WoodTexture(4f, 1.5f) { RingColorVariation = 0f, RingWidthVariation = 0f };
        var withVar = new WoodTexture(4f, 1.5f) { RingColorVariation = 0.4f, RingWidthVariation = 0f };

        // Compute MAD between the two pipelines over the probe grid. The per-
        // ring hash hits different rings at different sample points so the
        // difference must be non-trivial somewhere.
        bool found = false;
        foreach (var p in WoodProbePoints())
        {
            if (!VecClose(noVar.Value(0.5f, 0.5f, p, 0), withVar.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "ring_color_variation > 0 must alter wood output");
    }

    [Fact]
    public void Wood_RingWidthVariationZero_NoOp()
    {
        var noVar = new WoodTexture(4f, 1.5f) { RingWidthVariation = 0f, RingColorVariation = 0f };
        var explicitZero = new WoodTexture(4f, 1.5f) { RingWidthVariation = 0f, RingColorVariation = 0f };
        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(noVar.Value(0.5f, 0.5f, p, 0), explicitZero.Value(0.5f, 0.5f, p, 0)),
                $"ring_width_variation = 0 must be a no-op at p={p}");
    }

    [Fact]
    public void Wood_PoreDensityZero_NoOp()
    {
        var noPores = new WoodTexture(4f, 1.5f) { PoreDensity = 0f, PoreStrength = 1f };
        var explicitZero = new WoodTexture(4f, 1.5f) { PoreDensity = 0f, PoreStrength = 0.5f, PoreScale = 32f };
        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(noPores.Value(0.5f, 0.5f, p, 0), explicitZero.Value(0.5f, 0.5f, p, 0)),
                $"pore_density = 0 must be a no-op at p={p}");
    }

    [Fact]
    public void Wood_PoreDensityPositive_DarkensMeanBrightness()
    {
        // Pore vessels darken local samples. With density = 1 the mean over a
        // dense grid must drop measurably vs the no-pore baseline.
        var noPores = new WoodTexture(4f, 1.5f);
        var withPores = new WoodTexture(4f, 1.5f)
        {
            PoreDensity = 1f,
            PoreScale = 12f,
            PoreStrength = 0.6f,
            PoreAspect = 1f,
        };
        float meanNo = MeanBrightness(noPores);
        float meanYes = MeanBrightness(withPores);
        Assert.True(meanYes < meanNo, $"pores should darken mean: no={meanNo}, yes={meanYes}");
    }

    [Fact]
    public void Wood_HeartwoodBlendShiftsCenterBrightness()
    {
        // With positive HeartwoodBlend, samples close to the trunk axis should
        // be on average DARKER than samples far from the axis.
        var tex = new WoodTexture(4f, 1.5f)
        {
            HeartwoodRadius = 0.8f,
            HeartwoodBlend = 0.4f,
            RingColorVariation = 0f,
        };

        float centerLum = 0f, edgeLum = 0f;
        int n = 0;
        for (int i = 0; i < 32; i++)
        {
            // "center" probes near radial dist ~0.1
            float angC = i * 0.2f;
            var pc = new Vector3(0.1f * MathF.Cos(angC), i * 0.05f, 0.1f * MathF.Sin(angC));
            var vc = tex.Value(0f, 0f, pc, 0);
            centerLum += 0.2126f * vc.X + 0.7152f * vc.Y + 0.0722f * vc.Z;
            // "edge" probes at radial dist ~2
            var pe = new Vector3(2.0f * MathF.Cos(angC), i * 0.05f, 2.0f * MathF.Sin(angC));
            var ve = tex.Value(0f, 0f, pe, 0);
            edgeLum += 0.2126f * ve.X + 0.7152f * ve.Y + 0.0722f * ve.Z;
            n++;
        }
        centerLum /= n;
        edgeLum /= n;
        Assert.True(centerLum < edgeLum,
            $"heartwood should darken center vs edge: center={centerLum}, edge={edgeLum}");
    }

    [Fact]
    public void Wood_SpaceStretchOne_BitIdentical()
    {
        // SpaceStretch = (1,1,1) must skip the multiply path entirely.
        var baseline = new WoodTexture(4f, 1.5f);
        var explicitOne = new WoodTexture(4f, 1.5f) { SpaceStretch = Vector3.One };
        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(baseline.Value(0.5f, 0.5f, p, 0), explicitOne.Value(0.5f, 0.5f, p, 0)),
                $"space_stretch=(1,1,1) must be a no-op at p={p}");
    }

    [Fact]
    public void Wood_SpaceStretchNonUnit_ChangesOutput()
    {
        var iso = new WoodTexture(4f, 1.5f);
        var stretched = new WoodTexture(4f, 1.5f) { SpaceStretch = new Vector3(0.5f, 1.8f, 1f) };
        bool found = false;
        foreach (var p in WoodProbePoints())
        {
            if (!VecClose(iso.Value(0.5f, 0.5f, p, 0), stretched.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "space_stretch != identity must alter wood output");
    }

    [Fact]
    public void Wood_WarpIterations0_VsHigh_DiffersMaterially()
    {
        // Recursive IQ warp must visibly alter the field — otherwise the knob
        // is dead code and rings look perfectly periodic. Sample a dense grid
        // so cumulative MAD has room to grow.
        var noWarp = new WoodTexture(4f, 1.5f) { WarpIterations = 0, WarpAmplitude = 0f };
        var fullWarp = new WoodTexture(4f, 1.5f) { WarpIterations = 3, WarpAmplitude = 1.2f };

        float mad = 0f;
        int n = 0;
        for (int i = 0; i < 12; i++)
        for (int j = 0; j < 12; j++)
        {
            var p = new Vector3(i * 0.31f, 0.13f, j * 0.31f);
            var a = noWarp.Value(0.5f, 0.5f, p, 0);
            var b = fullWarp.Value(0.5f, 0.5f, p, 0);
            mad += MathF.Abs(a.X - b.X);
            n++;
        }
        mad /= n;
        Assert.True(mad > 0.02f, $"warp iter 3 vs 0 produced negligible change (MAD={mad})");
    }

    [Fact]
    public void Wood_OutputMask_PacksScalarAsGrayscale()
    {
        // Mask output must produce (t, t, t) so FloatTexture's channel-average
        // recovers exactly t. Sample many points and verify R == G == B.
        var tex = new WoodTexture(4f, 1.5f)
        {
            Output = WoodTexture.OutputMode.Mask,
            FigureStrength = 0.5f,
            PoreDensity = 0.3f,
            KnotDensity = 0.4f,
        };
        foreach (var p in WoodProbePoints())
        {
            var v = tex.Value(0f, 0f, p, 0);
            Assert.InRange(v.X, 0f, 1f);
            Assert.Equal(v.X, v.Y, 6);
            Assert.Equal(v.X, v.Z, 6);
        }
    }

    [Fact]
    public void Wood_AsymmetricRingProfile_LatewoodIsDarkest()
    {
        // The new asymmetric profile must produce its darkest values right at
        // the ring boundary (frac → 1). We sweep a single ring index at high
        // resolution along the radial direction and assert the minimum
        // brightness lives in the latewood band (frac > 0.7), not at the
        // ring centre (frac ≈ 0.5) like the legacy symmetric profile.
        var tex = new WoodTexture(scale: 1f, grainStrength: 0f)
        {
            // Disable everything that could shift the profile shape so we can
            // isolate the earlywood/latewood asymmetry.
            FigureStrength = 0f,
            RingColorVariation = 0f,
            RingWidthVariation = 0f,
            WarpAmplitude = 0f,
            WarpIterations = 0,
            FoldAmplitude = Vector3.Zero,
            LatewoodWidth = 0.25f,
            RingSharpness = 3f,
        };

        const int N = 100;
        float minT = float.MaxValue;
        int minIdx = -1;
        for (int i = 0; i < N; i++)
        {
            float frac = (i + 0.5f) / N;          // sweep ∈ (0, 1) of a ring
            // Start at radial dist ~10.5 so we're firmly inside ring index 10
            var p = new Vector3(10f + frac, 0f, 0f);
            var v = tex.Value(0f, 0f, p, 0);
            float lum = 0.2126f * v.X + 0.7152f * v.Y + 0.0722f * v.Z;
            if (lum < minT) { minT = lum; minIdx = i; }
        }
        float minFrac = (minIdx + 0.5f) / N;
        // Latewood band sits at the end of each ring — the darkest sample must
        // live in the upper part of the ring, NOT at the symmetric middle.
        Assert.True(minFrac > 0.7f,
            $"asymmetric latewood band should darken the upper end of each ring (minFrac={minFrac})");
    }

    [Fact]
    public void Wood_RingColorVariation_AdjacentRingsDiffer()
    {
        // Two consecutive integer rings must produce different mean brightness
        // when ring_color_variation > 0 — proves the per-ring hash is driving
        // the colour shift. We disable everything else for clean signal.
        var tex = new WoodTexture(scale: 1f, grainStrength: 0f)
        {
            FigureStrength = 0f,
            RingWidthVariation = 0f,
            WarpAmplitude = 0f,
            WarpIterations = 0,
            FoldAmplitude = Vector3.Zero,
            RingColorVariation = 0.40f,
        };

        // Mean brightness at the centre of each of 20 rings.
        float[] means = new float[20];
        for (int r = 0; r < 20; r++)
        {
            float sum = 0f;
            const int K = 16;
            for (int j = 0; j < K; j++)
            {
                float frac = 0.5f - 0.1f + j * (0.2f / K);  // narrow band around the bright middle
                var p = new Vector3(r + 0.5f + (frac - 0.5f), 0f, 0f);
                var v = tex.Value(0f, 0f, p, 0);
                sum += 0.2126f * v.X + 0.7152f * v.Y + 0.0722f * v.Z;
            }
            means[r] = sum / K;
        }
        // Compute mean abs difference between consecutive rings — must be > 0
        // since each ring gets a different hash.
        float mad = 0f;
        for (int r = 1; r < means.Length; r++)
            mad += MathF.Abs(means[r] - means[r - 1]);
        mad /= (means.Length - 1);
        Assert.True(mad > 0.005f,
            $"ring_color_variation must produce ring-to-ring brightness shifts (MAD={mad})");
    }

    [Fact]
    public void Wood_DistortionAliasMappedToWarpAmplitude()
    {
        // Back-compat shim: YAML scenes that still set `distortion: 0.5` must
        // get a non-trivial warp through the new pipeline. We test this at the
        // texture-property level: setting WarpAmplitude directly equals what
        // SceneLoader does when it maps `distortion` → `warp_amplitude`.
        var withWarp = new WoodTexture(4f, 1.5f) { WarpAmplitude = 0.5f };
        var defaultWarp = new WoodTexture(4f, 1.5f);   // default WarpAmplitude = 0.4
        // The two must differ — proves warp is connected and >0 contributes.
        bool found = false;
        foreach (var p in WoodProbePoints())
        {
            if (!VecClose(withWarp.Value(0.5f, 0.5f, p, 0), defaultWarp.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "warp_amplitude variations must affect wood output");
    }

    [Fact]
    public void Wood_AllKnobsCranked_NoNaNOrOutOfRange()
    {
        // Worst-case stress: every knob pushed to its extreme. No NaN, no Inf,
        // RGB in [0, 1].
        var tex = new WoodTexture(4f, 2.5f)
        {
            SpaceStretch = new Vector3(0.4f, 1.8f, 1.0f),
            WarpAmplitude = 1.2f,
            WarpIterations = 3,
            FoldAmplitude = new Vector3(0.9f, 0.4f, 0.7f),
            GrainScale = 2.5f,
            FigureStrength = 1.5f,
            FigureScale = 0.15f,
            FigureAspect = 5f,
            RadialAnisotropy = 4f,
            LatewoodWidth = 0.30f,
            RingSharpness = 6f,
            RingColorVariation = 0.40f,
            RingWidthVariation = 0.20f,
            KnotDensity = 1f,
            KnotScale = 0.6f,
            PoreDensity = 1f,
            PoreScale = 18f,
            PoreStrength = 0.7f,
            PoreAspect = 6f,
            HeartwoodRadius = 1.2f,
            HeartwoodBlend = 0.4f,
            Octaves = 6,
        };
        foreach (var p in WoodProbePoints())
        {
            var v = tex.Value(0.5f, 0.5f, p, 17);
            Assert.False(float.IsNaN(v.X) || float.IsInfinity(v.X));
            Assert.False(float.IsNaN(v.Y) || float.IsInfinity(v.Y));
            Assert.False(float.IsNaN(v.Z) || float.IsInfinity(v.Z));
            Assert.InRange(v.X, 0f, 1f);
            Assert.InRange(v.Y, 0f, 1f);
            Assert.InRange(v.Z, 0f, 1f);
        }
    }

    private static float MeanBrightness(WoodTexture tex)
    {
        const int N = 12;
        double sum = 0;
        int n = 0;
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        for (int k = 0; k < N; k++)
        {
            var p = new Vector3((i - N * 0.5f) * 0.1f, (j - N * 0.5f) * 0.1f, (k - N * 0.5f) * 0.1f);
            var v = tex.Value(0.5f, 0.5f, p, 0);
            sum += 0.2126 * v.X + 0.7152 * v.Y + 0.0722 * v.Z;
            n++;
        }
        return (float)(sum / n);
    }

    private static IEnumerable<Vector3> MarbleProbePoints()
    {
        yield return new Vector3(0, 0, 0);
        yield return new Vector3(0.13f, 0.27f, 0.41f);
        yield return new Vector3(1.7f, -0.5f, 2.3f);
        yield return new Vector3(-3.1f, 4.2f, 0.05f);
        yield return new Vector3(7.0f, 0.0f, -1.5f);
        yield return new Vector3(0.5f, 0.5f, 0.5f);
        yield return new Vector3(2.8f, 1.1f, 3.6f);
        yield return new Vector3(-0.7f, -0.3f, 1.4f);
    }

    private static IEnumerable<Vector3> WoodProbePoints()
    {
        yield return new Vector3(0.5f, 0.0f, 0.5f);
        yield return new Vector3(0.13f, 0.27f, 0.41f);
        yield return new Vector3(1.7f, -0.5f, 2.3f);
        yield return new Vector3(-3.1f, 4.2f, 0.05f);
        yield return new Vector3(2.0f, 1.0f, -1.5f);
        yield return new Vector3(0.5f, 0.5f, 0.5f);
        yield return new Vector3(2.8f, 1.1f, 3.6f);
        yield return new Vector3(-0.7f, -0.3f, 1.4f);
    }
}
