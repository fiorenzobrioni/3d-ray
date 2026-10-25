using System.Numerics;
using System.Text;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Scene;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The model importers (PIANO-REVIEW.md Phase 2): OBJ with MTL, PLY and
/// glTF 2.0 read into the same pipeline, checked on small files written here.
/// </summary>
public sealed class MeshImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rt-import-" + Guid.NewGuid().ToString("N"));
    private static readonly IMaterial Grey = new Lambertian(new Vector3(0.5f));

    public MeshImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string text) { var p = Path.Combine(_dir, name); File.WriteAllText(p, text); return p; }
    private string Write(string name, byte[] data) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, data); return p; }

    private static ImportResult Build(string path, IMaterial? entity = null)
    {
        var warnings = new List<string>();
        var imported = MeshImporter.Read(path, warnings);
        Assert.True(imported != null, string.Join("; ", warnings));
        var result = MeshImporter.Build(imported!, entity, null, Grey, SubdivisionOptions.Disabled, null, 0, warnings);
        Assert.NotNull(result);
        return result!;
    }

    private static List<IHittable> Triangles(IHittable h) => h switch
    {
        Mesh m => m.Triangles.ToList(),
        Group g => g.Children.SelectMany(Triangles).ToList(),
        _ => throw new InvalidOperationException(h.GetType().Name),
    };

    private static (Vector3 A, Vector3 B, Vector3 C) Corners(IHittable t) => t switch
    {
        Triangle f => (f.V0, f.V1, f.V2),
        SmoothTriangle s => (s.V0, s.V1, s.V2),
        _ => throw new InvalidOperationException(),
    };

    private static float Area(IHittable t)
    {
        var (a, b, c) = Corners(t);
        return 0.5f * Vector3.Cross(b - a, c - a).Length();
    }

    // ── OBJ ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Obj_ConcavePolygon_IsCoveredExactly()
    {
        // An L: area 3. A fan from its first corner reaches outside it.
        string obj = Write("l.obj", "v 2 0 0\nv 2 1 0\nv 1 1 0\nv 1 2 0\nv 0 2 0\nv 0 0 0\nf 1 2 3 4 5 6\n");
        var tris = Triangles(Build(obj).Hittable);
        Assert.Equal(4, tris.Count);
        Assert.Equal(3f, tris.Sum(Area), 4);
        foreach (var t in tris)
        {
            var (a, b, c) = Corners(t);
            Assert.True(Vector3.Cross(b - a, c - a).Z > 0f, "winding kept");
        }
    }

    [Fact]
    public void Obj_ConcaveQuad_IsCutThroughItsReflexCorner()
    {
        // A dart: corner 3 is reflex, only the diagonal 1-3 stays inside.
        string obj = Write("dart.obj", "v 0 0 0\nv 2 1 0\nv 0 2 0\nv 0.5 1 0\nf 1 2 3 4\n");
        var tris = Triangles(Build(obj).Hittable);
        const float area = 1.5f; // shoelace
        Assert.Equal(area, tris.Sum(Area), 4);
    }

    [Fact]
    public void Obj_MtlMaterials_GiveOneMeshPerMaterial()
    {
        Write("two.mtl", "newmtl red\nKd 0.8 0.1 0.1\nNs 98\nKs 0.5 0.5 0.5\n\nnewmtl blue\nKd 0.1 0.1 0.8\nPr 0.3\nPm 1\n");
        string obj = Write("two.obj", "mtllib two.mtl\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 2 0 0\nv 2 1 0\n" +
                                      "usemtl red\nf 1 2 3 4\nusemtl blue\nf 2 5 6 3\n");
        var result = Build(obj);
        var group = Assert.IsType<Group>(result.Hittable);
        var meshes = group.Children.Cast<Mesh>().ToList();
        Assert.Equal(2, meshes.Count);
        var red = (OpenPbr)meshes[0].Material;
        var blue = (OpenPbr)meshes[1].Material;
        Assert.Equal(new Vector3(0.8f, 0.1f, 0.1f), red.Parameters.BaseColor.Value(0, 0, Vector3.Zero, 0));
        Assert.Equal(MathF.Pow(2f / 100f, 0.25f), red.Parameters.SpecularRoughness.Value(0, 0, Vector3.Zero, 0), 5);
        Assert.Equal(0.3f, blue.Parameters.SpecularRoughness.Value(0, 0, Vector3.Zero, 0), 5);
        Assert.Equal(1f, blue.Parameters.BaseMetalness.Value(0, 0, Vector3.Zero, 0), 5);

        // The entity's material covers everything: one mesh.
        Assert.IsType<Mesh>(Build(obj, Grey).Hittable);
    }

    [Fact]
    public void Obj_SmoothingGroups_ShareNormalsInAGroup_FlatOtherwise()
    {
        // Two faces folded at 90° along x = 1.
        const string verts = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 1 0 -1\nv 1 1 -1\n";
        string smooth = Write("s.obj", verts + "s 1\nf 1 2 3 4\nf 2 5 6 3\n");
        string flat = Write("f.obj", verts + "s off\nf 1 2 3 4\nf 2 5 6 3\n");

        var st = Triangles(Build(smooth).Hittable).Cast<SmoothTriangle>().ToList();
        // On the fold the normal is the mean of the two faces'.
        var fold = Vector3.Normalize(new Vector3(1, 0, 1));
        foreach (var t in st)
            foreach (var (v, n) in new[] { (t.V0, t.N0), (t.V1, t.N1), (t.V2, t.N2) })
                if (MathF.Abs(v.X - 1f) < 1e-6f && v.Z == 0f) Assert.True(Vector3.Dot(n, fold) > 0.9999f);

        Assert.All(Triangles(Build(flat).Hittable), t => Assert.IsType<Triangle>(t));
    }

    [Fact]
    public void Obj_UvsWithoutNormals_AreKept()
    {
        string obj = Write("uv.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0.25 0.5\nvt 0.75 0.5\nvt 0.25 1\nf 1/1 2/2 3/3\n");
        var t = Assert.IsType<SmoothTriangle>(Triangles(Build(obj).Hittable).Single());
        Assert.True(t.HasTextureUvs);
        Assert.Equal(new Vector2(0.75f, 0.5f), t.UV1);
        Assert.Equal(Vector3.UnitZ, t.N0);
    }

    // ── PLY ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ascii")]
    [InlineData("binary_little_endian")]
    [InlineData("binary_big_endian")]
    public void Ply_ReadsEveryEncoding(string format)
    {
        var verts = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0), new Vector3(0.5f, 2, 0) };
        var header = $"ply\nformat {format} 1.0\ncomment test\nelement vertex {verts.Length}\nproperty float x\nproperty float y\nproperty float z\n" +
                     "property uchar red\nproperty float s\nproperty float t\n" +
                     "element face 2\nproperty list uchar int vertex_indices\nelement extra 1\nproperty double weight\nend_header\n";
        var body = new MemoryStream();
        if (format == "ascii")
        {
            var sb = new StringBuilder();
            foreach (var v in verts) sb.Append($"{v.X} {v.Y} {v.Z} 200 {v.X} {v.Y}\n");
            sb.Append("4 0 1 2 3\n3 3 2 4\n0.5\n");
            body.Write(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        else
        {
            bool big = format.Contains("big");
            void F(float x) { var b = BitConverter.GetBytes(x); if (big) Array.Reverse(b); body.Write(b); }
            void I(int x) { var b = BitConverter.GetBytes(x); if (big) Array.Reverse(b); body.Write(b); }
            foreach (var v in verts) { F(v.X); F(v.Y); F(v.Z); body.WriteByte(200); F(v.X); F(v.Y); }
            body.WriteByte(4); I(0); I(1); I(2); I(3);
            body.WriteByte(3); I(3); I(2); I(4);
            var d = BitConverter.GetBytes(0.5); if (big) Array.Reverse(d); body.Write(d);
        }
        string path = Write($"m-{format}.ply", Encoding.ASCII.GetBytes(header).Concat(body.ToArray()).ToArray());
        var result = Build(path);
        var tris = Triangles(result.Hittable);
        Assert.Equal(3, tris.Count);
        Assert.Equal(1.5f, tris.Sum(Area), 5);
        Assert.Equal(5, result.VertexCount);
        Assert.All(tris, t => Assert.True(((SmoothTriangle)t).HasTextureUvs));
    }

    // ── glTF ─────────────────────────────────────────────────────────────────

    // A unit quad in the XY plane (two triangles, normals +Z, UVs with glTF's
    // V downwards), material 0, in node 1 below node 0.
    private static (string Json, byte[] Bin) Quad(string nodeTransform, string material)
    {
        float[] pos = { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 };
        float[] nrm = { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 };
        float[] uv = { 0, 1, 1, 1, 1, 0, 0, 0 };
        ushort[] idx = { 0, 1, 2, 0, 2, 3 };
        var bin = new MemoryStream();
        foreach (var f in pos.Concat(nrm).Concat(uv)) bin.Write(BitConverter.GetBytes(f));
        foreach (var i in idx) bin.Write(BitConverter.GetBytes(i));
        string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
            "\"nodes\":[{\"children\":[1]," + nodeTransform + "},{\"mesh\":0}]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"TEXCOORD_0\":2},\"indices\":3,\"material\":0}]}]," +
            "\"materials\":[" + material + "]," +
            "\"buffers\":[{BUFFER\"byteLength\":" + bin.Length + "}]," +
            "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":48,\"byteLength\":48}," +
            "{\"buffer\":0,\"byteOffset\":96,\"byteLength\":32},{\"buffer\":0,\"byteOffset\":128,\"byteLength\":12}]," +
            "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]}," +
            "{\"bufferView\":1,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"}," +
            "{\"bufferView\":2,\"componentType\":5126,\"count\":4,\"type\":\"VEC2\"}," +
            "{\"bufferView\":3,\"componentType\":5123,\"count\":6,\"type\":\"SCALAR\"}]}";
        return (json, bin.ToArray());
    }

    private string Gltf(string name, string nodeTransform, string material)
    {
        var (json, bin) = Quad(nodeTransform, material);
        return Write(name, json.Replace("{BUFFER", "{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String(bin) + "\","));
    }

    private string Glb(string name, string nodeTransform, string material)
    {
        var (json, bin) = Quad(nodeTransform, material);
        var jb = Encoding.UTF8.GetBytes(json.Replace("{BUFFER", "{"));
        jb = jb.Concat(Enumerable.Repeat((byte)' ', (4 - jb.Length % 4) % 4)).ToArray();
        var bb = bin.Concat(new byte[(4 - bin.Length % 4) % 4]).ToArray();
        var file = new MemoryStream();
        void U(uint x) => file.Write(BitConverter.GetBytes(x));
        U(0x46546C67); U(2); U((uint)(12 + 8 + jb.Length + 8 + bb.Length));
        U((uint)jb.Length); U(0x4E4F534A); file.Write(jb);
        U((uint)bb.Length); U(0x004E4942); file.Write(bb);
        return Write(name, file.ToArray());
    }

    private const string Plain = "{\"name\":\"paint\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.2,0.4,0.6,1],\"metallicFactor\":0.25,\"roughnessFactor\":0.75}}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gltf_ReadsGeometryMaterialAndTransforms(bool binary)
    {
        // Translated by (5, 0, 0) and mirrored in X: the winding flips, so the
        // faces still face the file's +Z normals.
        const string node = "\"translation\":[5,0,0],\"scale\":[-2,1,1]";
        string path = binary ? Glb("q.glb", node, Plain) : Gltf("q.gltf", node, Plain);
        var result = Build(path);
        var tris = Triangles(result.Hittable).Cast<SmoothTriangle>().ToList();
        Assert.Equal(2, tris.Count);
        Assert.Equal(4, result.VertexCount); // welded
        Assert.Equal(2f, tris.Sum(Area), 5); // 2 × 1
        foreach (var t in tris)
        {
            Assert.InRange(new[] { t.V0.X, t.V1.X, t.V2.X }.Min(), 3f - 1e-5f, 5f);
            Vector3 face = Vector3.Cross(t.V1 - t.V0, t.V2 - t.V0);
            Assert.True(Vector3.Dot(face, t.N0) > 0f, "winding agrees with the normals");
            Assert.Equal(Vector3.UnitZ, t.N0);
        }
        // glTF's (0, 1) at the origin corner is the renderer's (0, 0).
        var corner = tris.SelectMany(t => new[] { (t.V0, t.UV0), (t.V1, t.UV1), (t.V2, t.UV2) }).First(c => c.Item1 == new Vector3(5, 0, 0));
        Assert.Equal(Vector2.Zero, corner.Item2);

        var mat = Assert.IsType<OpenPbr>(((Mesh)result.Hittable).Material);
        Assert.Equal(new Vector3(0.2f, 0.4f, 0.6f), mat.Parameters.BaseColor.Value(0, 0, Vector3.Zero, 0));
        Assert.Equal(0.25f, mat.Parameters.BaseMetalness.Value(0, 0, Vector3.Zero, 0));
        Assert.Equal(0.75f, mat.Parameters.SpecularRoughness.Value(0, 0, Vector3.Zero, 0));
    }

    [Fact]
    public void Gltf_TexturesWithAlphaMaskAndTransform()
    {
        // A 2×2 image: left column red, right column green; alpha 0 on the
        // bottom row (in the image, row 1).
        using var image = new Image<Rgba32>(2, 2);
        image[0, 0] = new Rgba32(255, 0, 0, 255); image[1, 0] = new Rgba32(0, 255, 0, 255);
        image[0, 1] = new Rgba32(255, 0, 0, 0);   image[1, 1] = new Rgba32(0, 255, 0, 0);
        var png = new MemoryStream();
        image.SaveAsPng(png);
        string uri = "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
        // The transform swaps left and right: u' = 1 - u... as offset 0.5 on a 2-texel image.
        string material = "{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0,\"extensions\":{\"KHR_texture_transform\":{\"offset\":[0.5,0]}}}},\"alphaMode\":\"MASK\"}";
        string path = Gltf("t.gltf", "\"translation\":[0,0,0]", material);
        string json = File.ReadAllText(path).TrimEnd('}') +
                      ",\"textures\":[{\"source\":0}],\"images\":[{\"uri\":\"" + uri + "\"}]}";
        File.WriteAllText(path, json);

        var mat = (OpenPbr)((Mesh)Build(path).Hittable).Material;
        // Renderer UV (0.25, 0.75): glTF (0.25, 0.25), shifted to (0.75, 0.25): the top-right texel, green.
        Vector3 c = mat.Parameters.BaseColor.Value(0.25f, 0.75f, Vector3.Zero, 0);
        Assert.True(c.Y > 0.9f && c.X < 0.1f, $"colour {c}");
        // Renderer V 0.25 is glTF's bottom row: alpha 0, cut out.
        Assert.Equal(0f, mat.Parameters.GeometryOpacity.Value(0.25f, 0.25f, Vector3.Zero, 0));
        Assert.Equal(1f, mat.Parameters.GeometryOpacity.Value(0.25f, 0.75f, Vector3.Zero, 0));
    }

    [Fact]
    public void Gltf_Transmission_IsThinWalledWithoutVolume()
    {
        string glass = "{\"pbrMetallicRoughness\":{\"roughnessFactor\":0},\"extensions\":{\"KHR_materials_transmission\":{\"transmissionFactor\":1},\"KHR_materials_ior\":{\"ior\":1.33}}}";
        var mat = (OpenPbr)((Mesh)Build(Gltf("g.gltf", "\"translation\":[0,0,0]", glass)).Hittable).Material;
        Assert.True(mat.Parameters.ThinWalled);
        Assert.Equal(1.33f, mat.Parameters.SpecularIor.Value(0, 0, Vector3.Zero, 0), 5);
        string solid = glass.Replace("}}}", "},\"KHR_materials_volume\":{\"thicknessFactor\":0.1}}}");
        var mat2 = (OpenPbr)((Mesh)Build(Gltf("g2.gltf", "\"translation\":[0,0,0]", solid)).Hittable).Material;
        Assert.False(mat2.Parameters.ThinWalled);
    }
}
