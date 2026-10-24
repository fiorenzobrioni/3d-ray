using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public interface IMaterial
{
    // ─────────────────────────────────────────────────────────────────────────
    // Direct-lighting control flags.
    //
    // Two coarse booleans replace the old Blinn-Phong (DiffuseWeight /
    // SpecularExponent / SpecularStrength) triple that the renderer used to
    // read. Both answer yes/no questions the renderer asks once per hit; they
    // are NOT consumed by any BSDF math any more.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// True when the renderer should run next-event estimation on this hit
    /// (sample every light, weigh it by <see cref="Evaluate"/>). False means
    /// NEE is a waste of shadow rays — used by <see cref="Emissive"/>, which
    /// is the light source itself and absorbs no incoming illumination.
    /// </summary>
    bool NeedsDirectLighting => true;

    /// <summary>
    /// True when every lobe of the material is a delta function (perfect
    /// mirror, ideal refraction): no light sample can reach it, so the renderer
    /// skips the caustic photon gather there. Each sample still reports its own
    /// deltaness through <see cref="BsdfSample.IsDelta"/>, which is what drives
    /// the emission weight of the next hit.
    /// </summary>
    bool IsDeltaScatter => false;

    /// <summary>
    /// Per-channel transmission factor for a "transparent shadow ray" travelling
    /// in direction <paramref name="wi"/> across this surface. Returned by
    /// dielectric/transmissive materials so that NEE can soften the otherwise
    /// binary occlusion test: glass casts a Fresnel-tinted shadow instead of a
    /// fully black one. Default is <see cref="Vector3.Zero"/> (opaque).
    ///
    /// The shadow ray is NOT refracted — it continues in a straight line and
    /// the returned factor is multiplied into the throughput. This is the
    /// standard "transparent shadow rays" approximation: it correctly handles
    /// the loss of direct light through glass (no more hard shadow under glass)
    /// but does NOT reproduce focused refractive lighting effects, which would
    /// require photon mapping (see DEVLOG roadmap).
    /// </summary>
    /// <param name="wi">Unit shadow-ray direction at the hit (surface → light).</param>
    /// <param name="rec">Hit record at the shadow-ray intersection.</param>
    Vector3 ShadowTransmittance(Vector3 wi, in HitRecord rec) => Vector3.Zero;

    /// <summary>
    /// True when light crosses this surface without changing direction: an
    /// index-matched (IOR 1) interface such as the transparent shell that bounds
    /// a fog or water volume, or a smooth thin-walled sheet. A shadow ray
    /// through it is then the exact transport, not the straight-through
    /// approximation, so the renderer lets shadow rays cross it in every mode
    /// (and drops the duplicate BSDF path through it, PIANO-REVIEW.md §3.21).
    /// </summary>
    bool TransmitsStraight(in HitRecord rec) => false;

    /// <summary>
    /// True when the BSDF is non-zero for light arriving from behind the
    /// surface through a non-delta lobe (rough glass, diffuse transmission), so
    /// direct lighting must also sample lights on the far side
    /// (PIANO-REVIEW.md §3.36).
    /// </summary>
    bool HasTransmission(in HitRecord rec) => false;

    /// <summary>
    /// Whether path guiding may draw this surface's bounces (PIANO-REVIEW.md
    /// Phase 7b): a broad reflection with no non-delta transmission. A sharp
    /// lobe is left to its BSDF: a guided direction misses it, and halving
    /// the BSDF's density would hand the MIS weight of its highlights to the
    /// light samples, the noisy strategy there (Veach's plates: 15× the
    /// error).
    /// </summary>
    bool Guidable(in HitRecord rec) => !IsDeltaScatter && !HasTransmission(in rec);

    /// <summary>GGX α (roughness²) below which a lobe counts as too sharp to guide.</summary>
    const float GuidingMinAlpha = 0.09f;

    /// <summary>
    /// Approximate surface reflectance at the hit point, consumed ONLY by the
    /// AOV/denoiser albedo guide buffer — never by light transport. The value
    /// should be the material's dominant colour (base colour / albedo texture)
    /// in roughly [0,1]; the renderer clamps it defensively. The default
    /// (white) suits colourless materials such as clear dielectrics, whose
    /// albedo guide comes from the surfaces seen through them instead.
    /// </summary>
    Vector3 AovAlbedo(in HitRecord rec) => Vector3.One;

    /// <summary>
    /// Per-channel volumetric absorption coefficient σ_a (Beer-Lambert) of the
    /// medium bounded by this surface, used by the transparent shadow walker
    /// to attenuate <c>exp(−σ_a · d)</c> along the interior segment between a
    /// front-face hit (entry) and the next back-face hit (exit). Default
    /// <see cref="Vector3.Zero"/> means no volumetric absorption — the surface
    /// is either opaque or thin-glass (only per-hit
    /// <see cref="ShadowTransmittance"/> applies).
    ///
    /// For Disney's <c>transmission_color</c> + <c>transmission_depth</c>
    /// parameterisation, this returns <c>−ln(transmission_color) /
    /// transmission_depth</c> per channel.
    /// </summary>
    Vector3 ShadowAbsorption(in HitRecord rec) => Vector3.Zero;

    /// <summary>
    /// True when this surface transmits light through a <b>smooth specular</b>
    /// (delta) interface — clear glass, water, a polished gem. This is exactly the
    /// transport the caustic photon map reconstructs (<c>L S+ D</c>), so when
    /// photon caustics are active the renderer makes such an interface opaque to
    /// straight NEE shadow rays: the focused, refracted (and Beer-Lambert tinted)
    /// light is supplied by the photon map instead, avoiding a double count and
    /// reproducing physically correct — coloured — refractive shadows.
    ///
    /// <para>Rough/frosted transmission returns <c>false</c>: the photon map does
    /// not capture it, so the soft straight-through transparent shadow stays its
    /// only fill. Default <c>false</c> (opaque or non-transmissive).</para>
    /// </summary>
    bool IsSpecularTransmissive(in HitRecord rec) => false;

    /// <summary>
    /// Reports the absolute index of refraction of this material's refractive
    /// interface, for the per-ray <see cref="Volumetrics.IorStack"/> the renderer
    /// pushes on a transmissive entry and pops on exit. Returns <c>false</c> when
    /// the material is not a refractive dielectric interface — the ray is then
    /// treated as staying in the same medium (no stack change, no relative-IOR
    /// tracking). The default keeps every opaque material out of the stack.
    ///
    /// Thin-walled transmissives return <c>false</c>: they model a zero-thickness
    /// sheet with no interior, so they never enter/exit a nested medium.
    /// </summary>
    bool TryGetDielectricIor(in HitRecord rec, out float ior)
    {
        ior = 1f;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Symmetric BSDF interface (BRDF value, PDF, sampling).
    //
    // The one description of the material: direct lighting weighs each light
    // sample by Evaluate · |N·L|, the indirect bounce samples with Sample, and
    // MIS combines the two with Pdf, so both strategies estimate the same
    // integrand by construction (PIANO-REVIEW.md §3.12). Evaluate returns the
    // BSDF WITHOUT the cosine term, so reciprocity reads f(V, L) = f(L, V).
    //
    // Default implementations return zero / no-sample — materials that want
    // to participate in MIS and the BSDF test suite must override.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates the BRDF f(V, L) at the hit point for the given view direction
    /// V (surface → camera) and outgoing direction L (surface → next bounce).
    /// Returns the BRDF value WITHOUT the N·L cosine — callers multiply by
    /// max(N·L, 0) themselves. Returns zero for directions below the surface.
    /// </summary>
    Vector3 Evaluate(Vector3 V, Vector3 L, in HitRecord rec) => Vector3.Zero;

    /// <summary>
    /// Solid-angle PDF of sampling the outgoing direction L given the view
    /// direction V from this material's importance-sampler. Non-zero only in
    /// the hemisphere(s) the sampler actually covers. Delta lobes return zero
    /// here — they must be sampled via <see cref="Sample"/> and tagged
    /// via <see cref="BsdfSample.IsDelta"/>.
    /// </summary>
    float Pdf(Vector3 V, Vector3 L, in HitRecord rec) => 0f;

    /// <summary>
    /// Samples an outgoing direction from this material's importance distribution.
    /// Returns null when sampling fails (fully absorbed, below-surface reflection,
    /// degenerate tangent frame, etc.). The returned <see cref="BsdfSample.F"/>
    /// matches <see cref="Evaluate"/>; <see cref="BsdfSample.Pdf"/> matches
    /// <see cref="Pdf"/> in solid angle.
    /// </summary>
    BsdfSample? Sample(Vector3 V, in HitRecord rec) => null;

    // ─────────────────────────────────────────────────────────────────────────
    // Emission.
    //
    // By default materials emit nothing. The Emissive material overrides this
    // to return color * intensity, making the surface a visible light source.
    // The emitted radiance is added to the path tracer's result BEFORE the
    // scatter/indirect term, so emissive objects are self-luminous and can
    // illuminate nearby surfaces through indirect bounces.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the emitted radiance at the given surface point.
    /// Non-emissive materials return black (zero emission).
    /// </summary>
    /// <param name="u">Texture U coordinate at the hit point.</param>
    /// <param name="v">Texture V coordinate at the hit point.</param>
    /// <param name="point">World-space hit point (for 3D procedural textures).</param>
    /// <param name="objectSeed">Per-object seed for texture randomisation.</param>
    /// <param name="frontFace">True if the ray hit the front face of the surface.</param>
    Vector3 Emit(float u, float v, Vector3 point, int objectSeed, bool frontFace)
        => Vector3.Zero;

    // ─────────────────────────────────────────────────────────────────────────
    // Normal Mapping.
    //
    // When non-null, the Renderer perturbs rec.Normal using the normal map
    // BEFORE any material logic (scatter, direct lighting, emission).
    // The normal map is sampled at the hit point's UV coordinates, the result
    // is transformed from tangent space to world space via the TBN matrix
    // (built from rec.Tangent, rec.Bitangent, rec.Normal), and the
    // perturbed normal replaces rec.Normal.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional normal map for surface detail. When set, the surface normal
    /// is perturbed before any shading computation, adding visual detail
    /// (bumps, grooves, relief) without additional geometry.
    /// </summary>
    NormalMapTexture? NormalMap => null;

    // ─────────────────────────────────────────────────────────────────────────
    // Bump Mapping.
    //
    // Scalar height field derived from any ITexture (procedural or image).
    // Applied AFTER NormalMap when both are present (Arnold/Cycles
    // convention: bump composes on top of the normal map). The renderer
    // re-orthogonalises the TBN against the already-perturbed N before
    // applying the bump perturbation.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional bump map (scalar height field). Sampled via Rec.709 luminance
    /// of the inner texture; perturbs the shading normal via finite-difference
    /// gradients in tangent space. Composes on top of <see cref="NormalMap"/>.
    /// </summary>
    BumpMapTexture? BumpMap => null;

    // ─────────────────────────────────────────────────────────────────────────
    // Surface Displacement.
    //
    // Material-level (Cycles/RenderMan parity): the same material can drive
    // displacement across multiple mesh entities without per-entity
    // duplication. Eager application — the mesh loader reads this property
    // off the resolved entity material and hands it to DisplacementEngine
    // before BVH construction. Non-mesh entities (analytic primitives, CSG,
    // groups) ignore it with a load-time warning.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional surface-displacement descriptor. When non-null and the entity
    /// is a polygonal mesh, the loader deforms the (sub)divided limit topology
    /// before BVH construction. See <see cref="MaterialDisplacement"/> for the
    /// leaf / mix variants.
    /// </summary>
    MaterialDisplacement? Displacement => null;
}
