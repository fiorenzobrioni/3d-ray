using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>Search/patch geometry and filter strength of one NL-means pass.</summary>
internal readonly struct NlmParams
{
    public readonly int SearchRadius;
    public readonly int PatchRadius;
    public readonly float K;

    public NlmParams(int searchRadius, int patchRadius, float k)
    {
        SearchRadius = searchRadius;
        PatchRadius = patchRadius;
        K = k;
    }
}

/// <summary>
/// Optional joint-filter guide term: per-pixel (not patch-averaged) squared
/// feature differences added to the colour patch distance. Planes come from
/// the prefiltered feature buffers; each plane carries its own inverse
/// bandwidth 1/σ² so albedo/normal/depth can be weighted independently.
/// </summary>
internal sealed class FeatureGuides
{
    public required float[] Data;          // plane-major, planeCount × (W·H)
    public required int PlaneCount;
    public required float[] InvBandwidth;  // per plane

    public static FeatureGuides Build(int width, int height, params (FrameBuffer Fb, float InvBandwidth)[] sources)
    {
        int n = width * height;
        int planeCount = 0;
        foreach (var (fb, _) in sources) planeCount += fb.Channels;

        var data = new float[planeCount * n];
        var invBw = new float[planeCount];
        int plane = 0;
        foreach (var (fb, bw) in sources)
        {
            for (int c = 0; c < fb.Channels; c++, plane++)
            {
                fb.Plane(c).CopyTo(data.AsSpan(plane * n, n));
                invBw[plane] = bw;
            }
        }
        return new FeatureGuides { Data = data, PlaneCount = planeCount, InvBandwidth = invBw };
    }
}

/// <summary>
/// Non-local means engine, offset-decomposed: instead of the naive
/// per-pixel × per-neighbour × per-patch O(N·|window|·|patch|) loop, each of
/// the (2R+1)² window offsets t is processed as O(N) plane sweeps —
/// pointwise modified distance between the image and its t-shifted copy
/// (SIMD over rows), separable patch box-average (running sums), then weight
/// + accumulation (SIMD + fast exp). Rows are processed in parallel; the
/// accumulators are written only at row-disjoint indices so no
/// synchronisation is needed.
///
/// The per-channel pointwise distance is the variance-cancelled form of
/// Rousselle et al. 2012 (k folded out so candidates can share distances):
///   d_ch(p,q) = ((u_p−u_q)² − (σ²_p + min(σ²_p, σ²_q))) / (ε + σ²_p + σ²_q)
/// and the weight is w = exp(−max(0, d̄(p,q)/k² + d_features(p,q))).
/// </summary>
internal static class NlMeansCore
{
    private const float Epsilon = 1e-10f;
    /// <summary>exp(−10) ≈ 4.5e-5 — contributions beyond this are noise.</summary>
    internal const float WeightCutoff = 10f;

