using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;

namespace RayTracer.Scene;

/// <summary>
/// glTF 2.0 reader (PIANO-REVIEW.md Phase 2): <c>.gltf</c> (JSON with external
/// or data-URI buffers) and <c>.glb</c> (binary container). The default scene's
/// node hierarchy is flattened into one mesh in the entity's space (node
/// matrices or TRS; a mirroring transform reverses the winding, as the
/// specification asks); positions shared by a mesh's primitives are welded,
/// so subdivision sees one surface across UV seams.
///
/// <para>Geometry: triangles, strips and fans; POSITION, NORMAL, TEXCOORD_0;
/// any accessor type, normalised integers (KHR_mesh_quantization) and sparse
/// accessors. glTF's texture V runs downwards, the renderer's upwards: v is
/// read as 1 − v. Points and lines, skins and morph targets (the rest pose is
/// used), vertex colours and Draco/meshopt compression are not read.</para>
///
/// <para>Materials: metallic-roughness to OpenPBR (base colour and alpha,
/// metalness and roughness from the B and G channels, normal map with its
/// scale, emission), alpha modes OPAQUE, MASK (a cut-out at alphaCutoff) and
/// BLEND (stochastic opacity), and the extensions KHR_materials_emissive_strength,
/// _ior, _transmission, _volume (else transmission is thin-walled, as glTF
/// defines it), _clearcoat, _sheen, _specular, _iridescence, _anisotropy,
/// _unlit, KHR_texture_transform and EXT_texture_webp. Occlusion maps are
/// ignored: the path tracer computes occlusion.</para>
/// </summary>
public static class GltfLoader
{
    public static ImportedMesh? Read(string path, List<string>? warnings)
    {
        try { return new Reader(path, warnings).Run(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException
                                       or IndexOutOfRangeException or ArgumentException or FormatException)
        {
            warnings?.Add($"glTF file cannot be read: {ex.Message}");
            return null;
        }
    }

    private sealed class Reader
    {
        private readonly string _dir;
        private readonly List<string>? _warnings;
        private readonly JsonElement _root;
        private readonly byte[]? _glbBin;
        private readonly List<byte[]> _buffers = new();
        private readonly Dictionary<(int, TextureColorSpace, ImageChannels), ImageTexture?> _images = new();
        private readonly HashSet<string> _warned = new();
        private readonly JsonDocument _doc;

        public Reader(string path, List<string>? warnings)
        {
            _dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            _warnings = warnings;
            byte[] file = File.ReadAllBytes(path);
            ReadOnlyMemory<byte> json = file;
            if (file.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(file) == 0x46546C67) // "glTF"
            {
                int offset = 12;
                json = default;
                while (offset + 8 <= file.Length)
                {
                    int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset));
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset + 4));
                    var chunk = new ReadOnlyMemory<byte>(file, offset + 8, length);
                    if (type == 0x4E4F534A) json = chunk;              // JSON
                    else if (type == 0x004E4942) _glbBin = chunk.ToArray(); // BIN
                    offset += 8 + length;
                }
                if (json.IsEmpty) throw new InvalidDataException("GLB without a JSON chunk");
            }
            _doc = JsonDocument.Parse(json);
            _root = _doc.RootElement;
        }

        private void Warn(string message)
        {
            if (_warned.Add(message)) _warnings?.Add(message);
        }

        public ImportedMesh? Run()
        {
            using var _ = _doc;
            if (_root.TryGetProperty("extensionsRequired", out var required))
                foreach (var ext in required.EnumerateArray())
                {
                    string name = ext.GetString() ?? "";
                    if (name is "KHR_draco_mesh_compression" or "EXT_meshopt_compression")
                    {
                        Warn($"glTF requires '{name}', which is not supported.");
                        return null;
                    }
                }

            if (_root.TryGetProperty("buffers", out var buffers))
                foreach (var b in buffers.EnumerateArray())
                    _buffers.Add(b.TryGetProperty("uri", out var uri) ? Resolve(uri.GetString()!) : _glbBin
                                 ?? throw new InvalidDataException("buffer without data"));

            var imported = new ImportedMesh();
            var materialSlots = new Dictionary<int, int>();
            int Slot(int material)
            {
                if (material < 0) return imported.Slot("");
                if (materialSlots.TryGetValue(material, out int s)) return s;
                var m = _root.GetProperty("materials")[material];
                string name = m.TryGetProperty("name", out var n) ? n.GetString() ?? $"material_{material}" : $"material_{material}";
                if (imported.SlotNames.Contains(name)) name = $"{name}#{material}";
                return materialSlots[material] = imported.Slot(name, BuildMaterial(m));
            }

            // The default scene, or every root node when there is none.
            var roots = new List<int>();
            if (_root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0)
            {
                int scene = _root.TryGetProperty("scene", out var si) ? si.GetInt32() : 0;
                if (scenes[scene].TryGetProperty("nodes", out var nodes))
                    foreach (var n in nodes.EnumerateArray()) roots.Add(n.GetInt32());
            }
            else if (_root.TryGetProperty("nodes", out var all))
            {
                var children = new HashSet<int>();
                foreach (var n in all.EnumerateArray())
                    if (n.TryGetProperty("children", out var c)) foreach (var x in c.EnumerateArray()) children.Add(x.GetInt32());
                for (int i = 0; i < all.GetArrayLength(); i++) if (!children.Contains(i)) roots.Add(i);
            }
            foreach (int r in roots) Visit(r, Matrix4x4.Identity, imported, Slot, 0);

            if (imported.FaceCount == 0) { Warn("glTF scene has no triangles."); return null; }
            if (imported.SlotNames.Count == 1 && imported.SlotMaterials[0] == null) imported.Poly.FaceMaterials = null;
            return imported;
        }

        private void Visit(int nodeIndex, Matrix4x4 parent, ImportedMesh imported, Func<int, int> slot, int depth)
        {
            if (depth > 256) throw new InvalidDataException("node hierarchy too deep (a cycle?)");
            var node = _root.GetProperty("nodes")[nodeIndex];
            Matrix4x4 local = Matrix4x4.Identity;
            if (node.TryGetProperty("matrix", out var mtx))
            {
                // Column-major in the file: read in order it is the transpose,
                // which is System.Numerics' row-vector form.
                var m = mtx.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                local = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                                      m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            }
            else
            {
                Vector3 t = node.TryGetProperty("translation", out var tr) ? Vec3(tr) : Vector3.Zero;
                Vector3 s = node.TryGetProperty("scale", out var sc) ? Vec3(sc) : Vector3.One;
                Quaternion q = Quaternion.Identity;
                if (node.TryGetProperty("rotation", out var rot))
                {
                    var r = rot.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    q = Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]));
                }
                local = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
            }
            Matrix4x4 world = local * parent;

            if (node.TryGetProperty("mesh", out var meshIndex))
                AddMesh(_root.GetProperty("meshes")[meshIndex.GetInt32()], world, imported, slot);
            if (node.TryGetProperty("children", out var children))
                foreach (var c in children.EnumerateArray()) Visit(c.GetInt32(), world, imported, slot, depth + 1);
        }

        private void AddMesh(JsonElement mesh, Matrix4x4 world, ImportedMesh imported, Func<int, int> slot)
        {
            var poly = imported.Poly;
            Matrix4x4.Invert(world, out var inverse);
            Matrix4x4 normalMatrix = Matrix4x4.Transpose(inverse);
            bool mirrored = world.GetDeterminant() < 0f;
            var weld = new Dictionary<Vector3, int>();

            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                int mode = prim.TryGetProperty("mode", out var md) ? md.GetInt32() : 4;
                if (mode is not (4 or 5 or 6)) { Warn("glTF points and lines are not read."); continue; }
                var attributes = prim.GetProperty("attributes");
                if (!attributes.TryGetProperty("POSITION", out var posAcc)) continue;
                if (attributes.TryGetProperty("COLOR_0", out _)) Warn("glTF vertex colours are not read.");
                if (prim.TryGetProperty("targets", out _)) Warn("glTF morph targets are ignored (rest pose).");

                float[] pos = ReadFloats(posAcc.GetInt32(), out _);
                int count = pos.Length / 3;
                float[]? nrm = attributes.TryGetProperty("NORMAL", out var na) ? ReadFloats(na.GetInt32(), out _) : null;
                float[]? uv = attributes.TryGetProperty("TEXCOORD_0", out var ta) ? ReadFloats(ta.GetInt32(), out _) : null;
                int[] indices = prim.TryGetProperty("indices", out var ia)
                    ? ReadInts(ia.GetInt32())
                    : Enumerable.Range(0, count).ToArray();

                var posIndex = new int[count];
                int normalBase = poly.Normals!.Count, uvBase = poly.UVs!.Count;
                for (int i = 0; i < count; i++)
                {
                    var p = Vector3.Transform(new Vector3(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]), world);
                    if (!weld.TryGetValue(p, out int pi)) { pi = poly.Positions.Count; poly.Positions.Add(p); weld[p] = pi; }
                    posIndex[i] = pi;
                    if (nrm != null)
                    {
                        var n = Vector3.TransformNormal(new Vector3(nrm[3 * i], nrm[3 * i + 1], nrm[3 * i + 2]), normalMatrix);
                        float len = n.Length();
                        poly.Normals.Add(len > 0f ? n / len : Vector3.Zero);
                    }
                    if (uv != null) poly.UVs.Add(new Vector2(uv[2 * i], 1f - uv[2 * i + 1]));
                }

                int material = prim.TryGetProperty("material", out var mi) ? mi.GetInt32() : -1;
                int s = slot(material);
                void Tri(int a, int b, int c)
                {
                    if (mirrored) (b, c) = (c, b);
                    poly.FacePositions.Add(new[] { posIndex[a], posIndex[b], posIndex[c] });
                    poly.FaceNormals!.Add(nrm != null ? new[] { normalBase + a, normalBase + b, normalBase + c } : Array.Empty<int>());
                    poly.FaceUVs!.Add(uv != null ? new[] { uvBase + a, uvBase + b, uvBase + c } : Array.Empty<int>());
                    poly.FaceMaterials!.Add(s);
                }
                switch (mode)
                {
                    case 4:
                        for (int k = 0; k + 2 < indices.Length; k += 3) Tri(indices[k], indices[k + 1], indices[k + 2]);
                        break;
                    case 5:
                        for (int k = 0; k + 2 < indices.Length; k++)
                            if (k % 2 == 0) Tri(indices[k], indices[k + 1], indices[k + 2]);
                            else Tri(indices[k + 1], indices[k], indices[k + 2]);
                        break;
                    case 6:
                        for (int k = 1; k + 1 < indices.Length; k++) Tri(indices[0], indices[k], indices[k + 1]);
                        break;
                }
            }
        }

        // ── Buffers and accessors ────────────────────────────────────────────

        private byte[] Resolve(string uri)
        {
            if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                int comma = uri.IndexOf(',');
                return Convert.FromBase64String(uri[(comma + 1)..]);
            }
            return File.ReadAllBytes(Path.Combine(_dir, Uri.UnescapeDataString(uri)));
        }

        private ReadOnlySpan<byte> View(int viewIndex, out int stride)
        {
            var view = _root.GetProperty("bufferViews")[viewIndex];
            byte[] buffer = _buffers[view.GetProperty("buffer").GetInt32()];
            int offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            int length = view.GetProperty("byteLength").GetInt32();
            stride = view.TryGetProperty("byteStride", out var st) ? st.GetInt32() : 0;
            return buffer.AsSpan(offset, length);
        }

        private static int Components(string type) => type switch
        {
            "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16,
            _ => throw new InvalidDataException($"accessor type '{type}'"),
        };

        private static int ComponentSize(int componentType) => componentType switch
        {
            5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
            _ => throw new InvalidDataException($"component type {componentType}"),
        };

        private static float Component(ReadOnlySpan<byte> data, int componentType, bool normalized)
        {
            return componentType switch
            {
                5126 => BinaryPrimitives.ReadSingleLittleEndian(data),
                5120 => normalized ? MathF.Max((sbyte)data[0] / 127f, -1f) : (sbyte)data[0],
                5121 => normalized ? data[0] / 255f : data[0],
                5122 => normalized ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(data) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(data),
                5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(data) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(data),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(data),
            };
        }

        private float[] ReadFloats(int accessorIndex, out int components)
        {
            var acc = _root.GetProperty("accessors")[accessorIndex];
            components = Components(acc.GetProperty("type").GetString()!);
            int count = acc.GetProperty("count").GetInt32();
            int ct = acc.GetProperty("componentType").GetInt32();
            bool normalized = acc.TryGetProperty("normalized", out var nz) && nz.GetBoolean();
            int size = ComponentSize(ct);
            var result = new float[count * components];
            if (acc.TryGetProperty("bufferView", out var bv))
            {
                var data = View(bv.GetInt32(), out int stride);
                int offset = acc.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                if (stride == 0) stride = size * components;
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < components; c++)
                        result[i * components + c] = Component(data[(offset + i * stride + c * size)..], ct, normalized);
            }
            if (acc.TryGetProperty("sparse", out var sparse))
            {
                int n = sparse.GetProperty("count").GetInt32();
                var si = sparse.GetProperty("indices");
                var sv = sparse.GetProperty("values");
                var idata = View(si.GetProperty("bufferView").GetInt32(), out _)
                    [(si.TryGetProperty("byteOffset", out var io) ? io.GetInt32() : 0)..];
                var vdata = View(sv.GetProperty("bufferView").GetInt32(), out _)
                    [(sv.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0)..];
                int ict = si.GetProperty("componentType").GetInt32(), isz = ComponentSize(ict);
                for (int k = 0; k < n; k++)
                {
                    int target = (int)Component(idata[(k * isz)..], ict, false);
                    for (int c = 0; c < components; c++)
                        result[target * components + c] = Component(vdata[((k * components + c) * size)..], ct, normalized);
                }
            }
            return result;
        }

        private int[] ReadInts(int accessorIndex)
        {
            var acc = _root.GetProperty("accessors")[accessorIndex];
            int count = acc.GetProperty("count").GetInt32();
            int ct = acc.GetProperty("componentType").GetInt32();
            int size = ComponentSize(ct);
            var data = View(acc.GetProperty("bufferView").GetInt32(), out int stride);
            int offset = acc.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            if (stride == 0) stride = size;
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                var d = data[(offset + i * stride)..];
                result[i] = ct switch
                {
                    5121 => d[0],
                    5123 => BinaryPrimitives.ReadUInt16LittleEndian(d),
                    _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(d),
                };
            }
            return result;
        }

        // ── Textures ─────────────────────────────────────────────────────────

        private ImageTexture? Image(int imageIndex, TextureColorSpace cs, ImageChannels ch)
        {
            var key = (imageIndex, cs, ch);
            if (_images.TryGetValue(key, out var cached)) return cached;
            ImageTexture? tex = null;
            try
            {
                var image = _root.GetProperty("images")[imageIndex];
                byte[] bytes;
                if (image.TryGetProperty("bufferView", out var bv)) bytes = View(bv.GetInt32(), out _).ToArray();
                else
                {
                    string uri = image.GetProperty("uri").GetString()!;
                    if (!uri.StartsWith("data:", StringComparison.Ordinal))
                    {
                        string file = Path.Combine(_dir, Uri.UnescapeDataString(uri));
                        if (Path.GetExtension(file).ToLowerInvariant() is ".exr" or ".hdr")
                        {
                            tex = new ImageTexture(file, 1f, 1f, cs, ch);
                            return _images[key] = tex;
                        }
                    }
                    bytes = Resolve(uri);
                }
                tex = new ImageTexture(bytes, cs, ch);
            }
            catch (Exception ex)
            {
                Warn($"glTF image {imageIndex} cannot be read ({ex.Message}).");
            }
            return _images[key] = tex;
        }

        /// <summary>The texture a textureInfo refers to, with its UV transform.</summary>
        private ITexture? Texture(JsonElement owner, string property, TextureColorSpace cs,
                                  ImageChannels ch = ImageChannels.Rgb)
        {
            if (!owner.TryGetProperty(property, out var info)) return null;
            var texture = _root.GetProperty("textures")[info.GetProperty("index").GetInt32()];
            int source = -1;
            if (texture.TryGetProperty("source", out var src)) source = src.GetInt32();
            if (texture.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("EXT_texture_webp", out var webp))
                source = webp.GetProperty("source").GetInt32();
            if (source < 0) { Warn("glTF texture without a readable source (KTX2/Basis is not supported)."); return null; }
            if (info.TryGetProperty("texCoord", out var tc) && tc.GetInt32() != 0)
                Warn("glTF textures on TEXCOORD_1 or above use TEXCOORD_0.");
            ITexture? image = Image(source, cs, ch);
            if (image == null) return null;
            if (info.TryGetProperty("extensions", out var iext) && iext.TryGetProperty("KHR_texture_transform", out var xf))
            {
                Vector2 offset = xf.TryGetProperty("offset", out var o) ? Vec2(o) : Vector2.Zero;
                Vector2 scale = xf.TryGetProperty("scale", out var sc) ? Vec2(sc) : Vector2.One;
                float rotation = xf.TryGetProperty("rotation", out var r) ? r.GetSingle() : 0f;
                image = new UvTransformTexture(image, offset, rotation, scale);
            }
            return image;
        }

        // ── Materials ────────────────────────────────────────────────────────

        private IMaterial BuildMaterial(JsonElement m)
        {
            JsonElement pbr = m.TryGetProperty("pbrMetallicRoughness", out var p) ? p : default;
            JsonElement ext = m.TryGetProperty("extensions", out var e) ? e : default;
            bool Has(JsonElement el, string name, out JsonElement value)
            {
                value = default;
                return el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out value);
            }
            float F(JsonElement el, string name, float fallback) => Has(el, name, out var v) ? v.GetSingle() : fallback;

            // Base colour and alpha.
            Vector4 baseFactor = Has(pbr, "baseColorFactor", out var bcf) ? Vec4(bcf) : Vector4.One;
            Vector3 baseRgb = new(baseFactor.X, baseFactor.Y, baseFactor.Z);
            ITexture? baseTex = pbr.ValueKind == JsonValueKind.Object ? Texture(pbr, "baseColorTexture", TextureColorSpace.Srgb) : null;
            ITexture baseColor = baseTex != null ? Scaled(baseTex, baseRgb) : new SolidColor(baseRgb);

            string alphaMode = Has(m, "alphaMode", out var am) ? am.GetString() ?? "OPAQUE" : "OPAQUE";
            FloatTexture opacity = 1f;
            if (alphaMode is "MASK" or "BLEND")
            {
                float cutoff = F(m, "alphaCutoff", 0.5f);
                ITexture? alphaTex = pbr.ValueKind == JsonValueKind.Object
                    ? Texture(pbr, "baseColorTexture", TextureColorSpace.Linear, ImageChannels.Alpha) : null;
                float fa = baseFactor.W;
                if (alphaMode == "MASK")
                    opacity = alphaTex != null
                        ? new FloatTexture(new DerivedTexture(alphaTex, c => new Vector3(c.X * fa >= cutoff ? 1f : 0f)))
                        : fa >= cutoff ? 1f : 0f;
                else
                    opacity = alphaTex != null ? new FloatTexture(Scaled(alphaTex, new Vector3(fa))) : fa;
            }

            // Metalness (B) and roughness (G).
            float metalFactor = F(pbr, "metallicFactor", 1f), roughFactor = F(pbr, "roughnessFactor", 1f);
            ITexture? mr = pbr.ValueKind == JsonValueKind.Object ? Texture(pbr, "metallicRoughnessTexture", TextureColorSpace.Linear) : null;
            FloatTexture metal = mr != null ? new FloatTexture(new DerivedTexture(mr, c => new Vector3(c.Z * metalFactor))) : metalFactor;
            FloatTexture rough = mr != null ? new FloatTexture(new DerivedTexture(mr, c => new Vector3(c.Y * roughFactor))) : roughFactor;

            // Emission.
            Vector3 emissiveFactor = Has(m, "emissiveFactor", out var ef) ? Vec3(ef) : Vector3.Zero;
            float strength = Has(ext, "KHR_materials_emissive_strength", out var es) ? F(es, "emissiveStrength", 1f) : 1f;
            ITexture? emTex = Texture(m, "emissiveTexture", TextureColorSpace.Srgb);
            bool emits = emissiveFactor != Vector3.Zero;
            ITexture emission = emTex != null ? Scaled(emTex, emissiveFactor) : new SolidColor(emissiveFactor);

            float ior = Has(ext, "KHR_materials_ior", out var iorExt) ? F(iorExt, "ior", 1.5f) : 1.5f;
            if (ior < 1f) ior = 1.5f;

            // Transmission and volume.
            FloatTexture transmission = 0f;
            bool thin = true;
            ITexture transColor = new SolidColor(Vector3.One);
            float transDepth = 0f;
            if (Has(ext, "KHR_materials_transmission", out var tr))
            {
                float tf = F(tr, "transmissionFactor", 0f);
                var tt = Texture(tr, "transmissionTexture", TextureColorSpace.Linear);
                transmission = tt != null ? new FloatTexture(new DerivedTexture(tt, c => new Vector3(c.X * tf))) : tf;
            }
            if (Has(ext, "KHR_materials_volume", out var vol))
            {
                thin = F(vol, "thicknessFactor", 0f) <= 0f;
                if (Has(vol, "attenuationColor", out var ac)) transColor = new SolidColor(Vec3(ac));
                transDepth = F(vol, "attenuationDistance", 0f);
                if (!float.IsFinite(transDepth)) transDepth = 0f;
            }

            // Coat, sheen, specular, iridescence, anisotropy.
            FloatTexture coat = 0f, coatRough = 0f;
            NormalMapTexture? coatNormal = null;
            if (Has(ext, "KHR_materials_clearcoat", out var cc))
            {
                float cf = F(cc, "clearcoatFactor", 0f), crf = F(cc, "clearcoatRoughnessFactor", 0f);
                var ct = Texture(cc, "clearcoatTexture", TextureColorSpace.Linear);
                var crt = Texture(cc, "clearcoatRoughnessTexture", TextureColorSpace.Linear);
                coat = ct != null ? new FloatTexture(new DerivedTexture(ct, c => new Vector3(c.X * cf))) : cf;
                coatRough = crt != null ? new FloatTexture(new DerivedTexture(crt, c => new Vector3(c.Y * crf))) : crf;
                if (Texture(cc, "clearcoatNormalTexture", TextureColorSpace.Linear) is ITexture cn)
                    coatNormal = new NormalMapTexture(cn, Has(cc, "clearcoatNormalTexture", out var cni) ? F(cni, "scale", 1f) : 1f);
            }
            FloatTexture fuzz = 0f, fuzzRough = 0.5f;
            ITexture fuzzColor = new SolidColor(Vector3.One);
            if (Has(ext, "KHR_materials_sheen", out var sh))
            {
                Vector3 sc = Has(sh, "sheenColorFactor", out var scf) ? Vec3(scf) : Vector3.Zero;
                if (sc != Vector3.Zero)
                {
                    fuzz = 1f;
                    var sct = Texture(sh, "sheenColorTexture", TextureColorSpace.Srgb);
                    fuzzColor = sct != null ? Scaled(sct, sc) : new SolidColor(sc);
                    float srf = F(sh, "sheenRoughnessFactor", 0f);
                    var srt = Texture(sh, "sheenRoughnessTexture", TextureColorSpace.Linear, ImageChannels.Alpha);
                    fuzzRough = srt != null ? new FloatTexture(Scaled(srt, new Vector3(srf))) : srf;
                }
            }
            FloatTexture specWeight = 1f;
            ITexture specColor = new SolidColor(Vector3.One);
            if (Has(ext, "KHR_materials_specular", out var sp))
            {
                float sf = F(sp, "specularFactor", 1f);
                var st = Texture(sp, "specularTexture", TextureColorSpace.Linear, ImageChannels.Alpha);
                specWeight = st != null ? new FloatTexture(Scaled(st, new Vector3(sf))) : sf;
                Vector3 scf2 = Has(sp, "specularColorFactor", out var scfe) ? Vec3(scfe) : Vector3.One;
                var sct = Texture(sp, "specularColorTexture", TextureColorSpace.Srgb);
                specColor = sct != null ? Scaled(sct, scf2) : new SolidColor(scf2);
            }
            float film = 0f, filmIor = 1.3f, filmThickness = 0.4f;
            if (Has(ext, "KHR_materials_iridescence", out var ir))
            {
                film = F(ir, "iridescenceFactor", 0f);
                filmIor = F(ir, "iridescenceIor", 1.3f);
                filmThickness = F(ir, "iridescenceThicknessMaximum", 400f) / 1000f; // nm → µm
            }
            float aniso = 0f, anisoRotation = 0f;
            if (Has(ext, "KHR_materials_anisotropy", out var an))
            {
                aniso = F(an, "anisotropyStrength", 0f);
                anisoRotation = F(an, "anisotropyRotation", 0f) / (2f * MathF.PI);
            }

            bool unlit = Has(ext, "KHR_materials_unlit", out _);
            var parameters = new OpenPbrParameters
            {
                BaseWeight = unlit ? 0f : 1f,
                BaseColor = baseColor,
                BaseMetalness = metal,
                SpecularWeight = unlit ? 0f : specWeight,
                SpecularColor = specColor,
                SpecularRoughness = rough,
                SpecularRoughnessAnisotropy = aniso,
                SpecularRotation = anisoRotation,
                SpecularIor = ior,
                TransmissionWeight = transmission,
                TransmissionColor = transColor,
                TransmissionDepth = transDepth,
                CoatWeight = coat,
                CoatRoughness = coatRough,
                CoatIor = 1.5f,
                FuzzWeight = fuzz,
                FuzzColor = fuzzColor,
                FuzzRoughness = fuzzRough,
                ThinFilmWeight = film,
                ThinFilmIor = filmIor,
                ThinFilmThickness = filmThickness,
                EmissionLuminance = unlit ? 1f : emits ? strength : 0f,
                EmissionColor = unlit ? baseColor : emission,
                GeometryOpacity = opacity,
                ThinWalled = thin && (!transmission.IsConstant || transmission.ConstantValue > 0f),
                CoatNormal = coatNormal,
            };
            var material = new OpenPbr(parameters);
            if (Texture(m, "normalTexture", TextureColorSpace.Linear) is ITexture normal)
                material.NormalMap = new NormalMapTexture(normal, Has(m, "normalTexture", out var nti) ? F(nti, "scale", 1f) : 1f);
            return material;
        }

        private static ITexture Scaled(ITexture t, Vector3 factor)
            => factor == Vector3.One ? t : new DerivedTexture(t, c => c * factor);

        private static Vector2 Vec2(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle());
        private static Vector3 Vec3(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        private static Vector4 Vec4(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle(), a[3].GetSingle());
    }

    /// <summary>
    /// KHR_texture_transform: uv' = T(offset) R(rotation) S(scale) uv in glTF's
    /// texture space (V downwards), applied to the renderer's (u, 1 − v).
    /// </summary>
    private sealed class UvTransformTexture : ITexture
    {
        private readonly ITexture _inner;
        private readonly float _a, _b, _c, _d, _ox, _oy;

        public UvTransformTexture(ITexture inner, Vector2 offset, float rotation, Vector2 scale)
        {
            _inner = inner;
            float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
            _a = cos * scale.X; _b = sin * scale.Y;
            _c = -sin * scale.X; _d = cos * scale.Y;
            _ox = offset.X; _oy = offset.Y;
        }

        private (float U, float V) Map(float u, float v)
        {
            float vg = 1f - v;
            return (_a * u + _b * vg + _ox, 1f - (_c * u + _d * vg + _oy));
        }

        public Vector3 Value(float u, float v, Vector3 p, int objectSeed)
        {
            var (mu, mv) = Map(u, v);
            return _inner.Value(mu, mv, p, objectSeed);
        }

        public Vector3 Value(float u, float v, Vector3 p, int objectSeed, in FilterFootprint footprint)
        {
            var (mu, mv) = Map(u, v);
            if (!footprint.HasFootprint) return _inner.Value(mu, mv, p, objectSeed);
            // d(u') = a du − b dv ; d(v') = c du − d dv in the renderer's v, both
            // with the sign of v flipped twice.
            var f = new FilterFootprint(footprint.DPdx, footprint.DPdy,
                _a * footprint.DUdx - _b * footprint.DVdx, -_c * footprint.DUdx + _d * footprint.DVdx,
                _a * footprint.DUdy - _b * footprint.DVdy, -_c * footprint.DUdy + _d * footprint.DVdy);
            return _inner.Value(mu, mv, p, objectSeed, f);
        }
    }
}
