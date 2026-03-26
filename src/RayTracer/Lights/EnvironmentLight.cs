using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Rendering;

namespace RayTracer.Lights;

/// <summary>
/// Wraps SkySettings to provide direct lighting (Next Event Estimation)
/// from an HDRI environment map or a gradient sky with a sun disk.
/// Uses importance sampling to dramatically reduce noise vs purely indirect gathering.
///
/// Lifecycle:
///   - Illuminate()        → called once at Renderer construction for scene analysis.
///                           Returns a DETERMINISTIC luminance estimate (no PRNG).
///   - IlluminateAndTest() → called at every surface hit during rendering.
///                           Uses importance sampling (PRNG) for accurate NEE.
/// </summary>
public class EnvironmentLight : ILight
{
    private readonly SkySettings _sky;

    // FIX #9: removed redundant `= 1` inline initialiser — value always comes from constructor.
    /// <inheritdoc/>
    public int ShadowSamples { get; }

    public EnvironmentLight(SkySettings sky, int shadowSamples = 1)
    {
        _sky = sky;
        ShadowSamples = Math.Max(1, shadowSamples);
    }

    /// <summary>
    /// Returns a deterministic estimate of the environment's average luminance.
    ///
    /// FIX #7: guards on CanSampleDirectly (was missing — could return non-zero energy
    ///         for flat skies that have no direct-sampling support).
    /// FIX #8: divides by ShadowSamples for energetic consistency with IlluminateAndTest().
    /// FIX #10: uses SkySettings.EstimatedAverageLuminance instead of SampleDirectly(),
    ///          making the Renderer constructor's scene-analysis loop fully deterministic.
    ///          SampleDirectly() uses MathUtils.RandomFloat() internally; calling it here
    ///          made isIndirectDominant classification non-deterministic across runs.
    /// </summary>
    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        // FIX #7 — guard was absent in the original
        if (!_sky.CanSampleDirectly)
            return (Vector3.Zero, Vector3.UnitY, 0f);

        // FIX #10 — deterministic path: no PRNG
        float avgLum = _sky.EstimatedAverageLuminance;

        // FIX #8 — divide by ShadowSamples, consistent with IlluminateAndTest()
        return (new Vector3(avgLum / ShadowSamples), Vector3.UnitY, MathUtils.Infinity);
    }

    /// <inheritdoc/>
    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        if (!_sky.CanSampleDirectly)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        var (dir, color, pdf) = _sky.SampleDirectly();
        if (pdf <= 0f)
            return (true, Vector3.Zero, dir, 0f);

        float distance = MathUtils.Infinity;

        // Discard samples below the surface horizon
        float nDotL = Vector3.Dot(surfaceNormal, dir);
        if (nDotL <= 0f)
            return (true, Vector3.Zero, dir, distance);

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dir);
        var rec = new HitRecord();

        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, MathUtils.Infinity, ref rec);
        if (inShadow)
            return (true, Vector3.Zero, dir, distance);

        // L / (pdf × ShadowSamples): each sample contributes 1/ShadowSamples of total energy.
        // The N·L factor is applied by ComputeDirectLighting in Renderer (via EvaluateDirect).
        Vector3 attenuation = color / (pdf * ShadowSamples);
        return (false, attenuation, dir, distance);
    }
}
