using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// An HDR environment map for Image-Based Lighting (IBL).
///
/// Stores an equirectangular (latitude-longitude) HDR image and samples it
/// by ray direction using bilinear filtering. The result is linear HDR
/// radiance — values can exceed 1.0, which is the whole point.
///
/// Used by SkySettings in HDRI mode: when a ray escapes the scene, the
/// environment map is sampled to provide the sky/environment radiance.
/// This produces realistic reflections, refractions, and global illumination
/// from real-world lighting captured in the HDR photograph.
///
/// Features:
///   - Bilinear filtering for smooth sampling
///   - Y-axis rotation for aligning the environment to the scene
///   - Intensity multiplier for exposure control
/// </summary>
public class EnvironmentMap
{
    private readonly float[] _pixels;  // Flat RGB: [r0, g0, b0, r1, g1, b1, ...]
    private readonly int _width;
    private readonly int _height;
    private readonly float _intensity;
    private readonly float _rotationRad; // Y-axis rotation in radians

    // CDFs for Importance Sampling
    private float[] _margCdf;
    private float[][] _condCdf;

    /// <summary>
    /// Creates an environment map from pre-loaded HDR pixel data.
    /// </summary>
    /// <param name="pixels">Flat float RGB array from HdrLoader.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="intensity">Brightness multiplier (1.0 = original exposure).</param>
    /// <param name="rotationDeg">Y-axis rotation in degrees (0–360).</param>
    public EnvironmentMap(float[] pixels, int width, int height,
                          float intensity = 1f, float rotationDeg = 0f)
    {
        _pixels = pixels;
        _width = width;
        _height = height;
        _intensity = intensity;
        _rotationRad = rotationDeg * MathF.PI / 180f;
        BuildCdfs();
    }

    private void BuildCdfs()
    {
        float[] rowWeights = new float[_height];
        _condCdf = new float[_height][];
        
        for (int y = 0; y < _height; y++)
        {
            _condCdf[y] = new float[_width];
            float rowSum = 0f;
            // sin(theta) factor for solid angle weighting. theta is 0 at zenith, pi at bottom.
            float theta = MathF.PI * (y + 0.5f) / _height;
            float sinTheta = MathF.Sin(theta);

            for (int x = 0; x < _width; x++)
            {
                int idx = (y * _width + x) * 3;
                float lum = 0.2126f * _pixels[idx] + 0.7152f * _pixels[idx+1] + 0.0722f * _pixels[idx+2];
                rowSum += lum * sinTheta;
                _condCdf[y][x] = rowSum;
            }

            if (rowSum > 0f)
            {
                for (int x = 0; x < _width; x++) _condCdf[y][x] /= rowSum;
            }
            rowWeights[y] = rowSum;
        }

        _margCdf = new float[_height];
        float totalWeight = 0f;
        for (int y = 0; y < _height; y++)
        {
            totalWeight += rowWeights[y];
            _margCdf[y] = totalWeight;
        }

        if (totalWeight > 0f)
        {
            for (int y = 0; y < _height; y++) _margCdf[y] /= totalWeight;
        }
        else
        {
            // Fallback for completely black HDRI
            for (int y = 0; y < _height; y++) _margCdf[y] = (y + 1f) / _height;
        }
    }

