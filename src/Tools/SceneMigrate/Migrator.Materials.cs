using System.Numerics;
using RayTracer.Lights;
using RayTracer.Scene;
using YamlDotNet.RepresentationModel;
using static SceneMigrate.YamlEdits;

namespace SceneMigrate;

public sealed partial class Migrator
{
    // ── Materials ────────────────────────────────────────────────────────────

    private void MigrateMaterial(YamlMappingNode m)
    {
        string type = Str(m, "type") ?? "";
        switch (type)
        {
            case "disney" or "disney_bsdf" or "pbr":
                MigrateDisney(m);
                break;
            case "lambertian":
                TextureToColor(m, "color");
                break;
            case "metal":
                if (Key(m, "fuzz") != null) { Remove(m, "roughness"); RenameKeyOf(m, "fuzz", "roughness"); }
                bool measured = Key(m, "preset") != null || Key(m, "eta") != null || Key(m, "k") != null;
                if (Key(m, "color") == null && Key(m, "texture") == null && !measured)
                    Add(m, "color", "0.5");   // the format-1 default; format 2's is white
                TextureToColor(m, "color");
                break;
            case "dielectric":
                if (Key(m, "ior") != null)
                {
                    Note(Key(m, "ior")!, $"dielectric 'ior: {Str(m, "ior")}' was ignored by format 1 (it read refraction_index): " +
                                         "kept at the value format 1 used");
                    if (Key(m, "refraction_index") != null) Remove(m, "ior");
                    else Set(m, "ior", "1.5");
                }
                RenameKeyOf(m, "refraction_index", "ior");
                TextureToColor(m, "color");
                break;
            case "emissive":
                MigrateEmissive(m);
                break;
            case "openpbr":
                MigrateOpenPbr(m);
                break;
            case "mix" or "blend":
                if (type == "blend") SetType(m, "mix");
                if (Key(m, "mask") != null) { Remove(m, "blend"); RenameKeyOf(m, "mask", "blend"); }
                break;
            default:
                // Format 1 made any other type a Lambertian of its colour.
                Note(m, $"material type '{type}' was unknown to format 1 (a Lambertian): now lambertian");
                if (Key(m, "type") != null) SetType(m, "lambertian"); else Add(m, "type", "lambertian");
                TextureToColor(m, "color");
                type = "lambertian";
                break;
        }
        string v2Type = type switch
        {
            "disney" or "disney_bsdf" or "pbr" => "openpbr",
            "blend" => "mix",
            _ => type,
        };
        // Every block value but the modifiers below is a texture.
        foreach (var (key, kn, value) in Pairs(m))
            if (value is YamlMappingNode tex && !_ed.IsRemoved(kn)
                && key is not ("normal_map" or "coat_normal_map" or "bump_map" or "displacement"))
                MigrateTexture(tex);
        if (Val(m, "normal_map") is YamlMappingNode nm) Sweep(nm, typeof(NormalMapData), null);
        if (Val(m, "coat_normal_map") is YamlMappingNode cnm) Sweep(cnm, typeof(NormalMapData), null);
        if (Val(m, "bump_map") is YamlMappingNode bm)
        {
            if (Val(bm, "texture") is YamlMappingNode bt) MigrateTexture(bt);
            Sweep(bm, typeof(BumpMapData), null);
        }
        if (Val(m, "displacement") is YamlMappingNode dm) MigrateDisplacement(dm);
        Sweep(m, typeof(MaterialData), v2Type);
    }

    /// <summary>A material's <c>texture:</c> becomes its colour input (it won over the colour in format 1).</summary>
    private void TextureToColor(YamlMappingNode m, string colorKey)
    {
        if (Key(m, "texture") == null) return;
        Remove(m, colorKey);
        RenameKeyOf(m, "texture", colorKey);
    }

