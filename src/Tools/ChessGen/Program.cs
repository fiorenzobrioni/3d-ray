using System;
using System.IO;
using System.Text;
using System.Globalization;

namespace ChessGen
{
    // Generates scenes/chess.yaml: a Staunton chess set laid out in the Italian Game
    // (Giuoco Piano) opening position after 1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5, using
    // templates + instances. Material + lighting are tuned for a moody "tournament"
    // look: porcelain pieces on a walnut board, three-point studio lighting on a
    // near-black gradient sky.
    //
    // Coordinate system:
    //   file a..h -> x = (3.5 - file_index), so x ∈ {3.5, 2.5, ..., -3.5}
    //   rank 1..8 -> z = (rank - 4.5),       so z ∈ {-3.5, -2.5, ..., 3.5}
    //   White back rank at z = -3.5, Black back rank at z = 3.5.
    //
    //   x is *decreasing* from a to h because the camera's U (right) vector points
    //   in the world -x direction for this look-at setup; negating the file index
    //   restores the standard White-on-left orientation in the rendered image.
    class Program
    {
        static void Main(string[] args)
        {
            var sb = new StringBuilder();

            EmitHeader(sb);
            EmitWorld(sb);
            EmitCameras(sb);
            EmitMaterials(sb);
            EmitLights(sb);
            EmitTemplates(sb);
            EmitEntities(sb);

            string outPath = ResolveOutputPath();
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Chess scene generated: {outPath}");
        }

