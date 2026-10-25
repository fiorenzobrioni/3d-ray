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
    // =========================================================================
    //  Ground
    // =========================================================================

    /// <summary>
    /// Builds the <c>world.ground</c> primitive: an infinite plane (default),
    /// a quad or a disk through <c>point</c> with <c>normal</c>, its material
    /// (by id, or a Lambertian of the sky's ground albedo), the optional UV
    /// transform and the per-category visibility.
    /// </summary>
    private static IHittable? BuildGround(GroundData g, SkyData? sky, Dictionary<string, IMaterial> materials)
    {
        IMaterial material = ResolveGroundMaterial(g, sky, materials);

        Vector3 anchor = ToVector3(g.Point) ?? Vector3.Zero;
        Vector3 normal = ToVector3(g.Normal) ?? Vector3.UnitY;
        if (normal.LengthSquared() < 1e-12f)
        {
            Warn("Ground 'normal' is the zero vector. Falling back to (0, 1, 0).");
            normal = Vector3.UnitY;
        }
        normal = Vector3.Normalize(normal);

        IHittable hittable = g.Type switch
        {
            "quad" => BuildQuadGround(anchor, normal, g.Size, material),
            "disk" => new Disk(anchor, normal, g.Size, material),
            _      => new InfinitePlane(anchor, normal, material),
        };

        if (NeedsUvTransform(g))
        {
            float su = g.UvScale  is { Count: >= 1 } us ? us[0] : 1f;
            float sv = g.UvScale  is { Count: >= 2 } vs ? vs[1] : su;
            float ou = g.UvOffset is { Count: >= 1 } uo ? uo[0] : 0f;
            float ov = g.UvOffset is { Count: >= 2 } vo ? vo[1] : 0f;
            hittable = new UvTransformedHittable(hittable, su, sv, ou, ov, g.UvRotation);
        }

        hittable = ApplyVisibility(hittable, g.Visibility);
        // Stable seed for procedural textures.
        hittable.Seed = StableSeed(0, "ground", g.Type);
        return hittable;
    }

    /// <summary>
    /// A centred <see cref="Quad"/> of half-size <paramref name="size"/> on the
    /// plane through <paramref name="anchor"/>: U along world +X when the normal
    /// is close to +Y (so textures match the infinite plane), else a stable
    /// basis around the normal.
    /// </summary>
    private static IHittable BuildQuadGround(Vector3 anchor, Vector3 normal, float size, IMaterial material)
    {
        if (size <= 0f) size = 50f;
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
        Vector3 q = anchor - u * size - v * size;
        return new Quad(q, u * (2f * size), v * (2f * size), material);
    }

    /// <summary>
    /// The ground's material: the one named by <c>material</c>, else a
    /// Lambertian of the sky's <c>ground_albedo</c> / <c>ground_color</c> (a
    /// floor coherent with the sky), else grey 0.5.
    /// </summary>
    private static IMaterial ResolveGroundMaterial(GroundData g, SkyData? sky, Dictionary<string, IMaterial> materials)
    {
        if (!string.IsNullOrWhiteSpace(g.Material))
            return GetMaterial(materials, g.Material);
        Vector3? skyAlbedo = ToVector3(sky?.GroundAlbedo) ?? ToVector3(sky?.GroundColor);
        return new Lambertian(skyAlbedo ?? new Vector3(0.5f));
    }

    private static bool NeedsUvTransform(GroundData g)
    {
        if (g.UvScale  != null && g.UvScale.Count  > 0) return true;
        if (g.UvOffset != null && g.UvOffset.Count > 0) return true;
        if (MathF.Abs(g.UvRotation) > 1e-6f) return true;
        return false;
    }

    // =========================================================================
    //  Sky
    // =========================================================================

    /// <summary>
    /// The environment from <c>world.sky</c>: <c>flat</c>, <c>gradient</c>,
    /// <c>hdri</c> or a physical sky (<c>hosek_wilkie</c>, <c>preetham</c>,
    /// <c>nishita</c>, in nits). No sky: black.
    /// </summary>
    private static SkySettings BuildSkySettings(SkyData? skyData, string sceneDir)
    {
        if (skyData == null)
            return new SkySettings(Vector3.Zero);

        ISkyModel lightingModel = BuildSkyModel(skyData, sceneDir);
        if (lightingModel is AnalyticSky analytic)
        {
            // What the sky gives a horizontal plane on the ground: the scale to expose for.
            Info($"Sky light:   {analytic.SkyIlluminance * analytic.Intensity:0.###} lux on the ground, " +
                 $"{analytic.SunIlluminance * analytic.Intensity:0.###} of it from the sun");
        }
        ISkyModel? backgroundModel = skyData.Background != null ? BuildSkyModel(skyData.Background, sceneDir) : null;

        var visibility = BuildVisibility(skyData);
        // `rotate` turns the sky in the world as it would an entity; the sky
        // settings take the opposite, world to sky.
        var orientation = Quaternion.Conjugate(RotationQuaternion(skyData.Rotate, skyData.RotateOrder));

        return new SkySettings(lightingModel, backgroundModel, visibility, orientation);
    }

    private static ISkyModel BuildSkyModel(SkyData skyData, string sceneDir) => skyData.Type switch
    {
        "gradient"     => BuildGradientSkyModel(skyData),
        "hdri"         => BuildHdriSkyModel(skyData, sceneDir),
        "preetham"     => BuildPhysicalSkyModel(skyData, hosekWilkie: false),
        "hosek_wilkie" => BuildPhysicalSkyModel(skyData, hosekWilkie: true),
        "nishita"      => BuildNishitaSkyModel(skyData),
        // flat: radiance 1 by default, as a constant environment in Mitsuba.
        _              => new FlatSky((ToVector3(skyData.Color) ?? Vector3.One) * SkyScale(skyData)),
    };

    /// <summary>The sky's <c>exposure</c> as a multiplier.</summary>
    private static float SkyScale(SkyData skyData) => MathF.Pow(2f, skyData.Exposure);

    private static SkyVisibility BuildVisibility(SkyData skyData)
    {
        var v = skyData.Visibility;
        bool sunCam = skyData.Sun?.Visibility?.Camera ?? true;
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

    private static GradientSky BuildGradientSkyModel(SkyData skyData)
    {
        float scale = SkyScale(skyData);
        var zenith  = (ToVector3(skyData.ZenithColor)  ?? new Vector3(0.10f, 0.30f, 0.80f)) * scale;
        var horizon = (ToVector3(skyData.HorizonColor) ?? new Vector3(0.70f, 0.85f, 1.00f)) * scale;
        var ground  = (ToVector3(skyData.GroundColor)  ?? new Vector3(0.30f, 0.25f, 0.20f)) * scale;

        if (skyData.Sun == null || ToVector3(skyData.Sun.Direction) is not Vector3 sunDir)
            return new GradientSky(zenith, horizon, ground);

        // The disc's radiance from its illuminance on a surface facing it:
        // E = L π sin²θ for a uniform disc of half-angle θ.
        float halfAngle = skyData.Sun.AngularRadius ?? 1.5f;
        if (!(halfAngle > 0f))
        {
            Warn($"Sun 'angular_radius' must be > 0 (got {halfAngle}); using 1.5°.");
            halfAngle = 1.5f;
        }
        if (skyData.Sun.Illuminance == null)
            Warn("Gradient sky sun without 'illuminance' (lux): using 1.");
        float sinTheta = MathF.Sin(MathUtils.DegreesToRadians(halfAngle));
        float radiance = (skyData.Sun.Illuminance ?? 1f) / (MathF.PI * sinTheta * sinTheta) * scale;
        var tint = LightUnits.UnitTint(ToVector3(skyData.Sun.Color) ?? Vector3.One, null);
        return new GradientSky(zenith, horizon, ground,
            sunDirToSun: sunDir,
            sunRadiance: tint * radiance,
            sunHalfAngleDeg: halfAngle);
    }

    private static Vector3 SkySunDirection(SkyData skyData)
        => skyData.Sun?.Direction is { Count: >= 3 }
            ? Vector3.Normalize(ToVector3(skyData.Sun.Direction) ?? Vector3.UnitY)
            : Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f)); // noon-ish, slightly south

    private static NishitaSky BuildNishitaSkyModel(SkyData skyData)
        => new(SkySunDirection(skyData), airDensity: 1f,
               dustDensity: NishitaSky.DustFromTurbidity(skyData.Turbidity),
               intensity: SkyScale(skyData),
               groundAlbedo: ToVector3(skyData.GroundAlbedo));

    private static AnalyticSky BuildPhysicalSkyModel(SkyData skyData, bool hosekWilkie)
    {
        var albedo = ToVector3(skyData.GroundAlbedo) ?? new Vector3(0.3f);
        return hosekWilkie
            ? new HosekWilkieSky(SkySunDirection(skyData), skyData.Turbidity, albedo, SkyScale(skyData))
            : new PreethamSky(SkySunDirection(skyData), skyData.Turbidity, albedo, SkyScale(skyData));
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

            float scale = SkyScale(skyData);
            EnvironmentMap envMap;
            Vector3? extractedSunDir = null;
            Vector3? extractedSunRad = null;
            float extractedHalfDeg = 0.265f;

            if (skyData.Sun?.ExtractFromHdri == true)
            {
                var probeMap = new EnvironmentMap(pixels, width, height, scale);
                var extracted = HdriSunExtractor.Extract(probeMap, skyData.Sun.ExtractThreshold);
                if (extracted != null)
                {
                    extractedSunDir = extracted.Direction;
                    extractedSunRad = extracted.Radiance;
                    extractedHalfDeg = extracted.HalfAngleDeg;
                    envMap = new EnvironmentMap(extracted.InpaintedPixels, width, height, scale);
                    Info($"HDRI sun extracted: half-angle {extractedHalfDeg:F2}°");
                }
                else
                {
                    Info("HDRI sun extraction: no peak above threshold; using HDRI as-is.");
                    envMap = probeMap;
                }
            }
            else
            {
                envMap = new EnvironmentMap(pixels, width, height, scale);
            }

            Info($"HDRI:        {skyData.Path} ({width}×{height})");
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
