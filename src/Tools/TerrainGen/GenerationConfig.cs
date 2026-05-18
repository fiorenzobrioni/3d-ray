using System;

namespace TerrainGen;

public enum TerrainType { Pianura, Collina, Montagna }
public enum Season { Primavera, Estate, Autunno, Inverno }

[Flags]
public enum WaterFeatures
{
    None   = 0,
    Fiumi  = 1 << 0,
    Laghi  = 1 << 1,
    Mare   = 1 << 2,
    Isole  = 1 << 3,
}

public sealed record GenerationConfig
{
    public required string Name { get; init; }
    public required string OutputDir { get; init; }
    public TerrainType Type { get; init; } = TerrainType.Collina;
    public WaterFeatures Include { get; init; } = WaterFeatures.None;
    public Season Season { get; init; } = Season.Estate;
    public float Size { get; init; } = 100f;
    public int Resolution { get; init; } = 256;
    public int Seed { get; init; } = 0;
    public bool ErosionEnabled { get; init; } = true;
    public int ErosionIters { get; init; } = 50_000;
    public float SeaLevel { get; init; } = 0.30f;
    public bool WithCameras { get; init; } = false;

    public bool HasFlag(WaterFeatures f) => (Include & f) == f;
}
