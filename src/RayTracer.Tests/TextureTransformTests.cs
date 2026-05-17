using System.Numerics;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Tests for the split <see cref="TextureTransform"/> API.
///
/// <para>
/// The transform helpers separate two concerns:
/// <list type="bullet">
///   <item><description><b>Geometric</b>: <c>ApplyManual</c> (YAML offset/rotation)
///     and <c>ApplyRandomRotation</c> (per-instance rotation around origin —
///     preserves <c>‖p‖</c>) drive the spatial layout of the pattern.</description></item>
///   <item><description><b>Noise decorrelation</b>: <c>SeedOffset</c> returns a
///     large per-instance translation (1000 wu default) that the caller adds
///     <i>only</i> to the noise sampling input, never to the geometric point.</description></item>
/// </list>
/// </para>
///
/// <para>
/// The downstream invariants that this split guarantees — wood rings stay
/// concentric, gradient phase is fixed by <c>p</c>, marble vein direction is
/// stable, and noise textures actually decorrelate between instances — are
/// the real contracts. They live in this file too so that any future
/// re-coupling of the two paths is caught immediately.
/// </para>
/// </summary>
public class TextureTransformTests
{
    private const float Eps = 1e-5f;

    private static bool VecClose(Vector3 a, Vector3 b, float eps = Eps)
        => MathF.Abs(a.X - b.X) < eps && MathF.Abs(a.Y - b.Y) < eps && MathF.Abs(a.Z - b.Z) < eps;

    // ───────────────────────────────────────────────────────────────────────
    //  ApplyManual / ApplyRandomRotation / SeedOffset — unit-level invariants
    // ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyManual_IdentityWhenZeroOffsetAndZeroRotation()
    {
        var p = new Vector3(0.3f, -1.2f, 4.5f);
        var q = TextureTransform.ApplyManual(p, Vector3.Zero, Vector3.Zero);
        Assert.True(VecClose(p, q));
    }

    [Fact]
    public void ApplyManual_AppliesOffsetThenRotation()
    {
        var p = new Vector3(1f, 0f, 0f);
        var offset = new Vector3(0f, 1f, 0f);
        // 90° around Z brings (+X, +Y) → (-Y, +X). Input is (1,0,0)+(0,1,0)=(1,1,0)
        // and post-rotation should be (-1, 1, 0).
        var rotation = new Vector3(0f, 0f, 90f);
        var q = TextureTransform.ApplyManual(p, offset, rotation);
        Assert.True(VecClose(q, new Vector3(-1f, 1f, 0f), 1e-5f));
    }

    [Fact]
    public void ApplyRandomRotation_ZeroSeedIsIdentity()
    {
        var p = new Vector3(2f, -1f, 3f);
        var q = TextureTransform.ApplyRandomRotation(p, objectSeed: 0, enabled: true);
        Assert.True(VecClose(p, q));
    }

    [Fact]
    public void ApplyRandomRotation_DisabledIsIdentity()
    {
        var p = new Vector3(2f, -1f, 3f);
        var q = TextureTransform.ApplyRandomRotation(p, objectSeed: 4242, enabled: false);
        Assert.True(VecClose(p, q));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(999)]
    [InlineData(-42)]
    [InlineData(123456)]
    public void ApplyRandomRotation_PreservesNorm(int seed)
    {
        // A rotation around the origin is rigid: ‖p‖ must be invariant. This
        // is the property that keeps wood rings concentric: even with
        // randomize_rotation = true, points at radius r stay at radius r,
        // so the ring index `dist * scale` is unchanged.
        var p = new Vector3(0.3f, -1.2f, 4.5f);
        var q = TextureTransform.ApplyRandomRotation(p, seed, enabled: true);
        Assert.InRange(MathF.Abs(p.Length() - q.Length()), 0f, 1e-4f);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(2024)]
    public void ApplyRandomRotation_DeterministicPerSeed(int seed)
    {
        var p = new Vector3(1f, 2f, 3f);
        var q1 = TextureTransform.ApplyRandomRotation(p, seed, enabled: true);
        var q2 = TextureTransform.ApplyRandomRotation(p, seed, enabled: true);
        Assert.True(VecClose(q1, q2));
    }

    [Fact]
    public void SeedOffset_ZeroSeedReturnsZero()
    {
        var o = TextureTransform.SeedOffset(0, enabled: true);
        Assert.Equal(Vector3.Zero, o);
    }

