using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Domain-warping helpers built on <see cref="Perlin.NoiseVector"/>.
///
/// <para>
/// Domain warping is the de-facto industry technique for breaking the visible
/// periodicity of lattice noise: instead of evaluating <c>f(p)</c>, the field
/// is sampled at <c>f(p + w(p))</c> where <c>w</c> is a vector-valued noise.
/// Iterating the recipe (<c>f(p + w(p + w(p)))</c>) produces the organic,
/// non-self-similar flow seen in real geological textures — Inigo Quilez's
/// "warp warp warp" trick, baked into Arnold's <c>flake</c>, Cycles' "Noise
/// Distortion" and RenderMan's <c>PxrFlow</c>.
/// </para>
///
/// <para>
/// The helpers here are pure functions of <c>(Perlin, Vector3, parameters)</c>
/// — no allocation, no internal state — so they compose freely inside
/// procedural texture <c>Value()</c> calls and stay perfectly deterministic
/// under the <see cref="Perlin.GetOrCreate"/> seed cache.
/// </para>
/// </summary>
public static class DomainWarp
{
    // Decorrelated offset constants — each warp layer samples a different
    // region of the noise field so the iterations don't fold onto themselves.
    private static readonly Vector3[] _offsets =
    {
        new(  5.2f,   1.3f,   8.7f),
        new(  1.7f,   9.2f,   3.4f),
        new( 17.1f,   5.7f,  92.3f),
    };

    /// <summary>
    /// Recursive (IQ) domain warp. Returns <c>p + amplitude · w_n</c> where
    /// <c>w_n</c> is the n-th iteration of vector-noise sampling. With
    /// <paramref name="iterations"/> = 0 the input is returned unchanged
    /// (baseline path); 1 = single warp; 2 = the canonical IQ recipe;
    /// 3 = aggressive geological flow.
    ///
    /// <para>
    /// <paramref name="scale"/> is the world-space period of the warp field:
    /// larger values produce broader, slower deformations; smaller values
    /// produce tighter swirls. <paramref name="amplitude"/> scales the final
    /// displacement in world units.
    /// </para>
    /// </summary>
    public static Vector3 Recursive(Perlin noise, Vector3 p, int iterations, float amplitude, float scale)
    {
        if (iterations <= 0 || amplitude == 0f) return p;
        float inv = scale > 0f ? 1f / scale : 1f;

        // Bootstrap: first warp samples the bare input point.
        Vector3 w = noise.NoiseVector(p * inv + _offsets[0]);

        // Each subsequent iteration samples the previously warped point.
        int extra = Math.Min(iterations - 1, _offsets.Length - 1);
        for (int i = 1; i <= extra; i++)
        {
            Vector3 q = (p + amplitude * w) * inv + _offsets[i];
            w = noise.NoiseVector(q);
        }

        return p + amplitude * w;
    }

    /// <summary>
    /// Anisotropic single-iteration warp with per-axis amplitude. Used to
    /// simulate geological shear — real folds have a dominant direction
    /// (tectonic stress axis) rather than the isotropic spherical displacement
    /// of a vanilla warp. The largest component of <paramref name="ampPerAxis"/>
    /// dictates the visual direction of the fold.
    /// </summary>
    public static Vector3 Anisotropic(Perlin noise, Vector3 p, Vector3 ampPerAxis, float scale)
    {
        if (ampPerAxis == Vector3.Zero) return p;
        float inv = scale > 0f ? 1f / scale : 1f;
        Vector3 w = noise.NoiseVector(p * inv + _offsets[2]);
        return p + w * ampPerAxis;
    }
}
