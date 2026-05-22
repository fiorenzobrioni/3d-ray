using System.Numerics;
using RayTracer.Core;
using RayTracer.Rendering.Sky;
using RayTracer.Textures;

namespace RayTracer.Rendering;

/// <summary>
/// Per-ray category. Controls visibility flags and (when a separate
/// background is specified) which radiance source the ray sees.
///
/// <list type="bullet">
///   <item><description><c>Camera</c> — primary camera rays that escape the scene without hitting anything.</description></item>
///   <item><description><c>Diffuse</c> — indirect bounce off a Lambertian / matte / sheen / subsurface lobe.</description></item>
///   <item><description><c>Glossy</c> — indirect bounce off a glossy specular / clearcoat lobe.</description></item>
///   <item><description><c>Transmission</c> — refraction through dielectric / thin-walled transmission.</description></item>
///   <item><description><c>Shadow</c> — NEE direct-light sample (the radiance NEE pulls from the env).</description></item>
/// </list>
/// </summary>
public enum RayCategory
{
    Camera,
    Diffuse,
    Glossy,
    Transmission,
    Shadow,
}

/// <summary>
/// Per-ray-category visibility flags for the sky / environment, matching the
/// Arnold <c>aiSkyDomeLight.visibility.*</c> and Cycles "Ray Visibility"
/// switches. A flag of <c>false</c> means the sky contributes 0 radiance to
/// rays of that category. Sun visibility is governed separately because
/// hiding the sun from camera while keeping it as a light source is a
/// common keying setup.
/// </summary>
public sealed class SkyVisibility
{
    public bool Camera { get; init; } = true;
    public bool Diffuse { get; init; } = true;
    public bool Glossy { get; init; } = true;
    public bool Transmission { get; init; } = true;
    /// <summary>Always true conceptually — flags here mask the sky body for NEE samples too.</summary>
    public bool Shadow { get; init; } = true;
    /// <summary>When false, the analytical sun disc is hidden from camera rays (still lights the scene).</summary>
    public bool SunCamera { get; init; } = true;

    public bool For(RayCategory cat) => cat switch
    {
        RayCategory.Camera => Camera,
        RayCategory.Diffuse => Diffuse,
        RayCategory.Glossy => Glossy,
        RayCategory.Transmission => Transmission,
        RayCategory.Shadow => Shadow,
        _ => true,
    };

    public static readonly SkyVisibility AllOn = new();
}

/// <summary>
/// Sky / environment wrapper. Owns a primary <see cref="ISkyModel"/> that
/// provides illumination and (optionally) a secondary <see cref="ISkyModel"/>
/// shown to the camera as a background plate. Handles world↔sky orientation
/// via a <see cref="Quaternion"/>, per-ray-category visibility flags, and
/// MIS-correct NEE.
///
/// <para><b>Sun handling.</b> When the active sky model exposes an analytical
/// sun via <see cref="ISkyModel.AnalyticalSun"/>, the <see cref="SceneLoader"/>
/// instantiates a paired <see cref="Lights.PhysicalSun"/> alongside the
/// <see cref="Lights.EnvironmentLight"/>. The two lights are independent in
/// the NEE pool, so power-weighted light picking still works, and the sun
/// gets analytic cone sampling while the sky body uses the model's own
/// importance sampler (HDRI CDF, or uniform-sphere fallback).</para>
///
/// <para><b>Backwards compatibility.</b> The class retains the legacy name
/// <c>SkySettings</c> and a Flat / Gradient / HDRI-shaped constructor surface
/// so existing call sites (<see cref="Renderer"/>, <see cref="Lights.EnvironmentLight"/>,
/// <see cref="SceneLoader"/>) keep compiling. New scenes should use the
/// richer <see cref="SkyEnvironmentBuilder"/> ctor path.</para>
/// </summary>
public class SkySettings
{
    // ── Mode flags (retained for legacy logging / queries) ──────────────────
    public enum SkyMode { Flat, Gradient, Hdri, Physical }
    public SkyMode Mode { get; }
    public bool IsGradient => Mode == SkyMode.Gradient;
    public bool IsHdri => Mode == SkyMode.Hdri;
    public bool IsPhysical => Mode == SkyMode.Physical;

    // ── Lighting + (optional) background model ──────────────────────────────
    private readonly ISkyModel _lighting;
    private readonly ISkyModel? _background;   // null = same as lighting
    private readonly SkyVisibility _visibility;
    private readonly Quaternion _worldToSky;
    private readonly Quaternion _skyToWorld;

    // ── Legacy gradient / flat exposed properties ──────────────────────────
    public Vector3 FlatColor { get; }
    public Vector3 ZenithColor { get; }
    public Vector3 HorizonColor { get; }
    public Vector3 GroundColor { get; }
    public bool HasSun => _lighting.HasAnalyticalSun;
    public Vector3 SunDirection => _lighting.AnalyticalSun.Direction;
    public Vector3 SunColor => _lighting.AnalyticalSun.Radiance;

