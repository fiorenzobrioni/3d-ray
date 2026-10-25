namespace RayTracer.Textures;

/// <summary>
/// How the values stored in an image relate to linear light (PIANO-REVIEW.md
/// §3.3). In YAML: <c>color_space: srgb | linear | raw</c>; without it a colour
/// input reads its image as sRGB and a data input (roughness, metallic,
/// masks, heights, displacement) as raw.
/// </summary>
public enum TextureColorSpace
{
    /// <summary>sRGB-encoded colour (8-bit photographs, painted albedo maps).</summary>
    Srgb,
    /// <summary>Values used as stored: linear colour or non-colour data.</summary>
    Linear,
}
