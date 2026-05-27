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

public class SceneLoader
{
    /// <summary>
    /// Minimum number of objects before BVH construction is beneficial.
    /// Below this threshold, linear search in HittableList is faster due to
    /// lower overhead. Above it, O(log N) BVH traversal wins.
    /// </summary>
    private const int BvhThreshold = 4;

    /// <summary>
    /// Per-Load camera context used by the adaptive subdivision heuristic.
    /// Set once at the top of <see cref="Load"/>; read by
    /// <see cref="CreateMeshEntity"/> regardless of nesting depth (groups,
    /// templates, instances). Cleared by the next Load.
    /// </summary>
    private static SubdivisionScreenContext _currentScreenContext;

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
    private static bool _verbose;

    /// <summary>
    /// Enables or disables verbose output. When disabled, only warnings and
    /// essential informational messages are printed; when enabled, detailed
    /// debug-level messages (imports, templates, σ values, RR tuning) are
    /// included as well.
    /// </summary>
    public static void SetVerbose(bool verbose) => _verbose = verbose;

    /// <summary>
    /// Returns the current verbose state. Used by <see cref="Renderer"/> to
    /// decide how much scene-analysis detail to print.
    /// </summary>
    public static bool IsVerbose => _verbose;

    /// <summary>
    /// Queues a warning message to be printed after the loading phase completes.
    /// </summary>
    private static void Warn(string message)
    {
        _deferredMessages.Add($"  [Warning] {message}");
    }

    /// <summary>
    /// Queues an informational (non-warning) message to be printed after loading.
    /// Always visible regardless of verbose mode.
    /// </summary>
    private static void Info(string message)
    {
        _deferredMessages.Add($"  {message}");
    }

    /// <summary>
    /// Queues a debug-level message shown only in verbose mode.
    /// </summary>
    private static void Verbose(string message)
    {
        if (_verbose)
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
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, SkySettings Sky, IMedium? GlobalMedium)
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
                // Material-embedded SSS auto-defaults: when subsurface_radius
                // is set, the loader silently fills in the refractive plumbing
                // needed by the Disney BSDF transmission lobe (which is what
                // actually triggers MediumTransition.Enter / Exit and the
                // Random Walk Phase 3 integrator). Mirrors Arnold's
                // standard_surface convention: an artist who only sets
                // subsurface_* parameters gets working volumetric SSS without
                // boilerplate. Explicit values authored by the user are
                // always preserved.
                ApplyEmbeddedSssDefaults(m);
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

        var sky = BuildSkySettings(data.World?.Sky, sceneDir);

        // ── Global participating medium (opt-in) ─────────────────────────────────
        // Default null = surface-only path, bit-identical to pre-volumetric output.
        IMedium? globalMedium = null;
        if (data.World?.Medium is { } md)
        {
            globalMedium = BuildMedium(md, sceneDir, "world.medium");
        }

        // ── Medium library (per-entity binding via interior_medium / exterior_medium) ───
        // Built before entities are created so CreateEntity / its callers can
        // resolve a binding directly. Duplicate ids: last-write-wins matches
        // `materials:`. Mediums without an id are skipped with a warning —
        // unreferenceable.
        var mediumLibrary = new Dictionary<string, IMedium>(StringComparer.OrdinalIgnoreCase);
        if (data.Mediums != null)
        {
            foreach (var mm in data.Mediums)
            {
                if (string.IsNullOrWhiteSpace(mm.Id))
                {
                    Warn("Medium entry without an 'id' field. Skipping.");
                    continue;
                }
                var built = BuildMedium(mm, sceneDir, $"mediums['{mm.Id}']");
                if (built == null) continue;
                if (mediumLibrary.ContainsKey(mm.Id))
                    Warn($"Medium id '{mm.Id}' declared more than once. Last definition wins.");
                mediumLibrary[mm.Id] = built;
            }
            if (mediumLibrary.Count > 0)
                Info($"Mediums:     {mediumLibrary.Count} registered " +
                     $"({string.Join(", ", mediumLibrary.Keys)})");
        }

        // ── Material-embedded SSS mediums (Arnold standard_surface parity) ──
        // When a material declares `subsurface_radius`, build an anonymous
        // HomogeneousMedium derived from that MFP + subsurface_color (volume
        // albedo). The medium is later auto-injected on every entity that
        // references the material AND lacks an explicit `interior_medium`
        // (explicit binding always wins — Arnold/Cycles convention).
        var embeddedSssMedium = new Dictionary<string, IMedium>(StringComparer.OrdinalIgnoreCase);
        if (data.Materials != null)
        {
            foreach (var m in data.Materials)
            {
                if (m.Id == null) continue;
                if (m.SubsurfaceRadius == null) continue;
                if (m.SubsurfaceRadius.Count == 0) continue;

                var med = BuildEmbeddedSssMedium(m);
                if (med != null)
                    embeddedSssMedium[m.Id] = med;
            }
            if (embeddedSssMedium.Count > 0)
                Info($"SSS embedded: {embeddedSssMedium.Count} material(s) with auto-built medium " +
                     $"({string.Join(", ", embeddedSssMedium.Keys)})");
        }

        var objects = new List<IHittable>();

        // Ground plane — see BuildGround for the full type/material/UV/visibility dispatch.
        if (data.World?.Ground != null)
        {
            var groundHittable = BuildGround(data.World.Ground, data.World.Sky, materials, sceneDir);
            if (groundHittable != null)
                objects.Add(groundHittable);
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
                Verbose($"Templates:   {templates.Count} registered " +
                        $"({string.Join(", ", templates.Keys)})");
        }

        // Built once per template, on first instance request. Subsequent
        // instances share the same IHittable reference (and its BVH/meshes),
        // turning N instances of a heavy mesh from O(N) memory to O(1).
        var templateCache = new Dictionary<string, IHittable>(StringComparer.OrdinalIgnoreCase);

        // ── Pre-resolve camera for adaptive subdivision ───────────────────
        // The screen-space pixel-error heuristic needs the resolved camera
        // origin / forward axis / FOV before the mesh loader runs. We
        // resolve those now and build the actual Camera.Camera object below
        // after all entities are created.
        var camDataEarly   = ResolveCamera(data, cameraSelector);
        var camPosEarly    = ToVector3(camDataEarly.Position) ?? new Vector3(0, 1, -5);
        var camLookAtEarly = ToVector3(camDataEarly.LookAt)   ?? Vector3.Zero;
        var camForwardEarly = camLookAtEarly - camPosEarly;
        if (camForwardEarly.LengthSquared() < 1e-8f) camForwardEarly = -Vector3.UnitZ;
        camForwardEarly = Vector3.Normalize(camForwardEarly);
        float camFovEarlyRad = MathUtils.DegreesToRadians(camDataEarly.Fov);
        _currentScreenContext = new SubdivisionScreenContext
        {
            CameraOrigin       = camPosEarly,
            CameraForward      = camForwardEarly,
            VerticalFovRadians = camFovEarlyRad,
            ImageHeight        = imageHeight,
        };

