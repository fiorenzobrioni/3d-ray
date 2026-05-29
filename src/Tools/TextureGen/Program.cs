// ═══════════════════════════════════════════════════════════════════════════
//  TextureGen — Generatore di texture PRO-GRADE per 3D-Ray
//
//  Genera texture fotorealistiche 1024x1024 PBR (Albedo, Roughness, AO, Metallic).
// ═══════════════════════════════════════════════════════════════════════════

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tools;

string outputDir = "scenes/assets/textures";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--output" or "-o") outputDir = args[i + 1];
}

Directory.CreateDirectory(outputDir);

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║    TextureGen PRO — 3D-Ray Textures      ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Output: {Path.GetFullPath(outputDir)}/");
Console.WriteLine();

var sw = System.Diagnostics.Stopwatch.StartNew();

GenerateBrickWall(outputDir, "brick-wall", false);
GenerateBrickWall(outputDir, "brick-wall-white", true);
GenerateWoodFloor(outputDir, "wood-floor", true);
GenerateWoodFloor(outputDir, "wood-planks", false);
GenerateEarthMap(Path.Combine(outputDir, "earth.png"));
GenerateCheckerboard(Path.Combine(outputDir, "checkerboard.png"));
GenerateUVGrid(Path.Combine(outputDir, "grid-uv.png"));
GenerateMetalScratched(outputDir, "metal-scratched");
GenerateConcrete(outputDir, "concrete");
GenerateLogo(Path.Combine(outputDir, "logo-3dray.png"));

Console.WriteLine();
Console.WriteLine($"Done! {sw.ElapsedMilliseconds} ms — PBR textures generated in {Path.GetFullPath(outputDir)}/");


// ═════════════════════════════════════════════════════════════════════════════
//  GENERATORS (1024x1024)
// ═════════════════════════════════════════════════════════════════════════════

static Rgba32 ToRgba(float r, float g, float b) => new(
    (byte)Math.Clamp((int)(r * 255), 0, 255),
    (byte)Math.Clamp((int)(g * 255), 0, 255),
    (byte)Math.Clamp((int)(b * 255), 0, 255));

static Rgba32 ToGrayscale(float v) => ToRgba(v, v, v);

