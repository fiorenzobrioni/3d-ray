using System.Numerics;

namespace RayTracer.Rendering;

/// <summary>View transform: how scene-linear radiance becomes a display image (<c>--view</c>).</summary>
public enum ViewTransform
{
    /// <summary>AgX: wide-gamut log encoding and a sigmoid, then sRGB. Bright
    /// saturated light desaturates towards white as on film, instead of
    /// clipping one channel and shifting hue. The CLI default.</summary>
    AgX,
    /// <summary>ACES 1.x Output Transform (RRT + ODT, sRGB 100 nits, dim surround).</summary>
    Aces,
    /// <summary>No tone mapping: clip to [0, 1] and encode with the sRGB curve.</summary>
    Standard,
    /// <summary>The curve of the engine before the review: a per-channel fit of
    /// the ACES curve followed by a pure 2.2 gamma (PIANO-REVIEW.md §3.5).
    /// Kept to reproduce older renders.</summary>
    Legacy,
}

/// <summary>Creative look applied inside the AgX view (<c>--look</c>).</summary>
public enum ViewLook { None, Punchy, Golden }

/// <summary>The sRGB transfer function (IEC 61966-2-1), both directions.</summary>
public static class Srgb
{
    public static float Encode(float linear) =>
        linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;

    public static float Decode(float encoded) =>
        encoded <= 0.04045f ? encoded / 12.92f : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    public static Vector3 Encode(Vector3 c) => new(Encode(c.X), Encode(c.Y), Encode(c.Z));
}

