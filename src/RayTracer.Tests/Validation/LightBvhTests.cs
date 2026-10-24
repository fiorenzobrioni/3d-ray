using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>Light BVH (Conty Estevez and Kulla 2018): a proper distribution, sampled as reported.</summary>
[Collection("SamplerExclusive")]
public class LightBvhTests
{
    private readonly ITestOutputHelper _out;
    public LightBvhTests(ITestOutputHelper o)
    {
        _out = o;
        // The noise comparison assumes the stratified sampler; other classes
        // leave the process-global kind at Prng.
        Core.Sampling.Sampler.SetKind(Core.Sampling.SamplerKind.Sobol);
    }

    private static List<ILight> ManyLights(int n, int seed)
    {
        var rng = new Random(seed);
        var lights = new List<ILight>();
        for (int i = 0; i < n; i++)
        {
            var c = new Vector3(rng.NextSingle() * 20 - 10, 2 + rng.NextSingle() * 3, rng.NextSingle() * 20 - 10);
            lights.Add((i % 3) switch
            {
                0 => new PointLight(c, Vector3.One, 1f + 10f * rng.NextSingle()),
                1 => new SphereLight(c, 0.1f + 0.3f * rng.NextSingle(), Vector3.One, 5f),
                _ => new AreaLight(c, new Vector3(0.5f, 0, 0), new Vector3(0, 0, 0.5f), Vector3.One, 3f),
            });
        }
        lights.Add(new DirectionalLight(new Vector3(0.2f, -1, 0.1f), Vector3.One, 1f));
        return lights;
    }

    [Fact]
    public void Pmf_SumsToOne_AndMatchesTheSampler()
    {
        var lights = ManyLights(60, 3);
        var bvh = new LightBvh(lights, new AABB(new Vector3(-12, 0, -12), new Vector3(12, 6, 12)));
        var rng = new Random(5);
        for (int t = 0; t < 20; t++)
        {
            var p = new Vector3(rng.NextSingle() * 20 - 10, rng.NextSingle() * 4, rng.NextSingle() * 20 - 10);
            double sum = 0;
            for (int i = 0; i < lights.Count; i++) sum += bvh.Pmf(p, i);
            // A node reached through its children's looser union bounds may
            // hold no light that can reach p: that branch returns none. Its
            // lights have pmf 0, and none of them can light p.
            Assert.InRange(sum, 0.9, 1.001);

            // The sampler's own probability is the Pmf of what it returns, and
            // its frequencies follow it.
            var counts = new int[lights.Count];
            int none = 0;
            const int n = 20000;
            for (int k = 0; k < n; k++)
            {
                var (idx, pmf) = bvh.Sample(p, rng.NextSingle());
                if (idx < 0) { none++; continue; }
                Assert.Equal(bvh.Pmf(p, idx), pmf, 4);
                counts[idx]++;
            }
            for (int i = 0; i < lights.Count; i++)
            {
                double e = n * bvh.Pmf(p, i);
                Assert.InRange(counts[i], e - 5 * Math.Sqrt(e + 1) - 2, e + 5 * Math.Sqrt(e + 1) + 2);
            }
            double eNone = n * (1 - sum);
            Assert.InRange(none, eNone - 5 * Math.Sqrt(eNone + 1) - 2, eNone + 5 * Math.Sqrt(eNone + 1) + 2);
        }
    }

    [Fact]
    public void ManyLights_BvhIsUnbiased_AndLessNoisyThanUniform()
    {
        // 90 lights over a floor: the BVH and the uniform pick both converge to
        // the sum over all lights, the BVH with far less noise.
        var lights = ManyLights(90, 7);
        var floor = new Quad(new Vector3(-50, 0, 50), new Vector3(100, 0, 0), new Vector3(0, 0, -100), new Lambertian(new Vector3(0.8f)));
        var objects = new List<IHittable> { floor };
        var world = new HittableList(objects);
        var cam = RenderHarness.LookAt(new Vector3(0, 3, 1.2f), new Vector3(1.5f, 0, -0.5f), 0.01f, 4, 4);
        var sky = new SkySettings(Vector3.Zero);
        Vector3 all = RenderHarness.Mean(RenderHarness.RenderLinear(world, lights, sky, cam, 4, 4, 64, 2));
        (Vector3 Mean, double RelStd) Run(LightSamplingStrategy s)
        {
            var img = RenderHarness.RenderLinear(world, lights, sky, cam, 4, 4, 256, 2, lightSampling: s);
            Vector3 m = RenderHarness.Mean(img);
            double v = 0;
            foreach (var px in img) v += Math.Pow(MathUtils.Luminance(px) - MathUtils.Luminance(m), 2);
            return (m, Math.Sqrt(v / (img.Length - 1)) / MathUtils.Luminance(m));
        }
        var bvh = Run(LightSamplingStrategy.Bvh);
        var uni = Run(LightSamplingStrategy.Uniform);
        _out.WriteLine($"all {MathUtils.Luminance(all):F4}; bvh {MathUtils.Luminance(bvh.Mean):F4} (pixel rel. std {bvh.RelStd:F3}); uniform {MathUtils.Luminance(uni.Mean):F4} ({uni.RelStd:F3})");
        Assert.InRange(MathUtils.Luminance(bvh.Mean) / MathUtils.Luminance(all), 0.97f, 1.03f);
        Assert.True(bvh.RelStd < 0.5 * uni.RelStd, $"bvh {bvh.RelStd:F3} vs uniform {uni.RelStd:F3}");
    }
}
