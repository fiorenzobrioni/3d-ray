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
///   <item><description>Compute mean luminance and a threshold = <c>mean × thresholdFactor</c> (default 50).</description></item>
///   <item><description>Find pixels exceeding the threshold. Compute their luminance-weighted centroid in spherical coords.</description></item>
///   <item><description>Estimate disc half-angle from the bbox of those pixels.</description></item>
///   <item><description>Re-paint a circular neighbourhood of radius 2× the sun radius with the ring-averaged HDRI value (simple in-paint).</description></item>
///   <item><description>Return both the sun parameters and the in-painted pixel buffer.</description></item>
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
    public static SunExtractionResult? Extract(EnvironmentMap map, float thresholdFactor = 50f)
    {
        int w = map.Width;
        int h = map.Height;
        float[] src = map.CopyPixels();
        float intensity = map.Intensity;
        float rotation = map.RotationRad;

        // Solid-angle-weighted mean (so polar pixels don't inflate it).
        double weightedSum = 0;
        double weightTotal = 0;
        for (int y = 0; y < h; y++)
        {
            float theta = MathF.PI * (y + 0.5f) / h;
            float sinTheta = MathF.Sin(theta);
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 3;
                float L = 0.2126f * src[idx] + 0.7152f * src[idx + 1] + 0.0722f * src[idx + 2];
                weightedSum += L * sinTheta;
                weightTotal += sinTheta;
            }
        }
        if (weightTotal <= 0) return null;
        float mean = (float)(weightedSum / weightTotal);
        float threshold = mean * thresholdFactor;
        if (threshold <= 1e-6f) return null;

        // Pass 2 — find peaks. Track centroid and bbox.
        double sumPhi = 0, sumTheta = 0, sumWeight = 0;
        int peakMinX = int.MaxValue, peakMaxX = int.MinValue;
        int peakMinY = int.MaxValue, peakMaxY = int.MinValue;
        Vector3 colorSum = Vector3.Zero;
        int peakCount = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 3;
                float L = 0.2126f * src[idx] + 0.7152f * src[idx + 1] + 0.0722f * src[idx + 2];
                if (L < threshold) continue;
                float phi   = ((x + 0.5f) / w - 0.5f) * 2f * MathF.PI;
                float theta = (0.5f - (y + 0.5f) / h) * MathF.PI;  // latitude
                sumPhi += phi * L;
                sumTheta += theta * L;
                sumWeight += L;
                colorSum += new Vector3(src[idx], src[idx + 1], src[idx + 2]);
                peakCount++;
                if (x < peakMinX) peakMinX = x;
                if (x > peakMaxX) peakMaxX = x;
                if (y < peakMinY) peakMinY = y;
                if (y > peakMaxY) peakMaxY = y;
            }
        }
        if (peakCount == 0 || sumWeight <= 0) return null;

        float phiC   = (float)(sumPhi / sumWeight);
        float thetaC = (float)(sumTheta / sumWeight);
        // Undo rotation so the returned direction is in sky-local coords.
        phiC -= rotation;

        Vector3 sunDir = new(
            MathF.Cos(thetaC) * MathF.Sin(phiC),
            MathF.Sin(thetaC),
            -MathF.Cos(thetaC) * MathF.Cos(phiC));
        sunDir = Vector3.Normalize(sunDir);

        // Estimate half-angle from bbox: take max of horizontal & vertical
        // extents in radians, divide by 2.
        float dPhi   = (peakMaxX - peakMinX + 1) * (2f * MathF.PI / w);
        float dTheta = (peakMaxY - peakMinY + 1) * (MathF.PI / h);
        float halfAngleRad = 0.5f * MathF.Max(dPhi, dTheta);
        // Floor so a single-pixel sun maps to the real solar disc.
        halfAngleRad = MathF.Max(halfAngleRad, MathUtils.DegreesToRadians(0.265f));
        float halfAngleDeg = MathUtils.RadiansToDegrees(halfAngleRad);

        // Total energy in sun region (× intensity); convert to peak radiance
        // by dividing by the disc's solid angle.
        float solidAngle = 2f * MathF.PI * (1f - MathF.Cos(halfAngleRad));
        // The integrated energy over the bright pixels ≈ Σ L × dω. We
        // approximate dω = sin(colat) × dθ × dφ pixel-uniform; sum colorSum
        // and weight by pixel solid angle at centroid latitude.
        float sinColatC = MathF.Cos(thetaC);  // sin(colatitude) = cos(latitude)
        float dPhiPx   = 2f * MathF.PI / w;
        float dThetaPx = MathF.PI / h;
        float pixSolidAngle = MathF.Max(1e-8f, sinColatC * dPhiPx * dThetaPx);
        Vector3 totalEnergy = colorSum * pixSolidAngle * intensity;
        Vector3 sunRadiance = totalEnergy / MathF.Max(solidAngle, 1e-8f);

        // In-paint: replace sun pixels with a luminance-similar ring average.
        // Sample a ring at 2× the half-angle radius.
        float ringRad = 2f * halfAngleRad;
        Vector3 ring = SampleRingAverage(src, w, h, phiC + rotation, thetaC, ringRad);
        int padX = (int)MathF.Ceiling(halfAngleRad * w / (2f * MathF.PI)) + 1;
        int padY = (int)MathF.Ceiling(halfAngleRad * h / MathF.PI) + 1;
        int cx = ((peakMinX + peakMaxX) >> 1);
        int cy = ((peakMinY + peakMaxY) >> 1);
        for (int dy = -padY; dy <= padY; dy++)
        {
            int yy = cy + dy;
            if (yy < 0 || yy >= h) continue;
            for (int dx = -padX; dx <= padX; dx++)
            {
                int xx = ((cx + dx) % w + w) % w;
                int idx = (yy * w + xx) * 3;
                // Soft falloff: pixels inside the half-angle get pure ring colour,
                // pixels in the 1× → 2× buffer fade back to the original HDRI.
                float pixDistRad = PixelAngularDistance(xx, yy, cx, cy, w, h);
                float t = MathF.Max(0f, MathF.Min(1f, (ringRad - pixDistRad) / halfAngleRad));
                src[idx]     = src[idx]     * (1f - t) + ring.X * t;
                src[idx + 1] = src[idx + 1] * (1f - t) + ring.Y * t;
                src[idx + 2] = src[idx + 2] * (1f - t) + ring.Z * t;
            }
        }

        return new SunExtractionResult(sunDir, sunRadiance, halfAngleDeg, src);
    }

    private static Vector3 SampleRingAverage(float[] px, int w, int h, float phi, float theta, float radRad)
    {
        const int samples = 32;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            float a = (i + 0.5f) * (2f * MathF.PI / samples);
            float dx = MathF.Cos(a) * radRad;
            float dy = MathF.Sin(a) * radRad;
            float pPhi   = phi + dx;
            float pTheta = theta + dy;
            float u = 0.5f + pPhi / (2f * MathF.PI);
            float v = 0.5f - pTheta / MathF.PI;
            u -= MathF.Floor(u);
            v = Math.Clamp(v, 0f, 0.999f);
            int xs = Math.Clamp((int)(u * w), 0, w - 1);
            int ys = Math.Clamp((int)(v * h), 0, h - 1);
            int idx = (ys * w + xs) * 3;
            sum += new Vector3(px[idx], px[idx + 1], px[idx + 2]);
        }
        return sum / samples;
    }

    private static float PixelAngularDistance(int xa, int ya, int xb, int yb, int w, int h)
    {
        float dx = (xa - xb) * (2f * MathF.PI / w);
        float dy = (ya - yb) * (MathF.PI / h);
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
