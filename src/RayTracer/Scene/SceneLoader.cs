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
    /// Set once at the top of a load; read by
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
    /// Length of one scene unit in metres (<c>units.length</c>), for the
    /// quantities that carry metres: f-stop apertures, candela, light power,
    /// the atmosphere. Set at the start of <see cref="Load(ParsedScene, int, int, int?, string?)"/>.
    /// </summary>
    private static float _metersPerUnit = 1f;

    /// <summary>A scene file read and validated, its imports merged in.</summary>
    public sealed record ParsedScene(SceneData Data, string Path, string Directory);

    private static IDeserializer NewDeserializer() => new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new MaterialParamConverter())
        .Build();

    /// <summary>
    /// Reads a scene file of format 2: validates it (every error at once,
    /// <see cref="SceneFormatException"/>; warnings are queued), deserialises it
    /// and merges its imports. Clears the deferred messages of a previous load.
    /// </summary>
    public static ParsedScene Parse(string yamlPath)
    {
        _deferredMessages.Clear();
        var data = ReadValidated(yamlPath);
        var sceneDir = Path.GetDirectoryName(Path.GetFullPath(yamlPath)) ?? ".";
        if (data.Imports is { Count: > 0 })
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(yamlPath) };
            ProcessImports(data, sceneDir, visited);
        }
        return new ParsedScene(data, yamlPath, sceneDir);
    }

    private static SceneData ReadValidated(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var issues = SceneValidator.Validate(yaml);
        var errors = issues.Where(i => i.IsError).Select(i => i.Format(yamlPath)).ToList();
        if (errors.Count > 0) throw new SceneFormatException(yamlPath, errors);
        foreach (var w in issues) Warn(w.Format(yamlPath));
        try
        {
            return NewDeserializer().Deserialize<SceneData>(yaml)
                   ?? throw new SceneFormatException(yamlPath, ["the file is empty"]);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new SceneFormatException(yamlPath,
                [$"{Path.GetFileName(yamlPath)}:{ex.Start.Line}:{ex.Start.Column}: {ex.InnerException?.Message ?? ex.Message}"]);
        }
    }

    /// <summary>Reads, validates and builds a scene (see <see cref="Parse"/>).</summary>
    /// <param name="yamlPath">Path to the YAML scene file.</param>
    /// <param name="imageWidth">Target image width (for aspect ratio).</param>
    /// <param name="imageHeight">Target image height (for aspect ratio).</param>
    /// <param name="shadowSamplesOverride">When non-null, the shadow samples of every light (CLI <c>-S</c>).</param>
    /// <param name="cameraSelector">A camera of <c>cameras:</c> by name (case-insensitive) or index.</param>
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, SkySettings Sky, IMedium? GlobalMedium, MotionBlurSettings MotionBlur)
        Load(string yamlPath, int imageWidth, int imageHeight,
             int? shadowSamplesOverride = null, string? cameraSelector = null)
        => Load(Parse(yamlPath), imageWidth, imageHeight, shadowSamplesOverride, cameraSelector);

    /// <summary>Builds a parsed scene for an image of the given size.</summary>
    public static (IHittable World, Camera.Camera Camera, List<ILight> Lights, SkySettings Sky, IMedium? GlobalMedium, MotionBlurSettings MotionBlur)
        Load(ParsedScene scene, int imageWidth, int imageHeight,
             int? shadowSamplesOverride = null, string? cameraSelector = null)
    {
        var data = scene.Data;
        var sceneDir = scene.Directory;
        _metersPerUnit = ParseLengthUnit(data.Units?.Length);
        _csgEmissiveWarningEmitted = false;

        // Materials, in two passes so a mix can reference any material by id
        // (including another mix). Unresolved or cyclic references fall back
        // to a grey Lambertian with a warning.
        var materials = new Dictionary<string, IMaterial>();
        var deferredMix = new List<MaterialData>();
        if (data.Materials != null)
        {
            foreach (var m in data.Materials)
            {
                if (m.Id == null) { Warn("Material without an 'id'. Skipping."); continue; }
                if (m.Type == "mix") { deferredMix.Add(m); continue; }
                materials[m.Id] = CreateMaterial(m, sceneDir);
            }

            var remaining = deferredMix;
            while (remaining.Count > 0)
            {
                var stillRemaining = new List<MaterialData>();
                foreach (var m in remaining)
                {
                    bool canResolveA = m.MaterialA == null || materials.ContainsKey(m.MaterialA);
                    bool canResolveB = m.MaterialB == null || materials.ContainsKey(m.MaterialB);
                    if (canResolveA && canResolveB)
                        materials[m.Id!] = CreateMixMaterial(m, materials, sceneDir);
                    else
                        stillRemaining.Add(m);
                }
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

        // The medium filling the scene; none = surface-only rendering.
        IMedium? globalMedium = null;
        if (data.World?.Medium is { } md)
            globalMedium = BuildMedium(md, sceneDir, "world.medium");

        // Medium library, bound per entity by interior_medium / exterior_medium.
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

        // The medium inside an OpenPBR object with subsurface or scattering
        // transmission, bound to every entity that uses the material (an
        // explicit interior_medium wins).
        var embeddedSssMedium = new Dictionary<string, IMedium>(StringComparer.OrdinalIgnoreCase);
        if (data.Materials != null)
        {
            foreach (var m in data.Materials)
                if (m.Id != null && m.Type == "openpbr" && BuildOpenPbrMedium(m) is { } opbMedium)
                    embeddedSssMedium[m.Id] = opbMedium;
            if (embeddedSssMedium.Count > 0)
                Info($"SSS embedded: {embeddedSssMedium.Count} material(s) with auto-built medium " +
                     $"({string.Join(", ", embeddedSssMedium.Keys)})");
        }

        var objects = new List<IHittable>();

        if (data.World?.Ground != null && BuildGround(data.World.Ground, data.World.Sky, materials) is { } groundHittable)
            objects.Add(groundHittable);

        // Templates (last definition of a name wins: local over imported)
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
        // origin / forward axis / FOV before the mesh loader runs.
        var camData        = ResolveCamera(data, cameraSelector);
        var apertureShape  = ApplyPhysicalCamera(camData, (float)imageWidth / imageHeight);
        var camPos         = ToVector3(camData.Position) ?? new Vector3(0, 1, -5);
        var camLookAt      = ToVector3(camData.LookAt)   ?? Vector3.Zero;
        var camForward     = camLookAt - camPos;
        if (camForward.LengthSquared() < 1e-8f) camForward = -Vector3.UnitZ;
        _currentScreenContext = new SubdivisionScreenContext
        {
            CameraOrigin       = camPos,
            CameraForward      = Vector3.Normalize(camForward),
            VerticalFovRadians = MathUtils.DegreesToRadians(camData.Fov),
            ImageHeight        = imageHeight,
        };

        bool sceneHasMotion = false;
        if (data.Entities != null)
        {
            for (int idx = 0; idx < data.Entities.Count; idx++)
            {
                var e   = data.Entities[idx];
                var mat = GetMaterial(materials, e.Material);

                // Displacement needs a polygonal mesh (groups and instances
                // check their children).
                if (mat.Displacement != null && e.Type is not ("mesh" or "group" or "instance"))
                {
                    Warn($"Entity '{e.Name ?? e.Type ?? "(unnamed)"}' (type='{e.Type}') " +
                         $"uses material '{e.Material}' which has a displacement, " +
                         $"but displacement only applies to polygonal mesh entities. " +
                         $"Use a 'mesh' entity with subdivision, or attach 'bump_map' " +
                         $"to the material instead.");
                }

                IHittable? hittable = e.Type switch
                {
                    "csg"         => CreateCsgEntity(e, mat, materials, idx),
                    "mesh"        => CreateMeshEntity(e, mat, materials, sceneDir, idx),
                    // The group's own transform is the world transform of its
                    // children; passed so flagged children register in world space.
                    "group"       => CreateGroupEntity(e, mat, materials, sceneDir, idx, ComputeTransformMatrix(e)),
                    "instance"    => CreateInstanceEntity(e, materials, templates, templateCache, sceneDir, idx),
                    "heightfield" => CreateHeightFieldEntity(e, mat, materials, sceneDir, idx),
                    _             => CreateEntity(e, mat, idx),
                };

                if (hittable != null)
                {
                    // A motion block makes the transform an AnimatedTransform whose
                    // key 0 is the entity's base pose.
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
                    // Visibility wraps the transformed entity, so the BVH still
                    // partitions by its world-space box; the medium binding wraps
                    // last so its interface rides on every hit.
                    hittable = ApplyVisibility(hittable, e.Visibility);
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

        // BVH over the finite objects; infinite planes stay in a linear list
        // (their unbounded box would poison the tree). Below BvhThreshold
        // objects a linear list is faster.
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

        float aspect  = (float)imageWidth / imageHeight;
        var camUp     = ToVector3(camData.Up) ?? Vector3.UnitY;
        // focus_point (a point in focus) wins over focus_distance.
        float focusDist = ComputeFocusDistance(camPos, camLookAt, camData.FocusPoint, camData.FocusDistance);
        // Camera keyframes (base pose = key at time 0): the camera rebuilds
        // its basis at each ray's shutter time.
        var cameraMotion = BuildCameraMotionKeys(camData, camPos, camLookAt, camUp);
        if (cameraMotion != null)
            sceneHasMotion = true;
        var camera    = new Camera.Camera(
            camPos, camLookAt, camUp,
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

        var motionBlur = ResolveMotionBlurSettings(camData.ShutterInterval, sceneHasMotion);

        // Emissive geometry joins the NEE pool as GeometryLights (the proxies
        // of sphere and area lights are skipped: their light samples them).
        ExtractGeometryLights(objects, lights, shadowSamplesOverride ?? GeometryLight.DefaultShadowSamples);

        if (sky.CanSampleDirectly)
            lights.Add(new EnvironmentLight(sky, shadowSamplesOverride ?? 1));

        // The analytic sun of a sky is a light of its own: cone-sampled
        // shadows, lower variance than sampling it through the sky.
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

        return (world, camera, lights, sky, globalMedium, motionBlur);
    }

    /// <summary>
    /// <c>units.length</c>: metres per scene unit, as a number or a unit name.
    /// </summary>
    private static float ParseLengthUnit(string? length)
    {
        if (string.IsNullOrWhiteSpace(length)) return 1f;
        float? m = length.Trim() switch
        {
            "m" => 1f, "cm" => 0.01f, "mm" => 0.001f, "km" => 1000f, "in" => 0.0254f, "ft" => 0.3048f,
            var s => float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0f ? v : null,
        };
        if (m is float mm) return mm;
        Warn($"units.length '{length}' is not a positive number of metres or m, cm, mm, km, in, ft; using metres.");
        return 1f;
    }

    /// <summary>Wraps a surface in the per-category visibility mask of its <c>visibility:</c> block.</summary>
    private static IHittable ApplyVisibility(IHittable hittable, VisibilityData? v)
    {
        var mask = VisibilityMask(v);
        return mask == HitVisibilityMask.None ? hittable : new VisibilityFilteredHittable(hittable, mask);
    }

    private static HitVisibilityMask VisibilityMask(VisibilityData? v)
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

    // =========================================================================
    // YAML Imports
    // =========================================================================

    /// <summary>
    /// Merges the <c>imports:</c> of a scene, recursively: their materials,
    /// mediums, lights, entities and templates go before the local ones, so a
    /// local id or name wins. <c>world</c>, <c>cameras</c>, <c>render</c> and
    /// <c>units</c> are not imported. A cycle is skipped with a warning; an
    /// imported file is validated like the scene.
    /// </summary>
    private static void ProcessImports(SceneData data, string baseDir, HashSet<string> visited)
    {
        if (data.Imports == null) return;

        var importedMaterials = new List<MaterialData>();
        var importedEntities  = new List<EntityData>();
        var importedLights    = new List<LightData>();
        var importedTemplates = new List<EntityData>();
        var importedMediums   = new List<MediumData>();

        foreach (var import in data.Imports)
        {
            if (string.IsNullOrWhiteSpace(import))
            {
                Warn("Empty entry in 'imports'. Skipping.");
                continue;
            }

            string fullPath = Path.GetFullPath(Path.IsPathRooted(import) ? import : Path.Combine(baseDir, import));
            if (!visited.Add(fullPath))
            {
                Warn($"Circular import detected: '{import}'. Skipping.");
                continue;
            }
            if (!File.Exists(fullPath))
                throw new SceneFormatException(fullPath, [$"imported file not found: '{import}'"]);

            var importData = ReadValidated(fullPath);
            var importDir = Path.GetDirectoryName(fullPath) ?? ".";
            if (importData.Imports is { Count: > 0 })
                ProcessImports(importData, importDir, visited);

            if (importData.Materials != null) importedMaterials.AddRange(importData.Materials);
            if (importData.Entities != null)  importedEntities.AddRange(importData.Entities);
            if (importData.Lights != null)    importedLights.AddRange(importData.Lights);
            if (importData.Templates != null) importedTemplates.AddRange(importData.Templates);
            if (importData.Mediums != null)   importedMediums.AddRange(importData.Mediums);

            Verbose($"Imported:    {import} " +
                    $"({importData.Materials?.Count ?? 0} materials, " +
                    $"{importData.Entities?.Count ?? 0} entities, " +
                    $"{importData.Lights?.Count ?? 0} lights, " +
                    $"{importData.Templates?.Count ?? 0} templates, " +
                    $"{importData.Mediums?.Count ?? 0} mediums)");
        }

        // Imported go before local (local wins on id / name conflicts).
        if (importedMaterials.Count > 0) { importedMaterials.AddRange(data.Materials ?? []); data.Materials = importedMaterials; }
        if (importedEntities.Count > 0)  { importedEntities.AddRange(data.Entities ?? []);   data.Entities = importedEntities; }
        if (importedLights.Count > 0)    { importedLights.AddRange(data.Lights ?? []);       data.Lights = importedLights; }
        if (importedTemplates.Count > 0) { importedTemplates.AddRange(data.Templates ?? []); data.Templates = importedTemplates; }
        if (importedMediums.Count > 0)   { importedMediums.AddRange(data.Mediums ?? []);     data.Mediums = importedMediums; }
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
