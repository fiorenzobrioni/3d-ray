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
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, SkySettings Sky, IMedium? GlobalMedium, MotionBlurSettings MotionBlur)
        Load(string yamlPath, int imageWidth, int imageHeight,
             int? shadowSamplesOverride = null, string? cameraSelector = null)
    {
        // Clear any leftover messages from a previous Load() call
        _deferredMessages.Clear();

        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .WithTypeConverter(new MaterialParamConverter())
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
                if (IsOpenPbr(m))
                {
                    if (BuildOpenPbrMedium(m) is { } opbMedium) embeddedSssMedium[m.Id] = opbMedium;
                    continue;
                }
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
            {
                objects.Add(groundHittable);
            }
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
        var apertureShape  = ApplyPhysicalCamera(camDataEarly, (float)imageWidth / imageHeight);
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
        bool sceneHasMotion = false;
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
                    hittable = CreateMeshEntity(e, mat, materials, sceneDir, idx);
                }
                else if (string.Equals(e.Type, "group", StringComparison.OrdinalIgnoreCase))
                {
                    // The group's own entity transform is the world transform of its
                    // (group-local) children; pass it so flagged children register in
                    // world space. It is re-applied as the rendered outer Transform below.
                    hittable = CreateGroupEntity(e, mat, materials, sceneDir, idx, ComputeTransformMatrix(e));
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
                    // Motion blur: a non-empty `motion:` block promotes the static
                    // Transform to an AnimatedTransform whose keyframe 0 is the
                    // entity's base scale/rotate/translate pose. The wrapper order
                    // below (camera visibility, medium binding) is unchanged.
                    if (e.Motion is { Count: > 0 })
                    {
                        hittable = new AnimatedTransform(hittable, BuildMotionKeys(e));
                        sceneHasMotion = true;
                    }
                    else
                    {
                        var transform = ComputeTransformMatrix(e);
                        if (transform != Matrix4x4.Identity)
                            hittable = new Transform(hittable, transform);
                    }
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
                var light = CreateLight(l, shadowSamplesOverride, objects, sky, sceneDir);
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
        // Camera motion blur: a `motion:` block on the camera adds pose
        // keyframes (base pose = implicit key at time 0); the camera then
        // rebuilds its basis at each ray's shutter time.
        var cameraMotion = BuildCameraMotionKeys(camData, camPos, camLookAt, camVup);
        if (cameraMotion != null)
            sceneHasMotion = true;
        var camera    = new Camera.Camera(
            camPos, camLookAt, camVup,
            camData.Fov, aspect,
            camData.Aperture, focusDist,
            cameraMotion, apertureShape);
        if (camData.WhiteBalance is float wb)
        {
            if (wb >= WhiteBalance.MinKelvin && wb <= WhiteBalance.MaxKelvin)
                camera.WhiteBalanceKelvin = wb;
            else
                Warn($"Camera 'white_balance' must be within {WhiteBalance.MinKelvin}-{WhiteBalance.MaxKelvin} K (got {wb}); ignored.");
        }

        // Physical exposure (§3.34): EV100 directly, or from ISO, shutter and
        // f-number, mapping scene radiance in nits to the display.
        float? ev100 = camData.Ev100;
        if (ev100 == null && (camData.Iso != null || camData.ShutterSpeed != null))
        {
            if (camData.Iso is float iso && camData.ShutterSpeed is float shutter && camData.FStop is float fStop
                && iso > 0f && shutter > 0f && fStop > 0f)
                ev100 = LightUnits.Ev100(fStop, shutter, iso);
            else
                Warn("Camera physical exposure needs 'iso', 'shutter_speed' and 'f_stop' (or 'ev100'); ignored.");
        }
        if (ev100 is float ev)
        {
            camera.PhysicalExposureEv = LightUnits.ExposureEvFromEv100(ev);
            Info($"Exposure:    EV100 {ev:0.##} (scene in nits)");
        }

        var motionBlur = ResolveMotionBlurSettings(camData.Shutter, sceneHasMotion);

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

        return (world, camera, lights, sky, globalMedium, motionBlur);
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
    // Utility helpers
    // =========================================================================

    private static bool IsInfinitePlane(IHittable obj) => obj switch
    {
        InfinitePlane => true,
        Transform t                    => IsInfinitePlane(t.Inner),
        AnimatedTransform at           => IsInfinitePlane(at.Inner),
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
}
