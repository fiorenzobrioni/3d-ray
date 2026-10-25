using System.Numerics;
using RayTracer.Core;
using RayTracer.Lights;
using RayTracer.Rendering.Sky;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The physical skies (PIANO-REVIEW.md Phase 6): Hosek-Wilkie against the
/// authors' reference C code, Preetham's normalisation, and the sun of every
/// model consistent with its sky.
/// </summary>
public class SkyModelTests
{
    private readonly ITestOutputHelper _out;
    public SkyModelTests(ITestOutputHelper output) => _out = output;

    private static Vector3 SunAt(float elevationDeg)
    {
        float e = elevationDeg * MathF.PI / 180f;
        return new Vector3(MathF.Cos(e), MathF.Sin(e), 0f);
    }

    // { turbidity, albedo, solar elevation°, view θ°, γ°, R, G, B } from
    // ArHosekSkyModel.c (arhosek_rgb_skymodelstate_alloc_init +
    // arhosek_tristim_skymodel_radiance), compiled with gcc.
    public static readonly TheoryData<double, double, double, double, double, double, double, double> Oracle = new()
    {
        { 3.00, 0.30, 30.0, 0.0, 30.0, 3.120635466, 4.910404459, 8.405589169 },
        { 3.00, 0.30, 30.0, 45.0, 10.0, 12.72541431, 14.99868912, 17.62093457 },
        { 3.00, 0.30, 30.0, 80.0, 60.0, 10.18521259, 15.17374617, 20.56605574 },
        { 3.00, 0.30, 30.0, 60.0, 120.0, 3.887611048, 6.995574735, 11.98327103 },
        { 3.00, 0.30, 30.0, 20.0, 5.0, 9.588431838, 11.33607897, 13.74985112 },
        { 3.00, 0.30, 30.0, 89.0, 95.0, 12.24742311, 15.50850549, 15.80883266 },
        { 6.50, 0.10, 10.0, 0.0, 30.0, 4.12637178, 4.480781687, 4.682494966 },
        { 6.50, 0.10, 10.0, 45.0, 10.0, 24.35648449, 19.94069672, 12.70111264 },
        { 6.50, 0.10, 10.0, 80.0, 60.0, 9.515211075, 9.797094811, 8.694303564 },
        { 6.50, 0.10, 10.0, 60.0, 120.0, 3.503562668, 4.924377999, 6.14900743 },
        { 6.50, 0.10, 10.0, 20.0, 5.0, 16.79000123, 14.40725054, 9.73342785 },
        { 6.50, 0.10, 10.0, 89.0, 95.0, 4.901732371, 4.224146881, 2.797287239 },
        { 1.70, 0.80, 70.0, 0.0, 30.0, 3.447883288, 6.478803176, 13.78047024 },
        { 1.70, 0.80, 70.0, 45.0, 10.0, 9.950117399, 14.4532132, 24.0732218 },
        { 1.70, 0.80, 70.0, 80.0, 60.0, 15.76304116, 25.68625891, 39.45666697 },
        { 1.70, 0.80, 70.0, 60.0, 120.0, 6.0350096, 11.72602813, 21.67125615 },
        { 1.70, 0.80, 70.0, 20.0, 5.0, 7.713299384, 10.86833509, 18.44048676 },
        { 1.70, 0.80, 70.0, 89.0, 95.0, 24.73131133, 32.96007043, 38.30456984 },
        { 10.00, 0.50, 2.0, 0.0, 30.0, 1.451932532, 1.343336121, 1.284285829 },
        { 10.00, 0.50, 2.0, 45.0, 10.0, 5.071796239, 3.097916964, 1.961160111 },
        { 10.00, 0.50, 2.0, 80.0, 60.0, 2.137983058, 1.739233981, 1.254504175 },
        { 10.00, 0.50, 2.0, 60.0, 120.0, 2.012087673, 1.938660677, 1.674456544 },
        { 10.00, 0.50, 2.0, 20.0, 5.0, 2.905047585, 2.029683377, 1.508260208 },
        { 10.00, 0.50, 2.0, 89.0, 95.0, 0.4164750359, 0.4115862333, 0.3520935226 },
    };