// ─────────────────────────────────────────────────────────────────────────────
//  Brick Wall — Mattoni fotorealistici con fBM e Voronoi
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateBrickWall(string dir, string name, bool white)
{
    const int w = 1024, h = 1024;
    const int brickW = 128, brickH = 64;
    const int mortarThick = 6;

    using var imgAlbedo = new Image<Rgba32>(w, h);
    using var imgRoughness = new Image<Rgba32>(w, h);
    using var imgAO = new Image<Rgba32>(w, h);

    var mortarColor = white ? new Rgba32(200, 198, 195) : new Rgba32(180, 175, 165);

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            int brickRow = y / brickH;
            int offsetX = (brickRow % 2 == 1) ? brickW / 2 : 0;
            int lx = (x + offsetX) % brickW;
            int ly = y % brickH;

            // Use noise to make mortar lines irregular
            float noiseMortar = ProNoise.Fbm(x * 0.05f, y * 0.05f, 4) * 4.0f;
            int effectiveMortarX = mortarThick + (int)(noiseMortar * (MathF.Abs(ly - brickH/2f) < 20 ? 0 : 1));
            int effectiveMortarY = mortarThick + (int)(noiseMortar * (MathF.Abs(lx - brickW/2f) < 40 ? 0 : 1));

            bool isMortar = lx < effectiveMortarX || ly < effectiveMortarY;

            if (isMortar)
            {
                float mNoise = ProNoise.Fbm(x * 0.1f, y * 0.1f, 6);
                float mVal = 0.8f + mNoise * 0.4f;
                imgAlbedo[x, y] = ToRgba(mortarColor.R / 255f * mVal, mortarColor.G / 255f * mVal, mortarColor.B / 255f * mVal);
                
                // Mortar is very rough
                imgRoughness[x, y] = ToGrayscale(0.9f + mNoise * 0.1f);
                
                // AO deep in mortar
                imgAO[x, y] = ToGrayscale(0.4f + mNoise * 0.2f);
            }
            else
            {
                int seedX = (x + offsetX) / brickW;
                int seedY = brickRow;
                int seed = seedX * 31 + seedY * 17;
                
                // Brick variation
                float brickVar = ProNoise.Hash(seedX, seedY, 42);
                float surfaceNoise = ProNoise.Fbm(x * 0.02f, y * 0.02f, 5);
                float spots = ProNoise.VoronoiCell(x * 0.1f, y * 0.1f);

                float r, g, b;
                if (white)
                {
                    r = 0.85f + brickVar * 0.1f + surfaceNoise * 0.1f;
                    g = 0.82f + brickVar * 0.1f + surfaceNoise * 0.1f;
                    b = 0.80f + brickVar * 0.1f + surfaceNoise * 0.1f;
                }
                else
                {
                    r = 0.6f + brickVar * 0.2f + surfaceNoise * 0.15f;
                    g = 0.25f + brickVar * 0.1f + surfaceNoise * 0.1f;
                    b = 0.15f + brickVar * 0.1f + surfaceNoise * 0.1f;
                }
                
                // Add some dark spots (Voronoi)
                if (spots > 0.8f)
                {
                    r *= 0.8f; g *= 0.8f; b *= 0.8f;
                }

                imgAlbedo[x, y] = ToRgba(r, g, b);
                
                // Roughness: bricks are rough, but less than mortar
                float rough = 0.7f + surfaceNoise * 0.2f;
                imgRoughness[x, y] = ToGrayscale(rough);

                // Bevel AO
                int distFromLeft = lx - effectiveMortarX;
                int distFromRight = brickW - 1 - lx;
                int distFromTop = ly - effectiveMortarY;
                int distFromBottom = brickH - 1 - ly;
                int dist = Math.Min(Math.Min(distFromLeft, distFromRight), Math.Min(distFromTop, distFromBottom));
                
                float ao = 1.0f;
                if (dist < 8) ao = 0.6f + (dist / 8.0f) * 0.4f;
                imgAO[x, y] = ToGrayscale(ao);
            }
        }
    }

    imgAlbedo.SaveAsPng(Path.Combine(dir, $"{name}.png"));
    imgRoughness.SaveAsPng(Path.Combine(dir, $"{name}-roughness.png"));
    imgAO.SaveAsPng(Path.Combine(dir, $"{name}-ao.png"));
    Console.WriteLine($"  ✓ {name,-28} {w}×{h} (Albedo, Roughness, AO)");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Wood Floor / Planks — Venature con Domain Warping
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateWoodFloor(string dir, string name, bool dark)
{
    const int w = 1024, h = 1024;
    int plankW = dark ? 192 : 340; 
    int plankH = 512;
    const int gapW = 4, gapH = 4;
    int bevel = dark ? 6 : 12;

    using var imgAlbedo = new Image<Rgba32>(w, h);
    using var imgRoughness = new Image<Rgba32>(w, h);
    using var imgAO = new Image<Rgba32>(w, h);

    var rng = new Random(dark ? 42 : 137);

    int numPlanksX = (w / plankW) + 2;
    var plankTints = new (float r, float g, float b)[numPlanksX];
    var plankOffsets = new float[numPlanksX];
    var plankStagger = new int[numPlanksX];

    for (int i = 0; i < numPlanksX; i++)
    {
        float tR = 0.85f + (float)(rng.NextDouble() * 0.3 - 0.15);
        float tG = 0.85f + (float)(rng.NextDouble() * 0.3 - 0.15);
        float tB = 0.85f + (float)(rng.NextDouble() * 0.25 - 0.12);
        plankTints[i] = (tR, tG, tB);
        plankOffsets[i] = (float)rng.NextDouble() * 1000f;
        plankStagger[i] = rng.Next(0, plankH);
    }

    float baseR = dark ? 0.48f : 0.82f;
    float baseG = dark ? 0.30f : 0.68f;
    float baseB = dark ? 0.16f : 0.48f;
    float darkR = dark ? 0.25f : 0.58f;
    float darkG = dark ? 0.14f : 0.42f;
    float darkB = dark ? 0.07f : 0.26f;

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            int plankIdx = x / plankW;
            int lx = x % plankW;
            int ly = (y + plankStagger[plankIdx]) % plankH;

            bool isGapV = lx < gapW;
            bool isGapH = ly < gapH;

            if (isGapV || isGapH)
            {
                float gapShade = 0.1f * ProNoise.Hash(x, y, 1);
                imgAlbedo[x, y] = ToGrayscale(gapShade);
                imgRoughness[x, y] = ToGrayscale(0.9f);
                imgAO[x, y] = ToGrayscale(0.2f);
                continue;
            }

            int distFromEdgeX = Math.Min(lx - gapW, plankW - 1 - lx);
            int distFromEdgeY = Math.Min(ly - gapH, plankH - 1 - ly);
            int distFromEdge = Math.Min(distFromEdgeX, distFromEdgeY);
            
            float bevelFactor = 1f;
            if (distFromEdge < bevel)
            {
                bevelFactor = 0.70f + 0.30f * (distFromEdge / (float)bevel);
            }

            float px = x * 0.01f;
            float py = (y + plankOffsets[plankIdx]) * 0.005f;

            // Wood Grain using Domain Warping
            float warp = ProNoise.Fbm(px, py, 4) * 2.5f;
            float grain = ProNoise.Fbm(px + warp, py * 4.0f + warp, 6);
            
            // Contrast it
            grain = MathF.Pow(grain * 0.5f + 0.5f, 1.5f);

            // Knots for light wood
            float knotInfluence = 0f;
            if (!dark)
            {
                ProNoise.Voronoi(px * 0.5f, py * 0.5f, 1.0f, out float d1, out float d2);
                if (d1 < 0.15f)
                {
                    knotInfluence = 1f - (d1 / 0.15f);
                    grain += MathF.Sin(d1 * 50f) * knotInfluence * 0.5f; // Ring effect
                }
            }

            grain = Math.Clamp(grain, 0f, 1f);

            var (tR, tG, tB) = plankTints[plankIdx];

            float r = (baseR * grain + darkR * (1f - grain)) * tR;
            float g = (baseG * grain + darkG * (1f - grain)) * tG;
            float b = (baseB * grain + darkB * (1f - grain)) * tB;

            if (knotInfluence > 0)
            {
                r *= (1f - knotInfluence * 0.5f);
                g *= (1f - knotInfluence * 0.5f);
                b *= (1f - knotInfluence * 0.5f);
            }

            r *= bevelFactor;
            g *= bevelFactor;
            b *= bevelFactor;

            imgAlbedo[x, y] = ToRgba(r, g, b);
            
            // Roughness based on grain (darker grain is usually rougher or smoother depending on finish)
            float rough = dark ? (0.3f + grain * 0.2f) : (0.4f + grain * 0.3f + knotInfluence * 0.2f);
            imgRoughness[x, y] = ToGrayscale(rough);

            // AO
            imgAO[x, y] = ToGrayscale(bevelFactor);
        }
    }

    imgAlbedo.SaveAsPng(Path.Combine(dir, $"{name}.png"));
    imgRoughness.SaveAsPng(Path.Combine(dir, $"{name}-roughness.png"));
    imgAO.SaveAsPng(Path.Combine(dir, $"{name}-ao.png"));
    Console.WriteLine($"  ✓ {name,-28} {w}×{h} (Albedo, Roughness, AO)");
}


