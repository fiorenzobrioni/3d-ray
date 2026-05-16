using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural Voronoi / Worley cellular texture.
///
/// <para>
/// Reproduces the suite of cellular noises available in Cycles' Voronoi
/// Texture node, Arnold's <c>cell_noise</c> and RenderMan's <c>PxrVoronoise</c>:
/// F1, F2, F1+F2, F2−F1 (crackle), and per-cell colour.
/// </para>
///
/// <para>
/// Useful for: stone/pebble surfaces, lizard/reptile skin, cracked mud,
/// hex-tile-like floors (chebyshev metric), abstract organic patterns
/// (distortion &gt; 0).
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "voronoi"
///   scale: 5.0
///   metric: "euclidean"        # euclidean | manhattan | chebyshev | euclidean_squared
///   output: "f1"               # f1 | f2 | f3 | f4 | f2_minus_f1 | f3_minus_f1 |
///                              # f1_plus_f2 | cell | position
///   randomness: 1.0            # 0 = grid, 1 = full random scatter
///   distortion: 0.0            # domain-warp amplitude (Perlin warp before lookup)
///   smoothness: 0.0            # 0 = hard min (classic); ∈ (0,1] enables IQ Smooth Voronoi
///   colors: [[0,0,0], [1,1,1]] # endpoints for distance modes (ignored for "cell"/"position")
///   offset: [0,0,0]
///   rotation: [0,0,0]
/// </code>
///
/// <para>
/// <b>Smooth Voronoi.</b> When <see cref="Smoothness"/> &gt; 0 the inner
/// <c>min()</c> over the 3×3×3 cell neighbourhood is replaced by Inigo
/// Quilez' log-sum-exp soft-min <c>-log(Σ exp(-k·d_i))/k</c> with
/// <c>k = 20/smoothness</c>. F1 becomes C∞ across cell boundaries, and the
/// F2−F1 "crackle" loses its hard-V ridge — bordi morbidi, no step alias.
/// Useful for polished leather, rounded pebbles, supple reptile skin.
/// </para>
///
/// <para>
/// <b>Extended outputs.</b> <see cref="OutputMode.F3"/>, <see cref="OutputMode.F4"/>
/// and <see cref="OutputMode.F3MinusF1"/> expose the 3rd and 4th nearest
/// feature distances for hierarchical cellular shading (multi-scale leather,
/// large-band crackle networks, voronoi-on-voronoi). <see cref="OutputMode.Position"/>
/// returns the cell-local XYZ of the F1 feature point as RGB — a deterministic
/// "random colour per cell" usable as a stochastic ID to drive another
/// procedural. Same channels Cycles, RenderMan PxrVoronoise and Houdini
/// Voronoi expose. The extended modes always use the hard min (they ignore
/// <see cref="Smoothness"/>) for the same reason as <see cref="OutputMode.Cell"/>:
/// they describe discrete topology, not a smoothable scalar field.
/// </para>
/// </summary>
public class VoronoiTexture : ITexture
{
    public enum OutputMode { F1, F2, F2MinusF1, F1PlusF2, Cell, F3, F4, F3MinusF1, Position }

    private readonly WorleyNoise _worley;
    private readonly Perlin _warpNoise;
    private readonly float _scale;
    private readonly Vector3 _colorA;
    private readonly Vector3 _colorB;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    public WorleyNoise.Metric Metric { get; set; } = WorleyNoise.Metric.Euclidean;
    public OutputMode Output { get; set; } = OutputMode.F1;
    public float Randomness { get; set; } = 1f;
    public float Distortion { get; set; } = 0f;

    /// <summary>
    /// IQ "Smooth Voronoi" coefficient in [0, 1]. <c>0</c> = classic hard min
    /// (bit-identical to legacy behaviour); &gt; 0 enables a log-sum-exp
    /// soft-min over the 27-cell neighbourhood with <c>k = 20/smoothness</c>.
    /// Cycles' Smooth F1 mode, Houdini Voronoi Smoothness, RenderMan
    /// PxrVoronoise's blend factor.
    /// </summary>
    public float Smoothness { get; set; } = 0f;

    /// <summary>
    /// Optional multi-stop colour ramp. When set, the normalised distance
    /// value <c>t ∈ [0, 1]</c> is looked up on the ramp instead of being
    /// linearly blended between the two constructor colours. Ignored when
    /// <see cref="Output"/> is <see cref="OutputMode.Cell"/>, which already
    /// uses per-cell hashed colour and bypasses the lerp.
    /// </summary>
    public ColorRamp? ColorRamp { get; set; }

