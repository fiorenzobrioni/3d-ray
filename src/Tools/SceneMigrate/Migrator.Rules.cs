using System.Numerics;
using System.Reflection;
using RayTracer.Scene;
using YamlDotNet.RepresentationModel;
using static SceneMigrate.YamlEdits;

namespace SceneMigrate;

public sealed partial class Migrator
{
    // ── Renames and the final sweep ──────────────────────────────────────────

    private readonly Dictionary<YamlNode, string> _renamed = new(ReferenceEqualityComparer.Instance);

    private void RenameTracked(YamlMappingNode m, YamlNode key, string to)
    {
        _ed.RenameKey(key, to);
        _renamed[key] = to;
    }

    private void RenameKeyOf(YamlMappingNode m, string from, string to)
    {
        if (Key(m, from) is { } k && !_ed.IsRemoved(k)) RenameTracked(m, k, to);
    }

    /// <summary>
    /// The last step on a block: every key still there that format 2 does not
    /// have on this class, or that has no effect on its type, is commented
    /// out (format 1 ignored it or it did nothing for that type).
    /// </summary>
    private void Sweep(YamlMappingNode m, Type v2Class, string? v2Type, params string[] keep)
    {
        var keys = SceneValidator.KeysOf(v2Class);
        foreach (var (key, kn, _) in Pairs(m))
        {
            if (_ed.IsRemoved(kn) || keep.Contains(key)) continue;
            string name = _renamed.TryGetValue(kn, out var r) ? r : key;
            bool dead;
            if (!keys.TryGetValue(name, out var prop)) dead = true;
            else
            {
                var only = prop.GetCustomAttribute<OnlyForAttribute>();
                dead = only != null && v2Type != null && Array.IndexOf(only.Types, v2Type) < 0;
            }
            if (!dead) continue;
            string text = _ed.CommentOutPair(m, kn);
            Note(kn, $"'{key}' had no effect here: commented out ({Short(text)})");
        }
    }

    private void SetType(YamlMappingNode m, string type)
    {
        if (Val(m, "type") is YamlScalarNode t && t.Value != type) _ed.Replace(t, type);
    }

    /// <summary>A visible_to_camera flag as a visibility block.</summary>
    private void VisibleToCamera(YamlMappingNode m)
    {
        if (Key(m, "visible_to_camera") is not { } k) return;
        if (Bool(m, "visible_to_camera") == false)
        {
            RenameTracked(m, k, "visibility");
            _ed.Replace(m.Children[k], "{ camera: false }");
        }
        else _ed.RemovePair(m, k);
    }

    // ── Cameras ──────────────────────────────────────────────────────────────

    private void MigrateSingleCamera(YamlNode key, YamlNode value)
    {
        _ed.RenameKey(key, "cameras");
        if (value is not YamlMappingNode cam) return;
        if (cam.Style == YamlDotNet.Core.Events.MappingStyle.Flow || cam.Children.Count == 0)
        {
            _ed.Insert(Start(cam), "[");
            _ed.Insert(End(cam), "]");
        }
        else
        {
            // The block becomes the first item of a list: a dash before its
            // first key and its other lines two spaces deeper.
            var first = cam.Children.Keys.First();
            _ed.Insert(Start(first), "- ");
            int line = _ed.Text.IndexOf('\n', Start(first)) + 1;
            int last = _ed.LastLineEnd(cam);
            while (line > 0 && line <= last)
            {
                int end = _ed.Text.IndexOf('\n', line);
                if (end < 0) end = _ed.Text.Length;
                if (end > line) _ed.Insert(line, "  ");
                line = end + 1;
            }
        }
        MigrateCamera(cam);
    }