        // Entities
        if (data.Entities != null)
        {
            for (int idx = 0; idx < data.Entities.Count; idx++)
            {
                var e   = data.Entities[idx];

                // Hard error on legacy entity-level displacement YAML. The
                // model has moved to material-level (Cycles/RenderMan parity)
                // and silent acceptance would let stale scenes render with no
                // displacement at all. Fail fast and point the author at the
                // migration.
                CheckLegacyEntityDisplacement(e);

                var mat = GetMaterial(materials, e.Material);

                // Warn (don't fail) when a non-mesh entity uses a material
                // that carries a displacement: the loader has no subdivided
                // limit topology to displace and silently ignores it. CSG
                // children are checked recursively inside CreateCsgEntity.
                if (mat.Displacement != null
                    && !string.Equals(e.Type, "mesh", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(e.Type, "obj",  StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(e.Type, "group", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(e.Type, "instance", StringComparison.OrdinalIgnoreCase))
                {
                    Warn($"Entity '{e.Name ?? e.Type ?? "(unnamed)"}' (type='{e.Type}') " +
                         $"uses material '{e.Material}' which has a displacement, " +
                         $"but displacement only applies to polygonal mesh entities. " +
                         $"Use a 'mesh' entity with subdivision, or attach 'bump_map' " +
                         $"to the material instead.");
                }

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
                else if (IsHeightFieldType(e.Type))
                {
                    hittable = CreateHeightFieldEntity(e, mat, materials, sceneDir, idx);
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
                    // Per-entity Arnold/Cycles "camera" visibility flag. Wrap
                    // after Transform so the BVH still partitions by the
                    // entity's world-space AABB; the flag only annotates the
                    // resulting HitRecord (see CameraInvisibleHittable).
                    if (!e.VisibleToCamera)
                        hittable = new CameraInvisibleHittable(hittable);
                    // Per-entity participating-media binding (interior /
                    // exterior). Wrap last so MediumInterface rides on every
                    // hit forwarded by inner wrappers — the renderer needs
                    // it during refraction to push/pop the medium stack.
                    // When the entity has no explicit interior_medium but the
                    // referenced material carries `subsurface_radius`, inject
                    // the material-embedded medium (Arnold standard_surface
                    // parity). Explicit binding on the entity always wins.
                    var mi = ResolveEntityMediumInterface(e, mediumLibrary, embeddedSssMedium);
                    if (mi.Interior != null || mi.Exterior != null)
                        hittable = new MediumBoundHittable(hittable, mi);
                    objects.Add(hittable);
                }
            }
        }

        // Lights — created BEFORE the BVH so sphere/area lights can append a
        // visible emissive proxy primitive to `objects`. The proxy makes the
        // light hittable by BSDF sampling rays (closing Veach's MIS estimator
        // on smooth-specular materials — see ILight.ProxyMaterial).
        var lights = new List<ILight>();
        if (data.Lights != null)
        {
            foreach (var l in data.Lights)
            {
                var light = CreateLight(l, shadowSamplesOverride, objects, sky);
                if (light != null) lights.Add(light);
            }
        }

        Info($"Objects:     {objects.Count:N0}");

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

        // Camera (re-uses the pre-resolved descriptor; see camDataEarly)
        float aspect  = (float)imageWidth / imageHeight;
        var camData   = camDataEarly;
        var camPos    = camPosEarly;
        var camLookAt = camLookAtEarly;
        var camVup    = ToVector3(camData.Vup) ?? Vector3.UnitY;
        // Resolve focus distance: focal_pos (a 3D point) wins over the
        // scalar focal_dist when set, mirroring Arnold/Cycles/RenderMan
        // "Focus Object" workflow. ComputeFocusDistance projects the point
        // onto the optical axis and emits Warn/Info on anomalies.
        float focusDist = ComputeFocusDistance(
            camPos, camLookAt,
            camData.FocalPos, camData.FocalDist);
        var camera    = new Camera.Camera(
            camPos, camLookAt, camVup,
            camData.Fov, aspect,
            camData.Aperture, focusDist);

        // Add Emissive Objects as Geometry Lights. The default for geometry
        // lights is aligned with AreaLight/SphereLight (16), so emissive
        // primitives get comparable soft-shadow quality without extra YAML.
        // Proxy emissives created above are skipped inside
        // TryGetSamplableEmissive (their parent SphereLight/AreaLight is
        // already the NEE source).
        ExtractGeometryLights(objects, lights, shadowSamplesOverride ?? GeometryLight.DefaultShadowSamples);

        // Add Environment Light if applicable
        if (sky.CanSampleDirectly)
        {
            lights.Add(new EnvironmentLight(sky, shadowSamplesOverride ?? 1));
        }

        // Register a PhysicalSun alongside the env light when the sky model
        // exposes an analytical sun. Decoupling the sun from the sky body
        // gives clean cone-sampled shadows and lower variance.
        var analyticalSun = sky.GetAnalyticalSun();
        if (analyticalSun != null)
        {
            var (sunDir, sunRad, halfDeg, limb) = analyticalSun.Value;
            int sunSamples = shadowSamplesOverride ?? data.World?.Sky?.Sun?.ShadowSamples ?? 4;
            lights.Add(new PhysicalSun(sunDir, sunRad, intensity: 1f,
                                       angularRadiusDeg: halfDeg,
                                       limbDarkening: limb,
                                       shadowSamples: sunSamples));
        }

        // Default lighting only when lights section is completely absent from YAML.
        // An explicit empty list (lights: []) means "no lights" intentionally
        // (e.g. HDRI-only or emissive-only scenes).
        if (lights.Count == 0 && data.Lights == null)
        {
            lights.Add(new DirectionalLight(new Vector3(-1, -1, -1), Vector3.One, 0.8f));
            lights.Add(new PointLight(new Vector3(0, 10, -5), Vector3.One, 100f));
        }

        return (world, camera, lights, sky, globalMedium);
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
        var importedMediums   = new List<MediumData>();
 
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
                if (importData.Mediums != null)
                    importedMediums.AddRange(importData.Mediums);

                Verbose($"Imported:    {import.Path} " +
                        $"({importData.Materials?.Count ?? 0} materials, " +
                        $"{importData.Entities?.Count ?? 0} entities, " +
                        $"{importData.Lights?.Count ?? 0} lights, " +
                        $"{importData.Templates?.Count ?? 0} templates, " +
                        $"{importData.Mediums?.Count ?? 0} mediums)");
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
        if (importedMediums.Count > 0)
        {
            importedMediums.AddRange(data.Mediums ?? new List<MediumData>());
            data.Mediums = importedMediums;
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
        Info($"Cameras:     {list.Count} - using camera 0 (available cameras: {cameraNames})");
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
            // Proxy emissives back a sphere/area light's visible primitive —
            // they're already registered as the parent ILight in the renderer's
            // emitter map, so registering them again as a GeometryLight would
            // double-count NEE on those surfaces.
            case Emissive { IsLightProxy: true }:
                return false;

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
                                specular:            DisneyParam(m.Specular,            m.SpecularTexture,            sceneDir),
                                specularTint:        DisneyParam(m.SpecularTint,        m.SpecularTintTexture,        sceneDir),
                                sheen:               DisneyParam(m.Sheen,               m.SheenTexture,               sceneDir),
                                sheenTint:           DisneyParam(m.SheenTint,           m.SheenTintTexture,           sceneDir),
                                sheenRoughness:      DisneyParam(m.SheenRoughness,      m.SheenRoughnessTexture,      sceneDir),
                                clearcoat:           DisneyParam(m.Clearcoat,           m.ClearcoatTexture,           sceneDir),
                                clearcoatGloss:      DisneyParam(m.ClearcoatGloss,      m.ClearcoatGlossTexture,      sceneDir),
                                specTrans:           DisneyParam(m.SpecTrans,           m.SpecTransTexture,           sceneDir),
                                ior:                 DisneyParam(m.DisneyIor,           m.IorTexture,                 sceneDir),
                                anisotropic:         DisneyParam(m.Anisotropic,         m.AnisotropicTexture,         sceneDir),
                                anisotropicRotation: DisneyParam(m.AnisotropicRotation, m.AnisotropicRotationTexture, sceneDir),
                                transmissionColor:   DisneyColorParam(m.TransmissionColor, m.TransmissionColorTexture, sceneDir),
                                transmissionDepth:   DisneyParam(m.TransmissionDepth,   m.TransmissionDepthTexture,   sceneDir),
                                diffTrans:           DisneyParam(m.DiffTrans,           m.DiffTransTexture,           sceneDir),
                                thinWalled:          m.ThinWalled,
                                coatIor:             DisneyParam(m.CoatIor,             m.CoatIorTexture,             sceneDir),
                                // coat_roughness: only forwarded when the user
                                // explicitly set a non-negative value or a
                                // texture; otherwise null selects the legacy
                                // ClearcoatGloss path inside DisneyBsdf.
                                coatRoughness:       (m.CoatRoughness >= 0f || m.CoatRoughnessTexture != null)
                                                        ? DisneyParam(MathF.Max(m.CoatRoughness, 0f), m.CoatRoughnessTexture, sceneDir)
                                                        : null,
                                thinFilmThickness:   DisneyParam(m.ThinFilmThickness,   m.ThinFilmThicknessTexture, sceneDir),
                                thinFilmIor:         DisneyParam(m.ThinFilmIor,         m.ThinFilmIorTexture,       sceneDir)),
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

        // ── Bump map (optional, applied after normal_map at shade time) ─────
        if (m.BumpMap != null)
        {
            var bumpMap = LoadBumpMap(m.BumpMap, sceneDir);
            if (bumpMap != null)
            {
                switch (material)
                {
                    case Lambertian  lam: lam.BumpMap = bumpMap; break;
                    case Metal       met: met.BumpMap = bumpMap; break;
                    case Dielectric  die: die.BumpMap = bumpMap; break;
                    case Emissive    emi: emi.BumpMap = bumpMap; break;
                    case DisneyBsdf  dis: dis.BumpMap = bumpMap; break;
                    case MixMaterial mix: mix.BumpMap = bumpMap; break;
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

        // ── Legacy "fake SSS" knobs (clean break) ───────────────────────────
        // The Hanrahan-Krueger flat-blend approximation that used to live on
        // the Disney diffuse lobe has been removed in favour of physically-
        // based Random Walk subsurface scattering. Two production paths
        // are supported:
        //   1. entity-level: `interior_medium: <id>` on the entity, using a
        //      medium from the `mediums:` library;
        //   2. material-embedded: `subsurface_radius` on the material itself
        //      (Arnold standard_surface / Cycles Principled BSDF parity) —
        //      the loader auto-builds a HomogeneousMedium per material and
        //      injects it on every entity that references the material and
        //      lacks an explicit interior_medium.
        // The legacy diffuse-approximation 'subsurface' / 'subsurface_color' /
        // 'flatness' parameters remain parsed for backward compatibility but
        // contribute nothing to the random-walk path. Warn only when these
        // are set WITHOUT subsurface_radius, so the artist knows they need to
        // migrate to one of the two real SSS paths.
        if (material is DisneyBsdf
            && m.SubsurfaceRadius == null
            && (m.Subsurface > 0f
                || m.SubsurfaceColor != null
                || m.Flatness > 0f
                || m.SubsurfaceTexture != null
                || m.SubsurfaceColorTexture != null
                || m.FlatnessTexture != null))
        {
            Warn($"Material '{m.Id ?? "(unnamed)"}': legacy 'subsurface' / 'subsurface_color' / " +
                 $"'flatness' fields contribute nothing to the random-walk SSS. Add " +
                 $"'subsurface_radius' to enable material-embedded SSS, or bind " +
                 $"'interior_medium' on the entity. See " +
                 $"docs/technical/subsurface-scattering.md for the migration recipe.");
        }

        // ── Material-level surface displacement (Cycles/RenderMan parity) ───
        // When the YAML carries a 'displacement:' block under the material,
        // build a LeafDisplacement and attach it to the concrete material.
        // Every mesh entity that resolves this material then inherits the
        // displacement automatically.
        var leafDisp = BuildLeafDisplacement(m, sceneDir);
        if (leafDisp != null)
        {
            switch (material)
            {
                case Lambertian lam: lam.Displacement = leafDisp; break;
                case Metal      met: met.Displacement = leafDisp; break;
                case Dielectric die: die.Displacement = leafDisp; break;
                case Emissive   emi: emi.Displacement = leafDisp; break;
                case DisneyBsdf dis: dis.Displacement = leafDisp; break;
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

        // ── Bump map (optional) ──────────────────────────────────────────
        if (m.BumpMap != null)
        {
            var bumpMap = LoadBumpMap(m.BumpMap, sceneDir);
            if (bumpMap != null)
            {
                ((MixMaterial)material).BumpMap = bumpMap;
            }
        }

        // ── Mix-level displacement (Cycles "Mix Shader → Displacement") ──
        // The Mix material can opt in to vector-blend its children's per-
        // vertex displacement offsets via the same mask/blend factor that
        // drives the BSDF mix. When the YAML 'displacement: blend_with_mask:
        // true' is set we wrap the two children's displacement in a
        // MixDisplacement; otherwise the Mix carries no displacement of its
        // own even if the children would.
        if (m.Displacement != null && m.Displacement.BlendWithMask)
        {
            var mixMat = (MixMaterial)material;
            var dispA = mixMat.MaterialA.Displacement;
            var dispB = mixMat.MaterialB.Displacement;
            if (dispA == null || dispB == null)
            {
                Warn($"Mix material '{m.Id}': blend_with_mask=true requires " +
                     $"both child materials to have their own displacement. " +
                     $"material_a='{m.MaterialA}' has " +
                     $"{(dispA == null ? "no" : "a")} displacement; " +
                     $"material_b='{m.MaterialB}' has " +
                     $"{(dispB == null ? "no" : "a")} displacement. " +
                     $"Mix-level displacement disabled.");
            }
            else
            {
                mixMat.Displacement = new MixDisplacement(mixMat, dispA, dispB);
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
    /// Builds a <see cref="BumpMapTexture"/> from the YAML block. The inner
    /// height field is any <see cref="ITexture"/> resolved through the same
    /// dispatcher used by materials (procedural or image).
    /// </summary>
    private static BumpMapTexture? LoadBumpMap(BumpMapData bm, string sceneDir)
    {
        if (bm.Texture == null)
        {
            Warn("Bump map requires a 'texture' block. Skipping.");
            return null;
        }
        try
        {
            ITexture inner = CreateTexture(bm.Texture, sceneDir);
            return new BumpMapTexture(inner, bm.Strength, bm.Scale);
        }
        catch (Exception ex)
        {
            Warn($"Failed to build bump map: {ex.Message}. Skipping.");
            return null;
        }
    }

    /// <summary>
    /// Default flat sky color when no <c>world.sky</c> is specified.
    /// Daylight blue, matching the historical default of the now-removed
    /// <c>world.background</c> field.
    /// </summary>
    private static readonly Vector3 DefaultSkyColor = new(0.5f, 0.7f, 1.0f);

    // =========================================================================
    //  Ground dispatcher
    // =========================================================================

    /// <summary>
    /// Builds the <c>world.ground</c> primitive from a <see cref="GroundData"/>
    /// block. Returns <c>null</c> if no usable geometry can be produced (e.g.
    /// a <c>heightfield</c> ground without bounds/heightmap).
    ///
    /// <para>The method dispatches on <see cref="GroundData.Type"/> across the
    /// four supported shapes (<c>infinite_plane</c>/<c>plane</c>, <c>quad</c>,
    /// <c>disk</c>, <c>heightfield</c>/<c>terrain</c>), resolves the material
    /// (either by ID or via the inline <c>color/roughness/metallic</c>
    /// shortcut, or by syncing with <c>sky.ground_albedo</c>/<c>ground_color</c>
    /// when both <c>material</c> and <c>color</c> are absent), applies the
    /// optional UV transform (<c>uv_scale</c>, <c>uv_offset</c>,
    /// <c>uv_rotation</c>) and the per-ray-category visibility flags, then
    /// returns the resulting wrapped <see cref="IHittable"/>.</para>
    /// </summary>
    private static IHittable? BuildGround(GroundData g, SkyData? sky,
                                          Dictionary<string, IMaterial> materials,
                                          string sceneDir)
    {
        // ── 1. Resolve material ────────────────────────────────────────────────
        IMaterial material = ResolveGroundMaterial(g, sky, materials);

        // ── 2. Resolve geometric anchor / normal ──────────────────────────────
        // `point` (full 3D) takes precedence over the `y` shorthand.
        Vector3 anchor = ToVector3(g.Point) ?? new Vector3(0f, g.Y, 0f);
        Vector3 normal = ToVector3(g.Normal) ?? Vector3.UnitY;
        if (normal.LengthSquared() < 1e-12f)
        {
            Warn("Ground 'normal' is the zero vector. Falling back to (0, 1, 0).");
            normal = Vector3.UnitY;
        }
        normal = Vector3.Normalize(normal);

        // ── 3. Dispatch on type ────────────────────────────────────────────────
        string type = (g.Type ?? "infinite_plane").Trim().ToLowerInvariant();
        IHittable? hittable = type switch
        {
            "infinite_plane" or "plane" or ""
                => new InfinitePlane(anchor, normal, material),
            "quad"
                => BuildQuadGround(anchor, normal, g.Size, material),
            "disk"
                => new Disk(anchor, normal, g.Size, material),
            "heightfield" or "terrain" or "height_field"
                => BuildHeightFieldGround(g, material, materials, sceneDir),
            _   => WarnUnknownGroundType(type, anchor, normal, material),
        };

        if (hittable == null)
            return null;

        // ── 4. UV transform (scale / offset / rotation) ───────────────────────
        if (NeedsUvTransform(g))
        {
            float su = g.UvScale  is { Count: >= 1 } us ? us[0]                                 : 1f;
            float sv = g.UvScale  is { Count: >= 2 } vs ? vs[1] : su;
            float ou = g.UvOffset is { Count: >= 1 } uo ? uo[0]                                 : 0f;
            float ov = g.UvOffset is { Count: >= 2 } vo ? vo[1] : 0f;
            hittable = new UvTransformedHittable(hittable, su, sv, ou, ov, g.UvRotation);
        }

        // ── 5. Per-ray-category visibility flags ──────────────────────────────
        var visMask = BuildGroundVisibilityMask(g.Visibility);
        if (visMask != HitVisibilityMask.None)
            hittable = new VisibilityFilteredHittable(hittable, visMask);

        // ── 6. Stable seed for procedural textures ────────────────────────────
        hittable.Seed = StableSeed(0, "ground", g.Type);

        return hittable;
    }

    private static IHittable WarnUnknownGroundType(string type, Vector3 anchor, Vector3 normal, IMaterial material)
    {
        Warn($"Unknown ground type '{type}'. Falling back to 'infinite_plane'. " +
             "Accepted values: 'infinite_plane'/'plane', 'quad', 'disk', 'heightfield'/'terrain'.");
        return new InfinitePlane(anchor, normal, material);
    }

    /// <summary>
    /// Builds a centred <see cref="Quad"/> of half-size <paramref name="size"/>
    /// lying on the plane defined by <paramref name="anchor"/> and
    /// <paramref name="normal"/>. The quad's U axis aligns with world +X when
    /// the normal is close to +Y; otherwise it picks a stable orthonormal
    /// basis from the normal.
    /// </summary>
    private static IHittable BuildQuadGround(Vector3 anchor, Vector3 normal, float size, IMaterial material)
    {
        if (size <= 0f) size = 50f;
        // Build a stable orthonormal basis around the normal. For the standard
        // Y-up case this matches the world axes (U=+X, V=+Z) so unrotated
        // textures look the same as on the infinite plane.
        Vector3 u, v;
        if (MathF.Abs(normal.Y) > 0.999f)
        {
            u = Vector3.UnitX;
            v = Vector3.UnitZ * MathF.Sign(normal.Y); // preserve face orientation
        }
        else
        {
            u = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
            v = Vector3.Normalize(Vector3.Cross(normal, u));
        }
        // Quad's q is the bottom-left corner, U/V span the two sides.
        Vector3 q = anchor - u * size - v * size;
        return new Quad(q, u * (2f * size), v * (2f * size), material);
    }

    /// <summary>
    /// Builds a heightfield primitive from a <see cref="GroundData"/> block.
    /// Mirrors <see cref="CreateHeightFieldEntity"/> with the GroundData
    /// vocabulary and falls back to <c>null</c> with a warning when the
    /// required fields (<c>bounds</c> + one of <c>heightmap_path</c>/
    /// <c>height_texture</c>) are missing.
    /// </summary>
    private static IHittable? BuildHeightFieldGround(GroundData g, IMaterial material,
                                                     Dictionary<string, IMaterial> materials,
                                                     string sceneDir)
    {
        if (g.Bounds == null || g.Bounds.Count < 4)
        {
            Warn("Ground type='heightfield' requires 'bounds: [xMin, zMin, xMax, zMax]'. Skipping.");
            return null;
        }
        float xMin = g.Bounds[0], zMin = g.Bounds[1], xMax = g.Bounds[2], zMax = g.Bounds[3];
        if (xMax <= xMin || zMax <= zMin)
        {
            Warn("Ground heightfield 'bounds' must satisfy xMax>xMin and zMax>zMin. Skipping.");
            return null;
        }
        if (g.HeightScale <= 0f)
        {
            Warn("Ground heightfield 'height_scale' must be > 0. Skipping.");
            return null;
        }

        IMaterial? seaMat = null;
        if (!string.IsNullOrWhiteSpace(g.SeaMaterial))
            seaMat = GetMaterial(materials, g.SeaMaterial);

        List<HeightField.StratumBand>? strata = null;
        if (g.Strata != null && g.Strata.Count > 0)
        {
            strata = new List<HeightField.StratumBand>(g.Strata.Count);
            foreach (var s in g.Strata)
            {
                if (string.IsNullOrWhiteSpace(s.Material))
                {
                    Warn("Ground heightfield: stratum without 'material' skipped.");
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

        if (!string.IsNullOrWhiteSpace(g.HeightmapPath))
        {
            if (g.HeightTexture != null)
                Warn("Ground heightfield has both 'heightmap_path' and 'height_texture' — using the baked path.");

            string fullPath = Path.IsPathRooted(g.HeightmapPath)
                ? g.HeightmapPath
                : Path.Combine(sceneDir, g.HeightmapPath);
            if (!File.Exists(fullPath))
            {
                Warn($"Ground heightfield: heightmap file not found at '{fullPath}'. Skipping.");
                return null;
            }
            if (!HeightmapLoader.IsHighPrecision(fullPath))
                Warn($"Ground heightfield: '{Path.GetFileName(fullPath)}' is not 16-bit — terracing may appear on smooth slopes.");

            float[] samples;
            int sx, sz;
            try
            {
                samples = HeightmapLoader.Load(fullPath, out sx, out sz);
            }
            catch (Exception ex)
            {
                Warn($"Ground heightfield: failed to load heightmap '{fullPath}': {ex.Message}. Skipping.");
                return null;
            }
            return new HeightField(xMin, zMin, xMax, zMax,
                                   samples, sx, sz,
                                   g.HeightScale, material,
                                   g.SeaLevel, seaMat, strata);
        }
        if (g.HeightTexture != null)
        {
            ITexture tex = CreateTexture(g.HeightTexture, sceneDir);
            int res = g.Resolution > 0 ? g.Resolution : 512;
            return HeightField.FromProceduralTexture(
                xMin, zMin, xMax, zMax,
                tex, res,
                g.HeightScale, material,
                g.SeaLevel, seaMat, strata);
        }
        Warn("Ground heightfield needs either 'heightmap_path' or 'height_texture'. Skipping.");
        return null;
    }

    /// <summary>
    /// Resolves the surface material for the ground in three priority bands:
    /// (1) <c>material:</c> ID lookup (legacy), (2) inline shorthand
    /// <c>color/roughness/metallic</c> → anonymous Disney BSDF,
    /// (3) automatic albedo sync with <c>sky.ground_albedo</c> /
    /// <c>sky.ground_color</c> when both are absent — matching how
    /// <c>aiSkyDomeLight</c> seeds the floor in Arnold preview renders. Final
    /// fallback is a neutral grey Lambertian.
    /// </summary>
    private static IMaterial ResolveGroundMaterial(GroundData g, SkyData? sky,
                                                   Dictionary<string, IMaterial> materials)
    {
        if (!string.IsNullOrWhiteSpace(g.Material))
        {
            if (!materials.TryGetValue(g.Material, out var byId))
            {
                Warn($"Ground material '{g.Material}' not found. Using default grey Lambertian.");
                return new Lambertian(new Vector3(0.5f, 0.5f, 0.5f));
            }
            return byId;
        }

        if (g.Color != null || g.Roughness.HasValue || g.Metallic.HasValue)
        {
            Vector3 baseColor = ToVector3(g.Color) ?? new Vector3(0.5f, 0.5f, 0.5f);
            float roughness   = g.Roughness ?? 0.8f;
            float metallic    = g.Metallic  ?? 0f;
            // Anonymous Disney BSDF — keeps physical correctness even when the
            // user only supplied a colour, matching Arnold's <c>standard_surface</c>
            // floor default.
            return new DisneyBsdf(
                baseColor:  new SolidColor(baseColor),
                metallic:   new FloatTexture(metallic),
                roughness:  new FloatTexture(roughness));
        }

        // Auto-sync with sky.ground_albedo / sky.ground_color when nothing
        // else was specified. Mirrors the Arnold/Mitsuba "physical floor"
        // preview that uses the sky's ground term as a default albedo, so
        // pure-sky scenes with no material still get a coherent floor colour.
        Vector3? skyAlbedo = ToVector3(sky?.GroundAlbedo) ?? ToVector3(sky?.GroundColor);
        if (skyAlbedo.HasValue)
        {
            return new Lambertian(skyAlbedo.Value);
        }

        return new Lambertian(new Vector3(0.5f, 0.5f, 0.5f));
    }

    private static bool NeedsUvTransform(GroundData g)
    {
        if (g.UvScale  != null && g.UvScale.Count  > 0) return true;
        if (g.UvOffset != null && g.UvOffset.Count > 0) return true;
        if (MathF.Abs(g.UvRotation) > 1e-6f) return true;
        return false;
    }

    private static HitVisibilityMask BuildGroundVisibilityMask(GroundVisibilityData? v)
    {
        if (v == null) return HitVisibilityMask.None;
        HitVisibilityMask mask = HitVisibilityMask.None;
        if (!v.Camera)       mask |= HitVisibilityMask.Camera;
        if (!v.Diffuse)      mask |= HitVisibilityMask.Diffuse;
        if (!v.Glossy)       mask |= HitVisibilityMask.Glossy;
        if (!v.Transmission) mask |= HitVisibilityMask.Transmission;
        if (!v.Shadow)       mask |= HitVisibilityMask.Shadow;
        return mask;
    }

    /// <summary>
    /// Builds a <see cref="SkySettings"/> from the YAML sky section.
    /// Supported modes: <c>flat</c>, <c>gradient</c>, <c>hdri</c>,
    /// <c>preetham</c>/<c>hosek_wilkie</c> (analytical physical sky). When the
    /// <c>world.sky</c> block is missing or its <c>type</c> is unrecognised,
    /// falls back to a flat sky using <see cref="DefaultSkyColor"/>.
    /// </summary>
    private static SkySettings BuildSkySettings(SkyData? skyData, string sceneDir)
    {
        if (skyData == null)
            return new SkySettings(DefaultSkyColor);

        string skyType = skyData.Type?.ToLowerInvariant() ?? "";

        ISkyModel lightingModel = BuildSkyModel(skyType, skyData, sceneDir);
        ISkyModel? backgroundModel = null;
        if (skyData.Background != null)
        {
            string bgType = skyData.Background.Type?.ToLowerInvariant() ?? "";
            backgroundModel = BuildSkyModel(bgType, skyData.Background, sceneDir);
        }

        var visibility = BuildVisibility(skyData);
        var orientation = BuildOrientation(skyData);

        var sky = new SkySettings(lightingModel, backgroundModel, visibility, orientation);

        // Track Mode hint for legacy logging \u2014 best-effort.
        return sky;
    }

    private static ISkyModel BuildSkyModel(string skyType, SkyData skyData, string sceneDir)
    {
        return skyType switch
        {
            "flat" or "" => new FlatSky(ToVector3(skyData.Color) ?? DefaultSkyColor),
            "gradient"   => BuildGradientSkyModel(skyData),
            "hdri"       => BuildHdriSkyModel(skyData, sceneDir),
            "preetham" or "hosek_wilkie" or "physical"
                         => BuildPhysicalSkyModel(skyData),
            "nishita"    => BuildNishitaSkyModel(skyData),
            _            => WarnUnknownSkyTypeModel(skyType),
        };
    }

    private static SkyVisibility BuildVisibility(SkyData skyData)
    {
        var v = skyData.Visibility;
        bool sunCam = skyData.Sun?.VisibleToCamera ?? true;
        if (v == null) return new SkyVisibility { SunCamera = sunCam };
        return new SkyVisibility
        {
            Camera = v.Camera,
            Diffuse = v.Diffuse,
            Glossy = v.Glossy,
            Transmission = v.Transmission,
            Shadow = v.Shadow,
            SunCamera = sunCam,
        };
    }

    private static System.Numerics.Quaternion BuildOrientation(SkyData skyData)
    {
        // Quaternion takes precedence when both are set.
        if (skyData.Orientation?.Quaternion is { Count: 4 } q)
            return new System.Numerics.Quaternion(q[0], q[1], q[2], q[3]);
        if (skyData.Orientation?.Euler is { Count: 3 } e)
        {
            float rx = MathUtils.DegreesToRadians(e[0]);
            float ry = MathUtils.DegreesToRadians(e[1]);
            float rz = MathUtils.DegreesToRadians(e[2]);
            return System.Numerics.Quaternion.CreateFromYawPitchRoll(ry, rx, rz);
        }
        // Legacy `rotation` field \u2014 Y-axis only.
        if (MathF.Abs(skyData.Rotation) > 1e-6f)
        {
            return System.Numerics.Quaternion.CreateFromAxisAngle(
                Vector3.UnitY, MathUtils.DegreesToRadians(skyData.Rotation));
        }
        return System.Numerics.Quaternion.Identity;
    }

    private static ISkyModel WarnUnknownSkyTypeModel(string skyType)
    {
        Warn($"Unknown sky type '{skyType}'. Falling back to flat default.");
        return new FlatSky(DefaultSkyColor);
    }

    private static GradientSky BuildGradientSkyModel(SkyData skyData)
    {
        var zenith  = ToVector3(skyData.ZenithColor)  ?? new Vector3(0.10f, 0.30f, 0.80f);
        var horizon = ToVector3(skyData.HorizonColor) ?? new Vector3(0.70f, 0.85f, 1.00f);
        var ground  = ToVector3(skyData.GroundColor)  ?? new Vector3(0.30f, 0.25f, 0.20f);

        if (skyData.Sun == null)
            return new GradientSky(zenith, horizon, ground);

        var sunDir = ToVector3(skyData.Sun.Direction);
        if (sunDir == null)
            return new GradientSky(zenith, horizon, ground);

        var sunColor = ToVector3(skyData.Sun.Color) ?? Vector3.One;
        float halfAngle = skyData.Sun.AngularRadius > 0f
            ? skyData.Sun.AngularRadius
            : MathF.Max(0.01f, skyData.Sun.Size * 0.5f);
        return new GradientSky(zenith, horizon, ground,
            sunDirToSun: sunDir,
            sunRadiance: sunColor * skyData.Sun.Intensity,
            sunHalfAngleDeg: halfAngle);
    }

    private static NishitaSky BuildNishitaSkyModel(SkyData skyData)
    {
        Vector3 dirToSun = skyData.Sun?.Direction is { Count: >= 3 }
            ? Vector3.Normalize(ToVector3(skyData.Sun.Direction) ?? Vector3.UnitY)
            : Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f));
        float intensity = skyData.Intensity > 0f ? skyData.Intensity : 1f;
        // Reuse turbidity as a coarse haze knob: 1 → clean (dust=0.3), 10 → smoggy (dust=2.0).
        float dust = MathF.Max(0.1f, (skyData.Turbidity - 1f) * 0.2f + 0.5f);
        return new NishitaSky(dirToSun, airDensity: 1f, dustDensity: dust, intensity: intensity);
    }

    private static PreethamSky BuildPhysicalSkyModel(SkyData skyData)
    {
        Vector3 dirToSun;
        if (skyData.Sun?.Direction is { Count: >= 3 })
        {
            dirToSun = Vector3.Normalize(ToVector3(skyData.Sun.Direction) ?? Vector3.UnitY);
        }
        else
        {
            // Default: noon-ish sun pointing slightly south of overhead.
            dirToSun = Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f));
        }
        var albedo = ToVector3(skyData.GroundAlbedo) ?? new Vector3(0.3f);
        float intensity = skyData.Intensity > 0f ? skyData.Intensity : 1f;
        return new PreethamSky(dirToSun, skyData.Turbidity, albedo, intensity);
    }

    private static ISkyModel BuildHdriSkyModel(SkyData skyData, string sceneDir)
    {
        if (string.IsNullOrWhiteSpace(skyData.Path))
        {
            Warn("HDRI sky requires a 'path' field. Falling back to flat gray.");
            return new FlatSky(new Vector3(0.5f));
        }

        string hdrPath = Path.IsPathRooted(skyData.Path)
            ? skyData.Path
            : Path.Combine(sceneDir, skyData.Path);

        if (!File.Exists(hdrPath))
        {
            Warn($"HDRI file not found: {hdrPath}. Falling back to flat magenta.");
            return new FlatSky(new Vector3(1f, 0f, 1f));
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string ext = Path.GetExtension(hdrPath).ToLowerInvariant();
            (float[] pixels, int width, int height) = ext switch
            {
                ".exr" => ExrLoader.Load(hdrPath),
                _      => HdrLoader.Load(hdrPath),
            };

            EnvironmentMap envMap;
            Vector3? extractedSunDir = null;
            Vector3? extractedSunRad = null;
            float extractedHalfDeg = 0.265f;

            if (skyData.Sun?.ExtractFromHdri == true)
            {
                var probeMap = new EnvironmentMap(pixels, width, height,
                                                  skyData.Intensity, skyData.Rotation);
                var ext2 = HdriSunExtractor.Extract(probeMap, skyData.Sun.ExtractThreshold);
                if (ext2 != null)
                {
                    extractedSunDir = ext2.Direction;
                    extractedSunRad = ext2.Radiance;
                    extractedHalfDeg = ext2.HalfAngleDeg;
                    envMap = new EnvironmentMap(ext2.InpaintedPixels, width, height,
                                                skyData.Intensity, skyData.Rotation);
                    Info($"HDRI sun extracted: half-angle {extractedHalfDeg:F2}\u00b0");
                }
                else
                {
                    Info("HDRI sun extraction: no peak above threshold; using HDRI as-is.");
                    envMap = probeMap;
                }
            }
            else
            {
                envMap = new EnvironmentMap(pixels, width, height,
                                            skyData.Intensity, skyData.Rotation);
            }

            Info($"HDRI:        {skyData.Path} ({width}\u00d7{height})");
            Verbose($"HDRI load:   {sw.ElapsedMilliseconds} ms");
            return new HdriSky(envMap, extractedSunDir, extractedSunRad, extractedHalfDeg);
        }
        catch (Exception ex)
        {
            Warn($"Failed to load HDRI '{hdrPath}': {ex.Message}. Falling back to flat magenta.");
            return new FlatSky(new Vector3(1f, 0f, 1f));
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

            "noise" => new NoiseTexture(t.Scale,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.Zero : Vector3.Zero,
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.One  : Vector3.One),

            "marble" => new MarbleTexture(t.Scale,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.9f) : new Vector3(0.9f),
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.1f) : new Vector3(0.1f)),

            "wood" => new WoodTexture(t.Scale, t.GrainStrength ?? t.NoiseStrength ?? 1.5f,
                t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.85f, 0.65f, 0.40f) : new Vector3(0.85f, 0.65f, 0.40f),
                t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.45f, 0.28f, 0.14f) : new Vector3(0.45f, 0.28f, 0.14f)),

            "voronoi" or "worley" or "cell" or "cellular"
                => new VoronoiTexture(t.Scale,
                    t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.Zero : Vector3.Zero,
                    t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.One  : Vector3.One),

            "brick" or "bricks" or "tile"
                => new BrickTexture(
                    t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? new Vector3(0.65f, 0.27f, 0.20f) : new Vector3(0.65f, 0.27f, 0.20f),
                    t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? new Vector3(0.55f, 0.20f, 0.15f) : new Vector3(0.55f, 0.20f, 0.15f),
                    t.Colors is { Count: > 2 } ? ToVector3(t.Colors[2]) ?? new Vector3(0.85f, 0.83f, 0.78f) : new Vector3(0.85f, 0.83f, 0.78f)),

            "gradient" or "ramp"
                => new GradientTexture(
                    t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.Zero : Vector3.Zero,
                    t.Colors is { Count: > 1 } ? ToVector3(t.Colors[1]) ?? Vector3.One  : Vector3.One),

            "coordinate" or "coord" or "coords" or "texture_coord" or "tex_coord" or "st"
                => new CoordinateTexture(),

            "image" => CreateImageTexture(t, sceneDir),

            _ => new SolidColor(t.Colors is { Count: > 0 } ? ToVector3(t.Colors[0]) ?? Vector3.One : Vector3.One)
        };

        ApplyTextureParams(tex, t, sceneDir);
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
    private static void ApplyTextureParams(ITexture tex, TextureData t, string sceneDir)
    {
        if (tex is NoiseTexture nt)
        {
            if (t.Offset   != null) nt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) nt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            nt.RandomizeOffset   = t.RandomizeOffset;
            nt.RandomizeRotation = t.RandomizeRotation;
            if (t.NoiseStrength.HasValue) nt.NoiseStrength = t.NoiseStrength.Value;
            if (t.NoiseTypeName != null) nt.NoiseType = ParseNoiseKind(t.NoiseTypeName);
            if (t.Octaves.HasValue)      nt.Octaves    = Math.Clamp(t.Octaves.Value, 1, 16);
            if (t.Lacunarity.HasValue)   nt.Lacunarity = t.Lacunarity.Value;
            if (t.Gain.HasValue)         nt.Gain       = t.Gain.Value;
            if (t.Distortion.HasValue)   nt.Distortion = t.Distortion.Value;
            if (t.FractalIncrement.HasValue) nt.FractalIncrement = t.FractalIncrement.Value;
            if (t.FractalOffset.HasValue)    nt.FractalOffset    = t.FractalOffset.Value;
            nt.ColorRamp = BuildColorRamp(t.ColorRamp, "noise");
        }
        else if (tex is MarbleTexture mt)
        {
            if (t.Offset   != null) mt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) mt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            mt.RandomizeOffset   = t.RandomizeOffset;
            mt.RandomizeRotation = t.RandomizeRotation;

            if (t.NoiseStrength.HasValue) mt.NoiseStrength = t.NoiseStrength.Value;
            if (t.VeinAxis != null)       mt.VeinAxis      = ToVector3(t.VeinAxis) ?? Vector3.UnitY;

            // Recursive IQ warp
            if (t.WarpAmplitude.HasValue)  mt.WarpAmplitude  = MathF.Max(t.WarpAmplitude.Value, 0f);
            if (t.WarpScale.HasValue)      mt.WarpScale      = MathF.Max(t.WarpScale.Value, 1e-4f);
            if (t.WarpIterations.HasValue) mt.WarpIterations = Math.Clamp(t.WarpIterations.Value, 0, 3);

            // Anisotropic geological fold
            if (t.FoldAmplitude != null)   mt.FoldAmplitude = ToVector3(t.FoldAmplitude) ?? mt.FoldAmplitude;
            if (t.FoldScale.HasValue)      mt.FoldScale     = MathF.Max(t.FoldScale.Value, 1e-4f);

            // Multi-scale ridged vein layers
            if (t.VeinLayers.HasValue)     mt.VeinLayers     = Math.Clamp(t.VeinLayers.Value, 1, 3);
            if (t.VeinScale  is { } vsc)   mt.VeinScales     = vsc.ToArray();
            if (t.VeinWeight is { } vwt)   mt.VeinWeights    = vwt.ToArray();
            ValidateMarbleLayers(mt);
            if (t.Octaves.HasValue)        mt.Octaves        = Math.Clamp(t.Octaves.Value, 1, 16);
            if (t.Lacunarity.HasValue)     mt.Lacunarity     = t.Lacunarity.Value;
            if (t.Gain.HasValue)           mt.Gain           = t.Gain.Value;
            if (t.SoftMaxSharpness.HasValue) mt.SoftMaxSharpness = MathF.Max(t.SoftMaxSharpness.Value, 0.1f);

            // Vein thickness remap
            if (t.VeinThickness.HasValue)  mt.VeinThickness  = Math.Clamp(t.VeinThickness.Value, 0f, 1f);
            if (t.VeinSoftness.HasValue)   mt.VeinSoftness   = Math.Clamp(t.VeinSoftness.Value, 1e-4f, 1f);

            // Background tonal variation
            if (t.BackgroundScale.HasValue)   mt.BackgroundScale   = MathF.Max(t.BackgroundScale.Value, 1e-4f);
            if (t.BackgroundOctaves.HasValue) mt.BackgroundOctaves = Math.Clamp(t.BackgroundOctaves.Value, 1, 16);
            if (t.ColorVariation.HasValue)    mt.ColorVariation    = Math.Clamp(t.ColorVariation.Value, 0f, 1f);

            // Impurities
            if (t.ImpuritiesDensity.HasValue) mt.ImpuritiesDensity = Math.Clamp(t.ImpuritiesDensity.Value, 0f, 1f);
            if (t.ImpuritiesScale.HasValue)   mt.ImpuritiesScale   = MathF.Max(t.ImpuritiesScale.Value, 1e-4f);
            if (t.ImpurityWeight.HasValue)    mt.ImpurityWeight    = t.ImpurityWeight.Value;
            if (t.ImpuritiesTexture is not null)
                mt.ImpuritiesTexture = CreateTexture(t.ImpuritiesTexture, sceneDir);

            // Anisotropic space stretch (geological compression)
            if (t.SpaceStretch != null)
                mt.SpaceStretch = ToVector3(t.SpaceStretch) ?? Vector3.One;

            // Secondary linear cracks (Worley F2 − F1 overlay)
            if (t.CracksDensity.HasValue)  mt.CracksDensity  = Math.Clamp(t.CracksDensity.Value, 0f, 1f);
            if (t.CracksScale.HasValue)    mt.CracksScale    = MathF.Max(t.CracksScale.Value, 1e-4f);
            if (t.CracksSoftness.HasValue) mt.CracksSoftness = Math.Clamp(t.CracksSoftness.Value, 1e-4f, 1f);
            if (t.CracksWeight.HasValue)   mt.CracksWeight   = MathF.Max(t.CracksWeight.Value, 0f);

            // Output mode: `color` (default) or `mask` — packs vein scalar t as
            // (t,t,t) for FloatTexture-driven Disney parameters (roughness,
            // subsurface, sheen, etc.).
            if (t.Output != null) mt.Output = ParseMarbleOutput(t.Output);

            mt.ColorRamp = BuildColorRamp(t.ColorRamp, "marble");
        }
        else if (tex is WoodTexture wt)
        {
            if (t.Offset   != null) wt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) wt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            wt.RandomizeOffset   = t.RandomizeOffset;
            wt.RandomizeRotation = t.RandomizeRotation;

            // Grain amplitude — `grain_strength` is the canonical name; the
            // legacy `noise_strength` is still accepted (last-write-wins).
            if (t.NoiseStrength.HasValue)    wt.GrainStrength    = t.NoiseStrength.Value;
            if (t.GrainStrength.HasValue)    wt.GrainStrength    = t.GrainStrength.Value;

            // Geometry / ring layout
            if (t.RingAxis != null)          wt.RingAxis         = ToVector3(t.RingAxis) ?? Vector3.UnitY;
            if (t.SpaceStretch != null)      wt.SpaceStretch     = ToVector3(t.SpaceStretch) ?? Vector3.One;

            // Asymmetric ring profile
            if (t.RingSharpness.HasValue)    wt.RingSharpness    = MathF.Max(t.RingSharpness.Value, 0.05f);
            if (t.LatewoodWidth.HasValue)    wt.LatewoodWidth    = Math.Clamp(t.LatewoodWidth.Value, 0.02f, 0.95f);
            if (t.EarlywoodTransition.HasValue) wt.EarlywoodTransition = Math.Clamp(t.EarlywoodTransition.Value, 0.005f, 0.5f);

            // Per-ring variation
            if (t.RingColorVariation.HasValue) wt.RingColorVariation = Math.Clamp(t.RingColorVariation.Value, 0f, 1f);
            if (t.RingWidthVariation.HasValue) wt.RingWidthVariation = Math.Clamp(t.RingWidthVariation.Value, 0f, 0.5f);

            // Multi-band noise
            if (t.AxialGrain.HasValue)       wt.AxialGrain       = t.AxialGrain.Value;
            if (t.Octaves.HasValue)          wt.Octaves          = Math.Clamp(t.Octaves.Value, 1, 16);
            if (t.Lacunarity.HasValue)       wt.Lacunarity       = t.Lacunarity.Value;
            if (t.Gain.HasValue)             wt.Gain             = t.Gain.Value;
            if (t.GrainScale.HasValue)       wt.GrainScale       = MathF.Max(t.GrainScale.Value, 0f);
            if (t.FigureScale.HasValue)      wt.FigureScale      = MathF.Max(t.FigureScale.Value, 0f);
            if (t.FigureStrength.HasValue)   wt.FigureStrength   = MathF.Max(t.FigureStrength.Value, 0f);
            if (t.FigureAspect.HasValue)     wt.FigureAspect     = MathF.Max(t.FigureAspect.Value, 1e-3f);

            // Domain warp (recursive IQ + anisotropic fold). The legacy
            // `distortion` knob is mapped to `warp_amplitude` so older scenes
            // that still set it keep getting a non-trivial warp.
            if (t.Distortion.HasValue)       wt.WarpAmplitude    = MathF.Max(t.Distortion.Value, 0f);
            if (t.WarpAmplitude.HasValue)    wt.WarpAmplitude    = MathF.Max(t.WarpAmplitude.Value, 0f);
            if (t.WarpScale.HasValue)        wt.WarpScale        = MathF.Max(t.WarpScale.Value, 1e-4f);
            if (t.WarpIterations.HasValue)   wt.WarpIterations   = Math.Clamp(t.WarpIterations.Value, 0, 3);
            if (t.FoldAmplitude != null)     wt.FoldAmplitude    = ToVector3(t.FoldAmplitude) ?? wt.FoldAmplitude;
            if (t.FoldScale.HasValue)        wt.FoldScale        = MathF.Max(t.FoldScale.Value, 1e-4f);

            // Global drama dial
            if (t.NoiseStrength.HasValue && t.GrainStrength.HasValue)
            {
                // Already consumed above — no-op here, just for clarity.
            }

            // Radial anisotropy
            if (t.RadialAnisotropy.HasValue) wt.RadialAnisotropy = MathF.Max(t.RadialAnisotropy.Value, 0f);

            // Knots
            if (t.KnotDensity.HasValue)      wt.KnotDensity      = Math.Clamp(t.KnotDensity.Value, 0f, 1f);
            if (t.KnotScale.HasValue)        wt.KnotScale        = MathF.Max(t.KnotScale.Value, 1e-3f);

            // Open-pore vessels
            if (t.PoreDensity.HasValue)      wt.PoreDensity      = Math.Clamp(t.PoreDensity.Value, 0f, 1f);
            if (t.PoreScale.HasValue)        wt.PoreScale        = MathF.Max(t.PoreScale.Value, 1e-4f);
            if (t.PoreStrength.HasValue)     wt.PoreStrength     = Math.Clamp(t.PoreStrength.Value, 0f, 1f);
            if (t.PoreAspect.HasValue)       wt.PoreAspect       = MathF.Max(t.PoreAspect.Value, 1e-3f);

            // Sapwood/heartwood gradient
            if (t.HeartwoodRadius.HasValue)  wt.HeartwoodRadius  = MathF.Max(t.HeartwoodRadius.Value, 0f);
            if (t.HeartwoodBlend.HasValue)   wt.HeartwoodBlend   = Math.Clamp(t.HeartwoodBlend.Value, -1f, 1f);

            // Output mode — `color` (default) or `mask` for FloatTexture-driven
            // Disney parameters (roughness, sheen, subsurface, ...).
            if (t.Output != null) wt.Output = ParseWoodOutput(t.Output);

            wt.ColorRamp = BuildColorRamp(t.ColorRamp, "wood");
        }
        else if (tex is VoronoiTexture vt)
        {
            if (t.Offset   != null) vt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) vt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            vt.RandomizeOffset   = t.RandomizeOffset;
            vt.RandomizeRotation = t.RandomizeRotation;
            if (t.Metric != null)      vt.Metric     = ParseVoronoiMetric(t.Metric);
            if (t.Output != null)      vt.Output     = ParseVoronoiOutput(t.Output);
            if (t.Randomness.HasValue) vt.Randomness = Math.Clamp(t.Randomness.Value, 0f, 1f);
            if (t.Distortion.HasValue) vt.Distortion = t.Distortion.Value;
            if (t.Smoothness.HasValue) vt.Smoothness = Math.Clamp(t.Smoothness.Value, 0f, 1f);
            vt.ColorRamp = BuildColorRamp(t.ColorRamp, "voronoi");
        }
        else if (tex is BrickTexture bt)
        {
            if (t.Offset   != null) bt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) bt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            bt.RandomizeOffset   = t.RandomizeOffset;
            bt.RandomizeRotation = t.RandomizeRotation;
            if (t.BrickWidth.HasValue)     bt.BrickWidth     = t.BrickWidth.Value;
            if (t.BrickHeight.HasValue)    bt.BrickHeight    = t.BrickHeight.Value;
            if (t.MortarSize.HasValue)     bt.MortarSize     = t.MortarSize.Value;
            if (t.RowOffset.HasValue)      bt.RowOffset      = t.RowOffset.Value;
            if (t.ColorVariation.HasValue) bt.ColorVariation = Math.Clamp(t.ColorVariation.Value, 0f, 1f);
            if (t.NoiseScale.HasValue)     bt.NoiseScale     = t.NoiseScale.Value;
        }
        else if (tex is GradientTexture gt)
        {
            if (t.Offset   != null) gt.Offset   = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null) gt.Rotation = ToVector3(t.Rotation) ?? Vector3.Zero;
            gt.RandomizeOffset   = t.RandomizeOffset;
            gt.RandomizeRotation = t.RandomizeRotation;
            if (t.Mode != null)   gt.Mode   = ParseGradientMode(t.Mode);
            if (t.Axis != null)   gt.Axis   = ToVector3(t.Axis) ?? Vector3.UnitX;
            if (t.Length.HasValue) gt.Length = t.Length.Value;
            gt.ColorRamp = BuildColorRamp(t.ColorRamp, "gradient");
        }
        else if (tex is CoordinateTexture ct)
        {
            // CoordinateTexture: per-mode knobs. Defaults are already set on
            // the texture instance; only override when YAML supplies a value.
            if (t.Mode != null)         ct.Mode      = ParseCoordinateMode(t.Mode);
            // `scale` is the inherited TextureData.Scale field (defaults to
            // 1f in TextureData, matching CoordinateTexture's own default).
            ct.Scale = t.Scale;
            if (t.Offset   != null)     ct.Offset    = ToVector3(t.Offset)   ?? Vector3.Zero;
            if (t.Rotation != null)     ct.Rotation  = ToVector3(t.Rotation) ?? Vector3.Zero;
            if (t.BoundsMin != null)    ct.BoundsMin = ToVector3(t.BoundsMin) ?? new Vector3(-1f);
            if (t.BoundsMax != null)    ct.BoundsMax = ToVector3(t.BoundsMax) ?? new Vector3( 1f);
        }
    }

