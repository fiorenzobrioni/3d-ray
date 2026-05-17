using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Textures;

/// <summary>
/// Procedural brick wall — running-bond pattern with mortar lines and per-brick
/// colour variation.
///
/// <para>
/// Matches the feature set of Cycles' Brick Texture and RenderMan's
/// <c>PxrTile</c>:
/// <list type="bullet">
///   <item><description>Two brick colours blended by a deterministic per-brick hash (<see cref="ColorVariation"/>).</description></item>
///   <item><description>Separate mortar colour with configurable thickness (<see cref="MortarSize"/>).</description></item>
///   <item><description>Row offset (<see cref="RowOffset"/>) for running-bond (0.5), stack-bond (0), or quirky offsets.</description></item>
///   <item><description>Independent <see cref="BrickWidth"/> and <see cref="BrickHeight"/>.</description></item>
///   <item><description>Optional Perlin-fed bias on brick colour for weathered look (<see cref="NoiseScale"/>).</description></item>
/// </list>
/// </para>
///
/// <para>
/// The pattern lives on the XY plane by default — apply a <see cref="TextureTransform"/>
/// rotation to lay it on walls oriented differently. Z is ignored so the pattern
/// is consistent across depth (good for thick walls).
/// </para>
///
/// In YAML:
/// <code>
/// texture:
///   type: "brick"
///   colors: [[0.65,0.27,0.20], [0.55,0.20,0.15], [0.85,0.83,0.78]]  # brick A, brick B, mortar
///   brick_width: 0.5
///   brick_height: 0.2
///   mortar_size: 0.03
///   row_offset: 0.5             # 0 = stack, 0.5 = running bond
///   color_variation: 0.4        # 0 = all bricks same colour, 1 = full A/B contrast
///   noise_scale: 0.0            # weathering noise inside each brick (0 = off)
///   offset: [0,0,0]
///   rotation: [0,0,0]
/// </code>
/// </summary>
public class BrickTexture : ITexture
{
    private readonly Perlin _noise;
    private readonly Vector3 _brickA;
    private readonly Vector3 _brickB;
    private readonly Vector3 _mortar;

    public Vector3 Offset { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public bool RandomizeOffset { get; set; }
    public bool RandomizeRotation { get; set; }

    public float BrickWidth { get; set; } = 0.5f;
    public float BrickHeight { get; set; } = 0.2f;
    public float MortarSize { get; set; } = 0.03f;
    public float RowOffset { get; set; } = 0.5f;
    public float ColorVariation { get; set; } = 0.4f;
    public float NoiseScale { get; set; } = 0f;

    public BrickTexture(Vector3 brickA, Vector3 brickB, Vector3 mortar)
    {
        _noise = new Perlin();
        _brickA = brickA;
        _brickB = brickB;
        _mortar = mortar;
    }

    public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
    {
        // Brick layout is a deterministic grid (`floor(p/bw)`, `floor(p/bh)`):
        // applying the per-instance seed offset on the same point would
        // teleport the grid randomly per instance. Only the weathering noise
        // below gets the seed offset — bricks side-by-side share the grid but
        // have decorrelated weathering patterns.
        Vector3 qGeom = TextureTransform.ApplyRandomRotation(
            TextureTransform.ApplyManual(p, Offset, Rotation),
            objectSeed, RandomizeRotation);

        float bw = MathF.Max(BrickWidth, 1e-6f);
        float bh = MathF.Max(BrickHeight, 1e-6f);

        // Row index drives the horizontal offset (running bond pattern).
        float yRow = qGeom.Y / bh;
        int rowIdx = (int)MathF.Floor(yRow);
        float yFrac = yRow - rowIdx;

        float xShift = (rowIdx & 1) == 0 ? 0f : RowOffset;
        float xCol = (qGeom.X / bw) - xShift;
        int colIdx = (int)MathF.Floor(xCol);
        float xFrac = xCol - colIdx;

        // Mortar test — convert mortar_size from world units to brick-local fraction.
        float mortarX = MortarSize / bw;
        float mortarY = MortarSize / bh;
        bool inMortar = xFrac < mortarX || xFrac > 1f - mortarX
                     || yFrac < mortarY || yFrac > 1f - mortarY;

        if (inMortar)
        {
            return _mortar;
        }

        // Stable per-brick hash → blend between brickA and brickB by ColorVariation.
        int hash = HashBrick(colIdx, rowIdx);
        float h01 = ((hash & 0xFFFF) / 65535f); // [0,1]
        float blend = 0.5f + (h01 - 0.5f) * Math.Clamp(ColorVariation, 0f, 1f);
        Vector3 brick = Vector3.Lerp(_brickA, _brickB, blend);

        if (NoiseScale > 0f)
        {
            Perlin n = objectSeed != 0 ? Perlin.GetOrCreate(objectSeed) : _noise;
            Vector3 qNoise = qGeom + TextureTransform.SeedOffset(objectSeed, RandomizeOffset);
            float weather = (n.Noise(qNoise * (1f / MathF.Max(NoiseScale, 1e-3f))) + 1f) * 0.5f;
            // Weathering darkens or lightens the brick by up to 30% based on noise.
            float k = 1f + (weather - 0.5f) * 0.6f;
            brick *= k;
        }

        return brick;
    }

    private static int HashBrick(int x, int y)
    {
        uint h = unchecked((uint)(x * 374761393) ^ (uint)(y * 668265263));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return unchecked((int)h);
    }
}
