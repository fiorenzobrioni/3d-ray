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
    // Lazy-computed average luminance. Negative sentinel = not yet computed.
    // Thread-safe in practice: read once from the single-threaded Renderer constructor,
    // before Parallel.For begins.
    private float _avgLuminance = -1f;

    // CDFs for Importance Sampling
    private float[] _margCdf = [];
    private float[][] _condCdf = [];

    // ── Mipmap pyramid (lazy) ────────────────────────────────────────────────
    // Stored as a list of (pixels, width, height) tuples; index 0 = original
    // resolution. Each successive level is a 2×2 box filter with sin(θ) row
    // weighting to keep solid-angle-weighted radiance correct (a uniform-area
    // box filter on equirect HDRIs would over-weight the polar regions). The
    // pyramid is built once on first call to SampleMip(); HDRIs that never
    // need it (no roughness lookup) pay no extra memory.
    private (float[] Pixels, int W, int H)[]? _mipChain;
    private readonly object _mipLock = new();

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
        // EXR safety: clamp negative values that some authoring tools (or DWA
        // compression rounding) can introduce. Negative HDRI samples explode
        // into NaN/black after Beer-Lambert and tone mapping; the spec allows
        // them, but no environment light has negative radiance.
        // We mutate the supplied buffer in place since it's expected to be
        // single-owner from the loader.
        for (int i = 0; i < pixels.Length; i++)
            if (pixels[i] < 0f || float.IsNaN(pixels[i]) || float.IsInfinity(pixels[i]))
                pixels[i] = MathF.Max(0f, pixels[i]);

        _pixels = pixels;
        _width = width;
        _height = height;
        _intensity = intensity;
        _rotationRad = rotationDeg * MathF.PI / 180f;
        BuildCdfs();
    }

    /// <summary>
    /// Number of texels in the wrapped equirect image (W·H).
    /// </summary>
    public int PixelCount => _width * _height;

    /// <summary>Image width in pixels.</summary>
    public int Width => _width;

    /// <summary>Image height in pixels.</summary>
    public int Height => _height;

    /// <summary>
    /// Returns a copy of the raw RGB pixel buffer (unscaled by intensity). Used
    /// by <see cref="HdriSunExtractor"/> to in-paint a brightness peak before
    /// re-wrapping the map for IBL.
    /// </summary>
    public float[] CopyPixels()
    {
        var copy = new float[_pixels.Length];
        Array.Copy(_pixels, copy, _pixels.Length);
        return copy;
    }

    /// <summary>Intensity multiplier supplied at construction.</summary>
    public float Intensity => _intensity;

    /// <summary>Y-axis rotation applied at lookup time, in radians.</summary>
    public float RotationRad => _rotationRad;

    /// <summary>
    /// Average luminance across all HDRI texels, intensity-scaled.
    /// Computed lazily on first access by iterating the pixel buffer.
    /// O(W×H) on first call, O(1) thereafter.
    ///
    /// Used by SkySettings.EstimatedAverageLuminance → EnvironmentLight.ApproximatePower()
    /// for deterministic scene classification (no PRNG).
    /// </summary>
    public float EstimatedAverageLuminance
    {
        get
        {
            if (_avgLuminance >= 0f) return _avgLuminance;
 
            float total = 0f;
            int pixelCount = _width * _height;
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 3;
                total += MathUtils.Luminance(
                    new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]));
            }
 
            _avgLuminance = (pixelCount > 0 ? total / pixelCount : 0f) * _intensity;
            return _avgLuminance;
        }
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

    /// <summary>
    /// Solid-angle PDF of <see cref="SampleDirection"/> at an arbitrary direction.
    /// Mirrors the sampling routine: maps direction → equirect pixel → pdfPixel
    /// from the marginal/conditional CDFs, then converts to solid angle via the
    /// pixel's own dω = sin θ · dθ · dφ. Used for MIS.
    /// </summary>
    public float PdfDirection(Vector3 direction)
    {
        Vector3 dir = Vector3.Normalize(direction);

        float phi = MathF.Atan2(dir.X, -dir.Z); // undo the -cos/sin pairing used in SampleDirection
        // SampleDirection builds dir = (cos θ sin φ, sin θ, -cos θ cos φ), so
        // atan2(dir.X, -dir.Z) recovers φ in (-π, π].
        float theta = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));

        // Apply the same rotation convention as the sampling routine (phi was
        // emitted with `phi -= _rotationRad` from the raw pixel angle, so to
        // look up by direction we add it back).
        phi += _rotationRad;

        // phi ∈ (-π, π] → u ∈ [0, 1); theta ∈ [-π/2, π/2] → v ∈ [0, 1]
        float u = (phi / (2f * MathF.PI)) + 0.5f;
        u -= MathF.Floor(u);
        float v = 0.5f - theta / MathF.PI;

        int x = Math.Clamp((int)(u * _width), 0, _width - 1);
        int y = Math.Clamp((int)(v * _height), 0, _height - 1);

        float pdfRow = (y == 0 ? _margCdf[0] : _margCdf[y] - _margCdf[y - 1]);
        float pdfCol = (x == 0 ? _condCdf[y][0] : _condCdf[y][x] - _condCdf[y][x - 1]);
        float pdfPixel = pdfRow * pdfCol;

        float dTheta = MathF.PI / _height;
        float dPhi = 2f * MathF.PI / _width;
        float sinColat = MathF.Cos(theta); // sin(colat) = cos(lat)
        float dW = sinColat * dTheta * dPhi;
        return dW > 1e-8f ? pdfPixel / dW : 0f;
    }

    private Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Mipmap pyramid — for glossy roughness-driven HDRI lookups
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bilinear-filtered lookup at an arbitrary mipmap level. <paramref name="lod"/>
    /// is in continuous mip units (0 = native resolution; each integer step halves
    /// the resolution). Trilinear interpolation between the two bracketing levels.
    ///
    /// <para>Use case: glossy reflections of the environment with materials whose
    /// roughness controls the BSDF lobe width. Mapping <c>lod ≈ roughness · maxLod</c>
    /// turns the prefiltered HDRI into an importance-correct approximation of the
    /// rough-IBL integral, eliminating fireflies from undersampled high-contrast
    /// HDRIs (Cycles "Glossy BSDF + HDRI" workflow, Arnold "Indirect Specular
    /// Roughness Clamp"). The mip chain is built lazily on first call —
    /// HDRIs that never need it pay no extra memory.</para>
    /// </summary>
    public Vector3 SampleMip(Vector3 direction, float lod)
    {
        EnsureMipmap();
        int maxLevel = _mipChain!.Length - 1;
        float lf = Math.Clamp(lod, 0f, maxLevel);
        int l0 = (int)lf;
        int l1 = Math.Min(l0 + 1, maxLevel);
        float t = lf - l0;
        Vector3 a = SampleLevel(direction, l0);
        Vector3 b = SampleLevel(direction, l1);
        return Vector3.Lerp(a, b, t);
    }

    /// <summary>Highest mip index available (log2 of the larger dimension, rounded down).</summary>
    public int MaxMipLevel
    {
        get
        {
            EnsureMipmap();
            return _mipChain!.Length - 1;
        }
    }

    private void EnsureMipmap()
    {
        if (_mipChain != null) return;
        lock (_mipLock)
        {
            if (_mipChain != null) return;

            // Number of levels: stop when either dimension reaches 1.
            int maxDim = Math.Max(_width, _height);
            int levels = 1 + (int)MathF.Floor(MathF.Log2(maxDim));

            var chain = new (float[], int, int)[levels];
            chain[0] = (_pixels, _width, _height);

            for (int L = 1; L < levels; L++)
            {
                var (prev, pw, ph) = chain[L - 1];
                int nw = Math.Max(1, pw / 2);
                int nh = Math.Max(1, ph / 2);
                var next = new float[nw * nh * 3];

                for (int y = 0; y < nh; y++)
                {
                    int y0 = Math.Min(y * 2, ph - 1);
                    int y1 = Math.Min(y0 + 1, ph - 1);
                    // Solid-angle weighting: rows near the poles have less
                    // physical area, so weighting their contribution by sin(θ)
                    // keeps energy-correct down-sampling on equirect images.
                    float thetaA = MathF.PI * (y0 + 0.5f) / ph;
                    float thetaB = MathF.PI * (y1 + 0.5f) / ph;
                    float wA = MathF.Sin(thetaA);
                    float wB = MathF.Sin(thetaB);
                    float wTotal = wA + wB;
                    if (wTotal < 1e-8f) wTotal = 1f;
                    wA /= wTotal;
                    wB /= wTotal;

                    for (int x = 0; x < nw; x++)
                    {
                        int x0 = Math.Min(x * 2, pw - 1);
                        int x1 = Math.Min(x0 + 1, pw - 1);
                        int dst = (y * nw + x) * 3;
                        int i00 = (y0 * pw + x0) * 3;
                        int i01 = (y0 * pw + x1) * 3;
                        int i10 = (y1 * pw + x0) * 3;
                        int i11 = (y1 * pw + x1) * 3;
                        for (int c = 0; c < 3; c++)
                        {
                            float top = 0.5f * (prev[i00 + c] + prev[i01 + c]);
                            float bot = 0.5f * (prev[i10 + c] + prev[i11 + c]);
                            next[dst + c] = wA * top + wB * bot;
                        }
                    }
                }
                chain[L] = (next, nw, nh);
            }
            _mipChain = chain;
        }
    }

    private Vector3 SampleLevel(Vector3 direction, int level)
    {
        var (px, w, h) = _mipChain![level];
        Vector3 dir = Vector3.Normalize(direction);
        float phi = MathF.Atan2(dir.X, dir.Z);
        float theta = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        phi += _rotationRad;
        const float invPi = 1f / MathF.PI;
        const float inv2Pi = 0.5f * invPi;
        float u = 0.5f + phi * inv2Pi;
        float v = 0.5f - theta * invPi;
        u -= MathF.Floor(u);
        float px_ = u * (w - 1);
        float py_ = v * (h - 1);
        int x0 = (int)px_;
        int y0 = (int)py_;
        int x1 = (x0 + 1) % w;
        int y1 = Math.Min(y0 + 1, h - 1);
        float fx = px_ - x0;
        float fy = py_ - y0;
        Vector3 c00 = new(px[(y0 * w + x0) * 3], px[(y0 * w + x0) * 3 + 1], px[(y0 * w + x0) * 3 + 2]);
        Vector3 c10 = new(px[(y0 * w + x1) * 3], px[(y0 * w + x1) * 3 + 1], px[(y0 * w + x1) * 3 + 2]);
        Vector3 c01 = new(px[(y1 * w + x0) * 3], px[(y1 * w + x0) * 3 + 1], px[(y1 * w + x0) * 3 + 2]);
        Vector3 c11 = new(px[(y1 * w + x1) * 3], px[(y1 * w + x1) * 3 + 1], px[(y1 * w + x1) * 3 + 2]);
        Vector3 top2 = Vector3.Lerp(c00, c10, fx);
        Vector3 bot2 = Vector3.Lerp(c01, c11, fx);
        return Vector3.Lerp(top2, bot2, fy) * _intensity;
    }
}