    [Fact]
    public void SeedOffset_DisabledReturnsZero()
    {
        var o = TextureTransform.SeedOffset(4242, enabled: false);
        Assert.Equal(Vector3.Zero, o);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3001)]
    [InlineData(-7)]
    public void SeedOffset_DeterministicPerSeed(int seed)
    {
        var a = TextureTransform.SeedOffset(seed, enabled: true);
        var b = TextureTransform.SeedOffset(seed, enabled: true);
        Assert.True(VecClose(a, b));
    }

    [Fact]
    public void SeedOffset_DefaultMagnitudeIsBounded()
    {
        // Each component is in [0, magnitude]; the L∞ norm cannot exceed the
        // configured magnitude, no matter the seed.
        for (int seed = 1; seed <= 64; seed++)
        {
            var o = TextureTransform.SeedOffset(seed, enabled: true);
            Assert.InRange(o.X, 0f, TextureTransform.DefaultSeedOffsetMagnitude);
            Assert.InRange(o.Y, 0f, TextureTransform.DefaultSeedOffsetMagnitude);
            Assert.InRange(o.Z, 0f, TextureTransform.DefaultSeedOffsetMagnitude);
        }
    }

    [Fact]
    public void SeedOffset_AdjacentSeedsAreWellSeparated()
    {
        // The whole point of restoring the magnitude to 1000 wu is that 6
        // adjacent seeds (sphere-showcase row 3 uses 3001..3006) sample
        // far-apart regions of Perlin. With the previous 10-wu compromise
        // their average separation was ~1.6 wu; at 1000 wu it should jump
        // by two orders of magnitude.
        float minDist = float.PositiveInfinity;
        for (int a = 3001; a <= 3005; a++)
        {
            var oa = TextureTransform.SeedOffset(a, enabled: true);
            for (int b = a + 1; b <= 3006; b++)
            {
                var ob = TextureTransform.SeedOffset(b, enabled: true);
                float d = (oa - ob).Length();
                if (d < minDist) minDist = d;
            }
        }
        Assert.InRange(minDist, 50f, TextureTransform.DefaultSeedOffsetMagnitude * 2f);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Downstream invariants on the textures themselves
    // ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(999)]
    [InlineData(-42)]
    public void Wood_RingsRemainConcentric_WhenRandomizeOffset(int seed)
    {
        // Sample wood at four points on a circle in the XZ plane (ring axis
        // = Y), all at radius 0.3. Without distortion / knots / figure / aniso
        // / sharpening, the ring index `t` is `frac(dist * scale)` and must
        // be identical for every point on the ring — regardless of the
        // per-instance seed offset that decorrelates the GRAIN noise. The
        // legacy code with offset folded onto `dist` would have broken this.
        var wood = new WoodTexture()
        {
            RingAxis = Vector3.UnitY,
            RandomizeOffset = true,
            // Disable the grain perturbation so we test the radial-geometry
            // invariant in isolation (NoiseStrength = 0 ⇒ rings are pure
            // concentric circles).
            NoiseStrength = 0f,
            Octaves = 1,
            RingSharpness = 1f,
        };
        const float r = 0.3f;
        Vector3 a = new(r, 0f, 0f);
        Vector3 b = new(-r, 0f, 0f);
        Vector3 c = new(0f, 0f, r);
        Vector3 d = new(0f, 0f, -r);
        var va = wood.Value(0f, 0f, a, seed);
        var vb = wood.Value(0f, 0f, b, seed);
        var vc = wood.Value(0f, 0f, c, seed);
        var vd = wood.Value(0f, 0f, d, seed);
        // Allow ample slack for Perlin warp at the ring boundary, but all four
        // points must collapse to the same wood colour because they share
        // `dist`.
        Assert.True(VecClose(va, vb, 1e-3f));
        Assert.True(VecClose(va, vc, 1e-3f));
        Assert.True(VecClose(va, vd, 1e-3f));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(999)]
    public void Gradient_PhaseIsIndependentOfSeed(int seed)
    {
        // A gradient is purely geometric: the `t` parameter at a given world
        // point must NOT depend on per-instance seed randomisation. (Random
        // rotation would change which axis the gradient runs along — we
        // disable it here to test the offset-immunity invariant in isolation.)
        var grad = new GradientTexture(new Vector3(0f), new Vector3(1f))
        {
            Mode = GradientTexture.GradientMode.Linear,
            Axis = Vector3.UnitX,
            Length = 1f,
            RandomizeOffset = true,
            RandomizeRotation = false,
        };
        var p = new Vector3(0.42f, 0.1f, -0.3f);
        var withSeed = grad.Value(0f, 0f, p, seed);
        var withoutSeed = grad.Value(0f, 0f, p, 0);
        Assert.True(VecClose(withSeed, withoutSeed, Eps));
    }

    [Fact]
    public void Noise_TwoSeedsAreDecorrelated()
    {
        // 64 probes × 10 distinct seeds: the mean absolute difference between
        // two seeds' outputs at the same probe must be well above zero. With
        // the previous 10-wu offset adjacent seeds could land on visually
        // identical Perlin regions; at 1000 wu the decorrelation is decisive.
        var noise = new NoiseTexture(scale: 4f)
        {
            RandomizeOffset = true,
            NoiseType = NoiseTexture.NoiseKind.Fbm,
            Octaves = 4,
        };
        const int probes = 64;
        int[] seeds = { 1, 7, 17, 42, 99, 200, 555, 1337, 3001, 99999 };
        double maxPairDiff = 0;
        for (int i = 0; i < seeds.Length; i++)
        {
            for (int j = i + 1; j < seeds.Length; j++)
            {
                double diff = 0;
                for (int k = 0; k < probes; k++)
                {
                    float x = (k * 0.137f) - 4f;
                    float y = (k * 0.041f) + 2f;
                    float z = (k * 0.213f) - 1f;
                    var p = new Vector3(x, y, z);
                    var a = noise.Value(0f, 0f, p, seeds[i]);
                    var b = noise.Value(0f, 0f, p, seeds[j]);
                    diff += MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) + MathF.Abs(a.Z - b.Z);
                }
                diff /= probes * 3;
                if (diff > maxPairDiff) maxPairDiff = diff;
            }
        }
        // A trivial sanity threshold: at least one pair must average more
        // than 0.05 luminance units of difference. Empirically at 1000 wu
        // every pair clears 0.1+; this floor catches accidental re-coupling.
        Assert.True(maxPairDiff > 0.05, $"Max pair diff {maxPairDiff} is too low — instances aren't decorrelating.");
    }

    [Fact]
    public void Brick_GridMembershipIsSeedInvariant()
    {
        // Two seeds with `randomize_offset = true` and NoiseScale = 0 (no
        // weathering) must produce identical brick colours at the same point,
        // because the per-instance offset is now confined to the weathering
        // path. The grid itself stays aligned across instances.
        var brick = new BrickTexture(
            new Vector3(0.7f, 0.3f, 0.2f),
            new Vector3(0.5f, 0.2f, 0.15f),
            new Vector3(0.85f, 0.83f, 0.78f))
        {
            BrickWidth = 0.5f,
            BrickHeight = 0.2f,
            MortarSize = 0.03f,
            RowOffset = 0.5f,
            ColorVariation = 0.4f,
            NoiseScale = 0f, // weathering disabled
            RandomizeOffset = true,
        };
        foreach (int seed in new[] { 0, 1, 99, 4242 })
        {
            for (int k = 0; k < 10; k++)
            {
                var p = new Vector3(k * 0.13f - 0.5f, k * 0.07f - 0.2f, 0f);
                var withSeed = brick.Value(0f, 0f, p, seed);
                var noSeed = brick.Value(0f, 0f, p, 0);
                Assert.True(VecClose(withSeed, noSeed, Eps),
                    $"Brick at {p} differs between seed=0 and seed={seed}: {noSeed} vs {withSeed}");
            }
        }
    }

    [Fact]
    public void Marble_VeinAxisProducesDirectionalPattern_AcrossSeeds()
    {
        // Marble's directional vein term `sin(scale · dot(p, axis) + ...)` must
        // place its peaks at the same `p`-locations regardless of seed: the
        // sine phase is anchored on the object centre. We sample along the
        // vein axis at two seeds and verify the SIGN of `dot(p, axis)` →
        // `t = 0.5 + 0.5 sin(...)` retains an axis-anchored profile (the noise
        // term modulates the height but cannot move the locations
        // catastrophically when noise_strength is moderate).
        var marble = new MarbleTexture(scale: 6f, new Vector3(1f), new Vector3(0f))
        {
            VeinAxis = Vector3.UnitZ,
            VeinFrequency = 1f,
            VeinSharpness = 1f,
            NoiseStrength = 0f, // isolate the directional term
            Octaves = 1,
            RandomizeOffset = true,
        };
        // Two probes a half-period apart along Z: at scale=6, period =
        // 2π/6 ≈ 1.047, so half-period ≈ 0.524. With NoiseStrength = 0 these
        // must have opposite-sign sine outputs and therefore VERY different
        // colours — and that contrast must hold across seeds.
        Vector3 peak = new(0f, 0f, MathF.PI / 12f);   // sin(scale·z) ≈ sin(π/2) = 1
        Vector3 trough = new(0f, 0f, -MathF.PI / 12f); // sin(-π/2) = -1
        foreach (int seed in new[] { 0, 17, 3001, -99 })
        {
            var vp = marble.Value(0f, 0f, peak, seed);
            var vt = marble.Value(0f, 0f, trough, seed);
            // peak should be brighter than trough (t closer to 1 → base colour
            // (1,1,1)); trough should be darker (t closer to 0 → vein (0,0,0)).
            Assert.True(vp.X > vt.X + 0.5f,
                $"seed={seed}: vein direction collapsed (peak={vp}, trough={vt}).");
        }
    }
}