    // ─────────────────────────────────────────────────────────────────────────
    //  Constructors — legacy convenience (flat / gradient / hdri)
    // ─────────────────────────────────────────────────────────────────────────

    public SkySettings(Vector3 flatColor)
        : this(new FlatSky(flatColor),
               background: null,
               visibility: SkyVisibility.AllOn,
               orientation: Quaternion.Identity,
               mode: SkyMode.Flat)
    {
        FlatColor = flatColor;
        ZenithColor = flatColor;
        HorizonColor = flatColor;
        GroundColor = flatColor;
    }

    public SkySettings(Vector3 zenithColor, Vector3 horizonColor, Vector3 groundColor,
                       Vector3? sunDirection = null, Vector3? sunColor = null,
                       float sunIntensity = 10f, float sunSizeDeg = 3f, float sunFalloff = 32f)
        : this(BuildGradient(zenithColor, horizonColor, groundColor,
                             sunDirection, sunColor, sunIntensity, sunSizeDeg),
               background: null,
               visibility: SkyVisibility.AllOn,
               orientation: Quaternion.Identity,
               mode: SkyMode.Gradient)
    {
        ZenithColor = zenithColor;
        HorizonColor = horizonColor;
        GroundColor = groundColor;
        FlatColor = horizonColor;
        _ = sunFalloff; // retained for back-compat signature only
    }

