using System.Numerics;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Unit tests for <see cref="BumpMapTexture"/> — the scalar height-field
/// perturbation that drives the <c>bump_map</c> material channel
/// (Arnold/RenderMan/Cycles parity, DEVLOG "surface displacement" step 1/5).
///
/// <para>The tests cover the public <c>SampleTangentNormal</c> contract:</para>
/// <list type="number">
///   <item><description>Flat height fields produce <c>(0,0,1)</c> (no perturbation).</description></item>
///   <item><description>Linear gradients perturb in the expected direction.</description></item>
///   <item><description>Strength and scale scale the gradient amplitude as documented.</description></item>
///   <item><description>Rec.709 luminance reduction is applied per-channel before differentiation.</description></item>
/// </list>
/// </summary>
public class BumpMapTests
{
    // ─── Synthetic inner textures ─────────────────────────────────────────

    /// <summary>Constant grey field: same luminance everywhere → flat gradient.</summary>
    private sealed class ConstGrey : ITexture
    {
        private readonly Vector3 _c;
        public ConstGrey(float v) { _c = new Vector3(v); }
        public Vector3 Value(float u, float v, Vector3 p, int seed) => _c;
    }

    /// <summary>Linear gradient along U: luminance increases with u.</summary>
    private sealed class UGradient : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed) => new(u, u, u);
    }

    /// <summary>Linear gradient along V: luminance increases with v.</summary>
    private sealed class VGradient : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed) => new(v, v, v);
    }

    /// <summary>
    /// Periodic sinusoid in U: <c>h(u) = sin(u)</c>. The gradient is
    /// frequency-dependent, so a UV-scale of <c>k</c> multiplies the
    /// effective gradient by <c>k</c> at fixed UV coordinates (after the
    /// bump-map's internal <c>u' = u · scale</c>).
    /// </summary>
    private sealed class USinusoid : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed)
        {
            float h = 0.5f * (1f + MathF.Sin(u));
            return new Vector3(h);
        }
    }

    /// <summary>Constant pure red — non-grey but luminance is the same everywhere.</summary>
    private sealed class ConstRed : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed) => new(1f, 0f, 0f);
    }

    // ─── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void FlatHeightField_ReturnsUnitZ()
    {
        var bump = new BumpMapTexture(new ConstGrey(0.5f), strength: 1f, scale: 1f);
        Vector3 n = bump.SampleTangentNormal(0.3f, 0.7f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Assert.Equal(0f, n.X, 4);
        Assert.Equal(0f, n.Y, 4);
        Assert.Equal(1f, n.Z, 4);
    }

    [Fact]
    public void PositiveUGradient_TiltsTowardNegativeU()
    {
        var bump = new BumpMapTexture(new UGradient(), strength: 1f, scale: 1f);
        Vector3 n = bump.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Assert.True(n.X < -0.1f, $"expected n.X < -0.1, got {n.X}");
        Assert.InRange(n.Y, -1e-3f, 1e-3f);
        Assert.True(n.Z > 0f);
    }

    [Fact]
    public void PositiveVGradient_TiltsTowardNegativeV()
    {
        var bump = new BumpMapTexture(new VGradient(), strength: 1f, scale: 1f);
        Vector3 n = bump.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Assert.InRange(n.X, -1e-3f, 1e-3f);
        Assert.True(n.Y < -0.1f, $"expected n.Y < -0.1, got {n.Y}");
        Assert.True(n.Z > 0f);
    }

    [Fact]
    public void ZeroStrength_ReturnsUnitZ()
    {
        var bump = new BumpMapTexture(new UGradient(), strength: 0f, scale: 1f);
        Vector3 n = bump.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Assert.Equal(Vector3.UnitZ, n);
    }

    [Fact]
    public void StrengthScalesPerturbation()
    {
        // tan(angle) = |xy| / z grows linearly with strength because the
        // pre-normalise XY is strength·grad and Z is fixed at 1. Use a
        // small strength so the result stays in the linear regime (away
        // from the tangent-saturation limit at strength ≈ 1).
        var b1 = new BumpMapTexture(new UGradient(), strength: 0.05f, scale: 1f);
        var b2 = new BumpMapTexture(new UGradient(), strength: 0.10f, scale: 1f);

        Vector3 n1 = b1.SampleTangentNormal(0.4f, 0.6f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 n2 = b2.SampleTangentNormal(0.4f, 0.6f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);

        float tan1 = MathF.Abs(n1.X) / n1.Z;
        float tan2 = MathF.Abs(n2.X) / n2.Z;
        Assert.True(tan2 > 1.8f * tan1 && tan2 < 2.2f * tan1,
            $"expected tan2 ≈ 2·tan1, got tan1={tan1} tan2={tan2}");
    }

    [Fact]
    public void ScaleFactorShiftsSamplePoint()
    {
        // `scale` is a UV multiplier — it tiles the inner texture more
        // densely over a unit UV square. At UV coordinate u, scale=1
        // samples the texture at u, scale=2 samples at 2u. For a
        // periodic inner field this must produce different gradients:
        // sin'(π/2) = 0  but  sin'(π) = -1, so scale=2 at u=π/2 has a
        // markedly different gradient than scale=1 at u=π/2.
        var b1 = new BumpMapTexture(new USinusoid(), strength: 0.05f, scale: 1f);
        var b2 = new BumpMapTexture(new USinusoid(), strength: 0.05f, scale: 2f);

        float u = MathF.PI / 2f;
        Vector3 n1 = b1.SampleTangentNormal(u, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 n2 = b2.SampleTangentNormal(u, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);

        Assert.True(MathF.Abs(n1.X) < 1e-3f,
            $"expected |n1.X| ≈ 0 at sin' zero, got {n1.X}");
        Assert.True(MathF.Abs(n2.X) > 0.01f,
            $"expected |n2.X| > 0 at non-zero sin', got {n2.X}");
    }

    [Fact]
    public void ConstantNonGreyColor_ReturnsUnitZ()
    {
        // Pure red has Rec.709 luminance 0.2126 — non-zero, but constant in
        // (u, v). The gradient is zero, so the tangent normal is unperturbed.
        // This validates that luminance is computed on the SAMPLED colour, not
        // on a per-channel basis that could leak gradients from non-grey
        // constants.
        var bump = new BumpMapTexture(new ConstRed(), strength: 1f, scale: 1f);
        Vector3 n = bump.SampleTangentNormal(0.2f, 0.8f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Assert.Equal(0f, n.X, 4);
        Assert.Equal(0f, n.Y, 4);
        Assert.Equal(1f, n.Z, 4);
    }

    [Fact]
    public void StrengthClampedToTen()
    {
        // Strength is documented as clamped to [0, 10]. Two textures with
        // strength 10 and 1000 must produce the same tangent normal.
        var bClamped = new BumpMapTexture(new UGradient(), strength: 10f, scale: 1f);
        var bRaw     = new BumpMapTexture(new UGradient(), strength: 1000f, scale: 1f);

        Vector3 nClamped = bClamped.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 nRaw     = bRaw    .SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);

        Assert.Equal(nClamped.X, nRaw.X, 6);
        Assert.Equal(nClamped.Y, nRaw.Y, 6);
        Assert.Equal(nClamped.Z, nRaw.Z, 6);
    }

    [Fact]
    public void NonPositiveScale_CoercedToOne()
    {
        // Scale ≤ 0 is invalid (would silently flip or null the gradient).
        // The constructor coerces it to 1; the result must match a
        // scale=1 sibling.
        var bGood = new BumpMapTexture(new UGradient(), strength: 1f, scale: 1f);
        var bZero = new BumpMapTexture(new UGradient(), strength: 1f, scale: 0f);
        var bNeg  = new BumpMapTexture(new UGradient(), strength: 1f, scale: -3f);

        Vector3 nGood = bGood.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 nZero = bZero.SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);
        Vector3 nNeg  = bNeg .SampleTangentNormal(0.5f, 0.5f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 0);

        Assert.Equal(nGood.X, nZero.X, 6);
        Assert.Equal(nGood.X, nNeg .X, 6);
    }
}