    private static NoiseTexture.NoiseKind ParseNoiseKind(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "perlin"               => NoiseTexture.NoiseKind.Perlin,
            "fbm" or "fractal"     => NoiseTexture.NoiseKind.Fbm,
            "turbulence" or "turb" => NoiseTexture.NoiseKind.Turbulence,
            "ridged" or "ridge"    => NoiseTexture.NoiseKind.Ridged,
            "billow" or "billowed" => NoiseTexture.NoiseKind.Billow,
            "hetero_terrain" or "heteroterrain" or "hetero" or "heterogeneous"
                                   => NoiseTexture.NoiseKind.HeteroTerrain,
            "hybrid_multifractal" or "hybridmultifractal" or "hybrid" or "multifractal"
                                   => NoiseTexture.NoiseKind.HybridMultifractal,
            _                      => NoiseTexture.NoiseKind.Auto,
        };

    /// <summary>
    /// Validates that <see cref="MarbleTexture.VeinScales"/> and
    /// <see cref="MarbleTexture.VeinWeights"/> are coherent with
    /// <see cref="MarbleTexture.VeinLayers"/>. Mismatched lengths are
    /// reported through the deferred warning channel and the arrays are
    /// trimmed/extended to <c>VeinLayers</c> with sane defaults so the
    /// render still produces a valid image.
    /// </summary>
    private static void ValidateMarbleLayers(MarbleTexture mt)
    {
        int n = mt.VeinLayers;
        if (mt.VeinScales.Length != n)
        {
            if (mt.VeinScales.Length != 0)
                Warn($"Marble texture: vein_scale has {mt.VeinScales.Length} entries but vein_layers={n}. Padding/trimming with default 1.0.");
            mt.VeinScales = ResizeWith(mt.VeinScales, n, 1f);
        }
        if (mt.VeinWeights.Length != n)
        {
            if (mt.VeinWeights.Length != 0)
                Warn($"Marble texture: vein_weight has {mt.VeinWeights.Length} entries but vein_layers={n}. Padding/trimming with default 1.0.");
            mt.VeinWeights = ResizeWith(mt.VeinWeights, n, 1f);
        }
    }

