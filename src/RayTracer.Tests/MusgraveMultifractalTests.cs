using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Musgrave multifractal contract — DEVLOG "Texturing VFX production-grade" step 4.
///
/// <para>
/// Verifies the two production-grade Musgrave fractals (HeteroTerrain,
/// HybridMultifractal) against their canonical algorithmic definitions
/// (Ebert/Musgrave/Peachey/Perlin "Texturing &amp; Modeling: A Procedural
/// Approach", 3rd ed. §16.3.3-§16.3.4): per-octave spectral weight follows
/// <c>lacunarity^(-i·H)</c>, the offset acts as a sea-level bias inside each
/// octave, and the result is deterministic / bit-stable across renders.
/// </para>
/// </summary>
public class MusgraveMultifractalTests
{
    private const float Eps = 1e-5f;

    [Fact]
    public void HeteroTerrain_SingleOctave_EqualsOffsetPlusBaseNoise()
    {
        // With octaves = 1 the recursion never enters its body, so the result
        // collapses to the canonical Musgrave initial line:
        //   value = offset + Noise(p)
        // — which is the most fundamental sanity check (no spectral weight
        // contributions at all).
        var perlin = new Perlin(seed: 11);
        foreach (var p in SamplePoints())
        {
            float baseNoise = perlin.Noise(p);
            float ht = perlin.HeteroTerrain(p, octaves: 1, lacunarity: 2f, h: 0.5f, offset: 0.7f);
            Assert.Equal(0.7f + baseNoise, ht, precision: 5);
        }
    }

    [Fact]
    public void HybridMultifractal_SingleOctave_EqualsOffsetPlusBaseNoise()
    {
        // Same first-octave invariant as HeteroTerrain — the running `weight`
        // is initialised to result and never decayed when octaves == 1.
        var perlin = new Perlin(seed: 13);
        foreach (var p in SamplePoints())
        {
            float baseNoise = perlin.Noise(p);
            float hm = perlin.HybridMultifractal(p, octaves: 1, lacunarity: 2f, h: 0.5f, offset: 0.7f);
            Assert.Equal(0.7f + baseNoise, hm, precision: 5);
        }
    }

    [Fact]
    public void HeteroTerrain_TwoOctaves_MatchesAnalyticReference()
    {
        // Manual unroll of Musgrave §16.3.3 for octaves = 2:
        //   value₀ = offset + Noise(p)
        //   weight = lacunarity^(-H)
        //   value₁ = value₀ + (Noise(p · lacunarity) + offset) · weight · value₀
        // Tests the spectral-weight calculation and the running-value
        // multiplier (the "heterogeneous" part of the algorithm).
        var perlin = new Perlin(seed: 23);
        const float lac = 2f;
        const float H = 0.25f;
        const float off = 0.7f;
        float weight = MathF.Pow(lac, -H);

        foreach (var p in SamplePoints())
        {
            float v0 = off + perlin.Noise(p);
            float v1 = v0 + (perlin.Noise(p * lac) + off) * weight * v0;
            float ht = perlin.HeteroTerrain(p, octaves: 2, lacunarity: lac, h: H, offset: off);
            Assert.Equal(v1, ht, precision: 4);
        }
    }

    [Fact]
    public void HybridMultifractal_TwoOctaves_MatchesAnalyticReference()
    {
        // Manual unroll of Musgrave §16.3.4 for octaves = 2:
        //   result = (Noise(p) + offset) · 1    [spectral weight = lac^(-0·H) = 1]
        //   weight = result
        //   weight = min(weight, 1)
        //   signal = (Noise(p · lacunarity) + offset) · lac^(-H)
        //   result += weight · signal
        var perlin = new Perlin(seed: 29);
        const float lac = 2f;
        const float H = 0.25f;
        const float off = 0.7f;

        foreach (var p in SamplePoints())
        {
            float result = perlin.Noise(p) + off;
            float weight = MathF.Min(result, 1f);
            float signal = (perlin.Noise(p * lac) + off) * MathF.Pow(lac, -H);
            float expected = result + weight * signal;

            float hm = perlin.HybridMultifractal(p, octaves: 2, lacunarity: lac, h: H, offset: off);
            Assert.Equal(expected, hm, precision: 4);
        }
    }

