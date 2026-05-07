using System;
using System.Numerics;
using TerrainGen.Heightmap;
using TerrainGen.Hydrology;

namespace TerrainGen.Splatting;

/// <summary>
/// Decides which Stratum each triangle belongs to. We compute the triangle's
/// average height in [0,1] and slope (1 - n.y), plus check whether its three
/// underlying cells are wet, then apply a fixed decision tree. Thresholds
/// adjust mildly with season (winter pushes snow line down).
/// </summary>
public static class StratumClassifier
{
    public static Stratum Classify(GenerationConfig cfg, float hAvg, float slope, bool anyWet, bool allWet)
    {
        if (allWet) return Stratum.WaterBed;

        float snowMin = cfg.Season switch
        {
            Season.Inverno  => 0.55f,
            Season.Autunno  => 0.78f,
            Season.Primavera => 0.82f,
            _               => 0.85f,
        };

        // Beach band depends on whether sea is enabled.
        bool seaEnabled = cfg.SeaLevel >= 0f;
        float beachMax = seaEnabled ? cfg.SeaLevel + 0.04f : -1f;

        if (slope > 0.65f)        return Stratum.Rock;
        if (hAvg >= snowMin)      return Stratum.Snow;
        if (hAvg >= 0.70f)        return Stratum.Rock;
        if (anyWet)               return Stratum.Ground; // riverside but not yet underwater
        if (seaEnabled && hAvg < beachMax) return Stratum.Sand;
        return Stratum.Ground;
    }
}
