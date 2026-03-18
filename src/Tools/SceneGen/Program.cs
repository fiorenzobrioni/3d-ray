using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace SceneGen
{
    class Program
    {
        static void Main(string[] args)
        {
            // --- Determination of the filename ---
            string scenesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "scenes");
            if (!Directory.Exists(scenesDir))
            {
                // Fallback for execution from different contexts
                scenesDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "scenes"));
                if (!Directory.Exists(scenesDir))
                {
                    scenesDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "scenes"));
                }
            }

            int index = 1;
            string fileName;
            do
            {
                fileName = $"random-world-{index:D3}.yaml";
                index++;
            } while (File.Exists(Path.Combine(scenesDir, fileName)));

            string outPath = Path.Combine(scenesDir, fileName);

            // --- Scene Generation ---
            var sb = new StringBuilder();
            sb.AppendLine("# Procedurally generated random world");
            sb.AppendLine();

            // World & Camera
            sb.AppendLine("world:");
            sb.AppendLine("  ambient_light: [0.05, 0.05, 0.08]");
            sb.AppendLine("  background: [0.4, 0.6, 1.0]");
            sb.AppendLine("  ground:");
            sb.AppendLine("    type: \"infinite_plane\"");
            sb.AppendLine("    material: \"checker_floor\"");
            sb.AppendLine("    y: -0.5");
            sb.AppendLine();

            sb.AppendLine("camera:");
            sb.AppendLine("  position: [13.0, 2.0, 3.0]");
            sb.AppendLine("  look_at: [0.0, 0.0, 0.0]");
            sb.AppendLine("  fov: 20");
            sb.AppendLine("  aperture: 0.1");
            sb.AppendLine("  focal_dist: 10.0");
            sb.AppendLine();

            // Materials
            sb.AppendLine("materials:");
            sb.AppendLine("  - id: \"checker_floor\"");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    texture:");
            sb.AppendLine("      type: \"checker\"");
            sb.AppendLine("      scale: 2.0");
            sb.AppendLine("      colors:");
            sb.AppendLine("        - [0.15, 0.15, 0.15]");
            sb.AppendLine("        - [0.85, 0.85, 0.85]");
            sb.AppendLine();
            sb.AppendLine("  - id: \"glass\"");
            sb.AppendLine("    type: \"dielectric\"");
            sb.AppendLine("    refraction_index: 1.5");
            sb.AppendLine();
            sb.AppendLine("  - id: \"metal_gold\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.85, 0.65, 0.2]");
            sb.AppendLine("    fuzz: 0.05");
            sb.AppendLine();
            sb.AppendLine("  - id: \"metal_silver\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.9, 0.9, 0.95]");
            sb.AppendLine("    fuzz: 0.1");
            sb.AppendLine();

            // Lambertian Colors
            var lambertianMaterials = new[] {
                ("matte_red", new[] { 0.8, 0.15, 0.1 }),
                ("matte_green", new[] { 0.3, 0.6, 0.2 }),
                ("matte_blue", new[] { 0.1, 0.2, 0.8 }),
                ("matte_yellow", new[] { 0.8, 0.8, 0.0 }),
                ("matte_purple", new[] { 0.5, 0.0, 0.5 }),
                ("matte_orange", new[] { 0.9, 0.5, 0.1 })
            };

            foreach (var (id, color) in lambertianMaterials)
            {
                sb.AppendLine($"  - id: \"{id}\"");
                sb.AppendLine("    type: \"lambertian\"");
                sb.AppendLine($"    color: [{VFmt(color[0])}, {VFmt(color[1])}, {VFmt(color[2])}]");
                sb.AppendLine();
            }

            // Lights
            sb.AppendLine("lights:");
            sb.AppendLine("  - type: \"directional\"");
            sb.AppendLine("    direction: [-1, -1, -0.5]");
            sb.AppendLine("    color: [1.0, 1.0, 1.0]");
            sb.AppendLine("    intensity: 1.0");
            sb.AppendLine();
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [10, 15, 10]");
            sb.AppendLine("    color: [1.0, 1.0, 1.0]");
            sb.AppendLine("    intensity: 250.0");
            sb.AppendLine();

            // Random Spheres
            sb.AppendLine("entities:");
            var rand = new Random();
            var materialIds = new List<string> { "glass", "metal_gold", "metal_silver" };
            materialIds.AddRange(lambertianMaterials.Select(m => m.Item1));

            for (int i = 0; i < 100; i++)
            {
                float radius = 0.2f + (float)rand.NextDouble() * 0.5f;
                float x = -15.0f + (float)rand.NextDouble() * 30.0f;
                float z = -15.0f + (float)rand.NextDouble() * 30.0f;
                float y = -0.5f + radius;

                string mat = materialIds[rand.Next(materialIds.Count)];

                sb.AppendLine($"  - name: \"sphere_{i:D3}\"");
                sb.AppendLine("    type: \"sphere\"");
                sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                sb.AppendLine($"    radius: {VFmt(radius)}");
                sb.AppendLine($"    material: \"{mat}\"");
            }

            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Generated scene: {fileName}");
            Console.WriteLine($"Path: {outPath}");
        }

        static string VFmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
