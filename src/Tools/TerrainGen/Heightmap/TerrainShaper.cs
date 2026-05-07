using System;

namespace TerrainGen.Heightmap;

/// <summary>
/// Builds the initial heightmap from a NoiseStack and applies large-scale
/// shaping based on the terrain type and water inclusion (e.g. island radial mask).
/// </summary>
public static class TerrainShaper
{
    public static Heightmap2D Build(GenerationConfig cfg)
    {
        var stack = NoiseStack.ForType(cfg.Type, cfg.Seed);
        var hm = new Heightmap2D(cfg.Resolution);

        // Sample on a grid centred at origin so radial masks (islands) are isotropic.
        // World-space cell coords are (-half..+half) for the noise stack only; sampling
        // in those units (rather than pixel indices) keeps frequencies consistent across
        // resolutions.
        float worldHalf = cfg.Size * 0.5f;
        float cellWorld = cfg.Size / (cfg.Resolution - 1);

        bool islandMask = cfg.HasFlag(WaterFeatures.Isole);

        for (int z = 0; z < cfg.Resolution; z++)
        for (int x = 0; x < cfg.Resolution; x++)
        {
            float wx = -worldHalf + x * cellWorld;
            float wz = -worldHalf + z * cellWorld;
            float h = stack.Sample(wx, wz);

            if (islandMask)
            {
                // Smoothstep falloff from centre: keeps land in the middle, drops to
                // sea level at edges. Scale chosen so ~30% of map is firmly above sea.
                float r = MathF.Sqrt(wx * wx + wz * wz) / worldHalf; // 0 at centre, 1 at corner
                float falloff = 1f - Smoothstep(0.40f, 0.95f, r);
                // Bias up by sea_level so isolated peaks emerge above water.
                float bias = MathF.Max(cfg.SeaLevel, 0.30f) + 0.05f;
                h = (h * falloff) + bias * (1f - falloff) * 0.0f; // pure multiplicative drop
                h *= falloff;
            }

            hm[x, z] = h;
        }

        // Renormalise to [0,1] so erosion + classifier thresholds are stable.
        hm.Normalize01();
        return hm;
    }

    private static float Smoothstep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
