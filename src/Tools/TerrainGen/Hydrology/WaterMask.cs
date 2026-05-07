using System;
using TerrainGen.Heightmap;

namespace TerrainGen.Hydrology;

/// <summary>
/// Per-cell water classification produced by the hydrology pipeline. Each cell
/// is either dry land or one of three water types (sea/lake/river). Triangles
/// covering water cells are emitted into a separate "waterbed" mesh and a
/// flat water plane is added at the appropriate level.
/// </summary>
public enum WaterKind : byte
{
    None  = 0,
    Sea   = 1,
    Lake  = 2,
    River = 3,
}

public sealed class WaterMask
{
    public int N { get; }
    public WaterKind[] Kind { get; }
    public float[] WaterLevel { get; }    // absolute height of the water surface in each cell, when wet

    public float SeaLevel { get; init; }
    public bool HasAnyWater { get; private set; }

    public WaterMask(int n, float seaLevel)
    {
        N = n;
        Kind = new WaterKind[n * n];
        WaterLevel = new float[n * n];
        SeaLevel = seaLevel;
    }

    public bool IsWet(int x, int z) => Kind[z * N + x] != WaterKind.None;

    public void MarkWet(int x, int z, WaterKind kind, float level)
    {
        int i = z * N + x;
        Kind[i] = kind;
        WaterLevel[i] = level;
        HasAnyWater = true;
    }
}
