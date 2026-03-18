using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PrimitivesGen
{
    class Program
    {
        static void Main(string[] args)
        {
            // --- Determination of the filename ---
            string scenesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "scenes");
            if (!Directory.Exists(scenesDir))
            {
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
                fileName = $"random-primitives-{index:D3}.yaml";
                index++;
            } while (File.Exists(Path.Combine(scenesDir, fileName)));

            string outPath = Path.Combine(scenesDir, fileName);

            // --- Scene Generation ---
            var sb = new StringBuilder();
            sb.AppendLine("# Procedurally generated scene with all supported primitives");
            sb.AppendLine();

            // World & Camera
            sb.AppendLine("world:");
            sb.AppendLine("  ambient_light: [0.05, 0.05, 0.08]");
            sb.AppendLine("  background: [0.1, 0.1, 0.2]");
            sb.AppendLine();

            sb.AppendLine("camera:");
            sb.AppendLine("  position: [15.0, 10.0, -15.0]");
            sb.AppendLine("  look_at: [0.0, 2.0, 0.0]");
            sb.AppendLine("  fov: 40");
            sb.AppendLine();

            // Materials
            sb.AppendLine("materials:");
            var materials = new List<string> { "glass", "mirror", "gold", "matte_red", "matte_blue", "checker_tex", "marbled", "wooden", "noisy" };
            
            sb.AppendLine("  - id: \"glass\"\n    type: \"dielectric\"\n    refraction_index: 1.5\n");
            sb.AppendLine("  - id: \"mirror\"\n    type: \"metal\"\n    color: [0.9, 0.9, 0.9]\n    fuzz: 0.0\n");
            sb.AppendLine("  - id: \"gold\"\n    type: \"metal\"\n    color: [0.85, 0.65, 0.2]\n    fuzz: 0.1\n");
            sb.AppendLine("  - id: \"matte_red\"\n    type: \"lambertian\"\n    color: [0.8, 0.1, 0.1]\n");
            sb.AppendLine("  - id: \"matte_blue\"\n    type: \"lambertian\"\n    color: [0.1, 0.1, 0.8]\n");
            
            sb.AppendLine("  - id: \"checker_tex\"\n    type: \"lambertian\"\n    texture:\n      type: \"checker\"\n      scale: 1.0\n      colors: [[0.5, 0.5, 0.5], [0.1, 0.1, 0.1]]\n");
            sb.AppendLine("  - id: \"marbled\"\n    type: \"lambertian\"\n    texture:\n      type: \"marble\"\n      scale: 5.0\n      noise_strength: 10.0\n      colors: [[0.9, 0.9, 0.9], [0.1, 0.1, 0.3]]\n");
            sb.AppendLine("  - id: \"wooden\"\n    type: \"lambertian\"\n    texture:\n      type: \"wood\"\n      scale: 8.0\n      noise_strength: 3.5\n      colors: [[0.4, 0.25, 0.1], [0.15, 0.08, 0.03]]\n");
            sb.AppendLine("  - id: \"noisy\"\n    type: \"lambertian\"\n    texture:\n      type: \"noise\"\n      scale: 15.0\n");

            // Lights
            sb.AppendLine("lights:");
            sb.AppendLine("  - type: \"directional\"\n    direction: [-1, -1, 1]\n    color: [1, 1, 1]\n    intensity: 0.8\n");
            sb.AppendLine("  - type: \"point\"\n    position: [10, 20, -10]\n    color: [1, 1, 1]\n    intensity: 200\n");

            // Entities
            sb.AppendLine("entities:");
            var rand = new Random();
            
            string[] primitiveTypes = { "sphere", "box", "cylinder", "disk", "quad", "triangle", "infinite_plane" };

            for (int i = 0; i < 50; i++)
            {
                string type = primitiveTypes[rand.Next(primitiveTypes.Length)];
                string mat = materials[rand.Next(materials.Count)];
                string name = $"{type}_{i:D3}";

                sb.AppendLine($"  - name: \"{name}\"");
                sb.AppendLine($"    type: \"{type}\"");
                sb.AppendLine($"    material: \"{mat}\"");

                float x = (float)(rand.NextDouble() * 10.0 - 5.0);
                float y = (float)(rand.NextDouble() * 10.0 - 0.0);
                float z = (float)(rand.NextDouble() * 10.0 - 5.0);

                switch (type)
                {
                    case "sphere":
                        sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    radius: {VFmt(rand.NextDouble() * 1.5 + 0.2)}");
                        break;

                    case "box":
                        sb.AppendLine($"    translate: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    scale: [{VFmt(rand.NextDouble() * 2 + 0.5)}, {VFmt(rand.NextDouble() * 2 + 0.5)}, {VFmt(rand.NextDouble() * 2 + 0.5)}]");
                        sb.AppendLine($"    rotate: [{VFmt(rand.NextDouble() * 360)}, {VFmt(rand.NextDouble() * 360)}, {VFmt(rand.NextDouble() * 360)}]");
                        break;

                    case "cylinder":
                        sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    radius: {VFmt(rand.NextDouble() * 1.0 + 0.2)}");
                        sb.AppendLine($"    height: {VFmt(rand.NextDouble() * 2.5 + 0.5)}");
                        break;

                    case "disk":
                        sb.AppendLine($"    center: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    normal: [{VFmt(rand.NextDouble() * 2 - 1)}, {VFmt(rand.NextDouble() * 2 - 1)}, {VFmt(rand.NextDouble() * 2 - 1)}]");
                        sb.AppendLine($"    radius: {VFmt(rand.NextDouble() * 1.5 + 0.5)}");
                        break;

                    case "quad":
                        sb.AppendLine($"    q: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    u: [{VFmt(rand.NextDouble() * 3 - 1.5)}, {VFmt(rand.NextDouble() * 3 - 1.5)}, {VFmt(rand.NextDouble() * 3 - 1.5)}]");
                        sb.AppendLine($"    v: [{VFmt(rand.NextDouble() * 3 - 1.5)}, {VFmt(rand.NextDouble() * 3 - 1.5)}, {VFmt(rand.NextDouble() * 3 - 1.5)}]");
                        break;

                    case "triangle":
                        sb.AppendLine($"    v0: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    v1: [{VFmt(x + rand.NextDouble() * 3 - 1.5)}, {VFmt(y + rand.NextDouble() * 3 - 1.5)}, {VFmt(z + rand.NextDouble() * 3 - 1.5)}]");
                        sb.AppendLine($"    v2: [{VFmt(x + rand.NextDouble() * 3 - 1.5)}, {VFmt(y + rand.NextDouble() * 3 - 1.5)}, {VFmt(z + rand.NextDouble() * 3 - 1.5)}]");
                        break;

                    case "infinite_plane":
                        // Ground plane is already handled usually, but let's add vertical or slanted ones
                        sb.AppendLine($"    point: [{VFmt(x)}, {VFmt(y)}, {VFmt(z)}]");
                        sb.AppendLine($"    normal: [{VFmt(rand.NextDouble() * 2 - 1)}, {VFmt(rand.NextDouble() * 2 - 1)}, {VFmt(rand.NextDouble() * 2 - 1)}]");
                        break;
                }
                sb.AppendLine();
            }

            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Generated scene: {fileName}");
            Console.WriteLine($"Path: {outPath}");
        }

        static string VFmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
