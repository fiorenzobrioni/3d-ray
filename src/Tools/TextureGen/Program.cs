// ═══════════════════════════════════════════════════════════════════════════
//  TextureGen — Generatore di texture di test per 3D-Ray
//
//  Genera un set di immagini procedurali pronte all'uso con le image textures
//  del motore di ray tracing. Le texture vengono salvate nella cartella
//  textures/ alla root del progetto.
//
//  Uso:
//    cd 3d-ray
//    dotnet run --project src/Tools/TextureGen/TextureGen.csproj
//
//  Oppure con percorso personalizzato:
//    dotnet run --project src/Tools/TextureGen/TextureGen.csproj -- --output textures
//
//  Texture generate:
//    brick-wall.png        512×512   Muro di mattoni rossi con malta
//    brick-wall-white.png  512×512   Mattoni bianchi (stile scandinavo)
//    wood-floor.png        512×512   Parquet a doghe di legno scuro
//    wood-planks.png       512×512   Assi di legno chiaro (tavolo/staccionata)
//    earth.png             1024×512  Mappa terrestre stilizzata (eq. rect.)
//    checkerboard.png      512×512   Scacchiera B/N ad alta risoluzione
//    grid-uv.png           512×512   Griglia UV di debug (colori per quadrante)
//    metal-scratched.png   512×512   Metallo graffiato (per metal+fuzz)
//    concrete.png          512×512   Cemento grezzo con macchie
//    logo-3dray.png        512×384   Logo "3D-Ray" con gradiente
// ═══════════════════════════════════════════════════════════════════════════

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// ── Parse CLI ───────────────────────────────────────────────────────────────
string outputDir = "textures";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--output" or "-o")
        outputDir = args[i + 1];
}

Directory.CreateDirectory(outputDir);

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║       TextureGen — 3D-Ray Textures       ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Output: {Path.GetFullPath(outputDir)}/");
Console.WriteLine();

var sw = System.Diagnostics.Stopwatch.StartNew();

GenerateBrickWall(Path.Combine(outputDir, "brick-wall.png"), false);
GenerateBrickWall(Path.Combine(outputDir, "brick-wall-white.png"), true);
GenerateWoodFloor(Path.Combine(outputDir, "wood-floor.png"), dark: true);
GenerateWoodFloor(Path.Combine(outputDir, "wood-planks.png"), dark: false);
GenerateEarthMap(Path.Combine(outputDir, "earth.png"));
GenerateCheckerboard(Path.Combine(outputDir, "checkerboard.png"));
GenerateUVGrid(Path.Combine(outputDir, "grid-uv.png"));
GenerateMetalScratched(Path.Combine(outputDir, "metal-scratched.png"));
GenerateConcrete(Path.Combine(outputDir, "concrete.png"));
GenerateLogo(Path.Combine(outputDir, "logo-3dray.png"));

Console.WriteLine();
Console.WriteLine($"Done! {sw.ElapsedMilliseconds} ms — 10 textures generated in {Path.GetFullPath(outputDir)}/");


