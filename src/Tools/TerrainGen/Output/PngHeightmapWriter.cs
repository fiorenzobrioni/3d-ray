using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerrainGen.Heightmap;

namespace TerrainGen.Output;

/// <summary>
/// Writes a <see cref="Heightmap2D"/> to disk as a single-channel 16-bit
/// grayscale PNG (<see cref="L16"/>) — the format the engine's HeightField
/// primitive loads through <c>HeightmapLoader</c>. 16-bit gives 65k height
/// quantisation levels, which is enough to render smooth eroded slopes
/// without visible terracing.
/// </summary>
public static class PngHeightmapWriter
{
    public static void Write(string path, Heightmap2D heightmap)
    {
        int n = heightmap.N;
        using var img = new Image<L16>(n, n);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < n; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < n; x++)
                {
                    float h = heightmap[x, y];
                    if (h < 0f) h = 0f; else if (h > 1f) h = 1f;
                    row[x] = new L16((ushort)(h * 65535f + 0.5f));
                }
            }
        });
        img.SaveAsPng(path);
    }
}
