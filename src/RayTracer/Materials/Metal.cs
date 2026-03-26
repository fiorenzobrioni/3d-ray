using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

public class Metal : IMaterial
{
    public ITexture Albedo { get; }
    public float Fuzz { get; }

    public Metal(Vector3 color, float fuzz)
    {
        Albedo = new SolidColor(color);
        Fuzz = MathF.Min(fuzz, 1f);
    }

    public Metal(ITexture a, float fuzz)
    {
        Albedo = a;
        Fuzz = MathF.Min(fuzz, 1f);
    }

    // ── Direct lighting properties ──────────────────────────────────────────
    //
    // DiffuseWeight:
    //   A perfect mirror (fuzz=0) should receive ZERO direct diffuse lighting —
    //   its illumination comes entirely from the traced reflected ray.
    //   As fuzz increases the surface becomes rougher and behaves more diffusely,
    //   so we linearly ramp the diffuse contribution with fuzz.
    //
    // SpecularExponent:
    //   Maps fuzz to a Blinn-Phong exponent. Fuzz=0 → exponent≈∞ (point highlight),
    //   fuzz=1 → exponent≈2 (very broad highlight). We use 2/(fuzz²+0.001) clamped.
    //   This creates the visible "hotspot" of point/spot lights on shiny metals.
    //
    // SpecularStrength:
    //   The peak intensity of the highlight. Higher for polished metals (low fuzz),
    //   tapering off for rough surfaces where the highlight spreads and weakens.
    //
    public float DiffuseWeight => Fuzz;

    public float SpecularExponent
    {
        get
        {
            if (Fuzz >= 1f) return 2f;
            float f = MathF.Max(Fuzz, 0.001f);
            return MathF.Min(2f / (f * f), 2048f);
        }
    }

    public float SpecularStrength => 1f - Fuzz * 0.5f; // 1.0 at fuzz=0, 0.5 at fuzz=1

    /// <summary>
    /// Metal direct lighting with view-dependent Schlick Fresnel on the specular lobe.
    /// Replaces the uniform specStrength scalar with a Fresnel-boosted value that
    /// produces stronger, tighter highlights at grazing angles — the defining visual
    /// characteristic of conductors that Blinn-Phong with fixed weight misses.
    ///
    /// The Fresnel is scalar (achromatic boost). The color response comes from
    /// TraceRay's attenuation = metal_albedo, keeping the two paths consistent.
    /// </summary>
    public Vector3 EvaluateDirect(Vector3 toLight, Vector3 toEye, Vector3 normal)
    {
        float nDotL = MathF.Max(Vector3.Dot(normal, toLight), 0f);
 
        // Diffuse term: scales with fuzz (rough metals have some diffuse scattering)
        float diffuse = nDotL * DiffuseWeight; // DiffuseWeight = Fuzz
 
        if (SpecularExponent <= 0f || nDotL <= 0f)
            return new Vector3(diffuse);
 
        Vector3 h = Vector3.Normalize(toLight + toEye);
        float nDotH = MathF.Max(Vector3.Dot(normal, h), 0f);
        float vDotH = MathF.Max(Vector3.Dot(toEye, h), 0f);
 
        // GGX-calibrated Blinn-Phong shape (SpecularExponent = 2/fuzz²)
        float bpShape = MathF.Pow(nDotH, SpecularExponent);
 
        // Schlick Fresnel: F0 = SpecularStrength (= 1 - fuzz/2), grazing → 1.
        // Scalar version: color tinting comes from attenuation (metal_albedo) in TraceRay.
        float s = 1f - vDotH;
        s *= s * s * s * s; // (1 - V·H)^5
        float fresnel = SpecularStrength + (1f - SpecularStrength) * s;
 
        return new Vector3(diffuse + bpShape * fresnel);
    }

    public NormalMapTexture? NormalMap { get; set; }

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var reflected = MathUtils.Reflect(Vector3.Normalize(rayIn.Direction), rec.Normal);
        reflected += Fuzz * MathUtils.RandomInUnitSphere();
        scattered = new Ray(rec.Point, reflected);
        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        return Vector3.Dot(scattered.Direction, rec.Normal) > 0;
    }
}
