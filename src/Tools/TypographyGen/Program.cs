using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace TypographyGen;

// Generates 36 OBJ glyph files (A-Z, 0-9) under scenes/libraries/typography/glyphs/.
// Each glyph is a geometric sans-serif shape extruded from z=0 to z=-D.
// Cap-height is 1.0, baseline at y=0, left edge at x=0. Advance width is per-glyph.
//
// Curves are emitted as SmoothTriangle strips (with `vn` directives) so the
// RayTracer's ObjLoader (ObjLoader.cs:213-236) routes them to SmoothTriangle,
// giving visually smooth curves with ~32 segments per arc. Flat faces (stems,
// caps, diagonals) are emitted as flat triangles and will render correctly with
// the OBJ loader's per-face normal fallback.
internal static class Program
{
    const float H = 1.0f;       // cap height
    const float S = 0.15f;      // stem / stroke width
    const float D = 0.15f;      // extrusion depth along -Z
    const int ARC_SEG = 32;     // segments per half-circle

    static int Main()
    {
        string outDir = ResolveOutDir();
        Directory.CreateDirectory(outDir);

        var glyphs = new List<(string Name, float Advance, Action<ObjBuilder, float> Build)>
        {
            ("A", 0.85f, BuildA),
            ("B", 0.65f, BuildB),
            ("C", 0.80f, BuildC),
            ("D", 0.75f, BuildD),
            ("E", 0.65f, BuildE),
            ("F", 0.65f, BuildF),
            ("G", 0.85f, BuildG),
            ("H", 0.80f, BuildH),
            ("I", 0.25f, BuildI),
            ("J", 0.55f, BuildJ),
            ("K", 0.75f, BuildK),
            ("L", 0.60f, BuildL),
            ("M", 1.00f, BuildM),
            ("N", 0.85f, BuildN),
            ("O", 0.90f, BuildO),
            ("P", 0.70f, BuildP),
            ("Q", 1.00f, BuildQ),
            ("R", 0.75f, BuildR),
            ("S", 0.70f, BuildS),
            ("T", 0.75f, BuildT),
            ("U", 0.80f, BuildU),
            ("V", 0.85f, BuildV),
            ("W", 1.15f, BuildW),
            ("X", 0.80f, BuildX),
            ("Y", 0.80f, BuildY),
            ("Z", 0.75f, BuildZ),
            ("0", 0.80f, Build0),
            ("1", 0.40f, Build1),
            ("2", 0.70f, Build2),
            ("3", 0.70f, Build3),
            ("4", 0.80f, Build4),
            ("5", 0.70f, Build5),
            ("6", 0.75f, Build6),
            ("7", 0.70f, Build7),
            ("8", 0.70f, Build8),
            ("9", 0.75f, Build9),
        };

        foreach (var (name, advance, build) in glyphs)
        {
            var ob = new ObjBuilder();
            build(ob, advance);
            string path = Path.Combine(outDir, name + ".obj");
            ob.WriteObj(path, name, advance);
            Console.WriteLine($"  {name}.obj  advance={advance:F2}  v={ob.VertexCount,5}  n={ob.NormalCount,5}  f={ob.FaceCount,5}");
        }

        Console.WriteLine($"\nDone: {glyphs.Count} glyphs written to {outDir}");
        return 0;
    }

