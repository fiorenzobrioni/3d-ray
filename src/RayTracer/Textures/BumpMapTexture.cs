using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Scalar bump map driven by any <see cref="ITexture"/> (procedural or image).
///
/// Returns a tangent-space normal computed from the local gradient of the
/// height field h = Rec.709 luminance of the inner texture. Caller is
/// responsible for transforming the result to world space via the surface
/// TBN (the renderer does this in <c>ApplyBumpMap</c>).
///
/// Math (Blinn 1978, central differences). Each sample perturbs BOTH the
/// UV coordinate AND the world-space point <c>p</c> along the tangent /
/// bitangent — this is essential because some <see cref="ITexture"/>
/// implementations (noise, marble, wood, voronoi) ignore <c>u,v</c> and
/// sample purely on <c>p</c>, while others (image, checker) use <c>u,v</c>.
/// Perturbing both keeps the bump consistent across the two families:
///
///   p_u± = p ± T·Δ      ;   p_v± = p ± B·Δ
///   h_u± = Luminance(tex.Value(u±Δ, v, p_u±, seed))   etc.
///   ∂h/∂u = (h_u+ − h_u−) / (2·Δ)
///   n_ts  = normalize((−strength·∂h/∂u, −strength·∂h/∂v, 1))
///
/// Central differences (4 samples) are used over forward differences (3
/// samples) to avoid the directional bias that makes bumps "lean" toward
/// +u/+v on smooth procedurals — the extra sample is negligible compared
/// to the BSDF eval that follows.
///
/// In YAML:
///   bump_map:
///     texture: { type: noise, ... }   # any ITexture
///     strength: 1.0
///     scale: 1.0
/// </summary>
public sealed class BumpMapTexture
{
    private readonly ITexture _height;
    private readonly float _strength;
    private readonly float _scale;

    // Constant footprint in UV space. Analytic ray-differential filtering of
    // the inner texture belongs to the separate texture-filtering roadmap.
    private const float Delta = 1e-3f;

    /// <param name="height">Inner height-field texture (luminance is read).</param>
    /// <param name="strength">
    ///   Perturbation amplitude. Clamped to [0, 10] — bump gradients are
    ///   unitless and may need larger amplification than normal maps on flat
    ///   procedurals. 0 disables the perturbation.
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
    /// <param name="tangent">
    ///   Surface tangent (world-space, ideally unit length). Used to perturb
    ///   <paramref name="p"/> for inner textures that sample on 3D position.
    /// </param>
    /// <param name="bitangent">Surface bitangent (world-space).</param>
    public Vector3 SampleTangentNormal(float u, float v, Vector3 p,
                                       Vector3 tangent, Vector3 bitangent,
                                       int seed)
    {
        if (_strength <= 0f) return Vector3.UnitZ;

        float su = u * _scale;
        float sv = v * _scale;

        Vector3 dpu = tangent   * Delta;
        Vector3 dpv = bitangent * Delta;

        float hUp = MathUtils.Luminance(_height.Value(su + Delta, sv, p + dpu, seed));
        float hUm = MathUtils.Luminance(_height.Value(su - Delta, sv, p - dpu, seed));
        float hVp = MathUtils.Luminance(_height.Value(su, sv + Delta, p + dpv, seed));
        float hVm = MathUtils.Luminance(_height.Value(su, sv - Delta, p - dpv, seed));

        float dhdu = (hUp - hUm) / (2f * Delta);
        float dhdv = (hVp - hVm) / (2f * Delta);

        Vector3 nTs = new(-_strength * dhdu, -_strength * dhdv, 1f);
        return Vector3.Normalize(nTs);
    }
}