    [Fact]
    public void Determinism_SameSeed_BitIdenticalResults()
    {
        // Two Perlin instances built from the same seed must produce identical
        // Musgrave samples — same scene-load determinism guarantee as the
        // other procedurals (light-hardening cycle invariant).
        var a = new Perlin(seed: 1234);
        var b = new Perlin(seed: 1234);
        foreach (var p in SamplePoints())
        {
            Assert.Equal(a.HeteroTerrain(p, 6, 2f, 0.5f, 0.7f),
                         b.HeteroTerrain(p, 6, 2f, 0.5f, 0.7f));
            Assert.Equal(a.HybridMultifractal(p, 6, 2f, 0.5f, 0.7f),
                         b.HybridMultifractal(p, 6, 2f, 0.5f, 0.7f));
        }
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void Output_IsFiniteAcrossReasonableHRange(float H)
    {
        // Musgrave's exponential growth (HeteroTerrain) and weight·signal cascade
        // (HybridMultifractal) must never overflow into NaN/Inf for the range of
        // H values production artists realistically use. Catches a regression
        // where weight ≥ 1 unclamped would blow up the hybrid signal.
        var perlin = new Perlin(seed: 7);
        foreach (var p in SamplePoints())
        {
            float ht = perlin.HeteroTerrain(p, 10, 2f, H, 0.7f);
            float hm = perlin.HybridMultifractal(p, 10, 2f, H, 0.7f);
            Assert.False(float.IsNaN(ht) || float.IsInfinity(ht), $"HeteroTerrain NaN/Inf at p={p}, H={H}");
            Assert.False(float.IsNaN(hm) || float.IsInfinity(hm), $"HybridMultifractal NaN/Inf at p={p}, H={H}");
        }
    }

    [Fact]
    public void HighH_ProducesSmootherSignal_ThanLowH()
    {
        // H is the "fractal increment" — large H suppresses high-frequency
        // octaves (spectral weight = lacunarity^(-i·H) shrinks faster), so the
        // total variance of the field across a fine grid must drop with H.
        // Production artists rely on this monotonic behaviour.
        var perlin = new Perlin(seed: 8);

        float Variance(float H)
        {
            const int N = 64;
            const float step = 0.05f;
            double mean = 0, m2 = 0;
            int k = 0;
            for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                var p = new Vector3(i * step, 0.13f, j * step);
                float v = perlin.HeteroTerrain(p, octaves: 8, lacunarity: 2f, h: H, offset: 0.7f);
                k++;
                double delta = v - mean;
                mean += delta / k;
                m2   += delta * (v - mean);
            }
            return (float)(m2 / (k - 1));
        }

        float varLow  = Variance(0.1f);
        float varHigh = Variance(1.5f);

        // High-H must be at least 2x smoother (variance) than low-H over the
        // same grid; the constant is conservative to avoid statistical
        // false positives — typical ratios are well above 5x.
        Assert.True(varLow > varHigh * 2f,
            $"expected H=0.1 noisier than H=1.5: var(0.1)={varLow}, var(1.5)={varHigh}");
    }

    [Fact]
    public void OffsetParameter_Shifts_HeteroTerrain_Output()
    {
        // The Musgrave `offset` parameter biases each octave's signal additively.
        // For HeteroTerrain with a single octave it must be a pure additive shift:
        //   HT(p, off=a) - HT(p, off=b) ≡ a - b
        var perlin = new Perlin(seed: 41);
        const float a = 0.7f;
        const float b = 0.2f;
        foreach (var p in SamplePoints())
        {
            float ha = perlin.HeteroTerrain(p, octaves: 1, lacunarity: 2f, h: 0.5f, offset: a);
            float hb = perlin.HeteroTerrain(p, octaves: 1, lacunarity: 2f, h: 0.5f, offset: b);
            Assert.Equal(a - b, ha - hb, precision: 5);
        }
    }

    [Fact]
    public void NoiseTexture_HeteroTerrainMode_DiffersFromFbm()
    {
        // The whole point of step 4 is to give artists a fractal that doesn't
        // look like fBm. On at least one probe inside the field, hetero-terrain
        // must yield a different colour than fBm with the same scale/octaves —
        // otherwise the new code path would be a no-op.
        var fbm = new NoiseTexture(scale: 4f)
        {
            NoiseType = NoiseTexture.NoiseKind.Fbm,
            Octaves = 8,
            Lacunarity = 2f,
            Gain = 0.5f,
        };
        var ht = new NoiseTexture(scale: 4f)
        {
            NoiseType = NoiseTexture.NoiseKind.HeteroTerrain,
            Octaves = 8,
            Lacunarity = 2f,
            FractalIncrement = 0.25f,
            FractalOffset = 0.7f,
        };

        bool foundDifference = false;
        foreach (var p in SamplePoints())
        {
            Vector3 a = fbm.Value(0.5f, 0.5f, p, 0);
            Vector3 b = ht.Value(0.5f, 0.5f, p, 0);
            if ((a - b).LengthSquared() > 1e-6f) { foundDifference = true; break; }
        }
        Assert.True(foundDifference, "hetero_terrain must produce non-fBm output");
    }

    [Fact]
    public void NoiseTexture_HybridMultifractalMode_DiffersFromFbmAndHetero()
    {
        // Hybrid multifractal is its own beast (stratified rocks) — distinct
        // from both fBm and hetero-terrain. Verify by probing many points.
        var fbm = new NoiseTexture(scale: 4f) { NoiseType = NoiseTexture.NoiseKind.Fbm, Octaves = 8 };
        var ht  = new NoiseTexture(scale: 4f)
        {
            NoiseType = NoiseTexture.NoiseKind.HeteroTerrain,
            Octaves = 8, FractalIncrement = 0.25f, FractalOffset = 0.7f,
        };
        var hm  = new NoiseTexture(scale: 4f)
        {
            NoiseType = NoiseTexture.NoiseKind.HybridMultifractal,
            Octaves = 8, FractalIncrement = 0.25f, FractalOffset = 0.7f,
        };

        bool diffFromFbm = false;
        bool diffFromHt  = false;
        foreach (var p in SamplePoints())
        {
            var hmVal = hm.Value(0.5f, 0.5f, p, 0);
            if ((hmVal - fbm.Value(0.5f, 0.5f, p, 0)).LengthSquared() > 1e-6f) diffFromFbm = true;
            if ((hmVal - ht .Value(0.5f, 0.5f, p, 0)).LengthSquared() > 1e-6f) diffFromHt  = true;
            if (diffFromFbm && diffFromHt) break;
        }
        Assert.True(diffFromFbm, "hybrid_multifractal must differ from fbm");
        Assert.True(diffFromHt , "hybrid_multifractal must differ from hetero_terrain");
    }

