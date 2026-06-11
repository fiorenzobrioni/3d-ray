using System.Runtime.Intrinsics;
using RayTracer.Rendering;

namespace RayTracer.Denoising;

/// <summary>
/// First-order feature regression (the NFOR main pass). For window centres on
/// a stride-2 grid, the NL-means colour weights of the opposite half-buffer
/// drive a weighted linear least-squares fit of the window's colours against
/// the prefiltered features f(q) = [1, Δalbedo, Δnormal, Δdepth] (8
/// coefficients, one 8×8 Gram shared by the three RGB channels). Each solved
/// window predicts ALL of its pixels and splats the weighted predictions into
/// a global accumulator ("collaborative" reconstruction — overlapping windows
/// average out, and the stride cuts solves 4× at no visible cost). Everything
/// is cross-filtered: weights from half B regress half A and vice versa, so
/// the fit never chases its own noise.
///
/// With two filter-strength candidates (high quality), the per-pixel MSE of
/// each candidate is estimated from the disagreement of its two regressed
/// halves and the smaller-error candidate wins per pixel (softened blend).
///
/// Threading: tiles are processed in four checkerboard passes (by tile-coord
/// parity); same-pass tiles are ≥ one tile apart, farther than the splat
/// radius, so accumulator writes never race.
/// </summary>
internal static class NforRegression
{
    private const int TileSize = 64;
    private const int Dim = 8;                  // 1 + albedo(3) + normal(3) + depth(1)
    private const int FeaturePlanes = Dim - 1;
    private const float Epsilon = 1e-10f;
    private const float WeightCutoff = 10f;     // exp(−10) ≈ 4.5e-5
    private const float InvalidDistance = float.PositiveInfinity;
    private const int MseSmoothRadius = 4;      // 9×9 candidate-MSE smoothing
    private const int SelectionSmoothRadius = 2;

    /// <summary>Test/experiment override for the selection-bias margin
    /// (see the candidate-MSE comment in <see cref="Run"/>).</summary>
    internal static float? SelectionMarginOverride;

    /// <summary>
    /// Variance fraction charged to the filtered candidates in the MSE
    /// selection. With independent halves (PRNG) the estimates are unbiased
    /// and no margin is needed; with Sobol the even/odd halves of one
    /// Owen-scrambled sequence are anti-correlated, the dual-buffer variance
    /// is overstated, and the selection would systematically over-filter —
    /// 0.5 calibrated so near-converged Sobol renders never regress while
    /// low-spp renders keep their full gains.
    /// </summary>
    private static float SelectionMargin =>
        SelectionMarginOverride
        ?? (Core.Sampling.Sampler.Kind == Core.Sampling.SamplerKind.Sobol ? 0.5f : 0f);

