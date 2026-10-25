using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace RayTracer.Scene;

/// <summary>
/// Stanford PLY reader (PIANO-REVIEW.md Phase 2): <c>ascii</c>,
/// <c>binary_little_endian</c> and <c>binary_big_endian</c>, properties of any
/// scalar type. From the <c>vertex</c> element: <c>x y z</c>, the normal
/// <c>nx ny nz</c> and texture coordinates (<c>u v</c>, <c>s t</c> or
/// <c>texture_u texture_v</c>); from <c>face</c>: the list <c>vertex_indices</c>
/// (or <c>vertex_index</c>), polygons of any size. Other elements and
/// properties (colours, confidence, edges) are skipped. PLY carries no
/// materials: the entity's material covers the model.
/// </summary>
public static class PlyLoader
{
    private enum Format { Ascii, BinaryLittle, BinaryBig }

    private sealed record Property(string Name, string Type, string? CountType);

    private sealed class Element
    {
        public string Name = "";
        public int Count;
        public List<Property> Properties = new();
    }

    public static ImportedMesh? Read(string path, List<string>? warnings)
    {
        using var stream = File.OpenRead(path);
        var format = Format.Ascii;
        var elements = new List<Element>();

        // Header: ASCII lines up to end_header.
        string? magic = ReadLine(stream);
        if (magic?.Trim() != "ply")
        {
            warnings?.Add("not a PLY file (missing 'ply' magic).");
            return null;
        }
        while (true)
        {
            string? line = ReadLine(stream);
            if (line == null) { warnings?.Add("PLY header ends without end_header."); return null; }
            var tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) continue;
            switch (tok[0])
            {
                case "format":
                    format = tok.Length > 1 ? tok[1] switch
                    {
                        "ascii" => Format.Ascii,
                        "binary_little_endian" => Format.BinaryLittle,
                        "binary_big_endian" => Format.BinaryBig,
                        _ => Format.Ascii,
                    } : Format.Ascii;
                    break;
                case "element" when tok.Length >= 3:
                    elements.Add(new Element { Name = tok[1], Count = int.Parse(tok[2], CultureInfo.InvariantCulture) });
                    break;
                case "property" when elements.Count > 0:
                    if (tok.Length >= 5 && tok[1] == "list")
                        elements[^1].Properties.Add(new Property(tok[4], tok[3], tok[2]));
                    else if (tok.Length >= 3)
                        elements[^1].Properties.Add(new Property(tok[2], tok[1], null));
                    break;
            }
            if (tok[0] == "end_header") break;
        }

