namespace RayTracer.Core;

/// <summary>
/// Numerical root finder for univariate real polynomials of arbitrary degree.
///
/// Given a polynomial p(x) = Σ a_k x^k (k = 0..degree), the solver isolates and
/// refines every real root lying in a caller-specified interval [a, b].
///
/// The algorithm is the classical Sturm chain + Newton-Raphson hybrid used by
/// PovRay's <c>lathe</c>, PBRT's <c>Curve</c>, and most production path tracers
/// for the ray-surface intersection of high-degree implicit surfaces:
///
///   1. Build the Sturm chain   p_0 = p, p_1 = p', p_{k+1} = -rem(p_{k-1}, p_k)
///   2. For any x the number of sign changes V(x) in the chain satisfies
///         #roots in (a, b] = V(a) - V(b)
///      (Sturm's theorem). Bisecting [a, b] and counting on each half isolates
///      every real root into a sub-interval that contains exactly one.
///   3. Each isolated bracket is refined to full double precision by Newton
///      with bisection fallback — convergence is quadratic once inside the
///      basin, linear worst-case if Newton leaves the bracket.
///
/// Scope and limitations:
///   - Works for any degree, but is tuned for degree 2..8. Stack buffers are
///     sized for up to degree 16.
///   - Endpoints of [a, b] are treated as open on the left: a root exactly at
///     <c>a</c> is not reported, but a root exactly at <c>b</c> is. This is the
///     standard Sturm convention and matches what the lathe solver expects
///     (u = 0 belongs to the previous segment).
///   - Multiple (non-simple) roots are detected but only reported once. Triple
///     roots and higher are reliably found via the bisection fallback when
///     Newton stalls.
/// </summary>
public static class SturmSolver
{
    /// <summary>Maximum polynomial degree supported by the stack-allocated buffers.</summary>
    public const int MaxDegree = 16;

