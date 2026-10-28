using System.IO;
using System.Linq;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering.Sky;
using RayTracer.Scene;
using RayTracer.Textures;
using SceneMigrate;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// SceneMigrate (PIANO-REVIEW.md Phase 10): a format-1 scene converted to
/// format 2 keeps its comments, validates, and builds the materials and
/// lights format 1 built. The whole repository was checked render by render
/// (v1 engine on the v1 file against v2 on the migrated one); these tests
/// pin the conversions that carry arithmetic.
/// </summary>
[Collection("SceneLoader")]
public class SceneMigrateTests
{
    private static string Migrate(string v1)
    {
        var m = new Migrator("test.yaml", v1);
        string v2 = m.Migrate(schemaComment: null);
        var errors = SceneValidator.Validate(v2).Where(i => i.IsError).Select(i => i.Message).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n---\n" + v2);
        return v2;
    }

    private static (IHittable World, List<ILight> Lights, Rendering.SkySettings Sky) Load(string v2)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, v2);
        try
        {
            var (world, _, lights, sky, _, _) = SceneLoader.Load(path, 32, 32);
            return (world, lights, sky);
        }
        finally { File.Delete(path); }
    }

    private static OpenPbr HitMaterial(IHittable world)
    {
        var rec = new HitRecord();
        Assert.True(world.Hit(new Ray(Vector3.Zero, -Vector3.UnitZ), 1e-3f, 100f, ref rec));
        return Assert.IsType<OpenPbr>(rec.Material);
    }

    private const string Sphere = """
        entities:
          - { type: sphere, center: [0, 0, -3], radius: 1, material: m }
        camera:
          position: [0, 0, 0]
          look_at: [0, 0, -1]
        lights: []
        """;

    [Fact]
    public void KeepsComments_ConvertsTheCamera_AndAddsTheVersion()
    {
        string v2 = Migrate("""
            # A scene
            camera:
              position: [0, 1, 5]   # eye
              vup: [0, 1, 0]
              focal_dist: 4
            lights: []
            """);
        Assert.StartsWith("# A scene\nformat_version: 2\ncameras:\n  - position: [0, 1, 5]   # eye\n", v2);
        Assert.Contains("    up: [0, 1, 0]", v2);
        Assert.Contains("    focus_distance: 4", v2);
    }

    [Theory]
    [InlineData("", 1f)]                        // spec_trans unwritten: format 1 made it 1
    [InlineData("    spec_trans: 0.0\n", 0f)]  // an opaque polished stone stays opaque
    [InlineData("    spec_trans: 0.5\n", 0.5f)]
    public void DisneySubsurface_BecomesOpenPbrSubsurface(string specTrans, float weight)
    {
        string v2 = Migrate("materials:\n  - id: m\n    type: disney\n    color: [0.9, 0.9, 0.88]\n" +
                            "    subsurface_radius: [0.4, 0.3, 0.2]\n    subsurface_scale: 0.5\n" + specTrans + Sphere);
        var p = HitMaterial(Load(v2).World).Parameters;
        Assert.Equal(weight, p.SubsurfaceWeight.ConstantValue, 5);
        Assert.Equal(new Vector3(0.2f, 0.15f, 0.1f), p.SubsurfaceRadiusScale.Value(0, 0, Vector3.Zero, 0));
        Assert.Equal(1f, p.SubsurfaceRadius.ConstantValue);
    }

    /// <summary>The migrated material has the parameters <see cref="DisneyMaterial"/> gave the format-1 one.</summary>
    [Fact]
    public void Disney_BecomesTheOpenPbrDisneyMaterialBuilt()
    {
        string v2 = Migrate("""
            materials:
              - id: m
                type: disney
                color: [0.8, 0.3, 0.1]
                roughness: 0.4
                specular: 0.3
                specular_tint: 0.5
                sheen: 0.6
                clearcoat: 0.7
                clearcoat_gloss: 0.6
                anisotropic: 0.5
                anisotropic_rotation: 0.25
                thin_film_thickness: 400
            """ + "\n" + Sphere);
        var migrated = HitMaterial(Load(v2).World).Parameters;
        var reference = DisneyMaterial.Create(new SolidColor(new Vector3(0.8f, 0.3f, 0.1f)),
            roughness: 0.4f, specular: 0.3f, specularTint: 0.5f, sheen: 0.6f, clearcoat: 0.7f,
            clearcoatGloss: 0.6f, anisotropic: 0.5f, anisotropicRotation: 0.25f, thinFilmThickness: 400f).Parameters;
        var rec = new HitRecord();
        static float F(FloatTexture t) => t.ConstantValue;
        Assert.Equal(F(reference.SpecularWeight), F(migrated.SpecularWeight), 5);
        Assert.Equal(F(reference.SpecularRoughness), F(migrated.SpecularRoughness), 5);
        Assert.Equal(F(reference.SpecularRoughnessAnisotropy), F(migrated.SpecularRoughnessAnisotropy), 5);
        Assert.Equal(F(reference.SpecularRotation), F(migrated.SpecularRotation), 5);
        Assert.Equal(F(reference.CoatWeight), F(migrated.CoatWeight), 5);
        Assert.Equal(F(reference.CoatRoughness), F(migrated.CoatRoughness), 5);
        Assert.Equal(F(reference.CoatIor), F(migrated.CoatIor), 5);
        Assert.Equal(F(reference.FuzzWeight), F(migrated.FuzzWeight), 5);
        Assert.Equal(F(reference.FuzzRoughness), F(migrated.FuzzRoughness), 5);
        Assert.Equal(F(reference.ThinFilmThickness), F(migrated.ThinFilmThickness), 5);
        Assert.Equal(F(reference.ThinFilmIor), F(migrated.ThinFilmIor), 5);
        Assert.True(Vector3.Distance(reference.SpecularColor.Value(in rec), migrated.SpecularColor.Value(in rec)) < 1e-5f);
        Assert.True(Vector3.Distance(reference.FuzzColor.Value(in rec), migrated.FuzzColor.Value(in rec)) < 1e-5f);
    }

    /// <summary>A tinted light keeps its output: the unitless intensity times the colour's luminance.</summary>
    [Fact]
    public void LightIntensity_BecomesItsPhotometricQuantity()
    {
        var c = new Vector3(1f, 0.5f, 0.25f);
        string v2 = Migrate("""
            lights:
              - { type: point, position: [0, 2, 0], color: [1, 0.5, 0.25], intensity: 10 }
              - type: area
                corner: [0, 3, 0]
                u: [1, 0, 0]
                v: [0, 0, 2]
                color: [1, 0.5, 0.25]
                intensity: 4
                normalize: true
              - { type: sun, direction: [0, -1, 0], color: [1, 0.5, 0.25], intensity: 3 }
            """);
        var lights = Load(v2).Lights;
        var point = Assert.IsType<PointLight>(lights[0]);
        Assert.True(Vector3.Distance(c * 10f, point.Color * point.Intensity) < 1e-4f);
        var dir = Assert.IsType<DirectionalLight>(lights[2]);
        Assert.Contains("type: directional", v2);
        Assert.Contains("luminance: 1.", v2);   // 4 / area 2 × luminance of the colour
        Assert.NotNull(dir);
    }

    /// <summary>A physical sky of the format-1 relative scale is the photometric one, dimmed by an exposure.</summary>
    [Fact]
    public void RelativePhysicalSky_BecomesAnExposure()
    {
        string v2 = Migrate("world:\n  sky:\n    type: physical\n    sun: { direction: [0, 1, 1] }\nlights: []\n");
        Assert.Contains("type: hosek_wilkie", v2);
        var sky = Assert.IsAssignableFrom<AnalyticSky>(Load(v2).Sky.LightingModel);
        Assert.Equal(1f / 1000f, sky.Intensity, 6);
    }

    /// <summary>
    /// A block commented out whose keys are also removed (a sun on a flat sky):
    /// the "# " of a removed line once shifted the removal, leaving its last
    /// character behind.
    /// </summary>
    [Fact]
    public void RemovedKeysInsideACommentedOutBlock_LeaveNothingBehind()
    {
        string v2 = Migrate("world:\n  sky:\n    type: flat\n    sun:\n      direction: [0, 1, 0]   # up\n" +
                            "      color: [1, 1, 1]   # tint (a comment)\n      size: 3.0   # deg\n" +
                            "      shadow_samples: 4\nlights: []\n");
        Assert.Contains("    # sun:\n    #   direction: [0, 1, 0]   # up\n    #   shadow_samples: 4\n", v2);
        Assert.DoesNotContain("\n)\n", v2);
    }

    [Fact]
    public void KeysFormatOneIgnored_AreCommentedOut()
    {
        string v2 = Migrate("materials:\n  - id: m\n    type: lambertian\n    albedo: [1, 0, 0]\n    color: [0.5, 0.5, 0.5]\nlights: []\n");
        Assert.Contains("# albedo: [1, 0, 0]", v2);
    }
}
