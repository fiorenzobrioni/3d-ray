using System;
using System.Collections.Generic;
using TerrainGen.Heightmap;

namespace TerrainGen.Hydrology;

/// <summary>
/// Picks K sources in the upper quartile of the heightmap, traces a
/// steepest-descent path until it hits sea, lake, edge or a previously carved
/// river bed, then carves a Gaussian channel along that path. Each cell along
/// the path is also marked as <see cref="WaterKind.River"/> in the water mask.
/// </summary>
public static class RiverCarver
{
    public static void Apply(Heightmap2D hm, WaterMask mask, int seed, int riverCount = 5, float carveDepth = 0.04f, float carveRadius = 2f)
    {
        int n = hm.N;
        var rng = new Random(seed ^ unchecked((int)0x9E3779B9));
        var (hmin, hmax, _) = hm.Stats();
        float sourceMin = hmin + (hmax - hmin) * 0.70f;

        // Build a candidate list of high-altitude, dry cells away from edges.
        var candidates = new List<int>(2048);
        for (int z = 4; z < n - 4; z++)
        for (int x = 4; x < n - 4; x++)
        {
            if (hm[x, z] >= sourceMin && !mask.IsWet(x, z))
                candidates.Add(z * n + x);
        }
        if (candidates.Count == 0) return;

        for (int riv = 0; riv < riverCount; riv++)
        {
            int idx = candidates[rng.Next(candidates.Count)];
            int x = idx % n, z = idx / n;
            CarveOne(hm, mask, x, z, carveDepth, carveRadius);
        }
    }

    private static void CarveOne(Heightmap2D hm, WaterMask mask, int x, int z, float depth, float radius)
    {
        int n = hm.N;
        var path = new List<(int x, int z)>(256);
        var seen = new HashSet<int>();
        for (int step = 0; step < n * 2; step++)
        {
            int idx = z * n + x;
            if (!seen.Add(idx)) break;
            path.Add((x, z));

            // Stop conditions
            if (x <= 1 || x >= n - 2 || z <= 1 || z >= n - 2) break;
            if (mask.Kind[idx] == WaterKind.Sea || mask.Kind[idx] == WaterKind.Lake) break;
            if (mask.Kind[idx] == WaterKind.River && step > 0) break;

            // Find lowest neighbour (8-connected).
            float h = hm.Data[idx];
            int bestX = x, bestZ = z;
            float bestH = h;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oz == 0) continue;
                int nx = x + ox, nz = z + oz;
                float hn = hm[nx, nz];
                if (hn < bestH) { bestH = hn; bestX = nx; bestZ = nz; }
            }
            if (bestX == x && bestZ == z)
            {
                // Local minimum: carve a small "lake-spillover" notch and stop.
                break;
            }
            x = bestX; z = bestZ;
        }

        // Apply the carve as a Gaussian valley along the path.
        int radInt = (int)MathF.Ceiling(radius);
        float invSig2 = 1f / (radius * radius);
        foreach (var (px, pz) in path)
        {
            for (int oz = -radInt; oz <= radInt; oz++)
            for (int ox = -radInt; ox <= radInt; ox++)
            {
                int nx = px + ox, nz = pz + oz;
                if (nx < 0 || nx >= n || nz < 0 || nz >= n) continue;
                float d2 = ox * ox + oz * oz;
                float w = MathF.Exp(-d2 * invSig2);
                int i = nz * n + nx;
                hm.Data[i] -= depth * w;
            }
            // Mark the actual centerline as river (so the splat classifier paints
            // it with water_bed material). We don't paint the whole carve radius
            // because the channel walls should still be banked rock/grass.
            int ic = pz * n + px;
            if (mask.Kind[ic] == WaterKind.None)
                mask.MarkWet(px, pz, WaterKind.River, hm.Data[ic]);
        }
    }
}
