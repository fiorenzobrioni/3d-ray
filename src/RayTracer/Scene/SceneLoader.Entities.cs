using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Geometry.Subdivision;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;
using RayTracer.Textures;
using RayTracer.Rendering;
using RayTracer.Rendering.Sky;
using RayTracer.Volumetrics;

namespace RayTracer.Scene;
public partial class SceneLoader
{
    private static IHittable? CreateEntity(EntityData e, IMaterial mat, int entityIndex = 0)
    {
        IHittable? entity = e.Type?.ToLowerInvariant() switch
        {
            "sphere"   => new Sphere(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, mat),
            "box"      => new Box(mat),
            "triangle" or "smooth_triangle"
                       => CreateTriangleEntity(e, mat),
            "quad"     => new Quad(
                ToVector3(e.Q) ?? Vector3.Zero,
                ToVector3(e.U) ?? Vector3.UnitX,
                ToVector3(e.V) ?? Vector3.UnitY, mat),
            "cylinder" => new Cylinder(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, e.Height, mat),
            "cone" or "truncated_cone" or "frustum"
                       => new Cone(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, e.TopRadius, e.Height, mat),
            "torus" or "donut" or "ring"
                       => CreateTorusEntity(e, mat),
            "lathe" or "revolution" or "surface_of_revolution"
                       => CreateLatheEntity(e, mat),
            "extrusion" or "prism" or "linear_extrude"
                       => CreateExtrusionEntity(e, mat),
            "capsule" or "pill" or "sphylinder"
                       => new Capsule(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, e.Height, mat),
            "annulus" or "ring_disk" or "washer"
                       => new Annulus(ToVector3(e.Center) ?? Vector3.Zero, ToVector3(e.Normal) ?? Vector3.UnitY, e.Radius, e.InnerRadius, mat),
            "disk"     => new Disk(ToVector3(e.Center) ?? Vector3.Zero, ToVector3(e.Normal) ?? Vector3.UnitY, e.Radius, mat),
            "plane" or "infinite_plane"
                       => new InfinitePlane(ToVector3(e.Point) ?? Vector3.Zero, ToVector3(e.Normal) ?? Vector3.UnitY, mat),
            "csg"      => null, // Handled separately in Load() — needs materials dictionary
            "group"    => null, // Handled separately in Load() — needs materials dictionary and sceneDir
            "instance" => null, // Handled separately in Load() — needs templates dictionary
            _          => null
        };

        if (entity != null)
        {
            // BUG-05 fix: derive seed deterministically from entity index + type so
            // that two renders of the same YAML always produce identical results for
            // procedural textures using randomize_offset / randomize_rotation.
            // Explicit "seed" in YAML always takes precedence.
            entity.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
        }

        return entity;
    }

    private static bool IsHeightFieldType(string? type) => type?.ToLowerInvariant() switch
    {
        "heightfield" or "height_field" or "terrain" => true,
        _ => false,
    };

    /// <summary>
    /// Builds a <see cref="HeightField"/> from YAML. Either <c>heightmap_path</c>
    /// (baked PNG-16) or <c>height_texture</c> (procedural noise) must be
    /// provided; the path wins when both are set.
    /// </summary>
    private static IHittable? CreateHeightFieldEntity(EntityData e, IMaterial fallbackMat,
        Dictionary<string, IMaterial> materials, string sceneDir, int entityIndex)
    {
        if (e.Bounds == null || e.Bounds.Count < 4)
        {
            Warn($"HeightField entity '{e.Name ?? "(unnamed)"}' requires 'bounds: [xMin, zMin, xMax, zMax]'. Skipping.");
            return null;
        }
        float xMin = e.Bounds[0], zMin = e.Bounds[1], xMax = e.Bounds[2], zMax = e.Bounds[3];
        if (xMax <= xMin || zMax <= zMin)
        {
            Warn($"HeightField entity '{e.Name ?? "(unnamed)"}': bounds must satisfy xMax>xMin and zMax>zMin. Skipping.");
            return null;
        }
        if (e.HeightScale <= 0f)
        {
            Warn($"HeightField entity '{e.Name ?? "(unnamed)"}': height_scale must be > 0. Skipping.");
            return null;
        }

        IMaterial? seaMat = null;
        if (!string.IsNullOrWhiteSpace(e.SeaMaterial))
            seaMat = GetMaterial(materials, e.SeaMaterial);

        List<HeightField.StratumBand>? strata = null;
        if (e.Strata != null && e.Strata.Count > 0)
        {
            strata = new List<HeightField.StratumBand>(e.Strata.Count);
            foreach (var s in e.Strata)
            {
                if (string.IsNullOrWhiteSpace(s.Material))
                {
                    Warn($"HeightField '{e.Name ?? "(unnamed)"}': stratum without 'material' skipped.");
                    continue;
                }
                strata.Add(new HeightField.StratumBand
                {
                    MinAltitude = s.MinAltitude,
                    MaxAltitude = s.MaxAltitude,
                    MinSlopeDeg = s.MinSlopeDeg,
                    MaxSlopeDeg = s.MaxSlopeDeg,
                    BlendWidth  = s.BlendWidth,
                    Material    = GetMaterial(materials, s.Material),
                });
            }
            if (strata.Count == 0) strata = null;
        }

        HeightField hf;
        if (!string.IsNullOrWhiteSpace(e.HeightmapPath))
        {
            if (e.HeightTexture != null)
                Warn($"HeightField '{e.Name ?? "(unnamed)"}' has both 'heightmap_path' and 'height_texture' — using the baked path.");

            string fullPath = Path.IsPathRooted(e.HeightmapPath)
                ? e.HeightmapPath
                : Path.Combine(sceneDir, e.HeightmapPath);
            if (!File.Exists(fullPath))
            {
                Warn($"HeightField '{e.Name ?? "(unnamed)"}': heightmap file not found at '{fullPath}'. Skipping.");
                return null;
            }

            if (!HeightmapLoader.IsHighPrecision(fullPath))
                Warn($"HeightField '{e.Name ?? "(unnamed)"}': '{Path.GetFileName(fullPath)}' is not 16-bit — terracing may appear on smooth slopes.");

            float[] samples;
            int sx, sz;
            try
            {
                samples = HeightmapLoader.Load(fullPath, out sx, out sz);
            }
            catch (Exception ex)
            {
                Warn($"HeightField '{e.Name ?? "(unnamed)"}': failed to load heightmap '{fullPath}': {ex.Message}. Skipping.");
                return null;
            }
            hf = new HeightField(xMin, zMin, xMax, zMax,
                                 samples, sx, sz,
                                 e.HeightScale, fallbackMat,
                                 e.SeaLevel, seaMat, strata);
        }
        else if (e.HeightTexture != null)
        {
            ITexture tex = CreateTexture(e.HeightTexture, sceneDir, TextureRole.Data);
            int res = e.Resolution > 0 ? e.Resolution : 512;
            hf = HeightField.FromProceduralTexture(
                xMin, zMin, xMax, zMax,
                tex, res,
                e.HeightScale, fallbackMat,
                e.SeaLevel, seaMat, strata);
        }
        else
        {
            Warn($"HeightField '{e.Name ?? "(unnamed)"}' needs either 'heightmap_path' or 'height_texture'. Skipping.");
            return null;
        }

        hf.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
        return hf;
    }

