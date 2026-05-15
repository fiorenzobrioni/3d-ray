using RayTracer.Textures;

namespace RayTracer.Geometry.Subdivision;

/// <summary>
/// Scalar surface displacement parameters. Translates the YAML
/// <c>displacement: {...}</c> block on a mesh entity into engine-level
/// values consumed by <see cref="DisplacementEngine"/>.
///
/// <para>The displacement formula applied to every limit-surface vertex is
/// <c>v' = v + scale · (h − midlevel) · n_smooth</c>, where
/// <c>h = Rec.709 luminance(texture.Value(u, v, p))</c>. The smooth normal
/// <c>n_smooth</c> is the angle-weighted average of incident face normals
/// (Max 1999 — the production default of Blender, Maya and OpenSubdiv).</para>
///
/// <para>This is the canonical "scalar height-field" displacement of
/// Arnold (<c>displacementShader</c>, <c>disp_height</c>/<c>disp_zero_value</c>/
/// <c>disp_padding</c>), RenderMan (<c>PxrDisplace</c>'s <c>dispAmount</c>/
/// <c>dispMidpoint</c>/<c>dispBound</c>) and Cycles ("True Displacement" with
/// the height-only mode). Vector displacement (RGB → XYZ offset) is a
/// follow-up step in the DEVLOG roadmap and uses the same dispatch.</para>
/// </summary>
public readonly struct DisplacementOptions
{
    /// <summary>
    /// Inner height-field texture. Sampled per-vertex as
    /// <c>Rec.709 luminance(texture.Value(u, v, p))</c> — same convention as
    /// <see cref="BumpMapTexture"/>, so a single procedural can drive either
    /// channel and produce a matching look.
    /// </summary>
    public ITexture? Texture { get; init; }

    /// <summary>
    /// World-unit amplitude of the displacement (signed). The actual offset
    /// applied is <c>scale · (h − midlevel)</c>; values of <c>h</c> below
    /// <see cref="Midlevel"/> push the surface inward, values above push
    /// outward. 0 disables the displacement.
    /// </summary>
    public float Scale { get; init; }

    /// <summary>
    /// Reference luminance treated as "no displacement". Defaults to 0
    /// (matches RenderMan's <c>dispMidpoint</c> 0). 0.5 is the natural choice
    /// for 8-bit greyscale height maps where 128 means "flat".
    /// </summary>
    public float Midlevel { get; init; }

    /// <summary>
    /// Maximum expected displacement amplitude in world units. Used to pad
    /// the BVH leaf AABBs so any future shading-time bump or numerical
    /// drift on the displaced silhouette stays inside the box. Mirrors
    /// Arnold's <c>disp_padding</c> and RenderMan's <c>dispBound</c>.
    /// </summary>
    /// <remarks>
    /// In our eager-displacement pipeline the triangle vertices already
    /// reflect the post-displacement positions, so the bound is not strictly
    /// required for correctness — it functions as a safety margin and a
    /// validation hint: when the actually-applied displacement exceeds the
    /// bound the loader emits a warning so the user knows the value is
    /// under-set.
    /// </remarks>
    public float Bound { get; init; }

    /// <summary>
    /// Uniform UV multiplier stacked on top of any per-texture <c>uv_scale</c>.
    /// Identical to the <see cref="BumpMapTexture"/> field of the same name,
    /// so authors can dial the bump and the displacement together with the
    /// same number when sharing a procedural.
    /// </summary>
    public float UvScale { get; init; }

    /// <summary>True when both <see cref="Texture"/> is set and the scale is non-zero.</summary>
    public bool IsActive => Texture != null && Scale != 0f;

    public static DisplacementOptions Disabled => new()
    {
        Texture = null,
        Scale = 0f,
        Midlevel = 0f,
        Bound = 0f,
        UvScale = 1f,
    };
}