    public VoronoiTexture(float scale)
        : this(scale, Vector3.Zero, Vector3.One) { }

    public VoronoiTexture(float scale, Vector3 colorA, Vector3 colorB)
    {
        _worley = new WorleyNoise();
        _warpNoise = new Perlin();
        _scale = scale;
        _colorA = colorA;
        _colorB = colorB;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
        => SampleAtP(p, objectSeed);

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint)
    {
        if (!footprint.HasFootprint) return SampleAtP(p, objectSeed);

        // ── Adaptive supersampling over the footprint ──────────────────────
        // Voronoi has hard discontinuities at cell boundaries (F1 in particular)
        // so analytic frequency clamping doesn't apply — Worley's spectrum has
        // energy at every scale up to the cell density. Instead, supersample
        // within the surface footprint: cheap and matches what PxrVoronoise
        // does (Pixar's "voronoise" doc § "Anti-aliasing"). Sample count
        // scales with how many cells the footprint straddles — small footprint
        // = 1 sample (point-sampled, fast); large footprint = up to 16
        // jittered samples averaged.
        float fpExtent = footprint.MaxWorldAxis();
        float cellSpan = fpExtent * _scale;
        int samples = cellSpan switch
        {
            < 0.25f => 1,
            < 0.75f => 4,
            < 1.5f  => 9,
            _       => 16,
        };
        if (samples == 1) return SampleAtP(p, objectSeed);

        // Jittered stratified grid inside the (dPdx, dPdy) parallelogram.
        // Hash on (objectSeed, p) so neighbouring footprints stay decorrelated
        // — uncorrelated noise produces clean random-dither instead of
        // moiré bands on a uniform surface.
        int side = (int)MathF.Sqrt(samples);
        float invSide = 1f / side;
        Vector3 accum = Vector3.Zero;
        uint h = (uint)objectSeed * 2654435761u
               ^ (uint)BitConverter.SingleToInt32Bits(p.X) * 374761393u
               ^ (uint)BitConverter.SingleToInt32Bits(p.Y) * 668265263u
               ^ (uint)BitConverter.SingleToInt32Bits(p.Z) * 1274126177u;
        for (int j = 0; j < side; j++)
        {
            for (int i = 0; i < side; i++)
            {
                // Cheap hash-jitter — fully deterministic for a given pixel/footprint.
                h = h * 1103515245u + 12345u;
                float jx = ((h >> 8) & 0xFFFFFF) * (1f / 16777216f);
                h = h * 1103515245u + 12345u;
                float jy = ((h >> 8) & 0xFFFFFF) * (1f / 16777216f);
                // Offsets in [-0.5, 0.5] across the footprint parallelogram.
                float a = (i + jx) * invSide - 0.5f;
                float b = (j + jy) * invSide - 0.5f;
                Vector3 pSample = p + a * footprint.DPdx + b * footprint.DPdy;
                accum += SampleAtP(pSample, objectSeed);
            }
        }
        return accum / (side * side);
    }