    /// <summary>
    /// Creates a Torus centered at the origin, optionally wrapped in a
    /// translation Transform when a <c>center</c> is specified in YAML.
    ///
    /// The Torus primitive is always defined at the origin (XZ plane, Y axis
    /// as hole axis). When the YAML specifies a <c>center</c>, the torus is
    /// wrapped in a Translate transform — consistent with how Sphere, Cylinder,
    /// Cone, and Capsule handle their <c>center</c> parameter.
    ///
    /// Any additional transforms (scale/rotate/translate from YAML) are applied
    /// on top by the caller via <see cref="ComputeTransformMatrix"/>.
    /// </summary>
    private static IHittable CreateTorusEntity(EntityData e, IMaterial mat)
    {
        IHittable torus = new Torus(e.MajorRadius, e.MinorRadius, mat);

        var center = ToVector3(e.Center);
        if (center.HasValue && center.Value != Vector3.Zero)
            torus = new Transform(torus, Matrix4x4.CreateTranslation(center.Value));

        return torus;
    }

    /// <summary>
    /// Creates a <see cref="Lathe"/> from the YAML <c>profile</c>, optional
    /// <c>profile_type</c> (<c>linear</c> / <c>catmull_rom</c> / <c>bezier</c>)
    /// and, for the Bezier mode, <c>profile_bezier_controls</c>. Validates the
    /// profile (≥ 2 points, r ≥ 0, y monotonic) and routes bad inputs to a
    /// deferred warning rather than crashing the load.
    /// </summary>
    private static IHittable? CreateLatheEntity(EntityData e, IMaterial mat)
    {
        if (e.Profile == null || e.Profile.Count < 2)
        {
            Warn($"Lathe entity '{e.Name ?? "(unnamed)"}' requires a 'profile' of at least 2 [r, y] points. Skipping.");
            return null;
        }

        var pts = new List<Vector2>(e.Profile.Count);
        foreach (var p in e.Profile)
        {
            if (p == null || p.Count < 2)
            {
                Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': each profile point must be [r, y]. Skipping entity.");
                return null;
            }
            float r = p[0];
            float y = p[1];
            if (r < 0f)
            {
                Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': negative radius {r} clamped to 0.");
                r = 0f;
            }
            pts.Add(new Vector2(r, y));
        }

        // Ensure y is monotonically non-decreasing — silently sort if not.
        bool needsSort = false;
        for (int i = 1; i < pts.Count; i++)
            if (pts[i].Y < pts[i - 1].Y) { needsSort = true; break; }
        if (needsSort)
        {
            Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': profile y values were not monotonic; sorting by y.");
            pts.Sort((a, b) => a.Y.CompareTo(b.Y));
        }

        string rawMode = (e.ProfileType ?? "linear").Trim().ToLowerInvariant();
        LatheMode mode = rawMode switch
        {
            "linear" or ""            => LatheMode.Linear,
            "catmull_rom" or "catmull" or "smooth"
                                      => LatheMode.CatmullRom,
            "bezier" or "bézier"      => LatheMode.Bezier,
            _ => LatheMode.Linear,
        };
        if (mode == LatheMode.Linear && !string.IsNullOrEmpty(rawMode) && rawMode != "linear")
            Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': unknown profile_type '{e.ProfileType}'. Falling back to 'linear'.");

        if (mode == LatheMode.CatmullRom && pts.Count < 4)
        {
            Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': Catmull-Rom needs at least 4 points, got {pts.Count}. " +
                 "Falling back to 'linear'.");
            mode = LatheMode.Linear;
        }