    [Theory]
    [MemberData(nameof(Oracle))]
    public void HosekWilkie_MatchesReferenceCode(double turbidity, double albedo, double elevation,
        double theta, double gamma, double r, double g, double b)
    {
        var sky = new HosekWilkieSky(SunAt((float)elevation), (float)turbidity, new Vector3((float)albedo));
        Vector3 l = sky.RawRadiance(theta * Math.PI / 180, gamma * Math.PI / 180);
        Assert.Equal(r, l.X, r * 1e-4);
        Assert.Equal(g, l.Y, g * 1e-4);
        Assert.Equal(b, l.Z, b * 1e-4);
    }

    [Fact]
    public void HosekWilkie_EvaluatesTheRawModelInNits()
    {
        // The model's radiance is in W/(m² sr) per channel: 683 lm/W makes nits.
        var sun = SunAt(30f);
        var sky = new HosekWilkieSky(sun, 3f, new Vector3(0.3f));
        var dir = Vector3.Normalize(new Vector3(-0.3f, 0.8f, 0.5f));
        double theta = Math.Acos(dir.Y), gamma = Math.Acos(Vector3.Dot(dir, sun));
        Vector3 raw = sky.RawRadiance(theta, gamma);
        Assert.Equal(raw.Y * 683f, sky.EvaluateRadiance(dir).Y, raw.Y * 0.1f);
    }

    [Theory]
    [InlineData(2f, 20f)]
    [InlineData(3f, 45f)]
    [InlineData(6f, 70f)]
    public void Preetham_ZenithIsTheZenithLuminance(float turbidity, float elevation)
    {
        var sky = new PreethamSky(SunAt(elevation), turbidity);
        float ts = (90f - elevation) * MathF.PI / 180f;
        float chi = (4f / 9f - turbidity / 120f) * (MathF.PI - 2f * ts);
        float yz = (4.0453f * turbidity - 4.9710f) * MathF.Tan(chi) - 0.2155f * turbidity + 2.4192f;
        Assert.Equal(yz, sky.Luminance(Vector3.UnitY), yz * 1e-4f);
    }

    [Fact]
    public void Preetham_GlowsAroundTheSun()
    {
        // The circumsolar term: the sky brightens toward the sun at the same
        // zenith angle (it did not with the shifted coefficients of 3.1).
        var sun = SunAt(40f);
        var sky = new PreethamSky(sun, 3f);
        float near = sky.Luminance(Vector3.Normalize(new Vector3(MathF.Cos(0.75f), MathF.Sin(0.75f), 0.15f)));
        float away = sky.Luminance(Vector3.Normalize(new Vector3(-MathF.Cos(0.75f), MathF.Sin(0.75f), 0.15f)));
        Assert.True(near > 3f * away, $"near {near}, away {away}");
    }

    public static IEnumerable<object[]> Models()
    {
        foreach (float elev in new[] { 15f, 30f, 60f })
        {
            yield return new object[] { "hosek_wilkie", elev };
            yield return new object[] { "preetham", elev };
            yield return new object[] { "nishita", elev };
        }
    }

    private static AnalyticSky Build(string model, float elevation) => model switch
    {
        "hosek_wilkie" => new HosekWilkieSky(SunAt(elevation), 3f),
        "preetham" => new PreethamSky(SunAt(elevation), 3f),
        _ => new NishitaSky(SunAt(elevation), dustDensity: NishitaSky.DustFromTurbidity(3f)),
    };

