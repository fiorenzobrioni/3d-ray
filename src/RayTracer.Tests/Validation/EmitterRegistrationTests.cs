using System.Numerics;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Every samplable surface whose material may emit is a light for next-event
/// estimation, and the light is the surface's own material (its samples
/// evaluate the same Emit a BSDF ray collects there): an OpenPBR material with
/// emission, a mix with an emitting side (not a synthetic stand-in), a lathe
/// and an extrusion. A material that cannot emit makes no light.
/// </summary>
[Collection("SceneLoader")]
public class EmitterRegistrationTests
{
    [Fact]
    public void EmittingMaterials_BecomeLights_WithTheirOwnMaterial()
    {
        string path = Path.Combine(Path.GetTempPath(), $"3dray-emitters-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, @"format_version: 2
cameras:
  - { position: [0, 1, 6], look_at: [0, 1, 0] }
materials:
  - { id: glow, type: openpbr, emission_luminance: 4, emission_color: [1, 0.8, 0.6], geometry_thin_walled: true }
  - { id: dark, type: openpbr, base_color: [0.5, 0.5, 0.5] }
  - { id: lamp, type: emissive, color: [1, 1, 1], luminance: 3 }
  - { id: grey, type: lambertian }
  - { id: half, type: mix, material_a: lamp, material_b: grey, blend: 0.25 }
entities:
  - { name: pbr-sphere, type: sphere, center: [-2, 1, 0], radius: 0.5, material: glow }
  - { name: plain, type: sphere, center: [2, 1, 0], radius: 0.5, material: dark }
  - { name: mixed, type: quad, q: [-1, 3, -1], u: [2, 0, 0], v: [0, 0, 2], material: half }
  - name: vase
    type: lathe
    material: glow
    translate: [0, 0, -2]
    profile: [[0.3, 0.0], [0.4, 0.5], [0.2, 1.0]]
  - name: star
    type: extrusion
    material: lamp
    height: 0.2
    translate: [0, 0, 2]
    profile: [[1, 0], [0, 1], [-1, 0], [0, -1]]
lights: []
");
        try
        {
            var lights = SceneLoader.Load(path, 16, 16).Item3.OfType<GeometryLight>().ToList();
            Assert.Equal(4, lights.Count);
            Assert.Contains(lights, l => l.Geometry is Sphere && l.Material is OpenPbr);
            var mix = Assert.Single(lights, l => l.Material is MixMaterial);
            Assert.Equal(0.75f * 3f, mix.Material.Emit(0.5f, 0.5f, Vector3.Zero, 0, true).X, 4);
            Assert.Contains(lights, l => l.Geometry is Transform && l.Material is OpenPbr);   // the lathe
            Assert.Contains(lights, l => l.Geometry is Transform && l.Material is Emissive);  // the extrusion
            Assert.DoesNotContain(lights, l => l.Geometry is Sphere { Center.X: > 0 });
            Assert.All(lights, l => Assert.Same(l.Material, l.SurfaceMaterial));
        }
        finally { File.Delete(path); }
    }
}