    private void MigrateCamera(YamlMappingNode c)
    {
        RenameKeyOf(c, "vup", "up");
        RenameKeyOf(c, "focal_dist", "focus_distance");
        RenameKeyOf(c, "focal_pos", "focus_point");
        RenameKeyOf(c, "shutter", "shutter_interval");
        if (Key(c, "meters_per_unit") is { } mpu)
        {
            if (MathF.Abs((float)(NumOf(c, "meters_per_unit") ?? 1) - _mpu) > 1e-6f)
                Note(mpu, $"camera meters_per_unit differs from the scene's units ({_mpu} m): the f-stop aperture changes");
            _ed.RemovePair(c, mpu);
        }
        if (Val(c, "motion") is YamlSequenceNode keys)
            foreach (var k in keys.Children.OfType<YamlMappingNode>())
            {
                RenameKeyOf(k, "vup", "up");
                Sweep(k, typeof(CameraMotionKeyData), null);
            }
        Sweep(c, typeof(CameraData), null);
    }

    // ── World ────────────────────────────────────────────────────────────────

    private void MigrateWorld(YamlMappingNode w)
    {
        if (Val(w, "sky") is YamlMappingNode sky) MigrateSky(sky);
        if (Val(w, "ground") is YamlMappingNode ground) MigrateGround(ground);
        if (Val(w, "medium") is YamlMappingNode medium) MigrateMedium(medium);
        Sweep(w, typeof(WorldData), null);
    }

    private static readonly string[] PhysicalSkies = ["preetham", "hosek_wilkie", "nishita"];

    private void MigrateSky(YamlMappingNode sky)
    {
        string type = Str(sky, "type") ?? "flat";
        if (Key(sky, "type") == null) Add(sky, "type", "flat");
        if (type == "physical") { SetType(sky, "hosek_wilkie"); type = "hosek_wilkie"; }
        else if (type is not ("flat" or "gradient" or "hdri" or "preetham" or "hosek_wilkie" or "nishita"))
        {
            Note(sky, $"sky type '{type}' was unknown to format 1 (a flat sky of (0.5, 0.7, 1)): now that flat sky");
            SetType(sky, "flat");
            Put(sky, "color", "color", "[0.5, 0.7, 1.0]");
            type = "flat";
        }

        // The multiplier as an exposure. Format 1 ignored it on flat and
        // gradient skies; a physical sky in the relative scale is its
        // photometric self dimmed by a fixed factor.
        double scale = 1;
        if (type == "hdri") scale = NumOf(sky, "intensity") ?? 1;
        else if (PhysicalSkies.Contains(type))
        {
            double i = NumOf(sky, "intensity") ?? 1;
            scale = i > 0 ? i : 1;
            bool relative = Str(sky, "units") is null or "relative";
            if (relative) scale *= type == "nishita" ? 20.0 / 133_800.0 : 1.0 / 1000.0;
        }
        if (Key(sky, "units") is { } u) _ed.RemovePair(sky, u);
        double ev = Math.Log2(Math.Max(scale, 1e-30));
        if (Math.Abs(ev) > 1e-6) Put(sky, "intensity", "exposure", Num(ev));
        else Remove(sky, "intensity");

        if (Key(sky, "rotation") is { } rk)
        {
            double r = NumOf(sky, "rotation") ?? 0;
            if (Math.Abs(r) > 1e-6)
            {
                // Format 1 turned the sky by -r about Y, an HDRI twice (once
                // in its map lookup, once in the sky orientation).
                double turn = type == "hdri" ? -2 * r : -r;
                RenameTracked(sky, rk, "rotate");
                _ed.Replace(sky.Children[rk], Vec([0, turn, 0]));
                Note(rk, $"sky rotation {r} is rotate: [0, {Num(turn)}, 0]" + (type == "hdri" ? " (format 1 turned an HDRI twice)" : ""));
            }
            else _ed.RemovePair(sky, rk);
        }
        if (Key(sky, "orientation") is { } ok)
            Note(ok, "sky 'orientation' is now 'rotate' (Euler as an entity): convert it by hand");

        if (Val(sky, "sun") is YamlMappingNode sun) MigrateSun(sun, type);
        if (Val(sky, "visibility") is YamlMappingNode vis) Sweep(vis, typeof(VisibilityData), null);
        if (Val(sky, "background") is YamlMappingNode bg) MigrateSky(bg);
        Sweep(sky, typeof(SkyData), type);
    }