// ─────────────────────────────────────────────────────────────────────────────
//  Concrete — Cemento organico con Voronoi e fBM
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateConcrete(string dir, string name)
{
    const int w = 1024, h = 1024;
    using var imgAlbedo = new Image<Rgba32>(w, h);
    using var imgRoughness = new Image<Rgba32>(w, h);
    using var imgAO = new Image<Rgba32>(w, h);

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            float px = x * 0.01f;
            float py = y * 0.01f;

            // Low frequency color variation
            float lowF = ProNoise.Fbm(px * 0.2f, py * 0.2f, 4);
            // High frequency grit
            float highF = ProNoise.Fbm(px * 5f, py * 5f, 6);
            
            // Voronoi for pits / stains
            ProNoise.Voronoi(px * 0.8f, py * 0.8f, 1.0f, out float d1, out float d2);
            float pit = MathF.Max(0, 1.0f - d1 * 5f);
            pit *= pit; // sharp pits

            float val = 0.65f + lowF * 0.15f + highF * 0.05f - pit * 0.3f;
            val = Math.Clamp(val, 0f, 1f);

            // Slight warm tint
            imgAlbedo[x, y] = ToRgba(val, val * 0.98f, val * 0.95f);
            
            // Very rough surface
            float rough = 0.7f + highF * 0.2f + pit * 0.1f;
            imgRoughness[x, y] = ToGrayscale(Math.Clamp(rough, 0f, 1f));

            // AO is lowered in pits
            float ao = 1.0f - pit * 0.8f;
            imgAO[x, y] = ToGrayscale(Math.Clamp(ao, 0f, 1f));
        }
    }

    imgAlbedo.SaveAsPng(Path.Combine(dir, $"{name}.png"));
    imgRoughness.SaveAsPng(Path.Combine(dir, $"{name}-roughness.png"));
    imgAO.SaveAsPng(Path.Combine(dir, $"{name}-ao.png"));
    Console.WriteLine($"  ✓ {name,-28} {w}×{h} (Albedo, Roughness, AO)");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Metal Scratched — Graffi con matematica procedurale, PBR metallic workflow
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateMetalScratched(string dir, string name)
{
    const int w = 1024, h = 1024;
    using var imgAlbedo = new Image<Rgba32>(w, h);
    using var imgRoughness = new Image<Rgba32>(w, h);
    using var imgMetallic = new Image<Rgba32>(w, h);

    // Genera graffi lineari matematici ma con fBM path
    var rng = new Random(42);
    var scratches = new List<(float x, float y, float dx, float dy, float len, float depth, float w)>();
    for (int i = 0; i < 200; i++)
    {
        float angle = (float)rng.NextDouble() * MathF.PI;
        float len = 50 + rng.Next(400);
        float cx = rng.Next(w);
        float cy = rng.Next(h);
        float dx = MathF.Cos(angle);
        float dy = MathF.Sin(angle);
        float depth = 0.3f + (float)rng.NextDouble() * 0.7f;
        float width = 1.0f + (float)rng.NextDouble() * 2.5f;
        scratches.Add((cx, cy, dx, dy, len, depth, width));
    }

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            float scratchVal = 0;
            
            // We use a simplified distance check for performance instead of full segment distance for all 200
            // but for 1024x1024, an optimized segment distance is fine.
            foreach (var s in scratches)
            {
                float t = ((x - s.x) * s.dx + (y - s.y) * s.dy);
                if (t > -s.len/2 && t < s.len/2)
                {
                    float projX = s.x + t * s.dx;
                    float projY = s.y + t * s.dy;
                    float dist = MathF.Sqrt((x - projX)*(x - projX) + (y - projY)*(y - projY));
                    if (dist < s.w)
                    {
                        scratchVal = MathF.Max(scratchVal, s.depth * (1f - dist/s.w));
                    }
                }
            }

            float noise = ProNoise.Fbm(x * 0.1f, y * 0.1f, 3) * 0.05f;
            float baseAlbedo = 0.7f + noise;
            
            // Scratches reveal brighter/different metal and are rougher
            float albedo = Math.Clamp(baseAlbedo + scratchVal * 0.2f, 0f, 1f);
            
            imgAlbedo[x, y] = ToGrayscale(albedo);
            imgRoughness[x, y] = ToGrayscale(Math.Clamp(0.2f + noise * 2.0f + scratchVal * 0.4f, 0f, 1f));
            // Metal is fully metallic
            imgMetallic[x, y] = ToGrayscale(1.0f);
        }
    }

    imgAlbedo.SaveAsPng(Path.Combine(dir, $"{name}.png"));
    imgRoughness.SaveAsPng(Path.Combine(dir, $"{name}-roughness.png"));
    imgMetallic.SaveAsPng(Path.Combine(dir, $"{name}-metallic.png"));
    Console.WriteLine($"  ✓ {name,-28} {w}×{h} (Albedo, Roughness, Metallic)");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Earth Map — 2048x1024 (doppia risoluzione originale)
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateEarthMap(string path)
{
    const int w = 2048, h = 1024;
    using var imgAlbedo = new Image<Rgba32>(w, h);
    using var imgRoughness = new Image<Rgba32>(w, h);

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            float lon = (x / (float)w) * 2f * MathF.PI;
            float lat = (y / (float)h - 0.5f) * MathF.PI;

            // Pro-grade continent shapes using Ridged fBM on a sphere
            float nx = MathF.Cos(lat) * MathF.Cos(lon);
            float ny = MathF.Sin(lat);
            float nz = MathF.Cos(lat) * MathF.Sin(lon);

            float landNoise = ProNoise.RidgedFbm(nx * 3f, ny * 3f + nz * 3f, 6);
            float mask = ProNoise.Fbm(nx * 2f, ny * 2f, 4); 

            float land = landNoise * 0.7f + mask * 0.5f;

            bool isPole = MathF.Abs(lat) > 1.3f - ProNoise.Fbm(nx*5f, nz*5f, 3)*0.2f;

            if (isPole)
            {
                imgAlbedo[x, y] = new Rgba32(230, 235, 240);
                imgRoughness[x, y] = ToGrayscale(0.4f); // Ice is semi-rough
            }
            else if (land > 0.4f)
            {
                // Land
                float veg = ProNoise.Fbm(nx * 10f, ny * 10f, 4); // Vegetation
                int r = (int)(60 + veg * 50);
                int g = (int)(100 + veg * 60);
                int b = 40;
                imgAlbedo[x, y] = ToRgba(r/255f, g/255f, b/255f);
                imgRoughness[x, y] = ToGrayscale(0.9f); // Land is rough
            }
            else
            {
                // Ocean
                float depth = Math.Clamp(0.4f - land, 0f, 1f);
                int r = 10;
                int g = (int)(50 + depth * 50);
                int b = (int)(120 + depth * 80);
                imgAlbedo[x, y] = ToRgba(r/255f, g/255f, b/255f);
                imgRoughness[x, y] = ToGrayscale(0.1f); // Ocean is smooth
            }
        }
    }

    imgAlbedo.SaveAsPng(path);
    string dir = Path.GetDirectoryName(path)!;
    string name = Path.GetFileNameWithoutExtension(path);
    imgRoughness.SaveAsPng(Path.Combine(dir, $"{name}-roughness.png"));
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h} (Albedo, Roughness)");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Checkerboard
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateCheckerboard(string path)
{
    const int w = 1024, h = 1024;
    const int cellSize = 128;
    using var img = new Image<Rgba32>(w, h);

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            bool isWhite = ((x / cellSize) + (y / cellSize)) % 2 == 0;
            img[x, y] = isWhite ? new Rgba32(240, 240, 240) : new Rgba32(25, 25, 25);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  UV Grid
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateUVGrid(string path)
{
    const int w = 1024, h = 1024;
    const int gridSpacing = 64;
    using var img = new Image<Rgba32>(w, h);

    for (int y = 0; y < h; y++)
    {
        float fy = y / (float)(h - 1);
        for (int x = 0; x < w; x++)
        {
            float fx = x / (float)(w - 1);
            byte r = (byte)(fx * 255);
            byte g = (byte)((1f - fy) * 255);
            byte b = 40;

            bool isGridLine = (x % gridSpacing < 2) || (y % gridSpacing < 2);
            bool isBorder = x < 4 || x >= w - 4 || y < 4 || y >= h - 4;
            bool isCenterH = Math.Abs(x - w / 2) < 2;
            bool isCenterV = Math.Abs(y - h / 2) < 2;

            if (isBorder) img[x, y] = new Rgba32(255, 255, 255);
            else if (isCenterH || isCenterV) img[x, y] = new Rgba32(255, 255, 0);
            else if (isGridLine) img[x, y] = new Rgba32((byte)Math.Min(r + 60, 255), (byte)Math.Min(g + 60, 255), (byte)Math.Min(b + 60, 255));
            else img[x, y] = new Rgba32(r, g, b);
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Logo
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateLogo(string path)
{
    const int w = 1024, h = 768;
    using var img = new Image<Rgba32>(w, h);

    string[][] font = [
        [ ".###.", "....#", "....#", "..##.", "....#", "....#", ".###." ], // 3
        [ "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." ], // D
        [ ".....", ".....", ".....", ".###.", ".....", ".....", "....." ], // -
        [ "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" ], // R
        [ "..#..", ".#.#.", "#...#", "#####", "#...#", "#...#", "#...#" ], // A
        [ "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..", "..#.." ], // Y
    ];

    int charW = 5, charH = 7, spacing = 2;
    int totalChars = font.Length;
    int totalGridW = totalChars * charW + (totalChars - 1) * spacing;
    int scale = Math.Min(w / (totalGridW + 4), h / (charH + 6));
    int startX = (w - totalGridW * scale) / 2;
    int startY = (h - charH * scale) / 2;

    for (int y = 0; y < h; y++)
    {
        float fy = y / (float)h;
        for (int x = 0; x < w; x++)
        {
            float fx = x / (float)w;
            float cx = fx - 0.5f, cy = fy - 0.5f;
            float radial = MathF.Sqrt(cx * cx + cy * cy);
            
            // Fbm background
            float bgNoise = ProNoise.Fbm(fx * 5f, fy * 5f, 4) * 0.1f;
            int bgBase = (int)(18 + (1f - Math.Clamp(radial * 1.5f, 0f, 1f)) * 15 + bgNoise * 50);
            
            int bgR = Math.Clamp(bgBase, 0, 255);
            int bgG = Math.Clamp((int)(bgBase * 0.9f), 0, 255);
            int bgB = Math.Clamp((int)(bgBase * 1.3f), 0, 255);

            bool inGlyph = false;
            float glyphProgress = 0f;

            int gx = x - startX, gy = y - startY;

            if (gx >= 0 && gy >= 0 && gy < charH * scale)
            {
                int accumulatedX = 0;
                for (int c = 0; c < totalChars; c++)
                {
                    int charStartX = accumulatedX * scale;
                    int charEndX = (accumulatedX + charW) * scale;
                    if (gx >= charStartX && gx < charEndX)
                    {
                        int localX = (gx - charStartX) / scale;
                        int localY = gy / scale;
                        if (localX < charW && localY < charH && font[c][localY][localX] == '#')
                        {
                            inGlyph = true;
                            glyphProgress = (float)x / w;
                        }
                        break;
                    }
                    accumulatedX += charW + spacing;
                }
            }

            if (inGlyph)
            {
                float t = glyphProgress;
                int lr = (int)(30 + (1f - t) * 80);
                int lg = (int)(120 + t * 135);
                int lb = (int)(220 - t * 120);
                img[x, y] = new Rgba32((byte)lr, (byte)lg, (byte)lb);
            }
            else
            {
                img[x, y] = new Rgba32((byte)bgR, (byte)bgG, (byte)bgB);
            }
        }
    }
    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}
