using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Scene;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// The <c>openpbr</c> material in YAML: every input as a number, a colour or a
/// texture block, the specification's defaults where nothing is written, and
/// the subsurface medium bound to the objects that use the material.
/// </summary>
[Collection("SceneLoader")]
public class OpenPbrLoaderTests
{
    private static HitRecord LoadHit(string materialBody)
    {
        string yaml = $@"
materials:
  - id: ""m""
    type: ""openpbr""
{materialBody}
entities:
  - type: ""sphere""
    center: [0, 0, -3]
    radius: 1.0
    material: ""m""
format_version: 2
cameras:
  - position: [0, 0, 0]
    look_at: [0, 0, -1]
";
        string path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        try
        {
            var (world, _, _, _, _, _) = SceneLoader.Load(path, 64, 64);
            var rec = new HitRecord();
            var ray = new Ray(Vector3.Zero, new Vector3(0, 0, -1));
            Assert.True(world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec));
            Assert.IsType<OpenPbr>(rec.Material);
            return rec;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Defaults_AreTheSpecification()
    {
        var rec = LoadHit("    base_metalness: 0");
        var p = ((OpenPbr)rec.Material!).Parameters;
        Assert.Equal(new Vector3(0.8f), p.BaseColor.Value(in rec));
        Assert.Equal(0.3f, p.SpecularRoughness.Value(in rec));
        Assert.Equal(1.5f, p.SpecularIor.Value(in rec));
        Assert.Equal(1.6f, p.CoatIor.Value(in rec));
        Assert.Equal(0.5f, p.ThinFilmThickness.Value(in rec));
        Assert.Equal(1.4f, p.ThinFilmIor.Value(in rec));
        Assert.Equal(new Vector3(1f, 0.5f, 0.25f), p.SubsurfaceRadiusScale.Value(in rec));
        Assert.Null(rec.MediumIface.Interior);
    }

    [Fact]
    public void Inputs_AcceptNumbersColoursAndTextures()
    {
        var rec = LoadHit(@"    base_color: [0.2, 0.4, 0.6]
    specular_color: 0.5
    specular_roughness: { type: checker, colors: [[0.7, 0.7, 0.7], [0.7, 0.7, 0.7]] }
    coat_weight: 1
    coat_ior: 1.5
    fuzz_color: [1, 0.5, 0.25]");
        var p = ((OpenPbr)rec.Material!).Parameters;
        Assert.Equal(new Vector3(0.2f, 0.4f, 0.6f), p.BaseColor.Value(in rec));
        Assert.Equal(new Vector3(0.5f), p.SpecularColor.Value(in rec));
        Assert.Equal(0.7f, p.SpecularRoughness.Value(in rec), 5);
        Assert.Equal(1f, p.CoatWeight.Value(in rec));
        Assert.Equal(1.5f, p.CoatIor.Value(in rec));
        Assert.Equal(new Vector3(1f, 0.5f, 0.25f), p.FuzzColor.Value(in rec));
    }

    [Fact]
    public void Subsurface_BindsItsMediumToTheObject()
    {
        var rec = LoadHit(@"    subsurface_weight: 1
    subsurface_color: [0.9, 0.6, 0.4]
    subsurface_radius: 0.2");
        var medium = Assert.IsType<HomogeneousMedium>(rec.MediumIface.Interior);
        // Mean free paths 0.2 × (1, 0.5, 0.25).
        Vector3 sigmaT = medium.SigmaA + medium.SigmaS;
        Assert.Equal(5f, sigmaT.X, 3);
        Assert.Equal(20f, sigmaT.Z, 3);
        // A brighter colour needs a higher single-scattering albedo.
        Vector3 albedo = medium.SigmaS / sigmaT;
        Assert.True(albedo.X > albedo.Y && albedo.Y > albedo.Z);
        Assert.True(((OpenPbr)rec.Material!).InteriorIsMedium);
    }

    [Fact]
    public void ThinWalled_HasNoInterior()
    {
        var rec = LoadHit(@"    geometry_thin_walled: true
    subsurface_weight: 1");
        Assert.Null(rec.MediumIface.Interior);
        Assert.True(((OpenPbr)rec.Material!).Parameters.ThinWalled);
    }
}
