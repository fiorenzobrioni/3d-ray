using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

/// <summary>
/// Describes a material's specular interface to
/// <see cref="Rendering.ManifoldWalker"/> (MNEE / SMS). A material returns a
/// caster descriptor when it presents a specular interface the manifold walk
/// can solve — smooth glass (refraction) or a polished mirror/metal
/// (reflection) for Phase-2 MNEE, or a <em>rough</em> (frosted) glass / brushed
/// metal for Phase-2b Specular Manifold Sampling. Rough casters set
/// <see cref="IsRough"/> and carry the GGX roughness (<see cref="AlphaX"/>,
/// <see cref="AlphaY"/>) so the walker can sample/evaluate the microfacet
/// distribution; the smooth path leaves them zero. Materials that are not
/// specular casters at all return <see cref="None"/> and fall back to the
/// Phase-1 transparent shadow ray.
/// </summary>
public readonly struct CausticInterface
{
    /// <summary>True when the material is a specular MNEE/SMS caster.</summary>
    public readonly bool IsCaster;

    /// <summary>
    /// True for a refractive interface (glass: light bends via Snell's law and
    /// can cross a second interface inside a solid object). False for a purely
    /// reflective interface (mirror/polished metal).
    /// </summary>
    public readonly bool IsTransmissive;

    /// <summary>
    /// True when the interface is rough (frosted glass / brushed metal):
    /// the caustic connection is found by Specular Manifold Sampling (Phase 2b)
    /// — a stochastic microfacet normal sampled from the GGX distribution below
    /// drives the manifold target instead of the bare geometric normal. False
    /// for a smooth interface (the Phase-2 MNEE delta caster).
    /// </summary>
    public readonly bool IsRough;

    /// <summary>GGX α along the tangent axis (Burley aspect-mapped). Unused when smooth.</summary>
    public readonly float AlphaX;

    /// <summary>GGX α along the bitangent axis. Unused when smooth.</summary>
    public readonly float AlphaY;

    /// <summary>Raw GGX roughness, kept for diagnostics / thresholds. Zero when smooth.</summary>
    public readonly float Roughness;

    /// <summary>
    /// Absolute index of refraction of the material (e.g. 1.5 for glass),
    /// relative to the surrounding medium taken as air (η = 1). The walker
    /// derives the directional relative η from the interface orientation
    /// (entering vs. exiting) at each vertex. Unused for reflective casters.
    /// </summary>
    public readonly float Ior;

    /// <summary>
    /// Per-interface base transmission (or reflection) tint, WITHOUT the Fresnel
    /// factor — the walker multiplies in the exact Fresnel term it computes from
    /// the converged vertex geometry. For glass this is the transmission colour
    /// (albedo); for a tinted mirror, the reflection colour.
    /// </summary>
    public readonly Vector3 Tint;

    /// <summary>
    /// Beer-Lambert absorption coefficient σ_a of the interior medium (per
    /// channel), mirroring <see cref="IMaterial.ShadowAbsorption"/>. Applied by
    /// the walker over the interior segment between the entry and exit vertices
    /// of a two-interface (solid-glass) path. Zero for thin glass / mirrors.
    /// </summary>
    public readonly Vector3 Absorption;

    /// <summary>Smooth specular caster (Phase-2 MNEE): a delta interface.</summary>
    public CausticInterface(bool isTransmissive, float ior, Vector3 tint, Vector3 absorption)
    {
        IsCaster       = true;
        IsTransmissive = isTransmissive;
        IsRough        = false;
        AlphaX         = 0f;
        AlphaY         = 0f;
        Roughness      = 0f;
        Ior            = ior;
        Tint           = tint;
        Absorption     = absorption;
    }

    /// <summary>
    /// Rough specular caster (Phase-2b Specular Manifold Sampling): a frosted
    /// glass / brushed metal interface whose microfacet distribution is sampled
    /// by the walker. <paramref name="alphaX"/>/<paramref name="alphaY"/> are the
    /// anisotropic GGX widths (pass equal values for isotropic).
    /// </summary>
    public CausticInterface(bool isTransmissive, float ior, Vector3 tint, Vector3 absorption,
                            float alphaX, float alphaY, float roughness)
    {
        IsCaster       = true;
        IsTransmissive = isTransmissive;
        IsRough        = true;
        AlphaX         = MathF.Max(alphaX, 1e-3f);
        AlphaY         = MathF.Max(alphaY, 1e-3f);
        Roughness      = roughness;
        Ior            = ior;
        Tint           = tint;
        Absorption     = absorption;
    }

    /// <summary>The "not a caster" sentinel (<see cref="IsCaster"/> == false).</summary>
    public static CausticInterface None => default;
}
