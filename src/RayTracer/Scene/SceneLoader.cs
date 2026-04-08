using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Lights;
using RayTracer.Acceleration;
using RayTracer.Textures;
using RayTracer.Rendering;

namespace RayTracer.Scene;

public class SceneLoader
{
    /// <summary>
    /// Minimum number of objects before BVH construction is beneficial.
    /// Below this threshold, linear search in HittableList is faster due to
    /// lower overhead. Above it, O(log N) BVH traversal wins.
    /// </summary>
    private const int BvhThreshold = 4;

    // =========================================================================
    // Deferred warning / info messages
    // =========================================================================
    //
    // During Load(), warnings and informational messages are collected here
    // instead of being written directly to Console. This prevents them from
    // appearing between Program.cs's "Loading scene... " and "done (X ms)"
    // on the same line.
    //
    // After Load() returns and Program.cs has finished the "done" line, it
    // calls FlushMessages() to print everything in a clean, indented block.
    // =========================================================================

    private static readonly List<string> _deferredMessages = new();

    /// <summary>
    /// Queues a warning message to be printed after the loading phase completes.
    /// </summary>
    private static void Warn(string message)
    {
        _deferredMessages.Add($"  [Warning] {message}");
    }

    /// <summary>
    /// Queues an informational (non-warning) message to be printed after loading.
    /// </summary>
    private static void Info(string message)
    {
        _deferredMessages.Add($"  {message}");
    }

    /// <summary>
    /// Writes all deferred messages to the console and clears the buffer.
    /// Call this from Program.cs after printing the "done (X ms)" line.
    /// </summary>
    public static void FlushMessages()
    {
        if (_deferredMessages.Count == 0) return;

        foreach (var msg in _deferredMessages)
            Console.WriteLine(msg);

        _deferredMessages.Clear();
    }

