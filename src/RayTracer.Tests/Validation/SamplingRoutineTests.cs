using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Phase 1 of PIANO-REVIEW.md: the basic sampling routines draw from the
/// density they claim (chi-square), that density integrates to 1, and the
/// orthonormal basis every frame is built on is orthonormal to float precision.
/// </summary>
public class SamplingRoutineTests
{
    private const double Alpha = 1e-4;

    private static void AssertMatches(Func<Vector3> sample, Func<Vector3, double> pdf, string what, int seed = 3, int sub = 8)
    {
        MathUtils.ReseedThread(seed);
        var (p, integral, chi2, dof) = BsdfTestKit.DirectionalChiSquare(sample, pdf, sub: sub);
        Assert.True(p > Alpha, $"{what}: chi2 {chi2:0.0} / dof {dof}, p {p:E2}");
        Assert.InRange(integral, 0.995, 1.005);
    }

    [Fact]
    public void UniformSphere() =>
        AssertMatches(MathUtils.RandomUnitVector, _ => 1.0 / (4 * Math.PI), "RandomUnitVector");

    [Fact]
    public void UniformHemisphere()
    {
        var n = Vector3.Normalize(new Vector3(0.3f, 0.8f, -0.5f));
        AssertMatches(() => MathUtils.RandomInHemisphere(n),
                      w => Vector3.Dot(w, n) > 0 ? 1.0 / (2 * Math.PI) : 0.0, "RandomInHemisphere");
    }

    [Theory]
    [InlineData(0.5f, 2f)]
    [InlineData(0.05f, 3f)]      // 1° cone: the small-angle branch
    [InlineData(0.005f, 30f)]
    public void SphereCone_IsUniformInsideTheCone(float radius, float distance)
    {
        // Uniform in solid angle inside a cone ⇔ u = (1 − cosθ)/(1 − cosθmax)
        // and φ are uniform on [0, 1) × [0, 2π) in the cone's own frame. The
        // pdf the sampler reports must be 1 / Ω.
        MathUtils.ReseedThread(9);
        var from = new Vector3(0.2f, -0.1f, 0.4f);
        var axis = Vector3.Normalize(new Vector3(0.3f, 1f, -0.2f));
        var centre = from + axis * distance;
        var sphere = new Sphere(centre, radius, new Lambertian(new Vector3(0.5f)));
        double sin2Max = (double)radius * radius / ((double)distance * distance);
        double oneMinusCosMax = 1 - Math.Sqrt(1 - sin2Max);
        MathUtils.OrthonormalBasis(axis, out var b1, out var b2);

        const int ub = 10, pb = 20, n = 200_000;
        var counts = new double[ub * pb];
        for (int i = 0; i < n; i++)
        {
            var (point, _, _, pdf) = sphere.SampleSolidAngle(from);
            Assert.InRange(pdf * 2 * Math.PI * oneMinusCosMax, 0.999, 1.001);
            Vector3 w = point - from;
            double len = w.Length();
            double cos = Vector3.Dot(w, axis) / len;
            // 1 − cosθ without cancellation: sin²θ / (1 + cosθ).
            double perp2 = Math.Pow(Vector3.Dot(w, b1) / len, 2) + Math.Pow(Vector3.Dot(w, b2) / len, 2);
            double u = perp2 / (1 + cos) / oneMinusCosMax;
            double phi = Math.Atan2(Vector3.Dot(w, b2), Vector3.Dot(w, b1)); if (phi < 0) phi += 2 * Math.PI;
            // 1% slack: at 30 units a float point carries ~2e-6 of position error.
            Assert.True(u <= 1.01, $"sample outside the cone (u = {u})");
            counts[Math.Min((int)(u * ub), ub - 1) * pb + Math.Min((int)(phi / (2 * Math.PI) * pb), pb - 1)]++;
        }
        double e = (double)n / counts.Length, chi2 = counts.Sum(c => (c - e) * (c - e) / e);
        double p = BsdfTestKit.RegularizedGammaQ((counts.Length - 1) / 2.0, chi2 / 2.0);
        Assert.True(p > Alpha, $"chi2 {chi2:0.0}, p {p:E2}");
    }

    public static TheoryData<string> Phases() => new() { "iso", "hg0.7", "hg-0.5", "hg0.95", "hg0.0005", "dhg", "dhg-back", "rayleigh", "schlick0.6", "schlick-0.8" };

