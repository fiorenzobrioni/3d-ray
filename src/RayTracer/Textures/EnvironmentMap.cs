using System.Numerics;

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

    private Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]);
    }
}