    private static MarbleTexture.OutputMode ParseMarbleOutput(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "mask" or "scalar" or "vein_mask" or "veinmask" => MarbleTexture.OutputMode.Mask,
            _                                                => MarbleTexture.OutputMode.Color,
        };

    private static WoodTexture.OutputMode ParseWoodOutput(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "mask" or "scalar" or "latewood_mask" or "ringmask" => WoodTexture.OutputMode.Mask,
            _                                                    => WoodTexture.OutputMode.Color,
        };

    private static float[] ResizeWith(float[] src, int n, float fill)
    {
        if (src.Length == n) return src;
        var dst = new float[n];
        int copy = Math.Min(src.Length, n);
        Array.Copy(src, dst, copy);
        for (int i = copy; i < n; i++) dst[i] = fill;
        return dst;
    }

    private static WorleyNoise.Metric ParseVoronoiMetric(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "manhattan" or "l1" => WorleyNoise.Metric.Manhattan,
            "chebyshev" or "linf" or "chess" => WorleyNoise.Metric.Chebyshev,
            "euclidean_squared" or "euclidean2" or "sq" => WorleyNoise.Metric.EuclideanSquared,
            _ => WorleyNoise.Metric.Euclidean,
        };

    private static VoronoiTexture.OutputMode ParseVoronoiOutput(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "f1"            => VoronoiTexture.OutputMode.F1,
            "f2"            => VoronoiTexture.OutputMode.F2,
            "f3"            => VoronoiTexture.OutputMode.F3,
            "f4"            => VoronoiTexture.OutputMode.F4,
            "f2_minus_f1" or "f2-f1" or "crackle" => VoronoiTexture.OutputMode.F2MinusF1,
            "f3_minus_f1" or "f3-f1" or "wide_crackle" or "border" => VoronoiTexture.OutputMode.F3MinusF1,
            "f1_plus_f2" or "f1+f2"               => VoronoiTexture.OutputMode.F1PlusF2,
            "cell" or "color"                     => VoronoiTexture.OutputMode.Cell,
            "random" or "rnd" or "id" or "scalar_random" or "per_cell" => VoronoiTexture.OutputMode.Random,
            "position" or "feature_position" or "feature" or "p" => VoronoiTexture.OutputMode.Position,
            _ => VoronoiTexture.OutputMode.F1,
        };

