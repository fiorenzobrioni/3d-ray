using System;
using TerrainGen.Heightmap;

namespace TerrainGen.Erosion;

/// <summary>
/// Talus-angle thermal erosion: when the slope between a cell and its lowest
/// neighbour exceeds <c>talusAngle</c>, slide a fraction of the excess to that
/// neighbour. Iterating this smooths out impossibly-steep slopes and produces
/// scree fans at the base of cliffs — cheap and effective shape softener.
/// </summary>
public static class ThermalErosion
{
    public static void Apply(Heightmap2D hm, int iterations = 8, float talus = 0.012f, float fraction = 0.5f)
    {
        int n = hm.N;
        var src = hm.Data;
        var dst = new float[src.Length];
        for (int it = 0; it < iterations; it++)
        {
            Array.Copy(src, dst, src.Length);
            for (int z = 1; z < n - 1; z++)
            for (int x = 1; x < n - 1; x++)
            {
                float h = src[z * n + x];
                int dx = 0, dz = 0;
                float maxDiff = talus;
                for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oz == 0) continue;
                    float diff = h - src[(z + oz) * n + (x + ox)];
                    if (diff > maxDiff) { maxDiff = diff; dx = ox; dz = oz; }
                }
                if (dx != 0 || dz != 0)
                {
                    float move = fraction * (maxDiff - talus);
                    dst[z * n + x] -= move;
                    dst[(z + dz) * n + (x + dx)] += move;
                }
            }
            Array.Copy(dst, src, src.Length);
        }
    }
}
