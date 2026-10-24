using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Volumetrics;

/// <summary>
/// A phase function: the angular distribution of light scattered at a point
/// of a medium. <c>wo</c> is the direction the ray travels INTO the event
/// (<c>ray.Direction</c>), <c>wi</c> the scattered direction, so forward
/// scattering peaks at <c>wi = wo</c>. Every phase function here samples its
/// own value exactly: the pdf of <see cref="Sample(Vector3, Vector2)"/> is
/// <see cref="Evaluate"/>, and its weight is 1.
/// </summary>
public interface IPhaseFunction
{
    float Evaluate(Vector3 wo, Vector3 wi);

    /// <summary>Warps the stratified pair <paramref name="u"/> to a direction.</summary>
    (Vector3 Wi, float Pdf) Sample(Vector3 wo, Vector2 u);

    /// <summary>Samples with the vertex's next 2D pair.</summary>
    (Vector3 Wi, float Pdf) Sample(Vector3 wo)
    {
        var (u1, u2) = MathUtils.Random2D();
        return Sample(wo, new Vector2(u1, u2));
    }

    float Pdf(Vector3 wo, Vector3 wi) => Evaluate(wo, wi);
}

internal static class PhaseFrame
{
    /// <summary>The direction at cos θ, φ around <paramref name="w"/>.</summary>
    public static Vector3 Around(Vector3 w, float cosTheta, float phi)
    {
        cosTheta = Math.Clamp(cosTheta, -1f, 1f);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        MathUtils.OrthonormalBasis(w, out Vector3 t, out Vector3 b);
        return Vector3.Normalize(sinTheta * MathF.Cos(phi) * t + sinTheta * MathF.Sin(phi) * b + cosTheta * w);
    }
}