// ═════════════════════════════════════════════════════════════════════════════
//  GENERATORS
// ═════════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────────────
//  Brick Wall — mattoni con malta, variante rossa o bianca
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateBrickWall(string path, bool white)
{
    const int w = 512, h = 512;
    const int brickW = 64, brickH = 32;
    const int mortarThick = 3;

    using var img = new Image<Rgba32>(w, h);

    var mortarColor = white
        ? new Rgba32(200, 198, 195)
        : new Rgba32(180, 175, 165);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            int brickRow = y / brickH;
            int offsetX = (brickRow % 2 == 1) ? brickW / 2 : 0;

            for (int x = 0; x < w; x++)
            {
                int lx = (x + offsetX) % brickW;
                int ly = y % brickH;

                if (lx < mortarThick || ly < mortarThick)
                {
                    row[x] = mortarColor;
                }
                else
                {
                    int seed = ((x + offsetX) / brickW) * 31 + brickRow * 17;
                    int noise = ((x * 31 + y * 17) % 20) - 10;

                    int rv, gv, bv;
                    if (white)
                    {
                        rv = 215 + (seed * 7 % 25) + noise / 2;
                        gv = 210 + (seed * 13 % 20) + noise / 2;
                        bv = 205 + (seed * 23 % 20) + noise / 3;
                    }
                    else
                    {
                        rv = 160 + (seed * 7 % 40) + noise;
                        gv = 60 + (seed * 13 % 30) + noise / 2;
                        bv = 40 + (seed * 23 % 20) + noise / 3;
                    }

                    row[x] = new Rgba32(
                        (byte)Math.Clamp(rv, 0, 255),
                        (byte)Math.Clamp(gv, 0, 255),
                        (byte)Math.Clamp(bv, 0, 255));
                }
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Wood Floor — doghe di legno con venature sinusoidali
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateWoodFloor(string path, bool dark)
{
    const int w = 512, h = 512;
    const int plankW = 128;
    const int gapW = 2;

    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                int plank = x / plankW;
                int lx = x % plankW;

                if (lx < gapW)
                {
                    row[x] = dark ? new Rgba32(30, 22, 14) : new Rgba32(140, 120, 90);
                    continue;
                }

                int plankSeed = plank * 137;
                float grain = MathF.Sin((y + plankSeed) * 0.15f) * 0.5f + 0.5f;
                float noise = ((x * 31 + y * 17 + plankSeed) % 100) / 100f * 0.1f;
                float v = Math.Clamp(grain + noise, 0f, 1f);

                int rv, gv, bv;
                if (dark)
                {
                    rv = (int)(140 * v + 70 * (1 - v));
                    gv = (int)(95 * v + 45 * (1 - v));
                    bv = (int)(55 * v + 22 * (1 - v));
                }
                else
                {
                    rv = (int)(210 * v + 160 * (1 - v));
                    gv = (int)(180 * v + 130 * (1 - v));
                    bv = (int)(130 * v + 85 * (1 - v));
                }

                row[x] = new Rgba32((byte)rv, (byte)gv, (byte)bv);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Earth Map — continenti stilizzati, proiezione equirettangolare
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateEarthMap(string path)
{
    const int w = 1024, h = 512;
    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            float lat = (y / (float)h - 0.5f) * MathF.PI;

            for (int x = 0; x < w; x++)
            {
                float lon = (x / (float)w) * 2f * MathF.PI;

                float land = MathF.Sin(lon * 2f + 1f) * MathF.Cos(lat * 1.5f)
                           + MathF.Sin(lon * 3f - 0.5f) * MathF.Sin(lat * 2f + 1f) * 0.5f
                           + MathF.Cos(lon * 5f + 2f) * MathF.Cos(lat * 3f) * 0.3f;

                bool isPole = MathF.Abs(lat) > 1.2f;

                if (isPole)
                {
                    row[x] = new Rgba32(230, 235, 240);
                }
                else if (land > 0.15f)
                {
                    int g = (int)(80 + land * 80);
                    int r = (int)(60 + land * 50);
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(r, 0, 255),
                        (byte)Math.Clamp(g, 0, 255), 30);
                }
                else
                {
                    float depth = Math.Clamp(-land * 2f, 0f, 1f);
                    int b = (int)(140 + depth * 60);
                    int g = (int)(80 + depth * 30);
                    row[x] = new Rgba32(20,
                        (byte)Math.Clamp(g, 0, 255),
                        (byte)Math.Clamp(b, 0, 255));
                }
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Checkerboard — scacchiera B/N ad alta risoluzione, tileable
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateCheckerboard(string path)
{
    const int w = 512, h = 512;
    const int cellSize = 64;
    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                bool isWhite = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                row[x] = isWhite ? new Rgba32(240, 240, 240) : new Rgba32(25, 25, 25);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  UV Grid — griglia di debug con colori per quadrante e coordinate
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateUVGrid(string path)
{
    const int w = 512, h = 512;
    const int gridSpacing = 32;
    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            float fy = y / (float)(h - 1);

            for (int x = 0; x < w; x++)
            {
                float fx = x / (float)(w - 1);

                // Quadrant coloring: R = U, G = V, B = low constant
                byte r = (byte)(fx * 255);
                byte g = (byte)((1f - fy) * 255); // Flip V for visual convention
                byte b = 40;

                // Grid lines
                bool isGridLine = (x % gridSpacing < 2) || (y % gridSpacing < 2);
                // Border
                bool isBorder = x < 2 || x >= w - 2 || y < 2 || y >= h - 2;
                // Center cross
                bool isCenterH = Math.Abs(x - w / 2) < 1;
                bool isCenterV = Math.Abs(y - h / 2) < 1;

                if (isBorder)
                {
                    row[x] = new Rgba32(255, 255, 255);
                }
                else if (isCenterH || isCenterV)
                {
                    row[x] = new Rgba32(255, 255, 0);
                }
                else if (isGridLine)
                {
                    row[x] = new Rgba32(
                        (byte)Math.Min(r + 60, 255),
                        (byte)Math.Min(g + 60, 255),
                        (byte)Math.Min(b + 60, 255));
                }
                else
                {
                    row[x] = new Rgba32(r, g, b);
                }
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Metal Scratched — superficie metallica graffiata (per metal + fuzz basso)
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateMetalScratched(string path)
{
    const int w = 512, h = 512;
    using var img = new Image<Rgba32>(w, h);

    // Genera graffi pseudo-random
    var rng = new Random(42);
    var scratches = new List<(int x0, int y0, int x1, int y1, int bright)>();
    for (int i = 0; i < 120; i++)
    {
        int x0 = rng.Next(w), y0 = rng.Next(h);
        int angle = rng.Next(-30, 30); // Mostly horizontal scratches
        int len = rng.Next(40, 200);
        float rad = angle * MathF.PI / 180f;
        int x1 = x0 + (int)(len * MathF.Cos(rad));
        int y1 = y0 + (int)(len * MathF.Sin(rad));
        scratches.Add((x0, y0, x1, y1, rng.Next(180, 230)));
    }

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                // Base metal color with subtle noise
                int noise = ((x * 73 + y * 137) % 16) - 8;
                int baseVal = 155 + noise;

                // Check proximity to any scratch
                int scratchVal = 0;
                foreach (var (sx0, sy0, sx1, sy1, bright) in scratches)
                {
                    float dist = DistToSegment(x, y, sx0, sy0, sx1, sy1);
                    if (dist < 1.5f)
                    {
                        scratchVal = Math.Max(scratchVal, bright - (int)(dist * 30));
                    }
                }

                int final = Math.Max(baseVal, scratchVal);
                row[x] = new Rgba32((byte)Math.Clamp(final, 0, 255),
                                    (byte)Math.Clamp(final - 2, 0, 255),
                                    (byte)Math.Clamp(final - 5, 0, 255));
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

static float DistToSegment(float px, float py, float x0, float y0, float x1, float y1)
{
    float dx = x1 - x0, dy = y1 - y0;
    float len2 = dx * dx + dy * dy;
    if (len2 < 0.001f) return MathF.Sqrt((px - x0) * (px - x0) + (py - y0) * (py - y0));
    float t = Math.Clamp(((px - x0) * dx + (py - y0) * dy) / len2, 0f, 1f);
    float projX = x0 + t * dx, projY = y0 + t * dy;
    return MathF.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
}

// ─────────────────────────────────────────────────────────────────────────────
//  Concrete — cemento grezzo con variazione e macchie scure
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateConcrete(string path)
{
    const int w = 512, h = 512;
    using var img = new Image<Rgba32>(w, h);

    var rng = new Random(99);
    // Pre-generate stain centers
    var stains = new List<(int cx, int cy, int radius, int darken)>();
    for (int i = 0; i < 25; i++)
        stains.Add((rng.Next(w), rng.Next(h), rng.Next(15, 50), rng.Next(10, 35)));

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                // Base concrete grey with coarse noise
                int coarse = ((x * 7 + y * 13) % 23) - 11;
                int fine = ((x * 131 + y * 97) % 11) - 5;
                int baseVal = 165 + coarse + fine;

                // Stain darkening
                int stainDark = 0;
                foreach (var (cx, cy, r, d) in stains)
                {
                    float dist = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (dist < r)
                    {
                        float t = 1f - dist / r;
                        stainDark = Math.Max(stainDark, (int)(d * t * t));
                    }
                }

                int v = Math.Clamp(baseVal - stainDark, 0, 255);
                // Slight warm tint (concrete isn't pure grey)
                row[x] = new Rgba32((byte)v, (byte)Math.Max(v - 3, 0), (byte)Math.Max(v - 7, 0));
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Logo — "3D-Ray" con gradiente su sfondo scuro
// ─────────────────────────────────────────────────────────────────────────────
static void GenerateLogo(string path)
{
    const int w = 512, h = 384;
    using var img = new Image<Rgba32>(w, h);

    // Simple bitmap font for "3D-RAY" — 6 chars, each 5 wide in a 7-high grid
    // Each string is a 5×7 bitmask where '#' = pixel on
    string[][] font = [
        [ // 3
            ".###.",
            "....#",
            "....#",
            "..##.",
            "....#",
            "....#",
            ".###.",
        ],
        [ // D
            "####.",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            "#...#",
            "####.",
        ],
        [ // -
            ".....",
            ".....",
            ".....",
            ".###.",
            ".....",
            ".....",
            ".....",
        ],
        [ // R
            "####.",
            "#...#",
            "#...#",
            "####.",
            "#.#..",
            "#..#.",
            "#...#",
        ],
        [ // A
            "..#..",
            ".#.#.",
            "#...#",
            "#####",
            "#...#",
            "#...#",
            "#...#",
        ],
        [ // Y
            "#...#",
            ".#.#.",
            "..#..",
            "..#..",
            "..#..",
            "..#..",
            "..#..",
        ],
    ];

    int charW = 5, charH = 7;
    int totalChars = font.Length;
    int spacing = 2;
    int totalGridW = totalChars * charW + (totalChars - 1) * spacing;

    // Scale and center
    int scale = Math.Min(w / (totalGridW + 4), h / (charH + 6));
    int startX = (w - totalGridW * scale) / 2;
    int startY = (h - charH * scale) / 2;

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            float fy = y / (float)h;

            for (int x = 0; x < w; x++)
            {
                float fx = x / (float)w;

                // Dark background with radial gradient
                float cx = fx - 0.5f, cy = fy - 0.5f;
                float radial = MathF.Sqrt(cx * cx + cy * cy);
                int bgBase = (int)(18 + (1f - Math.Clamp(radial * 1.5f, 0f, 1f)) * 15);
                int bgR = bgBase;
                int bgG = (int)(bgBase * 0.9f);
                int bgB = (int)(bgBase * 1.3f);

                // Check if pixel is in a font glyph
                bool inGlyph = false;
                float glyphProgress = 0f; // 0..1 across all text for gradient

                int gx = x - startX;
                int gy = y - startY;

                if (gx >= 0 && gy >= 0 && gy < charH * scale)
                {
                    int charIdx = 0;
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
                                charIdx = c;
                            }
                            break;
                        }
                        accumulatedX += charW + spacing;
                    }
                }

                if (inGlyph)
                {
                    // Gradient across text: blue → cyan → green
                    float t = glyphProgress;
                    int lr = (int)(30 + (1f - t) * 80);
                    int lg = (int)(120 + t * 135);
                    int lb = (int)(220 - t * 120);
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(lr, 0, 255),
                        (byte)Math.Clamp(lg, 0, 255),
                        (byte)Math.Clamp(lb, 0, 255));
                }
                else
                {
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(bgR, 0, 255),
                        (byte)Math.Clamp(bgG, 0, 255),
                        (byte)Math.Clamp(bgB, 0, 255));
                }
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-28} {w}×{h}");
}