    /// <summary>
    /// Vectorised exp(−d) for d ∈ [0, ~80) via the Schraudolph bit trick
    /// (≈2% relative error — irrelevant for filter weights). Inputs beyond
    /// the cutoff must be masked out by the caller.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector<float> FastExpNeg(Vector<float> d)
    {
        // exp(−d) = 2^(−d/ln2);  float bits ≈ a·x + b with a = 2^23/ln2.
        var x = new Vector<float>(-12102203.16f) * d + new Vector<float>(1064866805.0f);
        return Vector.AsVectorSingle(Vector.ConvertToInt32(x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float FastExpNegScalar(float d)
    {
        int i = (int)(-12102203.16f * d + 1064866805.0f);
        return BitConverter.Int32BitsToSingle(i);
    }

    /// <summary>
    /// Pointwise variance-cancelled distance for <paramref name="count"/>
    /// consecutive pixels starting at index <paramref name="p0"/> (their
    /// partners start at <paramref name="q0"/>), averaged over
    /// <paramref name="channels"/> planes of size <paramref name="n"/>.
    /// Shared by the global filter and the NFOR tile machinery.
    /// </summary>
    internal static void DistanceRow(float[] g, float[] v, int n, int channels,
                                     int p0, int q0, int count, float[] dst, int dstBase)
    {
        int vw = Vector<float>.Count;
        float invC = 1f / channels;
        var eps = new Vector<float>(Epsilon);
        int i = 0;
        for (; i <= count - vw; i += vw)
        {
            var d = Vector<float>.Zero;
            for (int c = 0; c < channels; c++)
            {
                int cBase = c * n;
                var gp = Vector.LoadUnsafe(ref g[cBase + p0 + i]);
                var gq = Vector.LoadUnsafe(ref g[cBase + q0 + i]);
                var vp = Vector.LoadUnsafe(ref v[cBase + p0 + i]);
                var vq = Vector.LoadUnsafe(ref v[cBase + q0 + i]);
                var du = gp - gq;
                var vmin = Vector.Min(vp, vq);
                d += (du * du - (vp + vmin)) / (eps + vp + vq);
            }
            (d * new Vector<float>(invC)).StoreUnsafe(ref dst[dstBase + i]);
        }
        for (; i < count; i++)
        {
            float d = 0f;
            for (int c = 0; c < channels; c++)
            {
                int cBase = c * n;
                float du = g[cBase + p0 + i] - g[cBase + q0 + i];
                float vp = v[cBase + p0 + i], vq = v[cBase + q0 + i];
                float vmin = vp < vq ? vp : vq;
                d += (du * du - (vp + vmin)) / (Epsilon + vp + vq);
            }
            dst[dstBase + i] = d * invC;
        }
    }

    /// <summary>
    /// Filters <paramref name="target"/> with weights computed on
    /// <paramref name="guide"/> (values + per-pixel variance). For dual-buffer
    /// cross-filtering, pass one half as the guide and the other as the
    /// target; weights then never correlate with the noise they average.
    /// </summary>
    public static FrameBuffer Filter(FrameBuffer guide, FrameBuffer guideVar, FrameBuffer target,
                                     in NlmParams prm, FeatureGuides? features)
    {
        int w = guide.Width, h = guide.Height, n = w * h;
        int gc = guide.Channels, tc = target.Channels;
        int R = prm.SearchRadius, P = prm.PatchRadius;
        float invK2 = 1f / (prm.K * prm.K);

        float[] g = guide.Data, v = guideVar.Data, t = target.Data;
        var num = new FrameBuffer(w, h, tc);
        float[] numD = num.Data;
        var den = new float[n];
        var dpt = new float[n];   // pointwise distance plane for the current offset
        var dh = new float[n];    // horizontally patch-averaged distances
        float[]? fData = features?.Data;
        float[]? fBw = features?.InvBandwidth;
        int fPlanes = features?.PlaneCount ?? 0;
        int vw = Vector<float>.Count;

        for (int dy = -R; dy <= R; dy++)
        {
            for (int dx = -R; dx <= R; dx++)
            {
                int x0 = Math.Max(0, -dx), x1 = w - 1 - Math.Max(0, dx);
                int y0 = Math.Max(0, -dy), y1 = h - 1 - Math.Max(0, dy);
                if (x0 > x1 || y0 > y1) continue;
                int qOff = dy * w + dx;

                // Pass 1 — pointwise distance (SIMD) + horizontal patch
                // average (running sum, truncated at the valid borders).
                System.Threading.Tasks.Parallel.For(y0, y1 + 1, y =>
                {
                    int row = y * w;
                    DistanceRow(g, v, n, gc, row + x0, row + x0 + qOff, x1 - x0 + 1, dpt, row + x0);

                    float run = 0f;
                    int warm = Math.Min(x0 + P, x1);
                    for (int xx = x0; xx <= warm; xx++) run += dpt[row + xx];
                    for (int x = x0; x <= x1; x++)
                    {
                        int lo = Math.Max(x0, x - P), hi = Math.Min(x1, x + P);
                        dh[row + x] = run / (hi - lo + 1);
                        if (x + P + 1 <= x1) run += dpt[row + x + P + 1];
                        if (x - P >= x0) run -= dpt[row + x - P];
                    }
                });

                // Pass 2 — vertical patch average, joint feature distance,
                // fast-exp weight, accumulation. SIMD over the row.
                System.Threading.Tasks.Parallel.For(y0, y1 + 1, y =>
                {
                    int row = y * w;
                    int vlo = Math.Max(y0, y - P), vhi = Math.Min(y1, y + P);
                    var invCntY = new Vector<float>(1f / (vhi - vlo + 1));
                    var vInvK2 = new Vector<float>(invK2);
                    var vCutoff = new Vector<float>(WeightCutoff);
                    int x = x0;
                    for (; x <= x1 - vw + 1; x += vw)
                    {
                        int p = row + x;
                        var sum = Vector<float>.Zero;
                        for (int yy = vlo; yy <= vhi; yy++)
                            sum += Vector.LoadUnsafe(ref dh[yy * w + x]);
                        var d = Vector.Max(sum * invCntY, Vector<float>.Zero) * vInvK2;

                        if (fPlanes > 0)
                        {
                            int q0 = p + qOff;
                            for (int fp = 0; fp < fPlanes; fp++)
                            {
                                int fBase = fp * n;
                                var df = Vector.LoadUnsafe(ref fData![fBase + p])
                                       - Vector.LoadUnsafe(ref fData[fBase + q0]);
                                d += df * df * new Vector<float>(fBw![fp]);
                            }
                        }

                        var mask = Vector.LessThan(d, vCutoff);
                        if (mask == Vector<int>.Zero) continue;
                        var wgt = Vector.ConditionalSelect(mask, FastExpNeg(d), Vector<float>.Zero);

                        int q = p + qOff;
                        (Vector.LoadUnsafe(ref den[p]) + wgt).StoreUnsafe(ref den[p]);
                        for (int c = 0; c < tc; c++)
                        {
                            int cBase = c * n;
                            var acc = Vector.LoadUnsafe(ref numD[cBase + p])
                                    + wgt * Vector.LoadUnsafe(ref t[cBase + q]);
                            acc.StoreUnsafe(ref numD[cBase + p]);
                        }
                    }
                    // Scalar tail.
                    for (; x <= x1; x++)
                    {
                        int p = row + x;
                        float sum = 0f;
                        for (int yy = vlo; yy <= vhi; yy++) sum += dh[yy * w + x];
                        float d = MathF.Max(0f, sum / (vhi - vlo + 1)) * invK2;
                        if (fPlanes > 0)
                        {
                            int q0 = p + qOff;
                            for (int fp = 0; fp < fPlanes; fp++)
                            {
                                float df = fData![fp * n + p] - fData[fp * n + q0];
                                d += df * df * fBw![fp];
                            }
                        }
                        if (d >= WeightCutoff) continue;
                        float wgt = FastExpNegScalar(d);
                        int q = p + qOff;
                        den[p] += wgt;
                        for (int c = 0; c < tc; c++)
                            numD[c * n + p] += wgt * t[c * n + q];
                    }
                });
            }
        }

        // Normalise. den > 0 everywhere: the t = 0 offset always contributes
        // w = 1 (its distance is the negative variance-cancellation term,
        // clamped to zero).
        var output = new FrameBuffer(w, h, tc);
        float[] outD = output.Data;
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = row + x;
                float invDen = den[p] > 0f ? 1f / den[p] : 0f;
                for (int c = 0; c < tc; c++)
                    outD[c * n + p] = numD[c * n + p] * invDen;
            }
        });
        return output;
    }
}
