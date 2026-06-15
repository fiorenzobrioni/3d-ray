using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Guard-rail tests for shading-normal energy conservation under bump/normal
/// mapping.
///
/// A perturbed shading normal evaluated against a clamped cosine lets a flat
/// surface fabricate radiance at grazing incidence — the bump's up-tilts add
/// energy while the down-tilts that should occlude get clamped to zero, so the
/// surface washes toward white where it should stay dark. The renderer cures
/// this with a Conty-Kulla shadow-terminator factor applied on both the light
/// and view directions, and the bump generator bounds the perturbation tilt.
///
/// These tests pin down both mechanisms: a grazing render of a strongly bumped
/// flat plane must not exceed the un-bumped baseline (the factor only removes
/// fabricated energy, it never adds), the terminator factor obeys its limits,
/// and the bump tilt stays within the clamp.
/// </summary>
public class BumpTerminatorRegressionTests
{
    private const int Width  = 48;
    private const int Height = 48;
    private const int Spp    = 16;

    /// <summary>
    /// A flat plane viewed almost edge-on under a bright overhead light. With a
    /// strong procedural bump the mean radiance must stay at or below the
    /// un-bumped baseline — the shadow-terminator factor culls the energy a
    /// perturbed normal would otherwise fabricate at grazing incidence.
    /// </summary>
    [Fact]
    public void GrazingBump_DoesNotFabricateEnergy()
    {
        float bumped   = RenderGrazingPlaneMeanLuminance(withBump: true);
        float baseline = RenderGrazingPlaneMeanLuminance(withBump: false);

        // The plane must still render (not collapse to black).
        Assert.True(baseline > 0.01f,
            $"Baseline plane rendered nearly black (mean {baseline:F4}); the setup is degenerate.");

        // The bump may only redistribute / remove energy at grazing, never add
        // it. A small numerical margin absorbs sampler noise. The pre-fix code
        // would blow the bumped mean to several times the baseline here.
        Assert.True(bumped <= baseline * 1.25f,
            $"Bumped grazing mean luminance {bumped:F4} exceeds baseline {baseline:F4} × 1.25. " +
            $"A perturbed shading normal is fabricating energy at grazing incidence — " +
            $"the shadow-terminator softening may have regressed.");
    }

    private static float RenderGrazingPlaneMeanLuminance(bool withBump)
    {
        var disney = new DisneyBsdf(
            baseColor: new SolidColor(new Vector3(0.06f, 0.06f, 0.06f)),
            roughness: new FloatTexture(0.8f),
            specular:  new FloatTexture(0.5f),
            specTrans: new FloatTexture(0f),
            ior:       new FloatTexture(1.5f),
            clearcoat: new FloatTexture(0f));

        if (withBump)
        {
            // High-frequency Perlin field → steep, rapidly varying micro-relief,
            // the case that fabricated energy at grazing before the fix.
            disney.BumpMap = new BumpMapTexture(new NoiseTexture(30f), strength: 6f);
        }

        var plane = new InfinitePlane(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            disney);
        var world = new HittableList(new[] { (IHittable)plane });

        // Bright overhead light, well above the geometric horizon.
        var light = new PointLight(new Vector3(0f, 2.5f, -8f), Vector3.One, 40f);
        var lights = new System.Collections.Generic.List<ILight> { light };

        // Camera near the plane height, looking down its length → grazing view.
        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 0.35f, 6f),
            lookAt:      new Vector3(0f, 0.05f, -40f),
            vUp:         Vector3.UnitY,
            vFovDeg:     45f,
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
            maxDepth:        4,
            verbose:         false);

        var pixels = renderer.Render(Width, Height);

        // Average over the lower half of the frame, where the grazing plane is.
        float sum = 0f; int n = 0;
        for (int y = Height / 2; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                sum += MathUtils.Luminance(pixels[y, x]);
                n++;
            }
        return sum / n;
    }

    /// <summary>
    /// The shadow-terminator factor is identity when the shading normal is
    /// unperturbed — so shading is untouched wherever no map is present.
    /// </summary>
    [Fact]
    public void ShadowTerminator_Unperturbed_IsIdentity()
    {
        var n = Vector3.Normalize(new Vector3(0.1f, 1f, 0.2f));
        var w = Vector3.Normalize(new Vector3(-0.3f, 0.8f, 0.4f));
        Assert.Equal(1f, MathUtils.ShadowTerminatorTerm(n, n, w), 6);
    }

    /// <summary>
    /// A direction below the geometric horizon contributes nothing, even if the
    /// perturbed normal still faces it.
    /// </summary>
    [Fact]
    public void ShadowTerminator_BelowGeometricHorizon_IsZero()
    {
        var ng = Vector3.UnitY;                                  // flat surface
        var ns = Vector3.Normalize(new Vector3(0.9f, 0.4f, 0f)); // tilted hard sideways
        var w  = Vector3.Normalize(new Vector3(0.9f, -0.1f, 0f));// just under the horizon
        Assert.Equal(0f, MathUtils.ShadowTerminatorTerm(ng, ns, w));
    }

    /// <summary>
    /// When the perturbed normal faces a grazing direction more than the
    /// geometry allows, the factor is in (0, 1): the fabricated portion is
    /// removed but legitimate light is preserved.
    /// </summary>
    [Fact]
    public void ShadowTerminator_GrazingPerturbation_PartiallyCulls()
    {
        var ng = Vector3.UnitY;
        var ns = Vector3.Normalize(new Vector3(0.5f, 0.86f, 0f)); // tilted toward +X
        var w  = Vector3.Normalize(new Vector3(0.95f, 0.1f, 0f)); // near-grazing on +X
        float g = MathUtils.ShadowTerminatorTerm(ng, ns, w);
        Assert.True(g > 0f && g < 1f, $"Expected partial culling in (0,1), got {g}.");
    }

    /// <summary>
    /// The bump generator bounds the perturbation tilt: an arbitrarily steep
    /// height gradient cannot drive the tangent-space normal past the clamp.
    /// </summary>
    [Fact]
    public void BumpMap_TiltIsBounded_UnderExtremeGradient()
    {
        // Ramp height field with a huge slope along +X (luminance = 1000·x).
        var bump = new BumpMapTexture(new RampTexture(1000f), strength: 1f);

        Vector3 nTs = bump.SampleTangentNormal(
            u: 0f, v: 0f, p: Vector3.Zero,
            tangent: Vector3.UnitX, bitangent: Vector3.UnitY, seed: 0);

        Assert.True(nTs.Z > 0f, "Tangent-space normal must stay in the upper hemisphere.");
        float slope = MathF.Sqrt(nTs.X * nTs.X + nTs.Y * nTs.Y) / nTs.Z; // = tan(tilt)
        // MaxSlope inside BumpMapTexture is 6 (~80.5°); allow a tiny epsilon.
        Assert.True(slope <= 6f + 1e-3f,
            $"Bump tilt tan {slope:F3} exceeds the clamp (6). The tilt bound may have regressed.");
    }

    /// <summary>Grayscale ramp: luminance = <c>k · p.X</c>, an unbounded gradient.</summary>
    private sealed class RampTexture : ITexture
    {
        private readonly float _k;
        public RampTexture(float k) => _k = k;
        public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
            => new Vector3(_k * p.X);
    }
}
