using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// End-to-end regression for photon-mapped caustics. A solid glass sphere over a
/// diffuse floor lit by an overhead area light focuses light into a bright spot
/// on the floor directly beneath it.
///
/// <para><see cref="Photons_FocusOnTheFloorBeneathTheLens"/> drives the photon
/// pre-pass directly and asserts caustic photons are deposited and concentrated
/// under the lens — a deterministic check free of render noise. <see
/// cref="CausticsRender_StaysFiniteAndEnergyBounded"/> renders the scene and
/// guards against NaNs, firefly blow-ups, and gross energy violations (photon
/// caustics redistribute light, they do not invent it).</para>
/// </summary>
public class CausticRenderTests
{
    private static (IHittable world, List<ILight> lights, AABB bounds) BuildScene()
    {
        var floor = new InfinitePlane(new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
                                      new Lambertian(new Vector3(0.7f, 0.7f, 0.7f)));
        var glass = new Sphere(new Vector3(0f, 1.4f, 0f), 1.0f, new Dielectric(1.5f));
        var world = new HittableList(new[] { (IHittable)floor, glass });

        var light = new AreaLight(
            corner: new Vector3(-1.2f, 6.0f, -1.2f),
            u:      new Vector3(2.4f, 0f, 0f),
            v:      new Vector3(0f, 0f, 2.4f),
            color:  new Vector3(1f, 1f, 1f),
            intensity: 12f,
            shadowSamples: 4);

        var bounds = new AABB(new Vector3(-3f, -0.5f, -3f), new Vector3(3f, 3f, 3f));
        return (world, new List<ILight> { light }, bounds);
    }

    [Fact]
    public void Photons_FocusOnTheFloorBeneathTheLens()
    {
        Sampler.SetKind(SamplerKind.Prng);
        var (world, lights, bounds) = BuildScene();

        PhotonMap? map = CausticPhotonTracer.Build(world, lights, bounds,
                                                   photonBudget: 1_000_000, cellSize: 0.1f);

        Assert.NotNull(map);
        Assert.True(map!.Count > 1000, $"Expected a populated caustic map, got {map.Count} photons.");

        // Every caustic photon must have crossed the glass and landed on the
        // floor (y ≈ 0), not on the sphere — the deposit rule is L S+ D.
        var photons = map.Photons;
        int onFloor = 0;
        for (int i = 0; i < photons.Length; i++)
            if (MathF.Abs(photons[i].Position.Y) < 0.05f) onFloor++;
        Assert.True(onFloor > photons.Length * 0.9f,
            $"Expected caustic photons on the floor; only {onFloor}/{photons.Length} were.");

        // They must concentrate under the lens: a tight disc beneath the sphere
        // holds a large share of them (the focused spot), far more than an equal
        // off-axis disc well to the side.
        var center = new List<int>();
        var offAxis = new List<int>();
        map.QueryRadius(new Vector3(0f, 0f, 0f), 0.6f, center);
        map.QueryRadius(new Vector3(2.4f, 0f, 0f), 0.6f, offAxis);
        Assert.True(center.Count > offAxis.Count * 3,
            $"Caustic should focus under the lens: {center.Count} central vs {offAxis.Count} off-axis.");
    }

    [Fact]
    public void CausticsRender_StaysFiniteAndEnergyBounded()
    {
        const int W = 160, H = 160, Spp = 24, Depth = 8;
        var cam = new RayTracer.Camera.Camera(
            lookFrom: new Vector3(0f, 4.5f, 6f), lookAt: new Vector3(0f, 0f, 0f),
            vUp: Vector3.UnitY, vFovDeg: 40f, aspectRatio: (float)W / H,
            aperture: 0f, focusDist: 1f);

        Vector3[,] Render(bool caustics, int photons)
        {
            Sampler.SetKind(SamplerKind.Prng);
            var (world, lights, _) = BuildScene();
            var r = new Renderer(world, cam, lights, new SkySettings(Vector3.Zero), Spp, Depth,
                                 globalMedium: null, maxSampleRadiance: Renderer.DefaultMaxSampleRadiance,
                                 verbose: false, enableCaustics: caustics, causticPhotons: photons);
            return r.Render(W, H);
        }

        var off = Render(false, 0);
        var on  = Render(true, 1_500_000);

        float sumOff = 0f, sumOn = 0f;
        int spikes = 0;
        // Bright-tail of the floor (lower half of the frame): the focused caustic
        // concentrates light into a small set of bright pixels there, so its
        // brightest pixels must clearly outshine the path-traced baseline. This
        // guards against a gather that silently goes dim/invisible (e.g. a kernel
        // radius too wide), which leaves the energy ratio looking fine.
        var brightOn  = new List<float>();
        var brightOff = new List<float>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Vector3 p = on[y, x];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z),
                    $"Non-finite pixel at ({x},{y}) with caustics on.");
                float lumOn = MathUtils.Luminance(p);
                if (lumOn > 0.99f) spikes++;
                sumOn  += lumOn;
                sumOff += MathUtils.Luminance(off[y, x]);
                if (y >= H / 2)
                {
                    brightOn.Add(lumOn);
                    brightOff.Add(MathUtils.Luminance(off[y, x]));
                }
            }

        // Caustics redistribute light; total brightness must stay in a sane band
        // around the path-traced baseline (no energy explosion or collapse).
        float ratio = sumOn / MathF.Max(sumOff, 1e-3f);
        Assert.InRange(ratio, 0.85f, 1.25f);
        Assert.True(spikes <= 250, $"Caustics-on produced {spikes} near-white firefly pixels (threshold 250).");

        // Mean of the brightest 2% of floor pixels — robust to PRNG and location.
        brightOn.Sort();  brightOn.Reverse();
        brightOff.Sort(); brightOff.Reverse();
        int topN = Math.Max(1, brightOn.Count / 50);
        float tailOn  = brightOn.Take(topN).Average();
        float tailOff = brightOff.Take(topN).Average();
        Assert.True(tailOn > tailOff * 1.05f,
            $"Caustics-on floor bright-tail {tailOn:F4} should exceed off {tailOff:F4} — the caustic is not visible.");
    }
}