    public static FrameBuffer Run(RenderBuffers buffers, FrameBuffer halfVar,
                                  FrameBuffer albedo, FrameBuffer normal, FrameBuffer depth,
                                  DenoiserOptions opts)
    {
        int w = buffers.Width, h = buffers.Height, n = w * h;
        float[] feat = BuildScaledFeatures(albedo, normal, depth, n);
        float[] ks = opts.CandidateK;
        int searchRadius = opts.SearchRadius;

        var regA = RegressAllCandidates(guide: buffers.BeautyB, guideVar: halfVar,
                                        target: buffers.BeautyA, feat, w, h, ks, searchRadius);
        var regB = RegressAllCandidates(guide: buffers.BeautyA, guideVar: halfVar,
                                        target: buffers.BeautyB, feat, w, h, ks, searchRadius);

        int nA = buffers.SamplesA, nB = buffers.SamplesB;

        // Candidate set: the UNFILTERED mean (safety net for transport the
        // features cannot see — contact shadows, caustics — where any
        // feature-blind filter is biased) plus one filtered candidate per k.
        int nCand = ks.Length + 1;
        var candidates = new FrameBuffer[nCand];
        var beautyMean = PlaneOps.Combine(buffers.BeautyA, buffers.BeautyB, nA, nB);
        candidates[0] = beautyMean;
        for (int ki = 0; ki < ks.Length; ki++)
            candidates[ki + 1] = PlaneOps.Combine(regA[ki], regB[ki], nA, nB);

        // Per-candidate MSE estimate against the truth, not against each
        // other: each independent half mean B is an unbiased estimate of the
        // truth with known variance, so E[(F_A − B)²] = MSE(F_A) + Var[B] —
        // subtracting Var[B] yields an estimate that SEES BIAS, unlike the
        // half-disagreement |F_A − F_B|² (which only measures variance and
        // would happily select a systematically wrong candidate). The
        // unfiltered candidate's MSE is exactly its variance.
        var fullVar = VarianceEstimator.HalfToFull(halfVar);
        var mse = new float[nCand][];
        var tmp = new float[n];
        float[] vf = fullVar.Data;
        for (int i = 0; i < n; i++)
            tmp[i] = (vf[i] + vf[n + i] + vf[2 * n + i]) * (1f / 3f);
        mse[0] = new float[n];
        PlaneOps.BoxSmooth(tmp, mse[0], w, h, MseSmoothRadius);

        float[] hvA = buffers.BeautyA.Data, hvB = buffers.BeautyB.Data;
        float[] hv = halfVar.Data;
        float selMargin = SelectionMargin;
        for (int ki = 0; ki < ks.Length; ki++)
        {
            var fa = regA[ki].Data; var fb = regB[ki].Data;
            for (int i = 0; i < n; i++)
            {
                float e = 0f;
                float vSum = 0f;
                for (int c = 0; c < 3; c++)
                {
                    int p = c * n + i;
                    float dA = fa[p] - hvB[p];   // filtered A vs noisy half B
                    float dB = fb[p] - hvA[p];   // filtered B vs noisy half A
                    e += 0.5f * (dA * dA + dB * dB) - hv[p];
                    vSum += 0.5f * hv[p];        // = varFull per channel
                }
                // Selection-bias margin: with low-discrepancy samplers the
                // even/odd halves are anti-correlated, which overstates the
                // estimated variance — that bias enters this estimate twice
                // (inflating mse_noisy, deflating mse_k via the subtraction)
                // and would systematically prefer the filtered candidates.
                // Charging the filtered candidates a fraction of the variance
                // restores a conservative decision boundary (calibrated so
                // near-converged Sobol renders never regress).
                tmp[i] = (e + selMargin * vSum) * (1f / 3f);
            }
            mse[ki + 1] = new float[n];
            PlaneOps.BoxSmooth(tmp, mse[ki + 1], w, h, MseSmoothRadius);
        }

        // Softened per-pixel argmin: candidate indicator planes are box
        // blurred (avoids hard selection seams) and renormalised, then the
        // candidates are blended.
        var weights = new float[nCand][];
        var indicator = new float[n];
        for (int ci = 0; ci < nCand; ci++)
        {
            for (int i = 0; i < n; i++)
            {
                float best = mse[0][i]; int bestIdx = 0;
                for (int cj = 1; cj < nCand; cj++)
                    if (mse[cj][i] < best) { best = mse[cj][i]; bestIdx = cj; }
                indicator[i] = bestIdx == ci ? 1f : 0f;
            }
            weights[ci] = new float[n];
            PlaneOps.BoxSmooth(indicator, weights[ci], w, h, SelectionSmoothRadius);
        }

        var result = new FrameBuffer(w, h, 3);
        var dst = result.Data;
        for (int i = 0; i < n; i++)
        {
            float wSum = 0f;
            for (int ci = 0; ci < nCand; ci++) wSum += weights[ci][i];
            float inv = wSum > 0f ? 1f / wSum : 0f;
            for (int c = 0; c < 3; c++)
            {
                int p = c * n + i;
                float acc = 0f;
                for (int ci = 0; ci < nCand; ci++)
                    acc += weights[ci][i] * candidates[ci].Data[p];
                dst[p] = acc * inv;
            }
        }

        // Regression can overshoot below zero on HDR edges — radiance is
        // non-negative by definition.
        var data = result.Data;
        for (int i = 0; i < data.Length; i++)
            if (data[i] < 0f) data[i] = 0f;
        return result;
    }

