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

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        var reflected = MathUtils.Reflect(Vector3.Normalize(rayIn.Direction), rec.Normal);
        reflected += Fuzz * MathUtils.RandomInUnitSphere();
        scattered = new Ray(rec.Point, reflected);
        attenuation = Albedo.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        return Vector3.Dot(scattered.Direction, rec.Normal) > 0;
    }
}
