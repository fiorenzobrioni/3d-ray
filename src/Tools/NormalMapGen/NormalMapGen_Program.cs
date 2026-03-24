// ═══════════════════════════════════════════════════════════════════════════
//  NormalMapGen — Generatore di Normal Map di test per 3D-Ray
//
//  Genera normal map procedurali corrispondenti alle texture albedo prodotte
//  da TextureGen. Le normal map vengono salvate nella stessa cartella delle
//  texture (scenes/textures/ di default) con suffisso "-normal".
//
//  Uso:
//    cd 3d-ray
//    dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj
//
//  Oppure con percorso personalizzato:
//    dotnet run --project src/Tools/NormalMapGen/NormalMapGen.csproj -- --output path/to/dir
//
//  Normal map generate:
//    brick-wall-normal.png          512×512   Mattoni con fughe incavate e bevel
//    wood-floor-normal.png          512×512   Parquet: doghe strette, fughe, venature
//    wood-planks-normal.png         512×512   Assi larghe: giunture, nodi, grana grossa
//    concrete-normal.png            512×512   Cemento grezzo poroso multi-frequenza
//    metal-scratched-normal.png     512×512   Graffi lineari randomizzati su metallo
//    stone-cobble-normal.png        512×512   Ciottoli / pavé Voronoi irregolare
//    fabric-weave-normal.png        512×512   Trama tessuto intrecciato
//    tiles-normal.png               512×512   Piastrelle quadrate con fughe e bevel
//    flat-normal.png                512×512   Normale piatta (128,128,255) — test
//
//  Abbinamento con TextureGen (albedo → normal):
//    brick-wall.png         → brick-wall-normal.png
//    brick-wall-white.png   → brick-wall-normal.png   (stessa geometria, colore diverso)
//    wood-floor.png         → wood-floor-normal.png
//    wood-planks.png        → wood-planks-normal.png
//    metal-scratched.png    → metal-scratched-normal.png
//    concrete.png           → concrete-normal.png
//
//  Convenzione: OpenGL standard (Y+ = su nel tangent space).
//  Il colore (128, 128, 255) = normale non perturbata (0, 0, 1).
// ═══════════════════════════════════════════════════════════════════════════

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// ── Parse CLI ───────────────────────────────────────────────────────────────
string outputDir = "scenes/textures";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--output" or "-o")
        outputDir = args[i + 1];
}

Directory.CreateDirectory(outputDir);

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║    NormalMapGen — 3D-Ray Normal Maps     ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Output: {Path.GetFullPath(outputDir)}/");
Console.WriteLine();

var sw = System.Diagnostics.Stopwatch.StartNew();

GenerateBrickWallNormal(Path.Combine(outputDir, "brick-wall-normal.png"));
GenerateWoodFloorNormal(Path.Combine(outputDir, "wood-floor-normal.png"));
GenerateWoodPlanksNormal(Path.Combine(outputDir, "wood-planks-normal.png"));
GenerateConcreteNormal(Path.Combine(outputDir, "concrete-normal.png"));
GenerateMetalScratchedNormal(Path.Combine(outputDir, "metal-scratched-normal.png"));
GenerateStoneCobbleNormal(Path.Combine(outputDir, "stone-cobble-normal.png"));
GenerateFabricWeaveNormal(Path.Combine(outputDir, "fabric-weave-normal.png"));
GenerateTilesNormal(Path.Combine(outputDir, "tiles-normal.png"));
GenerateFlatNormal(Path.Combine(outputDir, "flat-normal.png"));

Console.WriteLine();
Console.WriteLine($"  Note: brick-wall-normal.png serves both brick-wall.png and brick-wall-white.png");
Console.WriteLine();
Console.WriteLine($"Done! {sw.ElapsedMilliseconds} ms total");


// ═══════════════════════════════════════════════════════════════════════════
//  HELPERS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Encodes a tangent-space normal vector [-1,1] to an RGB pixel [0,255].
/// </summary>
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

/// <summary>
/// Simple deterministic hash noise [0, 1].
/// </summary>
static float Hash(int x, int y, int seed)
    => ((x * 73856093 ^ y * 19349663 ^ seed * 83492791) & 0x7FFFFFFF) / (float)0x7FFFFFFF;


