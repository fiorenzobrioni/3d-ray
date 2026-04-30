using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;

namespace RayTracer.Lights;

/// <summary>
/// Parallel (sun) light with optional angular disc for realistic soft shadows.
///
/// <para><b>Hard mode (<c>angularRadiusDeg = 0</c>, default):</b>
/// Classic directional light — Dirac-delta distribution, <c>IsDelta = true</c>,
/// single shadow ray, perfectly sharp shadows.</para>
///
/// <para><b>Soft mode (<c>angularRadiusDeg &gt; 0</c>):</b>
/// Models a disc of finite angular size (e.g. 0.27° for the real Sun). Each
/// shadow sample perturbs <c>-Direction</c> by a random direction drawn
/// uniformly within the subtended cone using the same cone-sampling formula
/// as <see cref="SphereLight"/> (PBRT §6.2.3):
/// <c>cosθ = 1 − ξ₁(1 − cos(angularRadius))</c>, <c>φ = 2πξ₂</c>. The
/// <c>ShadowSamples</c> default is raised to 4 when disc mode is active so
/// the penumbra is captured at the same quality as area lights. <c>IsDelta</c>
/// becomes <c>false</c> and <c>PdfSolidAngle</c> returns 1/Ω for directions
/// inside the cone.</para>
///
/// <b>Sun value:</b> 0.27° ≈ the real solar disc half-angle.
/// </summary>
public class DirectionalLight : ILight
{
    public Vector3 Direction { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }

    /// <summary>
    /// Angular radius of the light disc in degrees. 0 = hard shadows (delta
    /// light, default). Set to 0.27 for a physically realistic solar disc.
    /// </summary>
    public float AngularRadiusDeg { get; }

    private readonly float _cosAngularRadius; // cos(angularRadius) — cone half-angle cosine
    private readonly float _solidAngle;       // 2π(1 − cos) — used by PdfSolidAngle for MIS

    // Stratified grid for shadow samples in disc mode
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    /// <inheritdoc/>
    // Hard directional light is a Dirac delta — no BSDF sampler can generate
    // the exact sun direction. When disc mode is active the distribution is a
    // uniform cone, which is an area-like emitter: delta = false.
    public bool IsDelta => AngularRadiusDeg <= 0f;

    public DirectionalLight(Vector3 direction, Vector3 color, float intensity = 1f,
                            float angularRadiusDeg = 0f, int shadowSamples = 0)
    {
        Direction = Vector3.Normalize(direction);
        Color = color;
        Intensity = intensity;
        AngularRadiusDeg = MathF.Max(0f, angularRadiusDeg);

        bool hasDisc = AngularRadiusDeg > 0f;
        float radians = MathUtils.DegreesToRadians(AngularRadiusDeg);
        _cosAngularRadius = hasDisc ? MathF.Cos(radians) : 1f;
        _solidAngle = hasDisc ? 2f * MathF.PI * (1f - _cosAngularRadius) : 0f;

        // shadowSamples = 0 → use sensible defaults: 1 for delta, 4 for disc
        ShadowSamples = shadowSamples > 0 ? shadowSamples : (hasDisc ? 4 : 1);

        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
        _invSqrtSamples = 1f / _sqrtSamples;
    }

