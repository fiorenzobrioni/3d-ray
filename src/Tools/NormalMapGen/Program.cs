// ═══════════════════════════════════════════════════════════════════════════
//  NormalMapGen — Generatore di Normal Map PRO-GRADE per 3D-Ray
//
//  Genera normal map 1024x1024 procedurali calcolando le derivate 
//  parziali di campi d'altezza complessi (fBM, Voronoi, ecc.).
// ═══════════════════════════════════════════════════════════════════════════

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tools;

string outputDir = "scenes/libraries/textures";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--output" or "-o") outputDir = args[i + 1];
}
Directory.CreateDirectory(outputDir);

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   NormalMapGen PRO — 3D-Ray Textures     ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Output: {Path.GetFullPath(outputDir)}/");
Console.WriteLine();

var sw = System.Diagnostics.Stopwatch.StartNew();

GenerateBrickWallNormal(Path.Combine(outputDir, "brick-wall-normal.png"));
GenerateWoodFloorNormal(Path.Combine(outputDir, "wood-floor-normal.png"), true);
GenerateWoodFloorNormal(Path.Combine(outputDir, "wood-planks-normal.png"), false);
GenerateConcreteNormal(Path.Combine(outputDir, "concrete-normal.png"));
GenerateMetalScratchedNormal(Path.Combine(outputDir, "metal-scratched-normal.png"));
GenerateStoneCobbleNormal(Path.Combine(outputDir, "stone-cobble-normal.png"));
GenerateFabricWeaveNormal(Path.Combine(outputDir, "fabric-weave-normal.png"));
GenerateTilesNormal(Path.Combine(outputDir, "tiles-normal.png"));
GenerateFlatNormal(Path.Combine(outputDir, "flat-normal.png"));

Console.WriteLine();
Console.WriteLine($"Done! {sw.ElapsedMilliseconds} ms total");

// ═══════════════════════════════════════════════════════════════════════════
//  HELPERS
// ═══════════════════════════════════════════════════════════════════════════

static Rgba32 EncodeNormal(float nx, float ny, float nz)
{
    float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
    if (len > 1e-6f) { nx /= len; ny /= len; nz /= len; }
    else { nx = 0; ny = 0; nz = 1; }

    return new Rgba32(
        (byte)Math.Clamp((int)((nx * 0.5f + 0.5f) * 255f), 0, 255),
        (byte)Math.Clamp((int)((ny * 0.5f + 0.5f) * 255f), 0, 255),
        (byte)Math.Clamp((int)((nz * 0.5f + 0.5f) * 255f), 0, 255));
}

static Rgba32 CalculateNormalFromHeight(Func<float, float, float> heightFunc, float x, float y, float strength = 1.0f)
{
    float eps = 1.0f;
    float hL = heightFunc(x - eps, y);
    float hR = heightFunc(x + eps, y);
    float hD = heightFunc(x, y - eps);
    float hU = heightFunc(x, y + eps);

    float nx = (hL - hR) * strength;
    float ny = (hD - hU) * strength;
    float nz = 2.0f * eps;
    return EncodeNormal(nx, ny, nz);
}