// ═══════════════════════════════════════════════════════════════════════════
//  BRICK WALL — Fughe incavate con bevel + superficie ruvida
//
//  Mattone 64×32 px, malta 3 px — identico a TextureGen brick-wall.png.
//  Running bond (righe dispari sfalsate di metà mattone).
//  Serve sia per brick-wall.png che brick-wall-white.png (stessa geometria).
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateBrickWallNormal(string path)
{
    const int w = 512, h = 512;
    const int brickW = 64, brickH = 32, mortarW = 3;
    const int bevelPx = 4;

    using var img = new Image<Rgba32>(w, h);

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

                float nx = 0f, ny = 0f, nz = 1f;

                int distFromLeft   = lx;
                int distFromRight  = brickW - 1 - lx;
                int distFromTop    = ly;
                int distFromBottom = brickH - 1 - ly;

                bool inMortarX = lx < mortarW;
                bool inMortarY = ly < mortarW;

                if (inMortarX && inMortarY)
                {
                    nz = 0.5f;
                }
                else if (inMortarX)
                {
                    nx = (lx < mortarW / 2f) ? -0.6f : 0.6f;
                    nz = 0.65f;
                }
                else if (inMortarY)
                {
                    ny = (ly < mortarW / 2f) ? -0.6f : 0.6f;
                    nz = 0.65f;
                }
                else
                {
                    // Bevel ai bordi del mattone
                    if (distFromLeft < bevelPx + mortarW && distFromLeft >= mortarW)
                    {
                        float t = 1f - (distFromLeft - mortarW) / (float)bevelPx;
                        nx = -0.4f * t;
                        nz = 1f - 0.2f * t;
                    }
                    else if (distFromRight < bevelPx)
                    {
                        float t = 1f - distFromRight / (float)bevelPx;
                        nx = 0.4f * t;
                        nz = 1f - 0.2f * t;
                    }

                    if (distFromTop < bevelPx + mortarW && distFromTop >= mortarW)
                    {
                        float t = 1f - (distFromTop - mortarW) / (float)bevelPx;
                        ny = -0.4f * t;
                        nz = MathF.Min(nz, 1f - 0.2f * t);
                    }
                    else if (distFromBottom < bevelPx)
                    {
                        float t = 1f - distFromBottom / (float)bevelPx;
                        ny = 0.4f * t;
                        nz = MathF.Min(nz, 1f - 0.2f * t);
                    }

                    // Rumore superficiale fine
                    nx += (Hash(x, y, 1) - 0.5f) * 0.06f;
                    ny += (Hash(x, y, 2) - 0.5f) * 0.06f;
                }

                row[x] = EncodeNormal(nx, ny, nz);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}   (also for brick-wall-white)");
}


