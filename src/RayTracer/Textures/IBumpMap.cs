using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// A bump-map sampler that returns a tangent-space normal at a shading point.
/// Implemented by the single-texture <see cref="BumpMapTexture"/> and by the
/// mix-blended <see cref="MixBumpMapTexture"/>. The renderer treats both
/// uniformly via <see cref="ApplyBumpMap"/>.
///
/// <para>Returning <c>(0, 0, 1)</c> means "no perturbation". Results are
/// normalised and ready to be transformed by the surface TBN.</para>
/// </summary>
public interface IBumpMap
{
    /// <summary>
    /// Samples the bump field and returns a tangent-space normal.
    /// </summary>
    Vector3 SampleTangentNormal(float u, float v, Vector3 p,
                                Vector3 tangent, Vector3 bitangent,
                                int seed);
}
