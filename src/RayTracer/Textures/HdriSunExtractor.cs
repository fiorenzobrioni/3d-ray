using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Detects a bright peak (the sun) in an equirectangular HDRI, returns it as
/// a discrete cone source, and produces a "sun-removed" copy of the HDRI
/// suitable for IBL.
///
/// <para><b>Why.</b> A captured HDRI's sun is typically clamped — the on-set
/// sensor cannot resolve the full ~5 orders of magnitude between sun and sky
/// — yet still produces large variance when importance-sampled by the
/// environment CDF (one or two pixels carry &gt; 50% of the energy). Extracting
/// the sun into a dedicated <see cref="Lights.PhysicalSun"/> with analytical
/// cone sampling gives:
/// <list type="bullet">
///   <item><description>Clean, hard-edged shadows (single-pixel HDRI suns produce noisy multi-pixel penumbras).</description></item>
///   <item><description>~10× lower variance for sun-direction shading at the same sample count.</description></item>
///   <item><description>Decoupled clamp control (firefly clamp the sky body without dimming the sun).</description></item>
/// </list>
/// This is the same workflow as Arnold's <c>aiSkyDomeLight</c> sun extraction
/// and Cycles' "Sun Lamp" recommendation for HDRIs with a visible sun.</para>
///
/// <para><b>Algorithm.</b>
/// <list type="number">
///   <item><description>Threshold = solid-angle-weighted mean luminance × <c>thresholdFactor</c> (default 50).</description></item>
///   <item><description>Seed at the brightest texel; the sun is the texels above threshold within <see cref="MaxSunRadiusDeg"/> of it, so a second bright spot elsewhere is not averaged in.</description></item>
///   <item><description>Direction: their luminance-weighted mean direction (a vector mean, so a sun across the ±180° seam stays where it is). Half-angle: the farthest of them from it, plus half a texel, floored at the real solar disc.</description></item>
///   <item><description>In-paint: within the half-angle the ring average at twice it, fading back to the HDRI at twice the half-angle (distances are angles on the sphere).</description></item>
///   <item><description>The sun receives exactly the energy the in-paint took (each texel weighted by its solid angle), spread evenly over its disc: sun plus in-painted map integrate to the original map.</description></item>
/// </list></para>
/// </summary>
public static class HdriSunExtractor
{
    public record SunExtractionResult(
        Vector3 Direction,
        Vector3 Radiance,
        float HalfAngleDeg,
        float[] InpaintedPixels);

    /// <summary>
    /// Attempts to extract a sun peak. Returns <c>null</c> when no peak above
    /// threshold is found (the HDRI is overcast / studio).
    /// </summary>
    /// <summary>Largest sun the extractor accepts, around its brightest texel.</summary>
    public const float MaxSunRadiusDeg = 5f;

