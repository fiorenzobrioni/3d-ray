using System.Numerics;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Marble + Wood "studio quality" — DEVLOG "Texturing VFX production-grade" step 5.
///
/// <para>
/// Verifies the new studio-quality controls added to <see cref="MarbleTexture"/>
/// (secondary cross-veining wave) and <see cref="WoodTexture"/> (figure band,
/// radial anisotropy, knot density). The over-arching contract is back-compat
/// byte-identity: every legacy scene must render unchanged when the new knobs
/// are at their defaults.
/// </para>
/// </summary>
public class MarbleWoodStudioTests
{
    private const float Eps = 1e-5f;

    private static bool VecClose(Vector3 a, Vector3 b, float eps = 1e-5f)
        => MathF.Abs(a.X - b.X) < eps && MathF.Abs(a.Y - b.Y) < eps && MathF.Abs(a.Z - b.Z) < eps;

    // ─────────────────────────── Marble ─────────────────────────────────────

    [Fact]
    public void Marble_SecondaryStrengthZero_BitIdenticalToLegacy()
    {
        // The DEVLOG step-5 invariant: any scene that omits `secondary_wave:`
        // (or leaves strength = 0) must produce bit-identical output to the
        // pre-change implementation. Sampling many points to catch any path
        // where the secondary wave accidentally contributes a constant offset.
        var legacy = new MarbleTexture(4f, new Vector3(0.9f), new Vector3(0.1f));
        var newProps = new MarbleTexture(4f, new Vector3(0.9f), new Vector3(0.1f))
        {
            // Vary the secondary axis/frequency but keep strength = 0:
            // these must be ignored entirely.
            SecondaryAxis = new Vector3(1, 1, 0),
            SecondaryFrequency = 3.7f,
            SecondaryStrength = 0f,
        };

        foreach (var p in MarbleProbePoints())
            Assert.True(VecClose(legacy.Value(0.5f, 0.5f, p, 0), newProps.Value(0.5f, 0.5f, p, 0)),
                $"secondary_wave with strength=0 must be a no-op at p={p}");
    }

