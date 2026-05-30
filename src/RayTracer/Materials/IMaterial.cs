using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public interface IMaterial
{
    bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered);

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
    /// (sample every light, call <see cref="EvaluateDirect"/>). False means
    /// NEE is a waste of shadow rays — used by <see cref="Emissive"/>, which
    /// is the light source itself and absorbs no incoming illumination.
    /// </summary>
    bool NeedsDirectLighting => true;

    /// <summary>
    /// True when the indirect bounce produced by <see cref="Scatter"/> is a
    /// delta function (perfect mirror, ideal refraction). The renderer uses
    /// this to set <c>prevIsDelta</c> for the next TraceRay call — delta
    /// bounces see emission at full weight since no NEE sampler can reach
    /// through them. Materials that use the <see cref="Sample"/> API instead
    /// convey deltaness via <see cref="BsdfSample.IsDelta"/> and this flag
    /// is ignored.
    /// </summary>
    bool IsDeltaScatter => false;

    /// <summary>
    /// Evaluates the rendering-equation integrand at one shadow-ray direction
    /// for direct lighting (NEE). Called once per unshadowed light sample in
    /// <c>ComputeDirectLighting</c>.
    ///
    /// PBRT/Arnold convention — the returned value is the FULL integrand
    /// <c>f(V, L) · max(N·L, 0)</c>, with every material colour factor
    /// (baseColor/albedo, metallic Fresnel, sheen tint, subsurface tint,
    /// thin-film iridescence) already baked in. The caller multiplies by the
    /// light radiance and adds the result directly to the radiance estimator:
    /// <c>L = Le + L_direct + scatter_attn · L_indirect</c>. The scatter
    /// attenuation never multiplies the direct term — that would couple the
    /// indirect importance sample to the shadow estimator and bias the
    /// direct contribution for any direction-dependent BSDF.
    ///
    /// Delta lobes (Dirac mirror / refraction / near-delta GGX) return zero
    /// here: their f/pdf collapses to a mirror direction, no shadow ray can
    /// reach them, and the delta path in <see cref="Sample"/> handles
    /// emission with full weight via the prevIsDelta flag.
    ///
    /// <paramref name="rec"/> provides UV coordinates, local point, and object seed
    /// so that textured materials (e.g. DisneyBsdf with a BaseColor texture) can
    /// evaluate the correct surface properties at the hit point rather than using
    /// a fixed (0.5, 0.5) approximation.
    ///
    /// Default: zero — pure delta BSDFs (Dielectric mirror/refraction, Emissive)
    /// do not receive direct lighting. Lambertian, Metal, MixMaterial and
    /// DisneyBsdf override this with their full analytic response.
    /// </summary>
    /// <param name="toLight">Unit vector from hit point toward the light.</param>
    /// <param name="toEye">Unit vector from hit point toward the camera.</param>
    /// <param name="normal">Shading normal (may be perturbed by normal map).</param>
    /// <param name="rec">Hit record with UV, LocalPoint, ObjectSeed for texture lookups.</param>
    Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, HitRecord rec)
        => Vector3.Zero;

    /// <summary>
    /// Per-channel transmission factor for a "transparent shadow ray" travelling
    /// in direction <paramref name="wi"/> across this surface. Returned by
    /// dielectric/transmissive materials so that NEE can soften the otherwise
    /// binary occlusion test: glass casts a Fresnel-tinted shadow instead of a
    /// fully black one. Default is <see cref="Vector3.Zero"/> (opaque).
    ///
    /// The shadow ray is NOT refracted — it continues in a straight line and
    /// the returned factor is multiplied into the throughput. This is the
    /// standard "transparent shadow rays" approximation used by Arnold and
    /// Cycles: it correctly handles the loss of direct light at the
    /// caster/receiver pair (no more hard shadow under glass) but does NOT
    /// reproduce focused refractive caustics, which would require manifold
    /// next-event estimation or photon mapping (see DEVLOG roadmap).
    /// </summary>
    /// <param name="wi">Unit shadow-ray direction at the hit (surface → light).</param>
    /// <param name="rec">Hit record at the shadow-ray intersection.</param>
    Vector3 ShadowTransmittance(Vector3 wi, HitRecord rec) => Vector3.Zero;

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
    Vector3 ShadowAbsorption(HitRecord rec) => Vector3.Zero;

    /// <summary>
    /// Describes this material as a Manifold-Next-Event-Estimation caustic
    /// caster (smooth specular glass or mirror), or
    /// <see cref="CausticInterface.None"/> when it is not a smooth specular
    /// interface MNEE can solve. Only consulted for surfaces flagged
    /// <c>caustic_caster</c> in YAML, so the default (not a caster) keeps every
    /// other material on the existing transparent-shadow-ray path. See
    /// <see cref="Rendering.ManifoldWalker"/>.
    /// </summary>
    CausticInterface GetCausticInterface(HitRecord rec) => CausticInterface.None;

    // ─────────────────────────────────────────────────────────────────────────
    // Symmetric BSDF interface (BRDF value, PDF, sampling).
    //
    // These complement Scatter/EvaluateDirect and are what MIS, furnace tests,
    // and reciprocity tests consume directly. Unlike EvaluateDirect, Evaluate
    // returns the BRDF WITHOUT the cosine term so that reciprocity holds
    // symbolically: f(V, L) = f(L, V) for reciprocal materials.
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
    Vector3 Evaluate(Vector3 V, Vector3 L, HitRecord rec) => Vector3.Zero;

    /// <summary>
    /// Solid-angle PDF of sampling the outgoing direction L given the view
    /// direction V from this material's importance-sampler. Non-zero only in
    /// the hemisphere(s) the sampler actually covers. Delta lobes return zero
    /// here — they must be sampled via <see cref="Sample"/> and tagged
    /// via <see cref="BsdfSample.IsDelta"/>.
    /// </summary>
    float Pdf(Vector3 V, Vector3 L, HitRecord rec) => 0f;

    /// <summary>
    /// Samples an outgoing direction from this material's importance distribution.
    /// Returns null when sampling fails (fully absorbed, below-surface reflection,
    /// degenerate tangent frame, etc.). The returned <see cref="BsdfSample.F"/>
    /// matches <see cref="Evaluate"/>; <see cref="BsdfSample.Pdf"/> matches
    /// <see cref="Pdf"/> in solid angle.
    /// </summary>
    BsdfSample? Sample(Vector3 V, HitRecord rec) => null;

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