    /// <summary>
    /// Loads a scene from a YAML file.
    /// </summary>
    /// <param name="yamlPath">Path to the YAML scene file.</param>
    /// <param name="imageWidth">Target image width (for aspect ratio).</param>
    /// <param name="imageHeight">Target image height (for aspect ratio).</param>
    /// <param name="shadowSamplesOverride">
    /// When non-null, overrides the <c>shadow_samples</c> value of every area light
    /// in the scene. This allows quick quality iteration from the CLI without editing
    /// the scene file (e.g. <c>-S 4</c> for preview, <c>-S 32</c> for production).
    /// When null, each area light uses its own YAML-defined value.
    /// </param>
    /// <param name="cameraSelector">
    /// When non-null, selects which camera to use from a <c>cameras:</c> list by
    /// name (case-insensitive) or zero-based index. Ignored when the scene uses
    /// the legacy single <c>camera:</c> syntax.
    /// </param>
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, Vector3 AmbientLight, SkySettings Sky)
        Load(string yamlPath, int imageWidth, int imageHeight,
             int? shadowSamplesOverride = null, string? cameraSelector = null)
    {
        // Clear any leftover messages from a previous Load() call
        _deferredMessages.Clear();

        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var data = deserializer.Deserialize<SceneData>(yaml);
        if (data == null) throw new InvalidOperationException("Failed to parse YAML scene.");

        // Scene directory for resolving relative paths (image textures, etc.)
        var sceneDir = Path.GetDirectoryName(Path.GetFullPath(yamlPath)) ?? ".";

        // ── YAML Imports ─────────────────────────────────────────────────────
        // Process imports before local definitions. Imported materials, entities,
        // lights, and templates are merged into the current SceneData. Local
        // definitions with the same ID/name override imported ones.
        if (data.Imports is { Count: > 0 })
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(yamlPath)
            };
            ProcessImports(data, sceneDir, deserializer, visited);
        }

        // Materials dictionary — two-pass loading to resolve MixMaterial references.
        // Pass 1: create all non-mix materials so their IDs are available.
        // Pass 2: create mix materials that reference other materials by ID.
        var materials = new Dictionary<string, IMaterial>();
        var deferredMix = new List<MaterialData>();
 
        if (data.Materials != null)
        {
            // ── Pass 1: non-mix materials ────────────────────────────────────
            foreach (var m in data.Materials)
            {
                if (m.Id == null) continue;
                if (string.Equals(m.Type, "mix", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(m.Type, "blend", StringComparison.OrdinalIgnoreCase))
                {
                    deferredMix.Add(m);
                    continue;
                }
                materials[m.Id] = CreateMaterial(m, sceneDir);
            }
 
            // ── Pass 2: mix materials ────────────────────────────────────────
            // Supports mix-of-mix by iterating until no more can be resolved.
            // Prevents infinite loops via progress check (remaining count must shrink).
            var remaining = deferredMix;
            int maxPasses = remaining.Count + 1; // safety bound
            while (remaining.Count > 0 && maxPasses-- > 0)
            {
                var stillRemaining = new List<MaterialData>();
                foreach (var m in remaining)
                {
                    bool canResolveA = m.MaterialA == null || materials.ContainsKey(m.MaterialA);
                    bool canResolveB = m.MaterialB == null || materials.ContainsKey(m.MaterialB);
 
                    if (canResolveA && canResolveB)
                    {
                        materials[m.Id!] = CreateMixMaterial(m, materials, sceneDir);
                    }
                    else
                    {
                        stillRemaining.Add(m);
                    }
                }
 
                // No progress — remaining mix materials have circular or missing refs
                if (stillRemaining.Count == remaining.Count)
                {
                    foreach (var m in stillRemaining)
                        Warn($"Mix material '{m.Id}': cannot resolve references " +
                             $"(material_a='{m.MaterialA}', material_b='{m.MaterialB}'). " +
                             $"Using default grey Lambertian.");
                    break;
                }
 
                remaining = stillRemaining;
            }
        }

        var ambientLight = ToVector3(data.World?.AmbientLight) ?? new Vector3(0.1f);
        var background   = ToVector3(data.World?.Background)   ?? new Vector3(0.5f, 0.7f, 1.0f);
        var sky          = BuildSkySettings(data.World?.Sky, background, sceneDir);

        var objects = new List<IHittable>();

        // Ground plane
        if (data.World?.Ground != null)
        {
            var groundMat = GetMaterial(materials, data.World.Ground.Material);
            float groundY = data.World.Ground.Y;
            objects.Add(new InfinitePlane(
                new Vector3(0, groundY, 0), Vector3.UnitY, groundMat));
        }

        // Templates
        var templates = new Dictionary<string, EntityData>(StringComparer.OrdinalIgnoreCase);
        if (data.Templates != null)
        {
            foreach (var t in data.Templates)
            {
                if (string.IsNullOrWhiteSpace(t.Name))
                {
                    Warn("Template without a 'name' field. Skipping.");
                    continue;
                }
                if (t.Children == null || t.Children.Count == 0)
                {
                    Warn($"Template '{t.Name}' has no 'children'. Skipping.");
                    continue;
                }
                // Last-write-wins: local templates override imported ones
                templates[t.Name] = t;
            }
            if (templates.Count > 0)
                Info($"Templates registered: {templates.Count} " +
                     $"({string.Join(", ", templates.Keys)})");
        }

        // Entities
        if (data.Entities != null)
        {
            for (int idx = 0; idx < data.Entities.Count; idx++)
            {
                var e   = data.Entities[idx];
                var mat = GetMaterial(materials, e.Material);
 
                IHittable? hittable;
                if (string.Equals(e.Type, "csg", StringComparison.OrdinalIgnoreCase))
                {
                    hittable = CreateCsgEntity(e, mat, materials, idx);
                }
                else if (string.Equals(e.Type, "mesh", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(e.Type, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    hittable = CreateMeshEntity(e, mat, sceneDir, idx);
                }
                else if (string.Equals(e.Type, "group", StringComparison.OrdinalIgnoreCase))
                {
                    hittable = CreateGroupEntity(e, mat, materials, sceneDir, idx);
                }
                else if (string.Equals(e.Type, "instance", StringComparison.OrdinalIgnoreCase))
                {
                    hittable = CreateInstanceEntity(e, materials, templates, sceneDir, idx);
                }
                else
                {
                    hittable = CreateEntity(e, mat, idx);
                }
 
                if (hittable != null)
                {
                    var transform = ComputeTransformMatrix(e);
                    if (transform != Matrix4x4.Identity)
                        hittable = new Transform(hittable, transform);
                    objects.Add(hittable);
                }
            }
        }

        // Build BVH — separate infinite planes (no finite AABB) from finite objects.
        // BVH is only beneficial above BvhThreshold objects; below that the tree
        // construction overhead exceeds the traversal savings.
        IHittable world;
        if (objects.Count > BvhThreshold)
        {
            var finiteObjects   = new List<IHittable>();
            var infiniteObjects = new List<IHittable>();
            foreach (var obj in objects)
            {
                if (IsInfinitePlane(obj))
                    infiniteObjects.Add(obj);
                else
                    finiteObjects.Add(obj);
            }

            var bvhObjects = new List<IHittable>();
            if (finiteObjects.Count > 0)
                bvhObjects.Add(new BvhNode(finiteObjects));
            bvhObjects.AddRange(infiniteObjects);
            world = new HittableList(bvhObjects);
        }
        else
        {
            world = new HittableList(objects);
        }

        // Camera
        float aspect  = (float)imageWidth / imageHeight;
        var camData   = ResolveCamera(data, cameraSelector);
        var camPos    = ToVector3(camData.Position) ?? new Vector3(0, 1, -5);
        var camLookAt = ToVector3(camData.LookAt)   ?? Vector3.Zero;
        var camVup    = ToVector3(camData.Vup)       ?? Vector3.UnitY;
        var camera    = new Camera.Camera(
            camPos, camLookAt, camVup,
            camData.Fov, aspect,
            camData.Aperture, camData.FocalDist);

        // Lights
        var lights = new List<ILight>();
        if (data.Lights != null)
        {
            foreach (var l in data.Lights)
            {
                var light = CreateLight(l, shadowSamplesOverride);
                if (light != null) lights.Add(light);
            }
        }

        // Add Emissive Objects as Geometry Lights
        ExtractGeometryLights(objects, lights, shadowSamplesOverride ?? 1);

        // Add Environment Light if applicable
        if (sky.CanSampleDirectly)
        {
            lights.Add(new EnvironmentLight(sky, shadowSamplesOverride ?? 1));
        }

        // Default lighting only when lights section is completely absent from YAML.
        // An explicit empty list (lights: []) means "no lights" intentionally
        // (e.g. HDRI-only or emissive-only scenes).
        if (lights.Count == 0 && data.Lights == null)
        {
            lights.Add(new DirectionalLight(new Vector3(-1, -1, -1), Vector3.One, 0.8f));
            lights.Add(new PointLight(new Vector3(0, 10, -5), Vector3.One, 100f));
        }

        return (world, camera, lights, ambientLight, sky);
    }

    // =========================================================================
    // YAML Imports
    // =========================================================================
 
    /// <summary>
    /// Processes the <c>imports:</c> section of a scene file. Recursively loads
    /// external YAML files and merges their materials, entities, lights, and
    /// templates into the current <see cref="SceneData"/>.
    ///
    /// Merge semantics:
    ///   - Materials, entities, lights, templates: imported are prepended.
    ///     Local definitions with the same ID/name override imported ones.
    ///   - World / Camera: NOT imported (main scene owns these).
    ///
    /// Circular import protection via the visited set.
    /// </summary>
    private static void ProcessImports(SceneData data, string baseDir,
        IDeserializer deserializer, HashSet<string> visited)
    {
        if (data.Imports == null) return;
 
        var importedMaterials = new List<MaterialData>();
        var importedEntities  = new List<EntityData>();
        var importedLights    = new List<LightData>();
        var importedTemplates = new List<EntityData>();
 
        foreach (var import in data.Imports)
        {
            if (string.IsNullOrWhiteSpace(import.Path))
            {
                Warn("Import entry has empty 'path'. Skipping.");
                continue;
            }
 
            string importPath = Path.IsPathRooted(import.Path)
                ? import.Path
                : Path.Combine(baseDir, import.Path);
 
            string fullPath = Path.GetFullPath(importPath);
 
            if (!visited.Add(fullPath))
            {
                Warn($"Circular import detected: '{import.Path}'. Skipping.");
                continue;
            }
 
            if (!File.Exists(fullPath))
            {
                Warn($"Import file not found: '{import.Path}' " +
                     $"(resolved to '{fullPath}'). Skipping.");
                continue;
            }
 
            try
            {
                var importYaml = File.ReadAllText(fullPath);
                var importData = deserializer.Deserialize<SceneData>(importYaml);
 
                if (importData == null)
                {
                    Warn($"Failed to parse import file: '{import.Path}'. Skipping.");
                    continue;
                }
 
                var importDir = Path.GetDirectoryName(fullPath) ?? ".";
                if (importData.Imports is { Count: > 0 })
                    ProcessImports(importData, importDir, deserializer, visited);
 
                if (importData.Materials != null)
                    importedMaterials.AddRange(importData.Materials);
                if (importData.Entities != null)
                    importedEntities.AddRange(importData.Entities);
                if (importData.Lights != null)
                    importedLights.AddRange(importData.Lights);
                if (importData.Templates != null)
                    importedTemplates.AddRange(importData.Templates);
 
                Info($"Imported: '{import.Path}' " +
                     $"({importData.Materials?.Count ?? 0} materials, " +
                     $"{importData.Entities?.Count ?? 0} entities, " +
                     $"{importData.Lights?.Count ?? 0} lights, " +
                     $"{importData.Templates?.Count ?? 0} templates)");
            }
            catch (Exception ex)
            {
                Warn($"Error loading import '{import.Path}': {ex.Message}. Skipping.");
            }
        }
 
        // Merge: imported go BEFORE local (local wins on ID/name conflicts)
        if (importedMaterials.Count > 0)
        {
            importedMaterials.AddRange(data.Materials ?? new List<MaterialData>());
            data.Materials = importedMaterials;
        }
        if (importedEntities.Count > 0)
        {
            importedEntities.AddRange(data.Entities ?? new List<EntityData>());
            data.Entities = importedEntities;
        }
        if (importedLights.Count > 0)
        {
            importedLights.AddRange(data.Lights ?? new List<LightData>());
            data.Lights = importedLights;
        }
        if (importedTemplates.Count > 0)
        {
            importedTemplates.AddRange(data.Templates ?? new List<EntityData>());
            data.Templates = importedTemplates;
        }
    }

    // =========================================================================
    // Camera resolution
    // =========================================================================

    /// <summary>
    /// Reads the YAML file and lists all cameras defined in <c>cameras:</c>.
    /// Called by Program.cs when <c>--list-cameras</c> is passed.
    /// </summary>
    /// <remarks>
    /// This method is invoked standalone (outside the Load flow), so it writes
    /// directly to Console — there is no "Loading scene... done" wrapper to conflict with.
    /// </remarks>
    public static void TryListCameras(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var data = deserializer.Deserialize<SceneData>(yaml);

        if (data?.Cameras == null || data.Cameras.Count == 0)
        {
            Console.WriteLine(data?.Camera != null
                ? "This scene uses a single 'camera:' entry (no named cameras list)."
                : "No camera defined in the scene.");
            return;
        }

        Console.WriteLine($"Cameras in scene ({data.Cameras.Count}):");
        for (int i = 0; i < data.Cameras.Count; i++)
        {
            var c     = data.Cameras[i];
            string label = c.Name != null ? $"  #{i}  \"{c.Name}\"" : $"  #{i}  (unnamed)";
            Console.WriteLine($"{label}   fov={c.Fov}°  pos={FormatVec(c.Position)}");
        }
    }

    /// <summary>
    /// Resolves which <see cref="CameraData"/> to use from the scene data.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>If <c>cameras:</c> list is present, it takes priority over the
    ///         legacy <c>camera:</c> singleton.</item>
    ///   <item>Within the list, <paramref name="selector"/> is matched first by
    ///         name (case-insensitive), then by zero-based index.</item>
    ///   <item>If no selector is provided and the list has more than one entry,
    ///         a warning is printed and the first camera is used.</item>
    ///   <item>Falls back to the legacy <c>camera:</c> singleton, or a built-in
    ///         default if neither is present.</item>
    /// </list>
    /// </summary>
    private static CameraData ResolveCamera(SceneData data, string? selector)
    {
        // No cameras list → fall back to legacy singleton
        if (data.Cameras == null || data.Cameras.Count == 0)
        {
            if (selector != null)
                Warn($"--camera '{selector}' was specified but the scene uses " +
                     "the single 'camera:' syntax. Ignoring selector.");
            return data.Camera ?? new CameraData();
        }

        var list = data.Cameras;

        // Selector provided → resolve by name first, then by index
        if (selector != null)
        {
            // Try name match (case-insensitive)
            var byName = list.FirstOrDefault(
                c => string.Equals(c.Name, selector, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;

            // Try numeric index
            if (int.TryParse(selector, out int idx))
            {
                if (idx >= 0 && idx < list.Count)
                    return list[idx];

                Warn($"--camera index {idx} is out of range " +
                     $"(scene has {list.Count} cameras, indices 0–{list.Count - 1}). " +
                     "Using camera 0.");
                return list[0];
            }

            // No match by name or index
            var names = string.Join(", ",
                list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
            Warn($"--camera '{selector}' not found. " +
                 $"Available: {names}. Using camera 0.");
            return list[0];
        }

        // No selector → use the only camera silently, or warn and use first
        if (list.Count == 1)
            return list[0];

        var cameraNames = string.Join(", ",
            list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
        Warn($"Scene contains {list.Count} cameras. Using camera 0. " +
             $"Use --camera <name|index> to select one. Available: {cameraNames}");
        return list[0];
    }

    private static string FormatVec(List<float>? v) =>
        v is { Count: >= 3 } ? $"[{v[0]:F2}, {v[1]:F2}, {v[2]:F2}]" : "(default)";

    // =========================================================================
    // Factory helpers
    // =========================================================================

    /// <summary>
    /// Scans all scene objects and registers ISamplable emissives as GeometryLights
    /// for Next Event Estimation (NEE / direct illumination).
    ///
    /// Handles three cases:
    ///   1. Bare primitive with Emissive material.
    ///   2. Transform-wrapped primitive.
    ///   3. Group / Transform-wrapped Group: recurses into children, composing
    ///      transforms correctly for world-space sampling.
    /// </summary>
    private static void ExtractGeometryLights(List<IHittable> objects, List<ILight> lights, int shadowSamples)
    {
        foreach (var obj in objects)
            ExtractGeometryLightsRecursive(obj, lights, shadowSamples);
    }
 
    private static void ExtractGeometryLightsRecursive(IHittable obj, List<ILight> lights, int shadowSamples)
    {
        switch (obj)
        {
            // Group: recurse into children
            case Group g:
                foreach (var child in g.Children)
                    ExtractGeometryLightsRecursive(child, lights, shadowSamples);
                break;
 
            // Transform wrapping a Group: compose outer transform onto each child
            case Transform t when t.Inner is Group innerGroup:
                foreach (var child in innerGroup.Children)
                {
                    var composedChild = new Transform(child, t.TransformMatrix);
                    var (s, e) = ResolveEmissiveSamplable(composedChild);
                    if (s != null && e != null)
                        lights.Add(new GeometryLight(s, e, shadowSamples));
                }
                break;
 
            // All other objects (primitives, CSG, Transform, etc.)
            default:
            {
                var (samplable, emissive) = ResolveEmissiveSamplable(obj);
                if (samplable != null && emissive != null)
                    lights.Add(new GeometryLight(samplable, emissive, shadowSamples));
                break;
            }
        }
    }

    /// <summary>
    /// Returns the ISamplable and Emissive for a hittable, if it qualifies as a
    /// geometry light. Handles bare primitives and Transform wrappers (including
    /// nested Transform chains).
    ///
    /// For a Transform, <c>obj</c> itself is used as the ISamplable — Transform
    /// now implements ISamplable and maps sample points/normals/area to world space.
    /// </summary>
    private static (ISamplable? Samplable, Emissive? Material) ResolveEmissiveSamplable(IHittable obj)
    {
        switch (obj)
        {
            // ── Bare primitives ──────────────────────────────────────────────────
            case Sphere         s  when s.Material  is Emissive em: return (s,  em);
            case Quad           q  when q.Material  is Emissive em: return (q,  em);
            case Triangle       tr when tr.Material is Emissive em: return (tr, em);
            case SmoothTriangle st when st.Material is Emissive em: return (st, em);
            case Disk           d  when d.Material  is Emissive em: return (d,  em);
            case Box            b  when b.Material  is Emissive em: return (b,  em);
            case Cylinder       cy when cy.Material is Emissive em: return (cy, em);
            case Cone           co when co.Material is Emissive em: return (co, em);
            case Torus          to when to.Material is Emissive em: return (to, em);
            case Capsule        ca when ca.Material is Emissive em: return (ca, em);
            case Annulus        an when an.Material is Emissive em: return (an, em);
            case Mesh           ms when ms.Material is Emissive em: return (ms, em);

            // ── Transform wrapper ────────────────────────────────────────────────
            // Transform implements ISamplable: it delegates Sample() to the
            // inner primitive and maps the result to world space with the
            // correct Jacobian-based area conversion.
            // We recurse into Inner to find the Emissive material; the
            // ISamplable we register is the Transform itself (world-space sampling).
            case Transform t:
            {
                var (innerSamplable, emissive) = ResolveEmissiveSamplable(t.Inner);
                if (innerSamplable == null || emissive == null)
                    return (null, null);

                // t itself is ISamplable (Transform : IHittable, ISamplable)
                return (t, emissive);
            }

            default:
                return (null, null);
        }
    }

    private static IMaterial CreateMaterial(MaterialData m, string sceneDir)
    {
        ITexture albedo = m.Texture != null
            ? CreateTexture(m.Texture, sceneDir)
            : new SolidColor(ToVector3(m.Color) ?? new Vector3(0.5f));

        IMaterial material = m.Type?.ToLowerInvariant() switch
        {
            "lambertian" => new Lambertian(albedo),
            "metal"      => new Metal(albedo, m.Fuzz),
            "dielectric" => new Dielectric(m.RefractionIndex, albedo),
            "emissive"   => new Emissive(albedo, m.Intensity),
            "disney" or "disney_bsdf" or "pbr"
                         => new DisneyBsdf(
                                albedo,
                                metallic:       m.Metallic,
                                roughness:      m.Roughness,
                                subsurface:     m.Subsurface,
                                specular:       m.Specular,
                                specularTint:   m.SpecularTint,
                                sheen:          m.Sheen,
                                sheenTint:      m.SheenTint,
                                clearcoat:      m.Clearcoat,
                                clearcoatGloss: m.ClearcoatGloss,
                                specTrans:      m.SpecTrans,
                                ior:            m.DisneyIor),
            _            => new Lambertian(albedo)
        };

        // ── Normal map (optional) ────────────────────────────────────────────
        if (m.NormalMap != null)
        {
            var normalMap = LoadNormalMap(m.NormalMap, sceneDir);
            if (normalMap != null)
            {
                // Set via the concrete type's property (all 6 materials have it)
                switch (material)
                {
                    case Lambertian  lam: lam.NormalMap = normalMap; break;
                    case Metal       met: met.NormalMap = normalMap; break;
                    case Dielectric  die: die.NormalMap = normalMap; break;
                    case Emissive    emi: emi.NormalMap = normalMap; break;
                    case DisneyBsdf  dis: dis.NormalMap = normalMap; break;
                    case MixMaterial mix: mix.NormalMap = normalMap; break;
                }
            }
        }

        return material;
    }

    /// <summary>
    /// Creates a MixMaterial that blends between two existing materials.
    /// The child materials must already exist in the dictionary (enforced by
    /// the two-pass loading in Load()).
    /// </summary>
    private static IMaterial CreateMixMaterial(MaterialData m,
        Dictionary<string, IMaterial> materials, string sceneDir)
    {
        // Resolve child materials
        var matA = GetMaterial(materials, m.MaterialA);
        var matB = GetMaterial(materials, m.MaterialB);
 
        // Build mask texture (optional)
        ITexture? mask = m.Mask != null ? CreateTexture(m.Mask, sceneDir) : null;
 
        float blend = Math.Clamp(m.Blend, 0f, 1f);
        IMaterial material = new MixMaterial(matA, matB, blend, mask);
 
        // ── Normal map (optional) ────────────────────────────────────────
        if (m.NormalMap != null)
        {
            var normalMap = LoadNormalMap(m.NormalMap, sceneDir);
            if (normalMap != null)
            {
                ((MixMaterial)material).NormalMap = normalMap;
            }
        }
 
        return material;
    }

    /// <summary>
    /// Loads a normal map texture from the YAML-specified path.
    /// </summary>
    private static NormalMapTexture? LoadNormalMap(NormalMapData nm, string sceneDir)
    {
        if (string.IsNullOrWhiteSpace(nm.Path))
        {
            Warn("Normal map requires a 'path' field. Skipping.");
            return null;
        }

        string mapPath = Path.IsPathRooted(nm.Path)
            ? nm.Path
            : Path.Combine(sceneDir, nm.Path);

        if (!File.Exists(mapPath))
        {
            Warn($"Normal map file not found: {mapPath}. Skipping.");
            return null;
        }

        try
        {
            float scaleU = nm.UvScale is { Count: > 0 } ? nm.UvScale[0] : 1f;
            float scaleV = nm.UvScale is { Count: > 1 } ? nm.UvScale[1] : scaleU;
            return new NormalMapTexture(mapPath, nm.Strength, scaleU, scaleV, nm.FlipY);
        }
        catch (Exception ex)
        {
            Warn($"Failed to load normal map '{mapPath}': {ex.Message}. Skipping.");
            return null;
        }
    }

    /// <summary>
    /// Builds a <see cref="SkySettings"/> from the YAML sky section.
    /// Supports three modes: flat (background color), gradient, and HDRI.
    /// </summary>
    private static SkySettings BuildSkySettings(SkyData? skyData, Vector3 background, string sceneDir)
    {
        if (skyData == null)
            return new SkySettings(background);

        string skyType = skyData.Type?.ToLowerInvariant() ?? "";

        return skyType switch
        {
            "hdri"     => BuildHdriSky(skyData, sceneDir),
            "gradient" => BuildGradientSky(skyData),
            _          => new SkySettings(background)
        };
    }

    private static SkySettings BuildGradientSky(SkyData skyData)
    {
        var zenith  = ToVector3(skyData.ZenithColor)  ?? new Vector3(0.10f, 0.30f, 0.80f);
        var horizon = ToVector3(skyData.HorizonColor) ?? new Vector3(0.70f, 0.85f, 1.00f);
        var ground  = ToVector3(skyData.GroundColor)  ?? new Vector3(0.30f, 0.25f, 0.20f);

        Vector3? sunDir       = null;
        Vector3? sunColor     = null;
        float    sunIntensity = 10f;
        float    sunSize      = 3f;
        float    sunFalloff   = 32f;

        if (skyData.Sun != null)
        {
            sunDir       = ToVector3(skyData.Sun.Direction);
            sunColor     = ToVector3(skyData.Sun.Color);
            sunIntensity = skyData.Sun.Intensity;
            sunSize      = skyData.Sun.Size;
            sunFalloff   = skyData.Sun.Falloff;
        }

        return new SkySettings(zenith, horizon, ground,
                               sunDir, sunColor, sunIntensity, sunSize, sunFalloff);
    }

    private static SkySettings BuildHdriSky(SkyData skyData, string sceneDir)
    {
        if (string.IsNullOrWhiteSpace(skyData.Path))
        {
            Warn("HDRI sky requires a 'path' field. Falling back to flat gray.");
            return new SkySettings(new Vector3(0.5f));
        }

        string hdrPath = Path.IsPathRooted(skyData.Path)
            ? skyData.Path
            : Path.Combine(sceneDir, skyData.Path);

        if (!File.Exists(hdrPath))
        {
            Warn($"HDRI file not found: {hdrPath}. Falling back to flat magenta.");
            return new SkySettings(new Vector3(1f, 0f, 1f));
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (pixels, width, height) = HdrLoader.Load(hdrPath);
            var envMap = new EnvironmentMap(pixels, width, height,
                                            skyData.Intensity, skyData.Rotation);
            Info($"HDRI loaded: {skyData.Path} ({width}x{height}, {sw.ElapsedMilliseconds} ms)");
            return new SkySettings(envMap);
        }
        catch (Exception ex)
        {
            Warn($"Failed to load HDRI '{hdrPath}': {ex.Message}. Falling back to flat magenta.");
            return new SkySettings(new Vector3(1f, 0f, 1f));
        }
    }

    private static ITexture CreateTexture(TextureData t, string sceneDir)
    {
        ITexture tex = t.Type?.ToLowerInvariant() switch
        {
            "checker" => new CheckerTexture(t.Scale,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.One  : Vector3.One,
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.Zero : Vector3.Zero),

            "noise" => new NoiseTexture(t.Scale),

            "marble" => new MarbleTexture(t.Scale,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.9f) : new Vector3(0.9f),
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.1f) : new Vector3(0.1f)),

            "wood" => new WoodTexture(t.Scale, t.NoiseStrength ?? 2.0f,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.85f, 0.65f, 0.40f) : new Vector3(0.85f, 0.65f, 0.40f),
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.60f, 0.40f, 0.20f) : new Vector3(0.60f, 0.40f, 0.20f)),

            "image" => CreateImageTexture(t, sceneDir),

            _ => new SolidColor(t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.One : Vector3.One)
        };

        ApplyTextureParams(tex, t);
        return tex;
    }

    /// <summary>
    /// Creates an ImageTexture from a file path specified in the YAML.
    /// The path is resolved relative to the scene file's directory.
    /// </summary>
    private static ITexture CreateImageTexture(TextureData t, string sceneDir)
    {
        if (string.IsNullOrWhiteSpace(t.Path))
        {
            Warn("Image texture requires a 'path' field. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f)); // Magenta = missing texture
        }

        // Resolve relative to scene directory
        string imagePath = Path.IsPathRooted(t.Path)
            ? t.Path
            : Path.Combine(sceneDir, t.Path);

        if (!File.Exists(imagePath))
        {
            Warn($"Image texture file not found: {imagePath}. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f));
        }

        try
        {
            float scaleU = t.UvScale is { Count: > 0 } ? t.UvScale[0] : 1f;
            float scaleV = t.UvScale is { Count: > 1 } ? t.UvScale[1] : scaleU;
            return new ImageTexture(imagePath, scaleU, scaleV);
        }
        catch (Exception ex)
        {
            Warn($"Failed to load image texture '{imagePath}': {ex.Message}. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f));
        }
    }

    /// <summary>
    /// Applies YAML-specified transform and randomization parameters to a
    /// procedural texture after construction.
    /// Note: <c>noise_strength</c> is supported by <c>noise</c>, <c>marble</c>,
    /// and <c>wood</c> texture types. For <c>marble</c> and <c>wood</c> it
    /// overrides the amplitude already baked into the constructor; for <c>noise</c>
    /// it controls turbulence weight (0 = smooth Perlin, >0 = turbulent output).
    /// </summary>
    private static void ApplyTextureParams(ITexture tex, TextureData t)
    {
        if (tex is NoiseTexture nt)
        {
            if (t.Offset   != null) nt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) nt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            nt.RandomizeOffset   = t.RandomizeOffset;
            nt.RandomizeRotation = t.RandomizeRotation;
            // FIX (ISSUE #2): noise_strength was previously ignored for type "noise".
            // It now sets the turbulence weight: 0 = smooth Perlin, >0 = turbulent.
            if (t.NoiseStrength.HasValue) nt.NoiseStrength = t.NoiseStrength.Value;
        }
        else if (tex is MarbleTexture mt)
        {
            if (t.Offset   != null) mt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) mt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            mt.RandomizeOffset   = t.RandomizeOffset;
            mt.RandomizeRotation = t.RandomizeRotation;
            if (t.NoiseStrength.HasValue) mt.NoiseStrength = t.NoiseStrength.Value;
        }
        else if (tex is WoodTexture wt)
        {
            if (t.Offset   != null) wt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) wt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            wt.RandomizeOffset   = t.RandomizeOffset;
            wt.RandomizeRotation = t.RandomizeRotation;
            if (t.NoiseStrength.HasValue) wt.NoiseStrength = t.NoiseStrength.Value;
        }
    }

    private static IHittable? CreateEntity(EntityData e, IMaterial mat, int entityIndex = 0)
    {
        IHittable? entity = e.Type?.ToLowerInvariant() switch
        {
            "sphere"   => new Sphere(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, mat),

            // FIX (BUG #1): min/max corner syntax was parsed from YAML but silently
            // ignored — the box always appeared as a unit cube at the origin.
            // When both min and max are specified they now define the box volume
            // directly, equivalent to scale + translate on the unit cube.
            // Note: if min/max AND scale/translate are both specified, the outer
            // transform in Load() is applied on top (useful for rotation).
            "box" => e.Min != null && e.Max != null
                     ? CreateBoxFromMinMax(e, mat)
                     : new Box(mat),
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
            entity.Seed = e.Seed ?? HashCode.Combine(entityIndex,
                                                      e.Type?.GetHashCode() ?? 0,
                                                      e.Name?.GetHashCode() ?? 0);
        }

        return entity;
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
    /// Creates a Box positioned and sized according to explicit min/max corner
    /// coordinates. Internally wraps a unit-cube Box in a Scale+Translate transform.
    /// This allows min/max to coexist with an additional rotate in YAML — the outer
    /// ComputeTransformMatrix() applies any extra transform on top.
    /// </summary>
    /// <remarks>
    /// BUG-08 fix: seed is NOT assigned here. CreateEntity() assigns it uniformly
    /// for all entity types after construction. Assigning it here AND there caused
    /// the Box to get a non-deterministic seed from Random.Shared.Next() instead
    /// of the deterministic index-based one computed in CreateEntity().
    /// </remarks>
    private static IHittable CreateBoxFromMinMax(EntityData e, IMaterial mat)
    {
        var min    = ToVector3(e.Min)!.Value;
        var max    = ToVector3(e.Max)!.Value;
        var center = (min + max) * 0.5f;
        var size   = max - min;
        // Scale the unit cube to the desired dimensions, then translate to center
        var matrix = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center);
        return new Transform(new Box(mat), matrix);
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
    /// Each child can be any entity type, including another "csg" for complex
    /// Boolean trees like (A ∪ B) \ C. Children inherit the parent's material
    /// unless they specify their own via material ID.
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
 
        return new CsgObject(operation.Value, leftHittable, rightHittable);
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
    /// Creates a Mesh entity by loading an OBJ file.
    /// The OBJ path is resolved relative to the scene YAML directory.
    /// </summary>
    private static IHittable? CreateMeshEntity(EntityData e, IMaterial mat, string sceneDir, int entityIndex)
    {
        if (string.IsNullOrWhiteSpace(e.Path))
        {
            Warn($"Mesh entity '{e.Name ?? "(unnamed)"}' requires a 'path' to an OBJ file. Skipping.");
            return null;
        }
 
        // Resolve the OBJ path relative to the YAML scene directory
        string objPath = Path.IsPathRooted(e.Path)
            ? e.Path
            : Path.Combine(sceneDir, e.Path);
 
        if (!File.Exists(objPath))
        {
            Warn($"Mesh entity '{e.Name ?? "(unnamed)"}': OBJ file not found at '{objPath}'. Skipping.");
            return null;
        }
 
        var warnings = new List<string>();
        var mesh = ObjLoader.Load(objPath, mat, warnings);
 
        // Report OBJ warnings
        foreach (var w in warnings)
            Warn($"Mesh '{e.Name ?? Path.GetFileName(objPath)}': {w}");
 
        if (mesh == null)
        {
            Warn($"Mesh entity '{e.Name ?? "(unnamed)"}': failed to load '{objPath}'. Skipping.");
            return null;
        }
 
        // Report mesh stats
        Info($"Mesh '{e.Name ?? Path.GetFileName(objPath)}': " +
             $"{mesh.FaceCount:N0} faces, {mesh.VertexCount:N0} vertices");
 
        // Seed assignment (same logic as CreateEntity)
        mesh.Seed = e.Seed ?? HashCode.Combine(entityIndex,
                                                e.Type?.GetHashCode() ?? 0,
                                                e.Name?.GetHashCode() ?? 0);
 
        return mesh;
    }

    /// <summary>
    /// Creates a Group entity — a hierarchical container of children with
    /// inherited material and shared transform.
    /// </summary>
    private static IHittable? CreateGroupEntity(EntityData e, IMaterial fallbackMat,
        Dictionary<string, IMaterial> materials, string sceneDir, int entityIndex)
    {
        if (e.Children == null || e.Children.Count == 0)
        {
            Warn($"Group '{e.Name ?? "(unnamed)"}' has no children. Skipping.");
            return null;
        }
 
        var childObjects = BuildChildList(e.Children, fallbackMat, materials, sceneDir, entityIndex);
 
        if (childObjects.Count == 0)
        {
            Warn($"Group '{e.Name ?? "(unnamed)"}': all children failed to load. Skipping.");
            return null;
        }
 
        var group = new Group(childObjects);
        group.Seed = e.Seed ?? HashCode.Combine(entityIndex,
                                                 e.Type?.GetHashCode() ?? 0,
                                                 e.Name?.GetHashCode() ?? 0);
 
        Info($"Group '{e.Name ?? "(unnamed)"}': {childObjects.Count} children");
        return group;
    }
 
    /// <summary>
    /// Creates an instance from a named template. The template's children are
    /// re-built as a new Group, optionally with a material override. The template's
    /// own transform (if any) is applied as the Group's "default pose", and the
    /// instance's transform is applied on top by the caller in Load().
    ///
    /// Transform composition chain:
    ///   child_local → template_transform → instance_transform
    /// </summary>
    private static IHittable? CreateInstanceEntity(EntityData e,
        Dictionary<string, IMaterial> materials,
        Dictionary<string, EntityData> templates,
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
 
        // Resolve material: instance override → template default → grey fallback
        var materialId = e.Material ?? templateDef.Material;
        var fallbackMat = GetMaterial(materials, materialId);
 
        // Build a fresh Group from the template's children
        var childObjects = BuildChildList(
            templateDef.Children!, fallbackMat, materials, sceneDir, entityIndex);
 
        if (childObjects.Count == 0)
        {
            Warn($"Instance '{e.Name ?? "(unnamed)"}' of '{e.Template}': " +
                 "all template children failed to load. Skipping.");
            return null;
        }
 
        IHittable result = new Group(childObjects);
 
        // Seed: instance seed takes precedence over template seed
        result.Seed = e.Seed ?? templateDef.Seed ?? HashCode.Combine(
            entityIndex, e.Template.GetHashCode(), e.Name?.GetHashCode() ?? 0);
 
        // Apply the template's own transform as the "default pose".
        // The instance's transform (from the caller in Load()) composes on top.
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
        string sceneDir, int parentIndex)
    {
        var result = new List<IHittable>();
 
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var mat = child.Material != null
                ? GetMaterial(materials, child.Material)
                : fallbackMat;
 
            int childIdx = parentIndex * 1000 + i;
 
            IHittable? hittable;
            if (string.Equals(child.Type, "csg", StringComparison.OrdinalIgnoreCase))
            {
                hittable = CreateCsgEntity(child, mat, materials, childIdx);
            }
            else if (string.Equals(child.Type, "mesh", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(child.Type, "obj", StringComparison.OrdinalIgnoreCase))
            {
                hittable = CreateMeshEntity(child, mat, sceneDir, childIdx);
            }
            else if (string.Equals(child.Type, "group", StringComparison.OrdinalIgnoreCase))
            {
                hittable = CreateGroupEntity(child, mat, materials, sceneDir, childIdx);
            }
            else
            {
                hittable = CreateEntity(child, mat, childIdx);
            }
 
            if (hittable == null) continue;
 
            var childTransform = ComputeTransformMatrix(child);
            if (childTransform != Matrix4x4.Identity)
                hittable = new Transform(hittable, childTransform);
 
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

    private static ILight? CreateLight(LightData l, int? shadowSamplesOverride)
    {
        var color = ToVector3(l.Color) ?? Vector3.One;

        return l.Type?.ToLowerInvariant() switch
        {
            "point" => new PointLight(
                ToVector3(l.Position) ?? new Vector3(0, 10, 0),
                color, l.Intensity),

            "directional" or "sun" => new DirectionalLight(
                ToVector3(l.Direction) ?? new Vector3(-1, -1, -1),
                color, l.Intensity),

            "spot" or "spotlight" => new SpotLight(
                ToVector3(l.Position)  ?? new Vector3(0, 10, 0),
                ToVector3(l.Direction) ?? new Vector3(0, -1, 0),
                color, l.Intensity, l.InnerAngle, l.OuterAngle),

            // ── Area light ───────────────────────────────────────────────────
            // YAML fields:
            //   corner: [x, y, z]        # one corner of the rectangle
            //   u:      [x, y, z]        # first edge vector  (e.g. [2,0,0])
            //   v:      [x, y, z]        # second edge vector (e.g. [0,0,2])
            //   intensity: 40.0          # brightness scalar
            //   shadow_samples: 16       # per-light default (overridable via CLI -S)
            "area" or "area_light" or "rect" or "rect_light"
                => CreateAreaLight(l, color, shadowSamplesOverride),

            // ── Sphere light ─────────────────────────────────────────────────
            // YAML fields:
            //   position: [x, y, z]      # center of the sphere
            //   radius:   0.5            # sphere radius (> 0)
            //   intensity: 30.0          # brightness scalar
            //   shadow_samples: 16       # per-light default (overridable via CLI -S)
            "sphere" or "sphere_light" or "ball" or "ball_light"
                => CreateSphereLight(l, color, shadowSamplesOverride),

            _ => null
        };
    }

    private static AreaLight? CreateAreaLight(LightData l, Vector3 color, int? shadowSamplesOverride)
    {
        var corner = ToVector3(l.Corner);
        var u      = ToVector3(l.U);
        var v      = ToVector3(l.V);

        if (corner == null || u == null || v == null)
        {
            Warn("Area light requires 'corner', 'u', and 'v' vectors. Skipping.");
            return null;
        }

        // CLI override takes precedence over per-light YAML value
        int effectiveShadowSamples = shadowSamplesOverride ?? l.ShadowSamples;

        return new AreaLight(corner.Value, u.Value, v.Value, color,
                             l.Intensity, effectiveShadowSamples);
    }

    private static SphereLight? CreateSphereLight(LightData l, Vector3 color,
                                                   int? shadowSamplesOverride)
    {
        var position = ToVector3(l.Position);
 
        if (position == null)
        {
            Warn("Sphere light requires 'position'. Using default [0, 10, 0].");
            position = new Vector3(0, 10, 0);
        }
 
        if (l.Radius <= 0f)
        {
            Warn($"Sphere light radius must be positive (got {l.Radius:F3}). Using 0.5.");
            l.Radius = 0.5f;
        }
 
        // CLI override takes precedence over per-light YAML value
        int effectiveShadowSamples = shadowSamplesOverride ?? l.ShadowSamples;
 
        return new SphereLight(position.Value, l.Radius, color,
                               l.Intensity, effectiveShadowSamples);
    }

    // =========================================================================
    // Utility helpers
    // =========================================================================

    private static bool IsInfinitePlane(IHittable obj) => obj switch
    {
        InfinitePlane => true,
        Transform t   => IsInfinitePlane(t.Inner),
        Group g       => g.Children.Count > 0 && g.Children.All(c => IsInfinitePlane(c)),
        _             => false
    };

    /// <summary>
    /// Retrieves a material by ID. Returns a default grey Lambertian if not found
    /// and emits a console warning to help identify typos in material references.
    /// </summary>
    private static IMaterial GetMaterial(Dictionary<string, IMaterial> dict, string? id)
    {
        if (id != null && dict.TryGetValue(id, out var mat)) return mat;
        // FIX (ISSUE #1): silently returning a fallback made missing IDs very hard
        // to debug. The warning makes the problem immediately visible in the console.
        if (id != null)
            Warn($"Material '{id}' not found. Using default grey Lambertian.");
        return new Lambertian(new Vector3(0.5f));
    }

    private static Vector3? ToVector3(List<float>? list)
    {
        if (list == null || list.Count < 3) return null;
        return new Vector3(list[0], list[1], list[2]);
    }

    private static System.Numerics.Vector2? ToVector2(List<float>? v)
    {
        if (v == null || v.Count < 2) return null;
        return new System.Numerics.Vector2(v[0], v[1]);
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
}