    /// <summary>Feature stack scaled to unit global standard deviation so the
    /// Tikhonov term and bandwidths are feature-magnitude independent.</summary>
    private static float[] BuildScaledFeatures(FrameBuffer albedo, FrameBuffer normal, FrameBuffer depth, int n)
    {
        var feat = new float[FeaturePlanes * n];
        var sources = new (FrameBuffer Fb, int Channel)[]
        {
            (albedo, 0), (albedo, 1), (albedo, 2),
            (normal, 0), (normal, 1), (normal, 2),
            (depth, 0),
        };
        for (int plane = 0; plane < FeaturePlanes; plane++)
        {
            var src = sources[plane].Fb.Plane(sources[plane].Channel);
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++) { sum += src[i]; sumSq += (double)src[i] * src[i]; }
            double mean = sum / n;
            float std = (float)Math.Sqrt(Math.Max(sumSq / n - mean * mean, 0.0));
            float scale = 1f / MathF.Max(std, 1e-4f);
            var dst = feat.AsSpan(plane * n, n);
            for (int i = 0; i < n; i++) dst[i] = src[i] * scale;
        }
        return feat;
    }

    /// <summary>Per-thread tile workspace, reused across tiles.</summary>
    private sealed class Scratch
    {
        public readonly float[] DptBlock;   // pointwise distances, (T+2P)²
        public readonly float[] DistBlock;  // patch distances, centres × offsets
        public readonly float[] WeightBuf;  // per-offset weights of one centre
        public readonly float[] GramF;      // 8×8 float accumulator
        public readonly float[] RhsF;       // 3×8 float accumulator
        public readonly float[] FVec;       // feature vector of one neighbour
        public readonly float[] BetaF;      // 3×8 solved coefficients
        public readonly double[] Gram;      // 8×8 (solver)
        public readonly double[] Rhs;       // 3×8 (solver)
        public readonly int[] CenterX;
        public readonly int[] CenterY;

        public Scratch(int offsets, int maxCenters, int blockSize)
        {
            DptBlock = new float[blockSize * blockSize];
            DistBlock = new float[maxCenters * offsets];
            WeightBuf = new float[offsets];
            GramF = new float[Dim * Dim];
            RhsF = new float[3 * Dim];
            FVec = new float[Dim];
            BetaF = new float[3 * Dim];
            Gram = new double[Dim * Dim];
            Rhs = new double[3 * Dim];
            CenterX = new int[maxCenters];
            CenterY = new int[maxCenters];
        }
    }

    private static FrameBuffer[] RegressAllCandidates(FrameBuffer guide, FrameBuffer guideVar,
                                                      FrameBuffer target, float[] feat,
                                                      int w, int h, float[] ks, int searchRadius)
    {
        int n = w * h;
        int nk = ks.Length;
        int R = searchRadius;
        const int P = DenoiserOptions.PatchRadius;
        const int stride = DenoiserOptions.RegressionStride;
        int side = 2 * R + 1;
        int offsets = side * side;

        // Offset lookup tables.
        var dxs = new int[offsets];
        var dys = new int[offsets];
        for (int o = 0; o < offsets; o++)
        {
            dxs[o] = o % side - R;
            dys[o] = o / side - R;
        }

        var num = new float[nk][];
        var den = new float[nk][];
        for (int ki = 0; ki < nk; ki++)
        {
            num[ki] = new float[3 * n];
            den[ki] = new float[n];
        }

        int tilesX = (w + TileSize - 1) / TileSize;
        int tilesY = (h + TileSize - 1) / TileSize;
        int maxCenters = ((TileSize + stride - 1) / stride + 1) * ((TileSize + stride - 1) / stride + 1);
        int blockSize = TileSize + 2 * P;
        var scratchPool = new ThreadLocal<Scratch>(() => new Scratch(offsets, maxCenters, blockSize));

        float[] g = guide.Data, v = guideVar.Data, t = target.Data;

        // Four checkerboard passes over tile parity — same-pass tiles are too
        // far apart for their splat regions to overlap.
        for (int pass = 0; pass < 4; pass++)
        {
            var tiles = new List<(int Tx, int Ty)>();
            for (int ty = 0; ty < tilesY; ty++)
            {
                if ((ty & 1) != (pass >> 1)) continue;
                for (int tx = (pass & 1); tx < tilesX; tx += 2)
                    tiles.Add((tx, ty));
            }

            System.Threading.Tasks.Parallel.ForEach(tiles, tile =>
                ProcessTile(tile.Tx, tile.Ty, scratchPool.Value!,
                            g, v, t, feat, w, h, n, ks, num, den,
                            dxs, dys, R, P, stride, offsets, blockSize));
        }
        scratchPool.Dispose();

        // Normalise; pixels never splatted (impossible in practice, guarded
        // anyway) fall back to the noisy target value.
        var result = new FrameBuffer[nk];
        for (int ki = 0; ki < nk; ki++)
        {
            var fb = new FrameBuffer(w, h, 3);
            var dst = fb.Data; var nm = num[ki]; var dn = den[ki];
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int p = row + x;
                    if (dn[p] > 0f)
                    {
                        float inv = 1f / dn[p];
                        dst[p] = nm[p] * inv;
                        dst[n + p] = nm[n + p] * inv;
                        dst[2 * n + p] = nm[2 * n + p] * inv;
                    }
                    else
                    {
                        dst[p] = t[p];
                        dst[n + p] = t[n + p];
                        dst[2 * n + p] = t[2 * n + p];
                    }
                }
            });
            result[ki] = fb;
        }
        return result;
    }

    private static void ProcessTile(int tx, int ty, Scratch s,
                                    float[] g, float[] v, float[] t, float[] feat,
                                    int w, int h, int n, float[] ks,
                                    float[][] num, float[][] den,
                                    int[] dxs, int[] dys, int R, int P, int stride,
                                    int offsets, int blockSize)
    {
        int tx0 = tx * TileSize, ty0 = ty * TileSize;
        int tx1 = Math.Min(tx0 + TileSize, w) - 1;
        int ty1 = Math.Min(ty0 + TileSize, h) - 1;

        // Window centres: the global stride grid restricted to this tile, so
        // coverage is uniform across tile borders.
        int cCount = 0;
        int firstY = ty0 + ((stride - ty0 % stride) % stride);
        int firstX = tx0 + ((stride - tx0 % stride) % stride);
        for (int gy = firstY; gy <= ty1; gy += stride)
        for (int gx = firstX; gx <= tx1; gx += stride)
        {
            s.CenterY[cCount] = gy;
            s.CenterX[cCount] = gx;
            cCount++;
        }
        if (cCount == 0) return;

        int bx0 = tx0 - P, by0 = ty0 - P;   // distance block origin (may be off-image)

        // ── Patch distances for every (centre, offset) pair ─────────────────
        for (int o = 0; o < offsets; o++)
        {
            int dx = dxs[o], dy = dys[o];
            int qOff = dy * w + dx;
            // Valid p range for this offset: p and q = p + t both in image.
            int pxLo = Math.Max(0, -dx), pxHi = w - 1 - Math.Max(0, dx);
            int pyLo = Math.Max(0, -dy), pyHi = h - 1 - Math.Max(0, dy);

            // Pointwise distances over the block rows that matter (SIMD).
            int rowLo = Math.Max(by0, pyLo), rowHi = Math.Min(by0 + blockSize - 1, pyHi);
            int colLo = Math.Max(bx0, pxLo), colHi = Math.Min(bx0 + blockSize - 1, pxHi);
            for (int gy = rowLo; gy <= rowHi; gy++)
            {
                int bRow = (gy - by0) * blockSize - bx0;   // + gx gives block index
                int pRow = gy * w;
                NlMeansCore.DistanceRow(g, v, n, 3,
                    pRow + colLo, pRow + colLo + qOff, colHi - colLo + 1,
                    s.DptBlock, bRow + colLo);
            }

            // Patch average at each centre (truncated to the valid region).
            for (int c = 0; c < cCount; c++)
            {
                int gx = s.CenterX[c], gy = s.CenterY[c];
                if (gx < pxLo || gx > pxHi || gy < pyLo || gy > pyHi)
                {
                    s.DistBlock[c * offsets + o] = InvalidDistance;
                    continue;
                }
                int xLo = Math.Max(gx - P, pxLo), xHi = Math.Min(gx + P, pxHi);
                int yLo = Math.Max(gy - P, pyLo), yHi = Math.Min(gy + P, pyHi);
                float sum = 0f;
                for (int yy = yLo; yy <= yHi; yy++)
                {
                    int bRow = (yy - by0) * blockSize - bx0;
                    for (int xx = xLo; xx <= xHi; xx++)
                        sum += s.DptBlock[bRow + xx];
                }
                s.DistBlock[c * offsets + o] = sum / ((xHi - xLo + 1) * (yHi - yLo + 1));
            }
        }

        // ── Per-centre regression + collaborative splat, per candidate ──────
        // Dim = 8 floats is exactly one AVX2 lane: the Gram accumulation
        // (8 row FMAs + 3 RHS FMAs per neighbour) and the splat dot products
        // run vectorised when Vector256 is available. Accumulation is float
        // (window sums of ≤ (2R+1)² unit-scale terms — well within float
        // precision); the Cholesky solve converts to double.
        bool v256 = Vector256.IsHardwareAccelerated;
        Span<float> fp = stackalloc float[FeaturePlanes];
        Span<double> meanFallback = stackalloc double[3];
        float[] f = s.FVec;
        float[] gramF = s.GramF;
        float[] rhsF = s.RhsF;
        float[] betaF = s.BetaF;
        for (int c = 0; c < cCount; c++)
        {
            int gx = s.CenterX[c], gy = s.CenterY[c];
            int p = gy * w + gx;
            for (int fpI = 0; fpI < FeaturePlanes; fpI++)
                fp[fpI] = feat[fpI * n + p];

            for (int ki = 0; ki < ks.Length; ki++)
            {
                float invK2 = 1f / (ks[ki] * ks[ki]);
                Array.Clear(gramF);
                Array.Clear(rhsF);

                // Gram + right-hand sides.
                for (int o = 0; o < offsets; o++)
                {
                    float dist = s.DistBlock[c * offsets + o];
                    if (float.IsPositiveInfinity(dist)) { s.WeightBuf[o] = 0f; continue; }
                    float d = MathF.Max(0f, dist) * invK2;
                    if (d >= WeightCutoff) { s.WeightBuf[o] = 0f; continue; }
                    float wgt = NlMeansCore.FastExpNegScalar(d);
                    s.WeightBuf[o] = wgt;

                    int q = p + dys[o] * w + dxs[o];
                    f[0] = 1f;
                    for (int fpI = 0; fpI < FeaturePlanes; fpI++)
                        f[fpI + 1] = feat[fpI * n + q] - fp[fpI];

                    float y0 = t[q], y1 = t[n + q], y2 = t[2 * n + q];
                    if (v256)
                    {
                        var fv = Vector256.LoadUnsafe(ref f[0]);
                        for (int i = 0; i < Dim; i++)
                        {
                            var row = Vector256.LoadUnsafe(ref gramF[i * Dim]);
                            (row + Vector256.Create(wgt * f[i]) * fv).StoreUnsafe(ref gramF[i * Dim]);
                        }
                        (Vector256.LoadUnsafe(ref rhsF[0]) + Vector256.Create(wgt * y0) * fv).StoreUnsafe(ref rhsF[0]);
                        (Vector256.LoadUnsafe(ref rhsF[Dim]) + Vector256.Create(wgt * y1) * fv).StoreUnsafe(ref rhsF[Dim]);
                        (Vector256.LoadUnsafe(ref rhsF[2 * Dim]) + Vector256.Create(wgt * y2) * fv).StoreUnsafe(ref rhsF[2 * Dim]);
                    }
                    else
                    {
                        for (int i = 0; i < Dim; i++)
                        {
                            float wfi = wgt * f[i];
                            rhsF[i] += wfi * y0;
                            rhsF[Dim + i] += wfi * y1;
                            rhsF[2 * Dim + i] += wfi * y2;
                            int gRow = i * Dim;
                            for (int j = 0; j < Dim; j++)
                                gramF[gRow + j] += wfi * f[j];
                        }
                    }
                }

                // Solve in double (fallback: weighted mean — β₀ only).
                float sumW = gramF[0];
                if (sumW <= 0f) continue;
                for (int i = 0; i < Dim * Dim; i++) s.Gram[i] = gramF[i];
                for (int i = 0; i < 3 * Dim; i++) s.Rhs[i] = rhsF[i];
                for (int ch = 0; ch < 3; ch++) meanFallback[ch] = s.Rhs[ch * Dim] / sumW;

                bool solved = RegressionSolver.Solve(s.Gram, s.Rhs, Dim, 3);
                if (solved)
                {
                    for (int i = 0; i < 3 * Dim; i++) betaF[i] = (float)s.Rhs[i];
                }
                else
                {
                    Array.Clear(betaF);
                    for (int ch = 0; ch < 3; ch++) betaF[ch * Dim] = (float)meanFallback[ch];
                }

                // Splat the window predictions.
                var numK = num[ki]; var denK = den[ki];
                if (v256)
                {
                    var b0 = Vector256.LoadUnsafe(ref betaF[0]);
                    var b1 = Vector256.LoadUnsafe(ref betaF[Dim]);
                    var b2 = Vector256.LoadUnsafe(ref betaF[2 * Dim]);
                    for (int o = 0; o < offsets; o++)
                    {
                        float wgt = s.WeightBuf[o];
                        if (wgt <= 0f) continue;
                        int q = p + dys[o] * w + dxs[o];
                        f[0] = 1f;
                        for (int fpI = 0; fpI < FeaturePlanes; fpI++)
                            f[fpI + 1] = feat[fpI * n + q] - fp[fpI];
                        var fv = Vector256.LoadUnsafe(ref f[0]);
                        numK[q] += wgt * Vector256.Sum(b0 * fv);
                        numK[n + q] += wgt * Vector256.Sum(b1 * fv);
                        numK[2 * n + q] += wgt * Vector256.Sum(b2 * fv);
                        denK[q] += wgt;
                    }
                }
                else
                {
                    for (int o = 0; o < offsets; o++)
                    {
                        float wgt = s.WeightBuf[o];
                        if (wgt <= 0f) continue;
                        int q = p + dys[o] * w + dxs[o];
                        f[0] = 1f;
                        for (int fpI = 0; fpI < FeaturePlanes; fpI++)
                            f[fpI + 1] = feat[fpI * n + q] - fp[fpI];
                        for (int ch = 0; ch < 3; ch++)
                        {
                            float pred = 0f;
                            int bBase = ch * Dim;
                            for (int i = 0; i < Dim; i++) pred += betaF[bBase + i] * f[i];
                            numK[ch * n + q] += wgt * pred;
                        }
                        denK[q] += wgt;
                    }
                }
            }
        }
    }
}