    /// <summary>
    /// Finds every real root of the polynomial whose coefficients are laid out in
    /// <paramref name="coeffs"/> as <c>coeffs[k] = a_k</c> (constant first, leading
    /// last), within the open-closed interval <c>(a, b]</c>. The roots are written
    /// in ascending order to <paramref name="roots"/> and the count returned.
    /// </summary>
    /// <param name="coeffs">Polynomial coefficients, <c>coeffs[0]</c> is the constant term.</param>
    /// <param name="a">Lower bound of the search interval (exclusive).</param>
    /// <param name="b">Upper bound of the search interval (inclusive).</param>
    /// <param name="roots">Output buffer, must be at least <c>degree</c> long.</param>
    /// <param name="tolerance">Root residual tolerance (default 1e-9).</param>
    public static int FindRoots(
        Span<double> coeffs,
        double a,
        double b,
        Span<double> roots,
        double tolerance = 1e-9)
    {
        int degree = coeffs.Length - 1;
        while (degree > 0 && System.Math.Abs(coeffs[degree]) < 1e-300)
            degree--;

        if (degree <= 0) return 0;
        if (b <= a) return 0;
        if (degree > MaxDegree)
            throw new ArgumentException($"Polynomial degree {degree} exceeds MaxDegree {MaxDegree}.");

        // Build the Sturm chain. chain[i] holds the coefficients of p_i,
        // lengths[i] holds the effective length (degree+1) of p_i.
        // We lay out all polynomials back-to-back in a single flat buffer so
        // that no allocation is needed on the hot path.
        Span<double> chainBuf = stackalloc double[(MaxDegree + 1) * (MaxDegree + 1)];
        Span<int> lengths     = stackalloc int[MaxDegree + 1];
        int chainCount = BuildSturmChain(coeffs[..(degree + 1)], chainBuf, lengths);

        int va = CountSignChanges(chainBuf, lengths, chainCount, a);
        int vb = CountSignChanges(chainBuf, lengths, chainCount, b);
        int totalRoots = va - vb;
        if (totalRoots <= 0) return 0;

        int found = 0;
        IsolateAndRefine(chainBuf, lengths, chainCount, coeffs[..(degree + 1)],
                         a, b, va, vb, roots, ref found, tolerance);

        // Sort ascending (bubble sort is fine for degree ≤ 16).
        for (int i = 1; i < found; i++)
        {
            double key = roots[i];
            int j = i - 1;
            while (j >= 0 && roots[j] > key) { roots[j + 1] = roots[j]; j--; }
            roots[j + 1] = key;
        }

        return found;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Sturm chain construction
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the Sturm chain into a flat buffer. Returns the chain length.
    /// Each polynomial p_i is stored at offset <c>i*(MaxDegree+1)</c> with
    /// length <paramref name="lengths"/>[i] (number of coefficients, constant
    /// first).
    /// </summary>
    private static int BuildSturmChain(
        ReadOnlySpan<double> p,
        Span<double> chainBuf,
        Span<int> lengths)
    {
        int stride = MaxDegree + 1;

        // chain[0] = p. We scale every chain entry by a POSITIVE factor only
        // (1 / |leading|) so numbers stay in a predictable range without
        // flipping signs — Sturm counting depends on the actual signs of the
        // coefficients, so multiplying by a negative constant would invalidate
        // the theorem.
        ScaleIntoChain(p, chainBuf, 0);
        lengths[0] = p.Length;

        // chain[1] = p' (formal derivative).
        int d = p.Length - 1;
        Span<double> pPrime = stackalloc double[stride];
        for (int k = 1; k <= d; k++)
            pPrime[k - 1] = p[k] * k;
        ScaleIntoChain(pPrime[..d], chainBuf, stride);
        lengths[1] = d;

        int count = 2;

        // Preallocate the scratch buffers once — the loop runs at most
        // 'degree' times so reusing these avoids any stack-growth concern.
        Span<double> remainder = stackalloc double[stride];
        Span<double> work      = stackalloc double[stride];

        // Successive negative remainders: chain[i+1] = -rem(chain[i-1], chain[i]).
        while (lengths[count - 1] > 1)
        {
            int prev2Off = (count - 2) * stride;
            int prev1Off = (count - 1) * stride;
            int nextOff  = count * stride;
            int prev2Len = lengths[count - 2];
            int prev1Len = lengths[count - 1];

            // Long-divide chain[count-2] by chain[count-1], keep the remainder.
            remainder.Clear();
            work.Clear();
            int remLen = PolynomialRemainder(
                chainBuf.Slice(prev2Off, prev2Len),
                chainBuf.Slice(prev1Off, prev1Len),
                remainder,
                work);

            if (remLen == 0) break; // Exact division — chain terminates here (repeated roots).

            // Negate the remainder (the sign matters — this is the Sturm
            // recurrence's defining property).
            for (int k = 0; k < remLen; k++) remainder[k] = -remainder[k];
            ScaleIntoChain(remainder[..remLen], chainBuf, nextOff);
            lengths[count] = remLen;
            count++;

            if (count > MaxDegree) break;
        }

        return count;
    }

    /// <summary>
    /// Copies <paramref name="src"/> to <c>chainBuf[destOff..]</c> after scaling
    /// by the reciprocal of <c>|leading|</c>. A zero leading coefficient is
    /// copied verbatim. Using the absolute value is essential — scaling by the
    /// signed leading coefficient would flip the polynomial's signs whenever
    /// that coefficient is negative and invalidate the Sturm sign counting.
    /// </summary>
    private static void ScaleIntoChain(ReadOnlySpan<double> src, Span<double> chainBuf, int destOff)
    {
        double lead = src[^1];
        double absLead = System.Math.Abs(lead);
        if (absLead < 1e-300)
        {
            for (int i = 0; i < src.Length; i++)
                chainBuf[destOff + i] = src[i];
            return;
        }
        double inv = 1.0 / absLead;
        for (int i = 0; i < src.Length; i++)
            chainBuf[destOff + i] = src[i] * inv;
    }

    /// <summary>
    /// Standard polynomial long division. Returns the length of the remainder
    /// (0 when the division is exact).  Both inputs and the output are in
    /// constant-first form.
    /// </summary>
    private static int PolynomialRemainder(
        ReadOnlySpan<double> num,
        ReadOnlySpan<double> den,
        Span<double> remainder,
        Span<double> work)
    {
        int nLen = num.Length;
        int dLen = den.Length;
        if (dLen == 0) return 0;

        for (int i = 0; i < nLen; i++) work[i] = num[i];

        double lead = den[dLen - 1];
        if (System.Math.Abs(lead) < 1e-300) return 0;

        for (int i = nLen - 1; i >= dLen - 1; i--)
        {
            double factor = work[i] / lead;
            for (int j = 0; j < dLen; j++)
                work[i - (dLen - 1) + j] -= factor * den[j];
        }

        // Remainder occupies positions 0..(dLen - 2). Trim trailing zeros.
        int remLen = dLen - 1;
        while (remLen > 0 && System.Math.Abs(work[remLen - 1]) < 1e-14) remLen--;
        for (int i = 0; i < remLen; i++) remainder[i] = work[i];
        return remLen;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Sign counting + evaluation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Counts the number of sign changes in the Sturm chain evaluated at
    /// <paramref name="x"/>. A value whose magnitude is dominated by the
    /// polynomial's natural roundoff floor (<c>1e-12 × Σ|a_k|·|x|^k</c>) is
    /// treated as a true zero and skipped, matching the classical convention
    /// for Sturm counting at (or near) a root and preventing the solver from
    /// losing a root when the interval endpoint happens to coincide with it.
    /// </summary>
    private static int CountSignChanges(
        ReadOnlySpan<double> chainBuf,
        ReadOnlySpan<int> lengths,
        int chainCount,
        double x)
    {
        int stride = MaxDegree + 1;
        int changes = 0;
        int prevSign = 0;
        double absX = System.Math.Abs(x);
        for (int i = 0; i < chainCount; i++)
        {
            var slice = chainBuf.Slice(i * stride, lengths[i]);
            double v = EvalPolynomial(slice, x);

            // Natural tolerance: |p(x)| ≤ Σ|a_k|·|x|^k, and catastrophic
            // cancellation rarely exceeds ~1e-14 of that bound for degree ≤ 16.
            double scale = 0.0;
            double xPow = 1.0;
            for (int k = 0; k < slice.Length; k++)
            {
                scale += System.Math.Abs(slice[k]) * xPow;
                xPow *= absX;
            }
            double tol = 1e-12 * System.Math.Max(1.0, scale);

            int sign = v > tol ? 1 : (v < -tol ? -1 : 0);
            if (sign != 0)
            {
                if (prevSign != 0 && sign != prevSign) changes++;
                prevSign = sign;
            }
        }
        return changes;
    }

    /// <summary>
    /// Horner-scheme evaluation of a polynomial in constant-first form.
    /// </summary>
    private static double EvalPolynomial(ReadOnlySpan<double> coeffs, double x)
    {
        if (coeffs.Length == 0) return 0.0;
        double acc = coeffs[^1];
        for (int i = coeffs.Length - 2; i >= 0; i--)
            acc = acc * x + coeffs[i];
        return acc;
    }

    private static double EvalDerivative(ReadOnlySpan<double> coeffs, double x)
    {
        // p'(x) = Σ k · a_k · x^{k-1}
        if (coeffs.Length <= 1) return 0.0;
        double acc = coeffs[^1] * (coeffs.Length - 1);
        for (int i = coeffs.Length - 2; i >= 1; i--)
            acc = acc * x + coeffs[i] * i;
        return acc;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Root isolation + refinement
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recursive bisection: splits <c>[lo, hi]</c> until each sub-interval contains
    /// exactly one root (determined by Sturm's count), then hands it to Newton
    /// refinement. The <paramref name="vLo"/>/<paramref name="vHi"/> sign-change
    /// counts are cached so we never evaluate the chain twice at the same x.
    /// </summary>
    private static void IsolateAndRefine(
        ReadOnlySpan<double> chainBuf,
        ReadOnlySpan<int> lengths,
        int chainCount,
        ReadOnlySpan<double> originalCoeffs,
        double lo, double hi,
        int vLo, int vHi,
        Span<double> roots,
        ref int found,
        double tolerance)
    {
        int nRoots = vLo - vHi;
        if (nRoots <= 0) return;

        if (nRoots == 1)
        {
            double r = RefineRoot(originalCoeffs, lo, hi, tolerance);
            if (found < roots.Length)
                roots[found++] = r;
            return;
        }

        double mid = 0.5 * (lo + hi);

        // Degenerate bracket: the interval can't be split further in double
        // precision. Accept 'nRoots' coincident roots at the midpoint — this
        // is the correct answer for roots with multiplicity > 1.
        if ((hi - lo) < 1e-14 * System.Math.Max(1.0, System.Math.Abs(mid)))
        {
            if (found < roots.Length)
                roots[found++] = mid;
            return;
        }

        int vMid = CountSignChanges(chainBuf, lengths, chainCount, mid);
        IsolateAndRefine(chainBuf, lengths, chainCount, originalCoeffs, lo,  mid, vLo,  vMid, roots, ref found, tolerance);
        IsolateAndRefine(chainBuf, lengths, chainCount, originalCoeffs, mid, hi,  vMid, vHi,  roots, ref found, tolerance);
    }

    /// <summary>
    /// Refines a single simple root lying in <c>[lo, hi]</c> via Newton-Raphson
    /// with bisection fallback. Bisection keeps a guaranteed bracket so that
    /// even a Newton step that leaps out of the basin cannot lose the root.
    ///
    /// Uses the original polynomial (not a chain entry) to minimise accumulated
    /// error from the normalisation done during chain construction.
    /// </summary>
    private static double RefineRoot(
        ReadOnlySpan<double> coeffs,
        double lo, double hi,
        double tolerance)
    {
        double fLo = EvalPolynomial(coeffs, lo);
        double fHi = EvalPolynomial(coeffs, hi);

        if (System.Math.Abs(fLo) < tolerance) return lo;
        if (System.Math.Abs(fHi) < tolerance) return hi;

        // Keep [lo, hi] as a signed bracket: fLo and fHi must have opposite signs.
        // If they don't (can happen at chain boundaries due to normalisation),
        // fall back to pure bisection.
        bool bracketed = fLo * fHi < 0.0;

        double x = 0.5 * (lo + hi);

        for (int iter = 0; iter < 60; iter++)
        {
            double f  = EvalPolynomial(coeffs, x);
            if (System.Math.Abs(f) < tolerance) return x;

            double fp = EvalDerivative(coeffs, x);

            // Tighten the bracket with the current sample.
            if (bracketed)
            {
                if (f * fLo < 0.0) { hi = x; fHi = f; }
                else               { lo = x; fLo = f; }
            }

            double xNewton = double.NaN;
            if (System.Math.Abs(fp) > 1e-300)
                xNewton = x - f / fp;

            // Accept Newton only when it lands strictly inside the bracket —
            // this is the standard safeguard that makes Newton-with-bisection
            // globally convergent even on tangent-like rays where fp → 0 near
            // the root.
            bool useNewton = !double.IsNaN(xNewton) && xNewton > lo && xNewton < hi;
            double xNext = useNewton ? xNewton : 0.5 * (lo + hi);

            if (System.Math.Abs(xNext - x) < 1e-15 * System.Math.Max(1.0, System.Math.Abs(xNext)))
                return xNext;

            x = xNext;
        }

        return x;
    }
}