    private void MigrateDisplacement(YamlMappingNode d)
    {
        RenameKeyOf(d, "displacement_method", "method");
        string? method = Str(d, "displacement_method");
        if (method is "displacement_and_bump" or "") Set(d, "displacement_method", "both");
        if (method == "displacement_only") Set(d, "displacement_method", "displacement");
        if (method == "bump") Set(d, "displacement_method", "bump_only");
        string? mode = Str(d, "mode");
        if (mode is "height" or "") Set(d, "mode", "scalar");
        if (mode == "vec") Set(d, "mode", "vector");
        if (Str(d, "space") == "local") Set(d, "space", "object");
        if (Val(d, "texture") is YamlMappingNode t) MigrateTexture(t);
        Sweep(d, typeof(DisplacementData), null);
    }

    private void MigrateEmissive(YamlMappingNode m)
    {
        // Format 1: a texture times `luminance` or `intensity`; a colour times
        // the black body times `intensity`, or a tint times `luminance`.
        // Format 2: a texture or a tint of luminance 1, times `luminance`.
        double? lumKey = NumOf(m, "luminance");
        double intensity = NumOf(m, "intensity") ?? 1;
        if (Key(m, "texture") != null)
        {
            Remove(m, "color");
            RenameKeyOf(m, "texture", "color");
            if (lumKey == null) Put(m, "intensity", "luminance", Num(intensity));
            else Remove(m, "intensity");
            return;
        }
        if (lumKey == null)
        {
            var color = Vec3(Nums(m, "color")) ?? Vector3.One;
            double l = intensity * Lum(Tinted(color, NumOf(m, "temperature")));
            Put(m, "intensity", "luminance", Num(l));
        }
        else Remove(m, "intensity");
    }

    private void MigrateOpenPbr(YamlMappingNode m)
    {
        // base_color fell back on the shared 'color' and 'texture'.
        if (Key(m, "base_color") == null)
        {
            if (Key(m, "texture") != null) { Remove(m, "color"); RenameKeyOf(m, "texture", "base_color"); }
            else RenameKeyOf(m, "color", "base_color");
        }
        foreach (var name in new[] { "transmission_color", "transmission_depth", "subsurface_color", "coat_roughness",
                                     "coat_ior", "thin_film_thickness", "thin_film_ior" })
            if (Key(m, name + "_texture") != null) { Remove(m, name); RenameKeyOf(m, name + "_texture", name); }
        if (Nums(m, "subsurface_radius") is { Length: 3 } r3 && Key(m, "subsurface_radius_scale") == null)
            RenameKeyOf(m, "subsurface_radius", "subsurface_radius_scale");
        else if (Nums(m, "subsurface_radius") is { Length: 1 } r1)
            Set(m, "subsurface_radius", Num(r1[0]));
        if (Key(m, "thin_walled") != null)
        {
            if (Key(m, "geometry_thin_walled") == null) RenameKeyOf(m, "thin_walled", "geometry_thin_walled");
            else Remove(m, "thin_walled");
        }
        if (NumOf(m, "coat_roughness") is double cr && cr < 0) Set(m, "coat_roughness", "0");
    }

    // ── Disney → OpenPBR (the mapping of RayTracer.Materials.DisneyMaterial) ─

    private readonly HashSet<YamlNode> _disneyHandled = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Writes <paramref name="v2Key"/> from the first of <paramref name="from"/>
    /// present (renamed in place, so its comment stays; <paramref name="value"/>
    /// null keeps its value), or adds it.
    /// </summary>
    private void Emit(YamlMappingNode m, string v2Key, string? value, params string[] from)
    {
        foreach (var f in from)
        {
            if (Key(m, f) is not { } k || _disneyHandled.Contains(k) || _ed.IsRemoved(k)) continue;
            _disneyHandled.Add(k);
            if (f != v2Key) RenameTracked(m, k, v2Key);
            if (value != null && !SameNumbers(value, m.Children[k])) _ed.Replace(m.Children[k], value);
            return;
        }
        if (value != null) Add(m, v2Key, value);
    }

