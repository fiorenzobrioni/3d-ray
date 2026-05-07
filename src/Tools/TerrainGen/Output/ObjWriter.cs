using System.Globalization;
using System.IO;
using System.Text;
using TerrainGen.Splatting;

namespace TerrainGen.Output;

/// <summary>
/// Writes a <see cref="StratumMesh"/> as a Wavefront OBJ file. Format matches
/// what <c>src/RayTracer/Scene/ObjLoader.cs</c> consumes: <c>v</c>,
/// <c>vt</c>, <c>vn</c>, and triangle <c>f v/vt/vn</c>.
/// </summary>
public static class ObjWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(string path, StratumMesh mesh, string headerComment)
    {
        var sb = new StringBuilder(mesh.Positions.Count * 64);
        sb.Append("# ").AppendLine(headerComment);
        sb.Append($"# verts={mesh.Positions.Count}, faces={mesh.Faces.Count}, shading=")
          .AppendLine(mesh.FlatShade ? "flat" : "smooth");
        sb.AppendLine();

        foreach (var p in mesh.Positions)
            sb.Append("v ")
              .Append(p.X.ToString("0.######", Inv)).Append(' ')
              .Append(p.Y.ToString("0.######", Inv)).Append(' ')
              .Append(p.Z.ToString("0.######", Inv)).AppendLine();

        foreach (var t in mesh.Uvs)
            sb.Append("vt ")
              .Append(t.X.ToString("0.######", Inv)).Append(' ')
              .Append(t.Y.ToString("0.######", Inv)).AppendLine();

        foreach (var nm in mesh.Normals)
            sb.Append("vn ")
              .Append(nm.X.ToString("0.######", Inv)).Append(' ')
              .Append(nm.Y.ToString("0.######", Inv)).Append(' ')
              .Append(nm.Z.ToString("0.######", Inv)).AppendLine();

        // OBJ indices are 1-based.
        foreach (var (a, b, c) in mesh.Faces)
        {
            int ia = a + 1, ib = b + 1, ic = c + 1;
            sb.Append("f ")
              .Append(ia).Append('/').Append(ia).Append('/').Append(ia).Append(' ')
              .Append(ib).Append('/').Append(ib).Append('/').Append(ib).Append(' ')
              .Append(ic).Append('/').Append(ic).Append('/').Append(ic).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
