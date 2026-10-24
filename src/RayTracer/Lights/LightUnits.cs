using System.Numerics;
using RayTracer.Core;
using RayTracer.Rendering;

namespace RayTracer.Lights;

/// <summary>
/// Photometric units for lights and the physical camera exposure
/// (PIANO-REVIEW.md §3.14, §3.34).
///
/// <para>
/// A scene that writes its lights in photometric units renders in nits: one
/// scene unit of radiance is one candela per square metre (lengths in metres,
/// <c>meters_per_unit</c> otherwise). A light's colour then only tints: it is
/// divided by its own luminance, so a lamp of 800 lm emits 800 lm whatever its
/// colour. Watts are radiant watts at the peak efficacy, 683 lm/W. The
/// camera's exposure (ISO, shutter, f-number, or EV100 directly) maps those
/// nits to the display, as a light meter does. The unitless <c>intensity</c>
/// keeps its old meaning, so existing scenes are unchanged.
/// </para>
/// </summary>
public static class LightUnits
{
    /// <summary>Luminous efficacy of one radiant watt at 555 nm (lm/W).</summary>
    public const float LumensPerWatt = 683f;

    /// <summary>
    /// Linear Rec.709 colour of a black body at <paramref name="kelvin"/>
    /// (the Planckian locus, 1667-25000 K), with luminance 1. Below about
    /// 1900 K the locus leaves the Rec.709 gamut: the colour is the nearest
    /// one inside (negative components clipped), luminance renormalised.
    /// </summary>
    public static Vector3 Blackbody(float kelvin)
    {
        var (x, y) = WhiteBalance.Planckian(Math.Clamp(kelvin, WhiteBalance.MinKelvin, WhiteBalance.MaxKelvin));
        var xyz = new Vector3((float)(x / y), 1f, (float)((1 - x - y) / y));
        Vector3 rgb = Vector3.Max(Vector3.Zero, ColorSpaces.XyzToRec709.Apply(xyz));
        float lum = MathUtils.Luminance(rgb);
        return lum > 0f ? rgb / lum : Vector3.One;
    }

    /// <summary>
    /// A colour divided by its luminance (white when it has none), times the
    /// black body of <paramref name="kelvin"/> when given: the tint of a light
    /// whose amount is set by a photometric quantity.
    /// </summary>
    public static Vector3 UnitTint(Vector3 color, float? kelvin)
    {
        if (kelvin is float k) color *= Blackbody(k);
        float lum = MathUtils.Luminance(color);
        return lum > 0f ? color / lum : Vector3.One;
    }

    /// <summary>
    /// EV100 of a camera setting: log2(N² / t) − log2(ISO / 100).
    /// </summary>
    public static float Ev100(float fStop, float shutterSeconds, float iso)
        => MathF.Log2(fStop * fStop / shutterSeconds) - MathF.Log2(iso / 100f);

    /// <summary>
    /// The exposure, in stops, that maps scene nits to display values for a
    /// camera at <paramref name="ev100"/>: the saturation-based ISO
    /// definition, a luminance of 1.2 × 2^EV100 reaching full scale (as in
    /// Lagarde and de Rousiers, "Moving Frostbite to PBR", 2014).
    /// </summary>
    public static float ExposureEvFromEv100(float ev100) => -ev100 - MathF.Log2(1.2f);
}
