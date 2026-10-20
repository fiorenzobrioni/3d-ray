using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Statistical checks for any <see cref="IMaterial"/> exposing the
/// Sample / Evaluate / Pdf API (PIANO-REVIEW.md, Phase 0, §2.2.1). The
/// renderer weights a non-delta sample by <c>F · |cos| / Pdf</c> and a delta
/// sample by <c>F</c> alone, so these are the quantities checked:
///
/// <list type="bullet">
/// <item><b>Chi-square</b> (Pearson, Mitsuba-style): the histogram of sampled
/// non-delta directions over an equal-area (cosθ, φ) grid against the
/// expected counts from integrating <c>Pdf</c> over each cell. Catches a
/// sampler that does not draw from the distribution its Pdf claims. Only the
/// BSDF's support (cells where <c>Evaluate</c> is non-zero) is compared, and
/// as a conditional distribution: materials may legitimately report Pdf mass
/// for directions whose sample they then reject (the VNDF convention of
/// reflecting into the lower hemisphere), and the overall normalisation is
/// what the albedo cross-check verifies.</item>
/// <item><b>Pdf integral</b> over the sphere: must equal the probability of
/// a non-delta sample, measured from the sampler.</item>
/// <item><b>Albedo</b> (white furnace): the mean sample weight. A material
/// without absorption must not exceed 1 in any channel.</item>
/// <item><b>Albedo cross-check</b>: the non-delta part of the albedo from
/// the sampler against <c>∫ Evaluate · |cos| dω</c> by quadrature. A
/// mismatch means the sample weight is not the estimator the Pdf implies.</item>
/// <item><b>Sample consistency</b>: a non-delta sample's F and Pdf must
/// equal <c>Evaluate</c> and <c>Pdf</c> at the sampled direction.</item>
/// <item><b>Reciprocity</b>: <c>f(V, L) = f(L, V)</c> for reflection.</item>
/// </list>
///
/// Shading frame: N = +Y. Sampling goes through <see cref="MathUtils.RandomFloat"/>,
/// which falls back to the thread PRNG outside a render, so callers seed it
/// with <see cref="MathUtils.ReseedThread"/> for reproducible results.
/// </summary>
internal static class BsdfTestKit
{
    public static readonly Vector3 N = Vector3.UnitY;

    public static HitRecord MakeRec(bool frontFace = true) => new()
    {
        Normal = N,
        ShadingNg = N,
        Point = Vector3.Zero,
        LocalPoint = Vector3.Zero,
        Tangent = Vector3.UnitX,
        Bitangent = -Vector3.UnitZ,
        U = 0.5f,
        V = 0.5f,
        ObjectSeed = 0,
        FrontFace = frontFace,
    };

    /// <summary>View direction at the given cosine to the normal, in the XY plane.</summary>
    public static Vector3 View(float cosTheta)
    {
        float s = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        return new Vector3(s, cosTheta, 0f);
    }

    public sealed record Report(
        string Name,
        float CosView,
        double ChiSquare,
        int Dof,
        double PValue,
        double PdfIntegral,
        double NonDeltaFraction,
        Vector3 Albedo,
        Vector3 AlbedoNonDeltaSampled,
        Vector3 AlbedoNonDeltaQuadrature,
        Vector3 AlbedoNonDeltaStdErr,
        int InconsistentSamples,
        int NullSamples,
        int OutsideSupport,
        int Samples)
    {
        /// <summary>True when the sampled and quadrature albedo agree within five
        /// standard errors of the Monte Carlo estimate plus 0.2% for the quadrature.</summary>
        public bool AlbedoCrossCheckPasses
        {
            get
            {
                for (int c = 0; c < 3; c++)
                {
                    double a = Get(AlbedoNonDeltaSampled, c), b = Get(AlbedoNonDeltaQuadrature, c);
                    double tol = 5 * Get(AlbedoNonDeltaStdErr, c) + 2e-3 * Math.Max(a, b) + 1e-4;
                    if (Math.Abs(a - b) > tol) return false;
                }
                return true;
            }
        }

        /// <summary>Largest relative gap between the two non-delta albedo estimates.</summary>
        public double AlbedoCrossCheckError
        {
            get
            {
                double worst = 0;
                for (int c = 0; c < 3; c++)
                {
                    double a = Get(AlbedoNonDeltaSampled, c), b = Get(AlbedoNonDeltaQuadrature, c);
                    double denom = Math.Max(Math.Max(a, b), 1e-3);
                    worst = Math.Max(worst, Math.Abs(a - b) / denom);
                }
                return worst;
            }
        }

        public double PdfIntegralError => Math.Abs(PdfIntegral - NonDeltaFraction);

        public float MaxAlbedo => MathF.Max(Albedo.X, MathF.Max(Albedo.Y, Albedo.Z));

        public override string ToString() =>
            $"{Name,-28} cosV={CosView:0.00} chi2={ChiSquare,9:0.0} dof={Dof,4} p={PValue:0.000e0} " +
            $"pdf∫={PdfIntegral:0.0000} nonDelta={NonDeltaFraction:0.0000} " +
            $"albedo=({Albedo.X:0.0000},{Albedo.Y:0.0000},{Albedo.Z:0.0000}) " +
            $"xcheck={AlbedoCrossCheckError:0.0000} inconsistent={InconsistentSamples} null={NullSamples} offSupport={OutsideSupport} " +
            $"deltaPart=({Albedo.X - AlbedoNonDeltaSampled.X:0.0000})";
    }

