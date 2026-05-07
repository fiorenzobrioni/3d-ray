using System;
using System.Collections.Generic;
using TerrainGen.Heightmap;

namespace TerrainGen.Hydrology;

/// <summary>
/// Finds local depressions and floods them up to the lowest "spill" point so
/// that they become flat lakes. Approach: for each candidate seed (a cell
/// below the local mean) we run a region-growing fill that always expands to
/// the lowest neighbouring frontier cell; we stop when that frontier cell
/// would breach the sea or fall below an edge of the map (which means we'd be
/// extending into the sea or off the map). The water level for that lake is
/// the height of the spill cell. We only keep lakes above a minimum area to
/// avoid puddles painted onto noise micro-pits.
/// </summary>
public static class LakeFinder
{
    public static void Apply(Heightmap2D hm, WaterMask mask, int seed, int maxLakes = 6, float minAreaFraction = 0.005f)
    {
        int n = hm.N;
        int totalCells = n * n;
        int minArea = Math.Max(8, (int)(totalCells * minAreaFraction));

        var rng = new Random(seed ^ 0x1FAB91);
        var visited = new bool[totalCells];

        // Pre-mark already-wet cells as visited so seas/rivers aren't re-flooded.
        for (int i = 0; i < mask.Kind.Length; i++)
            if (mask.Kind[i] != WaterKind.None) visited[i] = true;

        // Pick seeds: cells in the lower 30% of heights, away from edges.
        var (hmin, hmax, _) = hm.Stats();
        float seedThresh = hmin + (hmax - hmin) * 0.30f;

        int found = 0;
        // Scan in shuffled order so multiple lakes don't all spawn from one corner.
        var order = new int[totalCells];
        for (int i = 0; i < totalCells; i++) order[i] = i;
        for (int i = totalCells - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        foreach (int cell in order)
        {
            if (found >= maxLakes) break;
            if (visited[cell]) continue;
            int sx = cell % n, sz = cell / n;
            if (sx == 0 || sx == n - 1 || sz == 0 || sz == n - 1) continue;
            if (hm.Data[cell] > seedThresh) continue;

            if (TryFloodFromSeed(hm, mask, visited, sx, sz, minArea))
                found++;
        }
    }

    private static bool TryFloodFromSeed(Heightmap2D hm, WaterMask mask, bool[] visited, int sx, int sz, int minArea)
    {
        int n = hm.N;
        var basinCells = new List<int>(256);
        var inBasin = new HashSet<int>();
        var frontier = new SortedSet<(float h, int idx)>(new FrontierComparer());

        int seedIdx = sz * n + sx;
        inBasin.Add(seedIdx);
        basinCells.Add(seedIdx);
        AddNeighbours(frontier, hm, inBasin, sx, sz, n);

        float spillLevel = hm.Data[seedIdx];

        // Conservative cap on flood area to keep runtime bounded.
        int maxBasinSize = Math.Min(n * n / 4, 200_000);

        while (frontier.Count > 0 && basinCells.Count < maxBasinSize)
        {
            var top = frontier.Min;
            frontier.Remove(top);
            int idx = top.idx;
            if (inBasin.Contains(idx)) continue;

            int x = idx % n, z = idx / n;

            // If we've reached the map edge, the basin "spills off" — abort.
            if (x == 0 || x == n - 1 || z == 0 || z == n - 1) return false;
            // If we run into the sea, abort (we'd just merge into the ocean).
            if (mask.Kind[idx] == WaterKind.Sea) return false;

            spillLevel = top.h;

            inBasin.Add(idx);
            basinCells.Add(idx);
            AddNeighbours(frontier, hm, inBasin, x, z, n);
        }

        if (basinCells.Count < minArea) return false;

        foreach (int idx in basinCells)
        {
            visited[idx] = true;
            if (mask.Kind[idx] == WaterKind.None && hm.Data[idx] <= spillLevel)
                mask.MarkWet(idx % n, idx / n, WaterKind.Lake, spillLevel);
        }
        return true;
    }

    private static void AddNeighbours(SortedSet<(float h, int idx)> frontier, Heightmap2D hm, HashSet<int> inBasin, int x, int z, int n)
    {
        int[] dx = { -1, 1, 0, 0 };
        int[] dz = { 0, 0, -1, 1 };
        for (int k = 0; k < 4; k++)
        {
            int nx = x + dx[k], nz = z + dz[k];
            if (nx < 0 || nx >= n || nz < 0 || nz >= n) continue;
            int idx = nz * n + nx;
            if (inBasin.Contains(idx)) continue;
            frontier.Add((hm.Data[idx], idx));
        }
    }

    private sealed class FrontierComparer : IComparer<(float h, int idx)>
    {
        public int Compare((float h, int idx) a, (float h, int idx) b)
        {
            int c = a.h.CompareTo(b.h);
            return c != 0 ? c : a.idx.CompareTo(b.idx);
        }
    }
}
