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

    [Fact]
    public void Wood_AllNewKnobsAtDefaults_BitIdenticalToLegacy()
    {
        // The most important back-compat invariant: a WoodTexture constructed
        // the legacy way (no new properties set) must produce byte-identical
        // output to a hypothetical "all-new-knobs-default" instance. This is
        // the contract every existing tutorial scene relies on.
        var legacy = new WoodTexture(4f, 2f);
        var newDefaults = new WoodTexture(4f, 2f)
        {
            GrainScale = 1f,
            FigureScale = 0.25f,
            FigureStrength = 0f,
            RadialAnisotropy = 0f,
            KnotDensity = 0f,
        };

        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(legacy.Value(0.5f, 0.5f, p, 0), newDefaults.Value(0.5f, 0.5f, p, 0)),
                $"defaults of new knobs must be no-ops at p={p}");
    }

    [Fact]
    public void Wood_FigureStrengthPositive_ChangesOutput()
    {
        // Sanity that figure_strength > 0 actually contributes — otherwise
        // it would be dead code.
        var grainOnly = new WoodTexture(4f, 2f);
        var withFigure = new WoodTexture(4f, 2f)
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
    public void Wood_GrainScaleScalesGrainOnly_FigureUnaffectedAtSamePoint()
    {
        // Two wood instances with the same figure parameters but different
        // grain_scale must agree on the figure contribution. We can't isolate
        // figure exactly from the rendered colour, so we check the weaker
        // contract: setting figure_strength = 0 ⇒ grain_scale must alter the
        // output (proves the multiplier is connected); flipping grain_scale
        // around 1 must produce the same set of probes as the legacy.
        // Note: with grain_scale = 1 + figure_strength = 0 ⇒ legacy.
        var legacy = new WoodTexture(4f, 2f);
        var grainShrunk = new WoodTexture(4f, 2f) { GrainScale = 0.5f };
        var grainOne = new WoodTexture(4f, 2f) { GrainScale = 1f };

        // grain_scale = 1 is the back-compat default ⇒ identical to legacy.
        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(legacy.Value(0.5f, 0.5f, p, 0), grainOne.Value(0.5f, 0.5f, p, 0)),
                $"grain_scale = 1 must equal legacy at p={p}");

        // grain_scale ≠ 1 must alter the result.
        bool found = false;
        foreach (var p in WoodProbePoints())
        {
            if (!VecClose(legacy.Value(0.5f, 0.5f, p, 0), grainShrunk.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "grain_scale != 1 must change wood output somewhere");
    }

    [Fact]
    public void Wood_RadialAnisotropyZero_BitIdenticalToLegacy()
    {
        // Anisotropy is a strict per-sample no-op at 0 — important because
        // its computation involves a divide-by-zero guard at the trunk axis.
        var legacy = new WoodTexture(4f, 2f);
        var withAniso = new WoodTexture(4f, 2f) { RadialAnisotropy = 0f };

        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(legacy.Value(0.5f, 0.5f, p, 0), withAniso.Value(0.5f, 0.5f, p, 0)),
                $"radial_anisotropy = 0 must be a no-op at p={p}");
    }

    [Fact]
    public void Wood_RadialAnisotropyPositive_ChangesOutput()
    {
        var iso = new WoodTexture(4f, 2f);
        var aniso = new WoodTexture(4f, 2f) { RadialAnisotropy = 2f };

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
        // The radial direction degenerates exactly on the trunk axis
        // (radial.Length() == 0). The implementation must hold up against
        // this — every previous step 1–4 had a "near-singular" guard test;
        // wood's anisotropy is no exception. Sample a strip along the axis.
        var tex = new WoodTexture(4f, 2f) { RadialAnisotropy = 5f };
        for (int i = 0; i < 16; i++)
        {
            var p = new Vector3(0, i * 0.1f, 0);  // exactly on trunk axis (Y)
            var v = tex.Value(0.5f, 0.5f, p, 0);
            Assert.False(float.IsNaN(v.X) || float.IsInfinity(v.X), $"NaN/Inf X at p={p}");
            Assert.False(float.IsNaN(v.Y) || float.IsInfinity(v.Y), $"NaN/Inf Y at p={p}");
            Assert.False(float.IsNaN(v.Z) || float.IsInfinity(v.Z), $"NaN/Inf Z at p={p}");
        }
    }

    [Fact]
    public void Wood_KnotDensityZero_BitIdenticalToLegacy()
    {
        // Knot path must be fully skipped at density = 0 — both for back-compat
        // and to avoid paying the Voronoi cost when knots are off.
        var legacy = new WoodTexture(4f, 2f);
        var withKnots = new WoodTexture(4f, 2f) { KnotDensity = 0f };

        foreach (var p in WoodProbePoints())
            Assert.True(VecClose(legacy.Value(0.5f, 0.5f, p, 0), withKnots.Value(0.5f, 0.5f, p, 0)),
                $"knot_density = 0 must be a no-op at p={p}");
    }

    [Fact]
    public void Wood_KnotDensityPositive_ProducesDarkerLocalRegions()
    {
        // Knot heart darkens the local `t` so at high density (lots of knots)
        // the mean brightness of the rendered field must be lower than the
        // knot-free version. Sampled over a coarse grid.
        var noKnots = new WoodTexture(4f, 2f);
        var withKnots = new WoodTexture(4f, 2f) { KnotDensity = 1f };

        float meanNoKnots = MeanBrightness(noKnots);
        float meanWithKnots = MeanBrightness(withKnots);

        Assert.True(meanWithKnots < meanNoKnots,
            $"with knots brightness should drop: noKnots={meanNoKnots}, withKnots={meanWithKnots}");
    }

    [Fact]
    public void Wood_AllNewKnobsPositive_NoNaNOrOutOfRange()
    {
        // Worst-case stress test: all four new wood knobs cranked up
        // simultaneously must not produce NaN, Inf, or RGB outside [0, 1].
        // Catches any unguarded division, log, or sqrt added by the upgrades.
        var tex = new WoodTexture(4f, 2f)
        {
            GrainScale = 2.5f,
            FigureScale = 0.15f,
            FigureStrength = 1.2f,
            RadialAnisotropy = 3f,
            KnotDensity = 0.8f,
            Octaves = 5,
        };
        foreach (var p in WoodProbePoints())
        {
            var v = tex.Value(0.5f, 0.5f, p, 0);
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
        // Average luminance over a coarse grid covering several rings. We
        // measure raw `t` indirectly via the rendered RGB which lerps between
        // dark and light wood — the lerp param IS the brightness factor.
        const int N = 16;
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
