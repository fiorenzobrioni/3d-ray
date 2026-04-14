using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Geometry;

/// <summary>
/// Represents a geometry that can be sampled for direct lighting (Next Event Estimation).
/// This allows turning arbitrary emissive primitives (Spheres, Triangles, etc.) into area lights.
/// </summary>
public interface ISamplable
{
    /// <summary>
    /// Samples a random point uniformly on the surface of the geometry.
    /// </summary>
    /// <returns>
    /// A tuple containing the world-space <c>Point</c>, the surface <c>Normal</c>
    /// at that point, the texture-space <c>Uv</c> at that point (used by
    /// <see cref="Materials.Emissive"/> to evaluate the exact emitted radiance
    /// instead of the old center-texel approximation) and the total surface <c>Area</c>.
    ///
    /// Primitives that do not have a natural UV parameterisation (e.g. procedural
    /// 3D emissives) should return <c>Uv = (0.5, 0.5)</c> — a reasonable neutral value
    /// for 2D textures while 3D procedural textures ignore UV anyway and use the
    /// world-space point.
    /// </returns>
    (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) Sample();

    /// <summary>
    /// Stratified version of <see cref="Sample"/>: divides the surface into a
    /// <paramref name="sqrtSamples"/> × <paramref name="sqrtSamples"/> grid and
    /// returns a jittered point within the cell identified by <paramref name="sampleIndex"/>.
    ///
    /// This dramatically reduces noise compared to pure random sampling by
    /// ensuring samples are spread evenly across the surface. The default
    /// implementation falls back to <see cref="Sample"/> for primitives that
    /// have not yet implemented stratification.
    /// </summary>
    /// <param name="sampleIndex">Index of the current sample (0..N-1).</param>
    /// <param name="sqrtSamples">Side length of the stratification grid.</param>
    (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sqrtSamples)
        => Sample();
}
