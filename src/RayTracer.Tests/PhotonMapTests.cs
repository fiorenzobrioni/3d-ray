using System.Numerics;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Equivalence tests for <see cref="PhotonMap"/> in the style of
/// <c>AabbTests</c>/<c>BvhEquivalenceTests</c>: the spatial-hash grid query is
/// validated against a brute-force oracle so the acceleration structure can
/// never silently drop or duplicate a photon. Seeds are passed via
/// <see cref="InlineDataAttribute"/> so any failure replays deterministically.
/// </summary>
public class PhotonMapTests
{
    private static Photon[] RandomPhotons(int n, int seed)
    {
        var rng = new Random(seed);
        var photons = new Photon[n];
        for (int i = 0; i < n; i++)
        {
            var p = new Vector3(
                (float)(rng.NextDouble() * 20.0 - 10.0),
                (float)(rng.NextDouble() * 20.0 - 10.0),
                (float)(rng.NextDouble() * 20.0 - 10.0));
            var dir = Vector3.Normalize(new Vector3(
                (float)(rng.NextDouble() - 0.5),
                (float)(rng.NextDouble() - 0.5),
                (float)(rng.NextDouble() - 0.5)));
            var power = new Vector3((float)rng.NextDouble());
            photons[i] = new Photon(p, dir, power);
        }
        return photons;
    }

    private static SortedSet<int> BruteForceWithin(Photon[] photons, Vector3 q, float r)
    {
        float r2 = r * r;
        var set = new SortedSet<int>();
        for (int i = 0; i < photons.Length; i++)
            if (Vector3.DistanceSquared(photons[i].Position, q) <= r2)
                set.Add(i);
        return set;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void QueryRadius_MatchesBruteForce(int seed)
    {
        var photons = RandomPhotons(2000, seed);
        var map = new PhotonMap(photons, cellSize: 0.75f);
        var rng = new Random(seed * 31 + 5);

        for (int t = 0; t < 50; t++)
        {
            var q = new Vector3(
                (float)(rng.NextDouble() * 24.0 - 12.0),
                (float)(rng.NextDouble() * 24.0 - 12.0),
                (float)(rng.NextDouble() * 24.0 - 12.0));
            float r = (float)(rng.NextDouble() * 3.0 + 0.1);

            var expected = BruteForceWithin(photons, q, r);
            var got = new List<int>();
            map.QueryRadius(q, r, got);

            // No duplicates and exact set equality with the oracle.
            Assert.Equal(got.Count, new HashSet<int>(got).Count);
            Assert.Equal(expected, new SortedSet<int>(got));
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    public void GatherKNearest_ReturnsTheTrueKNearest(int seed)
    {
        var photons = RandomPhotons(1500, seed);
        var map = new PhotonMap(photons, cellSize: 0.6f);
        var rng = new Random(seed * 17 + 1);

        const int k = 30;
        const float maxR = 6f;
        Span<int> idx = new int[k];
        Span<float> heap = new float[k];

        for (int t = 0; t < 30; t++)
        {
            var q = new Vector3(
                (float)(rng.NextDouble() * 16.0 - 8.0),
                (float)(rng.NextDouble() * 16.0 - 8.0),
                (float)(rng.NextDouble() * 16.0 - 8.0));

            int count = map.GatherKNearest(q, k, maxR, idx, heap, out float radius);

            // Oracle: the true k nearest within maxR, by distance.
            var withinMax = BruteForceWithin(photons, q, maxR).ToList();
            withinMax.Sort((a, b) =>
                Vector3.DistanceSquared(photons[a].Position, q)
                .CompareTo(Vector3.DistanceSquared(photons[b].Position, q)));
            int expectedCount = Math.Min(k, withinMax.Count);
            Assert.Equal(expectedCount, count);

            if (count == 0) continue;

            // The gathered set must equal the expectedCount nearest (as a set —
            // ties at the boundary are resolved consistently by distance).
            var expectedSet = new SortedSet<int>(withinMax.Take(expectedCount));
            var gotSet = new SortedSet<int>(idx.Slice(0, count).ToArray());
            Assert.Equal(expectedSet, gotSet);

            // radius is the farthest gathered photon's distance.
            float farthest = 0f;
            for (int i = 0; i < count; i++)
                farthest = MathF.Max(farthest, Vector3.Distance(photons[idx[i]].Position, q));
            Assert.True(MathF.Abs(farthest - radius) < 1e-3f);
        }
    }

    [Fact]
    public void EmptyMap_QueriesAreSafe()
    {
        var map = new PhotonMap(Array.Empty<Photon>(), cellSize: 1f);
        var got = new List<int>();
        map.QueryRadius(Vector3.Zero, 5f, got);
        Assert.Empty(got);

        Span<int> idx = new int[8];
        Span<float> heap = new float[8];
        int count = map.GatherKNearest(Vector3.Zero, 8, 5f, idx, heap, out float radius);
        Assert.Equal(0, count);
        Assert.Equal(0f, radius);
    }
}