    private static GradientTexture.GradientMode ParseGradientMode(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "quadratic" or "quad"               => GradientTexture.GradientMode.Quadratic,
            "easing" or "ease" or "smoothstep"  => GradientTexture.GradientMode.Easing,
            "spherical" or "sphere"             => GradientTexture.GradientMode.Spherical,
            "radial" or "cylinder" or "cylindrical" => GradientTexture.GradientMode.Radial,
            _                                   => GradientTexture.GradientMode.Linear,
        };

    private static CoordinateTexture.CoordMode ParseCoordinateMode(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "uv" or "st" or "parametric"                => CoordinateTexture.CoordMode.UV,
            "generated" or "gen" or "ref" or "pref"     => CoordinateTexture.CoordMode.Generated,
            "world" or "pworld" or "world_space"        => CoordinateTexture.CoordMode.World,
            _                                           => CoordinateTexture.CoordMode.Object,
        };

    private static ColorRamp.Interp ParseRampInterp(string? s) =>
        s?.Trim().ToLowerInvariant() switch
        {
            "smoothstep" or "smooth"         => ColorRamp.Interp.Smoothstep,
            "constant" or "step" or "hold"   => ColorRamp.Interp.Constant,
            "ease" or "easing" or "smoother" => ColorRamp.Interp.Ease,
            _                                => ColorRamp.Interp.Linear,
        };

