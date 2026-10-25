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
            ITexture tex = CreateTexture(g.HeightTexture, sceneDir, TextureRole.Data);
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
            return DisneyMaterial.Create(
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
    /// Supported modes: <c>flat</c>, <c>gradient</c>, <c>hdri</c>, and the
    /// physical skies <c>hosek_wilkie</c> (<c>physical</c>), <c>preetham</c>,
    /// <c>nishita</c>. When the
    /// <c>world.sky</c> block is missing or its <c>type</c> is unrecognised,
    /// falls back to a flat sky using <see cref="DefaultSkyColor"/>.
    /// </summary>
    private static SkySettings BuildSkySettings(SkyData? skyData, string sceneDir)
    {
        if (skyData == null)
            return new SkySettings(DefaultSkyColor);

        string skyType = skyData.Type?.ToLowerInvariant() ?? "";

        ISkyModel lightingModel = BuildSkyModel(skyType, skyData, sceneDir);
        if (lightingModel is AnalyticSky analytic)
        {
            // What the sky gives a horizontal plane on the ground: the scale to
            // expose for (photometric: lux).
            string unit = analytic.Units == SkyUnits.Photometric ? "lux" : "(relative)";
            Info($"Sky light:   {analytic.SkyIlluminance * analytic.Intensity:0.###} {unit} on the ground, " +
                 $"{analytic.SunIlluminance * analytic.Intensity:0.###} of it from the sun");
        }
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
            "preetham"   => BuildPhysicalSkyModel(skyData, hosekWilkie: false),
            "hosek_wilkie" or "physical"
                         => BuildPhysicalSkyModel(skyData, hosekWilkie: true),
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

    private static Vector3 SkySunDirection(SkyData skyData)
        => skyData.Sun?.Direction is { Count: >= 3 }
            ? Vector3.Normalize(ToVector3(skyData.Sun.Direction) ?? Vector3.UnitY)
            : Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f)); // noon-ish, slightly south

    private static SkyUnits ParseSkyUnits(SkyData skyData)
    {
        switch (skyData.Units?.Trim().ToLowerInvariant())
        {
            case null or "" or "relative": return SkyUnits.Relative;
            case "photometric" or "nits": return SkyUnits.Photometric;
            default:
                Warn($"Unknown sky units '{skyData.Units}' (relative | photometric). Using relative.");
                return SkyUnits.Relative;
        }
    }

    private static NishitaSky BuildNishitaSkyModel(SkyData skyData)
    {
        float intensity = skyData.Intensity > 0f ? skyData.Intensity : 1f;
        return new NishitaSky(SkySunDirection(skyData), airDensity: 1f,
                              dustDensity: NishitaSky.DustFromTurbidity(skyData.Turbidity),
                              intensity: intensity,
                              groundAlbedo: ToVector3(skyData.GroundAlbedo),
                              units: ParseSkyUnits(skyData));
    }

    private static AnalyticSky BuildPhysicalSkyModel(SkyData skyData, bool hosekWilkie)
    {
        var albedo = ToVector3(skyData.GroundAlbedo) ?? new Vector3(0.3f);
        float intensity = skyData.Intensity > 0f ? skyData.Intensity : 1f;
        var units = ParseSkyUnits(skyData);
        return hosekWilkie
            ? new HosekWilkieSky(SkySunDirection(skyData), skyData.Turbidity, albedo, intensity, units)
            : new PreethamSky(SkySunDirection(skyData), skyData.Turbidity, albedo, intensity, units);
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
}
