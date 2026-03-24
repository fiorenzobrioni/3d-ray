using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Materials;

/// <summary>
/// Disney Principled BSDF — a unified PBR material model inspired by
/// Brent Burley's 2012 paper "Physically Based Shading at Disney".
///
/// This single material type can represent virtually any real-world surface
/// by blending multiple lobes via intuitive artist-friendly parameters:
///
///   baseColor     — Surface albedo (diffuse color or metallic reflectance)
///   metallic      — 0 = dielectric (plastic, wood, skin), 1 = metal (gold, chrome)
///   roughness     — 0 = mirror-smooth, 1 = perfectly diffuse
///   subsurface    — Blends Lambert diffuse with a flatter subsurface approximation
///   specular      — Intensity of the dielectric specular lobe (F0 control)
///   specularTint  — Tints the dielectric Fresnel by baseColor
///   sheen         — Additional grazing-angle sheen (fabric, velvet)
///   sheenTint     — Tints the sheen by baseColor
///   clearcoat     — Second specular lobe (car paint, lacquer)
///   clearcoatGloss— Roughness of the clearcoat layer (1 = glossy, 0 = satin)
///   specTrans     — Specular transmission (0 = opaque, 1 = glass-like)
///   ior           — Index of refraction for dielectric specular/transmission
///
/// In YAML:
///   - id: "plastic_red"
///     type: "disney"
///     color: [0.8, 0.1, 0.1]
///     roughness: 0.3
///     specular: 0.5
///
///   - id: "gold"
///     type: "disney"
///     color: [1.0, 0.71, 0.29]
///     metallic: 1.0
///     roughness: 0.2
///
///   - id: "car_paint"
///     type: "disney"
///     color: [0.05, 0.1, 0.6]
///     metallic: 0.0
///     roughness: 0.4
///     clearcoat: 1.0
///     clearcoat_gloss: 0.9
/// </summary>
public class DisneyBsdf : IMaterial
{
    // ── Core parameters ─────────────────────────────────────────────────────
    public ITexture BaseColor  { get; }
    public float Metallic      { get; }
    public float Roughness     { get; }
    public float Subsurface    { get; }
    public float Specular      { get; }
    public float SpecularTint  { get; }
    public float Sheen         { get; }
    public float SheenTint     { get; }
    public float Clearcoat     { get; }
    public float ClearcoatGloss{ get; }
    public float SpecTrans     { get; }
    public float Ior           { get; }

    // ── Normal map support ──────────────────────────────────────────────────
    public NormalMapTexture? NormalMap { get; set; }

    // ── Cached derived values ───────────────────────────────────────────────
    private readonly float _alpha;          // roughness² (GGX α)
    private readonly float _clearcoatAlpha; // clearcoat roughness²

