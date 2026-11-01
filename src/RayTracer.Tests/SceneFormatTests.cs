using System.IO;
using System.Linq;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Rendering;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// The scene format, version 2 (PIANO-REVIEW.md Phase 10): strict validation
/// (§3.23), the JSON Schema kept current, scene units, colours as hex, the
/// transform order and matrix, the sky's rotation, and the render block.
/// </summary>
[Collection("SceneLoader")]
public class SceneFormatTests
{
    private const string Head = "format_version: 2\n";

    private static (IHittable World, Camera.Camera Camera, List<ILight> Lights, SkySettings Sky) Load(string yaml)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        try
        {
            var (world, camera, lights, sky, _, _) = SceneLoader.Load(path, 32, 32);
            return (world, camera, lights, sky);
        }
        finally { File.Delete(path); }
    }

    private static List<SceneValidator.Issue> Validate(string yaml) => SceneValidator.Validate(yaml);

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public void MissingFormatVersion_IsAnError_PointingAtTheMigration()
    {
        var issues = Validate("materials: []\n");
        var e = Assert.Single(issues, i => i.IsError);
        Assert.Contains("SceneMigrate", e.Message);
    }

    [Fact]
    public void UnknownKeys_AreAllReported_WithLineAndSuggestion()
    {
        var issues = Validate(Head + """
            materials:
              - id: m
                type: metal
                roughnes: 0.2
                refraction_index: 1.5
            lights:
              - type: point
                positon: [0, 1, 0]
            """);
        var errors = issues.Where(i => i.IsError).ToList();
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Line == 5 && e.Message.Contains("did you mean 'roughness'"));
        Assert.Contains(errors, e => e.Line == 6 && e.Message.Contains("format-1 key, now 'ior'"));
        Assert.Contains(errors, e => e.Line == 9 && e.Message.Contains("did you mean 'position'"));
    }

    [Fact]
    public void UnknownTypeAndEnumValues_AreErrors()
    {
        var issues = Validate(Head + """
            entities:
              - type: sphre
            materials:
              - id: m
                type: openpbr
                base_color: { type: voronoi, metric: euclidian }
            """);
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("unknown entity type 'sphre'") && i.Message.Contains("did you mean 'sphere'"));
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("'metric': unknown value 'euclidian'"));
    }

    [Fact]
    public void KeyWithoutEffectOnItsType_IsAWarning()
    {
        var issues = Validate(Head + """
            materials:
              - id: m
                type: lambertian
                roughness: 0.3
            """);
        var w = Assert.Single(issues);
        Assert.False(w.IsError);
        Assert.Contains("no effect on a lambertian material", w.Message);
    }

    [Fact]
    public void WrongValueKinds_AreErrors()
    {
        var issues = Validate(Head + """
            cameras:
              - position: [0, 1, x]
                fov: wide
            render:
              guiding: maybe
            """);
        Assert.Equal(3, issues.Count(i => i.IsError));
    }

    [Fact]
    public void Load_ThrowsWithEveryError()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, Head + "materials:\n  - id: a\n    type: lambertian\n    colour: [1, 0, 0]\n  - id: b\n    type: plastic\n");
        try
        {
            var ex = Assert.Throws<SceneFormatException>(() => SceneLoader.Load(path, 8, 8));
            Assert.Equal(2, ex.Errors.Count);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The checked-in JSON Schema is the one generated from the classes. Run
    /// with UPDATE_SCENE_SCHEMA=1 to rewrite it after changing the format.
    /// </summary>
    [Fact]
    public void CheckedInJsonSchema_IsCurrent()
    {
        string xml = Path.ChangeExtension(typeof(SceneData).Assembly.Location, ".xml");
        Assert.True(File.Exists(xml), $"XML documentation not found next to the assembly: {xml}");
        string generated = SceneSchema.Generate(xml);
        string path = Path.Combine(RepoRoot(), "docs", "reference", "scene-schema.json");
        if (Environment.GetEnvironmentVariable("UPDATE_SCENE_SCHEMA") == "1")
            File.WriteAllText(path, generated);
        Assert.True(File.Exists(path), "docs/reference/scene-schema.json is missing (run with UPDATE_SCENE_SCHEMA=1)");
        Assert.True(File.ReadAllText(path).Replace("\r\n", "\n") == generated,
            "docs/reference/scene-schema.json is out of date: run this test with UPDATE_SCENE_SCHEMA=1");
    }

    /// <summary>Every scene of the repository is a valid format-2 file (no error; warnings allowed).</summary>
    [Fact]
    public void RepositoryScenes_AreValidFormat2()
    {
        string root = RepoRoot();
        var files = new[] { "scenes", Path.Combine("validation", "scenes"), Path.Combine("src", "RayTracer.Tests", "TestScenes") }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.yaml", SearchOption.AllDirectories))
            .ToList();
        Assert.True(files.Count > 100, $"only {files.Count} scenes found");
        var errors = files.SelectMany(f => SceneValidator.Validate(File.ReadAllText(f))
                                                        .Where(i => i.IsError).Select(i => i.Format(f)))
                          .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "3d-ray.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("repository root (3d-ray.slnx) not found");
    }

    // ── Semantics ────────────────────────────────────────────────────────────

    [Fact]
    public void HexColour_IsSrgbDecodedToLinear()
    {
        var (world, _, _, _) = Load(Head + """
            materials:
              - { id: m, type: lambertian, color: "#808080" }
            entities:
              - { type: sphere, center: [0, 0, -3], radius: 1, material: m }
            cameras:
              - { position: [0, 0, 0], look_at: [0, 0, -1] }
            lights: []
            """);
        var rec = new HitRecord();
        Assert.True(world.Hit(new Ray(Vector3.Zero, -Vector3.UnitZ), 1e-3f, 100f, ref rec));
        var albedo = ((Materials.Lambertian)rec.Material!).AovAlbedo(in rec);
        Assert.Equal(Srgb.Decode(128f / 255f), albedo.X, 5);
    }

    /// <summary>
    /// 1 cd seen from 1 m gives 1 lux in any length unit: a point light at
    /// 100 units of a centimetre scene lights like one at 1 unit of a metre scene.
    /// </summary>
    [Fact]
    public void Candela_IsInMetres_WhateverTheSceneUnit()
    {
        static float IrradianceAt(string units, float distance)
        {
            var (_, _, lights, _) = Load(Head + units + $$"""
                lights:
                  - { type: point, position: [0, {{distance}}, 0], intensity: 1 }
                """);
            var p = Assert.IsType<PointLight>(lights.Single());
            return p.Intensity / (distance * distance);
        }
        float metres = IrradianceAt("", 1f);
        float centimetres = IrradianceAt("units: { length: cm }\n", 100f);
        Assert.Equal(metres, centimetres, 4);
    }

    [Fact]
    public void RotateOrder_AndMatrix_GiveTheSameTransform()
    {
        // X 90° then Y 90° maps +Z to +X; zyx order applies Y first: +Z to +X then X leaves it.
        string Entity(string transform) => Head + $$"""
            entities:
              - { type: sphere, center: [0, 0, 1], radius: 0.25, {{transform}} }
            lights: []
            """;
        static Vector3 Centre(IHittable world)
        {
            var box = world.BoundingBox();
            return (box.Min + box.Max) * 0.5f;
        }
        var euler = Centre(Load(Entity("rotate: [90, 90, 0], rotate_order: xyz")).World);
        // Rows of the matrix acting on column vectors: (x, y, z) → (z?, ...). The
        // same rotation written out: R = Ry(90)·Rx(90) in column-vector form.
        var m = Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateRotationY(MathF.PI / 2);
        string rows = string.Join(", ", new[]
        {
            m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42,
            m.M13, m.M23, m.M33, m.M43, m.M14, m.M24, m.M34, m.M44,
        }.Select(v => MathF.Round(v, 6).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var matrix = Centre(Load(Entity($"matrix: [{rows}]")).World);
        Assert.True(Vector3.Distance(euler, matrix) < 1e-4f, $"{euler} vs {matrix}");
        var other = Centre(Load(Entity("rotate: [90, 90, 0], rotate_order: yxz")).World);
        Assert.True(Vector3.Distance(euler, other) > 0.5f, "the order changes the result");
    }

    /// <summary><c>rotate</c> turns the sky as it would an entity: a sun at +X turned 90° about Y ends at −Z.</summary>
    [Fact]
    public void SkyRotate_TurnsTheSkyLikeAnEntity()
    {
        var (_, _, _, sky) = Load(Head + """
            world:
              sky:
                type: gradient
                rotate: [0, 90, 0]
                sun: { direction: [1, 0.0001, 0], luminance: 1000, angular_radius: 1 }
            lights: []
            """);
        var sun = sky.GetAnalyticalSun();
        Assert.NotNull(sun);
        var d = Vector3.Normalize(sun!.Value.DirectionWorld);
        Assert.True(Vector3.Distance(d, -Vector3.UnitZ) < 1e-3f, $"sun at {d}");
    }

    [Fact]
    public void GradientSun_LuminanceIsTheDiscsLuminance_TheColourATint()
    {
        var (_, _, _, sky) = Load(Head + """
            world:
              sky:
                type: gradient
                sun: { direction: [0, 1, 0], luminance: 40, angular_radius: 2, color: [1, 0.5, 0.25] }
            lights: []
            """);
        var (_, radiance, half, _) = sky.GetAnalyticalSun()!.Value;
        Assert.Equal(40f, MathUtils.Luminance(radiance), 3);
        Assert.Equal(2f, half, 4);
    }

    [Fact]
    public void RenderBlock_IsTheFlagsOfTheSameName_AndACliPresetWinsOverItsKeys()
    {
        var r = new RenderData { Quality = "final", Samples = 64, View = "aces", Guiding = true, LightSampling = "bvh" };
        var a = RenderBlock.ToArgs(r, cliQuality: false);
        Assert.Equal(new[] { "--quality", "final", "--samples", "64", "--light-sampling", "bvh", "--guiding", "on", "--view", "aces" }, a);
        var b = RenderBlock.ToArgs(r, cliQuality: true);
        Assert.Equal(new[] { "--guiding", "on", "--view", "aces" }, b);
    }

    [Fact]
    public void NoSky_IsBlack_AndNoLightsMeansNoLights()
    {
        var (_, _, lights, sky) = Load(Head + "entities:\n  - { type: sphere, radius: 1 }\n");
        Assert.Empty(lights);
        Assert.Equal(Vector3.Zero, sky.Sample(new Ray(Vector3.Zero, Vector3.UnitY)));
    }
}
