using System.Numerics;
using RayTracer.Core.Sampling;

namespace RayTracer.Rendering;

/// <summary>Adaptive sampling settings (CLI <c>--adaptive</c>).</summary>
public sealed class AdaptiveSettings
{
    /// <summary>Default noise threshold: about 1% of a display level.</summary>
    public const float DefaultThreshold = 0.01f;

    /// <summary>
    /// A pixel stops when its estimated error falls below this (see
    /// <see cref="Renderer.AdaptiveError"/>): the standard error of the
    /// pixel's mean over the square root of its value, summed over the
    /// channels, after the exposure, roughly the noise on a display value.
    /// </summary>
    public float Threshold { get; init; } = DefaultThreshold;

    /// <summary>Samples every pixel takes before any can stop; 0 = from the
    /// threshold (<see cref="ResolveMinSamples"/>).</summary>
    public int MinSamples { get; init; }

    /// <summary>
    /// 4/√threshold (64 at the default), rounded up to a power of two (a
    /// Sobol prefix of that length is a stratified net), at most the budget.
    /// </summary>
    public int ResolveMinSamples(int budget)
    {
        int min = MinSamples > 0 ? MinSamples : (int)MathF.Ceiling(4f / MathF.Sqrt(Threshold));
        int pow2 = 1;
        while (pow2 < min) pow2 <<= 1;
        return Math.Clamp(MinSamples > 0 ? min : pow2, 1, budget);
    }
}

/// <summary>
/// Adaptive sampling (PIANO-REVIEW.md Phase 9, roadmap #14) on the dual-buffer
/// halves. Every pixel takes the first <see cref="AdaptiveSettings.MinSamples"/>
/// samples of its Sobol sequence, then passes double the count of the pixels
/// still noisy, up to the budget (<c>-s</c>). After each pass a pixel's error
/// is read from its two halves, the first and second half of its sequence
/// (their difference estimates the standard error of the mean, within about
/// 10%: <c>DualBufferVarianceTests</c>); a pixel stops when it and its eight
/// neighbours are below the threshold, so a single pixel that happens to look
/// converged does not stop alone. The halves stay the two halves of the
/// sequence: before each pass the samples so far become half A and the pass
/// fills half B.
///
/// Path tracer with the Sobol sampler only, no path guiding. Not in
/// <c>--reference</c>: stopping on the pixel's own samples biases it slightly.
/// </summary>
public partial class Renderer
{
    private AdaptiveSettings? _adaptive;

    /// <summary>Mean samples per pixel of the last adaptive render (0 otherwise).</summary>
    public double LastRenderMeanSamples { get; private set; }

    /// <summary>Turns adaptive sampling on for the next renders (null turns it off).</summary>
    public void EnableAdaptive(AdaptiveSettings? settings)
    {
        if (settings != null && _referenceMode)
            throw new InvalidOperationException("Adaptive sampling is biased: not in reference mode.");
        _adaptive = settings;
    }

    private bool AdaptiveActive =>
        _adaptive != null && Integrator == IntegratorKind.Path && _guide == null && Sampler.Kind == SamplerKind.Sobol;

    /// <summary>
    /// Error of a pixel from its half sums: Σ_c σ_c / (10⁻⁴ + √Σ_c I_c), with
    /// I the exposed mean and σ the standard error of the mean from the halves
    /// (|Ā − B̄|·√(nA·nB)/n). The square root makes it about the error of a
    /// display value; the sum over channels keeps a dark channel of a
    /// coloured pixel from dominating.
    /// </summary>
    internal static float AdaptiveError(Vector3 sumA, int nA, Vector3 sumB, int nB, float exposure)
    {
        int n = nA + nB;
        if (nA == 0 || nB == 0) return float.PositiveInfinity;
        Vector3 mean = (sumA + sumB) * (exposure / n);
        Vector3 diff = Vector3.Abs(sumA / nA - sumB / nB) * (exposure * MathF.Sqrt((float)nA * nB) / n);
        float sigma = diff.X + diff.Y + diff.Z;
        return sigma / (1e-4f + MathF.Sqrt(MathF.Max(0f, mean.X + mean.Y + mean.Z)));
    }

