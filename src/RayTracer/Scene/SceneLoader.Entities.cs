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
        IHittable? entity = e.Type switch
        {
            "sphere"         => new Sphere(Pos(e.Center, Vector3.Zero), e.Radius, mat),
            "box"            => new Box(mat),
            "triangle"       => CreateTriangleEntity(e, mat),
            "quad"           => new Quad(
                Pos(e.Q, Vector3.Zero),
                ToVector3(e.U) ?? Vector3.UnitX,
                ToVector3(e.V) ?? Vector3.UnitY, mat),
            "cylinder"       => new Cylinder(Pos(e.Center, Vector3.Zero), e.Radius, e.Height, mat),
            "cone"           => new Cone(Pos(e.Center, Vector3.Zero), e.Radius, e.TopRadius, e.Height, mat),
            "torus"          => CreateTorusEntity(e, mat),
            "lathe"          => CreateLatheEntity(e, mat),
            "extrusion"      => CreateExtrusionEntity(e, mat),
            "capsule"        => new Capsule(Pos(e.Center, Vector3.Zero), e.Radius, e.Height, mat),
            "annulus"        => new Annulus(Pos(e.Center, Vector3.Zero), ToVector3(e.Normal) ?? Vector3.UnitY, e.Radius, e.InnerRadius, mat),
            "disk"           => new Disk(Pos(e.Center, Vector3.Zero), ToVector3(e.Normal) ?? Vector3.UnitY, e.Radius, mat),
            "infinite_plane" => new InfinitePlane(Pos(e.Point, Vector3.Zero), ToVector3(e.Normal) ?? Vector3.UnitY, mat),
            // csg, mesh, group, instance and heightfield are built by their own
            // factories (they need the material and template dictionaries).
            _                => null
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
        // Bounds in the render space; the texture of a procedural terrain is
        // still read where the terrain stands in the world, and its altitude
        // is moved by a translation (the terrain has no base height).
        float xMin = (float)(e.Bounds[0] - _shift.X), zMin = (float)(e.Bounds[1] - _shift.Z);
        float xMax = (float)(e.Bounds[2] - _shift.X), zMax = (float)(e.Bounds[3] - _shift.Z);
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
                    MinSlopeDeg = s.MinSlope,
                    MaxSlopeDeg = s.MaxSlope,
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
                e.SeaLevel, seaMat, strata,
                sampleOffsetX: (float)_shift.X, sampleOffsetZ: (float)_shift.Z);
        }
        else
        {
            Warn($"HeightField '{e.Name ?? "(unnamed)"}' needs either 'heightmap_path' or 'height_texture'. Skipping.");
            return null;
        }

        hf.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
        if (_shift.Y != 0)
            return new Transform(hf, Matrix4x4.CreateTranslation(0f, (float)-_shift.Y, 0f));
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

        var center = Pos(e.Center, Vector3.Zero);
        if (center != Vector3.Zero)
            torus = new Transform(torus, Matrix4x4.CreateTranslation(center));

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

        LatheMode mode = e.ProfileType switch
        {
            "catmull_rom" => LatheMode.CatmullRom,
            "bezier"      => LatheMode.Bezier,
            _             => LatheMode.Linear,
        };

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

        var center = Pos(e.Center, Vector3.Zero);
        if (center != Vector3.Zero)
            lathe = new Transform(lathe, Matrix4x4.CreateTranslation(center));

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

        ExtrusionMode mode = e.ProfileType switch
        {
            "catmull_rom" => ExtrusionMode.CatmullRom,
            "bezier"      => ExtrusionMode.Bezier,
            _             => ExtrusionMode.Linear,
        };

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

        ExtrusionCaps caps = e.Caps switch
        {
            "start" => ExtrusionCaps.Start,
            "end"   => ExtrusionCaps.End,
            "none"  => ExtrusionCaps.None,
            _       => ExtrusionCaps.Both,
        };

        IHittable extrusion;
        try
        {
            extrusion = new Extrusion(pts, mode, e.Height, caps, mat,
                bezierControls: controls,
                twistDegrees: e.Twist,
                taper: e.Taper,
                curveSamples: Math.Max(2, e.CurveSamples),
                creaseAngleDeg: e.CreaseAngle);
        }
        catch (ArgumentException ex)
        {
            Warn($"Extrusion entity '{e.Name ?? "(unnamed)"}': {ex.Message}. Skipping.");
            return null;
        }

        var center = Pos(e.Center, Vector3.Zero);
        if (center != Vector3.Zero)
            extrusion = new Transform(extrusion, Matrix4x4.CreateTranslation(center));

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
 
        var operation = e.Operation switch
        {
            "union"        => CsgOperation.Union,
            "intersection" => CsgOperation.Intersection,
            "subtraction"  => CsgOperation.Subtraction,
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

        // A child of type "csg" is itself a Boolean operation (recursive).
        // Placed by its transform, or with its own coordinates in the current
        // context (the render space when the CSG itself has no transform).
        return BuildPlaced(child, () => child.Type == "csg"
            ? CreateCsgEntity(child, mat, materials, childIndex)
            : CreateEntity(child, mat, childIndex));
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

            // Placed by its transform, or with its own coordinates in the
            // current context (the render space when the group has no transform).
            IHittable? hittable = BuildPlaced(child, () => child.Type switch
            {
                "csg"         => CreateCsgEntity(child, mat, materials, childIdx),
                "mesh"        => CreateMeshEntity(child, mat, materials, sceneDir, childIdx),
                "group"       => CreateGroupEntity(child, mat, materials, sceneDir, childIdx, childToWorld),
                "heightfield" => CreateHeightFieldEntity(child, mat, materials, sceneDir, childIdx),
                _             => CreateEntity(child, mat, childIdx),
            });

            if (hittable == null) continue;

            // A child's visibility composes with its group's (the masks OR).
            hittable = ApplyVisibility(hittable, child.Visibility);

            result.Add(hittable);
        }

        return result;
    }

    /// <summary>
    /// Creates a Triangle or SmoothTriangle depending on whether per-vertex
    /// normals (n0/n1/n2) are specified in the YAML.
    ///
    /// The "triangle" type routes here. This means
    /// a plain "triangle" with n0/n1/n2 fields automatically upgrades to
    /// smooth shading — no type change needed.
    /// </summary>
    private static IHittable CreateTriangleEntity(EntityData e, IMaterial mat)
    {
        var v0 = Pos(e.V0, Vector3.Zero);
        var v1 = Pos(e.V1, Vector3.UnitX);
        var v2 = Pos(e.V2, Vector3.UnitY);
        IHittable tri = BuildTriangle(e, v0, v1, v2, mat);
        // A triangle's local point is its point: moved into the render space,
        // it goes in a one-triangle mesh that adds the move back (textures).
        return _shift.IsZero ? tri : new Mesh([tri], mat, 3) { LocalPointOffset = _shift.ToVector3() };
    }

    private static IHittable BuildTriangle(EntityData e, Vector3 v0, Vector3 v1, Vector3 v2, IMaterial mat)
    {
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
