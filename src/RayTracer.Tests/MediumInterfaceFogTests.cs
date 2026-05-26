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
/// Acceptance test for entity-bound NON-SSS media (Phase 4 "full MediumInterface
/// use cases"). A local homogeneous fog volume is bound to a single sphere via
/// <see cref="MediumBoundHittable"/>; rays that enter the sphere lose throughput
/// to Beer-Lambert / scattering inside it, while rays that miss the sphere
/// (looking at the env directly) keep full transmittance.
///
/// <para>This is the critical invariant Phase 4 demonstrates: the medium is
/// SCOPED to the bound entity, not global. Before MediumInterface the only way
/// to add fog was <c>world.medium</c> (everywhere); now an artist can drop fog
/// inside a CSG-room, smoke inside a teapot, water inside a tank, without
/// affecting the rest of the scene.</para>
/// </summary>
public class MediumInterfaceFogTests
{
    private const int Width  = 32;
    private const int Height = 32;
    private const int Spp    = 32;
    private const int Depth  = 6;

    private static Camera.Camera MakeCamera() => new(
        lookFrom:    new Vector3(0f, 0f, 4f),
        lookAt:      Vector3.Zero,
        vUp:         Vector3.UnitY,
        vFovDeg:     30f,
        aspectRatio: (float)Width / Height,
        aperture:    0f,
        focusDist:   1f);

    /// <summary>
    /// A pure-absorption local fog (<c>σ_a &gt; 0</c>, <c>σ_s = 0</c>) bound to
    /// a transparent sphere darkens the central pixels (rays cross the volume)
    /// without changing the corner pixels (rays see the env directly).
    /// Demonstrates the spatial scoping of the binding.
    /// </summary>
    [Fact]
    public void LocalAbsorption_DimsCenter_LeavesCornersUntouched()
    {
        Core.Sampling.Sampler.SetKind(Core.Sampling.SamplerKind.Prng);

        // Matched-IOR transparent boundary so refraction is invisible and the
        // only contribution to the central pixel difference is the bound medium.
        var glassThrough = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            roughness: new FloatTexture(0f),
            specular:  new FloatTexture(0f),
            specTrans: new FloatTexture(1f),
            ior:       new FloatTexture(1f),
            clearcoat: new FloatTexture(0f));
        var sphere = new Sphere(Vector3.Zero, 0.6f, glassThrough);

        // High σ_a, no scattering → Beer-Lambert only. exp(-σ_a · 1.2 wu chord)
        // ≈ 0.014 ⇒ center pixels darken to near-zero.
        var fog = new HomogeneousMedium(
            sigmaA: new Vector3(3.5f, 3.5f, 3.5f),
            sigmaS: Vector3.Zero,
            phase:  new IsotropicPhase());
        IHittable bound = new MediumBoundHittable(sphere,
            new MediumInterface(fog, exterior: null));

        var renderer = new Renderer(
            world:           new HittableList(new[] { bound }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth);
        var p = renderer.Render(Width, Height);

        // Center: rays cross the entire fog chord — heavily darkened.
        float center = MeanLuminance(p, Width / 2 - 2, Width / 2 + 2,
                                        Height / 2 - 2, Height / 2 + 2);

        // Corner: outside the sphere silhouette at z=4, fov=30 — env only.
        float corner = MeanLuminance(p, 0, 4, 0, 4);

        Assert.True(center < corner * 0.5f,
            $"Local fog should darken the bound interior much more than the env-only corners. " +
            $"center={center:F4}, corner={corner:F4}.");
        Assert.True(corner > 0.85f,
            $"Corner pixels see env directly (radiance=1.0, post-tonemap ≈ 0.97). " +
            $"Got corner={corner:F4}; suspect medium leakage outside the bound entity.");
    }

    /// <summary>
    /// Replacing the bound medium with the same medium declared globally
    /// (<c>world.medium</c>) darkens the WHOLE image — the corners drop with
    /// the center. This is the failure mode the entity binding fixes: a global
    /// medium cannot be localised.
    /// </summary>
    [Fact]
    public void GlobalMedium_DarkensCorners_ConfirmingTheBindingScope()
    {
        Core.Sampling.Sampler.SetKind(Core.Sampling.SamplerKind.Prng);

        var glassThrough = new DisneyBsdf(
            baseColor: new SolidColor(Vector3.One),
            roughness: new FloatTexture(0f),
            specular:  new FloatTexture(0f),
            specTrans: new FloatTexture(1f),
            ior:       new FloatTexture(1f),
            clearcoat: new FloatTexture(0f));
        var sphere = new Sphere(Vector3.Zero, 0.6f, glassThrough);

        var fog = new HomogeneousMedium(
            sigmaA: new Vector3(3.5f, 3.5f, 3.5f),
            sigmaS: Vector3.Zero,
            phase:  new IsotropicPhase());

        var renderer = new Renderer(
            world:           new HittableList(new[] { (IHittable)sphere }),
            camera:          MakeCamera(),
            lights:          new List<ILight>(),
            sky:             new SkySettings(Vector3.One),
            samplesPerPixel: Spp,
            maxDepth:        Depth,
            globalMedium:    fog);
        var p = renderer.Render(Width, Height);

        // With a global medium the corners are darkened too: rays at z=4 cross
        // 4 wu × σ_a 3.5 = exp(-14) ≈ 0 → all-black image. The corner check
        // proves the previous test's corner-bright behaviour was the binding
        // scope at work, not absence of attenuation.
        float corner = MeanLuminance(p, 0, 4, 0, 4);
        Assert.True(corner < 0.05f,
            $"Global medium should darken the env-direction corner pixels too. " +
            $"Got corner={corner:F4} — the global path may be bypassed.");
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