// ═══════════════════════════════════════════════════════════════════════════
//  WOOD FLOOR — Parquet: doghe strette (128 px), fughe, venature fini
//
//  Corrisponde a TextureGen wood-floor.png (parquet scuro).
//  Doghe strette con grana fine e stagger tra le file.
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateWoodFloorNormal(string path)
{
    const int w = 512, h = 512;
    const int plankW = 128, gapW = 2, bevelPx = 5;
    // Stagger: ogni doga ha un offset Y diverso per le giunture trasversali
    const int plankH = 256;

    using var img = new Image<Rgba32>(w, h);

    // Stagger fisso per riproducibilità
    int[] stagger = { 0, 97, 43, 181 };

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                int plank = x / plankW;
                int lx = x % plankW;
                int ly = (y + stagger[plank % stagger.Length]) % plankH;

                float nx = 0f, ny = 0f, nz = 1f;

                // Fuga verticale tra doghe
                if (lx < gapW)
                {
                    nx = (lx == 0) ? -0.5f : 0.5f;
                    nz = 0.7f;
                }
                // Giuntura trasversale (ogni plankH pixel)
                else if (ly < gapW)
                {
                    ny = (ly == 0) ? -0.4f : 0.4f;
                    nz = 0.75f;
                }
                else
                {
                    // Bevel bordo sinistro
                    if (lx - gapW < bevelPx)
                    {
                        float t = 1f - (lx - gapW) / (float)bevelPx;
                        nx = -0.3f * t;
                        nz = 1f - 0.1f * t;
                    }
                    // Bevel bordo destro
                    else if (plankW - 1 - lx < bevelPx)
                    {
                        float t = 1f - (plankW - 1 - lx) / (float)bevelPx;
                        nx = 0.3f * t;
                        nz = 1f - 0.1f * t;
                    }

                    // Venatura fine del legno: ondulazione lungo Y
                    float grain = MathF.Sin((y + plank * 137) * 0.4f) * 0.025f;
                    float grain2 = MathF.Sin((y + plank * 53) * 1.2f) * 0.012f;
                    ny += grain + grain2;

                    // Micro-rumore
                    nx += (Hash(x, y, 11) - 0.5f) * 0.025f;
                    ny += (Hash(x, y, 12) - 0.5f) * 0.025f;
                }

                row[x] = EncodeNormal(nx, ny, nz);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  WOOD PLANKS — Assi larghe: tavolo/staccionata, grana più grossa, nodi
//
//  Corrisponde a TextureGen wood-planks.png (legno chiaro).
//  Assi più larghe (170 px vs 128), grana pronunciata, nodi circolari.
//  Visivamente distinta da wood-floor-normal.png.
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateWoodPlanksNormal(string path)
{
    const int w = 512, h = 512;
    const int plankW = 170, gapW = 3, bevelPx = 6;

    // Nodi del legno: posizioni e raggi pre-calcolati
    var rng = new Random(99);
    var knots = new List<(int cx, int cy, float radius)>();
    for (int i = 0; i < 8; i++)
        knots.Add((rng.Next(w), rng.Next(h), 6f + (float)rng.NextDouble() * 10f));

    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                int lx = x % plankW;
                int plank = x / plankW;
                float nx = 0f, ny = 0f, nz = 1f;

                // Fuga tra assi
                if (lx < gapW)
                {
                    float frac = lx / (float)(gapW - 1);
                    nx = (frac < 0.5f) ? -0.55f : 0.55f;
                    nz = 0.65f;
                }
                else
                {
                    // Bevel bordi
                    if (lx - gapW < bevelPx)
                    {
                        float t = 1f - (lx - gapW) / (float)bevelPx;
                        nx = -0.35f * t;
                        nz = 1f - 0.12f * t;
                    }
                    else if (plankW - 1 - lx < bevelPx)
                    {
                        float t = 1f - (plankW - 1 - lx) / (float)bevelPx;
                        nx = 0.35f * t;
                        nz = 1f - 0.12f * t;
                    }

                    // Grana grossa del legno: onde più ampie rispetto al parquet
                    float grain = MathF.Sin((y + plank * 211) * 0.15f) * 0.04f;
                    float grain2 = MathF.Sin((y + plank * 79) * 0.6f) * 0.02f;
                    float grain3 = MathF.Sin((y * 0.08f + plank * 1.3f)) * 0.015f;
                    ny += grain + grain2;
                    nx += grain3;

                    // Nodi del legno: depressioni circolari
                    foreach (var knot in knots)
                    {
                        float dx = x - knot.cx;
                        float dy = y - knot.cy;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist < knot.radius)
                        {
                            // Anelli concentrici attorno al nodo
                            float ring = MathF.Sin(dist * 1.5f) * 0.12f * (1f - dist / knot.radius);
                            if (dist > 1e-3f)
                            {
                                nx += (dx / dist) * ring;
                                ny += (dy / dist) * ring;
                            }
                            // Centro del nodo: depressione
                            if (dist < knot.radius * 0.3f)
                            {
                                float dip = 0.15f * (1f - dist / (knot.radius * 0.3f));
                                if (dist > 1e-3f)
                                {
                                    nx += (dx / dist) * dip;
                                    ny += (dy / dist) * dip;
                                }
                            }
                        }
                    }

                    // Micro-rumore (più grosso del parquet)
                    nx += (Hash(x, y, 13) - 0.5f) * 0.04f;
                    ny += (Hash(x, y, 14) - 0.5f) * 0.04f;
                }

                row[x] = EncodeNormal(nx, ny, nz);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  CONCRETE — Superficie irregolare multi-frequenza (pori + ondulazioni)
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateConcreteNormal(string path)
{
    const int w = 512, h = 512;
    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                float nx = 0f, ny = 0f;

                // Fine grain (pori)
                nx += (Hash(x, y, 21) - 0.5f) * 0.18f;
                ny += (Hash(x, y, 22) - 0.5f) * 0.18f;

                // Medium bumps
                nx += (Hash(x / 4, y / 4, 23) - 0.5f) * 0.30f;
                ny += (Hash(x / 4, y / 4, 24) - 0.5f) * 0.30f;

                // Large undulations
                nx += (Hash(x / 16, y / 16, 25) - 0.5f) * 0.12f;
                ny += (Hash(x / 16, y / 16, 26) - 0.5f) * 0.12f;

                row[x] = EncodeNormal(nx, ny, 1f);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  METAL SCRATCHED — 50 graffi lineari con profondità/angolo/larghezza casuali
//  Seed fisso (42) per risultati riproducibili e allineamento con TextureGen.
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateMetalScratchedNormal(string path)
{
    const int w = 512, h = 512;
    using var img = new Image<Rgba32>(w, h);

    var rng = new Random(42);
    var scratches = new List<(float x0, float y0, float dx, float dy, float lenSq, float depth, float halfWidth)>();
    for (int i = 0; i < 50; i++)
    {
        float angle = (float)(rng.NextDouble() * Math.PI);
        float cx = rng.Next(w), cy = rng.Next(h);
        float len = 30 + rng.Next(250);
        float depth = 0.25f + (float)rng.NextDouble() * 0.55f;
        float halfW = 1.0f + (float)rng.NextDouble() * 1.5f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float x0 = cx - cos * len / 2, y0 = cy - sin * len / 2;
        float dx = cos * len, dy = sin * len;
        scratches.Add((x0, y0, dx, dy, dx * dx + dy * dy, depth, halfW));
    }

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                float nx = 0f, ny = 0f;

                foreach (var s in scratches)
                {
                    float t = Math.Clamp(((x - s.x0) * s.dx + (y - s.y0) * s.dy) / s.lenSq, 0f, 1f);
                    float nearX = s.x0 + t * s.dx;
                    float nearY = s.y0 + t * s.dy;
                    float dist = MathF.Sqrt((x - nearX) * (x - nearX) + (y - nearY) * (y - nearY));

                    if (dist < s.halfWidth)
                    {
                        float invLen = 1f / MathF.Sqrt(s.lenSq);
                        float perpX = -s.dy * invLen;
                        float perpY = s.dx * invLen;
                        float side = (x - nearX) * perpX + (y - nearY) * perpY;
                        float factor = s.depth * (1f - dist / s.halfWidth);
                        nx += perpX * factor * MathF.Sign(side);
                        ny += perpY * factor * MathF.Sign(side);
                    }
                }

                // Micro-graffi di fondo
                nx += (Hash(x, y, 31) - 0.5f) * 0.02f;
                ny += (Hash(x, y, 32) - 0.5f) * 0.02f;

                row[x] = EncodeNormal(nx, ny, 1f);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  STONE COBBLE — Ciottoli / pavé Voronoi con bordi arrotondati
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateStoneCobbleNormal(string path)
{
    const int w = 512, h = 512;
    const int numCells = 80;

    var rng = new Random(77);
    var centers = new (float x, float y)[numCells];
    for (int i = 0; i < numCells; i++)
        centers[i] = (rng.Next(w), rng.Next(h));

    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                float minDist = float.MaxValue, secDist = float.MaxValue;
                int closest = 0;
                for (int i = 0; i < numCells; i++)
                {
                    float dx = Math.Abs(x - centers[i].x);
                    float dy = Math.Abs(y - centers[i].y);
                    dx = Math.Min(dx, w - dx);
                    dy = Math.Min(dy, h - dy);
                    float d = dx * dx + dy * dy;

                    if (d < minDist)
                    {
                        secDist = minDist; minDist = d; closest = i;
                    }
                    else if (d < secDist) secDist = d;
                }

                float d1 = MathF.Sqrt(minDist);
                float d2 = MathF.Sqrt(secDist);
                float edge = d2 - d1;

                float nx, ny, nz;

                if (edge < 6f)
                {
                    float toCenterX = centers[closest].x - x;
                    float toCenterY = centers[closest].y - y;
                    float tcLen = MathF.Sqrt(toCenterX * toCenterX + toCenterY * toCenterY);
                    if (tcLen > 1e-3f)
                    {
                        nx = -(toCenterX / tcLen) * 0.6f * (1f - edge / 6f);
                        ny = -(toCenterY / tcLen) * 0.6f * (1f - edge / 6f);
                    }
                    else { nx = 0; ny = 0; }
                    nz = 0.6f + 0.4f * (edge / 6f);
                }
                else
                {
                    float toCenterX = centers[closest].x - x;
                    float toCenterY = centers[closest].y - y;
                    float tcLen = MathF.Sqrt(toCenterX * toCenterX + toCenterY * toCenterY);
                    float curvature = 0.15f / (1f + tcLen * 0.02f);
                    nx = (tcLen > 1e-3f) ? (toCenterX / tcLen) * curvature : 0f;
                    ny = (tcLen > 1e-3f) ? (toCenterY / tcLen) * curvature : 0f;
                    nz = 1f;

                    nx += (Hash(x, y, 41) - 0.5f) * 0.05f;
                    ny += (Hash(x, y, 42) - 0.5f) * 0.05f;
                }

                row[x] = EncodeNormal(nx, ny, nz);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  FABRIC WEAVE — Trama tessuto intrecciato (fili sovrapposti alternati)
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateFabricWeaveNormal(string path)
{
    const int w = 512, h = 512;
    const int weaveSize = 16;

    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                int wx = (x / weaveSize) % 2;
                int wy = (y / weaveSize) % 2;
                int lx = x % weaveSize;
                int ly = y % weaveSize;

                float nx = 0f, ny = 0f, nz = 1f;
                bool horizontalOnTop = (wx + wy) % 2 == 0;
                float centerDist;

                if (horizontalOnTop)
                {
                    centerDist = (ly - weaveSize / 2f) / (weaveSize / 2f);
                    ny = centerDist * 0.35f;
                }
                else
                {
                    centerDist = (lx - weaveSize / 2f) / (weaveSize / 2f);
                    nx = centerDist * 0.35f;
                }

                nz = 1f - MathF.Abs(centerDist) * 0.15f;

                nx += (Hash(x, y, 51) - 0.5f) * 0.04f;
                ny += (Hash(x, y, 52) - 0.5f) * 0.04f;

                row[x] = EncodeNormal(nx, ny, nz);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  TILES — Piastrelle quadrate 64×64 con fughe e bordi smussati
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateTilesNormal(string path)
{
    const int w = 512, h = 512;
    const int tileSize = 64, groutW = 3, bevelPx = 5;

    using var img = new Image<Rgba32>(w, h);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
            {
                int lx = x % tileSize;
                int ly = y % tileSize;

                float nx = 0f, ny = 0f, nz = 1f;

                bool inGroutX = lx < groutW;
                bool inGroutY = ly < groutW;

                if (inGroutX || inGroutY)
                {
                    if (inGroutX) nx = (lx < groutW / 2f) ? -0.5f : 0.5f;
                    if (inGroutY) ny = (ly < groutW / 2f) ? -0.5f : 0.5f;
                    nz = 0.6f;
                }
                else
                {
                    int dLeft = lx - groutW, dTop = ly - groutW;
                    int dRight = tileSize - 1 - lx, dBottom = tileSize - 1 - ly;

                    if (dLeft < bevelPx)
                    {
                        float t = 1f - dLeft / (float)bevelPx;
                        nx = -0.35f * t; nz = 1f - 0.15f * t;
                    }
                    else if (dRight < bevelPx)
                    {
                        float t = 1f - dRight / (float)bevelPx;
                        nx = 0.35f * t; nz = 1f - 0.15f * t;
                    }
                    if (dTop < bevelPx)
                    {
                        float t = 1f - dTop / (float)bevelPx;
                        ny = -0.35f * t; nz = MathF.Min(nz, 1f - 0.15f * t);
                    }
                    else if (dBottom < bevelPx)
                    {
                        float t = 1f - dBottom / (float)bevelPx;
                        ny = 0.35f * t; nz = MathF.Min(nz, 1f - 0.15f * t);
                    }

                    nx += (Hash(x, y, 61) - 0.5f) * 0.02f;
                    ny += (Hash(x, y, 62) - 0.5f) * 0.02f;
                }

                row[x] = EncodeNormal(nx, ny, nz);
            }
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}");
}


// ═══════════════════════════════════════════════════════════════════════════
//  FLAT — Normale non perturbata (128, 128, 255).
//  Applicarla a qualsiasi oggetto non deve cambiare nulla — è il test zero.
// ═══════════════════════════════════════════════════════════════════════════
static void GenerateFlatNormal(string path)
{
    const int w = 512, h = 512;
    using var img = new Image<Rgba32>(w, h);
    var flat = new Rgba32(128, 128, 255);

    img.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < h; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < w; x++)
                row[x] = flat;
        }
    });

    img.SaveAsPng(path);
    Console.WriteLine($"  ✓ {Path.GetFileName(path),-36} {w}×{h}   (test reference — no perturbation)");
}
