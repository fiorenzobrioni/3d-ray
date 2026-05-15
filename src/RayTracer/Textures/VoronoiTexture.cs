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
///   output: "f1"               # f1 | f2 | f2_minus_f1 | f1_plus_f2 | cell
///   randomness: 1.0            # 0 = grid, 1 = full random scatter
///   distortion: 0.0            # domain-warp amplitude (Perlin warp before lookup)
///   colors: [[0,0,0], [1,1,1]] # endpoints for distance modes (ignored for "cell")
///   offset: [0,0,0]
///   rotation: [0,0,0]
/// </code>
/// </summary>
public class VoronoiTexture : ITexture
{
    public enum OutputMode { F1, F2, F2MinusF1, F1PlusF2, Cell }

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
    {
        Vector3 transformedP = TextureTransform.Apply(
            p, Offset, Rotation, objectSeed, RandomizeOffset, RandomizeRotation);

        WorleyNoise worley = objectSeed != 0 ? WorleyNoise.GetOrCreate(objectSeed) : _worley;

        Vector3 q = _scale * transformedP;
        if (Distortion > 0f)
        {
            Perlin warp = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _warpNoise;
            q += Distortion * warp.NoiseVector(q + new Vector3(17.3f, 5.1f, 11.7f));
        }

        worley.Evaluate(q, Metric, Randomness, out float f1, out float f2, out int cellId);

        if (Output == OutputMode.Cell)
        {
            return WorleyNoise.CellColor(cellId);
        }

        // Normalise distances: max possible nearest-feature distance with
        // randomness=1 is bounded by the cell diagonal; clamp to keep output
        // in [0, 1] across all three metrics.
        float maxD = Metric == WorleyNoise.Metric.Manhattan ? 3f
                   : Metric == WorleyNoise.Metric.Chebyshev ? 1f
                   : Metric == WorleyNoise.Metric.EuclideanSquared ? 3f
                   : MathF.Sqrt(3f);

        float t = Output switch
        {
            OutputMode.F1         => f1 / maxD,
            OutputMode.F2         => f2 / maxD,
            OutputMode.F2MinusF1  => (f2 - f1) / maxD,
            OutputMode.F1PlusF2   => (f1 + f2) / (2f * maxD),
            _                     => f1 / maxD,
        };
        t = Math.Clamp(t, 0f, 1f);

        return Vector3.Lerp(_colorA, _colorB, t);
    }
}