/// <summary>3×3 colour matrix (row-major, applied to column vectors), built in
/// double precision from chromaticities.</summary>
public readonly struct ColorMatrix
{
    private readonly double _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;

    public ColorMatrix(double m00, double m01, double m02,
                       double m10, double m11, double m12,
                       double m20, double m21, double m22)
    {
        _m00 = m00; _m01 = m01; _m02 = m02;
        _m10 = m10; _m11 = m11; _m12 = m12;
        _m20 = m20; _m21 = m21; _m22 = m22;
    }

    public static readonly ColorMatrix Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public Vector3 Apply(Vector3 v) => new(
        (float)(_m00 * v.X + _m01 * v.Y + _m02 * v.Z),
        (float)(_m10 * v.X + _m11 * v.Y + _m12 * v.Z),
        (float)(_m20 * v.X + _m21 * v.Y + _m22 * v.Z));

    public (double X, double Y, double Z) Apply(double x, double y, double z) => (
        _m00 * x + _m01 * y + _m02 * z,
        _m10 * x + _m11 * y + _m12 * z,
        _m20 * x + _m21 * y + _m22 * z);

    public (double R, double G, double B) Row(int i) => i switch
    {
        0 => (_m00, _m01, _m02),
        1 => (_m10, _m11, _m12),
        _ => (_m20, _m21, _m22),
    };

    public static ColorMatrix operator *(ColorMatrix a, ColorMatrix b) => new(
        a._m00 * b._m00 + a._m01 * b._m10 + a._m02 * b._m20,
        a._m00 * b._m01 + a._m01 * b._m11 + a._m02 * b._m21,
        a._m00 * b._m02 + a._m01 * b._m12 + a._m02 * b._m22,
        a._m10 * b._m00 + a._m11 * b._m10 + a._m12 * b._m20,
        a._m10 * b._m01 + a._m11 * b._m11 + a._m12 * b._m21,
        a._m10 * b._m02 + a._m11 * b._m12 + a._m12 * b._m22,
        a._m20 * b._m00 + a._m21 * b._m10 + a._m22 * b._m20,
        a._m20 * b._m01 + a._m21 * b._m11 + a._m22 * b._m21,
        a._m20 * b._m02 + a._m21 * b._m12 + a._m22 * b._m22);

    public ColorMatrix Inverse()
    {
        double c00 = _m11 * _m22 - _m12 * _m21, c01 = _m12 * _m20 - _m10 * _m22, c02 = _m10 * _m21 - _m11 * _m20;
        double det = _m00 * c00 + _m01 * c01 + _m02 * c02;
        double inv = 1.0 / det;
        return new ColorMatrix(
            c00 * inv, (_m02 * _m21 - _m01 * _m22) * inv, (_m01 * _m12 - _m02 * _m11) * inv,
            c01 * inv, (_m00 * _m22 - _m02 * _m20) * inv, (_m02 * _m10 - _m00 * _m12) * inv,
            c02 * inv, (_m01 * _m20 - _m00 * _m21) * inv, (_m00 * _m11 - _m01 * _m10) * inv);
    }

    public static ColorMatrix Diagonal(double a, double b, double c) => new(a, 0, 0, 0, b, 0, 0, 0, c);

    /// <summary>RGB → XYZ for the given primaries and white (CIE xy).</summary>
    public static ColorMatrix RgbToXyz((double x, double y) r, (double x, double y) g, (double x, double y) b,
                                       (double x, double y) white)
    {
        static (double, double, double) Xyz((double x, double y) c) => (c.x / c.y, 1.0, (1 - c.x - c.y) / c.y);
        var (rx, ry, rz) = Xyz(r);
        var (gx, gy, gz) = Xyz(g);
        var (bx, by, bz) = Xyz(b);
        var p = new ColorMatrix(rx, gx, bx, ry, gy, by, rz, gz, bz);
        var (wx, wy, wz) = Xyz(white);
        var (sr, sg, sb) = p.Inverse().Apply(wx, wy, wz);
        return p * Diagonal(sr, sg, sb);
    }

    private static readonly ColorMatrix Bradford = new(
        0.8951, 0.2664, -0.1614,
        -0.7502, 1.7135, 0.0367,
        0.0389, -0.0685, 1.0296);

    /// <summary>Von Kries adaptation in the Bradford cone space from one white
    /// (CIE xy) to another, in XYZ.</summary>
    public static ColorMatrix BradfordAdaptation((double x, double y) from, (double x, double y) to)
    {
        var (fx, fy, fz) = Bradford.Apply(from.x / from.y, 1.0, (1 - from.x - from.y) / from.y);
        var (tx, ty, tz) = Bradford.Apply(to.x / to.y, 1.0, (1 - to.x - to.y) / to.y);
        return Bradford.Inverse() * Diagonal(tx / fx, ty / fy, tz / fz) * Bradford;
    }
}

/// <summary>Primaries and whites of the colour spaces the display transforms use.</summary>
public static class ColorSpaces
{
    public static readonly (double x, double y) D65 = (0.3127, 0.3290);
    public static readonly (double x, double y) AcesWhite = (0.32168, 0.33767);   // ≈ D60

    /// <summary>The working space: linear Rec.709 / sRGB primaries, D65.</summary>
    public static readonly ColorMatrix Rec709ToXyz =
        ColorMatrix.RgbToXyz((0.64, 0.33), (0.30, 0.60), (0.15, 0.06), D65);
    public static readonly ColorMatrix XyzToRec709 = Rec709ToXyz.Inverse();

    public static readonly ColorMatrix Rec2020ToXyz =
        ColorMatrix.RgbToXyz((0.708, 0.292), (0.170, 0.797), (0.131, 0.046), D65);
    public static readonly ColorMatrix Rec709ToRec2020 = Rec2020ToXyz.Inverse() * Rec709ToXyz;
    public static readonly ColorMatrix Rec2020ToRec709 = Rec709ToRec2020.Inverse();