    private static IPhaseFunction Phase(string name) => name switch
    {
        "iso" => new IsotropicPhase(),
        "hg0.7" => new HenyeyGreensteinPhase(0.7f),
        "hg-0.5" => new HenyeyGreensteinPhase(-0.5f),
        "hg0.95" => new HenyeyGreensteinPhase(0.95f),
        "hg0.0005" => new HenyeyGreensteinPhase(0.0005f),
        "dhg" => new DoubleHenyeyGreensteinPhase(0.8f, -0.4f, 0.7f),
        "dhg-back" => new DoubleHenyeyGreensteinPhase(0.9f, -0.6f, 0.2f),
        "rayleigh" => new RayleighPhase(),
        "schlick0.6" => new SchlickPhase(0.6f),
        "schlick-0.8" => new SchlickPhase(-0.8f),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(Phases))]
    public void PhaseFunction_SamplesItsPdf(string name)
    {
        var phase = Phase(name);
        var wo = Vector3.Normalize(new Vector3(0.2f, -0.9f, 0.4f));
        AssertMatches(() => phase.Sample(wo).Wi, w => phase.Pdf(wo, w), name);
    }

    [Theory]
    [MemberData(nameof(Phases))]
    public void PhaseFunction_WarpKeepsThePairStratified(string name)
    {
        // A phase function is sampled from one stratified pair (Phase 8): the
        // first coordinate alone sets the scattering angle, monotonically (the
        // two-lobe one inside each lobe's share of it), so an even split of the
        // pair is an even split of the angular CDF; the returned pdf is the
        // value, so the weight is exactly 1.
        var phase = Phase(name);
        var wo = Vector3.Normalize(new Vector3(-0.4f, 0.1f, 0.9f));
        float prev = float.NegativeInfinity;
        for (int i = 0; i < 256; i++)
        {
            var (wi, pdf) = phase.Sample(wo, new Vector2((i + 0.5f) / 256f, 0.3f));
            Assert.InRange(wi.Length(), 0.9999f, 1.0001f);
            Assert.Equal(phase.Evaluate(wo, wi), pdf, 1e-5f * MathF.Max(1f, pdf));
            float mu = Vector3.Dot(wo, wi);
            if (!name.StartsWith("dhg"))
                Assert.True(mu >= prev - 1e-4f, $"{name}: cos θ not monotone in u ({prev} then {mu})");
            prev = mu;
        }
    }

    [Fact]
    public void EnvironmentMap_SamplesItsPdf_ContinuouslyInsideTexels()
    {
        // A coarse map (16×8 texels, each wider than a chi-square cell) with a
        // gradient and one bright texel: sampling the texel centres, as before
        // §3.9, piles every sample into a few cells (chi2 1.3e6 on 799 dof).
        // The pdf is piecewise constant over texel edges that cut the cells, so
        // the expected counts need a finer quadrature than a smooth BSDF: with
        // MIS compensation (the default) half the texels drop to zero, and the
        // jumps from zero need 128 sub-cells.
        const int w = 16, h = 8;
        var pixels = new float[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float l = 0.2f + 0.1f * x + 0.3f * y;
                if (x == 5 && y == 2) l = 40f;
                for (int c = 0; c < 3; c++) pixels[(y * w + x) * 3 + c] = l;
            }
        var env = new RayTracer.Textures.EnvironmentMap(pixels, w, h, rotationDeg: 30f);
        int inconsistent = 0;
        AssertMatches(() =>
        {
            var (d, pdf) = env.SampleDirection();
            if (MathF.Abs(pdf - env.PdfDirection(d)) > 1e-3f * pdf) inconsistent++;
            return d;
        }, w => env.PdfDirection(w), "EnvironmentMap", sub: 128);
        // The returned pdf is the one MIS sees from the other strategy (a
        // sample on a texel border may land in its neighbour: a few per mille).
        Assert.True(inconsistent < 200, $"{inconsistent} samples whose pdf disagrees with PdfDirection");
    }

    [Fact]
    public void ConcentricDisk_IsUniformOverTheDisk()
    {
        // Equal-area cells in (r², φ): uniform over the disk means equal counts.
        MathUtils.ReseedThread(5);
        const int rb = 10, pb = 20, n = 200_000;
        var counts = new double[rb * pb];
        for (int i = 0; i < n; i++)
        {
            var d = MathUtils.RandomInUnitDisk();
            double r2 = d.X * d.X + d.Y * d.Y;
            double phi = Math.Atan2(d.Y, d.X); if (phi < 0) phi += 2 * Math.PI;
            Assert.True(r2 <= 1.0 + 1e-6);
            counts[Math.Min((int)(r2 * rb), rb - 1) * pb + Math.Min((int)(phi / (2 * Math.PI) * pb), pb - 1)]++;
        }
        double e = (double)n / counts.Length, chi2 = counts.Sum(c => (c - e) * (c - e) / e);
        double p = BsdfTestKit.RegularizedGammaQ((counts.Length - 1) / 2.0, chi2 / 2.0);
        Assert.True(p > Alpha, $"chi2 {chi2:0.0}, p {p:E2}");
    }

    [Fact]
    public void OrthonormalBasis_IsOrthonormalEverywhere()
    {
        // Half of the normals crowd against −Z, where Frisvad's form failed.
        var rng = new Random(1);
        double worst = 0;
        for (int i = 0; i < 400_000; i++)
        {
            double z = i % 2 == 0 ? rng.NextDouble() * 2 - 1 : -1 + Math.Pow(10, -rng.NextDouble() * 7);
            double r = Math.Sqrt(Math.Max(0, 1 - z * z)), phi = rng.NextDouble() * 2 * Math.PI;
            var n = Vector3.Normalize(new Vector3((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), (float)z));
            MathUtils.OrthonormalBasis(n, out var b1, out var b2);
            worst = Math.Max(worst, new[]
            {
                Math.Abs(Vector3.Dot(b1, n)), Math.Abs(Vector3.Dot(b2, n)), Math.Abs(Vector3.Dot(b1, b2)),
                Math.Abs(b1.Length() - 1), Math.Abs(b2.Length() - 1), (Vector3.Cross(b1, b2) - n).Length(),
            }.Max());
        }
        Assert.True(worst < 1e-5, $"worst orthonormality error {worst:E2}");
    }
}
