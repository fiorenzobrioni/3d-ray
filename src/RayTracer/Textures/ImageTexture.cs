using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RayTracer.Textures;

/// <summary>
/// A texture that samples color from an image file. Without ray differentials
/// the lookup is straight bilinear; with differentials it uses a mipmap pyramid
/// + anisotropic (ratio-trilinear) filtering — the GPU-standard "max-anisotropy
/// 8x" formulation that Arnold, RenderMan and Cycles all converge on, derived
/// from Heckbert's EWA framework simplified for production cost.
///
/// <para>
/// The mip pyramid is built in the constructor via repeated 2×2 box averaging
/// in linear-light space (sRGB → linear is done once at load). At lookup time
/// the major-axis footprint length picks the LOD and the minor-axis sets the
/// number of trilinear taps along the elongation direction — this is exactly
/// what's needed to keep walls/floors sharp at grazing angles without the
/// over-blurring you get from isotropic trilinear at the same LOD.
/// </para>
///
/// UV coordinates are expected in [0,1] range (as produced by the UV mapping
/// of all primitives). Values outside [0,1] are wrapped via frac() for
/// seamless tiling.
///
/// Supports all formats handled by ImageSharp (PNG, JPEG, BMP, GIF, TIFF,
/// WebP), read at 16 bits per channel, plus Radiance .hdr and OpenEXR.
///
/// In YAML:
///   texture:
///     type: "image"
///     path: "textures/brick_wall.png"      # Relative to the scene YAML file
///     scale: [1, 1]                        # Optional UV scale (tiling)
/// </summary>
public class ImageTexture : ITexture
{
    // Mip pyramid. Level 0 is full resolution, each subsequent level is half
    // size (rounded up). Last level is 1×1. Each level is stored as flat
    // RGB float array in linear-light space.
    private readonly float[][] _mips;
    private readonly int[] _mipWidths;
    private readonly int[] _mipHeights;
    private readonly int _mipCount;
    private readonly float _scaleU;
    private readonly float _scaleV;

    // GPU-style anisotropy cap. Beyond this the filter ramps the LOD up so the
    // major axis stays inside the budget; in practice 8× matches what Arnold
    // and RenderMan ship as default.
    private const int MaxAnisotropy = 8;

    /// <summary>The colour space the texture was decoded from.</summary>
    public TextureColorSpace ColorSpace { get; }

    // sRGB→linear decode table over the 16-bit code range (images are read at
    // 16 bits per channel, so 8-bit files land on every 257th entry exactly).
    // The exact piecewise sRGB curve, not a 2.2 power: the power law darkens
    // the shadows (byte 10 decodes to 8.0e-4 instead of 3.0e-3, §3.4).
    private static readonly Lazy<float[]> SrgbDecodeLut = new(() =>
    {
        var lut = new float[65536];
        for (int i = 0; i < lut.Length; i++)
            lut[i] = RayTracer.Rendering.Srgb.Decode(i / 65535f);
        return lut;
    });

