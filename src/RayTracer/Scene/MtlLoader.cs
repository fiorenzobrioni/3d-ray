using System.Globalization;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RayTracer.Scene;

/// <summary>
/// Wavefront MTL material library, read into OpenPBR materials
/// (PIANO-REVIEW.md Phase 2). The classic statements and the PBR extension
/// most exporters write:
///
/// <list type="bullet">
///   <item><description><c>Kd</c>, <c>map_Kd</c>: base colour (the map sRGB).</description></item>
///   <item><description><c>Ks</c>: specular weight, 2·luminance clamped to 1
///     (exporters write 0.5 for the usual 4% dielectric reflection).</description></item>
///   <item><description><c>Ns</c>: Phong exponent, turned into a roughness:
///     Beckmann α = √(2/(Ns + 2)), and OpenPBR's roughness is √α.</description></item>
///   <item><description><c>Pr</c>/<c>map_Pr</c> (roughness) and <c>Pm</c>/<c>map_Pm</c>
///     (metalness) of the PBR extension, taking precedence over Ns.</description></item>
///   <item><description><c>Ni</c>: index of refraction.</description></item>
///   <item><description><c>d</c> (or <c>Tr</c> = 1 − d) and <c>map_d</c>:
///     opacity (cut-out); with <c>illum</c> 4, 6, 7 or 9 (refraction) it is the
///     glass's transmission instead, <c>Tf</c> its colour.</description></item>
///   <item><description><c>Ke</c>/<c>map_Ke</c>: emission.</description></item>
///   <item><description><c>bump</c>/<c>map_Bump</c> (with <c>-bm</c>) a height
///     map; <c>norm</c>/<c>map_Kn</c> a tangent-space normal map.</description></item>
///   <item><description><c>Pc</c>/<c>Pcr</c>: clear coat and its roughness;
///     <c>Ps</c>: sheen (fuzz); <c>aniso</c>: anisotropy.</description></item>
/// </list>
/// Map options: <c>-s</c> (UV scale) and <c>-bm</c>; the others are
/// reported and ignored.
/// </summary>
public static class MtlLoader
{
    /// <summary>One material as the file states it.</summary>
    private sealed class Mtl
    {
        public string Name = "";
        public Vector3 Kd = new(0.8f), Ks = new(0.5f), Ke = Vector3.Zero, Tf = Vector3.One;
        public float Ns = -1f, Ni = 1.5f, D = 1f, Pr = -1f, Pm = 0f, Pc = 0f, Pcr = 0f, Ps = 0f, Aniso = 0f;
        public int Illum = 2;
        public bool KsGiven;
        public MapRef? MapKd, MapD, MapKe, MapPr, MapPm, Bump, Norm;
    }

    private sealed record MapRef(string Path, float ScaleU, float ScaleV, float BumpMultiplier);

