using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Materials;

/// <summary>
/// Describes a material's smooth specular interface to
/// <see cref="Rendering.ManifoldWalker"/> (MNEE). A material returns a caster
/// descriptor only when it presents a near-perfect specular interface the
/// manifold walk can solve analytically — smooth glass (refraction) or a
/// polished mirror/metal (reflection). Rough/frosted interfaces return
/// <see cref="None"/> (deferred to Phase 2b — Specular Manifold Sampling) and
/// fall back to the Phase-1 transparent shadow ray.
/// </summary>
public readonly struct CausticInterface
{
    /// <summary>True when the material is a smooth specular MNEE caster.</summary>
    public readonly bool IsCaster;

    /// <summary>
    /// True for a refractive interface (glass: light bends via Snell's law and
    /// can cross a second interface inside a solid object). False for a purely
    /// reflective interface (mirror/polished metal).
    /// </summary>
    public readonly bool IsTransmissive;

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

    public CausticInterface(bool isTransmissive, float ior, Vector3 tint, Vector3 absorption)
    {
        IsCaster       = true;
        IsTransmissive = isTransmissive;
        Ior            = ior;
        Tint           = tint;
        Absorption     = absorption;
    }

    /// <summary>The "not a caster" sentinel (<see cref="IsCaster"/> == false).</summary>
    public static CausticInterface None => default;
}