// ═══════════════════════════════════════════════════════════════════════════
//  BRICK WALL
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateBrickWallNormal(string path)
{
    const int w = 1024, h = 1024;
    const int brickW = 128, brickH = 64;
    const int mortarThick = 6;

    using var img = new Image<Rgba32>(w, h);

    Func<float, float, float> heightMap = (x, y) =>
    {
        int brickRow = (int)MathF.Floor(y / brickH);
        int offsetX = (brickRow % 2 == 1) ? brickW / 2 : 0;
        float lx = ((x + offsetX) % brickW + brickW) % brickW;
        float ly = (y % brickH + brickH) % brickH;

        float noiseMortar = ProNoise.Fbm(x * 0.05f, y * 0.05f, 4) * 4.0f;
        float effMortarX = mortarThick + noiseMortar * (MathF.Abs(ly - brickH/2f) < 20 ? 0 : 1);
        float effMortarY = mortarThick + noiseMortar * (MathF.Abs(lx - brickW/2f) < 40 ? 0 : 1);

        bool isMortar = lx < effMortarX || ly < effMortarY;

        if (isMortar)
        {
            return -20.0f + ProNoise.Fbm(x * 0.1f, y * 0.1f, 6) * 5.0f;
        }
        else
        {
            float distFromLeft = lx - effMortarX;
            float distFromRight = brickW - 1 - lx;
            float distFromTop = ly - effMortarY;
            float distFromBottom = brickH - 1 - ly;
            float dist = MathF.Min(MathF.Min(distFromLeft, distFromRight), MathF.Min(distFromTop, distFromBottom));

            float hVal = 0.0f;
            if (dist < 8) hVal = - (8 - dist) * 1.5f; // bevel

            hVal += ProNoise.Fbm(x * 0.02f, y * 0.02f, 5) * 4.0f;
            return hVal;
        }
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 1.5f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  WOOD FLOOR / PLANKS
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateWoodFloorNormal(string path, bool dark)
{
    const int w = 1024, h = 1024;
    int plankW = dark ? 192 : 340; 
    int plankH = 512;
    const int gapW = 4, gapH = 4;
    int bevel = dark ? 6 : 12;

    var rng = new Random(dark ? 42 : 137);
    int numPlanksX = (w / plankW) + 2;
    var plankOffsets = new float[numPlanksX];
    var plankStagger = new int[numPlanksX];

    for (int i = 0; i < numPlanksX; i++)
    {
        plankOffsets[i] = (float)rng.NextDouble() * 1000f;
        plankStagger[i] = rng.Next(0, plankH);
    }

    using var img = new Image<Rgba32>(w, h);

    Func<float, float, float> heightMap = (x, y) =>
    {
        int plankIdx = (int)MathF.Floor(x / plankW);
        if (plankIdx < 0) plankIdx = 0;
        if (plankIdx >= numPlanksX) plankIdx = numPlanksX - 1;

        float lx = ((x % plankW) + plankW) % plankW;
        float ly = ((y + plankStagger[plankIdx]) % plankH + plankH) % plankH;

        bool isGapV = lx < gapW;
        bool isGapH = ly < gapH;

        if (isGapV || isGapH) return -15.0f;

        float distFromEdgeX = MathF.Min(lx - gapW, plankW - 1 - lx);
        float distFromEdgeY = MathF.Min(ly - gapH, plankH - 1 - ly);
        float distFromEdge = MathF.Min(distFromEdgeX, distFromEdgeY);
        
        float hVal = 0.0f;
        if (distFromEdge < bevel) hVal = -(bevel - distFromEdge) * 1.5f;

        float px = x * 0.01f;
        float py = (y + plankOffsets[plankIdx]) * 0.005f;

        float warp = ProNoise.Fbm(px, py, 4) * 2.5f;
        float grain = ProNoise.Fbm(px + warp, py * 4.0f + warp, 6);

        // Nodi
        if (!dark)
        {
            ProNoise.Voronoi(px * 0.5f, py * 0.5f, 1.0f, out float d1, out float d2);
            if (d1 < 0.15f)
            {
                float knotInfluence = 1f - (d1 / 0.15f);
                grain += MathF.Sin(d1 * 50f) * knotInfluence * 0.5f;
                // Add a dip in the knot center
                hVal -= knotInfluence * 3.0f;
            }
        }

        hVal += grain * 2.0f;
        return hVal;
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 1.2f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  CONCRETE
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateConcreteNormal(string path)
{
    const int w = 1024, h = 1024;
    using var img = new Image<Rgba32>(w, h);

    Func<float, float, float> heightMap = (x, y) =>
    {
        float px = x * 0.01f, py = y * 0.01f;
        float highF = ProNoise.Fbm(px * 5f, py * 5f, 6);
        
        ProNoise.Voronoi(px * 0.8f, py * 0.8f, 1.0f, out float d1, out float d2);
        float pit = MathF.Max(0, 1.0f - d1 * 5f);
        pit *= pit;

        return highF * 3.0f - pit * 15.0f;
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 0.8f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  METAL SCRATCHED
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateMetalScratchedNormal(string path)
{
    const int w = 1024, h = 1024;
    using var img = new Image<Rgba32>(w, h);

    var rng = new Random(42);
    var scratches = new List<(float x, float y, float dx, float dy, float len, float depth, float w)>();
    for (int i = 0; i < 200; i++)
    {
        float angle = (float)rng.NextDouble() * MathF.PI;
        float len = 50 + rng.Next(400);
        float cx = rng.Next(w), cy = rng.Next(h);
        float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
        float depth = 0.3f + (float)rng.NextDouble() * 0.7f;
        float width = 1.0f + (float)rng.NextDouble() * 2.5f;
        scratches.Add((cx, cy, dx, dy, len, depth, width));
    }

    Func<float, float, float> heightMap = (x, y) =>
    {
        float hVal = ProNoise.Fbm(x * 0.1f, y * 0.1f, 3) * 1.5f;
        foreach (var s in scratches)
        {
            float t = ((x - s.x) * s.dx + (y - s.y) * s.dy);
            if (t > -s.len/2 && t < s.len/2)
            {
                float projX = s.x + t * s.dx, projY = s.y + t * s.dy;
                float dist = MathF.Sqrt((x - projX)*(x - projX) + (y - projY)*(y - projY));
                if (dist < s.w)
                {
                    hVal -= s.depth * 5.0f * (1f - dist/s.w);
                }
            }
        }
        return hVal;
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 1.0f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  STONE COBBLE (Voronoi)
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateStoneCobbleNormal(string path)
{
    const int w = 1024, h = 1024;
    using var img = new Image<Rgba32>(w, h);

    Func<float, float, float> heightMap = (x, y) =>
    {
        ProNoise.Voronoi(x * 0.015f, y * 0.015f, 1.0f, out float d1, out float d2);
        float edge = d2 - d1;
        float hVal = 0;

        if (edge < 0.06f)
        {
            hVal = -15.0f + edge * 200.0f; 
        }
        else
        {
            // Center of stone
            float stoneDome = 1.0f - d1; 
            hVal = stoneDome * 30.0f + ProNoise.Fbm(x * 0.05f, y * 0.05f, 4) * 3.0f;
        }
        return hVal;
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 0.7f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  FABRIC WEAVE
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateFabricWeaveNormal(string path)
{
    const int w = 1024, h = 1024;
    const int weaveSize = 32;

    using var img = new Image<Rgba32>(w, h);

    Func<float, float, float> heightMap = (x, y) =>
    {
        int wx = (int)MathF.Floor(x / weaveSize);
        int wy = (int)MathF.Floor(y / weaveSize);
        float lx = ((x % weaveSize) + weaveSize) % weaveSize;
        float ly = ((y % weaveSize) + weaveSize) % weaveSize;

        bool horizontalOnTop = (Math.Abs(wx) + Math.Abs(wy)) % 2 == 0;
        
        float cx = (lx - weaveSize / 2f) / (weaveSize / 2f);
        float cy = (ly - weaveSize / 2f) / (weaveSize / 2f);
        
        float hVal = 0;
        if (horizontalOnTop)
        {
            hVal = (1.0f - cy * cy) * 10.0f;
            // thread strands
            hVal += MathF.Sin(cx * MathF.PI * 6f) * 1.0f;
        }
        else
        {
            hVal = (1.0f - cx * cx) * 10.0f;
            hVal += MathF.Sin(cy * MathF.PI * 6f) * 1.0f;
        }

        hVal += ProNoise.Fbm(x * 0.1f, y * 0.1f, 2) * 1.0f;
        return hVal;
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 1.2f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  TILES
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateTilesNormal(string path)
{
    const int w = 1024, h = 1024;
    const int tileSize = 128, groutW = 6, bevelPx = 8;

    using var img = new Image<Rgba32>(w, h);

    Func<float, float, float> heightMap = (x, y) =>
    {
        float lx = ((x % tileSize) + tileSize) % tileSize;
        float ly = ((y % tileSize) + tileSize) % tileSize;

        bool inGroutX = lx < groutW;
        bool inGroutY = ly < groutW;

        if (inGroutX || inGroutY)
        {
            return -10.0f + ProNoise.Fbm(x * 0.2f, y * 0.2f, 3) * 2.0f;
        }
        else
        {
            float dLeft = lx - groutW, dTop = ly - groutW;
            float dRight = tileSize - 1 - lx, dBottom = tileSize - 1 - ly;
            float dist = MathF.Min(MathF.Min(dLeft, dRight), MathF.Min(dTop, dBottom));

            float hVal = 0.0f;
            if (dist < bevelPx) hVal = -(bevelPx - dist) * 1.5f;

            hVal += ProNoise.Fbm(x * 0.05f, y * 0.05f, 2) * 1.0f;
            return hVal;
        }
    };

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            img[x, y] = CalculateNormalFromHeight(heightMap, x, y, 1.0f);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}

// ═══════════════════════════════════════════════════════════════════════════
//  FLAT NORMAL
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateFlatNormal(string path)
{
    const int w = 1024, h = 1024;
    using var img = new Image<Rgba32>(w, h);
    var flat = new Rgba32(128, 128, 255);

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
            img[x, y] = flat;
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}   (test reference — no perturbation)");
}