    [Fact]
    public void Marble_SecondaryStrengthPositive_ChangesOutput()
    {
        // The whole point of the secondary wave is to break unidirectional
        // veining — there must be at least one probe where adding cross-veins
        // shifts the colour.
        var single = new MarbleTexture(4f);
        var cross = new MarbleTexture(4f)
        {
            SecondaryStrength = 0.5f,
        };

        bool found = false;
        foreach (var p in MarbleProbePoints())
        {
            if (!VecClose(single.Value(0.5f, 0.5f, p, 0), cross.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found, "secondary_strength > 0 must change the marble output somewhere");
    }

    [Fact]
    public void Marble_SecondaryAxis_AutoOrthogonalised_AgainstPrimaryAxis()
    {
        // The DEVLOG contract: "even if the user picks a collinear axis they
        // still get an off-axis component". Picking SecondaryAxis exactly
        // parallel to VeinAxis must NOT collapse the cross-vein effect — the
        // loader-side orthogonalisation has to kick in.
        var parallel = new MarbleTexture(4f)
        {
            VeinAxis = Vector3.UnitZ,
            SecondaryAxis = Vector3.UnitZ,    // intentionally collinear
            SecondaryStrength = 0.6f,
        };
        var primary = new MarbleTexture(4f) { VeinAxis = Vector3.UnitZ };

        bool found = false;
        foreach (var p in MarbleProbePoints())
        {
            if (!VecClose(parallel.Value(0.5f, 0.5f, p, 0), primary.Value(0.5f, 0.5f, p, 0), 1e-4f))
            { found = true; break; }
        }
        Assert.True(found,
            "collinear secondary axis must be auto-orthogonalised, not silently dropped");
    }

    [Fact]
    public void Marble_HighSharpness_BaseColorDominates_VeinIsThin()
    {
        // Step 5/7 fix: production renderers (Cycles, Arnold marble2, RM
        // PxrMarble) all interpret "sharpness/width" so that increasing it
        // narrows the vein and widens the BASE — i.e. real Carrara is
        // mostly white with thin dark veins. The previous `t = t^k` did the
        // opposite (most surface = vein color, thin highlights = base);
        // the corrected `t = 1 − (1 − t)^k` matches industry behaviour.
        // We verify by sampling many points and checking that the average
        // colour leans much closer to `colors[0]` (base) than `colors[1]`
        // (vein) when sharpness is high.
        Vector3 baseCol = new(0.95f, 0.93f, 0.90f);
        Vector3 veinCol = new(0.05f, 0.05f, 0.05f);
        var tex = new MarbleTexture(4f, baseCol, veinCol)
        {
            VeinSharpness = 4f,
        };

        Vector3 sum = Vector3.Zero;
        int count = 0;
        for (int i = 0; i < 64; i++)
        for (int j = 0; j < 64; j++)
        {
            var p = new Vector3(i * 0.05f, 0.13f, j * 0.05f);
            sum += tex.Value(0.5f, 0.5f, p, 0);
            count++;
        }
        Vector3 avg = sum / count;

        // Distance to base should be much smaller than distance to vein.
        float distToBase = (avg - baseCol).Length();
        float distToVein = (avg - veinCol).Length();
        Assert.True(distToBase < distToVein,
            $"Avg colour should lean toward base, not vein. avg={avg} base={baseCol} vein={veinCol}");
        // And sharpening k=4 should give a very strong bias (avg luminance
        // about (k+1−1)/(k+1) = 0.8 of the way from vein to base).
        Assert.True(avg.X > 0.6f,
            $"With sharpness 4, average red should be ≥ 0.6 (close to base); got {avg.X}");
    }

    [Fact]
    public void Marble_HigherSharpness_NarrowsTheVein()
    {
        // Strict monotonicity check: increasing VeinSharpness from 1 to N
        // must move the average sample CLOSER to the base colour (vein
        // shrinks). This is the per-axis statement of the production-renderer
        // contract and the test that would have caught the inverted code.
        Vector3 baseCol = new(1f, 1f, 1f);
        Vector3 veinCol = new(0f, 0f, 0f);

        float MeanLuminance(float k)
        {
            var tex = new MarbleTexture(4f, baseCol, veinCol) { VeinSharpness = k };
            float sum = 0;
            int count = 0;
            for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
            {
                var p = new Vector3(i * 0.07f, 0.13f, j * 0.07f);
                var v = tex.Value(0.5f, 0.5f, p, 0);
                sum += v.X;  // greyscale, X == Y == Z
                count++;
            }
            return sum / count;
        }

        float l1 = MeanLuminance(1f);   // no sharpening: mean ~ 0.5
        float l4 = MeanLuminance(4f);   // sharper: closer to white (base)
        float l8 = MeanLuminance(8f);   // even sharper: even closer to white

        Assert.True(l4 > l1, $"sharpness 4 should be brighter than 1: l1={l1}, l4={l4}");
        Assert.True(l8 > l4, $"sharpness 8 should be brighter than 4: l4={l4}, l8={l8}");
    }

    [Fact]
    public void Marble_SecondaryWave_PreservesOutputRange01()
    {
        // The (sin + strength·sin)/(1 + strength) renormalisation must keep
        // the sine-driven `t` parameter in [0, 1] so the colour lerp (or
        // ColorRamp.Sample(t)) stays well-defined. We probe the rendered RGB
        // and verify each component lies in [0, 1] — the input colours are
        // [0, 1] so this catches any out-of-range t that would extrapolate.
        var tex = new MarbleTexture(4f, new Vector3(0.05f), new Vector3(0.95f))
        {
            SecondaryAxis = new Vector3(1, 0.3f, 0),
            SecondaryFrequency = 0.7f,
            SecondaryStrength = 0.5f,
            VeinSharpness = 4f,
        };
        foreach (var p in MarbleProbePoints())
        {
            var v = tex.Value(0.5f, 0.5f, p, 0);
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
