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
    /// <returns>A tuple containing the world-space Point, the surface Normal at that point, and the total surface Area.</returns>
    (Vector3 Point, Vector3 Normal, float Area) Sample();
}
