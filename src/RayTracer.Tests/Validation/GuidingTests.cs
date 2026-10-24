using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Rendering.Guiding;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Path guiding (PIANO-REVIEW.md Phase 7b): the directional quadtree samples
/// the density it reports, the spatial tree splits where the records are, and
/// a guided render converges to the same image as an unguided one, with less
/// noise where the light arrives from a small bright region.
/// </summary>
[Collection("SamplerExclusive")]
public class GuidingTests
{
    private readonly ITestOutputHelper _out;
    public GuidingTests(ITestOutputHelper o)
    {
        _out = o;
        Sampler.SetKind(SamplerKind.Sobol);
    }

    [Fact]
    public void SquareMap_RoundTrips()
    {
        var rng = new Random(3);
        for (int i = 0; i < 1000; i++)
        {
            var d = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            Vector3 back = DTree.FromSquare(DTree.ToSquare(d));
            Assert.True((back - d).Length() < 1e-4f, $"{d} -> {back}");
        }
    }

    /// <summary>A tree trained on a peaked distribution, refined twice.</summary>
    private static DTree PeakedTree()
    {
        var rng = new Random(7);
        DTree building = DTree.Empty();
        DTree sampling = building;
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < 20000; i++)
            {
                var p = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                // Most energy in a small corner, a little everywhere.
                float w = p.X < 0.1f && p.Y > 0.8f ? 50f : 0.2f;
                building.Record(p, w);
            }
            sampling = building;
            building = sampling.Refined(0.01f);
        }
        return sampling;
    }

    [Fact]
    public void DTree_PdfIntegratesToOne()
    {
        DTree t = PeakedTree();
        Assert.True(t.NodeCount > 5);
        const int N = 512;
        double sum = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                sum += t.PdfSquare(new Vector2((i + 0.5f) / N, (j + 0.5f) / N));
        Assert.InRange(sum / (N * N), 0.995, 1.005);
    }

    [Fact]
    public void DTree_SamplesItsPdf()
    {
        // Histogram of the samples on an 8×8 grid against the pdf integrated
        // over each cell.
        DTree t = PeakedTree();
        var rng = new Random(11);
        const int G = 8, M = 400000, Q = 32;
        var hist = new double[G, G];
        for (int i = 0; i < M; i++)
        {
            Assert.True(t.Sample(new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()), out Vector2 p));
            hist[Math.Min(G - 1, (int)(p.X * G)), Math.Min(G - 1, (int)(p.Y * G))]++;
            Assert.True(t.PdfSquare(p) > 0f);
        }
        double chi2 = 0;
        for (int a = 0; a < G; a++)
        for (int b = 0; b < G; b++)
        {
            double mass = 0;
            for (int i = 0; i < Q; i++)
                for (int j = 0; j < Q; j++)
                    mass += t.PdfSquare(new Vector2((a + (i + 0.5f) / Q) / G, (b + (j + 0.5f) / Q) / G));
            double expected = mass / (Q * Q * G * G) * M;
            chi2 += (hist[a, b] - expected) * (hist[a, b] - expected) / Math.Max(expected, 1);
        }
        _out.WriteLine($"chi2 = {chi2:0.0} over {G * G} cells");
        Assert.True(chi2 < 120, $"chi2 {chi2}");
    }

    [Fact]
    public void SpatialTree_SplitsWhereTheRecordsAre()
    {
        // Within a pass the records of a leaf are assumed to split evenly
        // between its halves (Müller 2017), so the first pass refines
        // everywhere; from the second on, only where records keep arriving.
        var guide = new PathGuide(new AABB(new Vector3(-1), new Vector3(1)), new GuidingSettings { SpatialThreshold = 100 });
        for (int pass = 0; pass < 2; pass++)
        {
            var records = new List<GuideRecord>();
            for (int i = 0; i < 5000; i++)
                records.Add(new GuideRecord(guide.LeafAt(new Vector3(0.9f, 0.9f, 0.9f)), new Vector2(0.5f), 1f));
            guide.Splat(new[] { records });
            guide.EndIteration(1);
        }
        int near = guide.LeafAt(new Vector3(0.9f, 0.9f, 0.9f));
        Assert.NotEqual(near, guide.LeafAt(new Vector3(0.75f, 0.9f, 0.9f)));
        Assert.Equal(guide.LeafAt(new Vector3(-0.9f, -0.9f, -0.9f)), guide.LeafAt(new Vector3(-0.75f, -0.9f, -0.9f)));
        Assert.NotNull(guide.SamplingTree(near));
    }

    /// <summary>
    /// A closed room lit by the sun through a window (scenes/review/
    /// guiding-room.yaml): away from the sunlit patch on the floor, the light
    /// arrives from that patch and the window, small regions that cosine
    /// sampling rarely finds.
    /// </summary>
    private static (HittableList World, List<ILight> Lights, SkySettings Sky) Room()
    {
        var wall = new Lambertian(new Vector3(0.7f));
        var floor = new Lambertian(new Vector3(0.6f, 0.5f, 0.4f));
        static IHittable Q(Vector3 q, Vector3 u, Vector3 v, IMaterial m) => new Quad(q, u, v, m);
        var world = new HittableList(new IHittable[]
        {
            Q(new(-3, 0, -3), new(0, 0, 6), new(6, 0, 0), floor), Q(new(-3, 3, -3), new(6, 0, 0), new(0, 0, 6), wall),
            Q(new(-3, 0, -3), new(6, 0, 0), new(0, 3, 0), wall), Q(new(-3, 0, 3), new(0, 3, 0), new(6, 0, 0), wall),
            Q(new(3, 0, -3), new(0, 0, 6), new(0, 3, 0), wall),
            Q(new(-3, 0, -3), new(0, 1.2f, 0), new(0, 0, 6), wall), Q(new(-3, 2.4f, -3), new(0, 0.6f, 0), new(0, 0, 6), wall),
            Q(new(-3, 1.2f, -3), new(0, 1.2f, 0), new(0, 0, 2.4f), wall), Q(new(-3, 1.2f, 0.6f), new(0, 1.2f, 0), new(0, 0, 2.4f), wall),
        });
        var lights = new List<ILight> { new DirectionalLight(Vector3.Normalize(new Vector3(0.8f, -0.55f, 0.1f)), Vector3.One, 8f) };
        return (world, lights, new SkySettings(new Vector3(0.4f)));
    }

    private static Camera.Camera RoomCamera(int w, int h)
        => RenderHarness.LookAt(new Vector3(0.9f, 1.5f, 2.6f), new Vector3(-0.6f, 1.2f, -3), 70f, w, h);

    [Fact]
    public void Guided_ConvergesToTheUnguidedImage()
    {
        var (world, lights, sky) = Room();
        const int W = 48, H = 27;
        var cam = RoomCamera(W, H);
        float reference = RenderHarness.Mean(RenderHarness.RenderLinear(world, lights, sky, cam, W, H, 1024, 4)).Y;
        float guided = RenderHarness.Mean(RenderHarness.RenderLinear(world, lights, sky, cam, W, H, 256, 4,
            guiding: new GuidingSettings { TrainingSpp = 127, SpatialThreshold = 300 })).Y;
        _out.WriteLine($"reference {reference:0.00000} guided {guided:0.00000} ({guided / reference:0.0000})");
        Assert.InRange(guided / reference, 0.985f, 1.015f);
    }

    [Fact]
    public void Guide_LearnsWhereTheLightComesFrom()
    {
        var (world, lights, sky) = Room();
        const int W = 160, H = 90;
        var renderer = new Renderer(world, RoomCamera(W, H), lights, sky, 64, 4, referenceMode: true);
        renderer.EnableGuiding(new GuidingSettings { TrainingSpp = 63 });
        renderer.Render(W, H);
        var guide = renderer.Guide!;

        // From the back wall, the sunlit patch on the floor (about 1.2 × 1.8 m
        // around (-0.4, 0, 0.3)) subtends about 1% of the sphere.
        var patch = new Vector3(-0.4f, 0f, 0.3f);
        var wallPoint = new Vector3(0f, 1.5f, -2.99f);
        DTree? tree = guide.SamplingTree(guide.LeafAt(wallPoint));
        Assert.NotNull(tree);
        Vector3 toPatch = Vector3.Normalize(patch - wallPoint);
        float cosCone = MathF.Cos(MathF.Atan(0.9f / (patch - wallPoint).Length()));
        var rng = new Random(1);
        int hits = 0, n = 20000;
        for (int i = 0; i < n; i++)
        {
            tree!.Sample(new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()), out Vector2 sq);
            if (Vector3.Dot(DTree.FromSquare(sq), toPatch) > cosCone) hits++;
        }
        float share = hits / (float)n, uniform = (1f - cosCone) / 2f;
        _out.WriteLine($"{guide.LeafCount} regions; guide sends {share:P1} towards the patch (uniform: {uniform:P1})");
        Assert.True(share > 5f * uniform, $"{share} vs {uniform}");
    }

    [Fact]
    public void Guided_RenderIsDeterministic()
    {
        var world = new HittableList(new IHittable[] { new Sphere(Vector3.Zero, 2f, new Lambertian(new Vector3(0.7f))) });
        var lights = new List<ILight> { new PointLight(new Vector3(0.5f, 0.5f, 0), Vector3.One, 3f) };
        var cam = RenderHarness.LookAt(new Vector3(0, 0, 0.5f), new Vector3(0, 0, -1), 60f, 16, 16);
        var settings = new GuidingSettings { TrainingSpp = 7, SpatialThreshold = 500 };
        var a = RenderHarness.RenderLinear(world, lights, new SkySettings(Vector3.Zero), cam, 16, 16, 8, 4, guiding: settings);
        var b = RenderHarness.RenderLinear(world, lights, new SkySettings(Vector3.Zero), cam, 16, 16, 8, 4, guiding: settings);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(a[y, x], b[y, x]);
    }
}