    // Directional emitter: irradiance I (W/m²) on a plane perpendicular to the
    // direction, integrated over the scene's projected cross-section.
    //   Φ = I · π · R²   where R = scene bounding-sphere radius.
    // This produces a flux that scales with scene size the same way the other
    // lights do, so the Renderer's normalised-irradiance classifier behaves
    // consistently whether the scene is lit by a sun, a sky, or finite emitters.
    // The disc angular size has negligible effect on the total irradiance
    // (Ω_disc ≈ π × (0.27°/57.3)² ≈ 7e-5 sr for the real Sun — the power is
    // dominated by the solid-angle-integrated I, unchanged to <0.01%).
    public float ApproximatePower(AABB sceneBounds)
    {
        Vector3 extent = sceneBounds.Max - sceneBounds.Min;
        float radius = 0.5f * extent.Length();
        float crossSection = MathF.PI * radius * radius;
        return MathUtils.Luminance(Color) * Intensity * crossSection;
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        return IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1);
    }

    /// <summary>
    /// Stratified variant for the disc-mode multi-sample path.
    /// For hard (delta) lights both methods are equivalent.
    /// </summary>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal,
                                    IHittable world, int sampleIndex)
    {
        Vector3 toLight;

        if (AngularRadiusDeg > 0f)
        {
            // Disc mode: perturb -Direction uniformly within the angular cone.
            float xi1, xi2;
            if (sampleIndex >= 0)
            {
                int su = sampleIndex % _sqrtSamples;
                int sv = sampleIndex / _sqrtSamples;
                xi1 = (su + MathUtils.RandomFloat()) * _invSqrtSamples;
                xi2 = (sv + MathUtils.RandomFloat()) * _invSqrtSamples;
            }
            else
            {
                xi1 = MathUtils.RandomFloat();
                xi2 = MathUtils.RandomFloat();
            }

            // Uniform cone sampling (PBRT §6.2.3):
            //   cosθ = 1 - ξ₁(1 - cosR),  φ = 2πξ₂
            float cosTheta = 1f - xi1 * (1f - _cosAngularRadius);
            float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
            float phi = 2f * MathF.PI * xi2;

            Vector3 wDir = -Direction; // base sun direction
            Vector3 localSample = new(sinTheta * MathF.Cos(phi),
                                       sinTheta * MathF.Sin(phi),
                                       cosTheta);
            toLight = LocalToWorld(localSample, wDir);
        }
        else
        {
            toLight = -Direction;
        }

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, toLight);
        var rec = new HitRecord();
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, MathUtils.Infinity, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, toLight, MathUtils.Infinity);

        // The YAML-supplied `intensity` is irradiance (W/m²) — the contribution
        // a perpendicular surface would receive if unoccluded. In hard mode a
        // single delta sample carries the full irradiance. In disc mode we
        // average N stratified cone samples whose pdf is 1/Ω, so per-sample
        // contribution is Intensity / N (NOT × Ω — that factor is already
        // implicit in the cone sampling; the previous formula multiplied it in
        // and dimmed a real-Sun setup by ≈14 000×). Summing N samples gives
        // back the full Intensity, matching hard mode within Monte-Carlo noise.
        float energyPerSample = Intensity / ShadowSamples;

        return (false, Color * energyPerSample, toLight, MathUtils.Infinity);
    }

    // ── MIS ─────────────────────────────────────────────────────────────────
    // IsDelta overridden as property above.

    /// <summary>
    /// Solid-angle PDF of the uniform-cone sampler used in disc mode.
    /// Returns 1/Ω for directions inside the cone, 0 otherwise or for delta mode.
    /// </summary>
    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        if (AngularRadiusDeg <= 0f) return 0f;  // delta — no pdf
        if (_solidAngle <= 0f)     return 0f;

        float wiLen = wi.Length();
        if (wiLen < MathUtils.Epsilon) return 0f;

        float cosTheta = Vector3.Dot(wi / wiLen, -Direction);
        if (cosTheta < _cosAngularRadius) return 0f;

        return 1f / _solidAngle;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds an ONB with <paramref name="w"/> as the Z axis and transforms
    /// <paramref name="local"/> to world space. Uses Frisvad's method with a
    /// widened singularity guard (same as <see cref="SphereLight.LocalToWorld"/>).
    /// </summary>
    private static Vector3 LocalToWorld(Vector3 local, Vector3 w)
    {
        Vector3 u, v;

        if (w.Z < -0.999f)
        {
            u = new Vector3(0f, -1f, 0f);
            v = new Vector3(-1f, 0f, 0f);
        }
        else
        {
            float a = 1f / (1f + w.Z);
            float b = -w.X * w.Y * a;
            u = new Vector3(1f - w.X * w.X * a, b, -w.X);
            v = new Vector3(b, 1f - w.Y * w.Y * a, -w.Y);
        }

        Vector3 result = local.X * u + local.Y * v + local.Z * w;
        float lenSq = result.LengthSquared();
        return lenSq > 1e-8f ? result / MathF.Sqrt(lenSq) : w;
    }
}
