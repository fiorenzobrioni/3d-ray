using System.Numerics;

namespace RayTracer.Core;

public class Perlin
{
    private const int PointCount = 256;
    private readonly Vector3[] _ranvec;
    private readonly int[] _permX;
    private readonly int[] _permY;
    private readonly int[] _permZ;

    public Perlin()
    {
        _ranvec = new Vector3[PointCount];
        for (int i = 0; i < PointCount; i++)
        {
            _ranvec[i] = Vector3.Normalize(MathUtils.RandomVector3(-1f, 1f));
        }

        _permX = GeneratePerm();
        _permY = GeneratePerm();
        _permZ = GeneratePerm();
    }

    public float Noise(Vector3 p)
    {
        float u = p.X - MathF.Floor(p.X);
        float v = p.Y - MathF.Floor(p.Y);
        float w = p.Z - MathF.Floor(p.Z);

        int i = (int)MathF.Floor(p.X);
        int j = (int)MathF.Floor(p.Y);
        int k = (int)MathF.Floor(p.Z);

        var c = new Vector3[2, 2, 2];

        for (int di = 0; di < 2; di++)
        {
            for (int dj = 0; dj < 2; dj++)
            {
                for (int dk = 0; dk < 2; dk++)
                {
                    c[di, dj, dk] = _ranvec[
                        _permX[(i + di) & 255] ^
                        _permY[(j + dj) & 255] ^
                        _permZ[(k + dk) & 255]
                    ];
                }
            }
        }

        return PerlinInterp(c, u, v, w);
    }

    public float Turbulence(Vector3 p, int depth = 7)
    {
        float accum = 0f;
        Vector3 tempP = p;
        float weight = 1f;

        for (int i = 0; i < depth; i++)
        {
            accum += weight * Noise(tempP);
            weight *= 0.5f;
            tempP *= 2f;
        }

        return MathF.Abs(accum);
    }

    private static int[] GeneratePerm()
    {
        var p = new int[PointCount];
        for (int i = 0; i < PointCount; i++)
            p[i] = i;

        Permute(p, PointCount);
        return p;
    }

    private static void Permute(int[] p, int n)
    {
        for (int i = n - 1; i > 0; i--)
        {
            int target = MathUtils.Rng.Next(0, i + 1);
            (p[i], p[target]) = (p[target], p[i]);
        }
    }

    private static float PerlinInterp(Vector3[,,] c, float u, float v, float w)
    {
        float uu = u * u * (3 - 2 * u);
        float vv = v * v * (3 - 2 * v);
        float ww = w * w * (3 - 2 * w);
        float accum = 0f;

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector3 weightV = new Vector3(u - i, v - j, w - k);
                    accum += (i * uu + (1 - i) * (1 - uu))
                           * (j * vv + (1 - j) * (1 - vv))
                           * (k * ww + (1 - k) * (1 - ww))
                           * Vector3.Dot(c[i, j, k], weightV);
                }
            }
        }

        return accum;
    }
}
