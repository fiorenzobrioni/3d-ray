using System;
using System.Numerics;

namespace TerrainGen.Heightmap;

public sealed class Heightmap2D
{
    public int N { get; }
    public float[] Data { get; }

    public Heightmap2D(int n)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n));
        N = n;
        Data = new float[n * n];
    }

    public float this[int x, int z]
    {
        get => Data[z * N + x];
        set => Data[z * N + x] = value;
    }

    public float SampleClamped(int x, int z)
    {
        if (x < 0) x = 0; else if (x >= N) x = N - 1;
        if (z < 0) z = 0; else if (z >= N) z = N - 1;
        return Data[z * N + x];
    }

    public float SampleBilinear(float x, float z)
    {
        if (x < 0) x = 0; else if (x > N - 1) x = N - 1;
        if (z < 0) z = 0; else if (z > N - 1) z = N - 1;
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        int x1 = Math.Min(x0 + 1, N - 1);
        int z1 = Math.Min(z0 + 1, N - 1);
        float fx = x - x0, fz = z - z0;
        float h00 = Data[z0 * N + x0];
        float h10 = Data[z0 * N + x1];
        float h01 = Data[z1 * N + x0];
        float h11 = Data[z1 * N + x1];
        return (1 - fx) * (1 - fz) * h00
             + fx       * (1 - fz) * h10
             + (1 - fx) * fz       * h01
             + fx       * fz       * h11;
    }

    /// <summary>Slope magnitude at (x,z) using central differences. Returns ||grad H|| in cell units.</summary>
    public float SlopeAt(int x, int z, float cellSize)
    {
        float hL = SampleClamped(x - 1, z);
        float hR = SampleClamped(x + 1, z);
        float hD = SampleClamped(x, z - 1);
        float hU = SampleClamped(x, z + 1);
        float dx = (hR - hL) / (2f * cellSize);
        float dz = (hU - hD) / (2f * cellSize);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public Vector3 NormalAt(int x, int z, float cellSize)
    {
        float hL = SampleClamped(x - 1, z);
        float hR = SampleClamped(x + 1, z);
        float hD = SampleClamped(x, z - 1);
        float hU = SampleClamped(x, z + 1);
        var n = new Vector3((hL - hR) / (2f * cellSize), 1f, (hD - hU) / (2f * cellSize));
        return Vector3.Normalize(n);
    }

    public void Normalize01()
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        for (int i = 0; i < Data.Length; i++)
        {
            float v = Data[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }
        float range = MathF.Max(max - min, 1e-9f);
        for (int i = 0; i < Data.Length; i++)
            Data[i] = (Data[i] - min) / range;
    }

    public (float min, float max, float avg) Stats()
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        double sum = 0;
        for (int i = 0; i < Data.Length; i++)
        {
            float v = Data[i];
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v;
        }
        return (min, max, (float)(sum / Data.Length));
    }
}