    /// <summary>
    /// Reads every material of the library at <paramref name="path"/>;
    /// texture paths are relative to its folder.
    /// </summary>
    public static Dictionary<string, IMaterial> Load(string path, List<string>? warnings)
    {
        var result = new Dictionary<string, IMaterial>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            warnings?.Add($"MTL library not found: {path}");
            return result;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var all = new List<Mtl>();
        Mtl? cur = null;
        int lineNum = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNum++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int sp = line.IndexOfAny(new[] { ' ', '\t' });
            string key = sp < 0 ? line : line[..sp];
            string rest = sp < 0 ? "" : line[(sp + 1)..].Trim();
            if (key == "newmtl") { cur = new Mtl { Name = rest }; all.Add(cur); continue; }
            if (cur == null) continue;
            switch (key)
            {
                case "Kd": cur.Kd = Vec(rest, cur.Kd); break;
                case "Ks": cur.Ks = Vec(rest, cur.Ks); cur.KsGiven = true; break;
                case "Ke": cur.Ke = Vec(rest, cur.Ke); break;
                case "Tf": cur.Tf = Vec(rest, cur.Tf); break;
                case "Ns": cur.Ns = Num(rest, cur.Ns); break;
                case "Ni": cur.Ni = Num(rest, cur.Ni); break;
                case "d": cur.D = Num(rest, cur.D); break;
                case "Tr": cur.D = 1f - Num(rest, 0f); break;
                case "Pr": cur.Pr = Num(rest, cur.Pr); break;
                case "Pm": cur.Pm = Num(rest, cur.Pm); break;
                case "Pc": cur.Pc = Num(rest, cur.Pc); break;
                case "Pcr": cur.Pcr = Num(rest, cur.Pcr); break;
                case "Ps": cur.Ps = Num(rest, Vec(rest, Vector3.Zero).X); break;
                case "aniso": cur.Aniso = Num(rest, cur.Aniso); break;
                case "illum": cur.Illum = (int)Num(rest, cur.Illum); break;
                case "map_Kd": cur.MapKd = Map(rest, dir, warnings, lineNum); break;
                case "map_d": cur.MapD = Map(rest, dir, warnings, lineNum); break;
                case "map_Ke": cur.MapKe = Map(rest, dir, warnings, lineNum); break;
                case "map_Pr": cur.MapPr = Map(rest, dir, warnings, lineNum); break;
                case "map_Pm": cur.MapPm = Map(rest, dir, warnings, lineNum); break;
                case "bump" or "map_Bump" or "map_bump": cur.Bump = Map(rest, dir, warnings, lineNum); break;
                case "norm" or "map_Kn": cur.Norm = Map(rest, dir, warnings, lineNum); break;
                case "Ka" or "map_Ka" or "Ni_" or "sharpness": break; // ambient: no meaning here
                default:
                    if (key.StartsWith("map_") || key is "disp" or "decal" or "refl")
                        warnings?.Add($"MTL line {lineNum}: '{key}' is not supported, ignored");
                    break;
            }
        }
        foreach (var m in all)
            result[m.Name] = Build(m, warnings);
        return result;
    }

    private static IMaterial Build(Mtl m, List<string>? warnings)
    {
        ITexture? Tex(MapRef? r, TextureColorSpace cs, ImageChannels ch = ImageChannels.Rgb)
        {
            if (r == null) return null;
            try { return new ImageTexture(r.Path, r.ScaleU, r.ScaleV, cs, ch); }
            catch (Exception ex)
            {
                warnings?.Add($"MTL material '{m.Name}': cannot read '{r.Path}' ({ex.Message})");
                return null;
            }
        }

        ITexture baseColor = Tex(m.MapKd, TextureColorSpace.Srgb) is ITexture kd
            ? (m.Kd == Vector3.One ? kd : new DerivedTexture(kd, c => c * m.Kd))
            : new SolidColor(m.Kd);

        // A Kd map's own colour is the base colour: many exporters also write
        // Kd 0.8 or 0.64 next to it as a mere preview tint. Blender writes
        // Kd 1 with a map; others keep the multiplication. Following the
        // specification: the map is multiplied by Kd.
        float roughness = m.Pr >= 0f ? m.Pr
                        : m.Ns >= 0f ? MathF.Pow(2f / (m.Ns + 2f), 0.25f) : 0.5f;
        FloatTexture rough = Tex(m.MapPr, TextureColorSpace.Linear) is ITexture pr
            ? new FloatTexture(pr) : roughness;
        FloatTexture metal = Tex(m.MapPm, TextureColorSpace.Linear) is ITexture pm
            ? new FloatTexture(pm) : m.Pm;

        float specWeight = m.KsGiven ? Math.Clamp(2f * MathUtils.Luminance(m.Ks), 0f, 1f) : 1f;
        bool glass = m.Illum is 4 or 6 or 7 or 9;
        float transmission = glass ? Math.Clamp(1f - m.D, 0f, 1f) : 0f;

        FloatTexture opacity = 1f;
        if (!glass)
        {
            if (m.MapD != null)
            {
                bool alpha = HasAlpha(m.MapD.Path);
                if (Tex(m.MapD, TextureColorSpace.Linear, alpha ? ImageChannels.Alpha : ImageChannels.Rgb) is ITexture d)
                    opacity = new FloatTexture(m.D == 1f ? d : new DerivedTexture(d, c => c * m.D));
            }
            else opacity = Math.Clamp(m.D, 0f, 1f);
        }

        ITexture? emissionMap = Tex(m.MapKe, TextureColorSpace.Srgb);
        bool emits = m.Ke != Vector3.Zero || emissionMap != null;
        ITexture emission = emissionMap != null
            ? (m.Ke == Vector3.Zero || m.Ke == Vector3.One ? emissionMap : new DerivedTexture(emissionMap, c => c * m.Ke))
            : new SolidColor(m.Ke);

        var parameters = new OpenPbrParameters
        {
            BaseColor = baseColor,
            BaseMetalness = metal,
            SpecularWeight = specWeight,
            SpecularRoughness = rough,
            SpecularRoughnessAnisotropy = Math.Clamp(m.Aniso, 0f, 1f),
            SpecularIor = m.Ni > 0f ? m.Ni : 1.5f,
            TransmissionWeight = transmission,
            TransmissionColor = new SolidColor(m.Tf),
            CoatWeight = Math.Clamp(m.Pc, 0f, 1f),
            CoatRoughness = Math.Clamp(m.Pcr, 0f, 1f),
            FuzzWeight = Math.Clamp(m.Ps, 0f, 1f),
            EmissionLuminance = emits ? 1f : 0f,
            EmissionColor = emits ? emission : new SolidColor(Vector3.One),
            GeometryOpacity = opacity,
        };
        var material = new OpenPbr(parameters);
        if (m.Norm != null && Tex(m.Norm, TextureColorSpace.Linear) is ImageTexture nimg)
            material.NormalMap = new NormalMapTexture(nimg);
        if (m.Bump != null && Tex(m.Bump, TextureColorSpace.Linear) is ITexture bump)
            material.BumpMap = new BumpMapTexture(bump, m.Bump.BumpMultiplier);
        return material;
    }

    private static bool HasAlpha(string path)
    {
        try
        {
            var info = Image.Identify(path);
            return info.PixelType.AlphaRepresentation is { } a && a != PixelAlphaRepresentation.None;
        }
        catch { return false; }
    }

    // A map statement: options, then the file name (which may contain spaces).
    private static MapRef? Map(string rest, string dir, List<string>? warnings, int lineNum)
    {
        var tok = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        float su = 1f, sv = 1f, bm = 1f;
        int i = 0;
        while (i < tok.Length && tok[i].StartsWith('-'))
        {
            string opt = tok[i++];
            int args = opt switch
            {
                "-s" or "-o" or "-t" => 3,
                "-bm" or "-boost" or "-texres" or "-imfchan" or "-blendu" or "-blendv" or "-clamp" or "-cc" or "-type" => 1,
                "-mm" => 2,
                _ => 0,
            };
            // -s and -o take one to three numbers.
            var vals = new List<float>();
            for (int k = 0; k < args && i < tok.Length; k++)
            {
                if (opt is "-s" or "-o" or "-t" && !float.TryParse(tok[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) break;
                if (float.TryParse(tok[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) vals.Add(f);
                i++;
            }
            switch (opt)
            {
                case "-s":
                    if (vals.Count > 0) su = vals[0];
                    sv = vals.Count > 1 ? vals[1] : su;
                    break;
                case "-bm":
                    if (vals.Count > 0) bm = vals[0];
                    break;
                default:
                    warnings?.Add($"MTL line {lineNum}: map option '{opt}' is not supported, ignored");
                    break;
            }
        }
        if (i >= tok.Length) return null;
        string file = string.Join(' ', tok[i..]).Replace('\\', Path.DirectorySeparatorChar);
        string full = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
        return new MapRef(full, su, sv, bm);
    }

    private static float Num(string s, float fallback)
    {
        var tok = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tok.Length > 0 && float.TryParse(tok[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    private static Vector3 Vec(string s, Vector3 fallback)
    {
        var tok = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length == 0 || tok[0] is "spectral" or "xyz") return fallback;
        var v = new float[3];
        for (int k = 0; k < 3; k++)
        {
            string t = tok[Math.Min(k, tok.Length - 1)];
            if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v[k])) return fallback;
        }
        return new Vector3(v[0], v[1], v[2]);
    }
}
