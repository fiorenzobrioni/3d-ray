using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;

class Program
{
    static void Main()
    {
        string textChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        using var fontFamily = new FontFamily("Times New Roman"); 
        var yaml = new StringWriter();
        yaml.WriteLine("# ============================================================================");
        yaml.WriteLine("# Libreria Oggetti — Font 3D (Alfabeto e Numeri)");
        yaml.WriteLine("# ============================================================================");
        yaml.WriteLine("#");
        yaml.WriteLine("# Ogni carattere è un template centrato nell'origine in XZ, estruso lungo Y.");
        yaml.WriteLine("# Altezza default: 0.15. Materiale di default (font_material) da sovrascrivere nella scena.");
        yaml.WriteLine("#");
        yaml.WriteLine("templates:");

        foreach (char c in textChars)
        {
            using var path = new GraphicsPath();
            path.AddString(c.ToString(), fontFamily, (int)FontStyle.Regular, 100f, new PointF(0, 0), StringFormat.GenericDefault);

            if (path.PointCount == 0) continue;

            path.Flatten(new Matrix(), 0.1f); 

            var subpaths = new List<List<PointF>>();
            List<PointF> currentPath = null;
            for (int i = 0; i < path.PointCount; i++)
            {
                byte type = path.PathTypes[i];
                var pt = path.PathPoints[i];
                
                int pointType = type & 0x07; 
                bool isClose = (type & 0x80) == 0x80; 

                if (pointType == 0) 
                {
                    if (currentPath != null && currentPath.Count > 2)
                        subpaths.Add(currentPath);
                    currentPath = new List<PointF>();
                }
                
                if (currentPath != null)
                {
                    if (currentPath.Count == 0 || Math.Abs(currentPath.Last().X - pt.X) > 0.001f || Math.Abs(currentPath.Last().Y - pt.Y) > 0.001f)
                    {
                        currentPath.Add(pt);
                    }
                }
                
                if (isClose)
                {
                    if (currentPath != null && currentPath.Count > 2)
                        subpaths.Add(currentPath);
                    currentPath = null;
                }
            }
            if (currentPath != null && currentPath.Count > 2)
                subpaths.Add(currentPath);

            subpaths = subpaths.Where(sp => sp.Count > 2).ToList();
            if (subpaths.Count == 0) continue;

            float minX = subpaths.SelectMany(p => p).Min(p => p.X);
            float maxX = subpaths.SelectMany(p => p).Max(p => p.X);
            float minY = subpaths.SelectMany(p => p).Min(p => p.Y);
            float maxY = subpaths.SelectMany(p => p).Max(p => p.Y);
            
            float cx = (minX + maxX) / 2f;
            float cy = (minY + maxY) / 2f;
            float height = maxY - minY;
            if (height == 0) height = 1;

            var normLoops = subpaths.Select(sp => {
                var pts = sp.Select(pt => new PointF((pt.X - cx) / height, -(pt.Y - cy) / height)).ToList();
                if (pts.Count > 1 && Math.Abs(pts.First().X - pts.Last().X) < 0.001f && Math.Abs(pts.First().Y - pts.Last().Y) < 0.001f)
                {
                    pts.RemoveAt(pts.Count - 1);
                }
                return new {
                    Points = pts,
                    Area = SignedArea(pts)
                };
            }).Where(l => Math.Abs(l.Area) > 0.001f).OrderByDescending(l => Math.Abs(l.Area)).ToList();

            if (normLoops.Count == 0) continue;

            bool outerIsPositive = normLoops[0].Area > 0;

            var outers = normLoops.Where(l => (l.Area > 0) == outerIsPositive).ToList();
            var inners = normLoops.Where(l => (l.Area > 0) != outerIsPositive).ToList();

            string suffix = char.IsUpper(c) ? "_maiusc" : (char.IsLower(c) ? "_minusc" : "");
            string tplName = "lettera_" + c.ToString().ToLower() + suffix;
            if (char.IsDigit(c)) tplName = "numero_" + c;

            yaml.WriteLine($"  - name: \"{tplName}\"");
            yaml.WriteLine($"    children:");

            if (inners.Count == 0)
            {
                foreach (var o in outers)
                    WriteExtrusion(yaml, o.Points, 6, false, true);
            }
            else
            {
                yaml.WriteLine("      - type: \"csg\"");
                yaml.WriteLine("        operation: \"subtraction\"");
                yaml.WriteLine("        left:");
                WriteUnionOrSingle(yaml, outers.Cast<dynamic>().ToList(), 10, false);
                yaml.WriteLine("        right:");
                WriteUnionOrSingle(yaml, inners.Cast<dynamic>().ToList(), 10, true);
            }
            yaml.WriteLine("");
        }

        File.WriteAllText("font.yaml", yaml.ToString());
    }

    static void WriteUnionOrSingle(StringWriter yaml, List<dynamic> loops, int indent, bool isHole)
    {
        string ind = new string(' ', indent);
        if (loops.Count == 1)
        {
            WriteExtrusion(yaml, loops[0].Points, indent, isHole, false);
        }
        else
        {
            yaml.WriteLine($"{ind}type: \"csg\"");
            yaml.WriteLine($"{ind}operation: \"union\"");
            yaml.WriteLine($"{ind}left:");
            WriteExtrusion(yaml, loops[0].Points, indent + 2, isHole, false);
            yaml.WriteLine($"{ind}right:");
            var rest = loops.Skip(1).ToList();
            if (rest.Count == 1)
                WriteExtrusion(yaml, rest[0].Points, indent + 2, isHole, false);
            else
                WriteUnionOrSingle(yaml, rest, indent + 2, isHole);
        }
    }

    static void WriteExtrusion(StringWriter yaml, List<PointF> pts, int indent, bool isHole, bool listElement)
    {
        string ind = new string(' ', indent);
        string prefix = listElement ? "- " : "  ";
        yaml.WriteLine($"{ind.Substring(0, Math.Max(0, ind.Length - 2))}{prefix}type: \"extrusion\"");
        yaml.WriteLine($"{ind}profile_type: \"linear\"");
        
        if (isHole) 
        {
            // Fai il buco più lungo e traslato per perforare senza artefatti di Z-fighting
            yaml.WriteLine($"{ind}height: 0.17");
            yaml.WriteLine($"{ind}translate: [0, -0.01, 0]");
        } 
        else 
        {
            yaml.WriteLine($"{ind}height: 0.15");
        }
        
        yaml.WriteLine($"{ind}caps: \"both\"");
        yaml.WriteLine($"{ind}material: \"font_material\"");
        yaml.WriteLine($"{ind}profile:");
        foreach (var p in pts)
        {
            yaml.WriteLine($"{ind}  - [{p.X.ToString("0.000", CultureInfo.InvariantCulture),7}, {p.Y.ToString("0.000", CultureInfo.InvariantCulture),7}]");
        }
    }

    static float SignedArea(List<PointF> pts)
    {
        float area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p1 = pts[i];
            var p2 = pts[(i + 1) % pts.Count];
            area += (p1.X * p2.Y - p2.X * p1.Y);
        }
        return area / 2f;
    }
}
