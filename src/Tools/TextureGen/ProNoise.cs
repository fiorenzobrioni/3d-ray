using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tools;

/// <summary>
/// A collection of pro-grade procedural noise algorithms for photorealistic textures.
/// </summary>
public static class ProNoise
{
    private static readonly int[] P;
    private static readonly int[] Perm;

    static ProNoise()
    {
        var rng = new Random(42);
        P = new int[256];
        for (int i = 0; i < 256; i++) P[i] = i;
        
        // Shuffle
        for (int i = 0; i < 256; i++)
        {
            int r = rng.Next(256);
            (P[i], P[r]) = (P[r], P[i]);
        }
        
        Perm = new int[512];
        for (int i = 0; i < 512; i++)
        {
            Perm[i] = P[i & 255];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FastFloor(float x) => x > 0 ? (int)x : (int)x - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Dot(int[] g, float x, float y) => g[0] * x + g[1] * y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Dot(int[] g, float x, float y, float z) => g[0] * x + g[1] * y + g[2] * z;

    private static readonly int[][] Grad3 = {
        new[]{1,1,0}, new[]{-1,1,0}, new[]{1,-1,0}, new[]{-1,-1,0},
        new[]{1,0,1}, new[]{-1,0,1}, new[]{1,0,-1}, new[]{-1,0,-1},
        new[]{0,1,1}, new[]{0,-1,1}, new[]{0,1,-1}, new[]{0,-1,-1}
    };

    /// <summary>
    /// Simplex Noise 2D. Returns a value in the range [-1.0, 1.0].
    /// </summary>
    public static float Simplex(float x, float y)
    {
        float F2 = 0.5f * (MathF.Sqrt(3.0f) - 1.0f);
        float G2 = (3.0f - MathF.Sqrt(3.0f)) / 6.0f;

        float s = (x + y) * F2;
        int i = (int)FastFloor(x + s);
        int j = (int)FastFloor(y + s);

        float t = (i + j) * G2;
        float X0 = i - t;
        float Y0 = j - t;
        float x0 = x - X0;
        float y0 = y - Y0;

        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; }
        else { i1 = 0; j1 = 1; }

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1.0f + 2.0f * G2;
        float y2 = y0 - 1.0f + 2.0f * G2;

        int ii = i & 255;
        int jj = j & 255;
        int gi0 = Perm[ii + Perm[jj]] % 12;
        int gi1 = Perm[ii + i1 + Perm[jj + j1]] % 12;
        int gi2 = Perm[ii + 1 + Perm[jj + 1]] % 12;

        float n0, n1, n2;

        float t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 < 0) n0 = 0.0f;
        else
        {
            t0 *= t0;
            n0 = t0 * t0 * Dot(Grad3[gi0], x0, y0);
        }

        float t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 < 0) n1 = 0.0f;
        else
        {
            t1 *= t1;
            n1 = t1 * t1 * Dot(Grad3[gi1], x1, y1);
        }

        float t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 < 0) n2 = 0.0f;
        else
        {
            t2 *= t2;
            n2 = t2 * t2 * Dot(Grad3[gi2], x2, y2);
        }

        return 70.0f * (n0 + n1 + n2);
    }

    /// <summary>
    /// Fractal Brownian Motion based on Simplex Noise.
    /// Returns value roughly in [-1.0, 1.0].
    /// </summary>
    public static float Fbm(float x, float y, int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        float total = 0;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float maxVal = 0;

        for (int i = 0; i < octaves; i++)
        {
            total += Simplex(x * frequency, y * frequency) * amplitude;
            maxVal += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return total / maxVal;
    }

    /// <summary>
    /// Domain Warping: uses Fbm to distort the coordinates before feeding them into another Fbm.
    /// Great for marble, wood, and organic textures.
    /// </summary>
    public static float DomainWarp(float x, float y, float warpScale, int octaves)
    {
        float dx = Fbm(x + 5.3f, y + 1.1f, octaves);
        float dy = Fbm(x - 2.8f, y + 8.4f, octaves);
        return Fbm(x + dx * warpScale, y + dy * warpScale, octaves);
    }

    /// <summary>
    /// Ridged Multi-Fractal Noise. Great for mountains, cracks, rough surfaces.
    /// </summary>
    public static float RidgedFbm(float x, float y, int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        float total = 0;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float maxVal = 0;
        float weight = 1.0f;

        for (int i = 0; i < octaves; i++)
        {
            float v = MathF.Abs(Simplex(x * frequency, y * frequency));
            v = 1.0f - v; // Ridge
            v *= v;       // Sharpen
            v *= weight;  // Weight by previous octave
            weight = Math.Clamp(v * 2.0f, 0.0f, 1.0f);
            
            total += v * amplitude;
            maxVal += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        // Normalize roughly to [0, 1]
        return Math.Clamp(total / maxVal * 1.5f, 0f, 1f);
    }

    /// <summary>
    /// Worley/Voronoi Noise. Returns the distance to the closest feature point (d1) and the second closest (d2).
    /// Distances are Euclidean.
    /// </summary>
    public static void Voronoi(float x, float y, float jitter, out float d1, out float d2)
    {
        int ix = (int)FastFloor(x);
        int iy = (int)FastFloor(y);
        float fx = x - ix;
        float fy = y - iy;

        d1 = 1000.0f;
        d2 = 1000.0f;

        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                int vx = ix + i;
                int vy = iy + j;
                
                // Deterministic pseudo-random point for this cell
                int hash = (vx * 73856093 ^ vy * 19349663) & 0x7FFFFFFF;
                float px = (hash % 1000) / 1000.0f;
                float py = ((hash / 1000) % 1000) / 1000.0f;

                // Position relative to the cell
                float pointX = i + px * jitter;
                float pointY = j + py * jitter;

                // Distance squared
                float dx = pointX - fx;
                float dy = pointY - fy;
                float distSq = dx * dx + dy * dy;

                if (distSq < d1)
                {
                    d2 = d1;
                    d1 = distSq;
                }
                else if (distSq < d2)
                {
                    d2 = distSq;
                }
            }
        }

        d1 = MathF.Sqrt(d1);
        d2 = MathF.Sqrt(d2);
    }

    /// <summary>
    /// Cell Noise. Returns the pseudo-random value associated with the closest Voronoi cell.
    /// Useful for coloring distinct cells (like tiles or stones).
    /// </summary>
    public static float VoronoiCell(float x, float y, float jitter = 1.0f)
    {
        int ix = (int)FastFloor(x);
        int iy = (int)FastFloor(y);
        float fx = x - ix;
        float fy = y - iy;

        float minDistSq = 1000.0f;
        float closestCellVal = 0;

        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                int vx = ix + i;
                int vy = iy + j;
                
                int hash = (vx * 73856093 ^ vy * 19349663) & 0x7FFFFFFF;
                float px = (hash % 1000) / 1000.0f;
                float py = ((hash / 1000) % 1000) / 1000.0f;
                float cellVal = ((hash / 100000) % 1000) / 1000.0f;

                float pointX = i + px * jitter;
                float pointY = j + py * jitter;

                float dx = pointX - fx;
                float dy = pointY - fy;
                float distSq = dx * dx + dy * dy;

                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closestCellVal = cellVal;
                }
            }
        }

        return closestCellVal;
    }

    /// <summary>
    /// Value noise [0, 1] based on a hash.
    /// </summary>
    public static float Hash(int x, int y, int seed)
        => ((x * 73856093 ^ y * 19349663 ^ seed * 83492791) & 0x7FFFFFFF) / (float)0x7FFFFFFF;
}