    public static readonly ColorMatrix Ap0ToXyz =
        ColorMatrix.RgbToXyz((0.7347, 0.2653), (0.0, 1.0), (0.0001, -0.0770), AcesWhite);
    public static readonly ColorMatrix Ap1ToXyz =
        ColorMatrix.RgbToXyz((0.713, 0.293), (0.165, 0.830), (0.128, 0.044), AcesWhite);
    public static readonly ColorMatrix XyzToAp1 = Ap1ToXyz.Inverse();
    public static readonly ColorMatrix Ap0ToAp1 = XyzToAp1 * Ap0ToXyz;
    public static readonly ColorMatrix Ap1ToAp0 = Ap0ToAp1.Inverse();

    /// <summary>Linear Rec.709 (D65) → ACES2065-1 (AP0, ACES white), Bradford-adapted.</summary>
    public static readonly ColorMatrix Rec709ToAp0 =
        Ap0ToXyz.Inverse() * ColorMatrix.BradfordAdaptation(D65, AcesWhite) * Rec709ToXyz;
}

/// <summary>
/// Display transform (PIANO-REVIEW.md, Phase 3): scene-linear Rec.709 radiance
/// (already scaled by the exposure) → display-encoded sRGB in [0, 1]. Optional
/// white balance first, in linear light, then the view transform.
/// </summary>
public sealed class DisplayTransform
{
    public static readonly DisplayTransform Legacy = new(ViewTransform.Legacy);

    public ViewTransform View { get; }
    public ViewLook Look { get; }
    private readonly ColorMatrix? _whiteBalance;

    public DisplayTransform(ViewTransform view, ViewLook look = ViewLook.None, ColorMatrix? whiteBalance = null)
    {
        View = view;
        Look = view == ViewTransform.AgX ? look : ViewLook.None;
        _whiteBalance = whiteBalance;
    }

    public Vector3 Apply(Vector3 linear)
    {
        if (_whiteBalance is ColorMatrix wb) linear = wb.Apply(linear);
        return View switch
        {
            ViewTransform.AgX => AgX(linear, Look),
            ViewTransform.Aces => AcesOutput.Apply(linear),
            ViewTransform.Standard => Srgb.Encode(Vector3.Clamp(linear, Vector3.Zero, Vector3.One)),
            _ => LegacyCurve(linear),
        };
    }

