using System.Numerics;

namespace RayTracer.Textures;

public static class TextureTransform
{
    public static Vector3 Apply(Vector3 p, Vector3 offset, Vector3 rotation, int objectSeed, bool randomizeOffset, bool randomizeRotation)
    {
        Vector3 finalP = p;

        // 1. Randomizzazione basata su seed dell'oggetto
        if (objectSeed != 0)
        {
            // Usiamo il seed per generare spostamenti pseudo-casuali deterministici
            if (randomizeOffset)
            {
                float ox = Hash(objectSeed, 12.34f) * 1000f;
                float oy = Hash(objectSeed, 56.78f) * 1000f;
                float oz = Hash(objectSeed, 90.12f) * 1000f;
                finalP += new Vector3(ox, oy, oz);
            }

            if (randomizeRotation)
            {
                float rx = Hash(objectSeed, 11.11f) * 360f;
                float ry = Hash(objectSeed, 22.22f) * 360f;
                float rz = Hash(objectSeed, 33.33f) * 360f;
                finalP = Rotate(finalP, new Vector3(rx, ry, rz));
            }
        }

        // 2. Trasformazioni manuali da YAML
        finalP += offset;
        if (rotation != Vector3.Zero)
        {
            finalP = Rotate(finalP, rotation);
        }

        return finalP;
    }

    private static float Hash(int seed, float salt)
    {
        // Una semplice funzione di hashing per ottenere un valore 0-1 basato su seed
        return (MathF.Abs(MathF.Sin(seed * 12.9898f + salt * 78.233f) * 43758.5453f)) % 1f;
    }

    public static Vector3 Rotate(Vector3 p, Vector3 degrees)
    {
        float rx = degrees.X * MathF.PI / 180f;
        float ry = degrees.Y * MathF.PI / 180f;
        float rz = degrees.Z * MathF.PI / 180f;

        Vector3 v = p;

        // Rotazione X
        if (rx != 0)
        {
            float cos = MathF.Cos(rx);
            float sin = MathF.Sin(rx);
            float y = v.Y * cos - v.Z * sin;
            float z = v.Y * sin + v.Z * cos;
            v = new Vector3(v.X, y, z);
        }

        // Rotazione Y
        if (ry != 0)
        {
            float cos = MathF.Cos(ry);
            float sin = MathF.Sin(ry);
            float x = v.X * cos + v.Z * sin;
            float z = -v.X * sin + v.Z * cos;
            v = new Vector3(x, v.Y, z);
        }

        // Rotazione Z
        if (rz != 0)
        {
            float cos = MathF.Cos(rz);
            float sin = MathF.Sin(rz);
            float x = v.X * cos - v.Y * sin;
            float y = v.X * sin + v.Y * cos;
            v = new Vector3(x, y, v.Z);
        }

        return v;
    }
}