        // Walk up from the running binary until we find the repo root (identified by
        // the presence of a `scenes/` directory), then point at scenes/chess.yaml.
        // Robust whether `dotnet run` is invoked from the repo root or from the
        // project directory.
        static string ResolveOutputPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "scenes")))
                dir = dir.Parent;
            if (dir == null)
                throw new DirectoryNotFoundException("Could not locate repo root (no 'scenes/' directory found above the binary).");
            return Path.Combine(dir.FullName, "scenes", "chess.yaml");
        }

        // ---------- formatting helpers ----------

        static string VFmt(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        static string Vec3(float x, float y, float z) => $"[{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]";

        // Returns the world-space X coordinate for a given chess file.
        // file: 0=a, 1=b, ..., 7=h
        // x decreases as the file index increases so that the rendered image
        // shows a-file on the left and h-file on the right from White's perspective.
        static float FileX(int file) => 3.5f - file;

        // Returns the world-space Z coordinate for a given chess rank (1..8).
        static float RankZ(int rank) => rank - 4.5f;

        // ---------- sections ----------

        static void EmitHeader(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  Chess — Set Staunton, Apertura Italiana (Giuoco Piano)");
            sb.AppendLine("#");
            sb.AppendLine("#  Set di scacchi completo in porcellana chiara/scura su scacchiera lignea.");
            sb.AppendLine("#  Posizione: 1.e4 e5 2.Cf3 Cc6 3.Ac4 Ac5 (Giuoco Piano, 3 mosse per lato).");
            sb.AppendLine("#  Generato proceduralmente da src/Tools/ChessGen — modifica Program.cs");
            sb.AppendLine("#  per rigenerare la posizione o cambiare l'apertura.");
            sb.AppendLine("#");
            sb.AppendLine("#  Illuminazione a tre punti su gradient sky quasi-nero: key calda");
            sb.AppendLine("#  dall'alto-sinistra, fill fredda opposta, rim posteriore per separare i");
            sb.AppendLine("#  pezzi dallo sfondo.");
            sb.AppendLine("#");
            sb.AppendLine("#  Suggerimenti di rendering:");
            sb.AppendLine("#    Preview:  -w 640  -H 360  -s 64   -d 4  -S 1");
            sb.AppendLine("#    Standard: -w 1280 -H 720  -s 256  -d 6  -S 4");
            sb.AppendLine("#    Finale:   -w 1920 -H 1080 -s 1024 -d 8  -S 4");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();
        }

        static void EmitWorld(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  WORLD — Sky gradient quasi-nero con sun disk e piano in legno");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("world:");
            sb.AppendLine("  # Lo sky gradient fornisce ai metalli un cielo credibile da riflettere");
            sb.AppendLine("  # ed e' la sola sorgente di illuminazione ambientale (path-traced GI).");
            sb.AppendLine("  sky:");
            sb.AppendLine("    type: \"gradient\"");
            sb.AppendLine("    zenith_color:  [0.02, 0.03, 0.06]");
            sb.AppendLine("    horizon_color: [0.08, 0.07, 0.09]");
            sb.AppendLine("    ground_color:  [0.01, 0.01, 0.015]");
            sb.AppendLine("    sun:");
            sb.AppendLine("      direction: [-0.6, -0.8, -0.4]");
            sb.AppendLine("      color: [1.0, 0.96, 0.88]");
            sb.AppendLine("      intensity: 2.0");
            sb.AppendLine("      size: 2.5");
            sb.AppendLine("  ground:");
            sb.AppendLine("    type: \"infinite_plane\"");
            sb.AppendLine("    material: \"table_wood\"");
            sb.AppendLine("    y: -0.20");
            sb.AppendLine();
        }

        static void EmitCameras(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  CAMERAS");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("cameras:");
            sb.AppendLine("  - name:       classica        # Prospettiva Classica");
            sb.AppendLine("    position:   [0, 5, -8]");
            sb.AppendLine("    look_at:    [0, -0.2, 0]");
            sb.AppendLine("    fov:        45");
            sb.AppendLine("    aperture:   0.1");
            sb.AppendLine("    focal_dist: 12.0");
            sb.AppendLine();
            sb.AppendLine("  - name:       macro           # Dettaglio sui Pezzi Centrali");
            sb.AppendLine("    position:   [1.5, 1.2, -4]");
            sb.AppendLine("    look_at:    [0, 0.8, 0]");
            sb.AppendLine("    fov:        25.0");
            sb.AppendLine("    aperture:   0.2");
            sb.AppendLine("    focal_dist: 4.25");
            sb.AppendLine();
            sb.AppendLine("  - name:       hero            # Angolo Basso e Imponente");
            sb.AppendLine("    position:   [0.0, 0.5, -6.5]");
            sb.AppendLine("    look_at:    [0.0, 1.0, 0.5]");
            sb.AppendLine("    fov:        55.0");
            sb.AppendLine("    aperture:   0.1");
            sb.AppendLine("    focal_dist: 7.0");
            sb.AppendLine();
            sb.AppendLine("  - name:       zenitale        # Vista dall'alto tattica");
            sb.AppendLine("    position:   [0.0, 12.0, 0.01]");
            sb.AppendLine("    look_at:    [0.0, 0.0, 0.0]");
            sb.AppendLine("    fov:        35.0");
            sb.AppendLine("    aperture:   0.0");
            sb.AppendLine("    focal_dist: 12.0");
            sb.AppendLine();
            sb.AppendLine("  - name:       dutch           # Dinamica e Inclinata");
            sb.AppendLine("    position:   [6.0, 4.0, -6.0]");
            sb.AppendLine("    look_at:    [0.0, 0.5, 0.0]");
            sb.AppendLine("    fov:        40.0");
            sb.AppendLine("    aperture:   0.1");
            sb.AppendLine("    focal_dist: 8.5");
            sb.AppendLine();
            sb.AppendLine("  - name:       pawns_eye       # Ad altezza pedina, Bokeh estremo");
            sb.AppendLine("    position:   [0.0, 0.4, -3.5]");
            sb.AppendLine("    look_at:    [0.0, 0.6, 2.0]");
            sb.AppendLine("    fov:        70.0");
            sb.AppendLine("    aperture:   0.3");
            sb.AppendLine("    focal_dist: 3.5");
            sb.AppendLine();
        }

        static void EmitMaterials(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  MATERIALS");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("materials:");

            sb.AppendLine("  - id: \"table_wood\"");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"wood\"");
            sb.AppendLine("      scale: 4.0");
            sb.AppendLine("      noise_strength: 2.8");
            sb.AppendLine("      colors: [[0.28, 0.16, 0.08], [0.15, 0.08, 0.04]]");
            sb.AppendLine("      randomize_offset: true");
            sb.AppendLine("      rotation: [90, 0, 0]");
            sb.AppendLine();

            sb.AppendLine("  - id: \"board_checker\"");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"checker\"");
            sb.AppendLine("      scale: 1.0");
            sb.AppendLine("      colors: [[0.82, 0.70, 0.48], [0.20, 0.11, 0.06]]");
            sb.AppendLine();

            sb.AppendLine("  - id: \"board_border\"");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"wood\"");
            sb.AppendLine("      scale: 1.8");
            sb.AppendLine("      noise_strength: 2.0");
            sb.AppendLine("      colors: [[0.22, 0.09, 0.05], [0.09, 0.04, 0.02]]");
            sb.AppendLine();

            sb.AppendLine("  - id: \"piece_white\"");
            sb.AppendLine("    type: \"disney\"");
            sb.AppendLine("    color: [0.92, 0.84, 0.68]");
            sb.AppendLine("    metallic: 0.0");
            sb.AppendLine("    roughness: 0.28");
            sb.AppendLine("    specular: 0.5");
            sb.AppendLine("    clearcoat: 0.5");
            sb.AppendLine("    coat_roughness: 0.15");
            sb.AppendLine();

            sb.AppendLine("  - id: \"piece_black\"");
            sb.AppendLine("    type: \"disney\"");
            sb.AppendLine("    color: [0.05, 0.04, 0.035]");
            sb.AppendLine("    metallic: 0.0");
            sb.AppendLine("    roughness: 0.22");
            sb.AppendLine("    specular: 0.5");
            sb.AppendLine("    clearcoat: 0.6");
            sb.AppendLine("    coat_roughness: 0.10");
            sb.AppendLine();
        }

        static void EmitLights(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  LIGHTS — Three-point: key calda + fill fredda + rim posteriore");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("lights:");
            sb.AppendLine("  # Key Light — allineata approssimativamente alla direzione del sun disk");
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [-3, 10, -4]");
            sb.AppendLine("    color: [1.0, 0.95, 0.9]");
            sb.AppendLine("    intensity: 50");
            sb.AppendLine();
            sb.AppendLine("  # Fill Light — luce fredda più debole dall'altro lato, ammorbidisce le ombre");
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [5, 6, -3]");
            sb.AppendLine("    color: [0.85, 0.85, 1.0]");
            sb.AppendLine("    intensity: 20");
            sb.AppendLine();
            sb.AppendLine("  # Back/Rim Light — luce posteriore per distaccare i pezzi dallo sfondo");
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [0, 8, 6]");
            sb.AppendLine("    color: [1.0, 0.95, 0.85]");
            sb.AppendLine("    intensity: 140");
            sb.AppendLine();
        }

        // ---------- templates ----------

        static void EmitTemplates(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  TEMPLATES — Un template per tipo di pezzo (pedone, torre, alfiere, …)");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("templates:");
            EmitTemplate(sb, "pawn",         BuildPawn);
            EmitTemplate(sb, "rook",         BuildRook);
            EmitTemplate(sb, "bishop",       BuildBishop);
            EmitTemplate(sb, "queen",        BuildQueen);
            EmitTemplate(sb, "king",         BuildKing);
            EmitTemplate(sb, "knight_white", sb2 => BuildKnight(sb2, +1));
            EmitTemplate(sb, "knight_black", sb2 => BuildKnight(sb2, -1));
        }

        static void EmitTemplate(StringBuilder sb, string name, Action<StringBuilder> buildChildren)
        {
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    children:");
            buildChildren(sb);
            sb.AppendLine();
        }

        // Primitive emitters for template children: no name, no material (inherited),
        // always in local coordinates around the piece origin at (0,0,0).

        static void ChildCyl(StringBuilder sb, float y, float radius, float height)
        {
            sb.AppendLine("      - type: \"cylinder\"");
            sb.AppendLine($"        center: {Vec3(0, y, 0)}");
            sb.AppendLine($"        radius: {VFmt(radius)}");
            sb.AppendLine($"        height: {VFmt(height)}");
        }

        static void ChildSph(StringBuilder sb, float y, float radius) =>
            ChildSphAt(sb, 0, y, 0, radius);

        static void ChildSphAt(StringBuilder sb, float x, float y, float z, float radius)
        {
            sb.AppendLine("      - type: \"sphere\"");
            sb.AppendLine($"        center: {Vec3(x, y, z)}");
            sb.AppendLine($"        radius: {VFmt(radius)}");
        }

        // Box is centred on its `translate`; (dx, dy, dz) are HALF extents.
        static void ChildBox(StringBuilder sb, float x, float y, float z, float dx, float dy, float dz)
        {
            sb.AppendLine("      - type: \"box\"");
            sb.AppendLine($"        scale: {Vec3(dx * 2, dy * 2, dz * 2)}");
            sb.AppendLine($"        translate: {Vec3(x, y, z)}");
        }

        // ---------- piece geometry (local coords, base at y=0) ----------

        static void BuildPawn(StringBuilder sb)
        {
            ChildCyl(sb, 0.00f, 0.35f, 0.10f);   // base1
            ChildCyl(sb, 0.10f, 0.25f, 0.05f);   // base2
            ChildCyl(sb, 0.15f, 0.16f, 0.40f);   // stem
            ChildCyl(sb, 0.55f, 0.20f, 0.05f);   // collar
            ChildSph(sb, 0.78f, 0.22f);           // head
        }

        static void BuildRook(StringBuilder sb)
        {
            ChildCyl(sb, 0.00f, 0.40f, 0.15f);   // base1
            ChildCyl(sb, 0.15f, 0.30f, 0.05f);   // base2
            ChildCyl(sb, 0.20f, 0.24f, 0.60f);   // stem
            ChildCyl(sb, 0.80f, 0.34f, 0.20f);   // head ring
            // 4 crenellations on the corners of the top ring
            ChildBox(sb, -0.22f, 1.05f, -0.22f, 0.08f, 0.05f, 0.08f);
            ChildBox(sb,  0.22f, 1.05f, -0.22f, 0.08f, 0.05f, 0.08f);
            ChildBox(sb, -0.22f, 1.05f,  0.22f, 0.08f, 0.05f, 0.08f);
            ChildBox(sb,  0.22f, 1.05f,  0.22f, 0.08f, 0.05f, 0.08f);
        }

        static void BuildBishop(StringBuilder sb)
        {
            ChildCyl(sb, 0.00f, 0.35f, 0.10f);   // base1
            ChildCyl(sb, 0.10f, 0.25f, 0.05f);   // base2
            ChildCyl(sb, 0.15f, 0.15f, 0.55f);   // stem
            ChildCyl(sb, 0.70f, 0.22f, 0.05f);   // collar
            ChildSph(sb, 0.92f, 0.22f);           // mitre lower
            ChildSph(sb, 1.08f, 0.14f);           // mitre upper
            ChildSph(sb, 1.28f, 0.06f);           // top ball
        }

        static void BuildQueen(StringBuilder sb)
        {
            ChildCyl(sb, 0.00f, 0.40f, 0.10f);   // base1
            ChildCyl(sb, 0.10f, 0.30f, 0.05f);   // base2
            ChildCyl(sb, 0.15f, 0.20f, 0.80f);   // stem
            ChildCyl(sb, 0.95f, 0.30f, 0.05f);   // collar
            ChildSph(sb, 1.15f, 0.20f);           // head
            // 8 crown points
            for (int i = 0; i < 8; i++)
            {
                double ang = i * Math.PI / 4.0;
                float px = (float)Math.Cos(ang) * 0.20f;
                float pz = (float)Math.Sin(ang) * 0.20f;
                ChildSphAt(sb, px, 1.30f, pz, 0.06f);
            }
            ChildSph(sb, 1.42f, 0.05f);           // centre pearl
        }

        static void BuildKing(StringBuilder sb)
        {
            ChildCyl(sb, 0.00f, 0.40f, 0.10f);   // base1
            ChildCyl(sb, 0.10f, 0.30f, 0.08f);   // base2
            ChildCyl(sb, 0.18f, 0.22f, 0.85f);   // stem
            ChildCyl(sb, 1.03f, 0.34f, 0.05f);   // collar
            ChildSph(sb, 1.25f, 0.24f);           // head
            ChildSph(sb, 1.45f, 0.04f);           // cross base pearl
            // Latin cross (vertical + horizontal bars)
            ChildBox(sb, 0.00f, 1.58f, 0.00f, 0.03f, 0.12f, 0.03f);
            ChildBox(sb, 0.00f, 1.60f, 0.00f, 0.08f, 0.03f, 0.03f);
        }

        // Knight faces towards lookDir * +Z. White pieces (lookDir = +1) face Black
        // (positive Z); Black (-1) faces back toward White (negative Z).
        static void BuildKnight(StringBuilder sb, int lookDir)
        {
            ChildCyl(sb, 0.00f, 0.35f, 0.10f);   // base1
            ChildCyl(sb, 0.10f, 0.25f, 0.05f);   // base2
            ChildCyl(sb, 0.15f, 0.22f, 0.40f);   // stem
            ChildSph(sb, 0.65f, 0.25f);           // chest
            ChildSphAt(sb, 0.00f, 0.90f, lookDir * 0.10f, 0.22f);          // head
            ChildBox(sb, 0.00f, 0.85f, lookDir * 0.25f, 0.15f, 0.10f, 0.18f); // snout
            ChildSphAt(sb, -0.10f, 1.05f, -lookDir * 0.05f, 0.06f);       // ear left
            ChildSphAt(sb,  0.10f, 1.05f, -lookDir * 0.05f, 0.06f);       // ear right
        }

        // ---------- entities (board + piece instances) ----------

        // Italian Game (Giuoco Piano) after 1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5.
        // x = FileX(file_index_0based), z = RankZ(rank_1based).
        record Inst(string Name, string Template, float X, float Z, string Mat);

        static readonly Inst[] Pieces = new[]
        {
            // ── White back rank (untouched): Ra1 Nb1 Bc1 Qd1 Ke1 Rh1 ──────────────
            new Inst("wr_a1", "rook",         FileX(0), RankZ(1), "piece_white"),  // a1
            new Inst("wn_b1", "knight_white", FileX(1), RankZ(1), "piece_white"),  // b1
            new Inst("wb_c1", "bishop",       FileX(2), RankZ(1), "piece_white"),  // c1 (dark-square bishop, unmoved)
            new Inst("wq_d1", "queen",        FileX(3), RankZ(1), "piece_white"),  // d1
            new Inst("wk_e1", "king",         FileX(4), RankZ(1), "piece_white"),  // e1
            new Inst("wr_h1", "rook",         FileX(7), RankZ(1), "piece_white"),  // h1

            // ── White developed pieces ────────────────────────────────────────────────
            new Inst("wb_c4", "bishop",       FileX(2), RankZ(4), "piece_white"),  // Bc4 (from f1)
            new Inst("wn_f3", "knight_white", FileX(5), RankZ(3), "piece_white"),  // Nf3 (from g1)

            // ── White pawns on rank 2, except e-pawn advanced to e4 ──────────────────
            new Inst("wp_a2", "pawn",         FileX(0), RankZ(2), "piece_white"),
            new Inst("wp_b2", "pawn",         FileX(1), RankZ(2), "piece_white"),
            new Inst("wp_c2", "pawn",         FileX(2), RankZ(2), "piece_white"),
            new Inst("wp_d2", "pawn",         FileX(3), RankZ(2), "piece_white"),
            new Inst("wp_f2", "pawn",         FileX(5), RankZ(2), "piece_white"),
            new Inst("wp_g2", "pawn",         FileX(6), RankZ(2), "piece_white"),
            new Inst("wp_h2", "pawn",         FileX(7), RankZ(2), "piece_white"),
            new Inst("wp_e4", "pawn",         FileX(4), RankZ(4), "piece_white"),  // e4

            // ── Black back rank (untouched): Ra8 Bc8 Qd8 Ke8 Ng8 Rh8 ─────────────
            new Inst("br_a8", "rook",         FileX(0), RankZ(8), "piece_black"),  // a8
            new Inst("bb_c8", "bishop",       FileX(2), RankZ(8), "piece_black"),  // c8 (dark-square bishop, unmoved)
            new Inst("bq_d8", "queen",        FileX(3), RankZ(8), "piece_black"),  // d8
            new Inst("bk_e8", "king",         FileX(4), RankZ(8), "piece_black"),  // e8
            new Inst("bn_g8", "knight_black", FileX(6), RankZ(8), "piece_black"),  // g8 (unmoved)
            new Inst("br_h8", "rook",         FileX(7), RankZ(8), "piece_black"),  // h8

            // ── Black developed pieces ────────────────────────────────────────────────
            new Inst("bn_c6", "knight_black", FileX(2), RankZ(6), "piece_black"),  // Nc6 (from b8)
            new Inst("bb_c5", "bishop",       FileX(2), RankZ(5), "piece_black"),  // Bc5 (from f8)

            // ── Black pawns on rank 7, except e-pawn advanced to e5 ──────────────────
            new Inst("bp_a7", "pawn",         FileX(0), RankZ(7), "piece_black"),
            new Inst("bp_b7", "pawn",         FileX(1), RankZ(7), "piece_black"),
            new Inst("bp_c7", "pawn",         FileX(2), RankZ(7), "piece_black"),
            new Inst("bp_d7", "pawn",         FileX(3), RankZ(7), "piece_black"),
            new Inst("bp_f7", "pawn",         FileX(5), RankZ(7), "piece_black"),
            new Inst("bp_g7", "pawn",         FileX(6), RankZ(7), "piece_black"),
            new Inst("bp_h7", "pawn",         FileX(7), RankZ(7), "piece_black"),
            new Inst("bp_e5", "pawn",         FileX(4), RankZ(5), "piece_black"),  // e5
        };

        static void EmitEntities(StringBuilder sb)
        {
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("#  ENTITIES — Scacchiera + istanze pezzi (white/black)");
            sb.AppendLine("# ═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("entities:");
            sb.AppendLine("  - name: \"chessboard_main\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine("    scale: [8.000, 0.100, 8.000]");
            sb.AppendLine("    translate: [0.000, -0.050, 0.000]");
            sb.AppendLine("    material: \"board_checker\"");
            sb.AppendLine();
            sb.AppendLine("  - name: \"border\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine("    scale: [8.600, 0.160, 8.600]");
            sb.AppendLine("    translate: [0.000, -0.090, 0.000]");
            sb.AppendLine("    material: \"board_border\"");
            sb.AppendLine();

            foreach (var p in Pieces)
            {
                sb.AppendLine($"  - name: \"{p.Name}\"");
                sb.AppendLine("    type: \"instance\"");
                sb.AppendLine($"    template: \"{p.Template}\"");
                sb.AppendLine($"    translate: {Vec3(p.X, 0.0f, p.Z)}");
                sb.AppendLine($"    material: \"{p.Mat}\"");
                sb.AppendLine();
            }
        }
    }
}