        var imported = new ImportedMesh();
        var poly = imported.Poly;
        var reader = new ValueReader(stream, format);
        bool hasNormals = false, hasUvs = false;
        foreach (var el in elements)
        {
            bool vertex = el.Name == "vertex", face = el.Name == "face";
            int ix = -1, iy = -1, iz = -1, inx = -1, iny = -1, inz = -1, iu = -1, iv = -1, iList = -1;
            for (int k = 0; k < el.Properties.Count; k++)
            {
                switch (el.Properties[k].Name)
                {
                    case "x": ix = k; break;
                    case "y": iy = k; break;
                    case "z": iz = k; break;
                    case "nx": inx = k; break;
                    case "ny": iny = k; break;
                    case "nz": inz = k; break;
                    case "u" or "s" or "texture_u" or "texture_s": iu = k; break;
                    case "v" or "t" or "texture_v" or "texture_t": iv = k; break;
                    case "vertex_indices" or "vertex_index": iList = k; break;
                }
            }
            if (vertex) { hasNormals = inx >= 0 && iny >= 0 && inz >= 0; hasUvs = iu >= 0 && iv >= 0; }
            var values = new double[el.Properties.Count];
            for (int i = 0; i < el.Count; i++)
            {
                int[]? list = null;
                for (int k = 0; k < el.Properties.Count; k++)
                {
                    var prop = el.Properties[k];
                    if (prop.CountType != null)
                    {
                        int n = (int)reader.Read(prop.CountType);
                        if (face && k == iList)
                        {
                            list = new int[n];
                            for (int j = 0; j < n; j++) list[j] = (int)reader.Read(prop.Type);
                        }
                        else for (int j = 0; j < n; j++) reader.Read(prop.Type);
                    }
                    else values[k] = reader.Read(prop.Type);
                }
                if (!reader.Ok) { warnings?.Add($"PLY data ends early in element '{el.Name}'."); return Finish(); }
                if (vertex)
                {
                    poly.Positions.Add(new Vector3((float)values[ix], (float)values[iy], (float)values[iz]));
                    if (hasNormals) poly.Normals!.Add(new Vector3((float)values[inx], (float)values[iny], (float)values[inz]));
                    if (hasUvs) poly.UVs!.Add(new Vector2((float)values[iu], (float)values[iv]));
                }
                else if (face && list is { Length: >= 3 })
                {
                    if (Array.Exists(list, x => x < 0 || x >= poly.Positions.Count))
                    {
                        warnings?.Add($"PLY face {i} refers to a missing vertex, skipped.");
                        continue;
                    }
                    poly.FacePositions.Add(list);
                    poly.FaceNormals!.Add(hasNormals ? list : Array.Empty<int>());
                    poly.FaceUVs!.Add(hasUvs ? list : Array.Empty<int>());
                }
            }
            if (vertex && (ix < 0 || iy < 0 || iz < 0))
            {
                warnings?.Add("PLY vertex element has no x, y, z.");
                return null;
            }
        }
        return Finish();

        ImportedMesh? Finish()
        {
            if (poly.FaceCount == 0) { warnings?.Add("PLY file has no faces."); return null; }
            poly.FaceMaterials = null;
            return imported;
        }
    }

    // Header lines are ASCII, read byte by byte so the binary body that
    // follows starts exactly after end_header.
    private static string? ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            if (b == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)b);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>Scalars of the body, in the file's format.</summary>
    private sealed class ValueReader
    {
        private readonly Stream _s;
        private readonly Format _format;
        private readonly byte[] _buf = new byte[8];
        private readonly BufferedStream? _buffered;
        private string[] _tokens = Array.Empty<string>();
        private int _next;
        public bool Ok { get; private set; } = true;

        public ValueReader(Stream s, Format format)
        {
            _format = format;
            _buffered = new BufferedStream(s, 1 << 16);
            _s = _buffered;
        }

        public double Read(string type)
        {
            if (_format == Format.Ascii)
            {
                while (_next >= _tokens.Length)
                {
                    string? line = ReadLine(_s);
                    if (line == null) { Ok = false; return 0; }
                    _tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    _next = 0;
                }
                return double.Parse(_tokens[_next++], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            int size = type switch
            {
                "char" or "int8" or "uchar" or "uint8" => 1,
                "short" or "int16" or "ushort" or "uint16" => 2,
                "int" or "int32" or "uint" or "uint32" or "float" or "float32" => 4,
                "double" or "float64" => 8,
                _ => 4,
            };
            if (_s.ReadAtLeast(_buf.AsSpan(0, size), size, throwOnEndOfStream: false) < size) { Ok = false; return 0; }
            var span = _buf.AsSpan(0, size);
            bool little = _format == Format.BinaryLittle;
            return type switch
            {
                "char" or "int8" => (sbyte)span[0],
                "uchar" or "uint8" => span[0],
                "short" or "int16" => little ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span),
                "ushort" or "uint16" => little ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span),
                "int" or "int32" => little ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span),
                "uint" or "uint32" => little ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span),
                "double" or "float64" => little ? BinaryPrimitives.ReadDoubleLittleEndian(span) : BinaryPrimitives.ReadDoubleBigEndian(span),
                _ => little ? BinaryPrimitives.ReadSingleLittleEndian(span) : BinaryPrimitives.ReadSingleBigEndian(span),
            };
        }
    }
}