    /// <summary>
    /// Builds a <see cref="ColorRamp"/> from the YAML <c>color_ramp:</c>
    /// list. Returns <c>null</c> when the list is null/empty so the caller
    /// can fall back to the legacy two-colour lerp. Invalid stops (missing
    /// or short colour triplet) are skipped with a deferred warning; if no
    /// valid stops survive, the result is null.
    /// </summary>
    private static ColorRamp? BuildColorRamp(List<ColorRampStopData>? stops, string textureLabel)
    {
        if (stops is null || stops.Count == 0) return null;

        var built = new List<ColorRamp.Stop>(stops.Count);
        foreach (var s in stops)
        {
            Vector3? col = ToVector3(s.Color);
            if (!col.HasValue)
            {
                Warn($"color_ramp stop on '{textureLabel}' missing 3-component color, skipped.");
                continue;
            }
            built.Add(new ColorRamp.Stop(s.Position, col.Value, ParseRampInterp(s.Interp)));
        }
        if (built.Count == 0)
        {
            Warn($"color_ramp on '{textureLabel}' has no valid stops, falling back to colors:.");
            return null;
        }
        return new ColorRamp(built);
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
            ITexture tex = CreateTexture(e.HeightTexture, sceneDir);
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
    /// Creates a Mesh entity by loading an OBJ file. Resolves the path
    /// relative to the scene YAML directory and forwards subdivision
    /// options (Loop / Catmull-Clark, iterations, screen-space pixel error)
    /// to <see cref="ObjLoader"/>.
    /// </summary>
    private static IHittable? CreateMeshEntity(EntityData e, IMaterial mat, string sceneDir,
        int entityIndex)
    {
        var screen = _currentScreenContext;
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

        // Build the subdivision request. The entity transform is folded
        // into the screen-space context so the adaptive heuristic measures
        // the *world-space* projected edge length of the mesh after
        // translate/rotate/scale, not the unit-cube-local coordinates.
        var subdivision = BuildSubdivisionOptions(e, screen);

        // Resolve the displacement off the material (material-level model \u2014
        // Cycles/RenderMan parity). The entity can suppress it for this
        // single instance via 'displacement_enabled: false'.
        int seed = e.Seed ?? StableSeed(entityIndex, e.Type, e.Name);
        var matDisp = e.DisplacementEnabled ? mat.Displacement : null;

        var warnings = new List<string>();
        var mesh = ObjLoader.Load(objPath, mat, subdivision, matDisp, seed,
            warnings,
            out var appliedScheme, out var appliedIterations,
            out var maxDisplacement);

        foreach (var w in warnings)
            Warn($"Mesh '{e.Name ?? Path.GetFileName(objPath)}': {w}");

        if (mesh == null)
        {
            Warn($"Mesh entity '{e.Name ?? "(unnamed)"}': failed to load '{objPath}'. Skipping.");
            return null;
        }

        // Detect under-bounded displacement: if the material provided an
        // explicit displacement bound but the texture pushed vertices past
        // it, warn so the author can either raise the bound or clamp their
        // texture. The eager-displaced AABBs are already correct, so this
        // is purely a forward-compat hint for future lazy-displacement
        // modes that would actually rely on the bound for correctness.
        float effectiveBound = matDisp?.Bound ?? 0f;
        if (matDisp != null && matDisp.RequestsGeometricDisplacement
            && effectiveBound > 0f
            && maxDisplacement > effectiveBound * 1.0001f)
        {
            Warn($"Mesh '{e.Name ?? Path.GetFileName(objPath)}': " +
                 $"material's displacement.bound={effectiveBound:F4} is smaller " +
                 $"than the maximum applied displacement {maxDisplacement:F4}. " +
                 $"Raise displacement.bound to {maxDisplacement:F4} or higher " +
                 $"to fully enclose the displaced silhouette.");
        }

        string dispTag = DescribeDisplacement(matDisp);

        if (appliedIterations > 0 && matDisp != null && matDisp.RequestsGeometricDisplacement)
        {
            Info($"Mesh:        {e.Name ?? Path.GetFileName(objPath)} \u2014 " +
                 $"{mesh.FaceCount:N0} faces, {mesh.VertexCount:N0} vertices " +
                 $"(subdivision: {appliedScheme} \u00d7 {appliedIterations}, " +
                 $"displacement: {dispTag}, max={maxDisplacement:F4})");
        }
        else if (appliedIterations > 0)
        {
            Info($"Mesh:        {e.Name ?? Path.GetFileName(objPath)} \u2014 " +
                 $"{mesh.FaceCount:N0} faces, {mesh.VertexCount:N0} vertices " +
                 $"(subdivision: {appliedScheme} \u00d7 {appliedIterations})");
        }
        else if (matDisp != null && matDisp.RequestsGeometricDisplacement)
        {
            Info($"Mesh:        {e.Name ?? Path.GetFileName(objPath)} \u2014 " +
                 $"{mesh.FaceCount:N0} faces, {mesh.VertexCount:N0} vertices " +
                 $"(displacement: {dispTag}, max={maxDisplacement:F4})");
        }
        else
        {
            Info($"Mesh:        {e.Name ?? Path.GetFileName(objPath)} \u2014 " +
                 $"{mesh.FaceCount:N0} faces, {mesh.VertexCount:N0} vertices");
        }

        // Seed assignment (same logic as CreateEntity)
        mesh.Seed = seed;

        return mesh;
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
            inner = CreateTexture(disp.Texture, sceneDir);
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

        return new SubdivisionOptions
        {
            Scheme        = scheme,
            Iterations    = Math.Max(0, e.SubdivisionIterations),
            PixelError    = Math.Max(0f, e.SubdivisionPixelError),
            MaxIterations = Math.Max(1, e.SubdivisionMaxIterations),
            Screen        = ctx,
        };

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
            else if (IsHeightFieldType(child.Type))
            {
                hittable = CreateHeightFieldEntity(child, mat, materials, sceneDir, childIdx);
            }
            else
            {
                hittable = CreateEntity(child, mat, childIdx);
            }
 
            if (hittable == null) continue;

            var childTransform = ComputeTransformMatrix(child);
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

    private static ILight? CreateLight(LightData l, int? shadowSamplesOverride,
                                        List<IHittable> objects, SkySettings sky)
    {
        var color = ToVector3(l.Color) ?? Vector3.One;

        return l.Type?.ToLowerInvariant() switch
        {
            "point" => new PointLight(
                ToVector3(l.Position) ?? new Vector3(0, 10, 0),
                color, l.Intensity, l.SoftRadius),

            "directional" or "sun" => new DirectionalLight(
                ToVector3(l.Direction) ?? new Vector3(-1, -1, -1),
                color, l.Intensity, l.AngularRadius,
                shadowSamplesOverride ?? 0),   // 0 → auto (1 if delta, 16 if disc)

            "spot" or "spotlight" => new SpotLight(
                ToVector3(l.Position)  ?? new Vector3(0, 10, 0),
                ToVector3(l.Direction) ?? new Vector3(0, -1, 0),
                color, l.Intensity, l.InnerAngle, l.OuterAngle, l.SoftRadius,
                shadowSamplesOverride ?? l.ShadowSamples),

            // ── Portal light ─────────────────────────────────────────────────
            // Window/skylight onto the environment. Restricts NEE to directions
            // the env can actually be seen from the receiver, transforming
            // interior renders from ~30% useful samples to ~95%.
            // YAML fields:
            //   anchor (or corner): [x, y, z]   # one corner
            //   u:      [x, y, z]               # edge along U
            //   v:      [x, y, z]               # edge along V
            //   shadow_samples: 8               # per-light default
            "portal" or "portal_light"
                => CreatePortalLight(l, shadowSamplesOverride, sky),

            // ── Area light ───────────────────────────────────────────────────
            // YAML fields:
            //   corner: [x, y, z]        # one corner of the rectangle
            //   u:      [x, y, z]        # first edge vector  (e.g. [2,0,0])
            //   v:      [x, y, z]        # second edge vector (e.g. [0,0,2])
            //   intensity: 40.0          # brightness scalar (radiance, W/m²/sr)
            //   shadow_samples: 4        # per-light default (overridable via CLI -S)
            //   soft_radius: 0.0         # optional 1/d² singularity floor (volumetric only)
            "area" or "area_light" or "rect" or "rect_light"
                => CreateAreaLight(l, color, shadowSamplesOverride, objects),

            // ── Sphere light ─────────────────────────────────────────────────
            // YAML fields:
            //   position: [x, y, z]      # center of the sphere
            //   radius:   0.5            # sphere radius (> 0); also defines proxy size
            //   intensity: 30.0          # brightness scalar (radiance, W/m²/sr)
            //   shadow_samples: 4        # per-light default (overridable via CLI -S)
            // (soft_radius is intentionally not consumed for sphere lights —
            //  the solid-angle estimator is bounded by construction.)
            "sphere" or "sphere_light" or "ball" or "ball_light"
                => CreateSphereLight(l, color, shadowSamplesOverride, objects),

            _ => null
        };
    }

    private static PortalLight? CreatePortalLight(LightData l, int? shadowSamplesOverride,
                                                    SkySettings sky)
    {
        var anchor = ToVector3(l.Anchor) ?? ToVector3(l.Corner);
        var u      = ToVector3(l.U);
        var v      = ToVector3(l.V);

        if (anchor == null || u == null || v == null)
        {
            Warn("Portal light requires 'anchor' (or 'corner'), 'u', and 'v' vectors. Skipping.");
            return null;
        }
        if (Vector3.Cross(u.Value, v.Value).LengthSquared() < 1e-12f)
        {
            Warn("Portal light has collinear u/v edges. Skipping.");
            return null;
        }
        if (!sky.CanSampleDirectly)
        {
            Warn("Portal light requires a sky that participates in NEE (HDRI / sun-bearing / non-black flat). Skipping.");
            return null;
        }
        int samples = shadowSamplesOverride ?? (l.ShadowSamples > 0 ? l.ShadowSamples : 8);
        return new PortalLight(sky, anchor.Value, u.Value, v.Value, samples);
    }

    private static AreaLight? CreateAreaLight(LightData l, Vector3 color,
                                               int? shadowSamplesOverride,
                                               List<IHittable> objects)
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

        // Visible emissive proxy — Quad with the light's emission as Lambertian
        // radiance. The proxy's radiance equals the light's `intensity * color`
        // because the existing area-light estimator
        //   L_sample = Intensity × area × cosLight / (d² × N)
        // is the Lambertian-radiance integrand: NEE and the BSDF-sampled hit on
        // the proxy produce the same total energy, closing Veach's MIS estimator.
        // Wrap in BackFaceCulledHittable so the camera and specular rays see
        // through the dark (back) side of the panel — same pattern Arnold and
        // Cycles use for analytic single-sided quad lights.
        var proxyMat = new Emissive(color, l.Intensity) { IsLightProxy = true };
        IHittable proxy = new BackFaceCulledHittable(
            new Quad(corner.Value, u.Value, v.Value, proxyMat));
        // visible_to_camera: false → wrap so primary rays skip past the proxy
        // (mirror reflections, refractions and NEE continue to see it; see
        // Renderer.TraceRay for the skip-loop). Default true is a no-op so
        // existing scenes produce bit-identical BVH content.
        if (!l.VisibleToCamera)
            proxy = new CameraInvisibleHittable(proxy);
        objects.Add(proxy);

        return new AreaLight(corner.Value, u.Value, v.Value, color,
                             l.Intensity, effectiveShadowSamples, l.SoftRadius,
                             proxyMat);
    }

    private static SphereLight? CreateSphereLight(LightData l, Vector3 color,
                                                   int? shadowSamplesOverride,
                                                   List<IHittable> objects)
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

        // Visible emissive proxy — Sphere with the light's emission as
        // Lambertian radiance. The existing solid-angle estimator
        //   L_sample = Intensity × Ω / N
        // already treats `intensity` as surface radiance (W/m²/sr) at
        // convergence: `Intensity × Ω` matches the irradiance at the receiver
        // from a uniform sphere of that radiance. Setting the proxy's
        // emission to `color × intensity` therefore gives BSDF-sampled hits
        // the same energy as NEE — required for MIS to be unbiased.
        var proxyMat = new Emissive(color, l.Intensity) { IsLightProxy = true };
        IHittable proxy = new Sphere(position.Value, l.Radius, proxyMat);
        // visible_to_camera: false → wrap so primary rays skip past the proxy
        // (mirror reflections, refractions and NEE continue to see it; see
        // Renderer.TraceRay for the skip-loop). Default true is a no-op so
        // existing scenes produce bit-identical BVH content.
        if (!l.VisibleToCamera)
            proxy = new CameraInvisibleHittable(proxy);
        objects.Add(proxy);

        return new SphereLight(position.Value, l.Radius, color,
                               l.Intensity, effectiveShadowSamples,
                               proxyMat);
    }

