using System.Numerics;

namespace RayTracer.Core;

public static class MathUtils
{
    public static Random Rng => Random.Shared;

    public const float Epsilon = 1e-4f;
    public const float Infinity = float.MaxValue;
    public const float Pi = MathF.PI;

    public static float DegreesToRadians(float degrees) => degrees * Pi / 180f;

    public static float RandomFloat() => (float)Rng.NextDouble();
    public static float RandomFloat(float min, float max) => min + (max - min) * RandomFloat();

    public static Vector3 RandomVector3() =>
        new(RandomFloat(), RandomFloat(), RandomFloat());

    public static Vector3 RandomVector3(float min, float max) =>
        new(RandomFloat(min, max), RandomFloat(min, max), RandomFloat(min, max));

    public static Vector3 RandomInUnitSphere()
    {
        while (true)
        {
            var p = RandomVector3(-1f, 1f);
            if (p.LengthSquared() < 1f)
                return p;
        }
    }

    public static Vector3 RandomUnitVector() => Vector3.Normalize(RandomInUnitSphere());

    public static Vector3 RandomInHemisphere(Vector3 normal)
    {
        var inUnitSphere = RandomInUnitSphere();
        return Vector3.Dot(inUnitSphere, normal) > 0 ? inUnitSphere : -inUnitSphere;
    }

    public static Vector3 RandomInUnitDisk()
    {
        while (true)
        {
            var p = new Vector3(RandomFloat(-1f, 1f), RandomFloat(-1f, 1f), 0f);
            if (p.LengthSquared() < 1f)
                return p;
        }
    }

    public static Vector3 Reflect(Vector3 v, Vector3 n) =>
        v - 2f * Vector3.Dot(v, n) * n;

    public static Vector3 Refract(Vector3 uv, Vector3 n, float etaiOverEtat)
    {
        float cosTheta = MathF.Min(Vector3.Dot(-uv, n), 1f);
        Vector3 rOutPerp = etaiOverEtat * (uv + cosTheta * n);
        Vector3 rOutParallel = -MathF.Sqrt(MathF.Abs(1f - rOutPerp.LengthSquared())) * n;
        return rOutPerp + rOutParallel;
    }

    public static float Schlick(float cosine, float refractionIndex)
    {
        float r0 = (1f - refractionIndex) / (1f + refractionIndex);
        r0 *= r0;
        return r0 + (1f - r0) * MathF.Pow(1f - cosine, 5f);
    }

    public static bool NearZero(Vector3 v)
    {
        const float s = 1e-8f;
        return MathF.Abs(v.X) < s && MathF.Abs(v.Y) < s && MathF.Abs(v.Z) < s;
    }
}