    [Theory]
    [InlineData(NoiseTexture.NoiseKind.HeteroTerrain,      0.7f)]
    [InlineData(NoiseTexture.NoiseKind.HeteroTerrain,      0.2f)]
    [InlineData(NoiseTexture.NoiseKind.HybridMultifractal, 0.7f)]
    [InlineData(NoiseTexture.NoiseKind.HybridMultifractal, 0.2f)]
    public void NoiseTexture_MusgraveOutput_StaysInsideColorRampDomain(
        NoiseTexture.NoiseKind kind, float offset)
    {
        // Regression for the "musgrave-multifractal-showcase saturates to
        // white" bug: at canonical offset = 0.7 with low H = 0.25 the raw
        // Musgrave value diverges to ~30+ and a naive [0,1] clamp paints
        // every panel pure white. The NoiseTexture layer normalises into
        // [0,1] with non-trivial spread, so a color_ramp keyed at canonical
        // terrain positions (water/grass/rock/snow) actually exercises all
        // of its stops. The black colour at ramp 0 and white at ramp 1
        // bracket the legal output range; anything outside means the
        // normalisation broke (saturation back to either extreme).
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, Vector3.Zero, ColorRamp.Interp.Linear),
            new ColorRamp.Stop(1f, Vector3.One,  ColorRamp.Interp.Linear),
        });
        var tex = new NoiseTexture(scale: 3.2f)
        {
            NoiseType = kind,
            Octaves = 8,
            Lacunarity = 2f,
            FractalIncrement = 0.25f,
            FractalOffset = offset,
            ColorRamp = ramp,
        };

        int saturatedHigh = 0;
        int saturatedLow  = 0;
        int total = 0;
        var rng = new Random(0xC0FFEE);
        for (int i = 0; i < 4000; i++)
        {
            var p = new Vector3(
                ((float)rng.NextDouble() - 0.5f) * 12f,
                ((float)rng.NextDouble() - 0.5f) * 12f,
                ((float)rng.NextDouble() - 0.5f) * 12f);
            float g = tex.Value(0.5f, 0.5f, p, 0).X;
            Assert.InRange(g, 0f, 1f);
            if (g > 0.995f) saturatedHigh++;
            if (g < 0.005f) saturatedLow ++;
            total++;
        }

        // Less than ~40% of samples should hit either rail. A failing
        // bound means the field collapses into a constant (loss of detail)
        // — exactly the visible bug in the original showcase.
        Assert.True(saturatedHigh < total * 0.40, $"{kind} offset={offset}: {saturatedHigh}/{total} saturated high (loss of detail at peaks)");
        Assert.True(saturatedLow  < total * 0.40, $"{kind} offset={offset}: {saturatedLow}/{total} saturated low (loss of detail in valleys)");
    }

    [Fact]
    public void NoiseTexture_BackCompat_LegacyScenesUnchanged()
    {
        // A NoiseTexture that doesn't opt in to the Musgrave modes (NoiseType
        // left at Auto or set to one of the historical kinds) must produce
        // byte-identical results to before this commit. Construct two
        // instances varying only the new FractalIncrement / FractalOffset
        // properties and confirm they yield identical values.
        var legacy = new NoiseTexture(scale: 3f);
        var newProps = new NoiseTexture(scale: 3f)
        {
            FractalIncrement = 1.5f,  // must not affect non-Musgrave kinds
            FractalOffset = 0.123f,
        };

        foreach (var p in SamplePoints())
        {
            Assert.Equal(legacy.Value(0.4f, 0.6f, p, 0), newProps.Value(0.4f, 0.6f, p, 0));
        }
    }

    private static IEnumerable<Vector3> SamplePoints()
    {
        // Deterministic set covering grid-aligned, mid-cell and slightly off-grid
        // positions; chosen to exercise the Perlin permutation tables broadly.
        yield return new Vector3(0f, 0f, 0f);
        yield return new Vector3(0.5f, 0.5f, 0.5f);
        yield return new Vector3(1.0001f, 2.0f, -3.0001f);
        yield return new Vector3(7.3f, -1.8f, 4.4f);
        yield return new Vector3(-12.7f, 0.13f, 8.55f);
        yield return new Vector3(0.27f, 0.81f, 0.55f);
        yield return new Vector3(3.0f, 0.0f, 3.0f);
        yield return new Vector3(-0.7f, 4.2f, 0.1f);
    }
}
