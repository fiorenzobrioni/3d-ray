using System.Numerics;
using RayTracer.Scene;
using YamlDotNet.RepresentationModel;
using static SceneMigrate.YamlEdits;

namespace SceneMigrate;

public sealed partial class Migrator
{
    // ── Entities ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> EntityTypes = new()
    {
        ["obj"] = "mesh", ["smooth_triangle"] = "triangle", ["plane"] = "infinite_plane",
        ["truncated_cone"] = "cone", ["frustum"] = "cone", ["donut"] = "torus", ["ring"] = "torus",
        ["revolution"] = "lathe", ["surface_of_revolution"] = "lathe", ["prism"] = "extrusion",
        ["linear_extrude"] = "extrusion", ["pill"] = "capsule", ["sphylinder"] = "capsule",
        ["ring_disk"] = "annulus", ["washer"] = "annulus", ["height_field"] = "heightfield", ["terrain"] = "heightfield",
    };

    private static readonly string[] EntityKinds =
        ["sphere", "box", "triangle", "quad", "cylinder", "cone", "torus", "capsule", "disk", "annulus",
         "infinite_plane", "lathe", "extrusion", "mesh", "heightfield", "csg", "group", "instance"];

    /// <summary>Comments out a whole list item (format 1 built nothing from it).</summary>
    private void CommentOutItem(YamlMappingNode item, string why)
    {
        int line = _ed.LineStart(Start(item));
        int last = _ed.LastLineEnd(item);
        int col = Start(item) - line;
        // The dash sits before the item's first key.
        int dash = _ed.Text.LastIndexOf('-', Math.Max(Start(item) - 1, 0));
        if (dash >= line) col = dash - line;
        while (line <= last && line < _ed.Text.Length)
        {
            int end = _ed.Text.IndexOf('\n', line);
            if (end < 0) end = _ed.Text.Length;
            int lead = 0;
            while (line + lead < end && _ed.Text[line + lead] == ' ') lead++;
            if (line + lead < end) _ed.Insert(line + Math.Min(lead, col), "# ");
            line = end + 1;
        }
        Note(item, why);
    }

    private void MigrateEntity(YamlMappingNode e, bool template = false)
    {
        string? type = Str(e, "type");
        if (type != null && EntityTypes.TryGetValue(type, out var canon)) { SetType(e, canon); type = canon; }
        if (!template && (type == null || !EntityKinds.Contains(type)))
        {
            CommentOutItem(e, $"entity of type '{type ?? "(none)"}': format 1 built nothing from it; commented out");
            return;
        }

        VisibleToCamera(e);
        RenameKeyOf(e, "twist_degrees", "twist");
        if (Str(e, "operation") is "subtract" or "difference") Set(e, "operation", "subtraction");
        if (Str(e, "profile_type") is "catmull" or "smooth") Set(e, "profile_type", "catmull_rom");
        if (Str(e, "profile_type") == "bézier") Set(e, "profile_type", "bezier");
        if (Str(e, "caps") is "bottom") Set(e, "caps", "start");
        if (Str(e, "caps") is "top") Set(e, "caps", "end");
        if (Str(e, "caps") is "off") Set(e, "caps", "none");
        if (Str(e, "subdivision_scheme") is "catmull-clark" or "cc" or "catmullclark") Set(e, "subdivision_scheme", "catmull_clark");
        if (Str(e, "subdivision_boundary") == "edge-and-corner") Set(e, "subdivision_boundary", "edge_and_corner");
        if (Str(e, "subdivision_boundary") == "edge-only") Set(e, "subdivision_boundary", "edge_only");

        if (Val(e, "height_texture") is YamlMappingNode ht) MigrateTexture(ht);
        if (Val(e, "strata") is YamlSequenceNode strata)
            foreach (var s in strata.Children.OfType<YamlMappingNode>())
            {
                RenameKeyOf(s, "min_slope_deg", "min_slope");
                RenameKeyOf(s, "max_slope_deg", "max_slope");
                Sweep(s, typeof(StratumData), null);
            }
        if (Val(e, "motion") is YamlSequenceNode motion)
            foreach (var k in motion.Children.OfType<YamlMappingNode>()) Sweep(k, typeof(MotionKeyData), null);
        if (Val(e, "children") is YamlSequenceNode children)
            foreach (var c in children.Children.OfType<YamlMappingNode>()) MigrateEntity(c);
        if (Val(e, "left") is YamlMappingNode l) MigrateEntity(l);
        if (Val(e, "right") is YamlMappingNode r) MigrateEntity(r);
        Sweep(e, typeof(EntityData), template && type == null ? "group" : type);
    }