    public static bool TryParseView(string? name, out ViewTransform view)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "agx": view = ViewTransform.AgX; return true;
            case "aces": view = ViewTransform.Aces; return true;
            case "standard": view = ViewTransform.Standard; return true;
            case "legacy": view = ViewTransform.Legacy; return true;
            default: view = ViewTransform.AgX; return false;
        }
    }

    public static bool TryParseLook(string? name, out ViewLook look)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "none": look = ViewLook.None; return true;
            case "punchy": look = ViewLook.Punchy; return true;
            case "golden": look = ViewLook.Golden; return true;
            default: look = ViewLook.None; return false;
        }
    }

    // ── Legacy ───────────────────────────────────────────────────────────────
    // Narkowicz's fit of the ACES curve per channel, then gamma 2.2: kept
    // bit-identical to the pre-review renderer.
    private static Vector3 LegacyCurve(Vector3 color)
    {
        color = Vector3.Max(color, Vector3.Zero);
        Vector3 a = color * (2.51f * color + new Vector3(0.03f));
        Vector3 b = color * (2.43f * color + new Vector3(0.59f)) + new Vector3(0.14f);
        Vector3 mapped = new Vector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

        const float invGamma = 1f / 2.2f;
        return new Vector3(
            MathF.Pow(Math.Clamp(mapped.X, 0f, 1f), invGamma),
            MathF.Pow(Math.Clamp(mapped.Y, 0f, 1f), invGamma),
            MathF.Pow(Math.Clamp(mapped.Z, 0f, 1f), invGamma));
    }

    // ── AgX ──────────────────────────────────────────────────────────────────
    // Sobotka's AgX in its widely used minimal form: Rec.2020 → inset matrix
    // (pulls the primaries inwards so that bright saturated colours path to
    // white) → log2 encoding of [-10, +6.5] stops around 0.18 → sigmoid (a
    // 6th-order polynomial fit of the reference curve, mean squared error
    // 3.7e-6) → optional look → outset matrix → 2.2 linearisation → Rec.709.
    private static readonly ColorMatrix AgxInset = new(
        0.856627153315983, 0.0951212405381588, 0.0482516061458583,
        0.137318972929847, 0.761241990602591, 0.101439036467562,
        0.11189821299995, 0.0767994186031903, 0.811302368396859);

    private static readonly ColorMatrix AgxOutset = new(
        1.1271005818144368, -0.11060664309660323, -0.016493938717834573,
        -0.1413297634984383, 1.157823702216272, -0.016493938717834257,
        -0.14132976349843826, -0.11060664309660294, 1.2519364065950405);

    private static readonly ColorMatrix AgxIn = AgxInset * ColorSpaces.Rec709ToRec2020;

    private const float AgxMinEv = -12.47393f;   // log2(2^-10 * 0.18)
    private const float AgxMaxEv = 4.026069f;    // log2(2^6.5 * 0.18)

    private static Vector3 AgX(Vector3 linear, ViewLook look)
    {
        Vector3 c = AgxIn.Apply(linear);
        c = new Vector3(AgxLog(c.X), AgxLog(c.Y), AgxLog(c.Z));
        c = new Vector3(AgxSigmoid(c.X), AgxSigmoid(c.Y), AgxSigmoid(c.Z));
        if (look != ViewLook.None) c = AgxLook(c, look);
        c = AgxOutset.Apply(c);
        c = new Vector3(
            MathF.Pow(MathF.Max(c.X, 0f), 2.2f),
            MathF.Pow(MathF.Max(c.Y, 0f), 2.2f),
            MathF.Pow(MathF.Max(c.Z, 0f), 2.2f));
        c = ColorSpaces.Rec2020ToRec709.Apply(c);
        return Srgb.Encode(Vector3.Clamp(c, Vector3.Zero, Vector3.One));
    }

    private static float AgxLog(float x)
    {
        float l = (MathF.Log2(MathF.Max(x, 1e-10f)) - AgxMinEv) / (AgxMaxEv - AgxMinEv);
        return Math.Clamp(l, 0f, 1f);
    }

    private static float AgxSigmoid(float x)
    {
        float x2 = x * x, x4 = x2 * x2;
        return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x
             + 0.4298f * x2 + 0.1191f * x - 0.00232f;
    }

    // ASC CDL looks (slope, power, saturation) on the sigmoid's output.
    private static Vector3 AgxLook(Vector3 v, ViewLook look)
    {
        var lw = new Vector3(0.2126f, 0.7152f, 0.0722f);
        float luma = Vector3.Dot(v, lw);
        Vector3 slope = Vector3.One, power = Vector3.One;
        float sat = 1f;
        if (look == ViewLook.Golden) { slope = new Vector3(1f, 0.9f, 0.5f); power = new Vector3(0.8f); sat = 1.3f; }
        else if (look == ViewLook.Punchy) { power = new Vector3(1.35f); sat = 1.4f; }
        v = Vector3.Max(v * slope, Vector3.Zero);
        v = new Vector3(MathF.Pow(v.X, power.X), MathF.Pow(v.Y, power.Y), MathF.Pow(v.Z, power.Z));
        return new Vector3(luma) + sat * (v - new Vector3(luma));
    }
}

