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
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var data = deserializer.Deserialize<SceneData>(yaml);
        if (data == null) throw new InvalidOperationException("Failed to parse YAML scene.");

        // Scene directory for resolving relative paths (image textures, etc.)
        var sceneDir = Path.GetDirectoryName(Path.GetFullPath(yamlPath)) ?? ".";

        // Materials dictionary
        var materials = new Dictionary<string, IMaterial>();
        if (data.Materials != null)
        {
            foreach (var m in data.Materials)
            {
                if (m.Id == null) continue;
                materials[m.Id] = CreateMaterial(m, sceneDir);
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

        // Entities
        if (data.Entities != null)
        {
            for (int idx = 0; idx < data.Entities.Count; idx++)
            {
                var e        = data.Entities[idx];
                var mat      = GetMaterial(materials, e.Material);
                var hittable = CreateEntity(e, mat, idx);
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
        //
        // BUG-02 fix: check also handles InfinitePlane wrapped in a Transform
        // (e.g. a tilted infinite plane). The Transform wrapper hides the concrete
        // type from a simple `is InfinitePlane` check. IsInfinitePlane() unwraps
        // the Transform chain recursively to detect the underlying type.
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
    // Camera resolution
    // =========================================================================

    /// <summary>
    /// Reads the YAML file and lists all cameras defined in <c>cameras:</c>.
    /// Called by Program.cs when <c>--list-cameras</c> is passed.
    /// </summary>
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
                Console.WriteLine($"[Warning] --camera '{selector}' was specified but the scene uses " +
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

                Console.WriteLine($"[Warning] --camera index {idx} is out of range " +
                                  $"(scene has {list.Count} cameras, indices 0–{list.Count - 1}). " +
                                  "Using camera 0.");
                return list[0];
            }

            // No match by name or index
            var names = string.Join(", ",
                list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
            Console.WriteLine($"[Warning] --camera '{selector}' not found. " +
                              $"Available: {names}. Using camera 0.");
            return list[0];
        }

        // No selector → use the only camera silently, or warn and use first
        if (list.Count == 1)
            return list[0];

        var cameraNames = string.Join(", ",
            list.Select((c, i) => c.Name != null ? $"\"{c.Name}\" (#{i})" : $"#{i}"));
        Console.WriteLine($"[Warning] Scene contains {list.Count} cameras. Using camera 0. " +
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
    /// Handles two cases:
    ///   1. Bare primitive (Sphere, Quad, Triangle, Disk) with Emissive material.
    ///   2. Transform-wrapped primitive: Transform now implements ISamplable and
    ///      correctly maps Sample() to world space, so rotated/scaled/translated
    ///      emissives participate in NEE like any other geometry light.
    ///
    /// Chain transforms (Transform wrapping another Transform) are handled
    /// recursively via UnwrapEmissive().
    /// </summary>
    private static void ExtractGeometryLights(List<IHittable> objects, List<ILight> lights, int shadowSamples)
    {
        foreach (var obj in objects)
        {
            var (samplable, emissive) = ResolveEmissiveSamplable(obj);
            if (samplable != null && emissive != null)
                lights.Add(new GeometryLight(samplable, emissive, shadowSamples));
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
            // ── Bare primitives ────────────────────────────────────────────
            case Sphere    s  when s.Material  is Emissive em: return (s,  em);
            case Quad      q  when q.Material  is Emissive em: return (q,  em);
            case Triangle  tr when tr.Material is Emissive em: return (tr, em);
            case Disk      d  when d.Material  is Emissive em: return (d,  em);

            // ── Transform wrapper ──────────────────────────────────────────
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
                // Set via the concrete type's property (all 5 materials have it)
                switch (material)
                {
                    case Lambertian  lam: lam.NormalMap = normalMap; break;
                    case Metal       met: met.NormalMap = normalMap; break;
                    case Dielectric  die: die.NormalMap = normalMap; break;
                    case Emissive    emi: emi.NormalMap = normalMap; break;
                    case DisneyBsdf  dis: dis.NormalMap = normalMap; break;
                }
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
            Console.WriteLine("[Warning] Normal map requires a 'path' field. Skipping.");
            return null;
        }

        string mapPath = Path.IsPathRooted(nm.Path)
            ? nm.Path
            : Path.Combine(sceneDir, nm.Path);

        if (!File.Exists(mapPath))
        {
            Console.WriteLine($"[Warning] Normal map file not found: {mapPath}. Skipping.");
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
            Console.WriteLine($"[Warning] Failed to load normal map '{mapPath}': {ex.Message}. Skipping.");
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
            Console.WriteLine("[Warning] HDRI sky requires a 'path' field. Falling back to flat gray.");
            return new SkySettings(new Vector3(0.5f));
        }

        string hdrPath = Path.IsPathRooted(skyData.Path)
            ? skyData.Path
            : Path.Combine(sceneDir, skyData.Path);

        if (!File.Exists(hdrPath))
        {
            Console.WriteLine($"[Warning] HDRI file not found: {hdrPath}. Falling back to flat magenta.");
            return new SkySettings(new Vector3(1f, 0f, 1f));
        }

        try
        {
            Console.Write($"  Loading HDRI: {skyData.Path}... ");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (pixels, width, height) = HdrLoader.Load(hdrPath);
            var envMap = new EnvironmentMap(pixels, width, height,
                                            skyData.Intensity, skyData.Rotation);
            Console.WriteLine($"done ({width}x{height}, {sw.ElapsedMilliseconds} ms)");
            return new SkySettings(envMap);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"failed!");
            Console.WriteLine($"[Warning] Failed to load HDRI '{hdrPath}': {ex.Message}. Falling back to flat magenta.");
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
            Console.WriteLine("[Warning] Image texture requires a 'path' field. Using fallback magenta.");
            return new SolidColor(new Vector3(1f, 0f, 1f)); // Magenta = missing texture
        }

        // Resolve relative to scene directory
        string imagePath = Path.IsPathRooted(t.Path)
            ? t.Path
            : Path.Combine(sceneDir, t.Path);

        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"[Warning] Image texture file not found: {imagePath}. Using fallback magenta.");
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
            Console.WriteLine($"[Warning] Failed to load image texture '{imagePath}': {ex.Message}. Using fallback magenta.");
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

            "triangle" => new Triangle(
                ToVector3(e.V0) ?? Vector3.Zero,
                ToVector3(e.V1) ?? Vector3.UnitX,
                ToVector3(e.V2) ?? Vector3.UnitY, mat),
            "quad"     => new Quad(
                ToVector3(e.Q) ?? Vector3.Zero,
                ToVector3(e.U) ?? Vector3.UnitX,
                ToVector3(e.V) ?? Vector3.UnitY, mat),
            "cylinder" => new Cylinder(ToVector3(e.Center) ?? Vector3.Zero, e.Radius, e.Height, mat),
            "disk"     => new Disk(ToVector3(e.Center) ?? Vector3.Zero, ToVector3(e.Normal) ?? Vector3.UnitY, e.Radius, mat),
            "plane" or "infinite_plane"
                       => new InfinitePlane(ToVector3(e.Point) ?? Vector3.Zero, ToVector3(e.Normal) ?? Vector3.UnitY, mat),
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
            Console.WriteLine(
                "[Warning] Area light requires 'corner', 'u', and 'v' vectors. Skipping.");
            return null;
        }

        // CLI override takes precedence over per-light YAML value
        int effectiveShadowSamples = shadowSamplesOverride ?? l.ShadowSamples;

        return new AreaLight(corner.Value, u.Value, v.Value, color,
                             l.Intensity, effectiveShadowSamples);
    }

    // =========================================================================
    // Utility helpers
    // =========================================================================

    /// <summary>
    /// BUG-02 fix: Returns true if the hittable (possibly inside a Transform chain)
    /// is ultimately an InfinitePlane. Prevents a Transform-wrapped InfinitePlane
    /// from entering the BVH with an enormous 2×10^6 AABB that degrades the tree.
    /// </summary>
    private static bool IsInfinitePlane(IHittable obj) => obj switch
    {
        InfinitePlane => true,
        Transform t   => IsInfinitePlane(t.Inner),
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
            Console.WriteLine($"[Warning] Material '{id}' not found. Using default grey Lambertian.");
        return new Lambertian(new Vector3(0.5f));
    }

    private static Vector3? ToVector3(List<float>? list)
    {
        if (list == null || list.Count < 3) return null;
        return new Vector3(list[0], list[1], list[2]);
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
