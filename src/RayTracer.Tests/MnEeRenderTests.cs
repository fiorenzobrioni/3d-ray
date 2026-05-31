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

    // The same glass-lens scene with a swappable `lights:` block, so the focused
    // caustic can be exercised under every emitter type that drives MNEE.
    private static string Scene(string lightsBlock) => @"
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
" + lightsBlock + @"
materials:
  - id: ""floor""
    type: ""lambertian""
    color: [0.7, 0.7, 0.7]
  - id: ""glass""
    type: ""dielectric""
    refraction_index: 1.5
";

    private const string AreaLightBlock = @"  - type: area
    corner: [-1.2, 6.0, -1.2]
    u: [2.4, 0.0, 0.0]
    v: [0.0, 0.0, 2.4]
    color: [1.0, 1.0, 1.0]
    intensity: 12.0
    shadow_samples: 4";

    private const string SphereLightBlock = @"  - type: sphere
    position: [0, 6.0, 0]
    radius: 0.6
    color: [1.0, 1.0, 1.0]
    intensity: 60.0
    shadow_samples: 4";

    private const string PointLightBlock = @"  - type: point
    position: [0, 6.0, 0]
    color: [1.0, 1.0, 1.0]
    intensity: 120.0
    soft_radius: 0.2";

    // Spot aimed straight down at the lens: the emission direction toward the
    // sphere is along the beam axis, so the cone falloff must NOT cancel it — a
    // caustic appears only if DirectionalEmissionScale has the correct sign.
    private const string SpotLightBlock = @"  - type: spot
    position: [0, 6.0, 0]
    direction: [0, -1, 0]
    color: [1.0, 1.0, 1.0]
    intensity: 120.0
    inner_angle: 25.0
    outer_angle: 45.0
    soft_radius: 0.2";

    private static float CausticSpotPeak(bool enableCaustics, string lightsBlock, int mneeSamples = 1)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, Scene(lightsBlock));
        try
        {
            Sampler.SetKind(SamplerKind.Prng);
            var (world, camera, lights, sky, globalMedium) =
                SceneLoader.Load(path, Width, Height, enableCaustics: enableCaustics);
            var casters = SceneLoader.LastCausticCasters;

            var renderer = new Renderer(
                world, camera, lights, sky, Spp, Depth, globalMedium,
                enableCaustics: enableCaustics, causticCasters: casters,
                mneeSamples: mneeSamples);

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
        float off = CausticSpotPeak(enableCaustics: false, AreaLightBlock);
        float on  = CausticSpotPeak(enableCaustics: true, AreaLightBlock);

        // Caustics on must concentrate a markedly brighter spot under the lens.
        Assert.True(on > off + 0.15f,
            $"Expected a focused caustic under the glass sphere: peak on={on:F3} should exceed off={off:F3} by >0.15.");
    }

    [Fact]
    public void GlassSphere_FocusedCaustic_EmergesWithSphereLight()
    {
        float off = CausticSpotPeak(enableCaustics: false, SphereLightBlock);
        float on  = CausticSpotPeak(enableCaustics: true, SphereLightBlock);

        Assert.True(on > off + 0.15f,
            $"Expected a focused caustic from the sphere light: on={on:F3} should exceed off={off:F3} by >0.15.");
    }

    [Fact]
    public void GlassSphere_FocusedCaustic_EmergesWithPointLight()
    {
        // The finite virtual bulb is noisier than an area light, so average a few
        // emitter samples to keep the peak above the off baseline reliably.
        float off = CausticSpotPeak(enableCaustics: false, PointLightBlock, mneeSamples: 4);
        float on  = CausticSpotPeak(enableCaustics: true, PointLightBlock, mneeSamples: 4);

        Assert.True(on > off + 0.15f,
            $"Expected a focused caustic from the point light bulb: on={on:F3} should exceed off={off:F3} by >0.15.");
    }

    [Fact]
    public void GlassSphere_FocusedCaustic_EmergesWithSpotLight()
    {
        // End-to-end guard on the spot cone-falloff sign: an inverted scale would
        // zero the caustic (the receiver-facing gate still passes), so on≈off.
        float off = CausticSpotPeak(enableCaustics: false, SpotLightBlock, mneeSamples: 4);
        float on  = CausticSpotPeak(enableCaustics: true, SpotLightBlock, mneeSamples: 4);

        Assert.True(on > off + 0.15f,
            $"Expected a focused caustic from the spot light bulb: on={on:F3} should exceed off={off:F3} by >0.15.");
    }
}