    public static SunExtractionResult? Extract(EnvironmentMap map, float thresholdFactor = 50f)
    {
        int w = map.Width;
        int h = map.Height;
        float[] src = map.CopyPixels();
        float[] orig = (float[])src.Clone();
        float intensity = map.Intensity;
        float rotation = map.RotationRad;

        float dPhi = 2f * MathF.PI / w, dTheta = MathF.PI / h;
        float Lat(int y) => (0.5f - (y + 0.5f) / h) * MathF.PI;
        float Lum(int idx) => 0.2126f * src[idx] + 0.7152f * src[idx + 1] + 0.0722f * src[idx + 2];
        Vector3 Dir(int x, int y) => EnvironmentMap.DirectionOf(((x + 0.5f) / w - 0.5f) * 2f * MathF.PI, Lat(y));

        // Solid-angle-weighted mean (so polar texels don't inflate it), and
        // the brightest texel.
        double weightedSum = 0, weightTotal = 0;
        float peak = 0f;
        int px = 0, py = 0;
        for (int y = 0; y < h; y++)
        {
            float dw = MathF.Cos(Lat(y));
            for (int x = 0; x < w; x++)
            {
                float L = Lum((y * w + x) * 3);
                weightedSum += L * dw;
                weightTotal += dw;
                if (L > peak) { peak = L; px = x; py = y; }
            }
        }
        if (weightTotal <= 0) return null;
        float threshold = (float)(weightedSum / weightTotal) * thresholdFactor;
        if (threshold <= 1e-6f || peak < threshold) return null;

        // The sun: texels above threshold near the brightest one.
        Vector3 seed = Dir(px, py);
        float cosMax = MathF.Cos(MathUtils.DegreesToRadians(MaxSunRadiusDeg));
        var (y0, y1) = RowsWithin(Lat(py), MathUtils.DegreesToRadians(MaxSunRadiusDeg) + dTheta, h);
        Vector3 sumDir = Vector3.Zero;
        for (int y = y0; y <= y1; y++)
            for (int x = 0; x < w; x++)
            {
                float L = Lum((y * w + x) * 3);
                if (L < threshold) continue;
                Vector3 d = Dir(x, y);
                if (Vector3.Dot(d, seed) < cosMax) continue;
                sumDir += L * d;
            }
        Vector3 centre = Vector3.Normalize(sumDir);

        float halfAngleRad = 0f;
        for (int y = y0; y <= y1; y++)
            for (int x = 0; x < w; x++)
            {
                if (Lum((y * w + x) * 3) < threshold) continue;
                Vector3 d = Dir(x, y);
                if (Vector3.Dot(d, seed) < cosMax) continue;
                halfAngleRad = MathF.Max(halfAngleRad, Angle(d, centre));
            }
        // Half a texel for the extent of the outermost texel; floor so a
        // single-texel sun maps to the real solar disc.
        halfAngleRad += 0.5f * dTheta;
        halfAngleRad = MathF.Max(halfAngleRad, MathUtils.DegreesToRadians(0.265f));
        float ringRad = 2f * halfAngleRad;

        // In-paint, and add up what it takes away.
        Vector3 ring = SampleRingAverage(orig, w, h, centre, ringRad);
        var (r0, r1) = RowsWithin(MathF.Asin(Math.Clamp(centre.Y, -1f, 1f)), ringRad + dTheta, h);
        Vector3 removed = Vector3.Zero;
        for (int y = r0; y <= r1; y++)
        {
            float dw = MathF.Cos(Lat(y)) * dTheta * dPhi;
            for (int x = 0; x < w; x++)
            {
                float dist = Angle(Dir(x, y), centre);
                if (dist >= ringRad) continue;
                // Pure ring colour inside the half-angle, fading back to the
                // HDRI between one and two half-angles.
                float t = MathF.Min(1f, (ringRad - dist) / halfAngleRad);
                int idx = (y * w + x) * 3;
                Vector3 before = new(src[idx], src[idx + 1], src[idx + 2]);
                Vector3 after = Vector3.Lerp(before, ring, t);
                src[idx] = after.X; src[idx + 1] = after.Y; src[idx + 2] = after.Z;
                removed += (before - after) * dw;
            }
        }

        float solidAngle = 2f * MathF.PI * (1f - MathF.Cos(halfAngleRad));
        Vector3 sunRadiance = Vector3.Max(Vector3.Zero, removed * intensity / MathF.Max(solidAngle, 1e-12f));

        // Direction in sky-local coordinates: Sample adds the rotation to φ.
        float phiC = MathF.Atan2(centre.X, centre.Z) - rotation;
        Vector3 sunDir = EnvironmentMap.DirectionOf(phiC, MathF.Asin(Math.Clamp(centre.Y, -1f, 1f)));

        return new SunExtractionResult(Vector3.Normalize(sunDir), sunRadiance,
                                       MathUtils.RadiansToDegrees(halfAngleRad), src);
    }

    private static float Angle(Vector3 a, Vector3 b)
    {
        // atan2 of |a×b| and a·b: accurate for the small angles of a sun disc.
        return MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));
    }

    /// <summary>Rows whose latitude lies within <paramref name="radius"/> of <paramref name="lat"/>.</summary>
    private static (int Y0, int Y1) RowsWithin(float lat, float radius, int h)
    {
        float vTop = 0.5f - MathF.Min(MathF.PI / 2f, lat + radius) / MathF.PI;
        float vBot = 0.5f - MathF.Max(-MathF.PI / 2f, lat - radius) / MathF.PI;
        return (Math.Clamp((int)MathF.Floor(vTop * h), 0, h - 1), Math.Clamp((int)MathF.Floor(vBot * h), 0, h - 1));
    }

    private static Vector3 SampleRingAverage(float[] px, int w, int h, Vector3 centre, float radRad)
    {
        const int samples = 32;
        MathUtils.OrthonormalBasis(centre, out Vector3 e1, out Vector3 e2);
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            float a = (i + 0.5f) * (2f * MathF.PI / samples);
            Vector3 d = MathF.Cos(radRad) * centre + MathF.Sin(radRad) * (MathF.Cos(a) * e1 + MathF.Sin(a) * e2);
            float u = 0.5f + MathF.Atan2(d.X, d.Z) / (2f * MathF.PI);
            float v = 0.5f - MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) / MathF.PI;
            int xs = Math.Clamp((int)((u - MathF.Floor(u)) * w), 0, w - 1);
            int ys = Math.Clamp((int)(v * h), 0, h - 1);
            int idx = (ys * w + xs) * 3;
            sum += new Vector3(px[idx], px[idx + 1], px[idx + 2]);
        }
        return sum / samples;
    }
}
