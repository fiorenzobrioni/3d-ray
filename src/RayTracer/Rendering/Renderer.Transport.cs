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

// Renderer, part: Ray categories, visibility chains and MIS weights.
public partial class Renderer
{
    // ═════════════════════════════════════════════════════════════════════════
    // CORE PATH TRACER
    // ═════════════════════════════════════════════════════════════════════════

    // ═════════════════════════════════════════════════════════════════════════
    // Multiple Importance Sampling (Veach, balance heuristic)
    //
    // Each bounce carries forward (PrevPdf, PrevIsDelta) to the next surface
    // hit or sky miss of the path loop. This encodes two cases:
    //
    //   prevIsDelta = true                 Emission is shown at full weight.
    //                                      Used for camera rays and for pure-
    //                                      delta BSDF samples (mirror / perfect
    //                                      refraction) that cannot participate
    //                                      in MIS with area lights.
    //
    //   prevIsDelta = false, pdf > 0       Proper MIS balance-heuristic weight
    //                                      on any emission. w_bsdf = pdf /
    //                                      (pdf + p_light). Used for materials
    //                                      return a non-delta sample. Emitters no
    //                                      light sampler can reach (not
    //                                      registered) keep their full weight.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps a <see cref="RayCategory"/> to the matching
    /// <see cref="HitVisibilityMask"/> bit. Used by the visibility-skip loop
    /// of <see cref="FindVisibleHit"/> and by
    /// <see cref="Geometry.ShadowRay.Transmittance"/> to translate "the ray
    /// the caller is tracing" into "the bit that, if set on a hit, means
    /// skip this surface".
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HitVisibilityMask CategoryToMask(RayCategory cat) => cat switch
    {
        RayCategory.Camera       => HitVisibilityMask.Camera,
        RayCategory.Diffuse      => HitVisibilityMask.Diffuse,
        RayCategory.Glossy       => HitVisibilityMask.Glossy,
        RayCategory.Transmission => HitVisibilityMask.Transmission,
        RayCategory.Shadow       => HitVisibilityMask.Shadow,
        _                        => HitVisibilityMask.None,
    };

    /// <summary>
    /// Picks a <see cref="RayCategory"/> for a recursive bounce given the
    /// direction it scattered into and whether the BSDF lobe was a delta
    /// (mirror / perfect refraction). Used by the indirect bounce paths to
    /// label the next ray of the path so the
    /// <see cref="HitVisibilityMask"/> skip works one bounce later.
    ///
    /// <para>Best-effort approximation: non-delta reflection rays are
    /// labelled <see cref="RayCategory.Diffuse"/> rather than parsing the
    /// BSDF lobe — Arnold's <c>diffuse</c>/<c>glossy</c> visibility split
    /// for rough specular would need lobe metadata propagated from the BSDF
    /// sampler. The 4 distinct rays (camera primary, diffuse bounce, perfect
    /// mirror reflection, transmission) all resolve correctly.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RayCategory ClassifyScatteredRay(Vector3 normal, Vector3 wo, bool isDelta)
    {
        bool transmitted = Vector3.Dot(normal, wo) < 0f;
        if (transmitted) return RayCategory.Transmission;
        return isDelta ? RayCategory.Glossy : RayCategory.Diffuse;
    }

    /// <summary>
    /// Advances the specular-chain state across a bounce. A non-delta bounce
    /// (re)starts a diffuse vertex; a delta bounce turns a diffuse vertex into
    /// a chain (specular after a diffuse hit) and keeps a pure-from-camera
    /// specular chain in <see cref="ChainNone"/> so a mirror's view of a light
    /// is never suppressed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextChainState(int state, bool isDelta, bool shadowPassable)
    {
        if (!isDelta) return ChainDiffuse;
        int s = state & ChainStateMask;
        if (s == ChainNone) return ChainNone;
        bool chainPassable = shadowPassable && (s == ChainDiffuse || (state & ChainShadowPassable) != 0);
        return ChainSpecular | (chainPassable ? ChainShadowPassable : 0);
    }

    /// <summary>
    /// Whether a sampled direction crosses the surface the way a shadow ray
    /// would also cross it (see <see cref="ChainShadowPassable"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShadowPassable(IMaterial material, in HitRecord rec, Vector3 wo)
    {
        if (Vector3.Dot(wo, rec.Normal) >= 0f) return false;   // reflection
        return _transparentShadows || material.TransmitsStraight(rec);
    }

    /// <summary>
    /// Emission reached through a specular chain after a diffuse vertex is
    /// already counted by the light sample when every surface of the chain
    /// passes shadow rays (straight transmission, or any transmission with
    /// transparent shadows).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SuppressChainEmission(int state)
        => (state & ChainStateMask) == ChainSpecular && (state & ChainShadowPassable) != 0;

    /// <summary>
    /// True when the light sample just taken reached the shading point through
    /// a transparent shadow: its BSDF counterpart is suppressed
    /// (<see cref="SuppressChainEmission"/>), so it must keep the full weight.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool LightSampleOwnsTransport() => ShadowRay.LastCrossedTransparent;

    /// <summary>
    /// MIS weight of a light sample whose own density is <paramref name="own"/>,
    /// against every light strategy that samples the same emitter in that
    /// direction (<paramref name="sum"/>, <paramref name="sumSq"/>, own included)
    /// and the BSDF or phase density <paramref name="pBsdf"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float NeeWeight(float own, float sum, float sumSq, float pBsdf)
        => _misHeuristic == MisHeuristic.Power
            ? own * own / (sumSq + pBsdf * pBsdf + 1e-30f)
            : own / (sum + pBsdf + 1e-30f);

    /// <summary>MIS weight of emission reached by a BSDF sample: the complement of <see cref="NeeWeight"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float BsdfWeight(float pBsdf, float sum, float sumSq)
        => _misHeuristic == MisHeuristic.Power
            ? pBsdf * pBsdf / (pBsdf * pBsdf + sumSq + 1e-30f)
            : pBsdf / (pBsdf + sum + 1e-30f);

    /// <summary>
    /// MIS weight for a light sample at a surface or medium vertex: 1 when no
    /// BSDF sample can deliver this light (delta lights, a directional disc,
    /// a proxy-less area light), otherwise the share of the light strategies.
    /// </summary>
    private float LightSampleWeight(ILight light, float pPick, Vector3 origin, Vector3 dir, float pBsdf)
    {
        if (pBsdf <= 0f || !_mis.PairsWithBsdf(light)) return 1f;
        var (own, sum, sumSq) = _mis.NeeDensity(light, pPick, origin, dir);
        return NeeWeight(own, sum, sumSq, pBsdf);
    }

    /// <summary>
    /// Applies the MIS weight to surface emission at the current hit — the
    /// "BSDF-sample hit a light" half of Veach's estimator. The light is the
    /// one that owns the surface hit at <paramref name="t"/>.
    /// </summary>
    private Vector3 WeightEmission(Vector3 rawEmission, IMaterial material, Ray ray, float t,
                                    float prevBsdfPdf, bool prevIsDelta)
    {
        if (prevIsDelta)
            return rawEmission;

        ILight? light = _mis.EmitterOwner(material, ray, t);
        if (light == null)
            return rawEmission;   // no next-event sampler covers it: full weight

        float pLight = _mis.Pick(light, ray.Origin) * light.PdfSolidAngle(ray.Origin, ray.Direction);
        if (prevBsdfPdf + pLight <= 1e-20f)
            return Vector3.Zero;
        return rawEmission * BsdfWeight(prevBsdfPdf, pLight, pLight * pLight);
    }
}