    /// <summary>
    /// The sun and the sky come from one light: on a clear day (turbidity 3)
    /// the direct sun gives a horizontal plane about what the sky gives with
    /// the sun at 15°, several times it higher up. The sun used to be about
    /// 15 000 times too dim against its sky. Nishita scatters once in a very
    /// clean atmosphere: its sky is about half a measured one, the ratio twice.
    /// </summary>
    [Theory]
    [MemberData(nameof(Models))]
    public void Sun_IsConsistentWithTheSky(string model, float elevation)
    {
        var sky = Build(model, elevation);
        float diffuse = sky.SkyIlluminance - sky.SunIlluminance;
        float ratio = sky.SunIlluminance / diffuse;
        _out.WriteLine($"{model} {elevation}°: sun {sky.SunIlluminance:0.###} sky {diffuse:0.###} ratio {ratio:0.##}");
        Assert.InRange(ratio, model == "nishita" ? 2f : 0.4f, model == "nishita" ? 20f : 8f);
        if (elevation > 15f) Assert.True(ratio > Build(model, 15f).SunIlluminance / (Build(model, 15f).SkyIlluminance - Build(model, 15f).SunIlluminance));
    }

    [Fact]
    public void Photometric_NoonSkyAndSunAreDaylight()
    {
        // Clear sky (turbidity 3), sun at 60°: global horizontal illuminance
        // about 90-120 klux, of which the sky 10-30 klux; the sun's disc about
        // 1.4·10⁹ nits (1.6·10⁹ at the zenith of a very clear day).
        foreach (var model in new[] { "hosek_wilkie", "preetham" })
        {
            var sky = Build(model, 60f);
            _out.WriteLine($"{model}: global {sky.SkyIlluminance:0} lx, sky {sky.SkyIlluminance - sky.SunIlluminance:0} lx, " +
                           $"sun disc {MathUtils.Luminance(sky.AnalyticalSun.Radiance):E2} nits, zenith {MathUtils.Luminance(sky.EvaluateRadiance(Vector3.UnitY)):0} nits");
            Assert.InRange(sky.SunIlluminance, 65_000f, 95_000f);
            Assert.InRange(sky.SkyIlluminance, 85_000f, 125_000f);
            Assert.InRange(sky.SkyIlluminance - sky.SunIlluminance, 8_000f, 35_000f);
            Assert.InRange(MathUtils.Luminance(sky.AnalyticalSun.Radiance), 1.1e9f, 1.7e9f);
        }
        // Nishita's clean air (turbidity 3 is dust 0.9): a brighter sun, half the sky.
        var nishita = Build("nishita", 60f);
        _out.WriteLine($"nishita: global {nishita.SkyIlluminance:0} lx, sky {nishita.SkyIlluminance - nishita.SunIlluminance:0} lx");
        Assert.InRange(nishita.SunIlluminance, 90_000f, 110_000f);
        Assert.InRange(nishita.SkyIlluminance - nishita.SunIlluminance, 3_000f, 10_000f);
    }

    [Fact]
    public void Ground_ReflectsTheSkyAndTheSun()
    {
        var sky = new HosekWilkieSky(SunAt(45f), 3f, new Vector3(0.2f));
        Vector3 ground = sky.EvaluateRadiance(-Vector3.UnitY);
        Assert.Equal(0.2f * sky.SkyIlluminance / MathF.PI, MathUtils.Luminance(ground), 1e-3f * sky.SkyIlluminance);
    }

    [Fact]
    public void LimbDarkening_KeepsTheDiscsMean()
    {
        float cosHalf = MathF.Cos(0.265f * MathF.PI / 180f);
        const int n = 200000;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            // Uniform in solid angle over the cone.
            float cos = 1f - (i + 0.5f) / n * (1f - cosHalf);
            sum += PhysicalSun.LimbFactor(cos, cosHalf);
        }
        Assert.Equal(1.0, sum / n, 2e-3);
        Assert.True(PhysicalSun.LimbFactor(1f, cosHalf) > 1.2f);
        Assert.True(PhysicalSun.LimbFactor(cosHalf, cosHalf) < 0.6f);
    }
}
