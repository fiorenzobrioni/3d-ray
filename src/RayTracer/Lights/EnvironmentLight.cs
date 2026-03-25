using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Rendering;

namespace RayTracer.Lights;

/// <summary>
/// A light that wraps the SkySettings to provide direct lighting (Next Event Estimation)
/// from the environment map (HDRI) or a gradient sky with a sun.
/// Uses Importance Sampling to dramatically reduce noise compared to purely indirect gathering.
/// </summary>
public class EnvironmentLight : ILight
{
    private readonly SkySettings _sky;

    // How many times to sample the environment per pixel/bounce during NEE. 
    public int ShadowSamples { get; } = 1;

    public EnvironmentLight(SkySettings sky, int shadowSamples = 1)
    {
        _sky = sky;
        ShadowSamples = shadowSamples;
    }

    public (Vector3 Color, Vector3 DirectionToLight, float Distance) Illuminate(Vector3 hitPoint)
    {
        // This is only called via Illuminate(hitPoint) standalone, which is rare.
        // We just sample a direction.
        var (dir, color, pdf) = _sky.SampleDirectly();
        if (pdf <= 0f) return (Vector3.Zero, dir, 0f);
        
        // Attenuation for Env Light in NEE: L / PDF
        return (color / pdf, dir, MathUtils.Infinity);
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance) IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
    {
        if (!_sky.CanSampleDirectly)
            return (true, Vector3.Zero, Vector3.UnitY, 0f);

        var (dir, color, pdf) = _sky.SampleDirectly();
        if (pdf <= 0f)
            return (true, Vector3.Zero, dir, 0f);

        // Env lights are infinitely far away
        float distance = MathUtils.Infinity;
        
        // Ensure the sampled direction is above the surface
        float nDotL = Vector3.Dot(surfaceNormal, dir);
        if (nDotL <= 0f)
            return (true, Vector3.Zero, dir, distance);

        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dir);
        var rec = new HitRecord();
        
        // Test shadow ray to infinity (practically a very large number)
        bool inShadow = world.Hit(shadowRay, MathUtils.Epsilon, MathUtils.Infinity, ref rec);

        if (inShadow)
            return (true, Vector3.Zero, dir, distance);

        // Solid-angle based attenuation for EnvLight needs to just divide by PDF.
        // The cos(theta) factor is applied by the caller (ComputeDirectLighting in Renderer).
        // Since we use 1 sample, attenuation = L / PDF.
        // For ShadowSamples > 1, we divide by ShadowSamples.
        Vector3 attenuation = color / (pdf * ShadowSamples);

        return (false, attenuation, dir, distance);
    }
}
