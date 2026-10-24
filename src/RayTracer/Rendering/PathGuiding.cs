using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering.Guiding;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

/// <summary>
/// Path guiding (PIANO-REVIEW.md Phase 7b; Müller, Gross, Novák 2017): before
/// the render, passes of 1, 2, 4, ... samples per pixel learn where the
/// radiance comes from at each point of the scene (<see cref="PathGuide"/>).
/// The render then draws each bounce at a non-specular vertex either from the
/// BSDF (or phase function) or from that distribution, and weighs it by the
/// density of the mixture (one-sample MIS), so the estimate stays unbiased
/// whatever the guide learned. Next-event estimation weighs its samples with
/// the same mixture density, so the two MIS strategies still pair.
/// </summary>
public partial class Renderer
{
    private PathGuide? _guide;
    private AABB _finiteSceneBounds;

    // Training passes kept for the render, weighted by inverse variance
    // (CombineGuidedPasses): per pixel the weighted sums of the even- and
    // odd-indexed samples (the dual-buffer halves), their total weights, and
    // how many samples the passes took.
    private Vector3[,]? _guideSumA, _guideSumB;
    private double _guideNormA, _guideNormB;
    private int _guideTrainCount;

    /// <summary>One pass's per-pixel sums: halves by sample index, and luminance squared.</summary>
    private sealed class GuidedPassSums
    {
        public readonly Vector3[,] A, B;
        public readonly float[,] Lum2;
        public int CountA, CountB;     // samples per pixel in each half

        public GuidedPassSums(int width, int height)
        {
            A = new Vector3[height, width];
            B = new Vector3[height, width];
            Lum2 = new float[height, width];
        }

        public void Add(int i, int j, int index, Vector3 sample)
        {
            if ((index & 1) == 0) A[j, i] += sample; else B[j, i] += sample;
            float l = MathUtils.Luminance(sample);
            Lum2[j, i] += l * l;
        }

        /// <summary>Sets the half counts for <paramref name="n"/> samples starting at global index <paramref name="first"/>.</summary>
        public void Count(int first, int n)
        {
            CountA = (n + ((first & 1) == 0 ? 1 : 0)) / 2;
            CountB = n - CountA;
        }

