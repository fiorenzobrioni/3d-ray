using RayTracer.Imaging;
using TerrainGen.Heightmap;

namespace TerrainGen.Output;

/// <summary>
/// Writes a <see cref="Heightmap2D"/> to disk as a single-channel 16-bit
/// grayscale PNG, the format the engine's HeightField primitive loads through
/// <c>HeightmapLoader</c>. 16-bit gives 65k height quantisation levels, which
/// is enough to render smooth eroded slopes without visible terracing.
/// </summary>
public static class PngHeightmapWriter
{
    public static void Write(string path, Heightmap2D heightmap)
    {
        int n = heightmap.N;
        var img = new RasterImage(n, n, 1, 16);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float h = Math.Clamp(heightmap[x, y], 0f, 1f);
                img[x, y, 0] = (ushort)(h * 65535f + 0.5f);
            }
        Png.Save(path, img);
    }
}