    /// <summary>A written value that already says <paramref name="text"/> (keeps its formatting).</summary>
    private bool SameNumbers(string text, YamlNode node)
    {
        if (text == _ed.Flow(node)) return true;
        var a = text.Trim('[', ']').Split(',', StringSplitOptions.TrimEntries);
        var b = Nums(node);
        if (b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
                || f != (float)b[i]) return false;
        return true;
    }

    private static Vector3 Hue(Vector3 c)
    {
        float l = Lum(c);
        return l > 0f ? c / l : Vector3.One;
    }

    private static string V(Vector3 c) => Vec([c.X, c.Y, c.Z]);

    private void MigrateDisney(YamlMappingNode m)
    {
        SetType(m, "openpbr");
        string id = Str(m, "id") ?? "(unnamed)";
        double Get(string k, double def) => NumOf(m, k) ?? def;
        YamlMappingNode? Tex(string k) => Val(m, k) as YamlMappingNode;

        // Base colour: the texture, else the colour (format-1 default grey 0.5).
        var baseTex = Tex("texture");
        var baseColor = Vec3(Nums(m, "color")) ?? new Vector3(0.5f);
        if (baseTex != null) { Emit(m, "base_color", null, "texture"); }
        else Emit(m, "base_color", V(baseColor), "color");
        string baseFlow = baseTex != null ? _ed.Flow(baseTex) : V(baseColor);

        void Approx(string what) => Note(m, $"material '{id}': {what} (approximated)");

        double metallic = Get("metallic", 0);
        if (Tex("metallic_texture") != null) Emit(m, "base_metalness", null, "metallic_texture");
        else if (metallic != 0) Emit(m, "base_metalness", Num(metallic), "metallic");
        bool fullMetal = Tex("metallic_texture") == null && metallic >= 1;

        // specular (0.5) scales F0 by 2·specular; a metal keeps white.
        double specular = Get("specular", 0.5);
        if (Tex("specular_texture") != null) Approx("specular_texture cannot be doubled into specular_weight; using 'specular'");
        double specWeight = fullMetal ? 1 : 2 * Math.Clamp(specular, 0, 2);
        if (Math.Abs(specWeight - 1) > 1e-7) Emit(m, "specular_weight", Num(specWeight), "specular");

        double specTint = Get("specular_tint", 0);
        if (!fullMetal && specTint > 0)
        {
            if (baseTex != null) Approx("specular_tint towards a textured colour; using white");
            else Emit(m, "specular_color", V(Vector3.Lerp(Vector3.One, Hue(Vector3.Max(baseColor, Vector3.Zero)), (float)Math.Clamp(specTint, 0, 1))), "specular_tint");
        }

        if (Tex("roughness_texture") != null) Emit(m, "specular_roughness", null, "roughness_texture");
        else Emit(m, "specular_roughness", Num(Get("roughness", 0.5)), "roughness");

        double aniso = Get("anisotropic", 0);
        if (Tex("anisotropic_texture") != null) Approx("anisotropic_texture is not scaled by 0.9");
        if (aniso != 0) Emit(m, "specular_roughness_anisotropy", Num(0.9 * Math.Clamp(aniso, 0, 1)), "anisotropic");
        double rot = Get("anisotropic_rotation", 0);
        if (rot != 0) Emit(m, "specular_rotation", Num(rot * 360), "anisotropic_rotation");

        double ior = Get("ior", 1.5);
        if (Tex("ior_texture") != null) Emit(m, "specular_ior", null, "ior_texture");
        else if (Math.Abs(ior - 1.5) > 1e-7) Emit(m, "specular_ior", Num(ior), "ior");

        // Transmission, subsurface, thin walls.
        bool thinWalled = Bool(m, "thin_walled") == true;
        double diffTrans = Get("diff_trans", 0);
        bool hasDiffTrans = diffTrans > 0 || Tex("diff_trans_texture") != null;
        bool thin = thinWalled || hasDiffTrans;
        var radius = Nums(m, "subsurface_radius");
        bool sss = radius is { Length: > 0 } && radius.Any(v => v > 0) && !thinWalled;
        double? specTransRaw = NumOf(m, "spec_trans");
        double specTrans = specTransRaw ?? (sss ? 1 : 0);
        var tcList = Nums(m, "transmission_color");
        var tcTex = Tex("transmission_color_texture");
        if (sss && tcList == null && tcTex == null) tcList = [1, 1, 1];
        double depth = Get("transmission_depth", 0);

        if (!sss && (specTrans > 0 || Tex("spec_trans_texture") != null))
        {
            if (Tex("spec_trans_texture") != null) Emit(m, "transmission_weight", null, "spec_trans_texture");
            else Emit(m, "transmission_weight", Num(specTrans), "spec_trans");
            // Format 1 tinted every refraction; format 2 tints the entry only.
            if (thin)
            {
                if (tcTex != null) Emit(m, "transmission_color", null, "transmission_color_texture");
                else if (tcList != null) Emit(m, "transmission_color", null, "transmission_color");
                else if (baseTex != null) { Approx("the square root of a textured colour as transmission colour"); Emit(m, "transmission_color", baseFlow); }
                else Emit(m, "transmission_color", V(Vector3.SquareRoot(Vector3.Max(baseColor, Vector3.Zero))));
                if (depth != 0) Emit(m, "transmission_depth", Num(depth), "transmission_depth");
            }
            else if (tcList == null && tcTex == null)
            {
                Emit(m, "transmission_color", baseFlow);
            }
            else if (depth == 0 && Tex("transmission_depth_texture") == null)
            {
                if (tcTex != null) { Approx("a textured transmission colour squared"); Emit(m, "transmission_color", null, "transmission_color_texture"); }
                else { var tc = Vec3(tcList)!.Value; Emit(m, "transmission_color", V(tc * tc), "transmission_color"); }
            }
            else
            {
                if (tcTex != null) Emit(m, "transmission_color", null, "transmission_color_texture");
                else Emit(m, "transmission_color", null, "transmission_color");
                if (Tex("transmission_depth_texture") != null) Emit(m, "transmission_depth", null, "transmission_depth_texture");
                else Emit(m, "transmission_depth", Num(depth), "transmission_depth");
            }
        }

        if (sss && !thin)
        {
            // The medium of format 1: subsurface_color, else the base colour
            // list (white when only a texture), the radius times its scale.
            if (Tex("spec_trans_texture") != null) Approx("spec_trans_texture as subsurface weight");
            Emit(m, "subsurface_weight", Num(specTrans), "spec_trans");
            var sc = Vec3(Nums(m, "subsurface_color")) ?? Vec3(Nums(m, "color")) ?? Vector3.One;
            Emit(m, "subsurface_color", V(sc), "subsurface_color");
            float rR = (float)radius![0], rG = radius.Length >= 2 ? (float)radius[1] : rR, rB = radius.Length >= 3 ? (float)radius[2] : rG;
            double scale = Get("subsurface_scale", 1);
            if (scale <= 0) scale = 1;
            Emit(m, "subsurface_radius_scale", V(new Vector3(rR, rG, rB) * (float)scale), "subsurface_radius");
            double g = Get("subsurface_anisotropy", 0);
            if (g != 0) Emit(m, "subsurface_scatter_anisotropy", Num(g), "subsurface_anisotropy");
        }
        else if (radius != null && thinWalled)
            Note(m, $"material '{id}': subsurface_radius on a thin-walled Disney material had no effect: dropped");

        if (hasDiffTrans)
        {
            Emit(m, "subsurface_weight", "1");
            Emit(m, "subsurface_color", baseFlow);
            if (Tex("diff_trans_texture") != null) Approx("diff_trans_texture as anisotropy 2d − 1");
            Emit(m, "subsurface_scatter_anisotropy", Num(2 * Math.Clamp(diffTrans, 0, 1) - 1), "diff_trans");
        }
        if (thin) Emit(m, "geometry_thin_walled", "true", "thin_walled");

        // Coat.
        double coat = Get("clearcoat", 0);
        if (coat > 0 || Tex("clearcoat_texture") != null)
        {
            if (Tex("clearcoat_texture") != null) Emit(m, "coat_weight", null, "clearcoat_texture");
            else Emit(m, "coat_weight", Num(coat), "clearcoat");
            if (Tex("coat_roughness_texture") != null) Emit(m, "coat_roughness", null, "coat_roughness_texture");
            else if (NumOf(m, "coat_roughness") is double crw && crw >= 0) Emit(m, "coat_roughness", Num(crw), "coat_roughness");
            else
            {
                if (Tex("clearcoat_gloss_texture") != null) Approx("clearcoat_gloss_texture; using clearcoat_gloss");
                double gloss = Math.Clamp(Get("clearcoat_gloss", 1), 0, 1);
                Emit(m, "coat_roughness", Num(Math.Sqrt(0.1 + (0.001 - 0.1) * gloss)), "clearcoat_gloss");
            }
            if (Tex("coat_ior_texture") != null) Emit(m, "coat_ior", null, "coat_ior_texture");
            else Emit(m, "coat_ior", Num(Get("coat_ior", 1.5)), "coat_ior");
        }

        // Sheen → fuzz, tinted towards the base hue.
        double sheen = Get("sheen", 0);
        if (sheen > 0 || Tex("sheen_texture") != null)
        {
            if (Tex("sheen_texture") != null) Emit(m, "fuzz_weight", null, "sheen_texture");
            else Emit(m, "fuzz_weight", Num(sheen), "sheen");
            double tint = Get("sheen_tint", 0.5);
            if (tint > 0)
            {
                if (baseTex != null) Approx("sheen tint towards a textured colour; using white");
                else Emit(m, "fuzz_color", V(Vector3.Lerp(Vector3.One, Hue(Vector3.Max(baseColor, Vector3.Zero)), (float)Math.Clamp(tint, 0, 1))), "sheen_tint");
            }
            if (Tex("sheen_roughness_texture") != null) Emit(m, "fuzz_roughness", null, "sheen_roughness_texture");
            else Emit(m, "fuzz_roughness", Num(Get("sheen_roughness", 0.3)), "sheen_roughness");
        }

        // Thin film: nanometres → micrometres.
        double film = Get("thin_film_thickness", 0);
        if (film > 0)
        {
            Emit(m, "thin_film_weight", "1");
            Emit(m, "thin_film_thickness", Num(film / 1000), "thin_film_thickness");
            Emit(m, "thin_film_ior", Num(Get("thin_film_ior", 1.5)), "thin_film_ior");
        }

        // Everything else of the Disney parameter set is gone (its value is in
        // the keys above, or it had no effect).
        string[] keep = ["id", "type", "normal_map", "bump_map", "displacement", "coat_normal_map"];
        foreach (var (key, kn, _) in Pairs(m))
        {
            if (keep.Contains(key) || _disneyHandled.Contains(kn) || _ed.IsRemoved(kn)) continue;
            if (key is "subsurface" or "flatness" or "subsurface_texture" or "flatness_texture")
                Note(kn, $"material '{id}': legacy '{key}' had no effect: dropped");
            _ed.RemovePair(m, kn);
        }
    }