    private void MigrateSun(YamlMappingNode sun, string skyType)
    {
        if (skyType == "gradient" && Val(sun, "direction") != null)
        {
            // Format 1: the disc's radiance colour × intensity over a half-angle
            // angular_radius, or half of size (default 3°). Format 2: the
            // illuminance E = L π sin²θ, the colour a tint.
            double ar = NumOf(sun, "angular_radius") ?? 0;
            double half = ar > 0 ? ar : Math.Max(0.01, (NumOf(sun, "size") ?? 3.0) * 0.5);
            var color = Vec3(Nums(sun, "color")) ?? Vector3.One;
            double radiance = Lum(color) * (NumOf(sun, "intensity") ?? 10.0);
            double s = Math.Sin(half * Math.PI / 180.0);
            Put(sun, "intensity", "illuminance", Num(radiance * Math.PI * s * s));
            Put(sun, "angular_radius", "angular_radius", Num(half));
            Remove(sun, "size");
        }
        else
        {
            // Not read by format 1 on these skies.
            foreach (var k in new[] { "intensity", "color", "size", "angular_radius" }) Remove(sun, k);
        }
        VisibleToCamera(sun);
        Sweep(sun, typeof(SunData), null);
    }

    private void MigrateGround(YamlMappingNode g)
    {
        string? type = Str(g, "type");
        if (type == "plane" || type == "") SetType(g, "infinite_plane");
        else if (type is "heightfield" or "terrain" or "height_field")
            Note(g, "a heightfield ground is an entity of type heightfield in format 2: move it by hand");

        if (Key(g, "y") is { } yk)
        {
            if (Key(g, "point") == null)
            {
                RenameTracked(g, yk, "point");
                _ed.Replace(g.Children[yk], Vec([0, NumOf(g, "y") ?? 0, 0]));
            }
            else _ed.RemovePair(g, yk);
        }

        // The inline material shorthand built an anonymous Disney material.
        bool inline = Key(g, "color") != null || Key(g, "roughness") != null || Key(g, "metallic") != null;
        if (inline)
        {
            if (Key(g, "material") == null)
            {
                var c = Vec3(Nums(g, "color")) ?? new Vector3(0.5f);
                string id = "ground_material";
                string mat = $"{{ id: {id}, type: openpbr, base_color: {Vec([c.X, c.Y, c.Z])}, " +
                             $"specular_roughness: {Num(NumOf(g, "roughness") ?? 0.8)}" +
                             (NumOf(g, "metallic") is double mt && mt != 0 ? $", base_metalness: {Num(mt)}" : "") + " }";
                _extraMaterials.Add(mat);
                Add(g, "material", id);
                Note(g, "the ground's inline colour/roughness/metallic is now the material 'ground_material'");
            }
            foreach (var k in new[] { "color", "roughness", "metallic" }) Remove(g, k);
        }
        if (Val(g, "visibility") is YamlMappingNode vis) Sweep(vis, typeof(VisibilityData), null);
        Sweep(g, typeof(GroundData), Str(g, "type") is "plane" or null or "" ? "infinite_plane" : Str(g, "type"));
    }

    private void MigrateMedium(YamlMappingNode m)
    {
        string? type = Str(m, "type");
        if (type == null) Add(m, "type", "homogeneous");
        if (type is "nishita" or "aerial_perspective") SetType(m, "atmosphere");
        string? phase = Str(m, "phase");
        if (phase == "henyey_greenstein") Set(m, "phase", "hg");
        if (phase == "double_henyey_greenstein") Set(m, "phase", "double_hg");
        string? interp = Str(m, "interpolation");
        if (interp is "linear" or "") Set(m, "interpolation", "trilinear");
        if (interp is "cubic" or "catmull-rom" or "catmull_rom" or "smooth") Set(m, "interpolation", "tricubic");
        Remove(m, "world_scale");
        string v2Type = type is "nishita" or "aerial_perspective" ? "atmosphere" : type ?? "homogeneous";
        Sweep(m, typeof(MediumData), v2Type);
    }
}