    // ── Lights ───────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> LightTypes = new()
    {
        ["sun"] = "directional", ["spotlight"] = "spot", ["area_light"] = "area", ["rect"] = "area",
        ["rect_light"] = "area", ["sphere_light"] = "sphere", ["ball"] = "sphere", ["ball_light"] = "sphere",
        ["portal_light"] = "portal",
    };

    private void MigrateLight(YamlMappingNode l)
    {
        string? type = Str(l, "type");
        if (type != null && LightTypes.TryGetValue(type, out var canon)) { SetType(l, canon); type = canon; }
        if (type is not ("point" or "spot" or "directional" or "area" or "sphere" or "portal"))
        {
            CommentOutItem(l, $"light of type '{type ?? "(none)"}': format 1 made no light of it; commented out");
            return;
        }
        if (Str(l, "power_unit") is { } pu)
            Set(l, "power_unit", pu is "lumen" or "lumens" or "lm" ? "lumen" : "watt");
        RenameKeyOf(l, "anchor", "corner");
        VisibleToCamera(l);

        // Format 1 converted candela and power without the scene's scale;
        // format 2 reads them in metres.
        double m2 = _mpu * _mpu;
        var color = Vec3(Nums(l, "color")) ?? Vector3.One;
        double? kelvin = NumOf(l, "temperature");
        double intensity = NumOf(l, "intensity") ?? 1;
        double tinted = Lum(Tinted(color, kelvin));
        bool hasIes = Key(l, "ies") != null;
        double? power = NumOf(l, "power");
        void ScalePower() { if (power is double p && m2 != 1) Set(l, "power", Num(p * m2)); }

        switch (type)
        {
            case "point" or "spot":
                Remove(l, "luminance"); Remove(l, "illuminance"); Remove(l, "normalize");
                if (hasIes)
                {
                    // Format 1: the profile scaled to candela's peak, power's flux, or by intensity.
                    if (NumOf(l, "candela") is double peak) { Remove(l, "intensity"); Put(l, "candela", "intensity", Num(peak * m2)); }
                    else if (power != null) { Remove(l, "intensity"); ScalePower(); }
                    else
                    {
                        double ev = Math.Log2(Math.Max(intensity * m2, 1e-30)) + (NumOf(l, "exposure") ?? 0);
                        Remove(l, "intensity");
                        if (Math.Abs(ev) > 1e-6) Put(l, "exposure", "exposure", Num(ev));
                    }
                }
                else if (NumOf(l, "candela") is double cd) { Remove(l, "intensity"); Put(l, "candela", "intensity", Num(cd * m2)); }
                else if (power != null) { Remove(l, "intensity"); ScalePower(); }
                else Put(l, "intensity", "intensity", Num(intensity * tinted * m2));
                break;

            case "area" or "sphere":
                Remove(l, "candela"); Remove(l, "illuminance");
                if (Key(l, "luminance") != null) { Remove(l, "intensity"); Remove(l, "power"); Remove(l, "normalize"); }
                else if (power != null) { Remove(l, "intensity"); Remove(l, "normalize"); ScalePower(); }
                else
                {
                    double area = 1;
                    if (type == "area" && Vec3(Nums(l, "u")) is Vector3 u && Vec3(Nums(l, "v")) is Vector3 v)
                        area = Vector3.Cross(u, v).Length();
                    else if (type == "sphere")
                    {
                        double rad = NumOf(l, "radius") ?? 0.5;
                        area = 4 * Math.PI * rad * rad;
                    }
                    double amount = intensity;
                    if (Bool(l, "normalize") == true && area > 0) amount /= area;
                    Remove(l, "normalize");
                    Put(l, "intensity", "luminance", Num(amount * tinted));
                }
                break;

            case "directional":
                Remove(l, "candela"); Remove(l, "luminance"); Remove(l, "power"); Remove(l, "power_unit"); Remove(l, "normalize");
                if (Key(l, "illuminance") != null) Remove(l, "intensity");
                else Put(l, "intensity", "illuminance", Num(intensity * tinted));
                break;

            case "portal":
                break;
        }
        Sweep(l, typeof(LightData), type);
    }
}