    // ── Textures ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> TextureTypes = new()
    {
        ["worley"] = "voronoi", ["cell"] = "voronoi", ["cellular"] = "voronoi",
        ["bricks"] = "brick", ["tile"] = "brick", ["ramp"] = "gradient",
        ["coord"] = "coordinate", ["coords"] = "coordinate", ["texture_coord"] = "coordinate",
        ["tex_coord"] = "coordinate", ["st"] = "coordinate",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> TextureEnums = new()
    {
        ["noise_type"] = new() { ["fractal"] = "fbm", ["turb"] = "turbulence", ["ridge"] = "ridged", ["billowed"] = "billow",
                                 ["heteroterrain"] = "hetero_terrain", ["hetero"] = "hetero_terrain", ["heterogeneous"] = "hetero_terrain",
                                 ["hybridmultifractal"] = "hybrid_multifractal", ["hybrid"] = "hybrid_multifractal",
                                 ["multifractal"] = "hybrid_multifractal" },
        ["metric"] = new() { ["l1"] = "manhattan", ["linf"] = "chebyshev", ["chess"] = "chebyshev",
                             ["euclidean2"] = "euclidean_squared", ["sq"] = "euclidean_squared" },
        ["mode"] = new() { ["quad"] = "quadratic", ["ease"] = "easing", ["smoothstep"] = "easing", ["sphere"] = "spherical",
                           ["cylinder"] = "radial", ["cylindrical"] = "radial", ["parametric"] = "uv",
                           ["gen"] = "generated", ["ref"] = "generated", ["pref"] = "generated", ["pworld"] = "world",
                           ["world_space"] = "world" },
        ["color_space"] = new() { ["raw"] = "linear", ["non-color"] = "linear", ["data"] = "linear" },
    };

    private static readonly Dictionary<string, string> OutputAliases = new()
    {
        ["scalar"] = "mask", ["vein_mask"] = "mask", ["veinmask"] = "mask", ["latewood_mask"] = "mask", ["ringmask"] = "mask",
        ["f2-f1"] = "f2_minus_f1", ["crackle"] = "f2_minus_f1", ["f3-f1"] = "f3_minus_f1", ["wide_crackle"] = "f3_minus_f1",
        ["border"] = "f3_minus_f1", ["f1+f2"] = "f1_plus_f2", ["color"] = "cell", ["rnd"] = "random", ["id"] = "random",
        ["scalar_random"] = "random", ["per_cell"] = "random", ["feature_position"] = "position", ["feature"] = "position",
        ["p"] = "position",
    };

    private void MigrateTexture(YamlMappingNode t)
    {
        string? type = Str(t, "type");
        if (type != null && TextureTypes.TryGetValue(type, out var canon)) { SetType(t, canon); type = canon; }
        if (type is null or "solid")
        {
            Note(t, $"texture type '{type ?? "(none)"}' was a solid colour in format 1: write the colour itself instead");
        }
        foreach (var (k, map) in TextureEnums)
            if (Str(t, k) is { } v && map.TryGetValue(v, out var c)) Set(t, k, c);
        if (Str(t, "output") is { } o && OutputAliases.TryGetValue(o, out var oc))
        {
            // 'color' is a cell colour for voronoi, the default colour output otherwise.
            if (!(o == "color" && type != "voronoi")) Set(t, "output", oc);
        }
        if (Str(t, "noise_basis") is { } nb)
        {
            if (nb is "classic" or "legacy") Note(t, "noise_basis classic is gone: the pattern changes to the improved basis");
            Remove(t, "noise_basis");
        }
        if (type == "wood")
        {
            if (Key(t, "grain_strength") != null) { Remove(t, "noise_strength"); RenameKeyOf(t, "grain_strength", "noise_strength"); }
            if (Key(t, "distortion") != null)
            {
                if (Key(t, "warp_amplitude") == null) RenameKeyOf(t, "distortion", "warp_amplitude");
                else Remove(t, "distortion");
            }
        }
        else Remove(t, "grain_strength");
        if (Val(t, "impurities_texture") is YamlMappingNode it) MigrateTexture(it);
        if (Val(t, "color_ramp") is YamlSequenceNode ramp)
            foreach (var stop in ramp.Children.OfType<YamlMappingNode>())
            {
                string? i = Str(stop, "interp");
                if (i == "smooth") Set(stop, "interp", "smoothstep");
                if (i is "step" or "hold") Set(stop, "interp", "constant");
                if (i is "easing" or "smoother") Set(stop, "interp", "ease");
                Sweep(stop, typeof(ColorRampStopData), null);
            }
        Sweep(t, typeof(TextureData), type);
    }
}