    public DisneyBsdf(
        ITexture baseColor,
        float metallic       = 0f,
        float roughness      = 0.5f,
        float subsurface     = 0f,
        float specular       = 0.5f,
        float specularTint   = 0f,
        float sheen          = 0f,
        float sheenTint      = 0.5f,
        float clearcoat      = 0f,
        float clearcoatGloss = 1f,
        float specTrans      = 0f,
        float ior            = 1.5f)
    {
        BaseColor      = baseColor;
        Metallic       = Math.Clamp(metallic, 0f, 1f);
        Roughness      = Math.Clamp(roughness, 0f, 1f);
        Subsurface     = Math.Clamp(subsurface, 0f, 1f);
        Specular       = Math.Clamp(specular, 0f, 2f);   // Allow > 1 for artistic control
        SpecularTint   = Math.Clamp(specularTint, 0f, 1f);
        Sheen          = Math.Clamp(sheen, 0f, 1f);
        SheenTint      = Math.Clamp(sheenTint, 0f, 1f);
        Clearcoat      = Math.Clamp(clearcoat, 0f, 1f);
        ClearcoatGloss = Math.Clamp(clearcoatGloss, 0f, 1f);
        SpecTrans      = Math.Clamp(specTrans, 0f, 1f);
        Ior            = MathF.Max(ior, 1.0001f);

        // GGX α parameter — clamp to avoid singularity at zero
        _alpha = MathF.Max(Roughness * Roughness, 0.001f);

        // Clearcoat uses a separate roughness: clearcoatGloss blends 0.1→0.001
        float ccRough = Lerp(0.1f, 0.001f, ClearcoatGloss);
        _clearcoatAlpha = ccRough * ccRough;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Direct lighting interface
    //
    // Maps Disney parameters to the renderer's DiffuseWeight / SpecularExponent
    // / SpecularStrength interface used by ComputeDirectLighting().
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Diffuse weight decreases with metallic and specTrans — metals and glass
    /// don't have a diffuse lobe. Roughness increases the diffuse contribution
    /// slightly for direct lighting visual consistency.
    /// </summary>
    public float DiffuseWeight
    {
        get
        {
            float diffuse = (1f - Metallic) * (1f - SpecTrans);
            return diffuse * MathF.Max(0.3f, Roughness);
        }
    }

    /// <summary>
    /// Blinn-Phong exponent derived from roughness for direct light highlights.
    /// Low roughness → tight sharp highlight; high roughness → broad soft highlight.
    /// </summary>
    public float SpecularExponent
    {
        get
        {
            if (_alpha >= 1f) return 2f;
            return MathF.Min(2f / (_alpha * _alpha), 2048f);
        }
    }

    /// <summary>
    /// Specular highlight strength: strong for metals and smooth dielectrics,
    /// with clearcoat adding extra highlight intensity.
    /// </summary>
    public float SpecularStrength
    {
        get
        {
            float baseSpec = Metallic > 0.5f ? 1f : Specular;
            float ccBoost = Clearcoat * 0.25f;
            return MathF.Min(baseSpec * (1f - Roughness * 0.5f) + ccBoost, 1f);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Scatter — stochastic lobe selection for indirect rays
    //
    // Each bounce randomly selects one lobe weighted by expected contribution.
    // The returned attenuation is compensated by 1/probability to keep the
    // Monte Carlo estimator unbiased across all lobes.
    // ═════════════════════════════════════════════════════════════════════════

    public bool Scatter(Ray rayIn, HitRecord rec, out Vector3 attenuation, out Ray scattered)
    {
        Vector3 baseCol = BaseColor.Value(rec.U, rec.V, rec.LocalPoint, rec.ObjectSeed);
        Vector3 N = rec.Normal;
        Vector3 V = Vector3.Normalize(-rayIn.Direction);

        // ── Compute lobe weights for importance sampling ────────────────────
        float diffuseW  = (1f - Metallic) * (1f - SpecTrans);
        float specularW = MathF.Max(0.1f, Lerp(Specular, 1f, Metallic)); // Always some specular
        float transW    = (1f - Metallic) * SpecTrans;
        float clearW    = Clearcoat * 0.25f;

        float totalW = diffuseW + specularW + transW + clearW;
        if (totalW < 1e-6f) { totalW = 1f; specularW = 1f; } // Fallback

        float pDiffuse  = diffuseW / totalW;
        float pSpecular = specularW / totalW;
        float pTrans    = transW / totalW;
        // pClearcoat = remainder

        float rnd = MathUtils.RandomFloat();

        bool result;
        if (rnd < pDiffuse)
        {
            result = ScatterDiffuse(rec, baseCol, N, V, pDiffuse, out attenuation, out scattered);
        }
        else if ((rnd -= pDiffuse) < pSpecular)
        {
            result = ScatterSpecular(rec, baseCol, N, V, pSpecular, out attenuation, out scattered);
        }
        else if ((rnd -= pSpecular) < pTrans)
        {
            result = ScatterTransmission(rayIn, rec, baseCol, N, pTrans, out attenuation, out scattered);
        }
        else
        {
            float pClearcoat = clearW / totalW;
            result = ScatterClearcoat(rec, N, V, pClearcoat, out attenuation, out scattered);
        }

        // ── Sanitize attenuation ────────────────────────────────────────────
        // Guard against NaN/Inf from degenerate geometry (zero-length half
        // vectors, near-singular ONB, etc.) and against extreme values from
        // edge-case BSDF evaluation. Without this, NaN propagates through
        // the TraceRay recursion and becomes a black spot (NaN → 0 in
        // ClampRadiance), while extreme values become fireflies.
        if (float.IsNaN(attenuation.X) || float.IsInfinity(attenuation.X) ||
            float.IsNaN(attenuation.Y) || float.IsInfinity(attenuation.Y) ||
            float.IsNaN(attenuation.Z) || float.IsInfinity(attenuation.Z))
        {
            // Fall back to simple Lambert — returns correct-ish color
            // instead of black (NaN→0) or white (Inf).
            attenuation = baseCol;
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Individual lobe scatter methods
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Disney diffuse: blend of standard Lambert and subsurface approximation
    /// with Fresnel-based retro-reflection at grazing angles.
    /// Sheen is added here as it contributes primarily at grazing angles.
    /// </summary>
    private bool ScatterDiffuse(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                                float probability,
                                out Vector3 attenuation, out Ray scattered)
    {
        // Cosine-weighted hemisphere sampling
        Vector3 scatterDir = N + MathUtils.RandomUnitVector();
        if (MathUtils.NearZero(scatterDir))
            scatterDir = N;
        scatterDir = Vector3.Normalize(scatterDir);

        scattered = new Ray(rec.Point, scatterDir);

        // Compute Disney diffuse Fresnel correction
        Vector3 L = scatterDir;
        float NdotV = MathF.Max(Vector3.Dot(N, V), 0.001f);
        float NdotL = MathF.Max(Vector3.Dot(N, L), 0.001f);

        // Half-vector — GUARD against NaN when V and L are nearly opposite
        // (grazing incidence with backscatter). V+L ≈ zero → Normalize → NaN.
        Vector3 Hraw = V + L;
        float hLenSq = Hraw.LengthSquared();
        Vector3 H = hLenSq > 1e-7f ? Hraw / MathF.Sqrt(hLenSq) : N;
        float LdotH = MathF.Max(Vector3.Dot(L, H), 0f);

        // Disney diffuse Fresnel factor (retro-reflection at grazing angles)
        float fd90 = 0.5f + 2f * Roughness * LdotH * LdotH;
        float fI = SchlickWeight(NdotV);
        float fO = SchlickWeight(NdotL);
        float fd = (1f + (fd90 - 1f) * fI) * (1f + (fd90 - 1f) * fO);

        // Subsurface approximation (Hanrahan-Krueger inspired).
        // CLAMP: the 1/(NdotV+NdotL) term explodes at grazing angles
        // (e.g. NdotV=NdotL=0.001 → 1/0.003 = 333 → ss ≈ 416).
        // Physically the subsurface effect is a ~1.25× brightness boost,
        // not a 400× explosion. Clamping ss to [0, 2] preserves the visual
        // effect while eliminating the firefly source.
        float fss90 = Roughness * LdotH * LdotH;
        float fssI = 1f + (fss90 - 1f) * fI;
        float fssO = 1f + (fss90 - 1f) * fO;
        float ssRaw = 1.25f * (fssI * fssO * (1f / (NdotV + NdotL + 0.001f) - 0.5f) + 0.5f);
        float ss = Math.Clamp(ssRaw, 0f, 2f);

        // Blend Lambert and subsurface — result is now bounded [0, ~2.5]
        float diffuseFactor = Lerp(fd, ss, Subsurface);
        attenuation = baseCol * diffuseFactor;

        // Add sheen at grazing angles
        if (Sheen > 0f)
        {
            float fH = SchlickWeight(LdotH);
            float lum = MathUtils.Luminance(baseCol);
            Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
            Vector3 sheenCol = Vector3.Lerp(Vector3.One, tintCol, SheenTint);
            attenuation += Sheen * fH * sheenCol;
        }

        // Compensate for lobe selection probability (multi-lobe MIS).
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// GGX specular reflection. For metals the Fresnel is tinted by baseColor;
    /// for dielectrics it uses the IOR-derived F0 value.
    /// </summary>
    private bool ScatterSpecular(HitRecord rec, Vector3 baseCol, Vector3 N, Vector3 V,
                                 float probability,
                                 out Vector3 attenuation, out Ray scattered)
    {
        // Sample GGX microfacet normal
        Vector3 H = SampleGGX(N, _alpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        if (Vector3.Dot(L, N) <= 0f)
        {
            // Below-surface reflection — absorb
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        float VdotH = MathF.Max(Vector3.Dot(V, H), 0f);

        // Compute Fresnel
        Vector3 F0 = ComputeF0(baseCol);
        Vector3 fresnel = FresnelSchlick(VdotH, F0);

        attenuation = fresnel;

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Specular transmission for glass-like materials.
    /// Uses Schlick's approximation for reflection vs refraction choice,
    /// with roughness-based perturbation for frosted glass effects.
    /// </summary>
    private bool ScatterTransmission(Ray rayIn, HitRecord rec, Vector3 baseCol,
                                     Vector3 N, float probability,
                                     out Vector3 attenuation, out Ray scattered)
    {
        float eta = rec.FrontFace ? (1f / Ior) : Ior;

        Vector3 unitDir = Vector3.Normalize(rayIn.Direction);
        float cosTheta = MathF.Min(Vector3.Dot(-unitDir, N), 1f);
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        bool cannotRefract = eta * sinTheta > 1f;
        Vector3 direction;

        if (cannotRefract || MathUtils.Schlick(cosTheta, eta) > MathUtils.RandomFloat())
        {
            // Total internal reflection or Fresnel reflection
            direction = MathUtils.Reflect(unitDir, N);
        }
        else
        {
            direction = MathUtils.Refract(unitDir, N, eta);
            // Add roughness perturbation for frosted glass
            if (Roughness > 0.01f)
            {
                float perturbation = Roughness * Roughness;
                direction = Vector3.Normalize(direction + perturbation * MathUtils.RandomInUnitSphere());
            }
        }

        scattered = new Ray(rec.Point, direction);

        // Tint transmitted light by sqrt(baseColor) for colored glass
        attenuation = new Vector3(
            MathF.Sqrt(baseCol.X),
            MathF.Sqrt(baseCol.Y),
            MathF.Sqrt(baseCol.Z));

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    /// <summary>
    /// Clearcoat: a fixed-IOR (1.5) secondary specular lobe with its own roughness.
    /// Always white (physically: a thin transparent varnish layer).
    /// </summary>
    private bool ScatterClearcoat(HitRecord rec, Vector3 N, Vector3 V,
                                  float probability,
                                  out Vector3 attenuation, out Ray scattered)
    {
        Vector3 H = SampleGGX(N, _clearcoatAlpha);
        Vector3 L = MathUtils.Reflect(-V, H);

        if (Vector3.Dot(L, N) <= 0f)
        {
            attenuation = Vector3.Zero;
            scattered = new Ray(rec.Point, N);
            return false;
        }

        scattered = new Ray(rec.Point, L);

        float VdotH = MathF.Max(Vector3.Dot(V, H), 0f);

        // Clearcoat uses fixed F0 = 0.04 (IOR ≈ 1.5)
        float f0 = 0.04f;
        float fresnel = f0 + (1f - f0) * SchlickWeight(VdotH);

        // Scale by clearcoat parameter — this is the lobe's energy budget
        attenuation = new Vector3(Clearcoat * fresnel);

        // Compensate for lobe selection probability
        float safeProbability = MathF.Max(probability, 0.1f);
        attenuation /= safeProbability;

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GGX sampling and Fresnel utilities
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Importance-samples a microfacet normal from the GGX (Trowbridge-Reitz)
    /// distribution. Returns a half-vector H in world space.
    /// </summary>
    private static Vector3 SampleGGX(Vector3 N, float alpha)
    {
        float u1 = MathUtils.RandomFloat();
        float u2 = MathUtils.RandomFloat();

        // GGX sampling in spherical coordinates
        float a2 = alpha * alpha;
        float cosTheta = MathF.Sqrt((1f - u1) / (1f + (a2 - 1f) * u1));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;

        // Local tangent-space H
        Vector3 Hlocal = new(
            sinTheta * MathF.Cos(phi),
            sinTheta * MathF.Sin(phi),
            cosTheta);

        // Transform to world space using an orthonormal basis around N
        return TangentToWorld(Hlocal, N);
    }

    /// <summary>
    /// Builds an orthonormal basis around N and transforms a tangent-space
    /// vector to world space. Uses Frisvad's robust method.
    /// </summary>
    private static Vector3 TangentToWorld(Vector3 local, Vector3 N)
    {
        Vector3 T, B;
        // Frisvad's method — but the 1/(1+N.Z) term becomes numerically
        // unstable as N.Z approaches -1. At N.Z = -0.999, a = 1000 and
        // the resulting T/B vectors are near-degenerate, potentially
        // producing NaN after normalization. Widen the threshold to -0.999
        // (was -0.9999) to use the safe fallback basis for near-south-pole
        // normals. The error is imperceptible (0.1° of normal deviation).
        if (N.Z < -0.999f)
        {
            T = new Vector3(0f, -1f, 0f);
            B = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + N.Z);
            float b = -N.X * N.Y * a;
            T = new Vector3(1f - N.X * N.X * a, b, -N.X);
            B = new Vector3(b, 1f - N.Y * N.Y * a, -N.Y);
        }

        Vector3 result = local.X * T + local.Y * B + local.Z * N;
        float lenSq = result.LengthSquared();
        // Guard against degenerate zero-length vector → NaN from Normalize
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : N;
    }

    /// <summary>
    /// Computes the Fresnel reflectance at normal incidence (F0).
    /// Metals use baseColor directly; dielectrics use IOR-derived value
    /// optionally tinted towards baseColor via specularTint.
    /// </summary>
    private Vector3 ComputeF0(Vector3 baseCol)
    {
        // Dielectric F0 from IOR
        float r = (Ior - 1f) / (Ior + 1f);
        float f0d = r * r;

        // Disney's specular parameter scales F0 (0.5 → standard, 1.0 → 2× brighter)
        float scaledF0 = f0d * 2f * Specular;

        // Tint towards baseColor if specularTint > 0
        float lum = MathUtils.Luminance(baseCol);
        Vector3 tintCol = lum > 0f ? baseCol / lum : Vector3.One;
        Vector3 dielectricF0 = Vector3.Lerp(new Vector3(scaledF0), scaledF0 * tintCol, SpecularTint);

        // Blend between dielectric and metallic F0
        return Vector3.Lerp(dielectricF0, baseCol, Metallic);
    }

    /// <summary>
    /// Schlick Fresnel with Vector3 for per-channel metallic coloring.
    /// </summary>
    private static Vector3 FresnelSchlick(float cosTheta, Vector3 f0)
    {
        float w = SchlickWeight(cosTheta);
        return f0 + (Vector3.One - f0) * w;
    }

    /// <summary>
    /// (1 - cosθ)^5 — the Schlick Fresnel weight.
    /// </summary>
    private static float SchlickWeight(float cosTheta)
    {
        float x = Math.Clamp(1f - cosTheta, 0f, 1f);
        float x2 = x * x;
        return x2 * x2 * x; // x^5
    }

    /// <summary>
    /// Scalar linear interpolation — private to avoid dependency on MathUtils
    /// for this single hot-path operation.
    /// </summary>
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
