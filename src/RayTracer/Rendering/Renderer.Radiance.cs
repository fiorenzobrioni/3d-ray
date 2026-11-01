using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

// Renderer, part: Radiance sanitising and the optional clamps.
public partial class Renderer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 Clamp01(Vector3 v) => Vector3.Clamp(v, Vector3.Zero, Vector3.One);

    /// <summary>
    /// Replaces NaN/Inf components with zero and floors negatives at zero.
    /// Idempotent — applying it more than once is a no-op — so it is safe to
    /// run on every contribution to stop a non-finite value from poisoning the
    /// path's sums or the pixel accumulator,
    /// without introducing the bias a repeated *magnitude* clamp would.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 SanitizeRadiance(Vector3 color)
    {
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z))
        {
            Interlocked.Increment(ref s_nonFiniteCount);
            if (!float.IsFinite(color.X)) color.X = 0f;
            if (!float.IsFinite(color.Y)) color.Y = 0f;
            if (!float.IsFinite(color.Z)) color.Z = 0f;
        }
        return Vector3.Max(color, Vector3.Zero);
    }

    /// <summary>Murmur3 finaliser: spreads a small user seed over all 32 bits.</summary>
    private static uint MixSeed(uint x)
    {
        x ^= x >> 16; x *= 0x85ebca6bu;
        x ^= x >> 13; x *= 0xc2b2ae35u;
        x ^= x >> 16;
        return x == 0 ? 1u : x;
    }

    /// <summary>
    /// Clamps a radiance sample to suppress firefly artifacts.
    /// Also replaces NaN/Inf values with black to prevent corruption
    /// from propagating into the pixel accumulator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampRadiance(Vector3 color)
    {
        color = SanitizeRadiance(color);

        // Luminance-preserving clamp — scales the entire vector down
        // to prevent Hue-shifting heavily saturated bright highlights.
        float lum = MathUtils.Luminance(color);
        if (lum > _maxSampleRadiance)
        {
            color *= _maxSampleRadiance / lum;
        }

        return color;
    }

    /// <summary>
    /// Clamps the indirect contribution using the depth-aware secondary clamp
    /// threshold (<c>_indirectMaxSampleRadiance</c>). When
    /// <c>--indirect-clamp-factor</c> is 1.0 this matches <see cref="ClampRadiance"/>;
    /// below 1 it suppresses caustics / deep-specular fireflies more
    /// aggressively than the primary pixel clamp.
    ///
    /// <para>Applied <b>once per camera path</b>, at the primary surface
    /// (<c>depth == _maxDepth</c>), to the full post-BSDF indirect contribution
    /// <c>attenuation · L_indirect</c>. This is the camera-relative clamp the
    /// firefly suppression is meant to be — applying it at every returning
    /// recursion frame (the previous behaviour) compounded into a clamp-of-clamp
    /// that lost energy proportional to path length, and clamping the bare
    /// returned radiance instead of the throughput-weighted contribution made
    /// the effective threshold depth-dependent.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampRadianceIndirect(Vector3 color)
    {
        color = SanitizeRadiance(color);

        float lum = MathUtils.Luminance(color);
        if (lum > _indirectMaxSampleRadiance && lum > 0f)
            color *= _indirectMaxSampleRadiance / lum;

        return color;
    }
}
