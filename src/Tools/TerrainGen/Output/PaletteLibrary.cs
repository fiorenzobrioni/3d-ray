using System.Numerics;
using TerrainGen.Splatting;

namespace TerrainGen.Output;

/// <summary>
/// Per-stratum, per-season material parameters and a small sky/light palette
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

    public static MatPalette ForStratum(Stratum stratum, Season season, Style style)
    {
        // Boost saturation for Minecraft style — more cartoony.
        float saturate = style == Style.Minecraft ? 1.20f : 1.0f;
        float noiseBias = style switch
        {
            Style.Minecraft => 1.5f,    // bigger noise blobs to mimic block patterns
            Style.Lowpoly   => 0.7f,
            _               => 1.0f,
        };

        return stratum switch
        {
            Stratum.Ground   => GroundFor(season, saturate, noiseBias),
            Stratum.Rock     => RockFor(season, saturate, noiseBias),
            Stratum.Snow     => SnowFor(season, saturate, noiseBias),
            Stratum.Sand     => SandFor(season, saturate, noiseBias),
            Stratum.WaterBed => WaterBedFor(season, saturate, noiseBias),
            _                => GroundFor(season, saturate, noiseBias),
        };
    }

    private static MatPalette GroundFor(Season season, float sat, float nb)
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
        return new MatPalette(
            Type: "lambertian",
            ColorA: ApplySat(a, sat), ColorB: ApplySat(b, sat),
            NoiseScale: 4.0f * nb, NoiseStrength: 0.5f,
            Roughness: rough, Specular: 0.10f,
            Subsurface: 0.0f, SubsurfaceColor: new Vector3(0f));
    }

    private static MatPalette RockFor(Season season, float sat, float nb)
    {
        Vector3 a, b;
        switch (season)
        {
            case Season.Inverno: a = new(0.38f, 0.38f, 0.42f); b = new(0.22f, 0.22f, 0.26f); break;
            default:             a = new(0.32f, 0.30f, 0.28f); b = new(0.18f, 0.17f, 0.18f); break;
        }
        return new MatPalette(
            Type: "disney",
            ColorA: ApplySat(a, sat), ColorB: ApplySat(b, sat),
            NoiseScale: 6.0f * nb, NoiseStrength: 0.7f,
            Roughness: 0.85f, Specular: 0.20f,
            Subsurface: 0.0f, SubsurfaceColor: new Vector3(0f));
    }

    private static MatPalette SnowFor(Season season, float sat, float nb)
    {
        return new MatPalette(
            Type: "disney",
            ColorA: new(0.95f, 0.97f, 1.0f),
            ColorB: new(0.88f, 0.92f, 0.97f),
            NoiseScale: 3.0f * nb, NoiseStrength: 0.30f,
            Roughness: 0.78f, Specular: 0.30f,
            Subsurface: 0.25f, SubsurfaceColor: new Vector3(0.85f, 0.92f, 1.0f));
    }

    private static MatPalette SandFor(Season season, float sat, float nb)
    {
        Vector3 a, b;
        switch (season)
        {
            case Season.Inverno: a = new(0.78f, 0.78f, 0.80f); b = new(0.62f, 0.62f, 0.65f); break;
            default:             a = new(0.82f, 0.72f, 0.48f); b = new(0.62f, 0.50f, 0.30f); break;
        }
        return new MatPalette(
            Type: "lambertian",
            ColorA: ApplySat(a, sat), ColorB: ApplySat(b, sat),
            NoiseScale: 5.0f * nb, NoiseStrength: 0.60f,
            Roughness: 0.95f, Specular: 0.10f,
            Subsurface: 0.0f, SubsurfaceColor: new Vector3(0f));
    }

    private static MatPalette WaterBedFor(Season season, float sat, float nb)
    {
        // Underwater "mud" — visible through translucent water above.
        return new MatPalette(
            Type: "lambertian",
            ColorA: new(0.20f, 0.26f, 0.22f),
            ColorB: new(0.10f, 0.14f, 0.12f),
            NoiseScale: 3.0f * nb, NoiseStrength: 0.50f,
            Roughness: 0.95f, Specular: 0.05f,
            Subsurface: 0.0f, SubsurfaceColor: new Vector3(0f));
    }

    public sealed record SkyPalette(
        Vector3 Zenith,
        Vector3 Horizon,
        Vector3 Ground,
        Vector3 SunDir,
        Vector3 SunColor,
        float SunIntensity,
        float SunSize);

    public static SkyPalette SkyFor(Season season)
    {
        return season switch
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

    private static Vector3 ApplySat(Vector3 c, float k)
    {
        if (k == 1f) return c;
        float lum = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        var grey = new Vector3(lum);
        var sat = grey + (c - grey) * k;
        return new Vector3(
            System.Math.Clamp(sat.X, 0f, 1f),
            System.Math.Clamp(sat.Y, 0f, 1f),
            System.Math.Clamp(sat.Z, 0f, 1f));
    }
}
