using TerrainGen.Heightmap;

namespace TerrainGen.Hydrology;

/// <summary>
/// Stamps WaterKind.Sea on every cell whose height is below the configured
/// sea level. Trivially simple but kept as a separate pass so the pipeline
/// reads sequentially.
/// </summary>
public static class SeaLevel
{
    public static void Apply(Heightmap2D hm, WaterMask mask)
    {
        if (mask.SeaLevel < 0f) return; // sea disabled
        int n = hm.N;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            if (hm[x, z] < mask.SeaLevel)
                mask.MarkWet(x, z, WaterKind.Sea, mask.SeaLevel);
        }
    }
}