    /// <summary>
    /// Samples the environment map for a given ray direction.
    /// Uses equirectangular (lat/long) mapping with bilinear filtering.
    /// </summary>
    public Vector3 Sample(Vector3 direction)
    {
        Vector3 dir = Vector3.Normalize(direction);

        // ── Spherical coordinates ───────────────────────────────────────
        // atan2(x, z) gives the azimuthal angle (longitude)
        // asin(y) gives the polar angle (latitude)
        float phi = MathF.Atan2(dir.X, dir.Z); // -π to π
        float theta = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)); // -π/2 to π/2

        // Apply Y-axis rotation (shifts the environment around the scene)
        phi += _rotationRad;

        // ── Map to UV [0, 1] ────────────────────────────────────────────
        // U: longitude mapped to [0, 1]  (phi: -π → 0, +π → 1)
        // V: latitude mapped to [0, 1]   (theta: -π/2 → 1, +π/2 → 0)
        const float invPi = 1f / MathF.PI;
        const float inv2Pi = 0.5f * invPi;

        float u = 0.5f + phi * inv2Pi; // [0, 1]
        float v = 0.5f - theta * invPi; // [0, 1] (top of image = zenith)

        // Wrap U for rotation overflow
        u -= MathF.Floor(u);

        // ── Bilinear filtering ──────────────────────────────────────────
        float px = u * (_width - 1);
        float py = v * (_height - 1);

        int x0 = (int)px;
        int y0 = (int)py;
        int x1 = (x0 + 1) % _width; // wrap horizontally (equirectangular seam)
        int y1 = Math.Min(y0 + 1, _height - 1);

        float fx = px - x0;
        float fy = py - y0;

        Vector3 c00 = GetPixel(x0, y0);
        Vector3 c10 = GetPixel(x1, y0);
        Vector3 c01 = GetPixel(x0, y1);
        Vector3 c11 = GetPixel(x1, y1);

        Vector3 top = Vector3.Lerp(c00, c10, fx);
        Vector3 bot = Vector3.Lerp(c01, c11, fx);
        Vector3 color = Vector3.Lerp(top, bot, fy);

        return color * _intensity;
    }

    /// <summary>
    /// Samples a random direction based on the HDRI luminance CDFs.
    /// </summary>
    /// <returns>The sampled direction, and the PDF (probability density function) of picking that direction.</returns>
    public (Vector3 Direction, float Pdf) SampleDirection()
    {
        // 1. Sample row (marginal CDF)
        float r1 = MathUtils.RandomFloat();
        int y = Array.BinarySearch(_margCdf, r1);
        if (y < 0) y = ~y;
        y = Math.Clamp(y, 0, _height - 1);

        // 2. Sample column (conditional CDF)
        float r2 = MathUtils.RandomFloat();
        int x = Array.BinarySearch(_condCdf[y], r2);
        if (x < 0) x = ~x;
        x = Math.Clamp(x, 0, _width - 1);

        // Compute PDF
        float pdfRow = (y == 0 ? _margCdf[0] : _margCdf[y] - _margCdf[y - 1]);
        float pdfCol = (x == 0 ? _condCdf[y][0] : _condCdf[y][x] - _condCdf[y][x - 1]);
        float pdfPixel = pdfRow * pdfCol; // Probability of picking this pixel

        // Convert UV to angles
        float u = (x + 0.5f) / _width;
        float v = (y + 0.5f) / _height;

        float phi = (u - 0.5f) * 2f * MathF.PI; // -π to π
        // Remove rotation applied during lookup
        phi -= _rotationRad;
        
        float theta = (0.5f - v) * MathF.PI; // π/2 (zenith) to -π/2 (bottom)

        Vector3 dir = new Vector3(
            MathF.Cos(theta) * MathF.Sin(phi),
            MathF.Sin(theta),
            -MathF.Cos(theta) * MathF.Cos(phi)
        );

        // Solid angle of one pixel: dW = sin(theta) * dTheta * dPhi
        // PDF with respect to solid angle: p(dir) = p(pixel) / dW
        float dTheta = MathF.PI / _height;
        float dPhi = 2f * MathF.PI / _width;
        float sinThetaColatitude = MathF.Cos(theta); // Because theta is latitude here, colatitude goes from 0 to pi. cos(lat) = sin(colat)
        
        float dW = sinThetaColatitude * dTheta * dPhi;
        float pdfSolidAngle = dW > 1e-8f ? pdfPixel / dW : 0f;

        return (Vector3.Normalize(dir), pdfSolidAngle);
    }

    private Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]);
    }
}
