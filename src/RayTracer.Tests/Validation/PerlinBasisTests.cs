using System.Numerics;
using RayTracer.Core;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The two lattice-noise bases (PIANO-REVIEW.md §3.30). Improved Perlin has
/// continuous curvature across lattice faces and no preferred direction, and
/// is scaled to the classic basis' standard deviation so the mappings tuned
/// on it still fit; the classic basis is unchanged.
/// </summary>
public class PerlinBasisTests
{
    private static Vector3 RandomPoint(Random rng) =>
        new(rng.NextSingle() * 256f - 128f, rng.NextSingle() * 256f - 128f, rng.NextSingle() * 256f - 128f);

    private static double Std(Perlin n, int count)
    {
        var rng = new Random(1);
        double s = 0, s2 = 0;
        for (int i = 0; i < count; i++)
        {
            double v = n.Noise(RandomPoint(rng));
            s += v; s2 += v * v;
        }
        double m = s / count;
        return Math.Sqrt(s2 / count - m * m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void Improved_HasTheClassicStandardDeviation(int seed)
    {
        double c = Std(new Perlin(seed, PerlinBasis.Classic), 400_000);
        double i = Std(new Perlin(seed, PerlinBasis.Improved), 400_000);
        Assert.InRange(i / c, 0.97, 1.03);
    }

    /// <summary>
    /// Mean change of the second derivative along x across the lattice face
    /// x = n, over the same change in the middle of a cell (same stencil): about
    /// 1 when the curvature is continuous, large when it jumps at the face.
    /// </summary>
    private static double FaceOverInteriorCurvatureChange(Perlin n)
    {
        var rng = new Random(3);
        const float h = 5e-3f;
        double face = 0, interior = 0;
        for (int k = 0; k < 4000; k++)
        {
            var p = RandomPoint(rng);
            float x0 = MathF.Floor(p.X);
            float Second(float x) =>
                (n.Noise(p with { X = x + h }) - 2f * n.Noise(p with { X = x }) + n.Noise(p with { X = x - h })) / (h * h);
            face += Math.Abs(Second(x0 + 2f * h) - Second(x0 - 2f * h));
            interior += Math.Abs(Second(x0 + 0.5f + 2f * h) - Second(x0 + 0.5f - 2f * h));
        }
        return face / interior;
    }

    [Fact]
    public void Improved_CurvatureIsContinuousAcrossLatticeFaces()
    {
        double classic = FaceOverInteriorCurvatureChange(new Perlin(0, PerlinBasis.Classic));
        double improved = FaceOverInteriorCurvatureChange(new Perlin(0, PerlinBasis.Improved));
        Assert.True(classic > 5.0, $"classic face/interior {classic:F2}");
        Assert.True(improved < 2.0, $"improved face/interior {improved:F2}");
    }

    [Fact]
    public void Improved_IsIsotropic_AlongAxesAndDiagonals()
    {
        // Spread of the directional derivative along an axis and along the
        // main diagonal: the same for a noise with no preferred direction.
        var n = new Perlin(0, PerlinBasis.Improved);
        var rng = new Random(5);
        const float h = 1e-3f;
        Vector3 diag = Vector3.Normalize(Vector3.One);
        double sa = 0, sd = 0;
        for (int i = 0; i < 200_000; i++)
        {
            var p = RandomPoint(rng);
            double da = (n.Noise(p + h * Vector3.UnitX) - n.Noise(p - h * Vector3.UnitX)) / (2 * h);
            double dd = (n.Noise(p + h * diag) - n.Noise(p - h * diag)) / (2 * h);
            sa += da * da; sd += dd * dd;
        }
        Assert.InRange(Math.Sqrt(sd / sa), 0.98, 1.02);
    }

    [Theory]
    [InlineData("fbm")]
    [InlineData("ridged")]
    [InlineData("billow")]
    public void FilteredOctaves_FadeToTheMean_WithoutGainingContrast(string kind)
    {
        // An anti-aliasing footprint that keeps 1 of 8 octaves must leave the
        // mean where it was and lower the spread: the dropped octaves count
        // at their mean and the normalisation still spans all eight. (It
        // used to renormalise over the octaves kept, doubling the contrast.)
        var n = new Perlin(0, PerlinBasis.Improved);
        float F(Vector3 p, int evaluated) => kind switch
        {
            "fbm" => n.Fbm(p, 8, 2f, 0.5f, evaluated: evaluated),
            "ridged" => n.Ridged(p, 8, 2f, 0.5f, evaluated: evaluated),
            _ => n.Billow(p, 8, 2f, 0.5f, evaluated: evaluated),
        };
        var rng = new Random(9);
        double mFull = 0, mOne = 0, sFull = 0, sOne = 0;
        const int count = 100_000;
        for (int i = 0; i < count; i++)
        {
            var p = RandomPoint(rng);
            double a = F(p, 8), b = F(p, 1);
            mFull += a; mOne += b; sFull += a * a; sOne += b * b;
        }
        mFull /= count; mOne /= count;
        double stdFull = Math.Sqrt(sFull / count - mFull * mFull), stdOne = Math.Sqrt(sOne / count - mOne * mOne);
        Assert.InRange(mOne - mFull, -0.01, 0.01);
        Assert.True(stdOne <= stdFull * 1.01, $"{kind}: filtered std {stdOne:F4} vs full {stdFull:F4}");
    }

    [Fact]
    public void Classic_IsUnchanged()
    {
        // Values of the basis every earlier render used (seed 0 and 7).
        Assert.Equal(ClassicPin0, new Perlin(0, PerlinBasis.Classic).Noise(new Vector3(0.3f, 1.7f, -2.2f)), 6);
        Assert.Equal(ClassicPin7, new Perlin(7, PerlinBasis.Classic).Noise(new Vector3(10.1f, -3.4f, 5.6f)), 6);
    }

    private const float ClassicPin0 = 0.31277043f;
    private const float ClassicPin7 = 0.28775084f;
}