    private Vector3 SampleAtP(Vector3 p, int objectSeed)
    {
        // Worley is locally aperiodic and shift-decorrelated — every read of
        // `transformedP` indexes the cell grid, so adding the per-instance
        // seed offset on the full point is safe.
        Vector3 transformedP = TextureTransform.ApplyManual(p, Offset, Rotation);
        transformedP = TextureTransform.ApplyRandomRotation(transformedP, objectSeed, RandomizeRotation);
        transformedP += TextureTransform.SeedOffset(objectSeed, RandomizeOffset);

        WorleyNoise worley = objectSeed != 0 ? WorleyNoise.GetOrCreate(objectSeed) : _worley;

        Vector3 q = _scale * transformedP;
        if (Distortion > 0f)
        {
            Perlin warp = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _warpNoise;
            q += Distortion * warp.NoiseVector(q + new Vector3(17.3f, 5.1f, 11.7f));
        }

        // Dispatch: F3/F4/F3-F1/Position need the extended 4-slot evaluator;
        // Cell stays on the cheaper 2-slot path. Smoothness applies to F1/F2
        // and the derived F2-F1/F1+F2 channels only — extended outputs are
        // discrete-topology descriptors (closest-N indexing, feature-point
        // identity) and follow the same "no soft-min" convention Cycles uses
        // for its Cell output.
        float f1, f2, f3, f4;
        int cellId;
        Vector3 featurePos;

        bool needsExtended =
            Output == OutputMode.F3 ||
            Output == OutputMode.F4 ||
            Output == OutputMode.F3MinusF1 ||
            Output == OutputMode.Position;

        if (needsExtended)
        {
            worley.EvaluateExtended(q, Metric, Randomness,
                out f1, out f2, out f3, out f4,
                out cellId, out featurePos);
        }
        else if (Smoothness > 0f)
        {
            // Smooth-min variant — IQ "Smooth Voronoi". Falls back to the hard
            // path internally when Smoothness ≤ 0 so the back-compat result is
            // bit-identical for legacy scenes that never set the property.
            worley.EvaluateSmooth(q, Metric, Randomness, Smoothness, out f1, out f2, out cellId);
            f3 = f4 = 0f;
            featurePos = Vector3.Zero;
        }
        else
        {
            worley.Evaluate(q, Metric, Randomness, out f1, out f2, out cellId);
            f3 = f4 = 0f;
            featurePos = Vector3.Zero;
        }

        if (Output == OutputMode.Cell)
        {
            // Cell-ID lookup is discrete by nature: no smoothing applied
            // even with Smoothness > 0 — matches Cycles' behaviour where
            // smoothness affects distance outputs only.
            return WorleyNoise.CellColor(cellId);
        }

        if (Output == OutputMode.Position)
        {
            // Cell-local feature point as RGB — the per-cell deterministic
            // [0, 1]³ jitter coordinate. Matches Cycles' "Position" output
            // wrapped into the owning cell so the result lives in colour
            // range without an extra Mapping node. Bypasses ColorRamp for
            // the same reason as Cell: it's an XYZ identity vector, not a
            // scalar mappable to a 1-D ramp.
            Vector3 cellLocal = new(
                featurePos.X - MathF.Floor(featurePos.X),
                featurePos.Y - MathF.Floor(featurePos.Y),
                featurePos.Z - MathF.Floor(featurePos.Z));
            return cellLocal;
        }

        // Normalisation strategy mirrors what Cycles / Arnold expose so that
        // each output mode lands naturally in [0, 1] across the full sphere
        // of a unit cell:
        //   - F1, F2, F3, F4:  divide by the metric-specific worst-case
        //                      distance. F3/F4 can occasionally exceed maxD
        //                      (the 3rd/4th nearest in the 3×3×3 window can
        //                      sit outside the central cell's bounding ball)
        //                      so the result is clamped to [0, 1] — same
        //                      response shape as Cycles' F3/F4 channels.
        //   - F2-F1, F3-F1:    divide by half the worst-case AND apply a
        //                      sqrt response curve, mirroring Cycles'
        //                      "Distance to Edge" compression. Without the
        //                      sqrt the difference stays packed near 0 and
        //                      the classic crackle look is unreachable from
        //                      colour endpoints alone. F3-F1 produces a
        //                      wider, lower-frequency border band than F2-F1.
        //   - F1+F2:           divide by 2× metric worst-case (no compression).
        float maxD = Metric == WorleyNoise.Metric.Manhattan ? 3f
                   : Metric == WorleyNoise.Metric.Chebyshev ? 1f
                   : Metric == WorleyNoise.Metric.EuclideanSquared ? 3f
                   : MathF.Sqrt(3f);

        float t = Output switch
        {
            OutputMode.F1         => f1 / maxD,
            OutputMode.F2         => f2 / maxD,
            OutputMode.F3         => f3 / maxD,
            OutputMode.F4         => f4 / maxD,
            OutputMode.F2MinusF1  => MathF.Sqrt(Math.Clamp((f2 - f1) / (0.5f * maxD), 0f, 1f)),
            OutputMode.F3MinusF1  => MathF.Sqrt(Math.Clamp((f3 - f1) / (0.5f * maxD), 0f, 1f)),
            OutputMode.F1PlusF2   => (f1 + f2) / (2f * maxD),
            _                     => f1 / maxD,
        };
        t = Math.Clamp(t, 0f, 1f);

        return ColorRamp is { } ramp ? ramp.Sample(t) : Vector3.Lerp(_colorA, _colorB, t);
    }
}
