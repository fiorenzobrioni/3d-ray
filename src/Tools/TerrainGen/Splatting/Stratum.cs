namespace TerrainGen.Splatting;

/// <summary>
/// Material strata that the classifier assigns to each triangle. The water
/// surface is not a stratum: it's a separate flat plane added at sea/lake/river
/// level. <see cref="WaterBed"/> is the underwater terrain visible through it.
/// </summary>
public enum Stratum : byte
{
    Ground   = 0,
    Rock     = 1,
    Snow     = 2,
    Sand     = 3,
    WaterBed = 4,
}

public static class StratumExtensions
{
    public static string Suffix(this Stratum s) => s switch
    {
        Stratum.Ground   => "ground",
        Stratum.Rock     => "rock",
        Stratum.Snow     => "snow",
        Stratum.Sand     => "sand",
        Stratum.WaterBed => "waterbed",
        _ => "unknown",
    };
}
