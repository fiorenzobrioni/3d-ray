using System;
using System.Collections.Generic;
using TerrainGen.Heightmap;

namespace TerrainGen.Erosion;

/// <summary>
/// Particle-drop hydraulic erosion (Hans Beyer / Sebastian Lague style).
///
/// Each "drop" is a virtual water particle that falls onto the terrain and is
/// stepped along the local gradient. At every step we compute the carrying
/// capacity from velocity, slope and water volume; if the drop carries more
/// sediment than capacity it deposits, otherwise it picks up material with a
/// brush kernel (so we don't carve a single cell into a needle). Evaporation
/// reduces capacity over time so drops finally settle. Tens of thousands of
/// drops produce convincing canyons, alluvial fans and dendritic drainage.
/// </summary>
public static class HydraulicErosion
{
    public static void Apply(Heightmap2D hm, int iterations, int seed)
    {
        const int   maxLifetime    = 30;
        const float inertia        = 0.05f;
        const float sedimentCap    = 4f;
        const float minSlope       = 0.01f;
        const float erodeSpeed     = 0.3f;
        const float depositSpeed   = 0.3f;
        const float evaporateSpeed = 0.01f;
        const float gravity        = 4f;
        const int   brushRadius    = 3;

        int n = hm.N;
        var data = hm.Data;
        var rng = new Random(seed);
        var brush = BuildBrushOffsets(brushRadius);

        for (int iter = 0; iter < iterations; iter++)
        {
            float posX = (float)rng.NextDouble() * (n - 1);
            float posZ = (float)rng.NextDouble() * (n - 1);
            float dirX = 0f, dirZ = 0f;
            float speed = 1f, water = 1f, sediment = 0f;

            for (int life = 0; life < maxLifetime; life++)
            {
                int nodeX = (int)posX;
                int nodeZ = (int)posZ;
                float cellOffX = posX - nodeX;
                float cellOffZ = posZ - nodeZ;

                var (h, gradX, gradZ) = HeightAndGrad(data, n, nodeX, nodeZ, cellOffX, cellOffZ);

                dirX = dirX * inertia - gradX * (1f - inertia);
                dirZ = dirZ * inertia - gradZ * (1f - inertia);
                float dl = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
                if (dl > 1e-6f) { dirX /= dl; dirZ /= dl; }

                posX += dirX;
                posZ += dirZ;

                if ((dirX == 0f && dirZ == 0f) ||
                    posX < 0f || posX >= n - 1 || posZ < 0f || posZ >= n - 1)
                    break;

                float newH = HeightOnly(data, n, posX, posZ);
                float dH = newH - h;

                float capacity = MathF.Max(-dH, minSlope) * speed * water * sedimentCap;

                if (sediment > capacity || dH > 0f)
                {
                    float deposit = (dH > 0f) ? MathF.Min(dH, sediment) : (sediment - capacity) * depositSpeed;
                    sediment -= deposit;
                    DepositBilinear(data, n, nodeX, nodeZ, cellOffX, cellOffZ, deposit);
                }
                else
                {
                    float erodeAmount = MathF.Min((capacity - sediment) * erodeSpeed, -dH);
                    EroseBrush(data, n, nodeX, nodeZ, brush, erodeAmount, ref sediment);
                }

                speed = MathF.Sqrt(MathF.Max(speed * speed + dH * gravity, 0f));
                water *= (1f - evaporateSpeed);
            }
        }
    }

    private static (float h, float gradX, float gradZ) HeightAndGrad(float[] data, int n, int x, int z, float fx, float fz)
    {
        int i00 = z * n + x;
        int i10 = i00 + 1;
        int i01 = i00 + n;
        int i11 = i01 + 1;
        float h00 = data[i00], h10 = data[i10], h01 = data[i01], h11 = data[i11];

        float gradX = (h10 - h00) * (1f - fz) + (h11 - h01) * fz;
        float gradZ = (h01 - h00) * (1f - fx) + (h11 - h10) * fx;

        float h = h00 * (1f - fx) * (1f - fz)
                + h10 * fx        * (1f - fz)
                + h01 * (1f - fx) * fz
                + h11 * fx        * fz;
        return (h, gradX, gradZ);
    }

    private static float HeightOnly(float[] data, int n, float x, float z)
    {
        int xi = (int)x, zi = (int)z;
        float fx = x - xi, fz = z - zi;
        int i00 = zi * n + xi;
        return data[i00]         * (1f - fx) * (1f - fz)
             + data[i00 + 1]     * fx        * (1f - fz)
             + data[i00 + n]     * (1f - fx) * fz
             + data[i00 + n + 1] * fx        * fz;
    }

    private static void DepositBilinear(float[] data, int n, int x, int z, float fx, float fz, float amount)
    {
        int i00 = z * n + x;
        data[i00]         += amount * (1f - fx) * (1f - fz);
        data[i00 + 1]     += amount * fx        * (1f - fz);
        data[i00 + n]     += amount * (1f - fx) * fz;
        data[i00 + n + 1] += amount * fx        * fz;
    }

    private readonly record struct BrushOffset(int Dx, int Dz, float Weight);

    private static BrushOffset[] BuildBrushOffsets(int radius)
    {
        var list = new List<BrushOffset>();
        float total = 0f;
        for (int oz = -radius; oz <= radius; oz++)
        for (int ox = -radius; ox <= radius; ox++)
        {
            float d = MathF.Sqrt(ox * ox + oz * oz);
            if (d > radius) continue;
            float w = 1f - d / radius;
            total += w;
            list.Add(new BrushOffset(ox, oz, w));
        }
        var result = new BrushOffset[list.Count];
        for (int i = 0; i < list.Count; i++)
            result[i] = new BrushOffset(list[i].Dx, list[i].Dz, list[i].Weight / total);
        return result;
    }

    private static void EroseBrush(float[] data, int n, int x, int z, BrushOffset[] brush, float amount, ref float sediment)
    {
        // Renormalise weights for cells that fall outside the map.
        float wsum = 0f;
        for (int i = 0; i < brush.Length; i++)
        {
            int nx = x + brush[i].Dx;
            int nz = z + brush[i].Dz;
            if (nx < 0 || nx >= n || nz < 0 || nz >= n) continue;
            wsum += brush[i].Weight;
        }
        if (wsum < 1e-6f) return;

        for (int i = 0; i < brush.Length; i++)
        {
            int nx = x + brush[i].Dx;
            int nz = z + brush[i].Dz;
            if (nx < 0 || nx >= n || nz < 0 || nz >= n) continue;
            float w = brush[i].Weight / wsum;
            int idx = nz * n + nx;
            float take = MathF.Min(data[idx], amount * w);
            data[idx] -= take;
            sediment += take;
        }
    }
}
