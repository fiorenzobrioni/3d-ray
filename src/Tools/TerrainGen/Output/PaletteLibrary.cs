using System.Numerics;
using TerrainGen.Splatting;

namespace TerrainGen.Output;

/// <summary>
/// Per-stratum, per-season material parameters plus a small sky/light palette
/// for the optional preview scene. All numeric values are tuned by hand to
/// look reasonable on a path-traced render with the engine's Disney BSDF.
/// </summary>
public static class PaletteLibrary
{
    public sealed record MatPalette(
        string Type,                    // "lambertian" | "disney"
        Vector3 ColorA,                 // texture color 1
        Vector3 ColorB,                 // texture color 2
        float NoiseScale,
        float NoiseStrength,
        float Roughness,
        float Specular,
        float Subsurface,
        Vector3 SubsurfaceColor);

    public static MatPalette ForStratum(Stratum stratum, Season season) => stratum switch
    {
        Stratum.Ground   => GroundFor(season),
        Stratum.Rock     => RockFor(season),
        Stratum.Snow     => SnowFor(season),
        Stratum.Sand     => SandFor(season),
        Stratum.WaterBed => WaterBedFor(season),
        _                => GroundFor(season),
    };

    private static MatPalette GroundFor(Season season)
    {
        Vector3 a, b; float rough = 0.95f;
        switch (season)
        {
            case Season.Primavera: a = new(0.32f, 0.48f, 0.18f); b = new(0.22f, 0.36f, 0.12f); break;
            case Season.Estate:    a = new(0.42f, 0.50f, 0.20f); b = new(0.30f, 0.36f, 0.14f); break;
            case Season.Autunno:   a = new(0.55f, 0.32f, 0.12f); b = new(0.36f, 0.20f, 0.08f); break;
            case Season.Inverno:   a = new(0.72f, 0.74f, 0.78f); b = new(0.45f, 0.45f, 0.50f); rough = 0.90f; break;
            default:               a = new(0.40f, 0.48f, 0.20f); b = new(0.28f, 0.34f, 0.14f); break;
        }
        return new MatPalette("lambertian", a, b, 4.0f, 0.5f, rough, 0.10f, 0f, Vector3.Zero);
    }

    private static MatPalette RockFor(Season season)
    {
        Vector3 a, b;
        switch (season)
        {
            case Season.Inverno: a = new(0.38f, 0.38f, 0.42f); b = new(0.22f, 0.22f, 0.26f); break;
            default:             a = new(0.32f, 0.30f, 0.28f); b = new(0.18f, 0.17f, 0.18f); break;
        }
        return new MatPalette("disney", a, b, 6.0f, 0.7f, 0.85f, 0.20f, 0f, Vector3.Zero);
    }

    private static MatPalette SnowFor(Season season)
        => new("disney",
            new(0.95f, 0.97f, 1.0f), new(0.88f, 0.92f, 0.97f),
            3.0f, 0.30f,
            0.78f, 0.30f,
            0.25f, new Vector3(0.85f, 0.92f, 1.0f));

    private static MatPalette SandFor(Season season)
    {
        Vector3 a, b;
        switch (season)
        {
            case Season.Inverno: a = new(0.78f, 0.78f, 0.80f); b = new(0.62f, 0.62f, 0.65f); break;
            default:             a = new(0.82f, 0.72f, 0.48f); b = new(0.62f, 0.50f, 0.30f); break;
        }
        return new MatPalette("lambertian", a, b, 5.0f, 0.60f, 0.95f, 0.10f, 0f, Vector3.Zero);
    }

    private static MatPalette WaterBedFor(Season season)
        => new("lambertian",
            new(0.20f, 0.26f, 0.22f), new(0.10f, 0.14f, 0.12f),
            3.0f, 0.50f, 0.95f, 0.05f, 0f, Vector3.Zero);

    public sealed record SkyPalette(
        Vector3 Zenith,
        Vector3 Horizon,
        Vector3 Ground,
        Vector3 SunDir,
        Vector3 SunColor,
        float SunIntensity,
        float SunSize);

    public static SkyPalette SkyFor(Season season) => season switch
    {
        Season.Primavera => new(
            Zenith:   new(0.30f, 0.55f, 0.85f),
            Horizon:  new(0.92f, 0.94f, 0.96f),
            Ground:   new(0.30f, 0.36f, 0.20f),
            SunDir:   new(-0.4f, -0.7f, -0.3f),
            SunColor: new(1.0f, 0.97f, 0.92f),
            SunIntensity: 14f, SunSize: 2.5f),

        Season.Estate => new(
            Zenith:   new(0.18f, 0.38f, 0.78f),
            Horizon:  new(0.85f, 0.90f, 0.96f),
            Ground:   new(0.36f, 0.34f, 0.20f),
            SunDir:   new(-0.5f, -0.85f, -0.2f),
            SunColor: new(1.0f, 0.96f, 0.86f),
            SunIntensity: 18f, SunSize: 2.0f),

        Season.Autunno => new(
            Zenith:   new(0.10f, 0.20f, 0.45f),
            Horizon:  new(0.92f, 0.55f, 0.30f),
            Ground:   new(0.30f, 0.20f, 0.10f),
            SunDir:   new(-0.7f, -0.4f, -0.5f),
            SunColor: new(1.0f, 0.78f, 0.55f),
            SunIntensity: 12f, SunSize: 3.0f),

        Season.Inverno => new(
            Zenith:   new(0.25f, 0.32f, 0.45f),
            Horizon:  new(0.78f, 0.82f, 0.88f),
            Ground:   new(0.55f, 0.55f, 0.60f),
            SunDir:   new(-0.4f, -0.55f, -0.5f),
            SunColor: new(0.95f, 0.95f, 1.0f),
            SunIntensity: 10f, SunSize: 3.0f),

        _ => SkyFor(Season.Estate),
    };
}
