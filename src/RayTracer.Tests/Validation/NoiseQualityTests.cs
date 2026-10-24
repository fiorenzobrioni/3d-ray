using System.Numerics;
using RayTracer.Core;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Properties of the procedural noise every procedural texture is built on
/// (PIANO-REVIEW.md, Phase 1): range, zeros on the lattice, continuity of the
/// value and of the gradient across lattice faces (bump maps differentiate
/// noise, so a gradient kink shows as a crease along the grid), isotropy, and
/// for Worley the 1-Lipschitz continuity of F1 that a missed neighbour cell
/// would break.
/// </summary>
public class NoiseQualityTests
{
    private readonly ITestOutputHelper _out;
    public NoiseQualityTests(ITestOutputHelper output) => _out = output;

    private static readonly Perlin Noise = new(7);

    private static Vector3 RandomPoint(Random rng, float extent = 64f) =>
        new((float)(rng.NextDouble() * extent), (float)(rng.NextDouble() * extent), (float)(rng.NextDouble() * extent));

    [Fact]
    public void Perlin_RangeAndLatticeZeros()
    {
        var rng = new Random(1);
        float min = float.MaxValue, max = float.MinValue;
        double sum = 0;
        const int n = 400_000;
        for (int i = 0; i < n; i++)
        {
            float v = Noise.Noise(RandomPoint(rng));
            min = MathF.Min(min, v); max = MathF.Max(max, v); sum += v;
        }
        for (int i = 0; i < 1000; i++)
        {
            var lattice = new Vector3(rng.Next(-50, 50), rng.Next(-50, 50), rng.Next(-50, 50));
            Assert.Equal(0f, Noise.Noise(lattice), 6);
        }
        _out.WriteLine($"perlin range [{min:0.000}, {max:0.000}], mean {sum / n:+0.0000;-0.0000}");
        Assert.InRange(min, -1f, -0.5f);
        Assert.InRange(max, 0.5f, 1f);
        Assert.InRange(sum / n, -0.01, 0.01);
    }

    [Fact]
    public void Perlin_ValueAndGradientAreContinuousAcrossLatticeFaces()
    {
        // Straddle the face x = integer with a small step h: the value and the
        // first derivative must match on both sides (C1).
        var rng = new Random(2);
        const float h = 1e-3f;
        double worstValueJump = 0, worstGradJump = 0, worstCurvJump = 0;
        for (int i = 0; i < 20000; i++)
        {
            var p = RandomPoint(rng);
            p.X = MathF.Floor(p.X);
            float l2 = Noise.Noise(p - new Vector3(2 * h, 0, 0)), l1 = Noise.Noise(p - new Vector3(h, 0, 0));
            float c = Noise.Noise(p);
            float r1 = Noise.Noise(p + new Vector3(h, 0, 0)), r2 = Noise.Noise(p + new Vector3(2 * h, 0, 0));
            worstValueJump = Math.Max(worstValueJump, Math.Abs((r1 - c) - (c - l1)) / h);   // slope mismatch ~ kink
            double gl = (c - l1) / h, gr = (r1 - c) / h;
            worstGradJump = Math.Max(worstGradJump, Math.Abs(gr - gl));
            double kl = (c - 2 * l1 + l2) / (h * h), kr = (r2 - 2 * r1 + c) / (h * h);
            worstCurvJump = Math.Max(worstCurvJump, Math.Abs(kr - kl));
        }
        _out.WriteLine($"perlin across lattice faces: gradient jump {worstGradJump:0.000}, curvature jump {worstCurvJump:0.00}");
        // C1: a one-sided difference over h differs by O(h · curvature) only.
        Assert.True(worstGradJump < 0.05, $"gradient jump {worstGradJump:0.000}");
    }

    [Fact]
    public void Perlin_IsIsotropic()
    {
        // Variance of the directional derivative along an axis and along a
        // diagonal: equal for isotropic noise.
        var rng = new Random(3);
        const float h = 1e-3f;
        var axis = Vector3.UnitX;
        var diag = Vector3.Normalize(new Vector3(1, 1, 1));
        double va = 0, vd = 0;
        const int n = 200_000;
        for (int i = 0; i < n; i++)
        {
            var p = RandomPoint(rng);
            double da = (Noise.Noise(p + h * axis) - Noise.Noise(p - h * axis)) / (2 * h);
            double dd = (Noise.Noise(p + h * diag) - Noise.Noise(p - h * diag)) / (2 * h);
            va += da * da; vd += dd * dd;
        }
        double ratio = vd / va;
        _out.WriteLine($"perlin derivative variance diagonal/axis: {ratio:0.000}");
        Assert.InRange(ratio, 0.9, 1.1);
    }

    [Fact]
    public void Worley_F1IsOneLipschitz_AndOrdered()
    {
        var w = new WorleyNoise(5);
        var rng = new Random(4);
        double worst = 0;
        for (int i = 0; i < 100_000; i++)
        {
            var p = RandomPoint(rng, 32f);
            var d = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            const float step = 0.01f;
            w.Evaluate(p, WorleyNoise.Metric.Euclidean, 1f, out float f1a, out float f2a, out _);
            w.Evaluate(p + d * step, WorleyNoise.Metric.Euclidean, 1f, out float f1b, out _, out _);
            Assert.True(f1a >= 0f && f2a >= f1a);
            worst = Math.Max(worst, Math.Abs(f1b - f1a) / step);
        }
        _out.WriteLine($"worley F1 Lipschitz constant: {worst:0.0000}");
        // A distance to a point set changes no faster than the point moves.
        Assert.True(worst <= 1.001, $"F1 jumped: Lipschitz {worst:0.000}");
    }
}
