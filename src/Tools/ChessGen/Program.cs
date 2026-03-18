using System;
using System.IO;
using System.Text;
using System.Globalization;

namespace ChessGen
{
    class Program
    {
        static void Main(string[] args)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# Ray-Tracing Scene - Highly Detailed Chess Set");
            sb.AppendLine("# Procedurally generated using fine primitives (spheres, cylinders, boxes)");
            sb.AppendLine();
            sb.AppendLine("world:");
            sb.AppendLine("  # --- Configurazione Corrente: Studio Buio ---");
            sb.AppendLine("  ambient_light: [0.0, 0.0, 0.0]");
            sb.AppendLine("  background: [0.0, 0.0, 0.0]");
            sb.AppendLine("  ground:");
            sb.AppendLine("    type: \"infinite_plane\"");
            sb.AppendLine("    material: \"table_wood\"");
            sb.AppendLine("    y: -0.2");
            sb.AppendLine();
            sb.AppendLine("  # --- AMBIENTE: STUDIO BILANCIATO (Luce Morbida e Diffusa) ---");
            sb.AppendLine("  # ambient_light: [0.05, 0.05, 0.05]  # Luce di riempimento globale (fill light)");
            sb.AppendLine("  # background: [0.15, 0.15, 0.2]      # Colore del cielo/orizzonte");
            sb.AppendLine();
            sb.AppendLine("  # --- AMBIENTE: MIDNIGHT BLUE (Atmosfera Notturna) ---");
            sb.AppendLine("  # ambient_light: [0.01, 0.01, 0.02]");
            sb.AppendLine("  # background: [0.02, 0.02, 0.05]");
            sb.AppendLine();
            sb.AppendLine("  # --- AMBIENTE: WARM SUNSET (Luce Calda di Taglio) ---");
            sb.AppendLine("  # ambient_light: [0.1, 0.05, 0.02]");
            sb.AppendLine("  # background: [0.8, 0.4, 0.1]");
            sb.AppendLine();
            sb.AppendLine("  # --- AMBIENTE: HIGH-KEY STUDIO (Bianco Pulito) ---");
            sb.AppendLine("  # ambient_light: [0.2, 0.2, 0.2]");
            sb.AppendLine("  # background: [0.9, 0.9, 0.9]");
            sb.AppendLine();
            sb.AppendLine("  # --- AMBIENTE: NOIR / THE VOID (Nero Assoluto) ---");
            sb.AppendLine("  # ambient_light: [0.0, 0.0, 0.0]");
            sb.AppendLine("  # background: [0.0, 0.0, 0.0]");
            sb.AppendLine();
            sb.AppendLine("camera:");
            sb.AppendLine("  # --- Camera Principale (Prospettiva Classica) ---");
            sb.AppendLine("  position: [0, 5, -8]");
            sb.AppendLine("  look_at: [0, 0, 0]");
            sb.AppendLine("  fov: 45");
            sb.AppendLine("  aperture: 0.1");
            sb.AppendLine("  focal_dist: 9.43         # sqrt(0^2 + 5^2 + (-8)^2) = 9.43");
            sb.AppendLine();
            sb.AppendLine("  # --- VISTA MACRO (Dettaglio sui Pezzi Centrali) ---");
            sb.AppendLine("  # position: [1.5, 1.2, -4]");
            sb.AppendLine("  # look_at: [0, 0.8, 0]");
            sb.AppendLine("  # fov: 25.0");
            sb.AppendLine("  # aperture: 0.2");
            sb.AppendLine("  # focal_dist: 4.25");
            sb.AppendLine();
            sb.AppendLine("  # --- VISTA HERO (Angolo Basso e Imponente) ---");
            sb.AppendLine("  # position: [0.0, 0.5, -6.5]");
            sb.AppendLine("  # look_at: [0.0, 1.0, 0.5]");
            sb.AppendLine("  # fov: 55.0");
            sb.AppendLine("  # aperture: 0.1");
            sb.AppendLine("  # focal_dist: 7.0");
            sb.AppendLine();
            sb.AppendLine("  # --- VISTA ZENITALE (Vista dall'alto tattica) ---");
            sb.AppendLine("  # position: [0.0, 12, 0.01]");
            sb.AppendLine("  # look_at: [0, 0, 0]");
            sb.AppendLine("  # fov: 35");
            sb.AppendLine("  # aperture: 0.0");
            sb.AppendLine("  # focal_dist: 12.0");
            sb.AppendLine();
            sb.AppendLine("  # --- VISTA DUTCH ANGLE (Dinamica e Inclinata) ---");
            sb.AppendLine("  # position: [6.0, 4.0, -6.0]");
            sb.AppendLine("  # look_at: [0.0, 0.5, 0.0]");
            sb.AppendLine("  # fov: 40.0");
            sb.AppendLine("  # aperture: 0.1");
            sb.AppendLine("  # focal_dist: 8.5");
            sb.AppendLine();
            sb.AppendLine("  # --- VISTA PAWN'S EYE (Ad altezza pedina, Bokeh estremo) ---");
            sb.AppendLine("  # position: [0.0, 0.4, -3.5]");
            sb.AppendLine("  # look_at: [0.0, 0.6, 2.0]");
            sb.AppendLine("  # fov: 70.0");
            sb.AppendLine("  # aperture: 0.3");
            sb.AppendLine("  # focal_dist: 3.5");
            sb.AppendLine();
            sb.AppendLine("materials:");
            sb.AppendLine("  - id: \"table_wood\"");
            sb.AppendLine("    # --- Configurazione Corrente: Noce Satinato (Walnut) ---");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"wood\"");
            sb.AppendLine("      scale: 20.0                # Venature più fini e realistiche");
            sb.AppendLine("      noise_strength: 3.2");
            sb.AppendLine("      colors: [[0.25, 0.12, 0.05], [0.12, 0.06, 0.02]]");
            sb.AppendLine("      randomize_offset: true");
            sb.AppendLine("      rotation: [90, 0, 0]");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: LEGNO LACCATO LUCIDO (Polished Rosewood) ---");
            sb.AppendLine("    # type: \"metal\"              # Base metallica per riflessi speculari");
            sb.AppendLine("    # fuzz: 0.08                 # Riflesso tipico della lacca");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"wood\"");
            sb.AppendLine("    #   scale: 22.0");
            sb.AppendLine("    #   colors: [[0.35, 0.05, 0.02], [0.15, 0.02, 0.01]] # Toni rossastri profondi");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: QUERCIA CHIARA (Light Oak) ---");
            sb.AppendLine("    # type: \"lambertian\"");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"wood\"");
            sb.AppendLine("    #   scale: 15.0");
            sb.AppendLine("    #   colors: [[0.7, 0.5, 0.3], [0.5, 0.3, 0.15]]");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: EBANO OPACO (Matte Ebony) ---");
            sb.AppendLine("    # type: \"lambertian\"");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"wood\"");
            sb.AppendLine("    #   scale: 40.0              # Venature quasi impercettibili");
            sb.AppendLine("    #   colors: [[0.05, 0.05, 0.05], [0.01, 0.01, 0.01]]");
            sb.AppendLine();
            sb.AppendLine("  - id: \"board_checker\"");
            sb.AppendLine("    # --- Configurazione Corrente: Specchio Scaccato (Mirror Checker) ---");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    fuzz: 0.0");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"checker\"");
            sb.AppendLine("      scale: 0.125");
            sb.AppendLine("      colors: [[0.95, 0.95, 0.95], [0.05, 0.05, 0.05]]");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: MARMO CLASSICO (Carrara & Marquina) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # fuzz: 0.02");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"checker\"");
            sb.AppendLine("    #   scale: 0.125");
            sb.AppendLine("    #   # Ogni \"colore\" della scacchiera può essere a sua volta una texture (non ancora supportato nestato, usiamo colori solidi approssimati o marble texture esterna)");
            sb.AppendLine("    #   colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.1]]");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: LEGNO CLASSICO (Acero & Noce) ---");
            sb.AppendLine("    # type: \"lambertian\"");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"checker\"");
            sb.AppendLine("    #   scale: 0.125");
            sb.AppendLine("    #   colors: [[0.8, 0.7, 0.5], [0.2, 0.1, 0.05]]");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: OSSIDIANA (Deep Obsidian) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # fuzz: 0.0");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"checker\"");
            sb.AppendLine("    #   scale: 0.125");
            sb.AppendLine("    #   colors: [[0.03, 0.03, 0.05], [0.01, 0.01, 0.02]]");
            sb.AppendLine();
            sb.AppendLine("  - id: \"board_border\"");
            sb.AppendLine("    # --- Configurazione Corrente: Metallo Brunito (Gunmetal) ---");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.15, 0.15, 0.18]");
            sb.AppendLine("    fuzz: 0.12");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: EBANO PREGIATO (Dark Wood) ---");
            sb.AppendLine("    # type: \"lambertian\"");
            sb.AppendLine("    # texture:");
            sb.AppendLine("    #   type: \"wood\"");
            sb.AppendLine("    #   scale: 40.0");
            sb.AppendLine("    #   colors: [[0.1, 0.08, 0.05], [0.02, 0.01, 0.01]]");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: ORO SATINATO (Brushed Gold) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # color: [0.85, 0.65, 0.25]");
            sb.AppendLine("    # fuzz: 0.1");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: ALLUMINIO SPAZZOLATO (Brushed Aluminum) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # color: [0.7, 0.7, 0.75]");
            sb.AppendLine("    # fuzz: 0.2");
            sb.AppendLine();
            sb.AppendLine("  - id: \"piece_white_solid\"");
            sb.AppendLine("    # --- Configurazione Corrente: Marmo Bianco Lucido (Carrara) ---");
            sb.AppendLine("    type: \"metal\"                    ");
            sb.AppendLine("    fuzz: 0.04");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"marble\"");
            sb.AppendLine("      scale: 30.0");
            sb.AppendLine("      colors: [[0.98, 0.98, 0.98], [0.75, 0.75, 0.75]]");
            sb.AppendLine("      noise_strength: 12.0");
            sb.AppendLine("      randomize_offset: true");
            sb.AppendLine("      randomize_rotation: true");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: VETRO PURO (Clear Glass) ---");
            sb.AppendLine("    # type: \"dielectric\"");
            sb.AppendLine("    # refraction_index: 1.5");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: ARGENTO LUCIDO (Polished Silver) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # color: [0.95, 0.95, 0.95]");
            sb.AppendLine("    # fuzz: 0.02");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: PORCELLANA BIANCA (White Porcelain) ---");
            sb.AppendLine("    # type: \"lambertian\"");
            sb.AppendLine("    # color: [0.9, 0.9, 0.9]");
            sb.AppendLine();
            sb.AppendLine("  - id: \"piece_black_solid\"");
            sb.AppendLine("    # --- Configurazione Corrente: Marmo Nero Lucido (Nero Marquina) ---");
            sb.AppendLine("    type: \"metal\"                    ");
            sb.AppendLine("    fuzz: 0.08");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"marble\"");
            sb.AppendLine("      scale: 25.0");
            sb.AppendLine("      colors: [[0.02, 0.02, 0.03], [0.15, 0.15, 0.2]]");
            sb.AppendLine("      noise_strength: 20.0");
            sb.AppendLine("      randomize_offset: true");
            sb.AppendLine("      randomize_rotation: true");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: VETRO FUMÉ (Tinted Glass) ---");
            sb.AppendLine("    # type: \"dielectric\"");
            sb.AppendLine("    # refraction_index: 1.5");
            sb.AppendLine("    # color: [0.2, 0.2, 0.25] # Vetro scuro/bluastro");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: ORO PURO (Polished Gold) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # color: [1.0, 0.84, 0.0]");
            sb.AppendLine("    # fuzz: 0.01");
            sb.AppendLine();
            sb.AppendLine("    # --- VARIANTE: CERAMICA NERA (Black Polished Ceramic) ---");
            sb.AppendLine("    # type: \"metal\"");
            sb.AppendLine("    # color: [0.02, 0.02, 0.02]");
            sb.AppendLine("    # fuzz: 0.05");
            sb.AppendLine();
            sb.AppendLine("lights:");
            sb.AppendLine("  # --- Configurazione Corrente: Luce Singola (Luce Calda e Diffusa) ---");
            sb.AppendLine("  - { type: \"point\", position: [-2, 8, -2], color: [1.0, 0.95, 0.9], intensity: 30 }");
            sb.AppendLine();
            sb.AppendLine("  # --- SET: Balanced 3-Point Lighting (Luce Morbida e Diffusa) ---");
            sb.AppendLine("  # - { type: \"point\", position: [5, 10, -5], color: [1.0, 1.0, 1.0], intensity: 100 }  # Key Light");
            sb.AppendLine("  # - { type: \"point\", position: [-5, 5, -2], color: [0.8, 0.8, 1.0], intensity: 40 }   # Fill Light");
            sb.AppendLine("  # - { type: \"point\", position: [0, 8, 5], color: [1.0, 0.9, 0.7], intensity: 60 }     # Back Light");
            sb.AppendLine();
            sb.AppendLine("  # --- SET: DRAMATIC RIM (Solo contorno e silhouette) ---");
            sb.AppendLine("  # - { type: \"point\", position: [0, 12, 10], color: [1.0, 1.0, 1.0], intensity: 200 } # Rim Light");
            sb.AppendLine("  # - { type: \"point\", position: [-8, 2, -2], color: [0.2, 0.2, 0.3], intensity: 20 }  # Deep Fill");
            sb.AppendLine();
            sb.AppendLine("  # --- SET: WARM SUNSET (Atmosfera pomeridiana calda) ---");
            sb.AppendLine("  # - { type: \"directional\", direction: [-1, -0.3, -0.5], color: [1.0, 0.6, 0.2], intensity: 2.0 }");
            sb.AppendLine("  # - { type: \"point\", position: [5, 3, -5], color: [0.3, 0.3, 0.4], intensity: 15 }");
            sb.AppendLine();
            sb.AppendLine("  # --- SET: COLD MOONLIGHT (Notte fredda e ombre nette) ---");
            sb.AppendLine("  # - { type: \"directional\", direction: [0.5, -1.0, 0.5], color: [0.6, 0.7, 1.0], intensity: 0.8 }");
            sb.AppendLine("  # - { type: \"point\", position: [0, 5, 0], color: [0.1, 0.1, 0.2], intensity: 10 }");
            sb.AppendLine();
            sb.AppendLine("  # --- SET: HIGH-CONTRAST (Unica sorgente dura) ---");
            sb.AppendLine("  # - { type: \"point\", position: [-10, 8, -5], color: [1.0, 0.95, 0.9], intensity: 250 }");
            sb.AppendLine();
            sb.AppendLine("entities:");

            // Chessboard
            sb.AppendLine("  - name: \"chessboard_main\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine("    scale: [8.000, 0.100, 8.000]");
            sb.AppendLine("    translate: [0.000, -0.050, 0.000]");
            sb.AppendLine("    material: \"board_checker\"");

            // Board border
            sb.AppendLine("  - name: \"border\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine("    scale: [8.400, 0.140, 8.400]");
            sb.AppendLine("    translate: [0.000, -0.080, 0.000]");
            sb.AppendLine("    material: \"board_border\"");

            // Generate Pieces
            float tileSize = 1.0f;
            string matWhite = "piece_white_solid";
            string matBlack = "piece_black_solid";

            for (int c = 0; c < 8; c++)
            {
                float x = (c - 3.5f) * tileSize;
                
                // Pawns
                BuildPawn(sb, $"wp_{c}", x, -2.5f, matWhite);
                BuildPawn(sb, $"bp_{c}", x, 2.5f, matBlack);

                // Row 1 (White) & Row 8 (Black)
                if (c == 0 || c == 7)
                {
                    BuildRook(sb, $"wr_{c}", x, -3.5f, matWhite);
                    BuildRook(sb, $"br_{c}", x, 3.5f, matBlack);
                }
                else if (c == 1 || c == 6)
                {
                    BuildKnight(sb, $"wn_{c}", x, -3.5f, matWhite, 1);
                    BuildKnight(sb, $"bn_{c}", x, 3.5f, matBlack, -1);
                }
                else if (c == 2 || c == 5)
                {
                    BuildBishop(sb, $"wb_{c}", x, -3.5f, matWhite);
                    BuildBishop(sb, $"bb_{c}", x, 3.5f, matBlack);
                }
                else if (c == 3) // Queen
                {
                    BuildQueen(sb, $"wq", x, -3.5f, matWhite);
                    BuildQueen(sb, $"bq", x, 3.5f, matBlack);
                }
                else if (c == 4) // King
                {
                    BuildKing(sb, $"wk", x, -3.5f, matWhite);
                    BuildKing(sb, $"bk", x, 3.5f, matBlack);
                }
            }

            string outPath = @"..\..\..\scenes\chess.yaml";
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Chess scene generated with thousands of primitives at: {outPath}");
        }

        static string Invariant(string formattable)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}", formattable);
        }
        
        static string VFmt(float v) => string.Format(CultureInfo.InvariantCulture, "{0:0.000}", v);

        static void AddCyl(StringBuilder sb, string name, float x, float y, float z, float r, float h, string mat)
        {
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    type: \"cylinder\"");
            sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
            sb.AppendLine($"    radius: {VFmt(r)}");
            sb.AppendLine($"    height: {VFmt(h)}");
            sb.AppendLine($"    material: \"{mat}\"");
        }

        static void AddSph(StringBuilder sb, string name, float x, float y, float z, float r, string mat)
        {
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    type: \"sphere\"");
            sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
            sb.AppendLine($"    radius: {VFmt(r)}");
            sb.AppendLine($"    material: \"{mat}\"");
        }

        static void AddBox(StringBuilder sb, string name, float x, float y, float z, float dx, float dy, float dz, string mat)
        {
            sb.AppendLine($"  - name: \"{name}\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine($"    scale: [{VFmt(dx * 2)}, {VFmt(dy * 2)}, {VFmt(dz * 2)}]");
            sb.AppendLine($"    translate: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
            sb.AppendLine($"    material: \"{mat}\"");
            sb.AppendLine();
        }

        // --- Piece Builders ---

        static void BuildPawn(StringBuilder sb, string pfx, float x, float z, string mat)
        {
            AddCyl(sb, $"{pfx}_base1", x, 0.0f, z, 0.35f, 0.1f, mat);
            AddCyl(sb, $"{pfx}_base2", x, 0.1f, z, 0.25f, 0.05f, mat);
            AddCyl(sb, $"{pfx}_stem", x, 0.15f, z, 0.18f, 0.4f, mat);
            AddCyl(sb, $"{pfx}_neck", x, 0.55f, z, 0.22f, 0.05f, mat);
            AddSph(sb, $"{pfx}_head", x, 0.8f, z, 0.25f, mat);
        }

        static void BuildRook(StringBuilder sb, string pfx, float x, float z, string mat)
        {
            AddCyl(sb, $"{pfx}_base1", x, 0.0f, z, 0.4f, 0.15f, mat);
            AddCyl(sb, $"{pfx}_base2", x, 0.15f, z, 0.3f, 0.05f, mat);
            AddCyl(sb, $"{pfx}_stem", x, 0.2f, z, 0.25f, 0.6f, mat);
            AddCyl(sb, $"{pfx}_head_base", x, 0.8f, z, 0.35f, 0.2f, mat);
            
            // Crenellations
            AddBox(sb, $"{pfx}_cren1", x - 0.2f, 1.05f, z - 0.2f, 0.08f, 0.05f, 0.08f, mat);
            AddBox(sb, $"{pfx}_cren2", x + 0.2f, 1.05f, z - 0.2f, 0.08f, 0.05f, 0.08f, mat);
            AddBox(sb, $"{pfx}_cren3", x - 0.2f, 1.05f, z + 0.2f, 0.08f, 0.05f, 0.08f, mat);
            AddBox(sb, $"{pfx}_cren4", x + 0.2f, 1.05f, z + 0.2f, 0.08f, 0.05f, 0.08f, mat);
        }

        static void BuildKnight(StringBuilder sb, string pfx, float x, float z, string mat, int lookDir)
        {
            AddCyl(sb, $"{pfx}_base", x, 0.0f, z, 0.35f, 0.1f, mat);
            AddCyl(sb, $"{pfx}_base2", x, 0.1f, z, 0.25f, 0.05f, mat);
            AddCyl(sb, $"{pfx}_stem", x, 0.15f, z, 0.22f, 0.4f, mat);
            
            // Horse head constructed from spheres and boxes
            AddSph(sb, $"{pfx}_chest", x, 0.65f, z, 0.25f, mat);
            AddSph(sb, $"{pfx}_head", x, 0.9f, z + lookDir*0.1f, 0.22f, mat);
            AddBox(sb, $"{pfx}_snout", x, 0.85f, z + lookDir*0.25f, 0.15f, 0.1f, 0.18f, mat);
            
            // Ears
            AddSph(sb, $"{pfx}_ear_L", x - 0.1f, 1.05f, z - lookDir*0.05f, 0.06f, mat);
            AddSph(sb, $"{pfx}_ear_R", x + 0.1f, 1.05f, z - lookDir*0.05f, 0.06f, mat);
        }

        static void BuildBishop(StringBuilder sb, string pfx, float x, float z, string mat)
        {
            AddCyl(sb, $"{pfx}_base", x, 0.0f, z, 0.35f, 0.1f, mat);
            AddCyl(sb, $"{pfx}_stem", x, 0.1f, z, 0.15f, 0.6f, mat);
            AddCyl(sb, $"{pfx}_neck", x, 0.7f, z, 0.22f, 0.05f, mat);
            
            // Mitre shape (stretched) by stacking slightly offset spheres
            AddSph(sb, $"{pfx}_head1", x, 0.9f, z, 0.22f, mat);
            AddSph(sb, $"{pfx}_head2", x, 1.05f, z, 0.15f, mat);
            AddSph(sb, $"{pfx}_top_ball", x, 1.25f, z, 0.05f, mat);
        }

        static void BuildQueen(StringBuilder sb, string pfx, float x, float z, string mat)
        {
            AddCyl(sb, $"{pfx}_base", x, 0.0f, z, 0.4f, 0.1f, mat);
            AddCyl(sb, $"{pfx}_base2", x, 0.1f, z, 0.3f, 0.05f, mat);
            AddCyl(sb, $"{pfx}_stem", x, 0.15f, z, 0.2f, 0.8f, mat);
            AddCyl(sb, $"{pfx}_neck", x, 0.95f, z, 0.3f, 0.05f, mat);
            AddSph(sb, $"{pfx}_head", x, 1.15f, z, 0.2f, mat);
            
            // Crown spheres
            for(int i=0; i<8; i++)
            {
                double ang = i * Math.PI / 4.0;
                float px = x + (float)Math.Cos(ang) * 0.2f;
                float pz = z + (float)Math.Sin(ang) * 0.2f;
                AddSph(sb, $"{pfx}_crown_{i}", px, 1.25f, pz, 0.05f, mat);
            }
        }

        static void BuildKing(StringBuilder sb, string pfx, float x, float z, string mat)
        {
            AddCyl(sb, $"{pfx}_base", x, 0.0f, z, 0.4f, 0.1f, mat);
            AddCyl(sb, $"{pfx}_base2", x, 0.1f, z, 0.3f, 0.08f, mat);
            AddCyl(sb, $"{pfx}_stem", x, 0.18f, z, 0.22f, 0.85f, mat);
            AddCyl(sb, $"{pfx}_neck", x, 1.03f, z, 0.35f, 0.05f, mat);
            AddSph(sb, $"{pfx}_head", x, 1.25f, z, 0.25f, mat);
            
            // Cross
            AddBox(sb, $"{pfx}_cross_v", x, 1.6f, z, 0.03f, 0.12f, 0.03f, mat);
            AddBox(sb, $"{pfx}_cross_h", x, 1.62f, z, 0.08f, 0.03f, 0.03f, mat);
        }
    }
}
