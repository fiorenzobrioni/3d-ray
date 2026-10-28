using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Photometric light units and the physical camera exposure
/// (PIANO-REVIEW.md §3.14, §3.34).
/// </summary>
[Collection("SceneLoader")]
public class PhysicalUnitsTests
{
    private static List<ILight> LoadLights(string lightsYaml, string extra = "")
    {
        string yaml = $@"
format_version: 2
cameras:
  - position: [0, 0, 5]
    look_at: [0, 0, 0]
{extra}
lights:
{lightsYaml}
entities: []
";
        string path = Path.Combine(Path.GetTempPath(), $"3dray-units-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try { return SceneLoader.Load(path, 16, 16).Item3; }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Blackbody_HasLuminanceOne_AndRunsWarmToCool()
    {
        foreach (float k in new[] { 1900f, 2700f, 4000f, 6500f, 10000f })
            Assert.Equal(1f, MathUtils.Luminance(LightUnits.Blackbody(k)), 4);
        Vector3 warm = LightUnits.Blackbody(2700f), white = LightUnits.Blackbody(6504f), cool = LightUnits.Blackbody(12000f);
        Assert.True(warm.X > warm.Y && warm.Y > warm.Z);
        Assert.True(cool.Z > cool.Y && cool.Y > cool.X);
        // The black body at 6504 K sits just off D65 (it is not on the daylight locus).
        Assert.InRange(white.X, 0.95f, 1.08f);
        Assert.InRange(white.Z, 0.92f, 1.05f);
    }

    [Fact]
    public void PointAndSpot_PowerAndCandela()
    {
        var lights = LoadLights(@"
  - { type: point, position: [0, 3, 0], power: 800, power_unit: lumen, color: [1, 0.5, 0.2] }
  - { type: point, position: [0, 3, 0], power: 10 }
  - { type: point, position: [0, 3, 0], intensity: 100, exposure: 1 }
  - { type: spot, position: [0, 3, 0], power: 800, power_unit: lumen }");
        var p0 = (PointLight)lights[0];
        Assert.Equal(800f / (4f * MathF.PI), p0.Intensity, 3);
        Assert.Equal(1f, MathUtils.Luminance(p0.Color), 4);              // the colour only tints
        Assert.Equal(10f * 683f / (4f * MathF.PI), ((PointLight)lights[1]).Intensity, 2);
        Assert.Equal(200f, ((PointLight)lights[2]).Intensity, 3);          // 100 cd, +1 stop
        Assert.Equal(800f / (4f * MathF.PI), ((SpotLight)lights[3]).Intensity, 3);
    }

    [Fact]
    public void AreaSphereDirectional_LuminancePowerIlluminance()
    {
        var lights = LoadLights(@"
  - { type: area, corner: [-1, 3, 0], u: [2, 0, 0], v: [0, 0, 1], power: 1000, power_unit: lumen }
  - { type: area, corner: [-1, 3, 0], u: [2, 0, 0], v: [0, 0, 1], luminance: 5000 }
  - { type: area, corner: [-1, 3, 0], u: [2, 0, 0], v: [0, 0, 1], power: 10 }
  - { type: sphere, position: [0, 3, 0], radius: 0.25, power: 1000, power_unit: lumen }
  - { type: directional, direction: [0, -1, 0], illuminance: 100000, temperature: 5800 }");
        Assert.Equal(1000f / (MathF.PI * 2f), ((AreaLight)lights[0]).Intensity, 2);
        Assert.Equal(5000f, ((AreaLight)lights[1]).Intensity, 2);
        Assert.Equal(10f * LightUnits.LumensPerWatt / (MathF.PI * 2f), ((AreaLight)lights[2]).Intensity, 2);
        Assert.Equal(1000f / (4f * MathF.PI * MathF.PI * 0.0625f), ((SphereLight)lights[3]).Intensity, 1);
        var sun = (DirectionalLight)lights[4];
        Assert.Equal(100000f, sun.Intensity, 1);
        Assert.Equal(1f, MathUtils.Luminance(sun.Color), 4);
        Assert.True(sun.Color.X > sun.Color.Z);

        // The flux the samples deliver is the lumens asked for.
        Assert.InRange(LightPowerTests.MeasuredFlux(lights[0], new Vector3(0, 3, 0.5f)), 970f, 1030f);
        Assert.InRange(LightPowerTests.MeasuredFlux(lights[3], new Vector3(0, 3, 0)), 970f, 1030f);
    }

    [Fact]
    public void Emissive_Luminance_AndTemperature()
    {
        string yaml = @"
format_version: 2
cameras: [{ position: [0, 0, 5], look_at: [0, 0, 0] }]
lights: []
materials:
  - { id: glow, type: emissive, color: [1, 0.5, 0.2], luminance: 3000 }
  - { id: warm, type: emissive, luminance: 2, temperature: 3000 }
entities:
  - { type: sphere, center: [-1, 0, 0], radius: 0.4, material: glow }
  - { type: sphere, center: [1, 0, 0], radius: 0.4, material: warm }
";
        string path = Path.Combine(Path.GetTempPath(), $"3dray-units-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var lights = SceneLoader.Load(path, 16, 16).Item3.OfType<GeometryLight>().ToList();
            Assert.Equal(2, lights.Count);
            var glow = lights.Single(g => g.Geometry is Sphere { Center.X: < 0 }).Material;
            var warm = lights.Single(g => g.Geometry is Sphere { Center.X: > 0 }).Material;
            Assert.Equal(3000f, MathUtils.Luminance(glow.EmissionAt(0.5f, 0.5f, Vector3.Zero)), 0);
            Vector3 w = warm.EmissionAt(0.5f, 0.5f, Vector3.Zero);
            Assert.Equal(2f, MathUtils.Luminance(w), 3);
            Assert.True(w.X > w.Y && w.Y > w.Z);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Camera_Ev100_FromIsoShutterAndFStop()
    {
        // Sunny 16: f/16, 1/100 s, ISO 100 → EV100 = log2(256 / 0.01) = 14.64.
        Assert.Equal(14.644f, LightUnits.Ev100(16f, 0.01f, 100f), 3);
        // A white (0.8) diffuse surface under 100 000 lux reads 0.83 of full scale.
        float L = 0.8f * 100000f / MathF.PI;
        float exposure = MathF.Pow(2f, LightUnits.ExposureEvFromEv100(LightUnits.Ev100(16f, 0.01f, 100f)));
        Assert.InRange(L * exposure, 0.82f, 0.84f);

        string yaml = @"
format_version: 2
cameras: [{ position: [0, 0, 5], look_at: [0, 0, 0], focal_length: 50, f_stop: 16, iso: 100, shutter_speed: 0.01 }]
lights: []
entities: []
";
        string path = Path.Combine(Path.GetTempPath(), $"3dray-units-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var cam = SceneLoader.Load(path, 16, 16).Item2;
            Assert.Equal(LightUnits.ExposureEvFromEv100(14.644f), cam.PhysicalExposureEv!.Value, 2);
        }
        finally { File.Delete(path); }
    }
}
