using System.Numerics;
using RayTracer.Core;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Spherical-rectangle sampling (Ureña 2013): points on the rectangle,
/// uniform in the solid angle it subtends, density reported consistently.
/// </summary>
public class SphericalRectangleTests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        new object[] { new Vector3(0.2f, -0.5f, 0.1f) },   // close, under the middle
        new object[] { new Vector3(3f, -0.3f, 2f) },       // off to the side, grazing
        new object[] { new Vector3(0.5f, 0.02f, 0.4f) },   // almost in the plane, above it
        new object[] { new Vector3(-0.3f, -8f, 0.6f) },    // far: small solid angle
    };

    private static readonly Vector3 S = new(-1f, 0f, -0.5f), Ex = new(2f, 0f, 0f), Ey = new(0f, 0f, 1.2f);

    [Theory, MemberData(nameof(Cases))]
    public void Samples_AreUniformInSolidAngle(Vector3 p)
    {
        const int nx = 8, ny = 6, n = 200_000;
        float omega = SphericalRectangle.SolidAngle(p, S, Ex, Ey);
        Assert.True(omega >= SphericalRectangle.MinSolidAngle);
        var counts = new double[nx * ny];
        var rng = new Random(11);
        for (int i = 0; i < n; i++)
        {
            Assert.True(SphericalRectangle.TrySample(p, S, Ex, Ey, rng.NextSingle(), rng.NextSingle(), out var q, out var pdf));
            Assert.Equal(1f / omega, pdf, 3);
            float a = Vector3.Dot(q - S, Ex) / Ex.LengthSquared(), b = Vector3.Dot(q - S, Ey) / Ey.LengthSquared();
            Assert.InRange(a, -1e-4f, 1.0001f);
            Assert.InRange(b, -1e-4f, 1.0001f);
            Assert.InRange(MathF.Abs(Vector3.Dot(q - S, Vector3.Cross(Ex, Ey))), 0f, 1e-4f);
            counts[Math.Clamp((int)(a * nx), 0, nx - 1) * ny + Math.Clamp((int)(b * ny), 0, ny - 1)]++;
            // The reported density at the sampled direction matches.
            Assert.Equal(pdf, SphericalRectangle.Pdf(p, S, Ex, Ey, Vector3.Normalize(q - p)), 2);
        }
        double chi2 = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double e = n * SphericalRectangle.SolidAngle(p, S + i * Ex / nx + j * Ey / ny, Ex / nx, Ey / ny) / omega;
                chi2 += (counts[i * ny + j] - e) * (counts[i * ny + j] - e) / e;
            }
        double pValue = BsdfTestKit.RegularizedGammaQ((nx * ny - 1) / 2.0, chi2 / 2.0);
        Assert.True(pValue > 1e-3, $"chi2 {chi2:F1}, p {pValue:E2}");
    }

    [Fact]
    public void SolidAngle_MatchesTheAnalyticSquare()
    {
        // A unit square seen from distance h above its centre:
        // Ω = 4 asin(a² / (a² + 4h²)), a the side.
        float h = 0.7f;
        float expected = 4f * MathF.Asin(1f / (1f + 4f * h * h));
        float got = SphericalRectangle.SolidAngle(new Vector3(0.5f, -h, 0.5f), Vector3.Zero, Vector3.UnitX, Vector3.UnitZ);
        Assert.Equal(expected, got, 5);
    }
}
