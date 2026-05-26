using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// Refractive medium transition signalled by a BSDF sample to drive the
/// renderer's <see cref="Volumetrics.MediumStack"/>. <see cref="None"/> on
/// reflection lobes and on transmissions through geometry that has no medium
/// binding; <see cref="Enter"/> when the sample enters the geometry's
/// interior medium (front-face refraction); <see cref="Exit"/> when it exits
/// (back-face refraction).
/// </summary>
public enum MediumTransition
{
    None,
    Enter,
    Exit,
}

/// <summary>
/// The result of a BSDF direction-sample — a sampled outgoing direction
/// together with the BRDF value and the solid-angle PDF at that direction.
///
/// Conventions (PBRT-v4 / Mitsuba, matching the naming used in DisneyBsdf):
///   - V is the view direction (surface → camera), passed to the sampler.
///   - Wo is the sampled direction (surface → next bounce / light).
///   - F is the BRDF value f(V, Wo) WITHOUT the N·Wo cosine term. Callers
///     that integrate radiance multiply by max(N·Wo, 0) themselves — keeping
///     F cosine-free lets the same value be used in MIS where the cosine
///     appears in the light-transport integrand, not in the BRDF.
///   - Pdf is the PDF of sampling Wo from the sampler's distribution in solid
///     angle around the shading normal.
///   - IsDelta marks a Dirac-delta lobe (perfect mirror / perfect refraction).
///     For delta lobes Pdf is set to 1 and MIS treats the sample with an
///     implicit weight of 1 (they cannot be reached by any other sampler).
///   - NextSegmentAbsorption carries the per-channel Beer-Lambert coefficient
///     σ_a that the renderer should apply to the NEXT ray segment (from this
///     hit to the next hit). A non-null value instructs the renderer to
///     switch the interior medium — it's written when the sample is a
///     refraction: entering glass (FrontFace=true) picks up the material's
///     σ_a, exiting (FrontFace=false) restores vacuum (Vector3.Zero). A null
///     value means "unchanged" — the renderer keeps whatever interior medium
///     the ray was traversing before this hit. Non-transmissive samples
///     (reflection lobes) always return null. <em>Legacy back-compat field —
///     superseded by <see cref="Transition"/> + the entity-level
///     <c>interior_medium</c> binding in Phase 2 of the MediumInterface
///     rollout. Refraction lobes still emit it so scenes without explicit
///     medium bindings keep working unchanged.</em>
///   - Transition describes the medium-stack effect of this sample (None /
///     Enter / Exit). The renderer reads <see cref="Core.HitRecord.MediumIface"/>
///     at the same hit to know *which* medium to push or pop.
/// </summary>
public readonly struct BsdfSample
{
    public readonly Vector3  Wo;
    public readonly Vector3  F;
    public readonly float    Pdf;
    public readonly bool     IsDelta;
    public readonly Vector3? NextSegmentAbsorption;
    public readonly MediumTransition Transition;

    public BsdfSample(Vector3 wo, Vector3 f, float pdf, bool isDelta = false,
                      Vector3? nextSegmentAbsorption = null,
                      MediumTransition transition = MediumTransition.None)
    {
        Wo = wo;
        F = f;
        Pdf = pdf;
        IsDelta = isDelta;
        NextSegmentAbsorption = nextSegmentAbsorption;
        Transition = transition;
    }
}
