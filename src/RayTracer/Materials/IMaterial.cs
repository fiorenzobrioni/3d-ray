using System.Numerics;
using RayTracer.Core;

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
}
