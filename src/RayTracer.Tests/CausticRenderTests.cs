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
    public void Photons_AreTintedByColouredGlass()
    {
        // A red absorbing glass sphere (Disney transmission_color red +
        // transmission_depth → Beer-Lambert σ_a). Photons that refract through it
        // must come out reddish, so the focused caustic (= the coloured refractive
        // shadow) is red — the physical realism the Beer-Lambert photon transport
        // exists to produce.
        Sampler.SetKind(SamplerKind.Prng);

        var floor = new InfinitePlane(new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
                                      new Lambertian(new Vector3(0.7f, 0.7f, 0.7f)));
        var redGlass = new DisneyBsdf(
            baseColor:         new SolidColor(new Vector3(1f, 1f, 1f)),
            roughness:         new FloatTexture(0f),
            specTrans:         new FloatTexture(1f),
            ior:               new FloatTexture(1.5f),
            transmissionColor: new SolidColor(new Vector3(0.9f, 0.08f, 0.08f)),
            transmissionDepth: new FloatTexture(1.0f));
        var glass = new Sphere(new Vector3(0f, 1.2f, 0f), 1.0f, redGlass);
        var world = new HittableList(new[] { (IHittable)floor, glass });

        var light = new AreaLight(
            corner: new Vector3(-1.0f, 6.0f, -1.0f), u: new Vector3(2.0f, 0f, 0f),
            v: new Vector3(0f, 0f, 2.0f), color: new Vector3(1f, 1f, 1f),
            intensity: 12f, shadowSamples: 4);
        var bounds = new AABB(new Vector3(-3f, -0.5f, -3f), new Vector3(3f, 3f, 3f));

        PhotonMap? map = CausticPhotonTracer.Build(world, new List<ILight> { light }, bounds,
                                                   photonBudget: 1_000_000, cellSize: 0.1f);
        Assert.NotNull(map);
        Assert.True(map!.Count > 500, $"Expected caustic photons, got {map.Count}.");

        var photons = map.Photons;
        Vector3 mean = Vector3.Zero;
        for (int i = 0; i < photons.Length; i++) mean += photons[i].Power;
        mean /= photons.Length;

        // Beer-Lambert through the red glass kills green/blue far more than red.
        Assert.True(mean.X > mean.Y * 1.5f && mean.X > mean.Z * 1.5f,
            $"Caustic photons should be red after a red glass; mean power = {mean}.");
    }

    [Fact]
    public void Photons_CarryNearUniformPower_NoRussianRouletteBoost()
    {
        // Regression for the photon-RR "mega-photon" bug: survival must be
        // measured RELATIVE to the emitted power. The absolute-power test it
        // replaced saw max-channel ≈ Φ/N ≈ 1e-4, killed virtually every photon
        // past the RR start bounce, and boosted the rare survivor
        // (power /= survive) to max-channel ≈ 1 — a several-thousand-fold
        // outlier the camera gather rendered as a bright disc or sparkle.
        //
        // Two concentric clear-glass spheres force ≥ 4 specular bounces
        // (enter/exit × 2) on every photon reaching the floor beneath the
        // centre, so those photons all face at least one RR decision.
        Sampler.SetKind(SamplerKind.Prng);

        var floor = new InfinitePlane(new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
                                      new Lambertian(new Vector3(0.7f, 0.7f, 0.7f)));
        var outer = new Sphere(new Vector3(0f, 1.4f, 0f), 1.0f, new Dielectric(1.5f));
        var inner = new Sphere(new Vector3(0f, 1.4f, 0f), 0.5f, new Dielectric(1.5f));
        var world = new HittableList(new[] { (IHittable)floor, outer, inner });

        var light = new AreaLight(
            corner: new Vector3(-1.2f, 6.0f, -1.2f), u: new Vector3(2.4f, 0f, 0f),
            v: new Vector3(0f, 0f, 2.4f), color: new Vector3(1f, 1f, 1f),
            intensity: 12f, shadowSamples: 4);
        var bounds = new AABB(new Vector3(-3f, -0.5f, -3f), new Vector3(3f, 3f, 3f));

        const int Budget = 500_000;
        PhotonMap? map = CausticPhotonTracer.Build(world, new List<ILight> { light }, bounds,
                                                   photonBudget: Budget, cellSize: 0.1f);
        Assert.NotNull(map);

        // The multi-bounce photons must SURVIVE the roulette: the focused disc
        // beneath the nested lens can only be reached through both spheres
        // (≥ 4 delta bounces). The absolute-power RR starved it to ~zero.
        var central = new List<int>();
        map!.QueryRadius(new Vector3(0f, 0f, 0f), 0.8f, central);
        Assert.True(central.Count > 200,
            $"Expected the ≥4-bounce focused photons to survive RR; found {central.Count} under the lens.");

        // And no survivor may carry more than its emitted power Φ/N: clear
        // glass attenuates by at most 1 per bounce and the relative-survival
        // boost is capped at the emitted power.
        float perPhotonMax = 12f * (2.4f * 2.4f) * MathF.PI / Budget;
        var photons = map.Photons;
        float maxChan = 0f;
        for (int i = 0; i < photons.Length; i++)
            maxChan = MathF.Max(maxChan, MathF.Max(photons[i].Power.X,
                                MathF.Max(photons[i].Power.Y, photons[i].Power.Z)));
        Assert.True(maxChan <= perPhotonMax * 1.05f,
            $"Photon power must stay bounded by the emitted {perPhotonMax:E3}; found {maxChan:E3}.");
    }

    [Fact]
    public void CausticOccupancyWeight_FadesSparseGathers_LeavesFocusedFull()
    {
        // Regression for the "ring of discs" artefact: weak reflective caustics
        // off glossy near-specular surfaces (e.g. clearcoat billiard balls)
        // deposit only a handful of photons, so a gather finds far fewer than k.
        // The old code stamped a flat full-radius disc around each; the occupancy
        // confidence now fades those sparse gathers out while leaving dense,
        // focused caustics (count == k) at full strength.
        const int K = 40;

        // Dense focused caustic: full gather → untouched.
        Assert.Equal(1f, Renderer.CausticOccupancyWeight(K, K));
        Assert.Equal(1f, Renderer.CausticOccupancyWeight(K + 7, K));
        Assert.True(Renderer.CausticOccupancyWeight(K - 1, K) > 0.9f,
            "A nearly-full gather must stay bright.");

        // Sparse stray photons: faded to a negligible fraction → no visible disc.
        Assert.Equal(0f, Renderer.CausticOccupancyWeight(0, K));
        Assert.True(Renderer.CausticOccupancyWeight(1, K) < 0.01f,
            "A lone stray photon must not paint a disc.");
        Assert.True(Renderer.CausticOccupancyWeight(4, K) < 0.05f);

        // Monotonic non-decreasing in the photon count.
        float prev = -1f;
        for (int c = 0; c <= K; c++)
        {
            float w = Renderer.CausticOccupancyWeight(c, K);
            Assert.True(w >= prev, $"Occupancy weight must be monotonic; dropped at count={c}.");
            prev = w;
        }
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