    static string ResolveOutDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "scenes")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate repo root (no 'scenes/' directory found above the binary).");
        return Path.Combine(dir.FullName, "scenes", "libraries", "typography", "glyphs");
    }

    // ============================================================
    //  ObjBuilder: accumulates v / vn / f lines and writes OBJ.
    // ============================================================
    sealed class ObjBuilder
    {
        readonly List<Vector3> _positions = new();
        readonly List<Vector3> _normals = new();
        readonly List<(int v0, int n0, int v1, int n1, int v2, int n2)> _tris = new();

        public int VertexCount => _positions.Count;
        public int NormalCount => _normals.Count;
        public int FaceCount => _tris.Count;

        public int V(Vector3 p)
        {
            _positions.Add(p);
            return _positions.Count - 1;
        }

        public int N(Vector3 n)
        {
            float len = n.Length();
            _normals.Add(len > 1e-6f ? n / len : new Vector3(0, 0, 1));
            return _normals.Count - 1;
        }

        public void Tri(int v0, int n0, int v1, int n1, int v2, int n2)
            => _tris.Add((v0, n0, v1, n1, v2, n2));

        // Flat triangle: all 3 vertices share the same normal index.
        public void TriFlat(int v0, int v1, int v2, int n)
            => _tris.Add((v0, n, v1, n, v2, n));

        // Flat quad as 2 tris (v0,v1,v2,v3 should be CCW as viewed from the normal direction).
        public void QuadFlat(int v0, int v1, int v2, int v3, int n)
        {
            _tris.Add((v0, n, v1, n, v2, n));
            _tris.Add((v0, n, v2, n, v3, n));
        }

        public void WriteObj(string path, string name, float advance)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Glyph '{name}' — geometric sans-serif");
            sb.AppendLine($"# Procedurally generated by src/Tools/TypographyGen");
            sb.AppendLine($"# Cap height: {H:F2}   Stroke width: {S:F2}   Extrusion depth: {D:F2}");
            sb.AppendLine($"# Advance width: {advance:F3}");
            sb.AppendLine($"# Vertices: {_positions.Count}   Normals: {_normals.Count}   Triangles: {_tris.Count}");
            sb.AppendLine();
            sb.AppendLine($"o glyph_{name}");

            foreach (var p in _positions)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "v {0:F6} {1:F6} {2:F6}", p.X, p.Y, p.Z));

            foreach (var n in _normals)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "vn {0:F6} {1:F6} {2:F6}", n.X, n.Y, n.Z));

            foreach (var t in _tris)
                sb.AppendLine($"f {t.v0 + 1}//{t.n0 + 1} {t.v1 + 1}//{t.n1 + 1} {t.v2 + 1}//{t.n2 + 1}");

            File.WriteAllText(path, sb.ToString());
        }
    }

    // ============================================================
    //  Extrusion primitives
    // ============================================================

    // Axis-aligned rectangle in XY extruded to -Z. All faces flat.
    static void Rect(ObjBuilder ob, float x, float y, float w, float h)
    {
        float x1 = x + w, y1 = y + h;
        int fbl = ob.V(new(x,  y,  0));
        int fbr = ob.V(new(x1, y,  0));
        int ftr = ob.V(new(x1, y1, 0));
        int ftl = ob.V(new(x,  y1, 0));
        int bbl = ob.V(new(x,  y,  -D));
        int bbr = ob.V(new(x1, y,  -D));
        int btr = ob.V(new(x1, y1, -D));
        int btl = ob.V(new(x,  y1, -D));

        int nF = ob.N(new(0, 0, 1));
        int nB = ob.N(new(0, 0, -1));
        int nL = ob.N(new(-1, 0, 0));
        int nR = ob.N(new(1, 0, 0));
        int nT = ob.N(new(0, 1, 0));
        int nBo = ob.N(new(0, -1, 0));

        ob.QuadFlat(fbl, fbr, ftr, ftl, nF);    // front  (+Z)
        ob.QuadFlat(bbr, bbl, btl, btr, nB);    // back   (-Z)
        ob.QuadFlat(fbl, ftl, btl, bbl, nL);    // left   (-X)
        ob.QuadFlat(fbr, bbr, btr, ftr, nR);    // right  (+X)
        ob.QuadFlat(ftl, ftr, btr, btl, nT);    // top    (+Y)
        ob.QuadFlat(fbl, bbl, bbr, fbr, nBo);   // bottom (-Y)
    }

    // Convex polygon (2D vertex ring, any orientation) extruded to -Z. Flat
    // side-walls, flat (fan-triangulated) front/back caps. Auto-detects CW
    // input and reverses so side-wall normals always point outward. Generic
    // helper used by diagonals / trapezoids / parallelograms.
    static void Poly(ObjBuilder ob, (float x, float y)[] ring)
    {
        int n = ring.Length;
        // Signed area (shoelace): positive => CCW, negative => CW.
        float sArea2 = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            sArea2 += ring[i].x * ring[j].y - ring[j].x * ring[i].y;
        }
        if (sArea2 < 0f)
        {
            var rev = new (float x, float y)[n];
            for (int i = 0; i < n; i++) rev[i] = ring[n - 1 - i];
            ring = rev;
        }
        int[] f = new int[n];
        int[] b = new int[n];
        for (int i = 0; i < n; i++)
        {
            f[i] = ob.V(new(ring[i].x, ring[i].y, 0));
            b[i] = ob.V(new(ring[i].x, ring[i].y, -D));
        }
        int nF = ob.N(new(0, 0, 1));
        int nB = ob.N(new(0, 0, -1));

        // Front cap (CCW from +Z) — fan from vertex 0
        for (int i = 1; i < n - 1; i++)
            ob.TriFlat(f[0], f[i], f[i + 1], nF);
        // Back cap (CCW from -Z) — reverse winding
        for (int i = 1; i < n - 1; i++)
            ob.TriFlat(b[0], b[i + 1], b[i], nB);
        // Side walls: edge i -> (i+1)
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float dx = ring[j].x - ring[i].x;
            float dy = ring[j].y - ring[i].y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) continue;
            // CCW polygon -> outward normal is right-hand perpendicular of the edge: (dy, -dx).
            int nw = ob.N(new(dy / len, -dx / len, 0));
            // CCW from normal direction: f[i], f[j], b[j], b[i]
            ob.QuadFlat(f[i], f[j], b[j], b[i], nw);
        }
    }

    // Axis-aligned triangle extruded to -Z.
    static void Tri(ObjBuilder ob, (float x, float y) p0, (float x, float y) p1, (float x, float y) p2)
        => Poly(ob, new[] { p0, p1, p2 });

    // Convex parallelogram / trapezoid / quad extruded to -Z (4 corners, CCW).
    static void Quad(ObjBuilder ob, (float x, float y) p0, (float x, float y) p1, (float x, float y) p2, (float x, float y) p3)
        => Poly(ob, new[] { p0, p1, p2, p3 });

    // Elliptical annular sector (the main "curve" primitive). Outer radii (rOutX, rOutY),
    // inner (rInX, rInY). Angular span a0..a1 with a1 > a0. Segment count `segments`.
    // Side walls (inner/outer ellipsoidal surfaces) are emitted with per-vertex
    // radial normals -> SmoothTriangle. Front/back caps and the two radial end-caps
    // are flat. All coordinates in 2D; z is 0 (front) and -D (back).
    static void Arc(ObjBuilder ob,
                    float cx, float cy,
                    float rOutX, float rOutY,
                    float rInX, float rInY,
                    float a0, float a1,
                    int segments)
    {
        int np = segments + 1;
        int[] fo = new int[np], fi = new int[np], bo = new int[np], bi = new int[np];
        int[] nOut = new int[np], nIn = new int[np];

        for (int i = 0; i < np; i++)
        {
            float a = a0 + (a1 - a0) * i / segments;
            float cosA = MathF.Cos(a), sinA = MathF.Sin(a);
            fo[i] = ob.V(new(cx + rOutX * cosA, cy + rOutY * sinA, 0));
            fi[i] = ob.V(new(cx + rInX * cosA,  cy + rInY * sinA,  0));
            bo[i] = ob.V(new(cx + rOutX * cosA, cy + rOutY * sinA, -D));
            bi[i] = ob.V(new(cx + rInX * cosA,  cy + rInY * sinA,  -D));
            // Outward gradient of level-set (x-cx)^2/rOutX^2 + (y-cy)^2/rOutY^2 = 1:
            //   grad = (cos a / rOutX, sin a / rOutY)  (before normalization).
            nOut[i] = ob.N(new(cosA / rOutX, sinA / rOutY, 0));
            nIn[i]  = ob.N(new(-cosA / rInX, -sinA / rInY, 0));
        }

        int nFront = ob.N(new(0, 0, 1));
        int nBack  = ob.N(new(0, 0, -1));

        // Front cap (z=0), viewed from +Z, CCW: fi[i], fo[i], fo[i+1], fi[i+1]
        for (int i = 0; i < segments; i++)
            ob.QuadFlat(fi[i], fo[i], fo[i + 1], fi[i + 1], nFront);
        // Back cap (z=-D), viewed from -Z, reversed winding
        for (int i = 0; i < segments; i++)
            ob.QuadFlat(bi[i], bi[i + 1], bo[i + 1], bo[i], nBack);

        // Outer curved wall (smooth normals, facing radially outward).
        // CCW viewed from outside: fo[i], fo[i+1], bo[i+1], bo[i]
        for (int i = 0; i < segments; i++)
        {
            int a = fo[i], b = fo[i + 1], c = bo[i + 1], d = bo[i];
            int na = nOut[i], nb = nOut[i + 1];
            ob.Tri(a, na, b, nb, c, nb);
            ob.Tri(a, na, c, nb, d, na);
        }
        // Inner curved wall (smooth normals, facing radially inward).
        // CCW viewed from inside (toward center): reverse of outer.
        for (int i = 0; i < segments; i++)
        {
            int a = fi[i], b = fi[i + 1], c = bi[i + 1], d = bi[i];
            int na = nIn[i], nb = nIn[i + 1];
            ob.Tri(a, na, d, na, c, nb);
            ob.Tri(a, na, c, nb, b, nb);
        }

        // Radial end cap at a0 — outward normal is tangent rotated 90° CW (into decreasing angle).
        {
            float tx = -MathF.Sin(a0), ty = MathF.Cos(a0);
            int nCap = ob.N(new(ty, -tx, 0));  // rotate tangent -90°
            ob.QuadFlat(fi[0], bi[0], bo[0], fo[0], nCap);
        }
        // Radial end cap at a1 — outward normal is tangent rotated 90° CCW.
        {
            float tx = -MathF.Sin(a1), ty = MathF.Cos(a1);
            int nCap = ob.N(new(-ty, tx, 0));  // rotate tangent +90°
            ob.QuadFlat(fi[np - 1], fo[np - 1], bo[np - 1], bi[np - 1], nCap);
        }
    }

    // Circular variant for convenience.
    static void Arc(ObjBuilder ob, float cx, float cy, float rOut, float rIn, float a0, float a1, int segments)
        => Arc(ob, cx, cy, rOut, rOut, rIn, rIn, a0, a1, segments);

    // ============================================================
    //  Glyph builders (A-Z, 0-9)
    //  Conventions used inside these:
    //    - `ad` is advance width.
    //    - All letters share stem width S and cap height H.
    // ============================================================

    // --- Helpers used by diagonal letters ------------------------

    // Slanted "stem" parallelogram: horizontal width S maintained throughout,
    // from (x0Bottom, 0) to (x0Top, H). Useful for A/V/W/Y/N legs.
    static void SlantStem(ObjBuilder ob, float x0Bottom, float x0Top)
    {
        Quad(ob,
            (x0Bottom,     0),
            (x0Bottom + S, 0),
            (x0Top + S,    H),
            (x0Top,        H));
    }

    // --- A ------------------------------------------------------
    static void BuildA(ObjBuilder ob, float ad)
    {
        // Apex at (ad/2, H). Legs converge to width S at apex.
        float xLeftBot = 0f;
        float xLeftTop = ad / 2f - S / 2f;
        float xRightBot = ad - S;
        float xRightTop = ad / 2f - S / 2f;   // same as xLeftTop so apex fills seamlessly
        SlantStem(ob, xLeftBot,  xLeftTop);
        SlantStem(ob, xRightBot, xRightTop);

        // Crossbar at y = 0.35 * H, height S.
        float yBar = 0.35f;
        // inner edge of left leg at yBar: slope = (xLeftTop - xLeftBot)/H; inner offset = +S.
        float xL = xLeftBot + S + (xLeftTop - xLeftBot) * yBar / H;
        float xR = xRightBot + (xRightTop - xRightBot) * (yBar + S) / H;
        Rect(ob, xL, yBar, xR - xL, S);
    }

    // --- B ------------------------------------------------------
    static void BuildB(ObjBuilder ob, float ad)
    {
        // Stem on the left.
        Rect(ob, 0, 0, S, H);
        // Two circular bowls (upper + lower) touching at y=H/2.
        float rOut = H / 4f;     // 0.25
        float rIn  = rOut - S;   // 0.10
        // Right edge of bowls = S + rOut; advance is chosen (0.65) >= S + rOut + tiny margin.
        // We slightly stretch bowls horizontally so the right edge lands at `ad`.
        float rOutX = ad - S;        // stretch so bowl spans full remaining width
        float rInX  = rOutX - S;
        Arc(ob, S, 0.75f, rOutX, rOut, rInX, rIn, -MathF.PI / 2f, MathF.PI / 2f, ARC_SEG);
        Arc(ob, S, 0.25f, rOutX, rOut, rInX, rIn, -MathF.PI / 2f, MathF.PI / 2f, ARC_SEG);
        // Middle connector: a horizontal bar at y=H/2 - S/2 to x=S+rOutX (covers any
        // tiny gap and visually strengthens the waist).
        Rect(ob, 0, H / 2f - S / 2f, S + S * 0.1f, S);
    }

    // --- C ------------------------------------------------------
    static void BuildC(ObjBuilder ob, float ad)
    {
        // Circular ring with an opening on the right.
        float rOut = H / 2f;          // 0.50
        float rIn  = rOut - S;         // 0.35
        float cx = ad / 2f, cy = H / 2f;
        float rOutX = ad / 2f;
        float rInX  = rOutX - S;
        float gap = MathF.PI / 6f;     // 30° gap on each end of the opening
        Arc(ob, cx, cy, rOutX, rOut, rInX, rIn, gap, 2f * MathF.PI - gap, ARC_SEG * 2);
    }

    // --- D ------------------------------------------------------
    static void BuildD(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0, S, H);
        float rOut = H / 2f;
        float rIn  = rOut - S;
        float rOutX = ad - S;
        float rInX  = rOutX - S;
        Arc(ob, S, H / 2f, rOutX, rOut, rInX, rIn, -MathF.PI / 2f, MathF.PI / 2f, ARC_SEG);
    }

    // --- E ------------------------------------------------------
    static void BuildE(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0,         S,  H);         // stem
        Rect(ob, 0, H - S,     ad, S);         // top arm
        Rect(ob, 0, 0,         ad, S);         // bottom arm
        Rect(ob, 0, (H - S) / 2f, ad * 0.82f, S); // middle arm (slightly shorter)
    }

    // --- F ------------------------------------------------------
    static void BuildF(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0,         S,  H);
        Rect(ob, 0, H - S,     ad, S);
        Rect(ob, 0, (H - S) / 2f, ad * 0.80f, S);
    }

    // --- G ------------------------------------------------------
    static void BuildG(ObjBuilder ob, float ad)
    {
        // C + a horizontal serif on the inside-right at mid-height.
        float rOut = H / 2f, rIn = rOut - S;
        float cx = ad / 2f, cy = H / 2f;
        float rOutX = ad / 2f, rInX = rOutX - S;
        float gap = MathF.PI / 6f;
        Arc(ob, cx, cy, rOutX, rOut, rInX, rIn, gap, 2f * MathF.PI - gap, ARC_SEG * 2);
        // Horizontal inner-right serif
        Rect(ob, cx + rInX * 0.55f, cy - S / 2f, rInX * 0.45f, S);
        // Vertical short connector on right edge from the serif down to opening
        Rect(ob, ad - S, cy - S / 2f, S, 0f); // placeholder; we emit a proper rect below
        // Replace with: vertical leg from opening-start downward? The opening-start
        // is at angle `gap` on the right side -> (cx + rOutX cos gap, cy + rOut sin gap).
        // Skip the placeholder; emit a small vertical tab instead:
        float tabX = cx + rInX * MathF.Cos(gap);
        float tabYTop = cy + rIn * MathF.Sin(gap);
        Rect(ob, tabX - S, cy - S / 2f, S, tabYTop - (cy - S / 2f));
    }

    // --- H ------------------------------------------------------
    static void BuildH(ObjBuilder ob, float ad)
    {
        Rect(ob, 0,       0, S, H);
        Rect(ob, ad - S,  0, S, H);
        Rect(ob, S, (H - S) / 2f, ad - 2f * S, S);
    }

    // --- I ------------------------------------------------------
    static void BuildI(ObjBuilder ob, float ad)
    {
        Rect(ob, (ad - S) / 2f, 0, S, H);
    }

    // --- J ------------------------------------------------------
    static void BuildJ(ObjBuilder ob, float ad)
    {
        // Top-right stem descends to 1/3 H, then curves left into a hook.
        float stemX = ad - S;
        Rect(ob, stemX, H * 0.20f, S, H - H * 0.20f);
        // Hook: lower half-ring (bottom half of a small circle), center (ad/2, H*0.20)
        float cx = ad / 2f, cy = H * 0.20f;
        float rOutX = ad / 2f, rOut = H * 0.20f;
        float rInX = rOutX - S, rIn = rOut - S;
        Arc(ob, cx, cy, rOutX, rOut, rInX, rIn, MathF.PI, 2f * MathF.PI, ARC_SEG);
    }

    // --- K ------------------------------------------------------
    static void BuildK(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0, S, H);
        float midY = H / 2f;
        // Upper diagonal: from (S, midY) to (ad, H)
        Quad(ob,
            (S,           midY - S / 2f),
            (ad - S,      H - S),
            (ad,          H),
            (S + S * 0.3f, midY + S / 2f));
        // Lower diagonal: from (S, midY) to (ad, 0)
        Quad(ob,
            (S + S * 0.3f, midY - S / 2f),
            (ad,          0),
            (ad - S,      0),
            (S,           midY + S / 2f));
    }

    // --- L ------------------------------------------------------
    static void BuildL(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0, S,  H);
        Rect(ob, 0, 0, ad, S);
    }

    // --- M ------------------------------------------------------
    static void BuildM(ObjBuilder ob, float ad)
    {
        Rect(ob, 0,      0, S, H);
        Rect(ob, ad - S, 0, S, H);
        // Two inner diagonals meeting at (ad/2, H*0.25).
        float dipY = H * 0.30f;
        Quad(ob,
            (S,            H - S),
            (S + S * 0.1f, H),
            (ad / 2f + S / 2f, dipY),
            (ad / 2f - S / 2f, dipY));
        Quad(ob,
            (ad - S - S * 0.1f, H),
            (ad - S,            H - S),
            (ad / 2f + S / 2f,  dipY),
            (ad / 2f - S / 2f,  dipY));
    }

    // --- N ------------------------------------------------------
    static void BuildN(ObjBuilder ob, float ad)
    {
        Rect(ob, 0,      0, S, H);   // left stem
        Rect(ob, ad - S, 0, S, H);   // right stem
        // Diagonal parallelogram: horizontal width S, from top of left stem's
        // inner edge down to bottom of right stem's inner edge.
        Quad(ob,
            (ad - 2f * S, 0),    // base-left of diagonal
            (ad - S,      0),    // base-right (touches right stem)
            (2f * S,      H),    // top-right
            (S,           H));   // top-left (touches left stem)
    }

    // --- O ------------------------------------------------------
    static void BuildO(ObjBuilder ob, float ad)
    {
        float cx = ad / 2f, cy = H / 2f;
        float rOutX = ad / 2f, rOutY = H / 2f;
        float rInX  = rOutX - S, rInY = rOutY - S;
        Arc(ob, cx, cy, rOutX, rOutY, rInX, rInY, 0, 2f * MathF.PI, ARC_SEG * 2);
    }

    // --- P ------------------------------------------------------
    static void BuildP(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0, S, H);
        // Upper bowl, half-height.
        float rOut = H / 4f, rIn = rOut - S;
        float rOutX = ad - S, rInX = rOutX - S;
        Arc(ob, S, H - rOut, rOutX, rOut, rInX, rIn, -MathF.PI / 2f, MathF.PI / 2f, ARC_SEG);
    }

    // --- Q ------------------------------------------------------
    static void BuildQ(ObjBuilder ob, float ad)
    {
        // O-body (circular) + short tail at lower-right going outside the bowl.
        float oAdvance = ad - 0.15f;  // leave room for the tail
        float cx = oAdvance / 2f, cy = H / 2f;
        float rOutX = oAdvance / 2f, rOutY = H / 2f;
        float rInX  = rOutX - S, rInY = rOutY - S;
        Arc(ob, cx, cy, rOutX, rOutY, rInX, rInY, 0, 2f * MathF.PI, ARC_SEG * 2);
        // Tail: small diagonal from inside of the lower-right edge to outside.
        float ta = -MathF.PI / 4f;
        float ix = cx + rInX  * MathF.Cos(ta), iy = cy + rInY  * MathF.Sin(ta);
        float ox = cx + (rOutX + 0.15f) * MathF.Cos(ta);
        float oy = cy + (rOutY + 0.15f) * MathF.Sin(ta);
        // Thick diagonal line from (ix,iy) to (ox,oy), width S perpendicular.
        float dx = ox - ix, dy = oy - iy;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float pxn = -dy / len * (S / 2f), pyn = dx / len * (S / 2f);
        Quad(ob,
            (ix - pxn, iy - pyn),
            (ox - pxn, oy - pyn),
            (ox + pxn, oy + pyn),
            (ix + pxn, iy + pyn));
    }

    // --- R ------------------------------------------------------
    static void BuildR(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, 0, S, H);
        // Upper bowl (same as P).
        float rOut = H / 4f, rIn = rOut - S;
        float rOutX = ad - S, rInX = rOutX - S;
        Arc(ob, S, H - rOut, rOutX, rOut, rInX, rIn, -MathF.PI / 2f, MathF.PI / 2f, ARC_SEG);
        // Diagonal leg from inside of the bowl's lower-right to the baseline-right.
        float bowlBottomY = H - 2f * rOut;   // y where bowl meets stem
        Quad(ob,
            (S + S * 0.1f,     bowlBottomY),
            (S + S * 0.1f + S, bowlBottomY),
            (ad,               0),
            (ad - S,           0));
    }

    // --- S ------------------------------------------------------
    static void BuildS(ObjBuilder ob, float ad)
    {
        // Two half-arches + diagonal connector.
        float cxU = ad / 2f, cyU = H - H / 4f;  // upper center
        float cxL = ad / 2f, cyL = H / 4f;      // lower center
        float rOutX = ad / 2f, rOut = H / 4f;
        float rInX = rOutX - S, rIn = rOut - S;
        // Upper arch: top half of a circle (y > cy). Angles 0..π CCW through π/2.
        Arc(ob, cxU, cyU, rOutX, rOut, rInX, rIn, 0, MathF.PI, ARC_SEG);
        // Lower arch: bottom half (y < cy). Angles π..2π CCW through 3π/2.
        Arc(ob, cxL, cyL, rOutX, rOut, rInX, rIn, MathF.PI, 2f * MathF.PI, ARC_SEG);
        // Diagonal connector from upper-arch left-end (cxU - rOutX, cyU) to lower-arch right-end (cxL + rOutX, cyL).
        (float x, float y) p0o = (0,        cyU);
        (float x, float y) p1o = (ad,       cyL);
        float dx = p1o.x - p0o.x, dy = p1o.y - p0o.y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float px = -dy / len * (S / 2f), py = dx / len * (S / 2f);
        Quad(ob,
            (p0o.x - px, p0o.y - py),
            (p1o.x - px, p1o.y - py),
            (p1o.x + px, p1o.y + py),
            (p0o.x + px, p0o.y + py));
    }

    // --- T ------------------------------------------------------
    static void BuildT(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, H - S, ad, S);
        Rect(ob, (ad - S) / 2f, 0, S, H - S);
    }

    // --- U ------------------------------------------------------
    static void BuildU(ObjBuilder ob, float ad)
    {
        // Two vertical stems from H down to y = H/4, connected by lower half-ring.
        float cx = ad / 2f, cy = H / 4f;
        float rOutX = ad / 2f, rOut = H / 4f;
        float rInX = rOutX - S, rIn = rOut - S;
        Rect(ob, 0,      cy, S, H - cy);
        Rect(ob, ad - S, cy, S, H - cy);
        Arc(ob, cx, cy, rOutX, rOut, rInX, rIn, MathF.PI, 2f * MathF.PI, ARC_SEG);
    }

    // --- V ------------------------------------------------------
    static void BuildV(ObjBuilder ob, float ad)
    {
        // Two diagonals meeting at (ad/2, 0).
        float xLeftTop = 0f, xRightTop = ad - S;
        float xMeetLeft = ad / 2f - S / 2f, xMeetRight = ad / 2f - S / 2f;
        Quad(ob,
            (xLeftTop,     H),
            (xLeftTop + S, H),
            (xMeetRight + S, 0),
            (xMeetLeft,  0));
        Quad(ob,
            (xRightTop,     H),
            (xMeetLeft,     0),
            (xMeetRight + S, 0),
            (xRightTop + S, H));
    }

    // --- W ------------------------------------------------------
    static void BuildW(ObjBuilder ob, float ad)
    {
        // Four diagonal strokes forming two adjacent V's. Each stroke is a
        // parallelogram of horizontal width S. Top peaks at x=0, ad/2-S/2,
        // ad-S; bottom valleys at x=ad/4-S/2, 3ad/4-S/2.
        float tL = 0f;                        // left top peak (left edge)
        float tM = ad / 2f - S / 2f;          // middle top peak
        float tR = ad - S;                    // right top peak
        float bL = ad / 4f - S / 2f;          // left bottom valley
        float bR = 3f * ad / 4f - S / 2f;     // right bottom valley

        // Stroke 1:  (tL..tL+S, H)  ->  (bL..bL+S, 0)    [\]
        Quad(ob, (tL, H), (tL + S, H), (bL + S, 0), (bL, 0));
        // Stroke 2:  (bL..bL+S, 0)  ->  (tM..tM+S, H)    [/]
        Quad(ob, (bL, 0), (bL + S, 0), (tM + S, H), (tM, H));
        // Stroke 3:  (tM..tM+S, H)  ->  (bR..bR+S, 0)    [\]
        Quad(ob, (tM, H), (tM + S, H), (bR + S, 0), (bR, 0));
        // Stroke 4:  (bR..bR+S, 0)  ->  (tR..tR+S, H)    [/]
        Quad(ob, (bR, 0), (bR + S, 0), (tR + S, H), (tR, H));
    }

    // --- X ------------------------------------------------------
    static void BuildX(ObjBuilder ob, float ad)
    {
        // Two crossing diagonals.
        Quad(ob, (0, H), (S, H), (ad, 0), (ad - S, 0));
        Quad(ob, (ad - S, H), (ad, H), (S, 0), (0, 0));
    }

    // --- Y ------------------------------------------------------
    static void BuildY(ObjBuilder ob, float ad)
    {
        // Two upper diagonals from top corners to (ad/2, H/2), then vertical stem down.
        float midX = ad / 2f - S / 2f, midY = H / 2f;
        Quad(ob, (0, H),      (S, H),    (midX + S, midY), (midX,     midY));
        Quad(ob, (ad - S, H), (ad, H),   (midX + S, midY), (midX,     midY));
        Rect(ob, midX, 0, S, midY);
    }

    // --- Z ------------------------------------------------------
    static void BuildZ(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, H - S, ad, S);
        Rect(ob, 0, 0,     ad, S);
        Quad(ob, (0, S), (S, S), (ad, H - S), (ad - S, H - S));
    }

    // --- 0 ------------------------------------------------------
    static void Build0(ObjBuilder ob, float ad)
    {
        float cx = ad / 2f, cy = H / 2f;
        float rOutX = ad / 2f, rOutY = H / 2f;
        float rInX = rOutX - S, rInY = rOutY - S;
        Arc(ob, cx, cy, rOutX, rOutY, rInX, rInY, 0, 2f * MathF.PI, ARC_SEG * 2);
    }

    // --- 1 ------------------------------------------------------
    static void Build1(ObjBuilder ob, float ad)
    {
        // Centered vertical stem + short serif flag on upper-left + wider base.
        float stemX = (ad - S) / 2f;
        Rect(ob, stemX, 0, S, H);
        // Flag: diagonal from (stemX, H) down-left to (stemX - ad*0.35, H - S*1.5)
        Quad(ob,
            (stemX - ad * 0.40f, H - S * 1.2f),
            (stemX - ad * 0.40f + S * 0.6f, H - S * 1.2f - S * 0.6f),
            (stemX + S,   H),
            (stemX,       H));
        // Base
        Rect(ob, 0, 0, ad, S);
    }

    // --- 2 ------------------------------------------------------
    static void Build2(ObjBuilder ob, float ad)
    {
        // Upper: right-facing half-circle (angles -π/2 to π/2 swept around the TOP).
        // Simpler: top half-arch + diagonal descent + bottom bar.
        float rOutX = ad / 2f, rOut = H / 4f;
        float rInX = rOutX - S, rIn = rOut - S;
        float cx = ad / 2f, cy = H - H / 4f;
        // Top half (0..π CCW through π/2) -> extended to (−π/4 .. π + π/4) to form a "3/4 of top-arch".
        Arc(ob, cx, cy, rOutX, rOut, rInX, rIn, -MathF.PI / 4f, MathF.PI + MathF.PI / 4f, ARC_SEG);
        // Diagonal descent from lower-right of arch to lower-left.
        float arcEndRightX = cx + rOutX * MathF.Cos(-MathF.PI / 4f);
        float arcEndRightY = cy + rOut  * MathF.Sin(-MathF.PI / 4f);
        Quad(ob,
            (arcEndRightX - S * 0.5f, arcEndRightY),
            (arcEndRightX + S * 0.5f, arcEndRightY),
            (S,                       S),
            (0,                       S));
        // Bottom bar
        Rect(ob, 0, 0, ad, S);
    }

    // --- 3 ------------------------------------------------------
    static void Build3(ObjBuilder ob, float ad)
    {
        // Two right-facing arches (top + bottom) sharing middle.
        float rOut = H / 4f, rIn = rOut - S;
        float rOutX = ad / 2f, rInX = rOutX - S;
        Arc(ob, ad / 2f, H - rOut, rOutX, rOut, rInX, rIn, -MathF.PI / 2f - MathF.PI / 4f, MathF.PI / 2f, ARC_SEG);
        Arc(ob, ad / 2f, rOut,     rOutX, rOut, rInX, rIn, -MathF.PI / 2f,                 MathF.PI / 2f + MathF.PI / 4f, ARC_SEG);
    }

    // --- 4 ------------------------------------------------------
    static void Build4(ObjBuilder ob, float ad)
    {
        // Right stem full height + diagonal descent from top-right to mid-left + horizontal crossbar.
        float stemX = ad - S;
        Rect(ob, stemX, 0, S, H);
        float barY = H * 0.35f;
        Rect(ob, 0, barY, ad, S);
        // Diagonal from (stemX, H) going down-left to (0, barY+S).
        Quad(ob,
            (0,        barY + S),
            (S * 1.5f, barY + S),
            (stemX,    H),
            (stemX - S * 0.3f, H));
    }

    // --- 5 ------------------------------------------------------
    static void Build5(ObjBuilder ob, float ad)
    {
        // Top bar + upper-left short stem + middle bar + lower right-facing half-circle.
        Rect(ob, 0, H - S, ad, S);                        // top
        Rect(ob, 0, H / 2f, S, H / 2f - S);               // upper-left short stem
        // Lower bowl: right-facing half-ring from angle +π/2 (top) CCW through π (left), 3π/2 (bottom), to 2π (right).
        float rOutX = ad / 2f, rOut = H / 4f;
        float rInX = rOutX - S, rIn = rOut - S;
        Arc(ob, ad / 2f, H / 4f, rOutX, rOut, rInX, rIn, MathF.PI / 2f, 2f * MathF.PI, ARC_SEG);
    }

    // --- 6 ------------------------------------------------------
    static void Build6(ObjBuilder ob, float ad)
    {
        // Upper curve (open top-right) + lower closed circle.
        float rOut = H / 4f, rIn = rOut - S;
        float rOutX = ad / 2f, rInX = rOutX - S;
        // Lower circle (the bowl)
        Arc(ob, ad / 2f, rOut, rOutX, rOut, rInX, rIn, 0, 2f * MathF.PI, ARC_SEG * 2);
        // Upper arch from top of bowl up-and-left to top of the glyph.
        float upperR_OutX = ad / 2f, upperR_Out = H / 2f - rOut;
        float upperR_InX  = upperR_OutX - S, upperR_In  = upperR_Out - S;
        Arc(ob, ad / 2f, 2f * rOut, upperR_OutX, upperR_Out, upperR_InX, upperR_In, MathF.PI / 2f, MathF.PI, ARC_SEG);
    }

    // --- 7 ------------------------------------------------------
    static void Build7(ObjBuilder ob, float ad)
    {
        Rect(ob, 0, H - S, ad, S);
        // Diagonal from (ad, H) down-left to (ad*0.25, 0).
        Quad(ob,
            (ad * 0.18f,          0),
            (ad * 0.18f + S * 1.3f, 0),
            (ad,                  H - S),
            (ad - S * 1.3f,       H - S));
    }

    // --- 8 ------------------------------------------------------
    static void Build8(ObjBuilder ob, float ad)
    {
        float rOut = H / 4f, rIn = rOut - S;
        float rOutX = ad / 2f, rInX = rOutX - S;
        Arc(ob, ad / 2f, H - rOut, rOutX, rOut, rInX, rIn, 0, 2f * MathF.PI, ARC_SEG * 2);
        Arc(ob, ad / 2f, rOut,     rOutX, rOut, rInX, rIn, 0, 2f * MathF.PI, ARC_SEG * 2);
    }

    // --- 9 ------------------------------------------------------
    static void Build9(ObjBuilder ob, float ad)
    {
        // Mirror of 6: upper closed circle + lower curve (open bottom-left).
        float rOut = H / 4f, rIn = rOut - S;
        float rOutX = ad / 2f, rInX = rOutX - S;
        Arc(ob, ad / 2f, H - rOut, rOutX, rOut, rInX, rIn, 0, 2f * MathF.PI, ARC_SEG * 2);
        float lowerR_OutX = ad / 2f, lowerR_Out = H / 2f - rOut;
        float lowerR_InX  = lowerR_OutX - S, lowerR_In  = lowerR_Out - S;
        Arc(ob, ad / 2f, H - 2f * rOut, lowerR_OutX, lowerR_Out, lowerR_InX, lowerR_In, -MathF.PI / 2f, 0, ARC_SEG);
    }
}
