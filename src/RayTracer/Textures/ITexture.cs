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
}
