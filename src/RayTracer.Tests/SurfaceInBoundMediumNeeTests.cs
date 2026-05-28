using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using RayTracer.Volumetrics;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Acceptance test for NEE (next-event estimation) from a surface that sits
/// <i>inside</i> an entity-bound participating medium.
///
/// <para>A diffuse sphere is enclosed by a larger matched-IOR transparent
/// sphere bound to an absorbing medium. When a camera ray refracts into the
/// outer sphere the medium is pushed onto the <see cref="MediumStack"/>; the
/// shadow ray cast by the inner sphere's direct lighting then travels through
/// that medium before exiting. Previously the surface NEE path attenuated
/// shadow rays only by the <i>global</i> medium and ignored the stack, so an
/// object submerged in bound fog/water was lit as if the medium weren't there.</para>
///
/// <para>The invariant: the inner sphere lit through an <b>absorbing</b> bound
/// medium must be markedly dimmer than the same scene with a <b>vacuum</b> bound
/// medium (σ = 0), confirming the shadow-ray transmittance now sees the
/// stack medium.</para>
/// </summary>
public class SurfaceInBoundMediumNeeTests
{
    private const int Width  = 32;
    private const int Height = 32;
    private const int Spp    = 48;
    private const int Depth  = 3;

    private static Camera.Camera MakeCamera() => new(
        lookFrom:    new Vector3(0f, 0f, 6f),
        lookAt:      Vector3.Zero,
        vUp:         Vector3.UnitY,
        vFovDeg:     30f,
        aspectRatio: (float)Width / Height,
        aperture:    0f,
        focusDist:   1f);

    private static DisneyBsdf MatchedGlass() => new(
        baseColor: new SolidColor(Vector3.One),
        roughness: new FloatTexture(0f),
        specular:  new FloatTexture(0f),
        specTrans: new FloatTexture(1f),
        ior:       new FloatTexture(1f),
        clearcoat: new FloatTexture(0f));

    private static float RenderInnerLuminance(HomogeneousMedium boundMedium)
    {
        Core.Sampling.Sampler.SetKind(Core.Sampling.SamplerKind.Prng);

        // Inner lit object: white diffuse sphere at the origin.
        var inner = new Sphere(Vector3.Zero, 0.5f, new Lambertian(Vector3.One));

        // Outer matched-IOR transparent shell carrying the bound medium.
        var outerGeom = new Sphere(Vector3.Zero, 2f, MatchedGlass());
        IHittable outer = new MediumBoundHittable(outerGeom,
            new MediumInterface(boundMedium, exterior: null));

        // Point light between the camera and the spheres, on the +z axis, so it
        // lights the camera-facing cap of the inner sphere head-on and its
        // shadow ray runs straight back out through the bound medium.
        var light = new PointLight(new Vector3(0f, 0f, 4f), Vector3.One, intensity: 30f);

        var renderer = new Renderer(
            world:           new HittableList(new[] { (IHittable)inner, outer }),
            camera:          MakeCamera(),
            lights:          new List<ILight> { light },
            sky:             new SkySettings(Vector3.Zero),   // black env — only NEE lights the inner sphere
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var p = renderer.Render(Width, Height);

        // Central block covers the inner sphere's lit cap.
        return MeanLuminance(p, Width / 2 - 4, Width / 2 + 4, Height / 2 - 4, Height / 2 + 4);
    }

    [Fact]
    public void InnerSurface_LitThroughAbsorbingBoundMedium_IsDimmerThanVacuum()
    {
        var absorbing = new HomogeneousMedium(
            sigmaA: new Vector3(1.0f, 1.0f, 1.0f),
            sigmaS: Vector3.Zero,
            phase:  new IsotropicPhase());
        var vacuum = new HomogeneousMedium(Vector3.Zero, Vector3.Zero, new IsotropicPhase());

        float litVacuum    = RenderInnerLuminance(vacuum);
        float litAbsorbing = RenderInnerLuminance(absorbing);

        Assert.True(litVacuum > 0.02f,
            $"Sanity: the inner sphere should be lit with a vacuum medium. Got {litVacuum:F4}.");
        // Absorbing fog attenuates both the camera path and — critically — the
        // NEE shadow ray inside the bound medium, so the lit cap drops sharply.
        Assert.True(litAbsorbing < litVacuum * 0.5f,
            $"Inner surface inside absorbing bound medium should be much dimmer. " +
            $"vacuum={litVacuum:F4}, absorbing={litAbsorbing:F4}.");
    }

    private static float MeanLuminance(Vector3[,] p, int x0, int x1, int y0, int y1)
    {
        double a = 0; int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                a += MathUtils.Luminance(p[y, x]);
                n++;
            }
        return (float)(a / n);
    }
}