    public SkySettings(EnvironmentMap envMap)
        : this(new HdriSky(envMap),
               background: null,
               visibility: SkyVisibility.AllOn,
               orientation: Quaternion.Identity,
               mode: SkyMode.Hdri)
    {
        FlatColor = new Vector3(0.5f);
        ZenithColor = FlatColor;
        HorizonColor = FlatColor;
        GroundColor = FlatColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Primary constructor — pro-grade
    // ─────────────────────────────────────────────────────────────────────────

    public SkySettings(ISkyModel lighting,
                       ISkyModel? background = null,
                       SkyVisibility? visibility = null,
                       Quaternion? orientation = null,
                       SkyMode? mode = null)
    {
        _lighting = lighting;
        _background = background;
        _visibility = visibility ?? SkyVisibility.AllOn;
        _worldToSky = orientation ?? Quaternion.Identity;
        _skyToWorld = Quaternion.Inverse(_worldToSky);

        Mode = mode ?? (lighting switch
        {
            FlatSky      => SkyMode.Flat,
            GradientSky  => SkyMode.Gradient,
            HdriSky      => SkyMode.Hdri,
            PreethamSky  => SkyMode.Physical,
            NishitaSky   => SkyMode.Physical,
            _            => SkyMode.Flat,
        });

        // Legacy fields used by some callers — default to mid-gray when the
        // model doesn't expose a flat colour.
        FlatColor = lighting is FlatSky f ? f.Color : new Vector3(0.5f);
        if (lighting is GradientSky g)
        {
            ZenithColor = g.ZenithColor;
            HorizonColor = g.HorizonColor;
            GroundColor = g.GroundColor;
        }
        else
        {
            ZenithColor = FlatColor;
            HorizonColor = FlatColor;
            GroundColor = FlatColor;
        }
    }

    private static GradientSky BuildGradient(Vector3 z, Vector3 h, Vector3 g,
        Vector3? sunDir, Vector3? sunCol, float sunInt, float sunSizeDeg)
    {
        if (sunDir.HasValue)
        {
            // Legacy convention: YAML `direction` is the direction TOWARDS the sun.
            // (The historic SkySettings flipped its sign internally; we restore the
            // straightforward convention here. Scenes that supplied direction
            // semantics consistent with the old behaviour will now show the sun on
            // the opposite side — a deliberate beta-period correction.)
            var dirToSun = Vector3.Normalize(sunDir.Value);
            var col = sunCol ?? Vector3.One;
            // Treat sunSizeDeg as full angular diameter — half-angle = size / 2.
            return new GradientSky(z, h, g, dirToSun, col * sunInt, sunSizeDeg * 0.5f);
        }
        return new GradientSky(z, h, g);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public sampling API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Legacy entry point — equivalent to <see cref="Sample(Ray, RayCategory, bool)"/> with <see cref="RayCategory.Camera"/> and sun visible.</summary>
    public Vector3 Sample(Ray ray) => Sample(ray, RayCategory.Camera, includeAnalyticalSun: true);

    /// <summary>
    /// Linear HDR radiance for a ray that escaped the scene.
    ///
    /// <para><paramref name="cat"/> drives visibility-flag masking and selects
    /// between lighting and background models for camera rays.
    /// <paramref name="includeAnalyticalSun"/> controls whether the sky model's
    /// analytical sun disc is added — set <c>true</c> for camera and delta
    /// (mirror/refraction) bounces, <c>false</c> for non-delta indirect rays
    /// where a paired <see cref="Lights.PhysicalSun"/> handles the sun via
    /// NEE (preventing double-counting).</para>
    ///
    /// <para>When the analytical sun is hidden from camera rays via
    /// <see cref="SkyVisibility.SunCamera"/>, the disc is forced off even if
    /// <paramref name="includeAnalyticalSun"/> is true.</para>
    /// </summary>
    public Vector3 Sample(Ray ray, RayCategory cat, bool includeAnalyticalSun = true)
    {
        if (!_visibility.For(cat)) return Vector3.Zero;

        Vector3 worldDir = Vector3.Normalize(ray.Direction);
        Vector3 skyDir = Vector3.Transform(worldDir, _worldToSky);

        ISkyModel source = (cat == RayCategory.Camera && _background != null) ? _background : _lighting;

        Vector3 body = source.EvaluateRadiance(skyDir);

        if (source.HasAnalyticalSun && includeAnalyticalSun)
        {
            // Camera-visible sun gate.
            bool showSun = cat != RayCategory.Camera || _visibility.SunCamera;
            if (showSun)
            {
                var (sunDir, sunRad, cosHalf, limb) = source.AnalyticalSun;
                float cosAngle = Vector3.Dot(skyDir, sunDir);
                if (cosAngle >= cosHalf)
                {
                    Vector3 add = sunRad;
                    if (limb)
                    {
                        const float u1 = 0.6f;
                        add *= MathF.Max(0f, 1f - u1 * (1f - cosAngle));
                    }
                    body += add;
                }
            }
        }
        return body;
    }

    /// <summary>True when the environment is meaningful as a direct light source.</summary>
    public bool CanSampleDirectly =>
        _lighting.HasImportanceSampling || _lighting.HasAnalyticalSun;

    /// <summary>
    /// Deterministic spherical mean radiance, used by
    /// <see cref="Lights.EnvironmentLight.ApproximatePower"/>. No PRNG.
    /// </summary>
    public float EstimatedAverageLuminance
    {
        get
        {
            float body = _lighting.EstimatedAverageLuminance;
            if (_lighting.HasAnalyticalSun)
            {
                var (_, rad, cosHalf, _) = _lighting.AnalyticalSun;
                float omega = 2f * MathF.PI * (1f - cosHalf);
                body += MathUtils.Luminance(rad) * omega / (4f * MathF.PI);
            }
            return body;
        }
    }

    /// <summary>
    /// Samples a direction for NEE. Returns sky-body samples — the sun is
    /// handled separately by a paired <see cref="Lights.PhysicalSun"/> in the
    /// light list, so we do not return cone samples from this routine.
    /// </summary>
    public (Vector3 Direction, Vector3 Color, float Pdf) SampleDirectly()
    {
        if (_lighting.HasImportanceSampling)
        {
            var (skyDir, L, pdf) = _lighting.ImportanceSample();
            return (Vector3.Transform(skyDir, _skyToWorld), L, pdf);
        }
        // Fallback — uniform sphere
        Vector3 rd = MathUtils.RandomUnitVector();
        var skyDir2 = Vector3.Transform(rd, _worldToSky);
        return (rd, _lighting.EvaluateRadiance(skyDir2), 1f / (4f * MathF.PI));
    }

    /// <summary>Solid-angle PDF for NEE MIS balance heuristic.</summary>
    public float PdfSolidAngle(Vector3 worldDir)
    {
        if (!_lighting.HasImportanceSampling) return 0f;
        Vector3 skyDir = Vector3.Transform(Vector3.Normalize(worldDir), _worldToSky);
        return _lighting.Pdf(skyDir);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Sun handover — consumed by SceneLoader to register PhysicalSun
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the underlying sky model exposes an analytical sun, returns its
    /// parameters in <b>world space</b> (orientation applied). The
    /// SceneLoader registers a paired <see cref="Lights.PhysicalSun"/> from
    /// these values. Returns <c>null</c> when no analytical sun exists.
    /// </summary>
    public (Vector3 DirectionWorld, Vector3 Radiance, float HalfAngleDeg, bool LimbDarkening)? GetAnalyticalSun()
    {
        if (!_lighting.HasAnalyticalSun) return null;
        var (skyDir, rad, cosHalf, limb) = _lighting.AnalyticalSun;
        Vector3 worldDir = Vector3.Transform(skyDir, _skyToWorld);
        float halfDeg = MathUtils.RadiansToDegrees(MathF.Acos(Math.Clamp(cosHalf, -1f, 1f)));
        return (Vector3.Normalize(worldDir), rad, halfDeg, limb);
    }

    /// <summary>Exposes the underlying lighting model (read-only).</summary>
    public ISkyModel LightingModel => _lighting;
}