    private static Vector3 StdErr(Vector3 sum, Vector3 sumSq, int n)
    {
        Vector3 mean = sum / n;
        Vector3 var = Vector3.Max(sumSq / n - mean * mean, Vector3.Zero);
        return Vector3.SquareRoot(var / n);
    }

    private static double Get(Vector3 v, int c) => c == 0 ? v.X : c == 1 ? v.Y : v.Z;

    /// <summary>
    /// Runs every check for one material and view direction.
    /// </summary>
    /// <param name="cosBins">Equal-area bins in cosθ over [-1, 1].</param>
    /// <param name="phiBins">Bins in φ over [0, 2π).</param>
    /// <param name="sub">Quadrature points per cell side for the Pdf and Evaluate integrals.</param>
    public static Report Run(string name, IMaterial material, float cosView, int samples = 400_000,
                             int cosBins = 20, int phiBins = 40, int sub = 8, int seed = 1234, bool frontFace = true,
                             List<string>? diagnostics = null)
    {
        MathUtils.ReseedThread(seed);
        HitRecord rec = MakeRec(frontFace);
        Vector3 v = View(cosView);

        // ── Sampling pass ────────────────────────────────────────────────────
        var observed = new double[cosBins * phiBins];
        int nonDelta = 0, nulls = 0, inconsistent = 0;
        Vector3 albedoSum = Vector3.Zero, albedoNonDeltaSum = Vector3.Zero, albedoNonDeltaSq = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            BsdfSample? s = material.Sample(v, rec);
            if (s is not { } bs) { nulls++; continue; }
            if (bs.IsDelta)
            {
                albedoSum += bs.F;
                continue;
            }
            Vector3 wo = Vector3.Normalize(bs.Wo);
            if (bs.Pdf <= 0f || !float.IsFinite(bs.Pdf)) { inconsistent++; continue; }
            nonDelta++;
            Vector3 weight = bs.F * MathF.Abs(Vector3.Dot(N, wo)) / bs.Pdf;
            albedoSum += weight;
            albedoNonDeltaSum += weight;
            albedoNonDeltaSq += weight * weight;
            observed[CellIndex(wo, cosBins, phiBins)]++;

            // Consistency of the returned value with the analytic API.
            Vector3 fEval = material.Evaluate(v, wo, rec);
            float pEval = material.Pdf(v, wo, rec);
            if (!Close(bs.Pdf, pEval) || !Close(bs.F, fEval)) inconsistent++;
        }

