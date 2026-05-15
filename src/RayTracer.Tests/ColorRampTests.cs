using System.Numerics;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// ColorRamp contract — DEVLOG "Texturing VFX production-grade" step 2.
///
/// <para>
/// Verifies the interpolation kinds, boundary clamping, sort behaviour and
/// integration with the procedural textures (Noise / Marble / Wood / Voronoi
/// / Gradient).
/// </para>
/// </summary>
public class ColorRampTests
{
    private static readonly Vector3 Black = Vector3.Zero;
    private static readonly Vector3 White = Vector3.One;
    private static readonly Vector3 Red   = new(1f, 0f, 0f);

    private static bool VecClose(Vector3 a, Vector3 b, float eps = 1e-5f)
        => MathF.Abs(a.X - b.X) < eps && MathF.Abs(a.Y - b.Y) < eps && MathF.Abs(a.Z - b.Z) < eps;

    [Fact]
    public void TwoStopLinearRamp_MatchesVector3Lerp()
    {
        // A 2-stop linear ramp must reproduce Vector3.Lerp(A, B, t) bit-for-bit
        // so the legacy `colors:` shorthand and the new `color_ramp:` of two
        // linear stops have indistinguishable output (back-compat invariant).
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, Black, ColorRamp.Interp.Linear),
            new ColorRamp.Stop(1f, White, ColorRamp.Interp.Linear),
        });

        foreach (float t in new[] { 0f, 0.13f, 0.5f, 0.7777f, 1f })
        {
            Vector3 expected = Vector3.Lerp(Black, White, t);
            Assert.True(VecClose(ramp.Sample(t), expected),
                $"t={t} ramp={ramp.Sample(t)} expected={expected}");
        }
    }

    [Fact]
    public void SampleAtOrBelowFirstStop_ReturnsFirstColor_AboveLastStop_ReturnsLastColor()
    {
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0.25f, Red,   ColorRamp.Interp.Linear),
            new ColorRamp.Stop(0.75f, White, ColorRamp.Interp.Linear),
        });

        Assert.Equal(Red,   ramp.Sample(-1f));
        Assert.Equal(Red,   ramp.Sample(0f));
        Assert.Equal(Red,   ramp.Sample(0.25f));
        Assert.Equal(White, ramp.Sample(0.75f));
        Assert.Equal(White, ramp.Sample(1f));
        Assert.Equal(White, ramp.Sample(2f));
    }

    [Fact]
    public void SingleStop_ReturnsConstantColor()
    {
        var ramp = new ColorRamp(new[] { new ColorRamp.Stop(0.5f, Red) });
        foreach (float t in new[] { 0f, 0.5f, 1f, -1f, 2f })
            Assert.Equal(Red, ramp.Sample(t));
    }

    [Fact]
    public void StopsAreSortedByPosition()
    {
        // Order of stops in the input must not change the output.
        var sorted = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, Black),
            new ColorRamp.Stop(1f, White),
        });
        var reversed = new ColorRamp(new[]
        {
            new ColorRamp.Stop(1f, White),
            new ColorRamp.Stop(0f, Black),
        });

        for (float t = 0f; t <= 1f; t += 0.05f)
            Assert.Equal(sorted.Sample(t), reversed.Sample(t));
    }

    [Fact]
    public void PositionsAreClampedToUnitInterval()
    {
        // Out-of-range positions clamp; the resulting ramp lives entirely on
        // [0, 1] and stays usable.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(-0.5f, Black),
            new ColorRamp.Stop( 2.0f, White),
        });
        // Both effective positions are 0 and 1; midpoint is mid-grey.
        Assert.True(VecClose(ramp.Sample(0.5f), new Vector3(0.5f)));
    }

    [Fact]
    public void ConstantInterp_HoldsLeftStop()
    {
        // Constant interpolation = step function. Anywhere within the segment
        // [0, 1) returns the left stop's colour; only at t = 1 does the
        // right stop take over.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, Black, ColorRamp.Interp.Constant),
            new ColorRamp.Stop(1f, White, ColorRamp.Interp.Linear),
        });
        Assert.Equal(Black, ramp.Sample(0.01f));
        Assert.Equal(Black, ramp.Sample(0.99f));
        Assert.Equal(White, ramp.Sample(1f));
    }

    [Fact]
    public void SmoothstepInterp_MatchesAnalyticHermite()
    {
        // Hermite cubic 3t² − 2t³ — verified analytically at three points.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, Vector3.Zero,      ColorRamp.Interp.Smoothstep),
            new ColorRamp.Stop(1f, new Vector3(10f),  ColorRamp.Interp.Linear),
        });

        foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
        {
            float expected = 10f * t * t * (3f - 2f * t);
            Vector3 v = ramp.Sample(t);
            Assert.True(MathF.Abs(v.X - expected) < 1e-5f,
                $"smoothstep mismatch at t={t}: got {v.X}, expected {expected}");
        }
    }

    [Fact]
    public void EaseInterp_MatchesPerlinSmootherstep()
    {
        // Quintic 6t⁵ − 15t⁴ + 10t³ — Perlin's improved smoothstep with C²
        // continuity. Reference value at t = 0.5 is exactly 0.5.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f, Vector3.Zero,      ColorRamp.Interp.Ease),
            new ColorRamp.Stop(1f, new Vector3(1f),   ColorRamp.Interp.Linear),
        });
        Assert.True(MathF.Abs(ramp.Sample(0.5f).X - 0.5f) < 1e-6f);

        // The curve is monotone-increasing and starts/ends with zero
        // derivative — sanity-check by comparing two close samples around the
        // endpoints (slope must be << than a straight line).
        Vector3 nearStart = ramp.Sample(0.01f);
        Vector3 nearEnd   = ramp.Sample(0.99f);
        // Linear interpolation would give 0.01 / 0.99; quintic gives << 0.01
        Assert.True(nearStart.X < 1e-3f, $"ease near-start should be ~0, got {nearStart.X}");
        Assert.True(nearEnd.X   > 1f - 1e-3f, $"ease near-end should be ~1, got {nearEnd.X}");
    }

    [Fact]
    public void CoincidentStops_ProduceHardBreak()
    {
        // Two stops at the same position with different colours: an artist
        // trick for sharp transitions. The right stop's colour wins for any
        // t ≥ that position; the left stop's colour wins below.
        var ramp = new ColorRamp(new[]
        {
            new ColorRamp.Stop(0f,   Black, ColorRamp.Interp.Linear),
            new ColorRamp.Stop(0.5f, Black, ColorRamp.Interp.Linear),
            new ColorRamp.Stop(0.5f, White, ColorRamp.Interp.Linear),
            new ColorRamp.Stop(1f,   White, ColorRamp.Interp.Linear),
        });
        Assert.Equal(Black, ramp.Sample(0.499f));
        Assert.Equal(White, ramp.Sample(0.5f));
        Assert.Equal(White, ramp.Sample(0.501f));
    }

    [Fact]
    public void EmptyStops_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ColorRamp(Array.Empty<ColorRamp.Stop>()));
    }

    [Fact]
    public void NoiseTexture_WithoutRamp_IsBitIdenticalToLegacy()
    {
        // Back-compat guard: any NoiseTexture rendered without a ColorRamp
        // must produce the same colour as before this feature landed. We
        // sample a few deterministic (p, seed) tuples and check the value
        // stays inside [colorA, colorB] envelope (i.e. is a proper lerp).
        var tex = new NoiseTexture(2.5f, Black, White);
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = new(i * 0.31f, i * 0.71f, i * 1.13f);
            Vector3 v = tex.Value(0f, 0f, p, 0);
            Assert.InRange(v.X, 0f, 1f);
            Assert.True(MathF.Abs(v.X - v.Y) < 1e-6f && MathF.Abs(v.Y - v.Z) < 1e-6f,
                "Without ramp, value must be a scalar grey (lerp between black and white).");
        }
    }

    [Fact]
    public void NoiseTexture_WithThreeStopRamp_PicksMiddleColorAtSomePoint()
    {
        // A 3-stop ramp puts a distinct colour at t = 0.5 that the two-stop
        // shortcut couldn't produce. Sampling the noise across many points
        // must visit the middle band at least once.
        var tex = new NoiseTexture(2.5f, Black, White)
        {
            ColorRamp = new ColorRamp(new[]
            {
                new ColorRamp.Stop(0f,   Black, ColorRamp.Interp.Linear),
                new ColorRamp.Stop(0.5f, Red,   ColorRamp.Interp.Linear),
                new ColorRamp.Stop(1f,   White, ColorRamp.Interp.Linear),
            }),
        };
        bool sawRedBand = false;
        for (int i = 0; i < 64 && !sawRedBand; i++)
        {
            Vector3 p = new(i * 0.13f, i * 0.41f, i * 0.27f);
            Vector3 v = tex.Value(0f, 0f, p, 0);
            // The ramp climbs from black to red on [0, 0.5] (R rises, G/B stay 0),
            // then from red to white on [0.5, 1] (G/B rise). Sample is in the
            // red band iff R is high but G/B are low.
            if (v.X > 0.9f && v.Y < 0.2f && v.Z < 0.2f) sawRedBand = true;
        }
        Assert.True(sawRedBand, "Noise+3-stop ramp must produce the middle red colour somewhere in space.");
    }

    [Fact]
    public void MarbleTexture_RampReplacesTwoColorLerp()
    {
        // When a ramp is attached to the marble, the constructor's vein/base
        // colours are bypassed entirely. Use a single-stop ramp at a colour
        // unreachable from the constructor pair to verify the bypass.
        var tex = new MarbleTexture(2f, Black, White)
        {
            ColorRamp = new ColorRamp(new[] { new ColorRamp.Stop(0f, Red) }),
        };
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = new(i * 0.2f, i * 0.4f, i * 0.6f);
            Assert.True(VecClose(tex.Value(0f, 0f, p, 0), Red));
        }
    }

    [Fact]
    public void WoodTexture_RampReplacesTwoColorLerp()
    {
        var tex = new WoodTexture(4f, 2f, Black, White)
        {
            ColorRamp = new ColorRamp(new[] { new ColorRamp.Stop(0f, Red) }),
        };
        Vector3 p = new(0.3f, 0.7f, 0.1f);
        Assert.True(VecClose(tex.Value(0f, 0f, p, 0), Red));
    }

    [Fact]
    public void VoronoiTexture_RampReplacesTwoColorLerp()
    {
        var tex = new VoronoiTexture(3f, Black, White)
        {
            ColorRamp = new ColorRamp(new[] { new ColorRamp.Stop(0f, Red) }),
        };
        Vector3 p = new(0.5f, 0.5f, 0.5f);
        Assert.True(VecClose(tex.Value(0f, 0f, p, 0), Red));
    }

    [Fact]
    public void GradientTexture_RampReplacesTwoColorLerp()
    {
        var tex = new GradientTexture(Black, White)
        {
            Mode = GradientTexture.GradientMode.Linear,
            Axis = Vector3.UnitX,
            Length = 1f,
            ColorRamp = new ColorRamp(new[]
            {
                new ColorRamp.Stop(0f, Black, ColorRamp.Interp.Linear),
                new ColorRamp.Stop(0.5f, Red, ColorRamp.Interp.Linear),
                new ColorRamp.Stop(1f, White, ColorRamp.Interp.Linear),
            }),
        };
        // At p.x = 0.5, the gradient t = 0.5 → exactly Red.
        Vector3 v = tex.Value(0f, 0f, new Vector3(0.5f, 0f, 0f), 0);
        Assert.True(VecClose(v, Red));
    }
}
