using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Regression coverage for the material-embedded SSS auto-promotion in
/// <c>SceneLoader.ApplyEmbeddedSssDefaults</c>. A Disney material that
/// declares <c>subsurface_radius</c> but no <c>spec_trans</c> gets its
/// transmission lobe auto-promoted to 1.0 (so the random-walk SSS medium can
/// activate). The bug being guarded: an <em>explicit</em> <c>spec_trans: 0</c>
/// — i.e. an opaque polished marble that still wants the parameter present —
/// must be honoured rather than silently overwritten to 1.0. This is the
/// difference between an opaque stone and an unintended glass-like look.
/// </summary>
public class EmbeddedSssSpecTransTests
{
    private static DisneyBsdf LoadMaterial(string materialBody)
    {
        string yaml = $@"
materials:
  - id: ""m""
    type: ""disney""
    color: [0.9, 0.9, 0.88]
{materialBody}
entities:
  - type: ""sphere""
    center: [0, 0, -3]
    radius: 1.0
    material: ""m""
camera:
  position: [0, 0, 0]
  look_at: [0, 0, -1]
";
        string path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        try
        {
            var (world, _, _, _, _) = SceneLoader.Load(path, 64, 64);
            var rec = new HitRecord();
            var ray = new Ray(Vector3.Zero, Vector3.Normalize(new Vector3(0, 0, -1)));
            world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec);
            Assert.NotNull(rec.Material);
            return Assert.IsType<DisneyBsdf>(rec.Material);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SubsurfaceRadius_NoSpecTrans_AutoPromotesToOne()
    {
        var bsdf = LoadMaterial("    subsurface_radius: [0.4, 0.3, 0.2]");
        Assert.True(bsdf.SpecTrans.IsConstant);
        Assert.Equal(1f, bsdf.SpecTrans.ConstantValue, 5);
    }

    [Fact]
    public void SubsurfaceRadius_ExplicitZeroSpecTrans_IsHonoured()
    {
        // Opaque polished marble: keeps subsurface_radius authored but must
        // stay opaque (spec_trans: 0 not promoted to 1.0).
        var bsdf = LoadMaterial("    subsurface_radius: [0.4, 0.3, 0.2]\n    spec_trans: 0.0");
        Assert.True(bsdf.SpecTrans.IsConstant);
        Assert.Equal(0f, bsdf.SpecTrans.ConstantValue, 5);
    }

    [Fact]
    public void SubsurfaceRadius_ExplicitPartialSpecTrans_PassesThrough()
    {
        // Translucent onyx: explicit fractional transmission preserved.
        var bsdf = LoadMaterial("    subsurface_radius: [0.6, 0.5, 0.4]\n    spec_trans: 0.5");
        Assert.True(bsdf.SpecTrans.IsConstant);
        Assert.Equal(0.5f, bsdf.SpecTrans.ConstantValue, 5);
    }
}
