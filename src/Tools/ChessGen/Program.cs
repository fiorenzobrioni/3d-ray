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
            sb.AppendLine("  ambient_light: [0.05, 0.05, 0.08]");
            sb.AppendLine("  background: [0.1, 0.1, 0.15]");
            sb.AppendLine("  ground:");
            sb.AppendLine("    type: \"infinite_plane\"");
            sb.AppendLine("    material: \"table_wood\"");
            sb.AppendLine("    y: -0.2");
            sb.AppendLine();
            sb.AppendLine("camera:");
            sb.AppendLine("  position: [-5, 6, -6]");
            sb.AppendLine("  look_at: [0, 0, 0]");
            sb.AppendLine("  fov: 50");
            sb.AppendLine("  aperture: 0.15");
            sb.AppendLine("  focal_dist: 8.0");
            sb.AppendLine();
            sb.AppendLine("materials:");
            sb.AppendLine("  - id: \"table_wood\"");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    color: [0.4, 0.25, 0.15]");
            sb.AppendLine();
            sb.AppendLine("  - id: \"board_light\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.9, 0.85, 0.75]");
            sb.AppendLine("    fuzz: 0.05");
            sb.AppendLine();
            sb.AppendLine("  - id: \"board_dark\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.15, 0.15, 0.15]");
            sb.AppendLine("    fuzz: 0.05");
            sb.AppendLine();
            sb.AppendLine("  - id: \"board_border\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.3, 0.15, 0.05]");
            sb.AppendLine("    fuzz: 0.1");
            sb.AppendLine();
            sb.AppendLine("  - id: \"piece_white\"");
            sb.AppendLine("    type: \"dielectric\"");
            sb.AppendLine("    refraction_index: 1.5");
            sb.AppendLine();
            sb.AppendLine("  - id: \"piece_black\"");
            sb.AppendLine("    type: \"dielectric\"");
            sb.AppendLine("    refraction_index: 2.0"); // heavy crystal look for black
            sb.AppendLine();
            sb.AppendLine("  # Alternative set of materials for pieces in case dielectric is too noisy:");
            sb.AppendLine("  - id: \"piece_white_solid\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.9, 0.9, 0.9]");
            sb.AppendLine("    fuzz: 0.1");
            sb.AppendLine();
            sb.AppendLine("  - id: \"piece_black_solid\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.1, 0.1, 0.1]");
            sb.AppendLine("    fuzz: 0.1");
            sb.AppendLine();
            sb.AppendLine("lights:");
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [-2, 8, -2]");
            sb.AppendLine("    color: [1.0, 0.95, 0.9]");
            sb.AppendLine("    intensity: 80.0");
            sb.AppendLine();
            sb.AppendLine("  - type: \"directional\"");
            sb.AppendLine("    direction: [0.5, -1, 0.5]");
            sb.AppendLine("    color: [0.5, 0.6, 0.8]");
            sb.AppendLine("    intensity: 0.3");
            sb.AppendLine();
            sb.AppendLine("entities:");

            // Generate Board
            float tileSize = 1.0f;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    float x = (c - 3.5f) * tileSize;
                    float z = (r - 3.5f) * tileSize;
                    string mat = ((r + c) % 2 == 1) ? "board_light" : "board_dark";
                    
                    sb.AppendLine($"  - name: \"tile_{r}_{c}\"");
                    sb.AppendLine("    type: \"box\"");
                    sb.AppendLine($"    min: [{VFmt(x - 0.49f)}, -0.100, {VFmt(z - 0.49f)}]");
                    sb.AppendLine($"    max: [{VFmt(x + 0.49f)}, 0.000, {VFmt(z + 0.49f)}]");
                    sb.AppendLine($"    material: \"{mat}\"");
                }
            }

            // Board border
            sb.AppendLine("  - name: \"border\"");
            sb.AppendLine("    type: \"box\"");
            sb.AppendLine("    min: [-4.2, -0.15, -4.2]");
            sb.AppendLine("    max: [4.2, -0.01, 4.2]");
            sb.AppendLine("    material: \"board_border\"");

            // Generate Pieces
            string matWhite = "piece_white_solid"; // Change to "piece_white" for glass
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
            sb.AppendLine($"    min: [{VFmt(x - dx)}, {VFmt(y - dy)}, {VFmt(z - dz)}]");
            sb.AppendLine($"    max: [{VFmt(x + dx)}, {VFmt(y + dy)}, {VFmt(z + dz)}]");
            sb.AppendLine($"    material: \"{mat}\"");
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
