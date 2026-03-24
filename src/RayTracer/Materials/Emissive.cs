using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// An emissive (light-emitting) material. Objects with this material glow
/// with their own light and are visible even without external illumination.
/// They do NOT scatter incoming rays — all their contribution comes from emission.
///
/// Typical uses: neon tubes, lava, glowing orbs, visible light panels, LED strips,
/// fireflies, magic effects, signage.
///
/// In YAML:
///   - id: "neon_magenta"
///     type: "emissive"
///     color: [1.0, 0.0, 0.8]
///     intensity: 5.0
///
/// The emitted radiance is color * intensity.  Intensity > 1 creates bloom-like
/// over-bright surfaces that illuminate nearby objects via indirect bounces.
/// </summary>
public class Emissive : IMaterial
{
    public ITexture Albedo { get; }
    public float Intensity { get; }

    public Emissive(Vector3 color, float intensity = 1f)
    {
        Albedo = new SolidColor(color);
        Intensity = MathF.Max(intensity, 0f);
    }

    public Emissive(ITexture texture, float intensity = 1f)
    {
        Albedo = texture;
        Intensity = MathF.Max(intensity, 0f);
    }

    // ── Direct lighting properties ──────────────────────────────────────────
    // Emissive surfaces don't receive external illumination — they ARE the light.
    // No diffuse, no specular. All contribution comes from Emit().
    public float DiffuseWeight => 0f;
    public float SpecularExponent => 0f;
    public float SpecularStrength => 0f;
    public NormalMapTexture? NormalMap { get; set; }

    /// <summary>
    /// Emissive materials do not scatter incoming rays.
    /// Returning false tells the path tracer to stop bouncing and use
    /// only the emitted + direct light contribution for this hit.
    /// </summary>
    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        attenuation = Vector3.Zero;
        scattered = default;
        return false;
    }

    /// <summary>
    /// Returns the emitted radiance at the given surface point.
    /// Only emits from the front face — the back side of an emissive
    /// surface is dark, just like a real neon tube or LED panel.
    /// </summary>
    public Vector3 Emit(float u, float v, Vector3 point, int objectSeed, bool frontFace)
    {
        if (!frontFace) return Vector3.Zero;
        return Albedo.Value(u, v, point, objectSeed) * Intensity;
    }
}
