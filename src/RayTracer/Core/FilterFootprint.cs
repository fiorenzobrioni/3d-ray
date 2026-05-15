using System.Numerics;

namespace RayTracer.Core;

/// <summary>
/// Per-shading-sample analytic filter footprint, derived from ray differentials
/// at the surface hit (PBRT §10.1). Tells texture lookups the area of the
/// texel they're meant to represent so they can analytically pre-integrate
/// (mipmap LOD, octave clamping, supersampling within footprint) instead of
/// point-sampling and aliasing.
///
/// <para>
/// <see cref="DPdx"/> and <see cref="DPdy"/> are the world-space (or
/// object-space, when the footprint was carried through a
/// <see cref="Geometry.Transform"/>) deltas between the primary hit point
/// and the two auxiliary hit points produced by the +x and +y differential
/// rays. Used by 3D procedural textures sampling on <c>rec.LocalPoint</c>
/// (noise, fBm, marble, wood, Worley) — their footprint is just
/// <c>max(|DPdx|, |DPdy|)</c> after scaling by the texture's frequency.
/// </para>
///
/// <para>
/// <see cref="DUdx"/>, <see cref="DVdx"/>, <see cref="DUdy"/>, <see cref="DVdy"/>
/// are the partial derivatives of the surface UV coordinates with respect to
/// screen-space pixel deltas. Used by image-based textures
/// (<see cref="Textures.ImageTexture"/>) for mipmap LOD selection and
/// anisotropic filtering, and by 2D procedurals (checker, brick, gradient).
/// </para>
///
/// <para>
/// <see cref="HasFootprint"/> distinguishes a valid footprint (camera primary
/// ray with differentials) from the default-zero footprint emitted by shadow
/// rays, NEE evaluation, BSDF bounces and any other code path that doesn't
/// carry differentials. Textures that override the footprint-aware overload
/// MUST check this flag and fall back to point sampling when it's false —
/// the default <see cref="Textures.ITexture.Value(float, float, Vector3, int, in FilterFootprint)"/>
/// implementation does this automatically.
/// </para>
/// </summary>
public readonly struct FilterFootprint
{
    public Vector3 DPdx { get; }
    public Vector3 DPdy { get; }
    public float DUdx { get; }
    public float DVdx { get; }
    public float DUdy { get; }
    public float DVdy { get; }
    public bool HasFootprint { get; }

    public FilterFootprint(Vector3 dPdx, Vector3 dPdy,
                           float dUdx, float dVdx,
                           float dUdy, float dVdy)
    {
        DPdx = dPdx;
        DPdy = dPdy;
        DUdx = dUdx;
        DVdx = dVdx;
        DUdy = dUdy;
        DVdy = dVdy;
        HasFootprint = true;
    }

    /// <summary>
    /// Returns the spectral-radius estimate of the world/object-space footprint
    /// for 3D procedurals: <c>max(|DPdx|, |DPdy|)</c>. Used to clamp Perlin
    /// octaves at Nyquist and to set the sampling rate for Worley supersampling.
    /// </summary>
    public float MaxWorldAxis()
    {
        float ax = DPdx.Length();
        float ay = DPdy.Length();
        return MathF.Max(ax, ay);
    }

    /// <summary>
    /// Returns the spectral-radius estimate of the UV footprint:
    /// <c>max(sqrt(DUdx²+DVdx²), sqrt(DUdy²+DVdy²))</c>. Used by 2D textures
    /// to drive mipmap LOD selection.
    /// </summary>
    public float MaxUvAxis()
    {
        float lx = MathF.Sqrt(DUdx * DUdx + DVdx * DVdx);
        float ly = MathF.Sqrt(DUdy * DUdy + DVdy * DVdy);
        return MathF.Max(lx, ly);
    }

    /// <summary>
    /// Returns the minor-axis length of the UV footprint, i.e. the smaller of
    /// the two screen-space axes after projection. Used by anisotropic
    /// filtering: the major axis sets the LOD and the minor axis sets the
    /// number of trilinear taps along that LOD.
    /// </summary>
    public float MinUvAxis()
    {
        float lx = MathF.Sqrt(DUdx * DUdx + DVdx * DVdx);
        float ly = MathF.Sqrt(DUdy * DUdy + DVdy * DVdy);
        return MathF.Min(lx, ly);
    }

    /// <summary>
    /// A scaled copy of the footprint — used by texture-coordinate transforms
    /// that multiply UV by a tiling factor or scale 3D positions before
    /// noise lookup.
    /// </summary>
    public FilterFootprint Scaled(float scale)
    {
        if (!HasFootprint) return this;
        return new FilterFootprint(
            DPdx * scale, DPdy * scale,
            DUdx * scale, DVdx * scale,
            DUdy * scale, DVdy * scale);
    }

    /// <summary>
    /// A footprint whose 3D component has been transformed by the given matrix.
    /// Used by <see cref="Geometry.Transform.Hit"/> to bring the footprint
    /// back into world space after the inner primitive has computed it in
    /// object space (or vice-versa). UV partials are unaffected — they live
    /// in the primitive's parametric space, which is invariant to spatial
    /// transforms.
    /// </summary>
    public FilterFootprint TransformedBy(Matrix4x4 m)
    {
        if (!HasFootprint) return this;
        Vector3 dpdxT = Vector3.TransformNormal(DPdx, m);
        Vector3 dpdyT = Vector3.TransformNormal(DPdy, m);
        return new FilterFootprint(dpdxT, dpdyT, DUdx, DVdx, DUdy, DVdy);
    }

    /// <summary>The "no footprint" sentinel returned for rays that aren't tracking differentials.</summary>
    public static FilterFootprint None => default;
}
