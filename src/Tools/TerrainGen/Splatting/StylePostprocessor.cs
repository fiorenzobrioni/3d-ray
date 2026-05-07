using System;
using TerrainGen.Heightmap;

namespace TerrainGen.Splatting;

/// <summary>
/// Style-specific transformations applied to the heightmap right before mesh
/// emission. Realistic = pass through; Minecraft = quantize Y; Lowpoly =
/// downsample. Both Minecraft and Lowpoly want flat-shaded triangles in the
/// emitter.
/// </summary>
public static class StylePostprocessor
{
    public const int MinecraftLevels = 16;
    public const int LowpolyDecimation = 2; // halve resolution

    public static (Heightmap2D map, bool flatShade) Apply(Heightmap2D src, Style style)
    {
        switch (style)
        {
            case Style.Realistic:
                return (src, false);

            case Style.Minecraft:
            {
                var copy = new Heightmap2D(src.N);
                for (int i = 0; i < src.Data.Length; i++)
                {
                    float v = Math.Clamp(src.Data[i], 0f, 1f);
                    int step = (int)MathF.Floor(v * MinecraftLevels);
                    if (step >= MinecraftLevels) step = MinecraftLevels - 1;
                    copy.Data[i] = (float)step / MinecraftLevels;
                }
                return (copy, true);
            }

            case Style.Lowpoly:
            {
                int n2 = Math.Max(8, src.N / LowpolyDecimation);
                var dst = new Heightmap2D(n2);
                float scale = (float)(src.N - 1) / (n2 - 1);
                for (int z = 0; z < n2; z++)
                for (int x = 0; x < n2; x++)
                    dst[x, z] = src.SampleBilinear(x * scale, z * scale);
                return (dst, true);
            }

            default:
                return (src, false);
        }
    }
}
