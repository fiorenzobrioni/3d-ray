using System.Numerics;
using RayTracer.Camera;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Regression guard for the removal of the non-physical <c>world.ambient_light</c>
/// term. Before this change, <c>Renderer.ComputeDirectLighting</c> initialised its
/// result with a per-hit additive constant that bypassed the BRDF, the cosine
/// factor and the material albedo. The visible symptom was that pure-black
/// surfaces still emitted radiance, washing out shadows and saturated colours.
///
/// Industry-standard path tracers (Arnold, Cycles, RenderMan) have no such term:
/// indirect/ambient illumination arises from the path-traced GI loop alone.
///
/// This test renders a scene whose only surface is a Lambertian floor with
/// <c>albedo = (0,0,0)</c> under a directional light and a black flat sky.
/// Every pixel that hits the floor MUST be exactly zero — any non-zero value
/// would mean a hidden ambient floor has crept back in.
/// </summary>
public class AmbientLightRemovalTests
{
    private const int Width  = 32;
    private const int Height = 32;
    private const int Spp    = 4;
    private const int Depth  = 4;

    [Fact]
    public void BlackLambertianFloor_RendersBlack_WithBlackSky()
    {
        var blackFloor = new Lambertian(Vector3.Zero);
        var floor = new InfinitePlane(
            point:    Vector3.Zero,
            normal:   Vector3.UnitY,
            material: blackFloor);
        var world = new HittableList(new[] { (IHittable)floor });

        // A directional light strong enough to expose any leak — if any
        // non-BRDF-modulated term is added, the resulting pixels will not be
        // exactly zero.
        var sun = new DirectionalLight(
            direction: new Vector3(-1f, -1f, -1f),
            color:     Vector3.One,
            intensity: 5f);
        var lights = new System.Collections.Generic.List<ILight> { sun };

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1.5f, 5f),
            lookAt:      new Vector3(0f, 0f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     50f,
            aspectRatio: (float)Width / Height,
            aperture:    0f,
            focusDist:   1f);

        Sampler.SetKind(SamplerKind.Prng);

        var renderer = new Renderer(
            world:           world,
            camera:          camera,
            lights:          lights,
            sky:             new SkySettings(Vector3.Zero),
            samplesPerPixel: Spp,
            maxDepth:        Depth,
            verbose:         false);

        var pixels = renderer.Render(Width, Height);

        // Every channel of every pixel must be exactly zero. Pixels that
        // missed the floor see the black sky (also zero), so the whole image
        // collapses to black.
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector3 p = pixels[y, x];
                Assert.Equal(0f, p.X);
                Assert.Equal(0f, p.Y);
                Assert.Equal(0f, p.Z);
            }
        }
    }
}