        // ── Quadrature pass: expected counts, ∫pdf, ∫f|cos| ─────────────────
        var expected = new double[cosBins * phiBins];
        var support = new bool[cosBins * phiBins];
        double pdfIntegral = 0;
        double[] fIntegral = new double[3];
        double dCos = 2.0 / cosBins, dPhi = 2.0 * Math.PI / phiBins;
        double dA = dCos * dPhi / (sub * sub);
        for (int ci = 0; ci < cosBins; ci++)
        for (int pj = 0; pj < phiBins; pj++)
        {
            double cellPdf = 0;
            for (int a = 0; a < sub; a++)
            for (int b = 0; b < sub; b++)
            {
                double cosT = -1.0 + (ci + (a + 0.5) / sub) * dCos;
                double phi = (pj + (b + 0.5) / sub) * dPhi;
                Vector3 wo = SphericalToDir(cosT, phi);
                float p = material.Pdf(v, wo, rec);
                if (p > 0f && float.IsFinite(p)) cellPdf += p * dA;
                Vector3 f = material.Evaluate(v, wo, rec) * MathF.Abs((float)cosT);
                if (f.X > 0f || f.Y > 0f || f.Z > 0f) support[ci * phiBins + pj] = true;
                fIntegral[0] += f.X * dA; fIntegral[1] += f.Y * dA; fIntegral[2] += f.Z * dA;
            }
            expected[ci * phiBins + pj] = cellPdf;
            pdfIntegral += cellPdf;
        }

