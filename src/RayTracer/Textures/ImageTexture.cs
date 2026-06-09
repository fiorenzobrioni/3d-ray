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
/// Supports all formats handled by ImageSharp: PNG, JPEG, BMP, GIF, TIFF, WebP.
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

    // Shared sRGB→linear decode table (gamma 2.2), indexed by the raw 0-255
    // byte value. Built once for the whole process.
    private static readonly float[] SrgbDecodeLut = BuildSrgbDecodeLut();

    private static float[] BuildSrgbDecodeLut()
    {
        var lut = new float[256];
        const float inv255 = 1f / 255f;
        for (int i = 0; i < 256; i++)
            lut[i] = MathF.Pow(i * inv255, 2.2f);
        return lut;
    }

    public ImageTexture(string imagePath, float scaleU = 1f, float scaleV = 1f)
    {
        _scaleU = scaleU;
        _scaleV = scaleV;

        float[] level0;
        int w0, h0;
        // sRGB→linear decode via a 256-entry LUT. A byte channel has only 256
        // possible values, so the table is exact and turns millions of per-texel
        // MathF.Pow(x, 2.2) calls at load into a single array index.
        float[] srgbToLinear = SrgbDecodeLut;
        using (var image = Image.Load<Rgba32>(imagePath))
        {
            w0 = image.Width;
            h0 = image.Height;
            level0 = new float[w0 * h0 * 3];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h0; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int baseIdx = y * w0 * 3;
                    for (int x = 0; x < w0; x++)
                    {
                        var pixel = row[x];
                        int idx = baseIdx + x * 3;
                        level0[idx]     = srgbToLinear[pixel.R];
                        level0[idx + 1] = srgbToLinear[pixel.G];
                        level0[idx + 2] = srgbToLinear[pixel.B];
                    }
                }
            });
        }

        // ── Build mip pyramid (2×2 box filter, linear space, stops at 1×1) ─
        int mipCount = 1 + (int)MathF.Floor(MathF.Log2(MathF.Max(w0, h0)));
        _mips = new float[mipCount][];
        _mipWidths = new int[mipCount];
        _mipHeights = new int[mipCount];
        _mipWidths[0] = w0; _mipHeights[0] = h0; _mips[0] = level0;

        for (int m = 1; m < mipCount; m++)
        {
            int wPrev = _mipWidths[m - 1];
            int hPrev = _mipHeights[m - 1];
            int wCur = wPrev > 1 ? wPrev / 2 : 1;
            int hCur = hPrev > 1 ? hPrev / 2 : 1;
            float[] prev = _mips[m - 1];
            float[] cur = new float[wCur * hCur * 3];
            for (int y = 0; y < hCur; y++)
            {
                for (int x = 0; x < wCur; x++)
                {
                    // Average a 2×2 block (or 1×2 / 2×1 at odd boundaries).
                    int x0 = x * 2, y0 = y * 2;
                    int x1 = Math.Min(x0 + 1, wPrev - 1);
                    int y1 = Math.Min(y0 + 1, hPrev - 1);
                    float r = 0, g = 0, b = 0;
                    AccumPixel(prev, wPrev, x0, y0, ref r, ref g, ref b);
                    AccumPixel(prev, wPrev, x1, y0, ref r, ref g, ref b);
                    AccumPixel(prev, wPrev, x0, y1, ref r, ref g, ref b);
                    AccumPixel(prev, wPrev, x1, y1, ref r, ref g, ref b);
                    int idx = (y * wCur + x) * 3;
                    cur[idx]     = r * 0.25f;
                    cur[idx + 1] = g * 0.25f;
                    cur[idx + 2] = b * 0.25f;
                }
            }
            _mipWidths[m] = wCur; _mipHeights[m] = hCur; _mips[m] = cur;
        }
        _mipCount = mipCount;
    }

    private static void AccumPixel(float[] pixels, int w, int x, int y, ref float r, ref float g, ref float b)
    {
        int idx = (y * w + x) * 3;
        r += pixels[idx];
        g += pixels[idx + 1];
        b += pixels[idx + 2];
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

        float lenX = MathF.Sqrt(dudx * dudx + dvdx * dvdx);
        float lenY = MathF.Sqrt(dudy * dudy + dvdy * dvdy);

        float minor = MathF.Min(lenX, lenY);
        float major = MathF.Max(lenX, lenY);
        if (major <= 0f) return Value(u, v, p, objectSeed);

        // Clamp anisotropy ratio — raise minor to keep major/minor ≤ MaxAnisotropy.
        // This trades a sliver of aliasing along the minor axis at extreme
        // angles for a bounded sample count, exactly as GPUs do.
        if (minor * MaxAnisotropy < major) minor = major / MaxAnisotropy;

        // LOD = log2(minor × textureSize). Bigger footprint = higher level
        // (more blur). The texture coordinates are in [0,1], so multiply by
        // the max dimension to get back to texel units.
        float maxDim = MathF.Max(_mipWidths[0], _mipHeights[0]);
        float lod = MathF.Log2(MathF.Max(minor * maxDim, 1f));
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