        /// <summary>
        /// Variance of one sample's luminance, averaged over the pixels (the
        /// pass's noise per sample); 0 when it cannot be told (one sample).
        /// </summary>
        public double SampleVariance(int n)
        {
            if (n < 2) return 0;
            int h = A.GetLength(0), w = A.GetLength(1);
            double sum = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    double l = MathUtils.Luminance(A[j, i] + B[j, i]);
                    sum += Math.Max(0, (Lum2[j, i] - l * l / n) / (n - 1));
                }
            return sum / (w * h);
        }
    }

    /// <summary>
    /// Weight of one sample of a pass in the combined image: the inverse of
    /// the pass's variance per sample (Müller 2019, "Practical Path Guiding in
    /// Production"), so the early passes, drawn with little or no guide, count
    /// less than the late ones. In the reference estimator every sample
    /// weighs the same: weights estimated from the image itself correlate
    /// with it and bias the mean, by far less than the noise but not zero.
    /// </summary>
    private double PassWeight(double sampleVariance)
        => _referenceMode || !(sampleVariance > 0) || !double.IsFinite(sampleVariance) ? 1.0 : 1.0 / sampleVariance;

    /// <summary>
    /// Folds the training passes into the render: per pixel, the passes'
    /// weighted sums (each sample weighed by <see cref="PassWeight"/>) over
    /// their total weight, for the beauty and its two halves. Rewrites the
    /// display pixels and the beauty planes the render loop wrote.
    /// </summary>
    private void CombineGuidedPasses(GuidedPassSums final, int finalSamples, int trained,
                                     Vector3[,] pixels, RenderBuffers? buffers)
    {
        int h = pixels.GetLength(0), w = pixels.GetLength(1);
        final.Count(trained, finalSamples);
        double wf = PassWeight(final.SampleVariance(finalSamples));
        double normA = _guideNormA + wf * final.CountA, normB = _guideNormB + wf * final.CountB;
        var sumA = _guideSumA!;
        var sumB = _guideSumB!;
        Parallel.For(0, h, j =>
        {
            for (int i = 0; i < w; i++)
            {
                Vector3 a = sumA[j, i] + (float)wf * final.A[j, i];
                Vector3 b = sumB[j, i] + (float)wf * final.B[j, i];
                Vector3 linear = (a + b) / (float)(normA + normB);
                pixels[j, i] = _display.Apply(linear * _exposureScale);
                if (buffers == null) continue;
                buffers.Beauty.SetRgb(i, j, linear);
                Vector3 meanA = a / (float)normA;
                buffers.BeautyA.SetRgb(i, j, meanA);
                buffers.BeautyB.SetRgb(i, j, normB > 0 ? b / (float)normB : meanA);
            }
        });
    }

    /// <summary>The trained guide, after a render with guiding on.</summary>
    public PathGuide? Guide => _guide;

    /// <summary>Turns path guiding on for the next <see cref="Render(int, int, RenderCaptureOptions)"/>.</summary>
    public void EnableGuiding(GuidingSettings settings)
        => _guide = new PathGuide(_finiteSceneBounds, settings);

    private const int MaxGuideVertices = 64;

    /// <summary>A vertex whose incident radiance the path will record.</summary>
    private struct GuideVertex
    {
        public Vector3 Position;
        public Vector3 LeafSize; // size of the region the vertex is in (the filter's box)
        public Vector2 Square;   // direction of the bounce on the guide's square
        public float Pdf;        // density the direction was drawn with
        public Vector3 Beta;     // throughput after the bounce weight, before the roulette
        public Vector3 Before;   // radiance the path had gathered before the bounce
        // Drawn from the BSDF/guide mixture: the two densities and f·cos, to
        // learn the BSDF share.
        public bool Mixed;
        public float BsdfPdf, GuidePdf;
        public Vector3 FCos;
    }

    // Records of the current tile during a training pass (null otherwise).
    [ThreadStatic] private static List<GuideRecord>? t_guideRecords;
    [ThreadStatic] private static GuideVertex[]? t_guideVertices;
    // The guide distribution at the vertex whose NEE is being evaluated.
    [ThreadStatic] private static DTree? t_guideMis;
    [ThreadStatic] private static float t_guideBsdfFraction;

    private bool GuidingOn => _guide != null && (_guide.CanSample || t_guideRecords != null);

    /// <summary>Guided at a surface: a broad reflection (<see cref="IMaterial.Guidable"/>).</summary>
    private static bool GuideEligible(IMaterial material, in HitRecord rec) => material.Guidable(in rec);

    private static float GuidePdf(DTree tree, Vector3 dir)
        => tree.PdfSquare(DTree.ToSquare(dir)) * DTree.SquareToSolidAngle;

    /// <summary>The BSDF strategy's density for the MIS of a light sample: the mixture's where guided.</summary>
    private static float MisBsdfPdf(IMaterial material, Vector3 viewDir, Vector3 dir, in HitRecord rec)
    {
        float p = material.Pdf(viewDir, dir, rec);
        DTree? g = t_guideMis;
        if (g == null) return p;
        float a = t_guideBsdfFraction;
        return a * p + (1f - a) * GuidePdf(g, dir);
    }

    /// <summary>The phase strategy's density for the MIS of a light sample in a medium.</summary>
    private static float MisPhasePdf(IPhaseFunction phase, Vector3 wo, Vector3 dir)
    {
        float p = phase.Pdf(wo, dir);
        DTree? g = t_guideMis;
        if (g == null) return p;
        float a = t_guideBsdfFraction;
        return a * p + (1f - a) * GuidePdf(g, dir);
    }

    /// <summary>
    /// Draws the bounce at a guided surface vertex: from the BSDF with
    /// probability a, else from <paramref name="tree"/>; the weight divides
    /// by the mixture density. A delta lobe the BSDF picks keeps its own
    /// weight, divided by a (the chance of having asked the BSDF). False when
    /// the path is absorbed.
    ///
    /// <para>One stratified pair decides both: its first coordinate picks the
    /// strategy, and rescaled to the chosen share it places the direction
    /// (handed to the BSDF through <see cref="Sampler.Provide2D"/>). A
    /// separate coin would give each strategy a random subset of the pixel's
    /// samples and lose the stratification: with Sobol the guided render was
    /// up to twice as noisy as the plain one before this.</para>
    /// </summary>
    private static bool SampleGuided(IMaterial material, in HitRecord rec, Vector3 viewDir, DTree tree, float a,
                                     out BsdfSample s, out Vector3 attenuation, out float pdf,
                                     out float bsdfPdf, out float guidePdf)
    {
        attenuation = Vector3.Zero;
        pdf = bsdfPdf = guidePdf = 0f;
        var (u1, u2) = MathUtils.Random2D();
        if (u1 < a)
        {
            Sampler.Provide2D(u1 / a, u2);
            BsdfSample? sampled = material.Sample(viewDir, rec);
            Sampler.ClearProvided();
            if (!sampled.HasValue) { s = default; return false; }
            s = sampled.Value;
            if (s.IsDelta)
            {
                attenuation = s.F / a;
                return true;
            }
            bsdfPdf = s.Pdf;
            guidePdf = GuidePdf(tree, s.Wo);
        }
        else
        {
            if (!tree.Sample(new Vector2((u1 - a) / (1f - a), u2), out Vector2 sq)) { s = default; return false; }
            Vector3 wo = DTree.FromSquare(sq);
            Vector3 f = material.Evaluate(viewDir, wo, rec);
            bsdfPdf = material.Pdf(viewDir, wo, rec);
            guidePdf = tree.PdfSquare(sq) * DTree.SquareToSolidAngle;
            // A guided direction is classed as diffuse for the depth limits
            // and the visibility flags: guiding serves the rough surfaces.
            s = new BsdfSample(wo, f, 0f, isDelta: false, lobe: BsdfLobe.Diffuse);
        }
        pdf = a * bsdfPdf + (1f - a) * guidePdf;

        float absCos = MathF.Abs(Vector3.Dot(rec.Normal, s.Wo));
        if (absCos <= 0f || !(pdf > 0f) || s.F == Vector3.Zero) return false;
        attenuation = s.F * (absCos * LightSideTerminator(in rec, s.Wo) / pdf);
        return true;
    }

    /// <summary>Phase-function counterpart of <see cref="SampleGuided"/> at a medium vertex.</summary>
    private static bool SamplePhaseGuided(IPhaseFunction phase, Vector3 wo, DTree tree, float a,
                                          out Vector3 wi, out float weight, out float pdf,
                                          out float phasePdf, out float guidePdf)
    {
        var (u1, u2) = MathUtils.Random2D();
        if (u1 < a)
        {
            (wi, _) = phase.Sample(wo, new Vector2(u1 / a, u2));
        }
        else
        {
            if (!tree.Sample(new Vector2((u1 - a) / (1f - a), u2), out Vector2 sq))
            {
                wi = default; weight = pdf = phasePdf = guidePdf = 0f;
                return false;
            }
            wi = DTree.FromSquare(sq);
        }
        phasePdf = phase.Pdf(wo, wi);
        guidePdf = GuidePdf(tree, wi);
        pdf = a * phasePdf + (1f - a) * guidePdf;
        if (!(pdf > 0f)) { weight = 0f; return false; }
        weight = phase.Evaluate(wo, wi) / pdf;
        return true;
    }

    /// <summary>Remembers a bounce whose incident radiance the training pass will record.</summary>
    private static void PushGuideVertex(ref PathState path, Vector3 position, Vector3 leafSize, Vector3 dir, float pdf,
                                        bool mixed = false, float bsdfPdf = 0f, float guidePdf = 0f, Vector3 fCos = default)
    {
        if (path.GuideCount >= MaxGuideVertices || !(pdf > 0f)) return;
        var v = t_guideVertices ??= new GuideVertex[MaxGuideVertices];
        v[path.GuideCount++] = new GuideVertex
        {
            Position = position,
            LeafSize = leafSize,
            Square = DTree.ToSquare(dir),
            Pdf = pdf,
            Beta = path.Beta,
            Before = path.First + path.Rest,
            Mixed = mixed,
            BsdfPdf = bsdfPdf,
            GuidePdf = guidePdf,
            FCos = fCos,
        };
    }

    /// <summary>
    /// At the end of a training path: the radiance that reached each recorded
    /// vertex along its bounce is what the path gathered after it, divided by
    /// the throughput up to that bounce; the guide records its luminance over
    /// the density the direction was drawn with.
    /// </summary>
    private void FlushGuideVertices(ref PathState path)
    {
        var guide = _guide!;
        var records = t_guideRecords;
        var v = t_guideVertices;
        if (records == null || v == null) { path.GuideCount = 0; return; }
        Vector3 total = path.First + path.Rest;
        for (int i = 0; i < path.GuideCount; i++)
        {
            Vector3 gathered = total - v[i].Before;
            Vector3 b = v[i].Beta;
            var incident = new Vector3(
                b.X > 0f ? gathered.X / b.X : 0f,
                b.Y > 0f ? gathered.Y / b.Y : 0f,
                b.Z > 0f ? gathered.Z / b.Z : 0f);
            float w = MathUtils.Luminance(incident) / v[i].Pdf;
            float product = v[i].Mixed ? MathUtils.Luminance(incident * v[i].FCos) : 0f;
            int leaf = guide.LeafAt(GuideFilter.Jitter(v[i].Position, v[i].Square, v[i].LeafSize));
            records.Add(new GuideRecord(leaf, v[i].Square, float.IsFinite(w) && w > 0f ? w : 0f,
                                        v[i].BsdfPdf, v[i].GuidePdf, product, v[i].Mixed));
        }
        path.GuideCount = 0;
    }

    /// <summary>
    /// Trains the guide before the render: passes of 1, 2, 4, ... samples per
    /// pixel until the training budget is spent, each sampling with what the
    /// previous ones learned. Every pass is an unbiased estimate of the image
    /// (its guide was built from other samples), so its samples are kept and
    /// join the render's: training costs no samples. Tiles run in parallel in
    /// batches; a batch's records are splatted in tile order.
    /// </summary>
    private void TrainGuide(int width, int height)
    {
        var guide = _guide!;
        // At least one sample per pixel left for the render.
        int budget = Math.Clamp(guide.Settings.TrainingSpp, 1, Math.Max(1, _samplesPerPixel - 1));
        const int TileSize = 16;
        int tilesX = (width + TileSize - 1) / TileSize;
        int tilesY = (height + TileSize - 1) / TileSize;
        int totalTiles = tilesX * tilesY;
        bool useSobol = Sampler.Kind == SamplerKind.Sobol;
        var started = System.Diagnostics.Stopwatch.StartNew();
        _guideSumA = new Vector3[height, width];
        _guideSumB = new Vector3[height, width];
        _guideNormA = _guideNormB = 0;

        int used = 0, passSpp = 1, pass = 0;
        var passes = new List<int>();
        // Whole doubling passes that fit the budget (at least the first).
        while (pass == 0 || used + passSpp <= budget)
        {
            passes.Add(passSpp);

            var passSums = new GuidedPassSums(width, height);

            // Bounded memory: about eight million records (≈ 260 MB) in
            // flight; larger batches mean fewer barriers between them.
            int batch = Math.Max(Environment.ProcessorCount * 4, 8_000_000 / (TileSize * TileSize * passSpp * 3));
            for (int first = 0; first < totalTiles; first += batch)
            {
                int count = Math.Min(batch, totalTiles - first);
                var lists = new List<GuideRecord>[count];
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, b =>
                {
                    int tile = first + b;
                    var records = lists[b] = new List<GuideRecord>();
                    PrepareWorker(tile, useSobol);
                    t_guideRecords = records;
                    int x0 = tile % tilesX * TileSize, y0 = tile / tilesX * TileSize;
                    int x1 = Math.Min(x0 + TileSize, width), y1 = Math.Min(y0 + TileSize, height);
                    float dsdx = MathF.Max(DifferentialFloor, 1f / MathF.Sqrt(passSpp)) / width;
                    float dtdy = MathF.Max(DifferentialFloor, 1f / MathF.Sqrt(passSpp)) / height;
                    for (int j = y0; j < y1; j++)
                    for (int i = x0; i < x1; i++)
                    {
                        // The pixel's one sequence: the passes take its first
                        // samples, the render the rest, so no guide weighs the
                        // points it learned from, and together they are the
                        // whole stratified sequence. (Separate sequences per
                        // pass lost the stratification: on Veach's plates the
                        // error was 4.8× the plain render's.)
                        uint pixelSeed = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ _seedMix;
                        for (int s = 0; s < passSpp; s++)
                        {
                            if (useSobol) Sampler.BeginPixelSample(pixelSeed, (uint)(used + s));
                            Ray ray = CameraSample(i, j, width, height, dsdx, dtdy);
                            Vector3 sample = ClampRadiance(TracePath(ray));
                            if (useSobol) Sampler.EndPixelSample();
                            // Halves by the sample's index over all passes.
                            passSums.Add(i, j, used + s, sample);
                        }
                    }
                    t_guideRecords = null;
                    ReleaseWorker();
                });
                guide.Splat(lists);
            }
            guide.EndIteration(passSpp);

            // Into the image, weighted by the pass's noise. A one-sample pass
            // cannot tell its variance: it joins only the reference estimator,
            // where every sample weighs the same.
            passSums.Count(used, passSpp);
            double wk = _referenceMode ? 1.0 : passSpp > 1 ? PassWeight(passSums.SampleVariance(passSpp)) : 0.0;
            if (wk > 0)
            {
                float wf = (float)wk;
                Parallel.For(0, height, j =>
                {
                    for (int i = 0; i < width; i++)
                    {
                        _guideSumA[j, i] += wf * passSums.A[j, i];
                        _guideSumB[j, i] += wf * passSums.B[j, i];
                    }
                });
                _guideNormA += wk * passSums.CountA;
                _guideNormB += wk * passSums.CountB;
            }
            used += passSpp;
            _guideTrainCount = used;
            passSpp *= 2;
            pass++;
        }
        Console.WriteLine($"  Guiding:     trained {string.Join("+", passes)} spp, {guide.LeafCount} regions ({started.Elapsed.TotalSeconds:F1}s)");
    }

    /// <summary>
    /// One camera sample of pixel (i, j): filter-placed jitter, shutter time,
    /// the ray with its differentials. Draws in the order of the render loop.
    /// </summary>
    private Ray CameraSample(int i, int j, int width, int height, float dsdx, float dtdy)
    {
        var (jitterU, jitterV) = MathUtils.Random2D();
        float rayTime = _motionActive
            ? _shutterOpen + (_shutterClose - _shutterOpen) * MathUtils.RandomFloat()
            : 0f;
        float u = (i + _pixelFilter.SamplePosition(jitterU)) / width;
        float v = (height - j - 1 + _pixelFilter.SamplePosition(jitterV)) / height;
        return _emitRayDifferentials
            ? _camera.GetRayWithDifferentials(u, v, dsdx, dtdy, rayTime)
            : _camera.GetRay(u, v, rayTime);
    }

    /// <summary>Per-worker switches of a render (set before any ray, every tile).</summary>
    private void PrepareWorker(int tile, bool useSobol)
    {
        ShadowRay.BlockSpecularTransmission = _causticsActive;
        MathUtils.SceneScale = _sceneScale;
        ShadowRay.BlockAllTransmission = _referenceMode || (!_transparentShadows && !_causticsActive);
        if (_seedMix != 0 && !useSobol)
            MathUtils.ReseedThread(unchecked((int)(_seedMix ^ (uint)tile * 2654435761u)));
    }

    /// <summary>Pool threads outlive the render: leave none of its switches on.</summary>
    private static void ReleaseWorker()
    {
        ShadowRay.BlockAllTransmission = false;
        MathUtils.SceneScale = 0f;
    }
}
