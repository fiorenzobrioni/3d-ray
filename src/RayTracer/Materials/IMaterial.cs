using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public interface IMaterial
{
    bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered);

    // ─────────────────────────────────────────────────────────────────────────
    // Direct lighting control properties.
    //
    // DiffuseWeight:
    //   Fraction of incoming direct light that is diffusely reflected (Lambertian term).
    //   1.0 for fully diffuse materials, 0.0 for pure specular (mirrors, glass).
    //   Metal uses Fuzz as the weight — a perfect mirror gets zero diffuse and relies
    //   entirely on the traced reflected ray; a rough metal gets some direct diffuse.
    //
    // SpecularExponent:
    //   Controls the tightness of the Blinn-Phong specular highlight on direct lights.
    //   Higher values = sharper, smaller highlight. 0 = no highlight.
    //   Only used for materials that have a specular component to their direct lighting.
    //
    // SpecularStrength:
    //   The intensity multiplier of the specular highlight (0–1).
    //   Lets materials control how prominent their highlights are.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// How much of the direct light is diffusely reflected (Lambert N·L term).
    /// 0 = pure specular (mirror/glass), 1 = fully diffuse.
    /// </summary>
    float DiffuseWeight => 1f;

    /// <summary>
    /// Blinn-Phong specular exponent for direct light highlights.
    /// 0 = no highlight. Higher = tighter, sharper highlight.
    /// </summary>
    float SpecularExponent => 0f;

    /// <summary>
    /// Intensity multiplier for the specular highlight (0–1).
    /// </summary>
    float SpecularStrength => 0f;

    /// <summary>
    /// Evaluates the BRDF "shape" for direct lighting (NEE).
    /// Called once per unshadowed light sample in ComputeDirectLighting.
    ///
    /// Returns a combined diffuse + specular response WITHOUT material albedo/color —
    /// that factor is already present in the scatter attenuation applied by TraceRay.
    /// The light color is multiplied by the caller (ComputeDirectLighting).
    ///
    /// <paramref name="rec"/> provides UV coordinates, local point, and object seed
    /// so that textured materials (e.g. DisneyBsdf with a BaseColor texture) can
    /// evaluate the correct surface properties at the hit point rather than using
    /// a fixed (0.5, 0.5) approximation.
    ///
    /// Default: Lambert diffuse (N·L × diffuseWeight) + Blinn-Phong specular.
    /// Override in materials that benefit from view-dependent Fresnel (Metal, Disney).
    /// </summary>
    /// <param name="toLight">Unit vector from hit point toward the light.</param>
    /// <param name="toEye">Unit vector from hit point toward the camera.</param>
    /// <param name="normal">Shading normal (may be perturbed by normal map).</param>
    /// <param name="rec">Hit record with UV, LocalPoint, ObjectSeed for texture lookups.</param>
    Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal, HitRecord rec)
    {
        float nDotL = MathF.Max(Vector3.Dot(normal, toLight), 0f);
        float result = nDotL * DiffuseWeight;

        if (SpecularExponent > 0f && SpecularStrength > 0f && nDotL > 0f)
        {
            Vector3 h = Vector3.Normalize(toLight + toEye);
            float nDotH = MathF.Max(Vector3.Dot(normal, h), 0f);
            result += MathF.Pow(nDotH, SpecularExponent) * SpecularStrength;
        }

        return new Vector3(result);
    }

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
}
