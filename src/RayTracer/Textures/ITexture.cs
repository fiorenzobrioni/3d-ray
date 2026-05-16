using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

public interface ITexture
{
    /// <summary>
    /// Point-sampled lookup. The renderer falls back to this for every code
    /// path that doesn't carry ray differentials (shadow rays, NEE evaluation,
    /// BSDF bounces) and for textures that haven't implemented analytic
    /// pre-filtering.
    /// </summary>
    Vector3 Value(float u, float v, Vector3 p, int objectSeed);

    /// <summary>
    /// Footprint-aware lookup (DEVLOG "Texturing VFX production-grade" step 1).
    /// When <paramref name="footprint"/> is valid, filtered textures
    /// (NoiseTexture/Worley/ImageTexture) integrate over the footprint area
    /// analytically to suppress moiré and shimmer at oblique viewing angles
    /// and 4K+ resolutions. The default implementation forwards to the
    /// point-sampled overload, preserving full backward compatibility for
    /// every <see cref="ITexture"/> that doesn't override it.
    /// </summary>
    Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint)
        => Value(u, v, p, objectSeed);

    /// <summary>
    /// HitRecord-aware lookup (DEVLOG "Texturing VFX production-grade" step 7).
    /// Gives the texture access to the full shading point — including
    /// <c>rec.Point</c> (world space) and <c>rec.LocalPoint</c> (object
    /// space) — so coordinate-system textures (Cycles' "Texture Coordinate"
    /// node, RenderMan <c>Pref</c>/<c>Pworld</c>, Arnold <c>utility</c>
    /// node) can return the correct space. Every other texture continues
    /// to receive <c>rec.LocalPoint</c> as the procedural sample point via
    /// the default forwarding, so the behaviour of <c>NoiseTexture</c>,
    /// <c>VoronoiTexture</c>, <c>ImageTexture</c> &amp; co. is byte-identical
    /// regardless of which overload the material chooses to call.
    /// </summary>
    Vector3 Value(in HitRecord rec)
        => Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed, rec.Footprint);
}