/// <summary>
/// ACES 1.x Output Transform for an sRGB display (RRT + ODT 100 nits, dim
/// surround), transcribed from the Academy's CTL (RRT.ctl, ODT.Academy.
/// RGBmonitor_100nits_dim.ctl and their libraries) in double precision.
/// Checked against OpenColorIO's implementation (ACES 1.3 CG config, view
/// "ACES 1.0 - SDR Video"): within 7e-4 of a display code value.
/// </summary>
internal static class AcesOutput
{
    private static readonly ColorMatrix InToAp0 = ColorSpaces.Rec709ToAp0;
    private static readonly ColorMatrix Ap0ToAp1 = ColorSpaces.Ap0ToAp1;
    private static readonly ColorMatrix Ap1ToAp0 = ColorSpaces.Ap1ToAp0;
    private static readonly ColorMatrix Ap1ToXyz = ColorSpaces.Ap1ToXyz;
    private static readonly ColorMatrix XyzToAp1 = ColorSpaces.XyzToAp1;
    private static readonly ColorMatrix Ap1ToDisplay =
        ColorSpaces.XyzToRec709 * ColorMatrix.BradfordAdaptation(ColorSpaces.AcesWhite, ColorSpaces.D65) * ColorSpaces.Ap1ToXyz;
    private static readonly (double R, double G, double B) Ap1Luma = ColorSpaces.Ap1ToXyz.Row(1);

    private const double HalfMax = 65504.0, HalfMin = 5.96046448e-08;

    private static readonly double[] RrtLow = { -4.0, -4.0, -3.1573765773, -0.4852499958, 1.8477324706, 1.8477324706 };
    private static readonly double[] RrtHigh = { -0.7185482425, 2.0810307172, 3.6681241237, 4.0, 4.0, 4.0 };
    private static readonly double[] OdtLow = { -1.6989700043, -1.6989700043, -1.4779, -1.2291, -0.8648, -0.448, 0.00518, 0.4511080334, 0.9113744414, 0.9113744414 };
    private static readonly double[] OdtHigh = { 0.5154386965, 0.8470437783, 1.1358, 1.3802, 1.5197, 1.5985, 1.6467, 1.6746091357, 1.687873339, 1.687873339 };

    private readonly record struct Spline(double[] Low, double[] High, double MinX, double MinY,
                                          double MidX, double MidY, double MaxX, double MaxY,
                                          double SlopeLow, double SlopeHigh, int Knots);

    private static readonly Spline Rrt = new(RrtLow, RrtHigh, 0.18 * Math.Pow(2, -15), 0.0001,
                                             0.18, 4.8, 0.18 * Math.Pow(2, 18), 10000.0, 0.0, 0.0, 4);
    private static readonly Spline Odt = new(OdtLow, OdtHigh,
        Evaluate(Rrt, 0.18 * Math.Pow(2, -6.5)), 0.02,
        Evaluate(Rrt, 0.18), 4.8,
        Evaluate(Rrt, 0.18 * Math.Pow(2, 6.5)), 48.0, 0.0, 0.04, 8);

    /// <summary>The segmented quadratic B-spline tone scales (c5 for the RRT, c9 for the ODT).</summary>
    private static double Evaluate(Spline c, double x)
    {
        double logx = Math.Log10(Math.Max(x, HalfMin));
        double lMin = Math.Log10(c.MinX), lMid = Math.Log10(c.MidX), lMax = Math.Log10(c.MaxX);
        double logy;
        if (logx <= lMin)
            logy = logx * c.SlopeLow + (Math.Log10(c.MinY) - c.SlopeLow * lMin);
        else if (logx < lMid)
            logy = Segment(c.Low, (c.Knots - 1) * (logx - lMin) / (lMid - lMin));
        else if (logx < lMax)
            logy = Segment(c.High, (c.Knots - 1) * (logx - lMid) / (lMax - lMid));
        else
            logy = logx * c.SlopeHigh + (Math.Log10(c.MaxY) - c.SlopeHigh * lMax);
        return Math.Pow(10.0, logy);
    }

    private static double Segment(double[] coefs, double knot)
    {
        int j = (int)knot;
        double t = knot - j;
        double c0 = coefs[j], c1 = coefs[j + 1], c2 = coefs[j + 2];
        // [t², t, 1] · (cf × M), M = [[0.5, -1, 0.5], [-1, 1, 0.5], [0.5, 0, 0]]
        double a = 0.5 * c0 - c1 + 0.5 * c2;
        double b = -c0 + c1;
        double d = 0.5 * c0 + 0.5 * c1;
        return a * t * t + b * t + d;
    }

