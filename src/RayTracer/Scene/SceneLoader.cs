using System.Diagnostics.CodeAnalysis;
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
using RayTracer.Volumetrics;

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
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, Vector3 AmbientLight, SkySettings Sky, IMedium? GlobalMedium)
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

        // ── Global participating medium (opt-in) ─────────────────────────────────
        // Default null = surface-only path, bit-identical to pre-volumetric output.
        IMedium? globalMedium = null;
        if (data.World?.Medium is { } md)
        {
            globalMedium = BuildGlobalMedium(md, sceneDir);
        }

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

        // Built once per template, on first instance request. Subsequent
        // instances share the same IHittable reference (and its BVH/meshes),
        // turning N instances of a heavy mesh from O(N) memory to O(1).
        var templateCache = new Dictionary<string, IHittable>(StringComparer.OrdinalIgnoreCase);

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
                    hittable = CreateInstanceEntity(e, materials, templates, templateCache, sceneDir, idx);
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

        // Add Emissive Objects as Geometry Lights. The default for geometry
        // lights is aligned with AreaLight/SphereLight (16), so emissive
        // primitives get comparable soft-shadow quality without extra YAML.
        ExtractGeometryLights(objects, lights, shadowSamplesOverride ?? GeometryLight.DefaultShadowSamples);

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

        return (world, camera, lights, ambientLight, sky, globalMedium);
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
            Warn($"--camera '{selector}' not found. Using camera 0.");
            Warn($"Available cameras: {names}");
            return list[0];
        }

        // No selector → use the only camera silently, or warn and use first
        if (list.Count == 1)
            return list[0];

        var cameraNames = string.Join(", ",
            list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
        Warn($"Scene contains {list.Count} cameras. Using camera 0. " +
             $"Use --camera <name|index> to select one.");
        Warn($"Available cameras: {cameraNames}");
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
    /// Handles:
    ///   1. Bare primitive with Emissive material.
    ///   2. Transform-wrapped primitive (at any nesting depth, transforms composed).
    ///   3. Group / Transform(Group(...)) / Transform(Transform(...)) : recurses
    ///      into children, composing transforms for correct world-space sampling.
    ///   4. MixMaterial(Emissive, …) on a primitive — registered as a geometry light
    ///      using a synthetic <see cref="Emissive"/> built from the blended emission
    ///      of the two sub-materials (see <see cref="ExtractMixEmissive"/>).
    ///   5. CSG containing an Emissive leaf — emits a one-time warning because
    ///      CSG is not ISamplable and therefore cannot participate in NEE.
    /// </summary>
    private static void ExtractGeometryLights(List<IHittable> objects, List<ILight> lights, int shadowSamples)
    {
        foreach (var obj in objects)
            ExtractGeometryLightsRecursive(obj, lights, shadowSamples, Matrix4x4.Identity, hasOuterTransform: false);
    }

    /// <summary>
    /// Recursively walks the scene graph composing transforms and registering
    /// every emissive leaf it encounters. The composed matrix
    /// <paramref name="outerMatrix"/> accumulates the transforms seen so far;
    /// when a primitive is reached it is wrapped in a single <see cref="Transform"/>
    /// carrying the composed matrix (unless the accumulated matrix is still
    /// identity, in which case the primitive is registered directly).
    /// </summary>
    private static void ExtractGeometryLightsRecursive(
        IHittable obj,
        List<ILight> lights,
        int shadowSamples,
        Matrix4x4 outerMatrix,
        bool hasOuterTransform)
    {
        switch (obj)
        {
            // ── Group: recurse into children with the current outer transform
            case Group g:
                foreach (var child in g.Children)
                    ExtractGeometryLightsRecursive(child, lights, shadowSamples, outerMatrix, hasOuterTransform);
                break;

            // ── Transform: compose and recurse into Inner
            // Composition order follows left-to-right matrix multiplication:
            // world = M_outer * M_inner  →  the inner transform is applied first.
            case Transform t:
            {
                Matrix4x4 composed = hasOuterTransform
                    ? t.TransformMatrix * outerMatrix
                    : t.TransformMatrix;
                ExtractGeometryLightsRecursive(t.Inner, lights, shadowSamples, composed, hasOuterTransform: true);
                break;
            }

            // ── CSG: currently not supported for NEE; warn once if it hides an emissive leaf
            case CsgObject csg:
                WarnIfCsgContainsEmissive(csg);
                break;

            // ── Leaf primitive: try to register it
            default:
            {
                if (!TryGetSamplableEmissive(obj, out ISamplable? samplable, out Emissive? emissive))
                    return;

                // Wrap in a Transform only when there is an accumulated outer transform.
                // When outerMatrix is identity we register the primitive directly to
                // avoid the minor Jacobian overhead of a no-op Transform.
                ISamplable finalSamplable = hasOuterTransform
                    ? new Transform(obj, outerMatrix)
                    : samplable;

                lights.Add(new GeometryLight(finalSamplable, emissive, shadowSamples));
                break;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> and sets the ISamplable / Emissive pair when
    /// <paramref name="obj"/> is a primitive with either:
    ///   • a direct <see cref="Emissive"/> material, or
    ///   • a <see cref="MixMaterial"/> whose blend contains an Emissive component.
    ///
    /// For the Mix case the returned Emissive is a wrapper whose average emission
    /// is the blend of the two sub-materials' emissions — matching what
    /// <see cref="MixMaterial.Emit"/> returns. This lets NEE fire on surfaces such
    /// as "cooling lava" (Emissive + Lambertian) or "partial neon" (Emissive + Metal).
    /// </summary>
    private static bool TryGetSamplableEmissive(
        IHittable obj,
        [NotNullWhen(true)] out ISamplable? samplable,
        [NotNullWhen(true)] out Emissive? material)
    {
        samplable = null;
        material = null;

        if (obj is not ISamplable s)
            return false;

        IMaterial? mat = obj switch
        {
            Sphere         sp => sp.Material,
            Quad           q  => q.Material,
            Triangle       tr => tr.Material,
            SmoothTriangle st => st.Material,
            Disk           d  => d.Material,
            Box            b  => b.Material,
            Cylinder       cy => cy.Material,
            Cone           co => co.Material,
            Torus          to => to.Material,
            Capsule        ca => ca.Material,
            Annulus        an => an.Material,
            Mesh           ms => ms.Material,
            _ => null
        };

        switch (mat)
        {
            case Emissive em:
                samplable = s;
                material = em;
                return true;

            case MixMaterial mix when ExtractMixEmissive(mix) is Emissive mixEm:
                samplable = s;
                material = mixEm;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Produces a synthetic <see cref="Emissive"/> representing the effective
    /// emission of a MixMaterial. For a Mix(A, B) the emission is
    /// <c>lerp(A.Emit, B.Emit, t)</c> where t is the blend factor. We cannot
    /// evaluate t analytically without UV/world coords, so we use the documented
    /// neutral mid-value (t = 0.5) of the blend texture and the center-texel
    /// approximation for the two sub-emissives.
    /// Returns null when the mix contains no emissive sub-material.
    /// </summary>
    private static Emissive? ExtractMixEmissive(MixMaterial mix)
    {
        Emissive? a = mix.MaterialA as Emissive;
        Emissive? b = mix.MaterialB as Emissive;
        if (a is null && b is null) return null;

        // Representative emission of each sub-material at the centre texel.
        Vector3 ea = a is not null ? a.EmissionAt(0.5f, 0.5f, Vector3.Zero) : Vector3.Zero;
        Vector3 eb = b is not null ? b.EmissionAt(0.5f, 0.5f, Vector3.Zero) : Vector3.Zero;

        // Blend factor neutral assumption: t = 0.5.
        Vector3 blended = 0.5f * (ea + eb);
        float maxC = MathF.Max(blended.X, MathF.Max(blended.Y, blended.Z));
        if (maxC <= 1e-6f) return null;

        // Encode as (colour, intensity) with intensity = max channel luminance,
        // matching the SolidColor(colour) × intensity representation of Emissive.
        Vector3 normalised = blended / maxC;
        return new Emissive(normalised, maxC);
    }

    /// <summary>
    /// Walks a CSG tree and, if any leaf carries an Emissive material, prints
    /// a one-time warning explaining why it won't contribute to NEE.
    /// </summary>
    private static void WarnIfCsgContainsEmissive(CsgObject csg)
    {
        if (ContainsEmissive(csg.Left) || ContainsEmissive(csg.Right))
        {
            if (_csgEmissiveWarningEmitted) return;
            _csgEmissiveWarningEmitted = true;
            Console.WriteLine(
                "  Warning: CSG object contains an Emissive leaf. CSG objects are " +
                "not sampleable, so their emitters will NOT participate in Next " +
                "Event Estimation. The emissive surface will still glow via " +
                "indirect bounces (high variance). Consider wrapping the " +
                "emissive primitive outside the CSG if direct lighting is needed.");
        }
    }

    private static bool _csgEmissiveWarningEmitted;

    private static bool ContainsEmissive(IHittable obj)
    {
        return obj switch
        {
            CsgObject csg    => ContainsEmissive(csg.Left) || ContainsEmissive(csg.Right),
            Transform t      => ContainsEmissive(t.Inner),
            Group g          => g.Children.Any(ContainsEmissive),
            Sphere s         => s.Material is Emissive,
            Quad q           => q.Material is Emissive,
            Triangle tr      => tr.Material is Emissive,
            SmoothTriangle st=> st.Material is Emissive,
            Disk d           => d.Material is Emissive,
            Box b            => b.Material is Emissive,
            Cylinder cy      => cy.Material is Emissive,
            Cone co          => co.Material is Emissive,
            Torus to         => to.Material is Emissive,
            Capsule ca       => ca.Material is Emissive,
            Annulus an       => an.Material is Emissive,
            Mesh ms          => ms.Material is Emissive,
            _ => false
        };
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
                                metallic:            DisneyParam(m.Metallic,            m.MetallicTexture,            sceneDir),
                                roughness:           DisneyParam(m.Roughness,           m.RoughnessTexture,           sceneDir),
                                subsurface:          DisneyParam(m.Subsurface,          m.SubsurfaceTexture,          sceneDir),
                                specular:            DisneyParam(m.Specular,            m.SpecularTexture,            sceneDir),
                                specularTint:        DisneyParam(m.SpecularTint,        m.SpecularTintTexture,        sceneDir),
                                sheen:               DisneyParam(m.Sheen,               m.SheenTexture,               sceneDir),
                                sheenTint:           DisneyParam(m.SheenTint,           m.SheenTintTexture,           sceneDir),
                                clearcoat:           DisneyParam(m.Clearcoat,           m.ClearcoatTexture,           sceneDir),
                                clearcoatGloss:      DisneyParam(m.ClearcoatGloss,      m.ClearcoatGlossTexture,      sceneDir),
                                specTrans:           DisneyParam(m.SpecTrans,           m.SpecTransTexture,           sceneDir),
                                ior:                 DisneyParam(m.DisneyIor,           m.IorTexture,                 sceneDir),
                                anisotropic:         DisneyParam(m.Anisotropic,         m.AnisotropicTexture,         sceneDir),
                                anisotropicRotation: DisneyParam(m.AnisotropicRotation, m.AnisotropicRotationTexture, sceneDir),
                                transmissionColor:   DisneyColorParam(m.TransmissionColor, m.TransmissionColorTexture, sceneDir),
                                transmissionDepth:   DisneyParam(m.TransmissionDepth,   m.TransmissionDepthTexture,   sceneDir),
                                subsurfaceColor:     DisneyColorParam(m.SubsurfaceColor, m.SubsurfaceColorTexture,    sceneDir),
                                diffTrans:           DisneyParam(m.DiffTrans,           m.DiffTransTexture,           sceneDir),
                                flatness:            DisneyParam(m.Flatness,            m.FlatnessTexture,            sceneDir),
                                thinWalled:          m.ThinWalled,
                                coatIor:             DisneyParam(m.CoatIor,             m.CoatIorTexture,             sceneDir),
                                // coat_roughness: only forwarded when the user
                                // explicitly set a non-negative value or a
                                // texture; otherwise null selects the legacy
                                // ClearcoatGloss path inside DisneyBsdf.
                                coatRoughness:       (m.CoatRoughness >= 0f || m.CoatRoughnessTexture != null)
                                                        ? DisneyParam(MathF.Max(m.CoatRoughness, 0f), m.CoatRoughnessTexture, sceneDir)
                                                        : null),
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

        // ── Coat normal map (Disney only — exclusive to the clearcoat lobe) ─
        if (m.CoatNormalMap != null && material is DisneyBsdf disneyMat)
        {
            var coatNormal = LoadNormalMap(m.CoatNormalMap, sceneDir);
            if (coatNormal != null)
                disneyMat.CoatNormal = coatNormal;
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

    /// <summary>
    /// Builds a <see cref="FloatTexture"/> for a Disney BSDF parameter: prefers
    /// the texture block when supplied, otherwise wraps the scalar value.
    /// </summary>
    private static FloatTexture DisneyParam(float scalar, TextureData? tex, string sceneDir)
        => tex != null ? new FloatTexture(CreateTexture(tex, sceneDir)) : new FloatTexture(scalar);

    /// <summary>
    /// Builds a nullable <see cref="ITexture"/> for a Disney colour parameter
    /// where "unset" is semantically meaningful — e.g. <c>transmission_color</c>,
    /// whose null value activates the legacy sqrt(baseColor) fallback in
    /// <see cref="DisneyBsdf"/>. Prefers the texture block when supplied; else
    /// wraps the RGB triplet if the user provided one; else returns null.
    /// </summary>
    private static ITexture? DisneyColorParam(List<float>? scalar, TextureData? tex, string sceneDir)
    {
        if (tex != null) return CreateTexture(tex, sceneDir);
        Vector3? c = ToVector3(scalar);
        return c.HasValue ? new SolidColor(c.Value) : null;
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
        mesh.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
 
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
        group.Seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
 
        Info($"Group '{e.Name ?? "(unnamed)"}': {childObjects.Count} children");
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
            Info($"Template '{e.Template}' built and cached for instancing.");
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
            templateDef.Children!, fallbackMat, materials, sceneDir, templateIndex);

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

    /// <summary>
    /// Deterministic 32-bit hash of a string (FNV-1a). Used for seed fallback
    /// when the YAML does not specify <c>seed:</c> on an entity with procedural
    /// textures.
    ///
    /// <b>Why not <c>string.GetHashCode()</c>?</b> Since .NET Core 3, the built-in
    /// hash is randomized per process (mitigation against hash-collision DoS).
    /// For a ray tracer this would mean the same scene produces different Perlin
    /// patterns on every run — breaking reproducibility, visual regression tests,
    /// and the general expectation that "scene description = image". FNV-1a is
    /// stable across processes and architectures, has good dispersion for short
    /// strings, and costs essentially nothing.
    /// </summary>
    private static int StableHash(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        uint hash = 2166136261u; // FNV offset basis
        foreach (char c in s)
            hash = (hash ^ c) * 16777619u; // FNV prime
        return unchecked((int)hash);
    }

    /// <summary>
    /// Builds a deterministic per-object seed from an integer index and up to
    /// two string identifiers (e.g. type + name, or template + name).
    ///
    /// <b>Why not <c>HashCode.Combine</c>?</b> Same reason as <see cref="StableHash"/>:
    /// .NET's <c>HashCode</c> intentionally injects per-process randomness, so
    /// using it here would re-introduce the cross-run non-determinism the
    /// FNV-1a hash is meant to avoid. The Boost-style mixer below is fully
    /// deterministic, has excellent avalanche behavior for small inputs, and
    /// is used widely in C++ codebases for the same purpose.
    /// </summary>
    private static int StableSeed(int index, string? a, string? b)
    {
        unchecked
        {
            uint h = (uint)index;
            h ^= (uint)StableHash(a) + 0x9e3779b9u + (h << 6) + (h >> 2);
            h ^= (uint)StableHash(b) + 0x9e3779b9u + (h << 6) + (h >> 2);
            return (int)h;
        }
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

    // =========================================================================
    // Participating medium construction
    // =========================================================================

    /// <summary>
    /// Dispatches on <c>type</c> to build the appropriate <see cref="IMedium"/>.
    /// Returns null (and warns) on invalid or unknown configurations so the
    /// renderer falls back to surface-only mode instead of crashing.
    /// </summary>
    private static IMedium? BuildGlobalMedium(MediumData md, string sceneDir)
    {
        string type = md.Type ?? "homogeneous";
        IPhaseFunction phase = BuildPhaseFunction(md);

        switch (type.ToLowerInvariant())
        {
            case "homogeneous":
                return BuildHomogeneous(md, phase);
            case "height_fog":
                return BuildHeightFog(md, phase);
            case "procedural":
                return BuildProcedural(md, phase);
            case "grid":
                return BuildGrid(md, phase, sceneDir);
            default:
                Warn($"Unsupported medium type '{type}'. Supported: homogeneous, height_fog, procedural, grid. Ignoring.");
                return null;
        }
    }

    private static IPhaseFunction BuildPhaseFunction(MediumData md)
    {
        if (string.IsNullOrWhiteSpace(md.Phase)) return new IsotropicPhase();
        switch (md.Phase!.ToLowerInvariant())
        {
            case "isotropic":
                return new IsotropicPhase();
            case "hg":
            case "henyey_greenstein":
                return new HenyeyGreensteinPhase(md.G);
            case "rayleigh":
                return new RayleighPhase();
            case "schlick":
                return new SchlickPhase(md.G);
            case "double_hg":
            case "double_henyey_greenstein":
                return new DoubleHenyeyGreensteinPhase(md.G1, md.G2, md.W);
            default:
                Warn($"Unknown phase function '{md.Phase}'. Supported: isotropic, hg, rayleigh, schlick, double_hg. Falling back to isotropic.");
                return new IsotropicPhase();
        }
    }

    private static (Vector3 A, Vector3 S) ParseSigmas(MediumData md, string context)
    {
        var a = ToVector3(md.SigmaA) ?? Vector3.Zero;
        var s = ToVector3(md.SigmaS) ?? Vector3.Zero;
        if (a.X < 0f || a.Y < 0f || a.Z < 0f || s.X < 0f || s.Y < 0f || s.Z < 0f)
        {
            Warn($"{context} has negative σ_a={a} or σ_s={s}. Clamping to zero.");
            a = Vector3.Max(a, Vector3.Zero);
            s = Vector3.Max(s, Vector3.Zero);
        }
        return (a, s);
    }

    private static IMedium BuildHomogeneous(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'homogeneous'");
        Info($"Global medium: homogeneous, σ_a={sigmaA}, σ_s={sigmaS}, phase={md.Phase ?? "isotropic"}");
        return new HomogeneousMedium(sigmaA, sigmaS, phase);
    }

    private static IMedium BuildHeightFog(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'height_fog'");
        if (md.ScaleHeight <= 0f)
        {
            Warn($"Medium 'height_fog' has non-positive scale_height={md.ScaleHeight}. Using 1.");
        }
        float H = md.ScaleHeight > 0f ? md.ScaleHeight : 1f;
        Info($"Global medium: height_fog, σ_a={sigmaA}, σ_s={sigmaS}, y0={md.Y0}, H={H}, phase={md.Phase ?? "isotropic"}");
        return new HeightFogMedium(sigmaA, sigmaS, md.Y0, H, phase);
    }

    private static IMedium BuildProcedural(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'procedural'");
        if (md.Frequency <= 0f) Warn($"Medium 'procedural' has non-positive frequency={md.Frequency}. Using 1.");
        float freq = md.Frequency > 0f ? md.Frequency : 1f;
        Info($"Global medium: procedural (Perlin fBm), σ_base_a={sigmaA}, σ_base_s={sigmaS}, " +
             $"freq={freq}, octaves={md.Octaves}, phase={md.Phase ?? "isotropic"}");
        return new HeterogeneousProceduralMedium(
            sigmaA, sigmaS, freq, md.Octaves, md.Lacunarity, md.Gain, md.Seed, phase);
    }

    private static IMedium? BuildGrid(MediumData md, IPhaseFunction phase, string sceneDir)
    {
        var boundsMin = ToVector3(md.BoundsMin);
        var boundsMax = ToVector3(md.BoundsMax);
        if (boundsMin is null || boundsMax is null)
        {
            Warn("Medium 'grid' requires bounds_min and bounds_max. Ignoring.");
            return null;
        }

        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'grid'");

        float[]? data = null;
        int nx = md.Nx, ny = md.Ny, nz = md.Nz;

        if (!string.IsNullOrWhiteSpace(md.File))
        {
            // Binary .vol format:
            //   magic 'V','O','L','1' | nx int32 | ny int32 | nz int32
            //   boundsMin[3] float32  | boundsMax[3] float32
            //   data: nx*ny*nz float32 values in z-major order.
            try
            {
                string path = Path.IsPathRooted(md.File!)
                    ? md.File!
                    : Path.Combine(sceneDir, md.File!);
                (nx, ny, nz, data) = LoadVolFile(path);
                Info($"Global medium: grid from '{md.File}', {nx}×{ny}×{nz}");
            }
            catch (Exception ex)
            {
                Warn($"Medium 'grid' failed to load '{md.File}': {ex.Message}. Ignoring.");
                return null;
            }
        }
        else if (md.Data != null)
        {
            if (nx <= 0 || ny <= 0 || nz <= 0)
            {
                Warn("Medium 'grid' with inline 'data' requires positive nx, ny, nz. Ignoring.");
                return null;
            }
            if (md.Data.Count != nx * ny * nz)
            {
                Warn($"Medium 'grid' inline data length {md.Data.Count} does not match nx*ny*nz = {nx * ny * nz}. Ignoring.");
                return null;
            }
            data = md.Data.ToArray();
            Info($"Global medium: grid inline, {nx}×{ny}×{nz}");
        }
        else
        {
            Warn("Medium 'grid' requires either 'file' or 'data'. Ignoring.");
            return null;
        }

        GridInterpolation interp = GridInterpolation.Trilinear;
        string interpKey = (md.Interpolation ?? "trilinear").Trim().ToLowerInvariant();
        switch (interpKey)
        {
            case "":
            case "trilinear":
            case "linear":
                interp = GridInterpolation.Trilinear;
                break;
            case "tricubic":
            case "cubic":
            case "catmull-rom":
            case "catmull_rom":
            case "smooth":
                interp = GridInterpolation.Tricubic;
                break;
            default:
                Warn($"Medium 'grid' unknown interpolation '{md.Interpolation}'. Using 'trilinear'.");
                break;
        }

        try
        {
            var medium = new GridMedium(sigmaA, sigmaS,
                                        boundsMin.Value, boundsMax.Value,
                                        nx, ny, nz, data!, phase, interp);
            Info($"Global medium: grid interpolation = {interp.ToString().ToLowerInvariant()}");
            return medium;
        }
        catch (Exception ex)
        {
            Warn($"Medium 'grid' construction failed: {ex.Message}. Ignoring.");
            return null;
        }
    }

    private static (int Nx, int Ny, int Nz, float[] Data) LoadVolFile(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        using var br = new BinaryReader(fs);
        byte m0 = br.ReadByte(), m1 = br.ReadByte(), m2 = br.ReadByte(), m3 = br.ReadByte();
        if (m0 != (byte)'V' || m1 != (byte)'O' || m2 != (byte)'L' || m3 != (byte)'1')
            throw new InvalidDataException("Not a VOL1 file.");
        int nx = br.ReadInt32();
        int ny = br.ReadInt32();
        int nz = br.ReadInt32();
        // bounds are present in the file but already provided by YAML — skip 6 floats.
        for (int i = 0; i < 6; i++) br.ReadSingle();
        int count = nx * ny * nz;
        var data = new float[count];
        for (int i = 0; i < count; i++) data[i] = br.ReadSingle();
        return (nx, ny, nz, data);
    }
}