        List<Vector2>? controls = null;
        if (mode == LatheMode.Bezier)
        {
            int expected = 4 * (pts.Count - 1);
            if (e.ProfileBezierControls == null || e.ProfileBezierControls.Count != expected)
            {
                Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': profile_bezier_controls requires exactly " +
                     $"{expected} entries (got {e.ProfileBezierControls?.Count ?? 0}). Skipping entity.");
                return null;
            }
            controls = new List<Vector2>(expected);
            foreach (var c in e.ProfileBezierControls)
            {
                if (c == null || c.Count < 2)
                {
                    Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': each bezier control must be [r, y]. Skipping entity.");
                    return null;
                }
                controls.Add(new Vector2(MathF.Max(0f, c[0]), c[1]));
            }
        }

        IHittable lathe;
        try
        {
            lathe = new Lathe(pts, mode, mat, controls);
        }
        catch (ArgumentException ex)
        {
            Warn($"Lathe entity '{e.Name ?? "(unnamed)"}': {ex.Message}. Skipping.");
            return null;
        }

        var center = ToVector3(e.Center);
        if (center.HasValue && center.Value != Vector3.Zero)
            lathe = new Transform(lathe, Matrix4x4.CreateTranslation(center.Value));

        return lathe;
    }

    /// <summary>
    /// Creates an <see cref="Extrusion"/> from the YAML <c>profile</c>
    /// (closed XZ polygon), <c>height</c>, optional <c>profile_type</c>
    /// (linear / catmull_rom / bezier) plus <c>profile_bezier_controls</c>
    /// for the Bezier mode, and the optional <c>caps</c> / <c>twist_degrees</c>
    /// / <c>taper</c> / <c>curve_samples</c> modifiers. Validates the profile
    /// and routes bad inputs to a deferred warning rather than crashing the
    /// load.
    ///
    /// <b>Local frame:</b> the resulting <c>Extrusion</c> sits at
    /// <c>y ∈ [0, height]</c> in its local frame; <c>center</c> is applied as
    /// a pure translation, <em>not</em> a recentre. Authors who want a
    /// centred prism should set <c>center: [cx, cy − height/2, cz]</c> or wrap
    /// the entity in a parent transform.
    ///
    /// <b>Caps and CSG / volumetrics:</b> only <c>caps: both</c> produces a
    /// closed manifold; the open variants (<c>start</c>, <c>end</c>,
    /// <c>none</c>) are intended for purely visual surfaces and will misbehave
    /// inside CSG operations and as participating-media boundaries (rays may
    /// "leak" through the missing face).
    /// </summary>
    private static IHittable? CreateExtrusionEntity(EntityData e, IMaterial mat)
    {
        if (e.Profile == null || e.Profile.Count < 3)
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}' requires a 'profile' of at least 3 [x, z] points (closed loop). Skipping.");
            return null;
        }
        if (!(e.Height > 0f))
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': 'height' must be positive (got {e.Height}). Skipping.");
            return null;
        }
        if (!(e.Taper > 0f))
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': 'taper' must be positive (got {e.Taper}). Skipping.");
            return null;
        }

        // Parse the closed profile and drop accidental duplicate consecutive
        // vertices — they make ear clipping unstable for no visible gain.
        var pts = new List<Vector2>(e.Profile.Count);
        foreach (var p in e.Profile)
        {
            if (p == null || p.Count < 2)
            {
                Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': each profile point must be [x, z]. Skipping entity.");
                return null;
            }
            var v = new Vector2(p[0], p[1]);
            if (pts.Count == 0 || (pts[^1] - v).LengthSquared() > 1e-12f)
                pts.Add(v);
        }
        if (pts.Count >= 2 && (pts[^1] - pts[0]).LengthSquared() <= 1e-12f)
            pts.RemoveAt(pts.Count - 1); // YAML authors sometimes close the loop manually
        if (pts.Count < 3)
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': profile collapsed to fewer than 3 distinct points. Skipping.");
            return null;
        }

        string rawMode = (e.ProfileType ?? "linear").Trim().ToLowerInvariant();
        ExtrusionMode mode = rawMode switch
        {
            "linear" or ""            => ExtrusionMode.Linear,
            "catmull_rom" or "catmull" or "smooth"
                                      => ExtrusionMode.CatmullRom,
            "bezier" or "bézier"      => ExtrusionMode.Bezier,
            _ => ExtrusionMode.Linear,
        };
        if (mode == ExtrusionMode.Linear && !string.IsNullOrEmpty(rawMode) && rawMode != "linear")
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': unknown profile_type '{e.ProfileType}'. Falling back to 'linear'.");

        if (mode == ExtrusionMode.CatmullRom && pts.Count < 3)
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': Catmull-Rom needs at least 3 points, got {pts.Count}. Falling back to 'linear'.");
            mode = ExtrusionMode.Linear;
        }

        List<Vector2>? controls = null;
        if (mode == ExtrusionMode.Bezier)
        {
            int expected = 4 * pts.Count;
            if (e.ProfileBezierControls == null || e.ProfileBezierControls.Count != expected)
            {
                Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': profile_bezier_controls requires exactly " +
                     $"{expected} entries (one cubic per profile segment in a closed loop), got " +
                     $"{e.ProfileBezierControls?.Count ?? 0}. Skipping entity.");
                return null;
            }
            controls = new List<Vector2>(expected);
            foreach (var c in e.ProfileBezierControls)
            {
                if (c == null || c.Count < 2)
                {
                    Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': each bezier control must be [x, z]. Skipping entity.");
                    return null;
                }
                controls.Add(new Vector2(c[0], c[1]));
            }
        }

        ExtrusionCaps caps = (e.Caps ?? "both").Trim().ToLowerInvariant() switch
        {
            "both" or ""    => ExtrusionCaps.Both,
            "start" or "bottom" => ExtrusionCaps.Start,
            "end" or "top"  => ExtrusionCaps.End,
            "none" or "off" => ExtrusionCaps.None,
            _ => ExtrusionCaps.Both,
        };

        IHittable extrusion;
        try
        {
            extrusion = new Extrusion(pts, mode, e.Height, caps, mat,
                bezierControls: controls,
                twistDegrees: e.TwistDegrees,
                taper: e.Taper,
                curveSamples: Math.Max(2, e.CurveSamples),
                creaseAngleDeg: e.CreaseAngle);
        }
        catch (ArgumentException ex)
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': {ex.Message}. Skipping.");
            return null;
        }

        var center = ToVector3(e.Center);
        if (center.HasValue && center.Value != Vector3.Zero)
            extrusion = new Transform(extrusion, Matrix4x4.CreateTranslation(center.Value));

        return extrusion;
    }

    /// <summary>
    /// Creates a CSG (Constructive Solid Geometry) entity from nested left/right
    /// children and a Boolean operation.
    ///
    /// CSG entities are defined in YAML with inline children:
    /// <code>
    ///   - name: "lens"
    ///     type: "csg"
    ///     operation: "intersection"
    ///     left:
    ///       type: "sphere"
    ///       center: [0, 1, 0]
    ///       radius: 1.0
    ///       material: "glass"         # Per-child material (optional)
    ///     right:
    ///       type: "sphere"
    ///       center: [0, 1, 0.8]
    ///       radius: 1.0
    ///     material: "matte_white"     # Fallback for children without own material
    /// </code>
    ///
    /// Each child must be a solid primitive (sphere, box, cylinder, cone,
    /// torus, capsule, quad, disk, annulus, triangle, lathe) or another "csg"
    /// node for nested Boolean trees like (A ∪ B) \ C. Groups, meshes,
    /// instances and infinite planes are NOT accepted as CSG children —
    /// BuildCsgChild returns null and the entire CSG node is dropped with
    /// a "failed to create one or both children" warning. Children inherit
    /// the parent's material unless they specify their own via material ID.
    /// </summary>
    private static IHittable? CreateCsgEntity(EntityData e, IMaterial parentMat,
        Dictionary<string, IMaterial> materials, int entityIndex)
    {
        if (e.Left == null || e.Right == null)
        {
            Warn($"CSG entity '{e.Name ?? "(unnamed)"}' requires both 'left' and 'right' children. Skipping.");
            return null;
        }
 
        if (string.IsNullOrWhiteSpace(e.Operation))
        {
            Warn($"CSG entity '{e.Name ?? "(unnamed)"}' requires an 'operation' " +
                 "(union, intersection, subtraction). Skipping.");
            return null;
        }
 
        var operation = e.Operation.ToLowerInvariant() switch
        {
            "union"                                      => CsgOperation.Union,
            "intersection"                               => CsgOperation.Intersection,
            "subtraction" or "subtract" or "difference"  => CsgOperation.Subtraction,
            _ => (CsgOperation?)null
        };
 
        if (operation == null)
        {
            Warn($"CSG entity '{e.Name ?? "(unnamed)"}': unknown operation '{e.Operation}'. " +
                 "Valid values: union, intersection, subtraction. Skipping.");
            return null;
        }
 
        // Build children recursively. Each child resolves its own material or
        // falls back to the parent CSG entity's material.
        var leftHittable  = BuildCsgChild(e.Left,  parentMat, materials, entityIndex * 1000 + 1);
        var rightHittable = BuildCsgChild(e.Right, parentMat, materials, entityIndex * 1000 + 2);
 
        if (leftHittable == null || rightHittable == null)
        {
            Warn($"CSG entity '{e.Name ?? "(unnamed)"}': failed to create one or both children. Skipping.");
            return null;
        }
 
        var csg = new CsgObject(operation.Value, leftHittable, rightHittable);

        // Assign a deterministic seed (same convention as regular primitives /
        // groups / meshes). Explicit "seed:" in YAML wins; otherwise we derive
        // one from entity index + type + name. The CsgObject setter then
        // propagates the same value to Left and Right so the whole solid
        // shares a uniform procedural-texture pattern.
        csg.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);

        return csg;
    }

    /// <summary>
    /// Builds a single CSG child entity, resolving its material (own ID or parent
    /// fallback) and applying any local transforms. Supports recursive nesting —
    /// a child of type "csg" triggers another CreateCsgEntity() call.
    /// </summary>
    private static IHittable? BuildCsgChild(EntityData child, IMaterial fallbackMat,
        Dictionary<string, IMaterial> materials, int childIndex)
    {
        // Per-child material: if the child specifies a material ID, resolve it
        // from the scene's materials dictionary. Otherwise, inherit from parent.
        var mat = child.Material != null
            ? GetMaterial(materials, child.Material)
            : fallbackMat;

        if (child.Motion is { Count: > 0 })
            Warn($"CSG child '{child.Name ?? child.Type ?? "(unnamed)"}': 'motion' is only " +
                 $"supported on top-level entities — ignored. Animate the CSG entity itself.");

        IHittable? hittable;
        if (string.Equals(child.Type, "csg", StringComparison.OrdinalIgnoreCase))
        {
            // Recursive CSG — child is itself a Boolean operation
            hittable = CreateCsgEntity(child, mat, materials, childIndex);
        }
        else
        {
            hittable = CreateEntity(child, mat, childIndex);
        }
 
        if (hittable == null) return null;
 
        // Apply child-level transforms (scale, rotate, translate)
        var transform = ComputeTransformMatrix(child);
        if (transform != Matrix4x4.Identity)
            hittable = new Transform(hittable, transform);
 
        return hittable;
    }

    /// <summary>
    /// Creates a mesh entity from a model file (.obj with its MTL, .gltf,
    /// .glb, .ply; <see cref="MeshImporter"/>), resolved relative to the scene
    /// YAML, with subdivision and the material's displacement. The entity's
    /// <c>material</c>, when given, covers every face; otherwise the file's
    /// own materials are used, renamed by <c>material_overrides</c>, and
    /// <paramref name="mat"/> (the default, or a group's) covers faces with none.
    /// </summary>
    private static IHittable? CreateMeshEntity(EntityData e, IMaterial mat,
        Dictionary<string, IMaterial> materials, string sceneDir, int entityIndex)
    {
        var screen = _currentScreenContext;
        string label = e.Name ?? "(unnamed)";
        if (string.IsNullOrWhiteSpace(e.Path))
        {
            Warn($"Mesh entity '{label}' requires a 'path' to a model file. Skipping.");
            return null;
        }

        string modelPath = Path.IsPathRooted(e.Path) ? e.Path : Path.Combine(sceneDir, e.Path);
        if (!File.Exists(modelPath))
        {
            Warn($"Mesh entity '{label}': model file not found at '{modelPath}'. Skipping.");
            return null;
        }
        if (!MeshImporter.IsSupported(modelPath))
        {
            Warn($"Mesh entity '{label}': '{Path.GetExtension(modelPath)}' is not a supported model " +
                 "format (.obj, .gltf, .glb, .ply). Skipping.");
            return null;
        }
        string shown = e.Name ?? Path.GetFileName(modelPath);

        // The entity transform is folded into the screen-space context so the
        // adaptive heuristic measures the world-space projected edge length.
        var subdivision = BuildSubdivisionOptions(e, screen);

        // Displacement lives on the material; the entity can suppress it.
        int seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
        var matDisp = e.DisplacementEnabled ? mat.Displacement : null;

        Dictionary<string, IMaterial>? overrides = null;
        if (e.MaterialOverrides is { Count: > 0 })
        {
            overrides = new Dictionary<string, IMaterial>(StringComparer.Ordinal);
            foreach (var (fileName, id) in e.MaterialOverrides)
                overrides[fileName] = GetMaterial(materials, id);
        }

        var warnings = new List<string>();
        var imported = MeshImporter.Read(modelPath, warnings);
        ImportResult? result = imported == null ? null
            : MeshImporter.Build(imported, e.Material != null ? mat : null, overrides, mat,
                                 subdivision, matDisp, seed, warnings);
        foreach (var w in warnings)
            Warn($"Mesh '{shown}': {w}");
        if (result == null)
        {
            Warn($"Mesh entity '{label}': failed to load '{modelPath}'. Skipping.");
            return null;
        }
        if (imported != null && e.MaterialOverrides != null)
            foreach (var name in e.MaterialOverrides.Keys)
                if (!imported.SlotNames.Contains(name))
                    Warn($"Mesh '{shown}': material_overrides names '{name}', which the file does not have.");

        // An explicit bound smaller than the displacement applied: a hint for
        // the author (the displaced bounds are exact either way).
        float effectiveBound = matDisp?.Bound ?? 0f;
        if (matDisp != null && matDisp.RequestsGeometricDisplacement
            && effectiveBound > 0f && result.MaxDisplacement > effectiveBound * 1.0001f)
        {
            Warn($"Mesh '{shown}': material's displacement.bound={effectiveBound:F4} is smaller " +
                 $"than the maximum applied displacement {result.MaxDisplacement:F4}. " +
                 $"Raise displacement.bound to {result.MaxDisplacement:F4} or higher " +
                 $"to fully enclose the displaced silhouette.");
        }

        var details = new List<string>();
        if (result.MaterialCount > 1) details.Add($"{result.MaterialCount} materials");
        if (result.Iterations > 0) details.Add($"subdivision: {result.Scheme} \u00d7 {result.Iterations}");
        if (matDisp != null && matDisp.RequestsGeometricDisplacement)
            details.Add($"displacement: {DescribeDisplacement(matDisp)}, max={result.MaxDisplacement:F4}");
        Info($"Mesh:        {shown} \u2014 {result.FaceCount:N0} faces, {result.VertexCount:N0} vertices" +
             (details.Count > 0 ? $" ({string.Join(", ", details)})" : ""));

        result.Hittable.Seed = seed;
        return result.Hittable;
    }

    /// <summary>
    /// Throws when the YAML still uses the pre-migration entity-level
    /// displacement syntax. The model moved to material-level for Cycles/
    /// RenderMan parity (one displaced material drives many meshes). The
    /// error message points at the new layout so authors can migrate.
    /// </summary>
    private static void CheckLegacyEntityDisplacement(EntityData e)
    {
        if (e.LegacyEntityDisplacement == null && e.LegacyEntityDisplacementBound == 0f)
            return;

        string name = e.Name ?? e.Type ?? "(unnamed)";
        string field = e.LegacyEntityDisplacement != null
            ? "'displacement'"
            : "'displacement_bound'";
        throw new InvalidOperationException(
            $"Entity '{name}' uses the legacy entity-level {field} field, " +
            $"which has been moved to the material-level " +
            $"'displacement:' block (Cycles/RenderMan parity). " +
            $"Move the block from the entity to its material under " +
            $"'materials:' so it can be shared across instances, and add " +
            $"'displacement_enabled: false' on the entity if you want to " +
            $"suppress the material's displacement for this single instance. " +
            $"See docs/reference/scene-reference.md (section 'Material-level " +
            $"displacement').");
    }

    /// <summary>
    /// Short tag describing the displacement requested by a material, for
    /// load-time info logging.
    /// </summary>
    private static string DescribeDisplacement(MaterialDisplacement? d)
    {
        if (d == null) return "none";
        if (d is MixDisplacement mix)
            return $"mix({DescribeDisplacement(mix.A)},{DescribeDisplacement(mix.B)})";
        if (d is LeafDisplacement leaf)
        {
            string tag = leaf.Method == DisplacementMethod.BumpOnly
                ? "bump_only"
                : leaf.Options.Mode == DisplacementMode.Vector
                    ? $"vector-{leaf.Options.Space.ToString().ToLowerInvariant()}"
                    : "scalar";
            if (leaf.RequestsAutobump && leaf.Method != DisplacementMethod.BumpOnly)
                tag += "+autobump";
            return tag;
        }
        return "unknown";
    }

    /// <summary>
    /// Translates the <see cref="MaterialData"/> displacement block into a
    /// <see cref="LeafDisplacement"/> wrapping the engine-level
    /// <see cref="DisplacementOptions"/> struct. Material-level (Cycles/
    /// RenderMan parity): one displaced material drives every mesh that uses
    /// it, no per-entity duplication. Returns <c>null</c> when the block is
    /// absent, malformed, or has a zero scale.
    /// </summary>
    private static LeafDisplacement? BuildLeafDisplacement(
        MaterialData m, string sceneDir)
    {
        var disp = m.Displacement;
        if (disp == null) return null;

        string ownerTag = m.Id ?? "(unnamed material)";

        if (disp.Texture == null)
        {
            Warn($"Material '{ownerTag}': displacement requires a " +
                 $"'texture' block. Ignoring.");
            return null;
        }

        if (disp.Scale == 0f)
        {
            // Silent: scale=0 means "wire up the texture but don't displace"
            // \u2014 a valid authoring state.
            return null;
        }

        // \u2500\u2500 Parse mode (scalar | vector) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        var mode = DisplacementMode.Scalar;
        if (!string.IsNullOrWhiteSpace(disp.Mode))
        {
            string mm = disp.Mode.Trim().ToLowerInvariant();
            mode = mm switch
            {
                "scalar" or "height" or ""  => DisplacementMode.Scalar,
                "vector" or "vec"           => DisplacementMode.Vector,
                _ => DisplacementMode.Scalar,
            };
            if (mode == DisplacementMode.Scalar &&
                !(mm == "scalar" || mm == "height"))
            {
                Warn($"Material '{ownerTag}': unknown displacement " +
                     $"mode '{disp.Mode}'. Expected 'scalar' or 'vector'. " +
                     $"Falling back to scalar.");
            }
        }

        // \u2500\u2500 Parse space (tangent | object), vector mode only \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        var space = DisplacementSpace.Tangent;
        if (!string.IsNullOrWhiteSpace(disp.Space))
        {
            string s = disp.Space.Trim().ToLowerInvariant();
            space = s switch
            {
                "tangent" or "" => DisplacementSpace.Tangent,
                "object" or "local" => DisplacementSpace.Object,
                _ => DisplacementSpace.Tangent,
            };
            if (space == DisplacementSpace.Tangent &&
                !(s == "tangent" || s == ""))
            {
                Warn($"Material '{ownerTag}': unknown displacement " +
                     $"space '{disp.Space}'. Expected 'tangent' or 'object'. " +
                     $"Falling back to tangent.");
            }
            if (mode == DisplacementMode.Scalar)
            {
                Warn($"Material '{ownerTag}': 'space' is only used " +
                     $"in vector mode; ignored on a scalar displacement.");
            }
        }

        // ── Parse displacement_method (Cycles tri-state) ─────────────────
        var method = DisplacementMethod.Both;
        if (!string.IsNullOrWhiteSpace(disp.Method))
        {
            string mt = disp.Method.Trim().ToLowerInvariant();
            method = mt switch
            {
                "both" or "displacement_and_bump" or "" => DisplacementMethod.Both,
                "displacement" or "displacement_only"   => DisplacementMethod.Displacement,
                "bump" or "bump_only"                   => DisplacementMethod.BumpOnly,
                _ => DisplacementMethod.Both,
            };
            if (method == DisplacementMethod.Both &&
                !(mt == "both" || mt == "displacement_and_bump"))
            {
                Warn($"Material '{ownerTag}': unknown displacement_method " +
                     $"'{disp.Method}'. Expected 'both', 'displacement' or " +
                     $"'bump_only'. Falling back to 'both'.");
            }
        }

        ITexture inner;
        try
        {
            inner = CreateTexture(disp.Texture, sceneDir, TextureRole.Data);
        }
        catch (Exception ex)
        {
            Warn($"Material '{ownerTag}': failed to build " +
                 $"displacement texture: {ex.Message}. Ignoring.");
            return null;
        }

        // displacement_bound default: in scalar mode the maximum offset is
        // bounded by |scale\u00b7(h_max\u2212midlevel)| \u2264 |scale| for a luminance
        // texture in [0,1]; in vector mode the L2 length of an offset whose
        // components each lie in [-|scale|, +|scale|] is bounded by
        // |scale|\u00b7sqrt(3). The post-displacement warning fires whenever the
        // observed maximum exceeds the bound, so authors using out-of-range
        // textures (e.g. an HDR-EXR vector map storing values > 1) will be
        // told the exact bound they should set.
        float defaultBound = mode == DisplacementMode.Vector
            ? MathF.Abs(disp.Scale) * 1.732051f
            : MathF.Abs(disp.Scale);
        float bound = disp.Bound > 0f
            ? disp.Bound
            : defaultBound;

        // ── Autobump (step 5 of the surface-displacement stack) ────────────
        // Optional "autobump" that derives a residual BumpMapTexture from
        // the same displacement texture. Mirrors Arnold's autobump_visibility
        // flag on polymesh nodes: subdivision/displacement build the macro
        // silhouette, the autobump recovers sub-pixel detail finer than the
        // subdivision grid. Defaults are byte-identical to pre-step-5
        // (autobump: false; strength/scale ignored when off).
        bool autobump = disp.Autobump;
        float autobumpStrength = disp.AutobumpStrength;
        float autobumpScale = disp.AutobumpScale > 0f ? disp.AutobumpScale : 1f;

        if (autobump && autobumpStrength <= 0f)
        {
            Warn($"Material '{ownerTag}': 'autobump' is enabled but " +
                 $"'autobump_strength' is {autobumpStrength} (≤ 0). The autobump " +
                 $"would be a no-op; disabling it.");
            autobump = false;
        }

        // The Cycles tri-state forces autobump on/off independently of the
        // YAML 'autobump:' toggle: bump_only means the texture IS the bump
        // (autobump always on); displacement_only means no bump at all.
        if (method == DisplacementMethod.BumpOnly)
            autobump = true;
        if (method == DisplacementMethod.Displacement)
            autobump = false;

        var opts = new DisplacementOptions
        {
            Mode             = mode,
            Space            = space,
            Texture          = inner,
            Scale            = disp.Scale,
            Midlevel         = disp.Midlevel,
            Bound            = bound,
            UvScale          = disp.UvScale > 0f ? disp.UvScale : 1f,
            Autobump         = autobump,
            AutobumpStrength = autobumpStrength,
            AutobumpScale    = autobumpScale,
        };

        return new LeafDisplacement(opts, method);
    }

    /// <summary>
    /// Translates the <see cref="EntityData"/> subdivision fields into
    /// an engine-level <see cref="SubdivisionOptions"/> struct. Unknown
    /// scheme strings produce a warning and disable subdivision; the
    /// entity's translate/rotate/scale matrix is composed with the
    /// camera context so the pixel-error heuristic is measured in
    /// world-space.
    /// </summary>
    private static SubdivisionOptions BuildSubdivisionOptions(
        EntityData e, SubdivisionScreenContext screen)
    {
        if (e.SubdivisionIterations <= 0 && e.SubdivisionPixelError <= 0f
            && string.IsNullOrWhiteSpace(e.SubdivisionScheme))
        {
            return SubdivisionOptions.Disabled;
        }

        SubdivisionScheme scheme = SubdivisionScheme.Auto;
        if (!string.IsNullOrWhiteSpace(e.SubdivisionScheme))
        {
            scheme = e.SubdivisionScheme.Trim().ToLowerInvariant() switch
            {
                "none"                            => SubdivisionScheme.None,
                "loop"                            => SubdivisionScheme.Loop,
                "catmull_clark" or "catmull-clark"
                    or "cc" or "catmullclark"     => SubdivisionScheme.CatmullClark,
                "auto"                            => SubdivisionScheme.Auto,
                _ => Warned(e.SubdivisionScheme, e.Name)
            };
        }

        var entityToWorld = ComputeTransformMatrix(e);
        var ctx = new ScreenSpaceContext
        {
            CameraOrigin       = screen.CameraOrigin,
            CameraForward      = screen.CameraForward,
            ImageHeight        = screen.ImageHeight,
            VerticalFovRadians = screen.VerticalFovRadians,
            EntityToWorld      = entityToWorld,
        };

        var boundary = BoundaryInterpolation.EdgeAndCorner;
        if (!string.IsNullOrWhiteSpace(e.SubdivisionBoundary))
        {
            boundary = e.SubdivisionBoundary.Trim().ToLowerInvariant() switch
            {
                "edge_and_corner" or "edge-and-corner" => BoundaryInterpolation.EdgeAndCorner,
                "edge_only" or "edge-only"             => BoundaryInterpolation.EdgeOnly,
                _ => WarnedBoundary(e.SubdivisionBoundary, e.Name),
            };
        }

        return new SubdivisionOptions
        {
            Scheme          = scheme,
            Iterations      = Math.Max(0, e.SubdivisionIterations),
            PixelError      = Math.Max(0f, e.SubdivisionPixelError),
            MaxIterations   = Math.Max(1, e.SubdivisionMaxIterations),
            Screen          = ctx,
            Boundary        = boundary,
            CreaseAngle     = Math.Clamp(e.SubdivisionCreaseAngle, 0f, 180f),
            CreaseSharpness = e.SubdivisionCreaseSharpness is float cs && cs > 0f ? cs : float.PositiveInfinity,
        };

        static BoundaryInterpolation WarnedBoundary(string s, string? name)
        {
            Warn($"Mesh '{name ?? "(unnamed)"}': unknown subdivision_boundary " +
                 $"'{s}'. Expected edge_and_corner|edge_only. Using edge_and_corner.");
            return BoundaryInterpolation.EdgeAndCorner;
        }

        static SubdivisionScheme Warned(string s, string? name)
        {
            Warn($"Mesh '{name ?? "(unnamed)"}': unknown subdivision_scheme " +
                 $"'{s}'. Expected one of none|loop|catmull_clark|auto. " +
                 $"Falling back to auto.");
            return SubdivisionScheme.Auto;
        }
    }

    /// <summary>
    /// Camera context handed to <see cref="CreateMeshEntity"/> so the
    /// adaptive pixel-error heuristic can project mesh edges to screen
    /// space without needing the final <see cref="Camera.Camera"/> object.
    /// </summary>
    internal readonly struct SubdivisionScreenContext
    {
        public Vector3 CameraOrigin       { get; init; }
        public Vector3 CameraForward      { get; init; }
        public int      ImageHeight       { get; init; }
        public float    VerticalFovRadians { get; init; }
    }

    /// <summary>
    /// Creates a Group entity — a hierarchical container of children with
    /// inherited material and shared transform.
    /// </summary>
    private static IHittable? CreateGroupEntity(EntityData e, IMaterial fallbackMat,
        Dictionary<string, IMaterial> materials, string sceneDir, int entityIndex,
        Matrix4x4? groupToWorld)
    {
        if (e.Children == null || e.Children.Count == 0)
        {
            Warn($"Group '{e.Name ?? "(unnamed)"}' has no children. Skipping.");
            return null;
        }

        var childObjects = BuildChildList(e.Children, fallbackMat, materials, sceneDir, entityIndex, groupToWorld);
 
        if (childObjects.Count == 0)
        {
            Warn($"Group '{e.Name ?? "(unnamed)"}': all children failed to load. Skipping.");
            return null;
        }
 
        var group = new Group(childObjects);
        group.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
 
        Verbose($"Group:       {e.Name ?? "(unnamed)"} \u2014 {childObjects.Count} children");
        return group;
    }

    /// <summary>
    /// Creates an instance from a named template, sharing the template's geometry
    /// across all instances via <paramref name="templateCache"/>. The first instance
    /// of a given template triggers the build (children + template transform); every
    /// subsequent instance reuses the same <see cref="IHittable"/> reference, paying
    /// memory for the geometry, BVH and meshes only once per template.
    ///
    /// Per-instance state (seed for procedural textures and an optional material
    /// override) is carried by an <see cref="Instance"/> wrapper around the shared
    /// template — see <see cref="Instance"/> for the override semantics.
    ///
    /// Transform composition chain (object → world space):
    ///   child_local → template_transform → instance_transform
    /// </summary>
    private static IHittable? CreateInstanceEntity(EntityData e,
        Dictionary<string, IMaterial> materials,
        Dictionary<string, EntityData> templates,
        Dictionary<string, IHittable> templateCache,
        string sceneDir, int entityIndex)
    {
        if (string.IsNullOrWhiteSpace(e.Template))
        {
            Warn($"Instance '{e.Name ?? "(unnamed)"}' requires a 'template' name. Skipping.");
            return null;
        }

        if (!templates.TryGetValue(e.Template, out var templateDef))
        {
            Warn($"Instance '{e.Name ?? "(unnamed)"}': template '{e.Template}' not found. Skipping.");
            return null;
        }

        // Build the template once, on the first request, then reuse the same
        // reference for every subsequent instance. This is the memory-sharing
        // path of feature #22.
        if (!templateCache.TryGetValue(e.Template, out var sharedTemplate))
        {
            var built = BuildTemplateGeometry(templateDef, materials, sceneDir);
            if (built == null)
            {
                Warn($"Instance '{e.Name ?? "(unnamed)"}' of '{e.Template}': " +
                     "all template children failed to load. Skipping.");
                return null;
            }
            sharedTemplate = built;
            templateCache[e.Template] = sharedTemplate;
            Verbose($"Template:    '{e.Template}' built and cached");
        }

        // Material override is applied only when the YAML instance specifies a
        // material. When omitted, the template's per-child materials show through.
        var overrideMat = e.Material != null ? GetMaterial(materials, e.Material) : null;

        var instance = new Instance(sharedTemplate, overrideMat);

        // Seed precedence: instance → template → deterministic hash.
        // Stored on the Instance wrapper, not on the shared template.
        instance.Seed = e.Seed ?? templateDef.Seed ?? StableSeed(entityIndex, e.Template, e.Name);

        return instance;
    }

    /// <summary>
    /// Builds the shared geometry for a template: children resolved against the
    /// template's default material, wrapped in a Group, and (if the template
    /// defines transforms) wrapped again in a Transform for the default pose.
    ///
    /// The returned IHittable is immutable from the renderer's point of view —
    /// its Seed is intentionally left at the default value because each
    /// <see cref="Instance"/> overrides <c>rec.ObjectSeed</c> at hit time.
    /// </summary>
    private static IHittable? BuildTemplateGeometry(EntityData templateDef,
        Dictionary<string, IMaterial> materials, string sceneDir)
    {
        var fallbackMat = GetMaterial(materials, templateDef.Material);

        // A stable per-template index keeps child seeding deterministic
        // regardless of which instance triggers the build. Child seeds are
        // overridden per-instance at hit time, so this only matters for
        // procedural state computed at build time (none currently).
        int templateIndex = StableHash(templateDef.Name);

        var childObjects = BuildChildList(
            templateDef.Children!, fallbackMat, materials, sceneDir, templateIndex, groupToWorld: null);

        if (childObjects.Count == 0)
            return null;

        IHittable result = new Group(childObjects);

        var templateTransform = ComputeTransformMatrix(templateDef);
        if (templateTransform != Matrix4x4.Identity)
            result = new Transform(result, templateTransform);

        return result;
    }

    /// <summary>
    /// Shared helper: builds a list of IHittable from a list of EntityData children.
    /// Used by both CreateGroupEntity and CreateInstanceEntity.
    /// Each child resolves its own material (own ID → fallback), type (primitive,
    /// CSG, mesh, nested group), and local transform.
    /// </summary>
    private static List<IHittable> BuildChildList(List<EntityData> children,
        IMaterial fallbackMat, Dictionary<string, IMaterial> materials,
        string sceneDir, int parentIndex, Matrix4x4? groupToWorld)
    {
        var result = new List<IHittable>();

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];

            // Motion blur is only supported on top-level entities: a child's
            // transform is baked into the group/template chain at load time,
            // so per-child keyframes have no animated wrapper to live on.
            // Animate the enclosing group/instance entity instead.
            if (child.Motion is { Count: > 0 })
                Warn($"Child entity '{child.Name ?? child.Type ?? "(unnamed)"}': 'motion' is only " +
                     $"supported on top-level entities — ignored. Animate the parent group/instance.");

            var mat = child.Material != null
                ? GetMaterial(materials, child.Material)
                : fallbackMat;

            int childIdx = parentIndex * 1000 + i;

            // Compose this child's group-local transform onto the enclosing group's
            // world transform up front, so a nested group inherits the full chain
            // and a flagged child registers its seeding geometry in world space.
            var childTransform = ComputeTransformMatrix(child);
            Matrix4x4? childToWorld = groupToWorld is { } gw
                ? childTransform * gw
                : (Matrix4x4?)null;

            IHittable? hittable;
            if (string.Equals(child.Type, "csg", StringComparison.OrdinalIgnoreCase))
            {
                hittable = CreateCsgEntity(child, mat, materials, childIdx);
            }
            else if (string.Equals(child.Type, "mesh", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(child.Type, "obj", StringComparison.OrdinalIgnoreCase))
            {
                hittable = CreateMeshEntity(child, mat, materials, sceneDir, childIdx);
            }
            else if (string.Equals(child.Type, "group", StringComparison.OrdinalIgnoreCase))
            {
                hittable = CreateGroupEntity(child, mat, materials, sceneDir, childIdx, childToWorld);
            }
            else if (IsHeightFieldType(child.Type))
            {
                hittable = CreateHeightFieldEntity(child, mat, materials, sceneDir, childIdx);
            }
            else
            {
                hittable = CreateEntity(child, mat, childIdx);
            }

            if (hittable == null) continue;

            if (childTransform != Matrix4x4.Identity)
                hittable = new Transform(hittable, childTransform);

            // Symmetric with the top-level entity loop: per-child
            // visible_to_camera flag works inside groups too. The group's own
            // outer wrap (if set) still applies; this lets a child carry an
            // independent flag that composes as OR (parent OR child invisible
            // ⇒ child is invisible to primary rays).
            if (!child.VisibleToCamera)
                hittable = new CameraInvisibleHittable(hittable);

            result.Add(hittable);
        }

        return result;
    }

    /// <summary>
    /// Creates a Triangle or SmoothTriangle depending on whether per-vertex
    /// normals (n0/n1/n2) are specified in the YAML.
    ///
    /// Both "triangle" and "smooth_triangle" types route here. This means
    /// a plain "triangle" with n0/n1/n2 fields automatically upgrades to
    /// smooth shading — no type change needed.
    /// </summary>
    private static IHittable CreateTriangleEntity(EntityData e, IMaterial mat)
    {
        var v0 = ToVector3(e.V0) ?? Vector3.Zero;
        var v1 = ToVector3(e.V1) ?? Vector3.UnitX;
        var v2 = ToVector3(e.V2) ?? Vector3.UnitY;
 
        bool hasNormals = e.N0 != null && e.N1 != null && e.N2 != null;
 
        if (!hasNormals)
        {
            // Flat triangle (original behavior)
            return new Triangle(v0, v1, v2, mat);
        }
 
        var n0 = ToVector3(e.N0) ?? Vector3.UnitY;
        var n1 = ToVector3(e.N1) ?? Vector3.UnitY;
        var n2 = ToVector3(e.N2) ?? Vector3.UnitY;
 
        bool hasUVs = e.UV0 != null && e.UV1 != null && e.UV2 != null;
 
        if (hasUVs)
        {
            var uv0 = ToVector2(e.UV0) ?? System.Numerics.Vector2.Zero;
            var uv1 = ToVector2(e.UV1) ?? System.Numerics.Vector2.UnitX;
            var uv2 = ToVector2(e.UV2) ?? System.Numerics.Vector2.UnitY;
            return new SmoothTriangle(v0, v1, v2, n0, n1, n2, uv0, uv1, uv2, mat);
        }
        else
        {
            return new SmoothTriangle(v0, v1, v2, n0, n1, n2, mat);
        }
    }

    private static Matrix4x4 ComputeTransformMatrix(EntityData e)
    {
        var m = Matrix4x4.Identity;

        // Scale
        if (e.Scale != null)
        {
            if (e.Scale is List<object> list && list.Count >= 3)
            {
                var s = new Vector3(
                    Convert.ToSingle(list[0], System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToSingle(list[1], System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToSingle(list[2], System.Globalization.CultureInfo.InvariantCulture));
                m *= Matrix4x4.CreateScale(s);
            }
            else
            {
                float s = Convert.ToSingle(e.Scale, System.Globalization.CultureInfo.InvariantCulture);
                m *= Matrix4x4.CreateScale(s);
            }
        }

        // Rotate (degrees → radians, applied X then Y then Z)
        if (e.Rotate != null && e.Rotate.Count >= 3)
        {
            m *= Matrix4x4.CreateRotationX(MathUtils.DegreesToRadians(e.Rotate[0]));
            m *= Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(e.Rotate[1]));
            m *= Matrix4x4.CreateRotationZ(MathUtils.DegreesToRadians(e.Rotate[2]));
        }

        // Translate
        if (e.Translate != null && e.Translate.Count >= 3)
        {
            m *= Matrix4x4.CreateTranslation(e.Translate[0], e.Translate[1], e.Translate[2]);
        }

        return m;
    }

    // =========================================================================
    // Motion blur (entity keyframes, camera keyframes, shutter)
    // =========================================================================

    /// <summary>
    /// Parses a YAML <c>scale</c> value (scalar or <c>[x, y, z]</c> list) into
    /// a scale vector. Same accepted forms as <see cref="ComputeTransformMatrix"/>.
    /// </summary>
    private static Vector3 ParseScaleVector(object? scale)
    {
        if (scale == null) return Vector3.One;
        if (scale is List<object> list && list.Count >= 3)
        {
            return new Vector3(
                Convert.ToSingle(list[0], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(list[1], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(list[2], System.Globalization.CultureInfo.InvariantCulture));
        }
        float s = Convert.ToSingle(scale, System.Globalization.CultureInfo.InvariantCulture);
        return new Vector3(s);
    }

    /// <summary>
    /// Converts YAML Euler angles (degrees, applied X then Y then Z — the same
    /// order and row-vector convention as <see cref="ComputeTransformMatrix"/>)
    /// into the equivalent quaternion, so slerped keyframe rotations agree
    /// with the static-transform path at the keys.
    /// </summary>
    private static Quaternion RotationQuaternion(List<float>? rotate)
    {
        if (rotate == null || rotate.Count < 3) return Quaternion.Identity;
        var m = Matrix4x4.CreateRotationX(MathUtils.DegreesToRadians(rotate[0]))
              * Matrix4x4.CreateRotationY(MathUtils.DegreesToRadians(rotate[1]))
              * Matrix4x4.CreateRotationZ(MathUtils.DegreesToRadians(rotate[2]));
        return Quaternion.CreateFromRotationMatrix(m);
    }

    /// <summary>
    /// Builds the sorted keyframe list for an entity's <c>motion:</c> block.
    /// The base <c>translate</c>/<c>rotate</c>/<c>scale</c> is the implicit key
    /// at time 0; components omitted in a motion key inherit the base pose.
    /// Out-of-range times are clamped to [0, 1] and duplicated times collapse
    /// last-wins, both with a warning.
    /// </summary>
    private static MotionKey[] BuildMotionKeys(EntityData e)
    {
        string label = e.Name ?? e.Type ?? "(unnamed)";

        var baseT = ToVector3(e.Translate) ?? Vector3.Zero;
        var baseR = RotationQuaternion(e.Rotate);
        var baseS = ParseScaleVector(e.Scale);

        // time → key; the base pose seeds time 0 and an explicit `time: 0`
        // motion key overrides it (last-wins).
        var byTime = new SortedDictionary<float, MotionKey>
        {
            [0f] = new MotionKey(0f, baseT, baseR, baseS)
        };

        foreach (var k in e.Motion!)
        {
            float t = k.Time;
            if (t < 0f || t > 1f)
            {
                Warn($"Entity '{label}': motion key time {t:0.###} outside [0, 1] — clamped.");
                t = Math.Clamp(t, 0f, 1f);
            }
            if (byTime.ContainsKey(t) && t != 0f)
                Warn($"Entity '{label}': duplicate motion key at time {t:0.###} — last one wins.");

            byTime[t] = new MotionKey(
                t,
                k.Translate != null ? (ToVector3(k.Translate) ?? baseT) : baseT,
                k.Rotate != null ? RotationQuaternion(k.Rotate) : baseR,
                k.Scale != null ? ParseScaleVector(k.Scale) : baseS);
        }

        return byTime.Values.ToArray();
    }

    // =========================================================================
    // Participating medium construction
    // =========================================================================

    /// <summary>
    /// Resolves an entity's <c>interior_medium</c> / <c>exterior_medium</c>
    /// references against the medium library built from <c>mediums:</c>.
    /// Unknown ids are warned-and-dropped (returns vacuum on that side), so
    /// a typo never crashes the load — the surface just behaves as if the
    /// binding wasn't there.
    /// </summary>
    private static MediumInterface ResolveEntityMediumInterface(EntityData e,
        Dictionary<string, IMedium> mediumLibrary,
        Dictionary<string, IMedium>? embeddedSssMedium = null)
    {
        IMedium? interior = null;
        IMedium? exterior = null;

        if (!string.IsNullOrWhiteSpace(e.InteriorMedium))
        {
            if (!mediumLibrary.TryGetValue(e.InteriorMedium, out interior))
                Warn($"Entity '{e.Name ?? e.Type ?? "(unnamed)"}': unknown interior_medium '{e.InteriorMedium}'. Treating interior as vacuum.");
        }
        else if (embeddedSssMedium != null
                 && !string.IsNullOrWhiteSpace(e.Material)
                 && embeddedSssMedium.TryGetValue(e.Material, out var auto))
        {
            // Material-embedded SSS auto-injection (Arnold parity).
            interior = auto;
        }
        if (!string.IsNullOrWhiteSpace(e.ExteriorMedium))
        {
            if (!mediumLibrary.TryGetValue(e.ExteriorMedium, out exterior))
                Warn($"Entity '{e.Name ?? e.Type ?? "(unnamed)"}': unknown exterior_medium '{e.ExteriorMedium}'. Treating exterior as vacuum.");
        }

        return new MediumInterface(interior, exterior);
    }
}