    private static double SigmoidShaper(double x)
    {
        double t = Math.Max(1.0 - Math.Abs(x / 2.0), 0.0);
        double y = 1.0 + Math.Sign(x) * (1.0 - t * t);
        return y / 2.0;
    }

    private static double GlowGain(double yc, double gain, double mid)
    {
        if (yc <= 2.0 / 3.0 * mid) return gain;
        if (yc >= 2.0 * mid) return 0.0;
        return gain * (mid / yc - 0.5);
    }

    private static double CubicBasisShaper(double x, double w)
    {
        if (!(x > -w / 2 && x < w / 2)) return 0.0;
        double knot = (x + w / 2) * 4.0 / w;
        int j = (int)knot;
        double t = knot - j, t2 = t * t, t3 = t2 * t;
        double y = j switch
        {
            3 => -t3 / 6 + t2 / 2 - t / 2 + 1.0 / 6,
            2 => t3 / 2 - t2 + 2.0 / 3,
            1 => -t3 / 2 + t2 / 2 + t / 2 + 1.0 / 6,
            0 => t3 / 6,
            _ => 0.0,
        };
        return y * 1.5;
    }

    public static Vector3 Apply(Vector3 linearRec709)
    {
        var (r, g, b) = InToAp0.Apply(linearRec709.X, linearRec709.Y, linearRec709.Z);

        // ── RRT ──
        // Glow module: lifts dark saturated colours a little.
        double ma = Math.Max(r, Math.Max(g, b)), mi = Math.Min(r, Math.Min(g, b));
        double saturation = (Math.Max(ma, 1e-10) - Math.Max(mi, 1e-10)) / Math.Max(ma, 1e-2);
        double chroma = Math.Sqrt(Math.Max(0.0, b * (b - g) + g * (g - r) + r * (r - b)));
        double yc = (b + g + r + 1.75 * chroma) / 3.0;
        double glow = 1.0 + GlowGain(yc, 0.05 * SigmoidShaper((saturation - 0.4) / 0.2), 0.08);
        r *= glow; g *= glow; b *= glow;

        // Red modifier: tames the hue skew of saturated reds.
        double hue = r == g && g == b ? 0.0 : Math.Atan2(Math.Sqrt(3.0) * (g - b), 2.0 * r - g - b) * 180.0 / Math.PI;
        if (hue < 0) hue += 360.0;
        double centred = hue > 180.0 ? hue - 360.0 : hue;
        double hueWeight = CubicBasisShaper(centred, 135.0);
        r += hueWeight * saturation * (0.03 - r) * (1.0 - 0.82);

        r = Math.Clamp(r, 0.0, HalfMax); g = Math.Clamp(g, 0.0, HalfMax); b = Math.Clamp(b, 0.0, HalfMax);
        var (pr, pg, pb) = Ap0ToAp1.Apply(r, g, b);
        pr = Math.Clamp(pr, 0.0, HalfMax); pg = Math.Clamp(pg, 0.0, HalfMax); pb = Math.Clamp(pb, 0.0, HalfMax);
        (pr, pg, pb) = Desaturate(pr, pg, pb, 0.96);
        var (or, og, ob) = Ap1ToAp0.Apply(Evaluate(Rrt, pr), Evaluate(Rrt, pg), Evaluate(Rrt, pb));

        // ── ODT: sRGB, 100 nits, dim surround ──
        (pr, pg, pb) = Ap0ToAp1.Apply(or, og, ob);
        const double white = 48.0, black = 0.02;
        double lr = (Evaluate(Odt, pr) - black) / (white - black);
        double lg = (Evaluate(Odt, pg) - black) / (white - black);
        double lb = (Evaluate(Odt, pb) - black) / (white - black);

        // Dark → dim surround: a 0.9811 gamma on luminance.
        var (x, y, z) = Ap1ToXyz.Apply(lr, lg, lb);
        double divisor = Math.Max(x + y + z, 1e-10);
        double cx = x / divisor, cy = y / divisor;
        double yy = Math.Pow(Math.Clamp(y, 0.0, HalfMax), 0.9811);
        double ky = yy / Math.Max(cy, 1e-10);
        (lr, lg, lb) = XyzToAp1.Apply(cx * ky, yy, (1.0 - cx - cy) * ky);
        (lr, lg, lb) = Desaturate(lr, lg, lb, 0.93);

        var (dr, dg, db) = Ap1ToDisplay.Apply(lr, lg, lb);
        return new Vector3(
            Srgb.Encode((float)Math.Clamp(dr, 0.0, 1.0)),
            Srgb.Encode((float)Math.Clamp(dg, 0.0, 1.0)),
            Srgb.Encode((float)Math.Clamp(db, 0.0, 1.0)));
    }