    /// <summary>
    /// Loads an image as a texture. <paramref name="colorSpace"/> says how the
    /// stored values relate to linear light (PIANO-REVIEW.md §3.3): colour
    /// images are sRGB-encoded, data maps (roughness, metallic, masks,
    /// heights) are stored as they are and must not be decoded. Radiance
    /// <c>.hdr</c> and OpenEXR files hold linear values whatever is asked.
    /// </summary>
    public ImageTexture(string imagePath, float scaleU = 1f, float scaleV = 1f,
                        TextureColorSpace colorSpace = TextureColorSpace.Srgb)
    {
        _scaleU = scaleU;
        _scaleV = scaleV;

        float[] level0;
        int w0, h0;
        string ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is ".hdr" or ".pic" or ".exr")
        {
            // A colour stays non-negative; a data map keeps its sign (a
            // displacement EXR moves the surface both ways).
            (level0, w0, h0) = ext == ".exr"
                ? ExrLoader.Load(imagePath, clampNegative: colorSpace == TextureColorSpace.Srgb)
                : HdrLoader.Load(imagePath);
            ColorSpace = TextureColorSpace.Linear;
        }
        else
        {
            ColorSpace = colorSpace;
            float[]? decode = colorSpace == TextureColorSpace.Srgb ? SrgbDecodeLut.Value : null;
            const float inv65535 = 1f / 65535f;
            float[] pixels;
            int w, h;
            // 16 bits per channel: a 16-bit PNG keeps its precision (an 8-bit
            // read quantised heights and roughness to 256 steps).
            using (var image = Image.Load<Rgba64>(imagePath))
            {
                w = image.Width;
                h = image.Height;
                pixels = new float[w * h * 3];
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < h; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        int baseIdx = y * w * 3;
                        for (int x = 0; x < w; x++)
                        {
                            var px = row[x];
                            int idx = baseIdx + x * 3;
                            if (decode != null)
                            {
                                pixels[idx]     = decode[px.R];
                                pixels[idx + 1] = decode[px.G];
                                pixels[idx + 2] = decode[px.B];
                            }
                            else
                            {
                                pixels[idx]     = px.R * inv65535;
                                pixels[idx + 1] = px.G * inv65535;
                                pixels[idx + 2] = px.B * inv65535;
                            }
                        }
                    }
                });
            }
            level0 = pixels; w0 = w; h0 = h;
        }

        // ── Build mip pyramid (box filter, linear space, stops at 1×1) ──────
        // Each level halves the size, rounding up, and every texel averages
        // exactly the area of the level below it covers. An odd size then
        // weights its neighbours fractionally instead of dropping the last
        // row or column, so every level keeps the image's mean and its texel
        // centres line up with the level below.
        int mipCount = 1 + (int)MathF.Ceiling(MathF.Log2(MathF.Max(w0, h0)));
        _mips = new float[mipCount][];
        _mipWidths = new int[mipCount];
        _mipHeights = new int[mipCount];
        _mipWidths[0] = w0; _mipHeights[0] = h0; _mips[0] = level0;

        for (int m = 1; m < mipCount; m++)
        {
            int wPrev = _mipWidths[m - 1];
            int hPrev = _mipHeights[m - 1];
            int wCur = (wPrev + 1) / 2;
            int hCur = (hPrev + 1) / 2;
            float[] rows = Downsample(_mips[m - 1], wPrev, hPrev, wCur, horizontal: true);
            _mips[m] = Downsample(rows, wCur, hPrev, hCur, horizontal: false);
            _mipWidths[m] = wCur; _mipHeights[m] = hCur;
        }
        _mipCount = mipCount;
    }

    /// <summary>
    /// Box-filters one axis from <c>n</c> to <c>nOut</c> texels: output texel
    /// <c>i</c> averages input interval <c>[i·n/nOut, (i+1)·n/nOut)</c>, each
    /// input texel weighted by its overlap.
    /// </summary>
    private static float[] Downsample(float[] src, int w, int h, int nOut, bool horizontal)
    {
        int n = horizontal ? w : h;
        int outW = horizontal ? nOut : w;
        int outH = horizontal ? h : nOut;
        var dst = new float[outW * outH * 3];
        if (n == nOut)
        {
            Array.Copy(src, dst, dst.Length);
            return dst;
        }
        float scale = (float)n / nOut;
        float invScale = 1f / scale;
        int lines = horizontal ? h : w;
        for (int i = 0; i < nOut; i++)
        {
            float a = i * scale, b = (i + 1) * scale;
            int k0 = (int)MathF.Floor(a);
            int k1 = Math.Min((int)MathF.Ceiling(b), n);
            for (int k = k0; k < k1; k++)
            {
                float wgt = (MathF.Min(b, k + 1) - MathF.Max(a, k)) * invScale;
                if (wgt <= 0f) continue;
                for (int line = 0; line < lines; line++)
                {
                    int si = horizontal ? (line * w + k) * 3 : (k * w + line) * 3;
                    int di = horizontal ? (line * outW + i) * 3 : (i * outW + line) * 3;
                    dst[di]     += wgt * src[si];
                    dst[di + 1] += wgt * src[si + 1];
                    dst[di + 2] += wgt * src[si + 2];
                }
            }
        }
        return dst;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        u *= _scaleU;
        v *= _scaleV;
        u = u - MathF.Floor(u);
        v = v - MathF.Floor(v);
        v = 1f - v;
        return SampleBilinear(u, v, 0);
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint)
    {
        if (!footprint.HasFootprint || _mipCount <= 1) return Value(u, v, p, objectSeed);

        // ── Anisotropic filtering: ratio-trilinear (GPU industry-standard) ─
        // The footprint in UV space is an ellipse spanned by (dudx, dvdx) and
        // (dudy, dvdy). Pick the longer-axis vector as the "major axis" and
        // the shorter as the "minor axis". The minor axis length sets the
        // LOD; the major axis length / minor axis length sets the number of
        // trilinear samples along the major-axis direction.
        //
        // This is equivalent to Heckbert's EWA at a fixed Gaussian kernel
        // but with constant-time complexity (≤ MaxAnisotropy samples) and
        // is what every shipping GPU and pro renderer actually uses (Arnold's
        // "anisotropic", Cycles' "EWA" mode, RenderMan's "AnisoFilter").
        float dudx = footprint.DUdx * _scaleU;
        float dvdx = footprint.DVdx * _scaleV;
        float dudy = footprint.DUdy * _scaleU;
        float dvdy = footprint.DVdy * _scaleV;

        // Axis lengths in texels of level 0, so a non-square image picks its
        // level from the texels the footprint actually covers on each axis.
        float w0 = _mipWidths[0], h0 = _mipHeights[0];
        float lenX = MathF.Sqrt(dudx * w0 * dudx * w0 + dvdx * h0 * dvdx * h0);
        float lenY = MathF.Sqrt(dudy * w0 * dudy * w0 + dvdy * h0 * dvdy * h0);

        float minor = MathF.Min(lenX, lenY);
        float major = MathF.Max(lenX, lenY);
        if (major <= 0f) return Value(u, v, p, objectSeed);

        // Clamp anisotropy ratio — raise minor to keep major/minor ≤ MaxAnisotropy.
        // This trades a sliver of aliasing along the minor axis at extreme
        // angles for a bounded sample count, exactly as GPUs do.
        if (minor * MaxAnisotropy < major) minor = major / MaxAnisotropy;

        // LOD = log2(minor axis in texels). Bigger footprint = higher level
        // (more blur).
        float lod = MathF.Log2(MathF.Max(minor, 1f));
        lod = Math.Clamp(lod, 0f, _mipCount - 1);

        int samples = (int)MathF.Ceiling(major / minor);
        samples = Math.Clamp(samples, 1, MaxAnisotropy);

        // The major-axis direction in UV space — used to step along the
        // elongated footprint. Determined by which of (dudx,dvdx) or
        // (dudy,dvdy) was longer.
        float majDu, majDv;
        if (lenX >= lenY) { majDu = dudx; majDv = dvdx; }
        else              { majDu = dudy; majDv = dvdy; }

        // Centre UV (post-scale, pre-wrap).
        float ucen = u * _scaleU;
        float vcen = v * _scaleV;

        Vector3 accum = Vector3.Zero;
        float invSamples = 1f / samples;
        for (int i = 0; i < samples; i++)
        {
            // Stratified offsets in [-0.5, 0.5] along the major axis.
            float t = (i + 0.5f) * invSamples - 0.5f;
            float uu = ucen + t * majDu;
            float vv = vcen + t * majDv;
            uu = uu - MathF.Floor(uu);
            vv = vv - MathF.Floor(vv);
            vv = 1f - vv;
            accum += SampleTrilinear(uu, vv, lod);
        }
        return accum * invSamples;
    }

    private Vector3 SampleTrilinear(float u, float v, float lod)
    {
        int lo = (int)MathF.Floor(lod);
        int hi = Math.Min(lo + 1, _mipCount - 1);
        float f = lod - lo;
        Vector3 a = SampleBilinear(u, v, lo);
        if (f <= 0f) return a;
        Vector3 b = SampleBilinear(u, v, hi);
        return Vector3.Lerp(a, b, f);
    }

    private Vector3 SampleBilinear(float u, float v, int mip)
    {
        int w = _mipWidths[mip];
        int h = _mipHeights[mip];
        float[] pixels = _mips[mip];

        // Texel-CENTER addressing: texel i covers [i/w, (i+1)/w), centred at
        // (i+0.5)/w. Mapping the sample to texel space therefore subtracts the
        // half-texel offset — `u*w - 0.5` — instead of the old `u*(w-1)`, which
        // squeezed the image by one texel and introduced a half-texel shift.
        float px = u * w - 0.5f;
        float py = v * h - 0.5f;

        int x0 = (int)MathF.Floor(px);
        int y0 = (int)MathF.Floor(py);
        float fx = px - x0;
        float fy = py - y0;

        // WRAP (repeat) addressing for the neighbour texels so a tiled texture
        // is seamless: the right/bottom edge blends back into column/row 0
        // instead of clamping to the edge texel (which seamed every tile).
        int x0w = Wrap(x0, w);
        int y0w = Wrap(y0, h);
        int x1w = Wrap(x0 + 1, w);
        int y1w = Wrap(y0 + 1, h);

        Vector3 c00 = GetPixel(pixels, w, x0w, y0w);
        Vector3 c10 = GetPixel(pixels, w, x1w, y0w);
        Vector3 c01 = GetPixel(pixels, w, x0w, y1w);
        Vector3 c11 = GetPixel(pixels, w, x1w, y1w);

        Vector3 top = Vector3.Lerp(c00, c10, fx);
        Vector3 bot = Vector3.Lerp(c01, c11, fx);
        return Vector3.Lerp(top, bot, fy);
    }

    /// <summary>Positive modulo (repeat addressing) — maps any texel index into
    /// <c>[0, n)</c>, wrapping negatives and overflow back into range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Wrap(int i, int n)
    {
        i %= n;
        return i < 0 ? i + n : i;
    }

    private static Vector3 GetPixel(float[] pixels, int w, int x, int y)
    {
        int idx = (y * w + x) * 3;
        return new Vector3(pixels[idx], pixels[idx + 1], pixels[idx + 2]);
    }
}
