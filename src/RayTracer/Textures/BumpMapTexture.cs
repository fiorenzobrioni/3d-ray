using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Scalar bump map driven by any <see cref="ITexture"/> (procedural or image).
///
/// Returns a tangent-space normal computed from the slope of the height field
/// <c>h = strength · luminance(texture)</c>, a height in scene units: the
/// same surface a displacement of that height would make, so a bump and a
/// displacement of one texture agree (PIANO-REVIEW.md Phase 4).
///
/// Math (Blinn 1978, central differences). The slope is taken by stepping a
/// short length δ along the shading tangent and bitangent. A step moves both
/// inputs a texture may read, through <see cref="BumpFrame"/>: (u, v) by the
/// surface's own UV rate (∂P/∂u, ∂P/∂v), and the 3D point by the step itself
/// in the point's space. Image and checker textures read the first, noise,
/// marble, wood and voronoi the second; either way the slope is per unit of
/// length, whatever the object's size, scale or rotation:
///
///   h_T± = Luminance(tex.Value(uv ± δ·UvT, p ± δ·PointT))   (same along B)
///   ∂h/∂T = (h_T+ − h_T−) / (2·δ)
///   n_ts  = normalize((−strength·∂h/∂T, −strength·∂h/∂B, 1))
///
/// Central differences (4 samples) are used over forward differences (3
/// samples) to avoid the directional bias that makes bumps "lean" toward
/// +u/+v on smooth procedurals; the extra sample is negligible compared
/// to the BSDF eval that follows.
///
/// In YAML:
///   bump_map:
///     texture: { type: noise, ... }   # any ITexture
///     strength: 1.0                    # height of a texture value of 1, in scene units
///     scale: 1.0
/// </summary>
public sealed class BumpMapTexture : IBumpMap
{
    private readonly ITexture _height;
    private readonly float _strength;
    private readonly float _scale;

    // Step length for the central differences, in scene units, shortened on
    // a surface whose UVs run fast (a small object) so the step in UV stays
    // under Delta too.
    private const float Delta = 1e-3f;

    // Maximum tangent of the tilt the perturbed normal may take off the surface
    // normal (tan ≈ 6 → ~80.5°). Bounds the worst-case slope from steep height
    // fields so the bump stays a conditioned micro-relief rather than aliasing
    // onto the tangent plane.
    private const float MaxSlope = 6f;

    /// <param name="height">Inner height-field texture (luminance is read).</param>
    /// <param name="strength">
    ///   Height of a texture value of 1, in scene units. Clamped to [0, 10].
    ///   0 disables the perturbation.
    /// </param>
    /// <param name="scale">
    ///   Uniform UV multiplier stacked on top of any per-texture uv_scale.
    ///   Must be positive; non-positive values are coerced to 1.
    /// </param>
    public BumpMapTexture(ITexture height, float strength = 1f, float scale = 1f)
    {
        _height   = height;
        _strength = Math.Clamp(strength, 0f, 10f);
        _scale    = scale > 0f ? scale : 1f;
    }

    /// <summary>
    /// Samples the bump field and returns a tangent-space normal.
    /// <c>(0, 0, 1)</c> means "no perturbation". The result is normalised
    /// and ready to be transformed by the surface TBN.
    /// </summary>
    public Vector3 SampleTangentNormal(float u, float v, Vector3 p, in BumpFrame frame, int seed)
    {
        if (_strength <= 0f) return Vector3.UnitZ;

        Vector2 uv = new(u * _scale, v * _scale);
        Vector2 uvT = frame.UvT * _scale;
        Vector2 uvB = frame.UvB * _scale;
        float uvRate = MathF.Max(MathF.Max(MathF.Abs(uvT.X), MathF.Abs(uvT.Y)),
                                 MathF.Max(MathF.Abs(uvB.X), MathF.Abs(uvB.Y)));
        float d = uvRate > 1f ? Delta / uvRate : Delta;

        float H(Vector2 uvs, Vector3 ps) => MathUtils.Luminance(_height.Value(uvs.X, uvs.Y, ps, seed));

        float dhdt = (H(uv + d * uvT, p + d * frame.PointT) - H(uv - d * uvT, p - d * frame.PointT)) / (2f * d);
        float dhdb = (H(uv + d * uvB, p + d * frame.PointB) - H(uv - d * uvB, p - d * frame.PointB)) / (2f * d);

        Vector3 nTs = new(-_strength * dhdt, -_strength * dhdb, 1f);
        float slope = MathF.Sqrt(nTs.X * nTs.X + nTs.Y * nTs.Y);
        if (slope > MaxSlope)
        {
            float k = MaxSlope / slope;
            nTs.X *= k;
            nTs.Y *= k;
        }
        return Vector3.Normalize(nTs);
    }
}
