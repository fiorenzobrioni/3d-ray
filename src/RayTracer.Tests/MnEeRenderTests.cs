using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Rendering;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// End-to-end regression for MNEE caustics: the glass-sphere lens scene must
/// produce a focused bright spot on the floor under the sphere with
/// <c>--caustics on</c> that is absent (soft diffuse shadow only) with caustics
/// off. Renders small and deterministic (PRNG), in the spirit of
/// <c>FireflyRegressionTests</c>.
/// </summary>
[Collection("SceneLoader")]
public class MnEeRenderTests
{
    private const int Width  = 200;
    private const int Height = 150;
    private const int Spp    = 48;
    private const int Depth  = 6;

    private const string SceneYaml = @"
camera:
  position: [0, 4.5, 6]
  look_at: [0, 0, 0]
  fov: 40
world:
  sky:
    type: ""flat""
    color: [0.0, 0.0, 0.0]
  ground:
    type: ""infinite_plane""
    material: ""floor""
    y: 0
    caustic_receiver: true
entities:
  - name: ""glass_ball""
    type: ""sphere""
    center: [0, 1.4, 0]
    radius: 1.0
    material: ""glass""
    caustic_caster: true
lights:
  - type: area
    corner: [-1.2, 6.0, -1.2]
    u: [2.4, 0.0, 0.0]
    v: [0.0, 0.0, 2.4]
    color: [1.0, 1.0, 1.0]
    intensity: 12.0
    shadow_samples: 4
materials:
  - id: ""floor""
    type: ""lambertian""
    color: [0.7, 0.7, 0.7]
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
";

    private static float CausticSpotPeak(bool enableCaustics)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, SceneYaml);
        try
        {
            Sampler.SetKind(SamplerKind.Prng);
            var (world, camera, lights, sky, globalMedium) =
                SceneLoader.Load(path, Width, Height, enableCaustics: enableCaustics);
            var casters = SceneLoader.LastCausticCasters;

            var renderer = new Renderer(
                world, camera, lights, sky, Spp, Depth, globalMedium,
                enableCaustics: enableCaustics, causticCasters: casters);

            var px = renderer.Render(Width, Height);

            // Floor region directly under the sphere where the lens focuses
            // (mid-centre of the frame — the sphere base projects there).
            float peak = 0f;
            for (int y = (int)(Height * 0.42f); y < (int)(Height * 0.62f); y++)
            for (int x = (int)(Width * 0.40f); x < (int)(Width * 0.60f); x++)
                peak = System.MathF.Max(peak, MathUtils.Luminance(px[y, x]));
            return peak;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GlassSphere_FocusedCaustic_EmergesWithCausticsOn()
    {
        float off = CausticSpotPeak(enableCaustics: false);
        float on  = CausticSpotPeak(enableCaustics: true);

        // Caustics on must concentrate a markedly brighter spot under the lens.
        Assert.True(on > off + 0.15f,
            $"Expected a focused caustic under the glass sphere: peak on={on:F3} should exceed off={off:F3} by >0.15.");
    }
}
