using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Lights;

/// <summary>
/// What a bidirectional integrator needs to know about the scene when it
/// starts a path on a light: the bounding sphere, through whose cross-section
/// a light at infinity (sun, sky, directional) sends its rays.
/// </summary>
public readonly record struct EmissionContext(Vector3 SceneCenter, float SceneRadius);

/// <summary>
/// A ray leaving a light (PIANO-REVIEW.md Phase 8b): where, which way, the
/// emitted radiance along it (radiant intensity for a point source, the
/// radiance for a surface or the sky), and the densities it was drawn with.
/// <para><see cref="PdfPos"/> is per unit area (on the light, or on the disc of
/// the scene's cross-section for a light at infinity; 1 for a point source),
/// <see cref="PdfDir"/> per unit solid angle (1 for a single direction).
/// <see cref="Cos"/> is the cosine between the direction and the surface
/// normal (1 without a surface). The light subpath starts with throughput
/// <c>Radiance · Cos / (PdfPos · PdfDir)</c>.</para>
/// </summary>
public struct LightEmission
{
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Normal;
    public Vector3 Radiance;
    public float PdfPos;
    public float PdfDir;
    public float Cos;
    /// <summary>A single point (point, spot): no ray can hit it.</summary>
    public bool DeltaPosition;
    /// <summary>A single direction (directional without a disc).</summary>
    public bool DeltaDirection;
    /// <summary>At infinity: the position lies on the scene's cross-section, not on the light.</summary>
    public bool Infinite;
}

internal static class EmissionMath
{
    /// <summary>Cosine-weighted direction around <paramref name="n"/> (density cos θ / π).</summary>
    public static Vector3 CosineHemisphere(Vector3 n, Vector2 u, out float cos)
    {
        float r = MathF.Sqrt(u.X);
        float phi = 2f * MathF.PI * u.Y;
        cos = MathF.Sqrt(MathF.Max(0f, 1f - u.X));
        return Vector3.Normalize(MathUtils.LocalToWorld(new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), cos), n));
    }

    /// <summary>Uniform direction inside the cone of half-angle acos(<paramref name="cosMax"/>) around <paramref name="axis"/>.</summary>
    public static Vector3 UniformCone(Vector3 axis, float cosMax, Vector2 u)
    {
        float cos = 1f - u.X * (1f - cosMax);
        float sin = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
        float phi = 2f * MathF.PI * u.Y;
        return Vector3.Normalize(MathUtils.LocalToWorld(new Vector3(sin * MathF.Cos(phi), sin * MathF.Sin(phi), cos), axis));
    }

    public static Vector3 UniformSphere(Vector2 u)
    {
        float z = 1f - 2f * u.X;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        float phi = 2f * MathF.PI * u.Y;
        return new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
    }

    /// <summary>
    /// A ray of a light at infinity arriving from <paramref name="dirToLight"/>:
    /// a uniform point of the scene's cross-section disc, pushed out to the
    /// bounding sphere, travelling toward the scene. Density 1 / (π R²).
    /// </summary>
    public static (Vector3 Origin, float PdfPos) InfiniteOrigin(in EmissionContext ctx, Vector3 dirToLight, Vector2 u)
    {
        float r = MathF.Sqrt(u.X) * ctx.SceneRadius;
        float phi = 2f * MathF.PI * u.Y;
        Vector3 offset = MathUtils.LocalToWorld(new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), 0f), dirToLight);
        return (ctx.SceneCenter + dirToLight * ctx.SceneRadius + offset, InfiniteDiscPdf(ctx));
    }

    public static float InfiniteDiscPdf(in EmissionContext ctx) => 1f / (MathF.PI * ctx.SceneRadius * ctx.SceneRadius);
}