    // =========================================================================
    // Utility helpers
    // =========================================================================

    private static bool IsInfinitePlane(IHittable obj) => obj switch
    {
        InfinitePlane => true,
        Transform t                    => IsInfinitePlane(t.Inner),
        Group g                        => g.Children.Count > 0 && g.Children.All(c => IsInfinitePlane(c)),
        UvTransformedHittable uv       => IsInfinitePlane(uv.Inner),
        VisibilityFilteredHittable vis => IsInfinitePlane(vis.Inner),
        CameraInvisibleHittable ci     => IsInfinitePlane(ci.Inner),
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

    /// <summary>
    /// Resolves the camera focus distance from either a YAML
    /// <c>focal_pos: [x, y, z]</c> (Arnold/Cycles "Focus Point" workflow)
    /// or the scalar <c>focal_dist</c> fallback.
    ///
    /// <para><b>Math.</b> When <paramref name="focalPos"/> is set, the
    /// focus distance is the projection of the camera→focal-point vector
    /// onto the optical axis <c>forward = normalize(lookAt − camPos)</c>:
    /// <c>focusDist = (F − camPos) · forward</c>. The focus plane is
    /// perpendicular to the view direction passing through the focal
    /// point — so a focal point off-axis at <c>(3, 4, −5)</c> with the
    /// camera at the origin looking along <c>−Z</c> yields focus distance
    /// <c>5</c>, not the Euclidean <c>√50</c>. This matches every
    /// production renderer (Arnold, Cycles, RenderMan).</para>
    ///
    /// <para><b>Anomaly handling.</b> Falls back to
    /// <paramref name="fallbackFocalDist"/> with a <see cref="Warn"/> when
    /// the focal point is malformed, behind the camera, coincident with
    /// it, or when the camera itself is degenerate (lookAt == camPos).
    /// Emits <see cref="Info"/> when both fields are set so the user knows
    /// which one took effect.</para>
    /// </summary>
    public static float ComputeFocusDistance(
        Vector3 camPos, Vector3 lookAt,
        List<float>? focalPos, float fallbackFocalDist)
    {
        if (focalPos == null) return fallbackFocalDist;

        if (focalPos.Count < 3)
        {
            Warn("Camera 'focal_pos' requires 3 components [x, y, z]. " +
                 "Falling back to 'focal_dist'.");
            return fallbackFocalDist;
        }

        var forward = lookAt - camPos;
        float forwardLen = forward.Length();
        if (forwardLen < 1e-6f)
        {
            Warn("Camera 'focal_pos' ignored: 'position' and 'look_at' " +
                 "coincide (degenerate camera). Falling back to 'focal_dist'.");
            return fallbackFocalDist;
        }
        forward /= forwardLen;

        var F = new Vector3(focalPos[0], focalPos[1], focalPos[2]);
        float projected = Vector3.Dot(F - camPos, forward);
        if (projected <= 1e-4f)
        {
            Warn($"Camera 'focal_pos' projects behind or onto the camera " +
                 $"(distance = {projected:F4}). Falling back to 'focal_dist'.");
            return fallbackFocalDist;
        }

        // FocalDist defaults to 1f when omitted from YAML, so we can't
        // distinguish "explicitly 1" from "not set" without a custom
        // YamlDotNet converter. Treat any non-default value as user intent
        // for the override-info message.
        if (MathF.Abs(fallbackFocalDist - 1f) > 1e-6f)
            Info($"Camera 'focal_pos' overrides 'focal_dist' " +
                 $"(computed focus distance = {projected:F3}, " +
                 $"YAML focal_dist = {fallbackFocalDist:F3}).");

        return projected;
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

    /// <summary>
    /// Applies the auto-defaults required to wire material-embedded SSS
    /// through the Disney BSDF transmission lobe. Triggered when the
    /// material declares <c>subsurface_radius</c> with at least one
    /// positive channel. Defaults only fill in values the user did NOT
    /// author explicitly (detected by comparing to the C# field default):
    /// <list type="bullet">
    ///   <item><c>spec_trans</c>: 0 → 1.0 (transmission lobe must fire
    ///         to emit MediumTransition.Enter)</item>
    ///   <item><c>transmission_color</c>: null → [1, 1, 1] (no surface
    ///         Beer–Lambert — the medium owns the absorption colour)</item>
    ///   <item><c>transmission_depth</c>: kept at 0 (Beer–Lambert off by
    ///         default; the user can opt in explicitly)</item>
    /// </list>
    /// IOR keeps the Disney default (1.5). Warns on configurations that
    /// would silently disable the SSS path: <c>metallic &gt; 0</c>,
    /// <c>thin_walled = true</c>, or any non-Disney material type.
    /// </summary>
    private static void ApplyEmbeddedSssDefaults(MaterialData m)
    {
        if (m.SubsurfaceRadius == null || m.SubsurfaceRadius.Count == 0) return;
        bool anyPositive = false;
        foreach (var v in m.SubsurfaceRadius) if (v > 0f) { anyPositive = true; break; }
        if (!anyPositive) return;

        // Only Disney BSDF emits MediumTransition.Enter via the transmission
        // lobe. Other types (lambertian, metal, dielectric) ignore the
        // auto-injected medium because they never refract through the BSDF.
        if (!string.IsNullOrEmpty(m.Type)
            && !string.Equals(m.Type, "disney", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(m.Type, "principled", StringComparison.OrdinalIgnoreCase))
        {
            Warn($"Material '{m.Id ?? "(unnamed)"}': 'subsurface_radius' requires Disney BSDF " +
                 $"(type: disney) to drive the random-walk SSS path. " +
                 $"Type '{m.Type}' will skip the embedded SSS medium.");
            return;
        }

        if (m.Metallic > 0f)
            Warn($"Material '{m.Id ?? "(unnamed)"}': 'subsurface_radius' is ignored when " +
                 $"metallic > 0 — the transmission lobe is suppressed by the metallic blend.");

        if (m.ThinWalled)
            Warn($"Material '{m.Id ?? "(unnamed)"}': 'subsurface_radius' is ignored when " +
                 $"thin_walled = true — thin walls have no interior volume.");

        // spec_trans default = 0. Promote to 1.0 when the user only authored
        // SSS knobs (let any explicit value pass through untouched).
        if (m.SpecTrans <= 0f) m.SpecTrans = 1f;

        // transmission_color default = null → white (medium owns the colour).
        if (m.TransmissionColor == null)
            m.TransmissionColor = new List<float> { 1f, 1f, 1f };
    }

    /// <summary>
    /// Converts a material's <c>subsurface_radius</c> (mean free path per
    /// RGB channel, in world units) + <c>subsurface_color</c> (volume
    /// albedo) into a <see cref="HomogeneousMedium"/>. Mirrors the
    /// Arnold <c>standard_surface</c> / Cycles <c>Principled BSDF</c>
    /// random-walk SSS parameterization:
    /// <code>
    ///   r_eff = radius · scale         (effective MFP per channel)
    ///   σ_t   = 1 / r_eff              (extinction coefficient)
    ///   σ_s   = albedo · σ_t           (scattering coefficient)
    ///   σ_a   = (1 − albedo) · σ_t     (absorption coefficient)
    /// </code>
    /// Phase function: Henyey–Greenstein with g = <c>subsurface_anisotropy</c>
    /// (0 by default → near-isotropic, typical for marble / wax / stone).
    /// </summary>
    private static IMedium? BuildEmbeddedSssMedium(MaterialData m)
    {
        if (m.SubsurfaceRadius == null || m.SubsurfaceRadius.Count == 0)
            return null;

        // Volume albedo: subsurface_color when present, otherwise base_color
        // (Cycles convention). Falls back to white if neither is set.
        List<float>? albedoSrc = m.SubsurfaceColor ?? m.Color;
        Vector3 albedo = albedoSrc != null && albedoSrc.Count >= 3
            ? new Vector3(
                MathF.Max(0f, MathF.Min(1f, albedoSrc[0])),
                MathF.Max(0f, MathF.Min(1f, albedoSrc[1])),
                MathF.Max(0f, MathF.Min(1f, albedoSrc[2])))
            : Vector3.One;

        // Radius: pad / clamp to 3 channels. Values ≤ 0 fall back to a
        // sentinel large MFP so the channel is effectively transparent.
        var r = m.SubsurfaceRadius;
        float rR = r.Count >= 1 ? r[0] : 1f;
        float rG = r.Count >= 2 ? r[1] : rR;
        float rB = r.Count >= 3 ? r[2] : rG;

        float scale = m.SubsurfaceScale > 0f ? m.SubsurfaceScale : 1f;
        rR = MathF.Max(1e-4f, rR * scale);
        rG = MathF.Max(1e-4f, rG * scale);
        rB = MathF.Max(1e-4f, rB * scale);

        Vector3 sigmaT = new Vector3(1f / rR, 1f / rG, 1f / rB);
        Vector3 sigmaS = sigmaT * albedo;
        Vector3 sigmaA = sigmaT - sigmaS; // = σ_t · (1 − albedo), nonneg by clamp

        float g = MathF.Max(-0.999f, MathF.Min(0.999f, m.SubsurfaceAnisotropy));
        IPhaseFunction phase = MathF.Abs(g) < 1e-4f
            ? new IsotropicPhase()
            : new HenyeyGreensteinPhase(g);

        Verbose($"SSS medium:  material '{m.Id}' → " +
                $"r=[{rR:0.###},{rG:0.###},{rB:0.###}] α=[{albedo.X:0.##},{albedo.Y:0.##},{albedo.Z:0.##}] " +
                $"σ_s=[{sigmaS.X:0.##},{sigmaS.Y:0.##},{sigmaS.Z:0.##}] " +
                $"σ_a=[{sigmaA.X:0.###},{sigmaA.Y:0.###},{sigmaA.Z:0.###}] " +
                $"phase={(MathF.Abs(g) < 1e-4f ? "isotropic" : $"hg(g={g:0.##})")}");

        return new HomogeneousMedium(sigmaA, sigmaS, phase);
    }

    /// <summary>
    /// Dispatches on <c>type</c> to build the appropriate <see cref="IMedium"/>.
    /// Used for both <c>world.medium</c> (global) and the <c>mediums:</c>
    /// library entries referenced by entity-level <c>interior_medium</c>.
    /// Returns null (and warns) on invalid or unknown configurations so the
    /// renderer falls back to vacuum on the affected binding instead of
    /// crashing. <paramref name="context"/> is the label used in diagnostics
    /// (e.g. <c>"world.medium"</c> or <c>"mediums['marble_int']"</c>).
    /// </summary>
    private static IMedium? BuildMedium(MediumData md, string sceneDir, string context = "medium")
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
            case "atmosphere":
            case "nishita":
            case "aerial_perspective":
                return BuildNishitaAtmosphere(md, phase);
            default:
                Warn($"{context}: unsupported medium type '{type}'. Supported: homogeneous, height_fog, procedural, grid, atmosphere. Ignoring.");
                return null;
        }
    }

    private static NishitaAtmosphereMedium BuildNishitaAtmosphere(MediumData md, IPhaseFunction phase)
    {
        // The atmosphere medium reuses the Nishita physical constants; the
        // YAML knobs are limited to the scene-mapping ones (sea level, world
        // scale) and density multipliers. Default phase is HG g=0.76 (Mie
        // forward scattering); the user can override via the `phase` key.
        // When no phase is specified in YAML, BuildPhaseFunction returns
        // IsotropicPhase by default — for atmosphere we substitute the
        // Mie-typical HG g=0.76 if isotropic was the implicit choice.
        IPhaseFunction effectivePhase = string.IsNullOrWhiteSpace(md.Phase)
            ? new HenyeyGreensteinPhase(0.76f)
            : phase;

        Vector3 air     = ToVector3(md.AirDensity) ?? Vector3.One;
        float seaLevel  = md.Y0;
        Info($"Medium:      atmosphere (Nishita, sea_level={seaLevel}, world_scale={md.WorldScale} m/wu, {(string.IsNullOrWhiteSpace(md.Phase) ? "hg(0.76)" : md.Phase)} phase)");
        return new NishitaAtmosphereMedium(effectivePhase,
                                            airDensity: air,
                                            dustDensity: md.DustDensity,
                                            seaLevelY: seaLevel,
                                            worldScale: md.WorldScale);
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
        Info($"Medium:      homogeneous ({md.Phase ?? "isotropic"} phase)");
        Verbose($"Medium det:  \u03c3_a={sigmaA}, \u03c3_s={sigmaS}");
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
        Info($"Medium:      height_fog ({md.Phase ?? "isotropic"} phase)");
        Verbose($"Medium det:  \u03c3_a={sigmaA}, \u03c3_s={sigmaS}, y0={md.Y0}, H={H}");
        return new HeightFogMedium(sigmaA, sigmaS, md.Y0, H, phase);
    }

    private static IMedium BuildProcedural(MediumData md, IPhaseFunction phase)
    {
        var (sigmaA, sigmaS) = ParseSigmas(md, "Medium 'procedural'");
        if (md.Frequency <= 0f) Warn($"Medium 'procedural' has non-positive frequency={md.Frequency}. Using 1.");
        float freq = md.Frequency > 0f ? md.Frequency : 1f;
        Info($"Medium:      procedural/fBm ({md.Phase ?? "isotropic"} phase)");
        Verbose($"Medium det:  \u03c3_base_a={sigmaA}, \u03c3_base_s={sigmaS}, freq={freq}, octaves={md.Octaves}");
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
                Info($"Medium:      grid {nx}\u00d7{ny}\u00d7{nz} from '{md.File}'");
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
            Info($"Medium:      grid {nx}\u00d7{ny}\u00d7{nz} (inline)");
        }
        else
        {
            Warn("Medium 'grid' requires either 'file' or 'data'. Ignoring.");
            return null;
        }

        // GridMedium requires at least 2 voxels per axis for trilinear/tricubic
        // interpolation to be well-defined. Catch this here for a clear message.
        if (nx < 2 || ny < 2 || nz < 2)
        {
            Warn($"Medium 'grid' resolution {nx}×{ny}×{nz} is too small — each axis must be ≥ 2. Ignoring.");
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
            Verbose($"Medium det:  interpolation = {interp.ToString().ToLowerInvariant()}");
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
