using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Emission sampling for the bidirectional integrators (PIANO-REVIEW.md Phase
/// 8b): every light's subpaths carry its total flux on average, and the
/// density each ray is drawn with is the one <see cref="ILight.EmissionPdfOverCos"/>
/// reports for it.
/// </summary>
[Collection("SamplerExclusive")]
public class EmissionTests
{
    private readonly ITestOutputHelper _out;
    public EmissionTests(ITestOutputHelper o)
    {
        _out = o;
        Sampler.SetKind(SamplerKind.Prng);
    }

    private static readonly EmissionContext Ctx = new(new Vector3(0.3f, -0.2f, 0.1f), 7f);
    private static readonly Vector3 Tint = new(1f, 0.6f, 0.3f);

    /// <summary>The light and its flux (luminance-weighted), known in closed form.</summary>
    public static TheoryData<string> Lights() => new()
    {
        "point", "point-ies", "spot", "spot-disc", "area", "sphere", "geometry", "directional",
        "directional-disc", "sun", "sky-flat",
    };

    private static (ILight Light, double Flux) Make(string name)
    {
        double lum = MathUtils.Luminance(Tint);
        double crossSection = Math.PI * Ctx.SceneRadius * Ctx.SceneRadius;
        switch (name)
        {
            case "point":
                return (new PointLight(new Vector3(1, 2, 3), Tint, 2f), 4 * Math.PI * lum * 2);
            case "point-ies":
            {
                var ies = IesProfileTests.ConeProfile();
                return (new PointLight(new Vector3(1, 2, 3), Tint, 2f, ies: ies), ies.Flux * lum * 2);
            }
            case "spot":
            case "spot-disc":
            {
                var spot = new SpotLight(new Vector3(0, 3, 0), new Vector3(0.2f, -1, 0.1f), Tint, 5f, 20f, 35f,
                                         softRadius: name == "spot-disc" ? 0.3f : 0f, shadowSamples: name == "spot-disc" ? 4 : 1);
                double ci = Math.Cos(20 * Math.PI / 180), co = Math.Cos(35 * Math.PI / 180);
                return (spot, lum * 5 * (2 * Math.PI * (1 - ci) + 2 * Math.PI * (ci - co) / 3));
            }
            case "area":
                return (new AreaLight(new Vector3(-1, 3, -1), new Vector3(2, 0, 0), new Vector3(0, 0, 1.5f), Tint, 4f),
                        Math.PI * 3 * lum * 4);
            case "sphere":
                return (new SphereLight(new Vector3(0, 1, 0), 0.4f, Tint, 6f), Math.PI * 4 * Math.PI * 0.16 * lum * 6);
            case "geometry":
                return (new GeometryLight(new Sphere(new Vector3(1, 0, 0), 0.5f, new Emissive(Tint, 3f)), new Emissive(Tint, 3f)),
                        Math.PI * 4 * Math.PI * 0.25 * lum * 3);
            case "directional":
                return (new DirectionalLight(new Vector3(0.3f, -1, 0.2f), Tint, 3f), lum * 3 * crossSection);
            case "directional-disc":
                return (new DirectionalLight(new Vector3(0.3f, -1, 0.2f), Tint, 3f, angularRadiusDeg: 5f), lum * 3 * crossSection);
            case "sun":
            {
                var sun = new PhysicalSun(Vector3.Normalize(new Vector3(0.4f, 0.7f, 0.2f)), Tint, 1000f, 0.5f);
                double omega = 2 * Math.PI * (1 - Math.Cos(0.5 * Math.PI / 180));
                return (sun, lum * 1000 * omega * crossSection);
            }
            case "sky-flat":
                return (new EnvironmentLight(new SkySettings(Tint * 0.5f)), 4 * Math.PI * lum * 0.5 * crossSection);
        }
        throw new ArgumentException(name);
    }

    [Theory]
    [MemberData(nameof(Lights))]
    public void Emission_CarriesTheLightsFlux_AndReportsItsDensity(string name)
    {
        var (light, flux) = Make(name);
        MathUtils.ReseedThread(11);
        var rng = new Random(5);
        const int N = 400_000;
        double sum = 0, sum2 = 0;
        int mismatched = 0;
        for (int i = 0; i < N; i++)
        {
            var uPos = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            var uDir = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            double x = 0;
            if (light.SampleEmission(Ctx, uPos, uDir, out LightEmission e))
            {
                x = MathUtils.Luminance(e.Radiance) * e.Cos / (e.PdfPos * e.PdfDir);
                float reported = light.EmissionPdfOverCos(Ctx, e.Position, e.Direction);
                float own = e.PdfPos * e.PdfDir / e.Cos;
                if (MathF.Abs(reported - own) > 1e-3f * own) mismatched++;
            }
            sum += x;
            sum2 += x * x;
        }
        double mean = sum / N, sd = Math.Sqrt(Math.Max(0, sum2 / N - mean * mean) / N);
        _out.WriteLine($"{name}: flux {mean:0.000} ± {sd:0.000} vs {flux:0.000}; density mismatches {mismatched}");
        // The sun's limb darkening is normalised over the disc to 0.3%.
        double tol = name == "sun" ? 4e-3 : 2e-3;
        Assert.True(Math.Abs(mean - flux) < 5 * sd + tol * flux, $"{mean} vs {flux}");
        Assert.True(mismatched < N / 1000, $"{mismatched} samples whose density disagrees");
    }

    [Fact]
    public void Emission_DirectionsFollowTheReportedDensity()
    {
        // Chi-square of the directions of an area light, a spot and the sky
        // against the density over the cosine, times the cosine.
        var area = new AreaLight(new Vector3(-1, 3, -1), new Vector3(2, 0, 0), new Vector3(0, 0, 1.5f), Tint, 4f);
        var spot = new SpotLight(new Vector3(0, 3, 0), new Vector3(0.2f, -1, 0.1f), Tint, 5f, 20f, 35f);
        foreach (var (light, n) in new (ILight, Vector3)[] { (area, Vector3.Normalize(Vector3.Cross(area.U, area.V))), (spot, Vector3.Zero) })
        {
            var rng = new Random(9);
            Vector3 origin = Vector3.Zero;
            var (p, integral, chi2, dof) = BsdfTestKit.DirectionalChiSquare(
                () =>
                {
                    light.SampleEmission(Ctx, new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                                         new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()), out var e);
                    origin = e.Position;
                    return e.Direction;
                },
                d =>
                {
                    float cos = n == Vector3.Zero ? 1f : MathF.Max(0f, Vector3.Dot(n, d));
                    float pdfPos = light is AreaLight ? 1f / 3f : 1f;
                    return light.EmissionPdfOverCos(Ctx, origin, d) * cos / pdfPos;
                }, sub: 64);
            _out.WriteLine($"{light.GetType().Name}: chi2 {chi2:0.0} / {dof}, p {p:E2}, ∫ {integral:0.0000}");
            Assert.True(p > 1e-4, $"{light.GetType().Name}: p {p}");
            Assert.InRange(integral, 0.99, 1.01);
        }
    }
}