    private RenderResult RenderAdaptive(int width, int height, RenderCaptureOptions options)
    {
        var settings = _adaptive!;
        var pixels = new Vector3[height, width];
        _captureAovs = options.CaptureAovs;
        bool captureAovs = _captureAovs;
        Interlocked.Exchange(ref s_nonFiniteCount, 0);

        int budget = Math.Max(1, _samplesPerPixel);
        int n0 = settings.ResolveMinSamples(budget);
        int np = width * height;
        var acc = new CaptureAccumulator[np];
        var count = new int[np];
        var active = new bool[np];
        Array.Fill(active, true);
        var error = new float[np];

        const int TileSize = 16;
        int tilesX = (width + TileSize - 1) / TileSize;
        int tilesY = (height + TileSize - 1) / TileSize;
        int totalTiles = tilesX * tilesY;

        // Ray differentials for the budget: a pixel that stops early is smooth
        // by then, so its texture footprint matters no more.
        float diffShrink = MathF.Max(DifferentialFloor, 1f / MathF.Sqrt(budget));
        float dsdx = diffShrink / width;
        float dtdy = diffShrink / height;

        int done = 0, pass = 0, activeCount = np;
        while (true)
        {
            int target = done == 0 ? n0 : Math.Min(budget, 2 * done);
            int first = done;
            // The first pass splits itself; later passes fill half B.
            int halfA = done == 0 ? (n0 + 1) / 2 : 0;
            int completedTiles = 0;
            pass++;
            string label = $"pass {pass}, {target} spp, {100.0 * activeCount / np:F1}% of pixels";

            using var renderDone = new ManualResetEventSlim(false);
            int lastReported = -1;
            var reporter = Task.Run(() =>
            {
                while (!renderDone.Wait(100))
                {
                    int t = Volatile.Read(ref completedTiles);
                    if (t == lastReported) continue;
                    lastReported = t;
                    Console.Write($"\r  Rendering: {label}: {100f * t / totalTiles:F1}%   ");
                }
            });

            Parallel.For(0, totalTiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, tile =>
            {
                PrepareWorker(tile, useSobol: true);
                int x0 = tile % tilesX * TileSize, y0 = tile / tilesX * TileSize;
                int x1 = Math.Min(x0 + TileSize, width), y1 = Math.Min(y0 + TileSize, height);
                for (int j = y0; j < y1; j++)
                for (int i = x0; i < x1; i++)
                {
                    int p = j * width + i;
                    if (!active[p]) continue;
                    uint pixelSeed = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ _seedMix;
                    ref var a = ref acc[p];
                    if (first > 0) a.Promote();
                    for (int s = first; s < target; s++)
                    {
                        Sampler.BeginPixelSample(pixelSeed, (uint)s);
                        var ray = CameraSample(i, j, width, height, dsdx, dtdy);
                        if (captureAovs) (t_aovProbe ??= new AovProbe()).Reset();
                        Vector3 sample = TracePath(ray);
                        Sampler.EndPixelSample();
                        a.Add(ClampRadiance(sample), s < halfA, captureAovs);
                    }
                    count[p] = target;
                    error[p] = AdaptiveError(a.SumA, a.CountA, a.SumB, a.CountB, _exposureScale);
                }
                ReleaseWorker();
                Interlocked.Increment(ref completedTiles);
            });
            renderDone.Set();
            reporter.Wait();

            done = target;
            if (done >= budget) break;
            activeCount = UpdateConvergence(active, error, width, height, settings.Threshold);
            if (activeCount == 0) break;
        }
        Console.Write($"\r  Rendering: {pass} passes, 100.0%{new string(' ', 40)}");
        Console.WriteLine();

        RenderBuffers? buffers = options.Any
            ? new RenderBuffers(width, height, budget, options.CaptureAovs) { HalfCountA = new int[np] }
            : null;
        long total = 0;
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                int p = j * width + i;
                var a = acc[p];
                Vector3 linear = (a.SumA + a.SumB) / count[p];
                pixels[j, i] = _display.Apply(linear * _exposureScale);
                if (buffers != null) a.Store(buffers, i, j, linear, count[p]);
            }
        });
        foreach (int c in count) total += c;
        LastRenderMeanSamples = (double)total / np;
        _captureAovs = false;
        LastRenderNonFiniteCount = Interlocked.Read(ref s_nonFiniteCount);
        return new RenderResult { Pixels = pixels, Buffers = buffers };
    }

    /// <summary>
    /// Stops the pixels whose error and whose eight neighbours' errors are all
    /// below the threshold (stopped neighbours count as below). Returns how
    /// many pixels go on.
    /// </summary>
    internal static int UpdateConvergence(bool[] active, float[] error, int width, int height, float threshold)
    {
        var stop = new bool[active.Length];
        int remaining = 0;
        Parallel.For(0, height, () => 0, (j, _, local) =>
        {
            for (int i = 0; i < width; i++)
            {
                int p = j * width + i;
                if (!active[p]) continue;
                bool below = true;
                for (int dy = -1; dy <= 1 && below; dy++)
                for (int dx = -1; dx <= 1 && below; dx++)
                {
                    int x = i + dx, y = j + dy;
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    int q = y * width + x;
                    if (active[q] && !(error[q] < threshold)) below = false;
                }
                stop[p] = below;
                if (!below) local++;
            }
            return local;
        }, local => Interlocked.Add(ref remaining, local));
        for (int p = 0; p < active.Length; p++)
            if (stop[p]) active[p] = false;
        return remaining;
    }
}
