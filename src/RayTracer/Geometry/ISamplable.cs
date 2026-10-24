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
    /// Stratified version of <see cref="Sample"/>: the <paramref name="sampleIndex"/>-th
    /// of <paramref name="sampleCount"/> points, one per cell of a grid that
    /// tiles the parameter square exactly (<see cref="Core.MathUtils.Stratum2D"/>).
    ///
    /// This dramatically reduces noise compared to pure random sampling by
    /// ensuring samples are spread evenly across the surface. The default
    /// implementation falls back to <see cref="Sample"/> for primitives that
    /// have not yet implemented stratification.
    /// </summary>
    /// <param name="sampleIndex">Index of the current sample (0..N-1).</param>
    /// <param name="sampleCount">How many samples the caller draws (N).</param>
    (Vector3 Point, Vector3 Normal, Vector2 Uv, float Area) SampleStratified(int sampleIndex, int sampleCount)
        => Sample();

    /// <summary>
    /// Total surface area of the geometry, computed deterministically without
    /// consuming the PRNG. Used by <see cref="RayTracer.Lights.GeometryLight"/>
    /// during scene construction so that repeated scene loads with the same
    /// input always yield an identical per-pixel random sequence.
    ///
    /// The default implementation returns the <c>Area</c> field from a
    /// <see cref="Sample"/> call. Implementations should override this with a
    /// closed-form formula (e.g. 4πR² for a sphere) to make it truly
    /// PRNG-free.
    /// </summary>
    float SurfaceArea => Sample().Area;

    /// <summary>
    /// The density, in area measure, with which <see cref="Sample"/> draws the
    /// surface point <paramref name="point"/> of normal <paramref name="normal"/>
    /// (either orientation): what a light sample's MIS weight and a
    /// bidirectional emission density must use for a point reached by a ray.
    /// <c>1 / SurfaceArea</c> for a sampler uniform by area.
    /// </summary>
    float AreaPdf(Vector3 point, Vector3 normal) => 1f / SurfaceArea;

    /// <summary>
    /// True when <see cref="Sample"/> is uniform by area (then
    /// <see cref="AreaPdf"/> is the constant <c>1 / SurfaceArea</c>). A
    /// non-uniformly scaled <see cref="Transform"/> is not: it maps the inner
    /// shape's uniform samples through a Jacobian that varies with the normal.
    /// </summary>
    bool UniformArea => true;
}