        // Conditional distribution on the support: rescale the Pdf mass of the
        // support cells to the number of samples that landed there.
        double supportMass = 0, supportSamples = 0;
        int outsideSupport = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            if (support[i]) { supportMass += expected[i]; supportSamples += observed[i]; }
            else { outsideSupport += (int)observed[i]; observed[i] = 0; expected[i] = 0; }
        }
        for (int i = 0; i < expected.Length; i++)
            expected[i] = supportMass > 0 ? expected[i] / supportMass * supportSamples : 0;

        if (diagnostics != null)
        {
            var worst = Enumerable.Range(0, expected.Length)
                .Where(i => expected[i] > 0 || observed[i] > 0)
                .OrderByDescending(i => Math.Abs(observed[i] - expected[i]) / Math.Sqrt(Math.Max(expected[i], 1)))
                .Take(12);
            foreach (int i in worst)
            {
                int ci = i / phiBins, pj = i % phiBins;
                double cosLo = -1.0 + ci * dCos;
                diagnostics.Add($"  cell cos[{cosLo:0.00},{cosLo + dCos:0.00}] phi[{pj * dPhi * 180 / Math.PI:0},{(pj + 1) * dPhi * 180 / Math.PI:0}] " +
                                $"obs={observed[i]:0} exp={expected[i]:0.0} ratio={(expected[i] > 0 ? observed[i] / expected[i] : double.PositiveInfinity):0.000}");
            }
        }

        var (chi2, dof) = ChiSquare(observed, expected);
        double pValue = dof > 0 ? RegularizedGammaQ(dof / 2.0, chi2 / 2.0) : 1.0;

        return new Report(
            name, cosView, chi2, dof, pValue,
            pdfIntegral, (double)nonDelta / samples,
            albedoSum / samples, albedoNonDeltaSum / samples,
            new Vector3((float)fIntegral[0], (float)fIntegral[1], (float)fIntegral[2]),
            StdErr(albedoNonDeltaSum, albedoNonDeltaSq, samples),
            inconsistent, nulls, outsideSupport, samples);
    }

    /// <summary>Largest relative reciprocity error |f(V,L) − f(L,V)| over random
    /// pairs of upper-hemisphere directions.</summary>
    public static double ReciprocityError(IMaterial material, int pairs = 2000, int seed = 99)
    {
        var rng = new Random(seed);
        HitRecord rec = MakeRec();
        double worst = 0;
        for (int i = 0; i < pairs; i++)
        {
            Vector3 a = UpperHemisphere(rng), b = UpperHemisphere(rng);
            Vector3 fab = material.Evaluate(a, b, rec), fba = material.Evaluate(b, a, rec);
            for (int c = 0; c < 3; c++)
            {
                double x = Get(fab, c), y = Get(fba, c);
                double denom = Math.Max(Math.Max(x, y), 1e-2);
                worst = Math.Max(worst, Math.Abs(x - y) / denom);
            }
        }
        return worst;
    }

    /// <summary>Largest relative gap between the NEE integrand the renderer uses
    /// for direct light (<see cref="IMaterial.EvaluateDirect"/>) and the BSDF the
    /// sampler and MIS use (<c>Evaluate · cos</c>). Both estimators of the same
    /// integral must agree, or MIS blends two different materials.</summary>
    public static double DirectVsEvaluateError(IMaterial material, int pairs = 2000, int seed = 7)
    {
        var rng = new Random(seed);
        HitRecord rec = MakeRec();
        double worst = 0;
        for (int i = 0; i < pairs; i++)
        {
            Vector3 v = UpperHemisphere(rng), l = UpperHemisphere(rng);
            Vector3 direct = material.EvaluateDirect(l, v, N, rec);
            Vector3 eval = material.Evaluate(v, l, rec) * Vector3.Dot(N, l);
            for (int c = 0; c < 3; c++)
            {
                double x = Get(direct, c), y = Get(eval, c);
                double denom = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), 1e-2);
                worst = Math.Max(worst, Math.Abs(x - y) / denom);
            }
        }
        return worst;
    }

    // ── Chi-square with pooling of sparse cells (Mitsuba convention) ────────
    private static (double chi2, int dof) ChiSquare(double[] observed, double[] expected)
    {
        const double MinExpected = 5.0;
        var order = Enumerable.Range(0, expected.Length).OrderBy(i => expected[i]).ToArray();
        double chi2 = 0, pooledObs = 0, pooledExp = 0;
        int cells = 0;
        foreach (int i in order)
        {
            if (expected[i] == 0)
            {
                // Samples where the Pdf says none can land: decisive failure.
                if (observed[i] > 0) return (double.PositiveInfinity, 1);
                continue;
            }
            if (expected[i] < MinExpected)
            {
                pooledObs += observed[i];
                pooledExp += expected[i];
                continue;
            }
            double d = observed[i] - expected[i];
            chi2 += d * d / expected[i];
            cells++;
        }
        if (pooledExp > 0)
        {
            double d = pooledObs - pooledExp;
            chi2 += d * d / pooledExp;
            cells++;
        }
        return (chi2, Math.Max(cells - 1, 0));
    }

    private static int CellIndex(Vector3 wo, int cosBins, int phiBins)
    {
        // Same parameterisation as SphericalToDir: cosθ along +Y, φ in the XZ plane.
        double cosT = Math.Clamp(wo.Y, -1f, 1f);
        double phi = Math.Atan2(wo.Z, wo.X);
        if (phi < 0) phi += 2 * Math.PI;
        int ci = Math.Clamp((int)((cosT + 1.0) / 2.0 * cosBins), 0, cosBins - 1);
        int pj = Math.Clamp((int)(phi / (2 * Math.PI) * phiBins), 0, phiBins - 1);
        return ci * phiBins + pj;
    }

    private static Vector3 SphericalToDir(double cosT, double phi)
    {
        double s = Math.Sqrt(Math.Max(0, 1 - cosT * cosT));
        return new Vector3((float)(s * Math.Cos(phi)), (float)cosT, (float)(s * Math.Sin(phi)));
    }

    private static Vector3 UpperHemisphere(Random rng)
    {
        double z = rng.NextDouble();
        double r = Math.Sqrt(1 - z * z), phi = 2 * Math.PI * rng.NextDouble();
        return new Vector3((float)(r * Math.Cos(phi)), (float)Math.Max(z, 1e-3), (float)(r * Math.Sin(phi)));
    }

    private static bool Close(float a, float b) =>
        MathF.Abs(a - b) <= 1e-3f * MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));

    private static bool Close(Vector3 a, Vector3 b) => Close(a.X, b.X) && Close(a.Y, b.Y) && Close(a.Z, b.Z);

    // ── Regularised upper incomplete gamma Q(a, x) (Numerical Recipes §6.2) ─
    public static double RegularizedGammaQ(double a, double x)
    {
        if (double.IsPositiveInfinity(x)) return 0;
        if (x <= 0) return 1;
        if (x < a + 1)
        {
            // Series for P, Q = 1 − P.
            double sum = 1.0 / a, term = sum, ap = a;
            for (int n = 0; n < 1000; n++)
            {
                ap += 1;
                term *= x / ap;
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * 1e-15) break;
            }
            return 1.0 - sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }
        // Continued fraction (modified Lentz).
        double bb = x + 1 - a, c = 1e300, d = 1 / bb, h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            bb += 2;
            d = an * d + bb; if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = bb + an / c; if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-15) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    private static double LogGamma(double x)
    {
        // Lanczos approximation (g = 7, n = 9).
        double[] g =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7,
        };
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        x -= 1;
        double sum = g[0];
        for (int i = 1; i < 9; i++) sum += g[i] / (x + i);
        double t = x + 7.5;
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }
}
