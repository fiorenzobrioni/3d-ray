using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TeapotGen
{
    class Program
    {
        static void Main(string[] args)
        {
            string objPath = "teapot.obj";
            string outPath = "../../../scenes/teapot.yaml";

            if (!File.Exists(objPath))
            {
                Console.WriteLine("teapot.obj not found.");
                return;
            }

            var vertices = new List<float[]>();
            var faces = new List<int[]>();

            foreach (var line in File.ReadAllLines(objPath))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                if (parts[0] == "v" && parts.Length >= 4)
                {
                    float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    vertices.Add(new float[] { x, y, z });
                }
                else if (parts[0] == "f" && parts.Length >= 4)
                {
                    // Basic triangulation for n-gons
                    var faceVerts = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var indexStr = parts[i].Split('/')[0];
                        if (int.TryParse(indexStr, out int idx))
                        {
                            // OBJ indices are 1-based, can be negative
                            if (idx > 0) faceVerts.Add(idx - 1);
                            else faceVerts.Add(vertices.Count + idx);
                        }
                    }

                    for (int i = 1; i < faceVerts.Count - 1; i++)
                    {
                        faces.Add(new int[] { faceVerts[0], faceVerts[i], faceVerts[i + 1] });
                    }
                }
            }

            Console.WriteLine($"Parsed {vertices.Count} vertices and {faces.Count} triangles.");

            var sb = new StringBuilder();
            sb.AppendLine("# Ray-Tracing Scene - Multi-polygon Teapot");
            sb.AppendLine("");
            sb.AppendLine("world:");
            sb.AppendLine("  ambient_light: [0.2, 0.2, 0.2]");
            sb.AppendLine("  background: [0.1, 0.1, 0.15]");
            sb.AppendLine("  ground:");
            sb.AppendLine("    type: \"infinite_plane\"");
            sb.AppendLine("    material: \"floor_grid\"");
            sb.AppendLine("    y: 0.0");
            sb.AppendLine("");
            sb.AppendLine("camera:");
            // Adjust camera position based on the teapot's scale. 
            // The standard Utah teapot has bounds roughly [-3, 3] in X/Z and [0, 3] in Y.
            sb.AppendLine("  position: [0, 4, 10]");
            sb.AppendLine("  look_at: [0, 1.5, 0]");
            sb.AppendLine("  fov: 40");
            sb.AppendLine("  aperture: 0.05");
            sb.AppendLine("  focal_dist: 10.5");
            sb.AppendLine("");
            sb.AppendLine("materials:");
            sb.AppendLine("  - id: \"floor_grid\"");
            sb.AppendLine("    type: \"lambertian\"");
            sb.AppendLine("    color: [0.8, 0.8, 0.8]");
            sb.AppendLine("");
            sb.AppendLine("  - id: \"ceramic_teapot\"");
            sb.AppendLine("    type: \"dielectric\"");
            sb.AppendLine("    refraction_index: 1.5");
            sb.AppendLine("    # For a realistic teapot, we can use dielectric to make it glass-like,");
            sb.AppendLine("    # or a nice metal.");
            sb.AppendLine("");
            sb.AppendLine("  - id: \"gold_teapot\"");
            sb.AppendLine("    type: \"metal\"");
            sb.AppendLine("    color: [0.9, 0.7, 0.1]");
            sb.AppendLine("    fuzz: 0.1");
            sb.AppendLine("");
            sb.AppendLine("lights:");
            sb.AppendLine("  - type: \"point\"");
            sb.AppendLine("    position: [5, 10, 5]");
            sb.AppendLine("    color: [1.0, 0.95, 0.9]");
            sb.AppendLine("    intensity: 100.0");
            sb.AppendLine("");
            sb.AppendLine("  - type: \"directional\"");
            sb.AppendLine("    direction: [-0.5, -1, -0.5]");
            sb.AppendLine("    color: [0.2, 0.2, 0.4]");
            sb.AppendLine("    intensity: 0.5");
            sb.AppendLine("");
            sb.AppendLine("entities:");
            
            // Output triangles
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                var v0 = vertices[f[0]];
                var v1 = vertices[f[1]];
                var v2 = vertices[f[2]];

                // Scale and offset if needed
                float scale = 0.5f;

                sb.AppendLine($"  - name: \"tri_{i}\"");
                sb.AppendLine("    type: \"triangle\"");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    v0: [{0:F4}, {1:F4}, {2:F4}]", v0[0]*scale, v0[1]*scale, v0[2]*scale));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    v1: [{0:F4}, {1:F4}, {2:F4}]", v1[0]*scale, v1[1]*scale, v1[2]*scale));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    v2: [{0:F4}, {1:F4}, {2:F4}]", v2[0]*scale, v2[1]*scale, v2[2]*scale));
                sb.AppendLine("    material: \"gold_teapot\"");
            }

            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Saved {outPath}");
        }
    }
}
