using System.Numerics;

namespace RayTracer.Textures;

/// <summary>
/// How a step along the surface moves a texture's inputs, for a bump map
/// taking the height field's slope. Per unit of length along the shading
/// tangent and bitangent: the change of (u, v), and the change of the 3D
/// point the texture reads (<c>HitRecord.LocalPoint</c>, whose space is
/// rotated, not scaled, against world space).
/// </summary>
public readonly struct BumpFrame
{
    public Vector2 UvT { get; init; }
    public Vector2 UvB { get; init; }
    public Vector3 PointT { get; init; }
    public Vector3 PointB { get; init; }

    /// <summary>
    /// A flat surface with u along x and v along y, one UV unit per unit of
    /// length: the frame of a unit quad in its own space.
    /// </summary>
    public static BumpFrame Unit { get; } = new()
    {
        UvT = Vector2.UnitX, UvB = Vector2.UnitY,
        PointT = Vector3.UnitX, PointB = Vector3.UnitY,
    };
}

/// <summary>
/// A bump-map sampler that returns a tangent-space normal at a shading point.
/// Implemented by the single-texture <see cref="BumpMapTexture"/> and by the
/// mix-blended <see cref="MixBumpMapTexture"/>. The renderer treats both
/// uniformly via <c>ApplyBumpMap</c>.
///
/// <para>Returning <c>(0, 0, 1)</c> means "no perturbation". Results are
/// normalised and ready to be transformed by the surface TBN.</para>
/// </summary>
public interface IBumpMap
{
    /// <summary>
    /// Samples the bump field and returns a tangent-space normal.
    /// </summary>
    Vector3 SampleTangentNormal(float u, float v, Vector3 p, in BumpFrame frame, int seed);
}