    private static (double, double, double) Desaturate(double r, double g, double b, double sat)
    {
        double luma = Ap1Luma.R * r + Ap1Luma.G * g + Ap1Luma.B * b;
        return (sat * r + (1 - sat) * luma, sat * g + (1 - sat) * luma, sat * b + (1 - sat) * luma);
    }
}

/// <summary>
/// White balance by colour temperature (PIANO-REVIEW.md, Phase 3): the
/// chromaticity of a light at <c>kelvin</c> is adapted to the display white
/// (D65) with Bradford, so a light of that temperature renders neutral, as a
/// camera set to that white balance does. Lower values cool the image, higher
/// values warm it. The chromaticity follows the CIE daylight locus above
/// 4500 K (6504 K is D65 itself, so it is the identity) and the Planckian
/// locus below 3500 K (incandescent light), blended linearly in between.
/// </summary>
public static class WhiteBalance
{
    public const float MinKelvin = 1667f, MaxKelvin = 25000f;

    public static ColorMatrix Matrix(double kelvin)
    {
        var white = Chromaticity(kelvin);
        return ColorSpaces.XyzToRec709
             * ColorMatrix.BradfordAdaptation(white, ColorSpaces.D65)
             * ColorSpaces.Rec709ToXyz;
    }

    public static (double x, double y) Chromaticity(double kelvin)
    {
        double t = Math.Clamp(kelvin, MinKelvin, MaxKelvin);
        if (t >= 4500.0) return Daylight(t);
        if (t <= 3500.0) return Planckian(t);
        double w = (t - 3500.0) / 1000.0;
        var (px, py) = Planckian(t);
        var (dx, dy) = Daylight(t);
        return (px + w * (dx - px), py + w * (dy - py));
    }

    /// <summary>CIE daylight locus (CIE 15:2004), defined from 4000 K.</summary>
    private static (double x, double y) Daylight(double t)
    {
        double t2 = t * t, t3 = t2 * t;
        double x = t <= 7000.0
            ? 0.244063 + 0.09911e3 / t + 2.9678e6 / t2 - 4.6070e9 / t3
            : 0.237040 + 0.24748e3 / t + 1.9018e6 / t2 - 2.0064e9 / t3;
        return (x, -3.000 * x * x + 2.870 * x - 0.275);
    }

    /// <summary>Planckian locus, cubic spline of Kang et al. (2002), 1667-25000 K.</summary>
    private static (double x, double y) Planckian(double t)
    {
        double t2 = t * t, t3 = t2 * t;
        double x = t <= 4000.0
            ? -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910
            : -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;
        double x2 = x * x, x3 = x2 * x;
        double y = t <= 2222.0 ? -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683
                 : t <= 4000.0 ? -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867
                 : 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483;
        return (x, y);
    }
}
