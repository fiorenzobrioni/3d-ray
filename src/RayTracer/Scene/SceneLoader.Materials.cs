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
    /// Metal: a measured <c>preset</c> or an explicit complex IOR
    /// (<c>eta</c> + <c>k</c>) gives the exact conductor Fresnel; otherwise
    /// the F82 model from <c>color</c> (normal incidence) and
    /// <c>edge_color</c> (grazing tint).
    /// </summary>
    private static Metal CreateMetal(MaterialData m, ITexture albedo)
    {
        ConductorIor? ior = null;
        if (m.Preset != null)
        {
            if (ConductorIor.Presets.TryGetValue(m.Preset, out var preset))
                ior = preset;
            else
                Warn($"Material '{m.Id}': unknown metal preset '{m.Preset}' " +
                     $"(known: {string.Join(", ", ConductorIor.Presets.Keys)}); using 'color'.");
        }
        else if (m.Eta != null || m.K != null)
        {
            var eta = ToVector3(m.Eta);
            var k = ToVector3(m.K);
            if (eta is Vector3 e && k is Vector3 kk && e.X > 0f && e.Y > 0f && e.Z > 0f)
                ior = new ConductorIor(e, Vector3.Max(kk, Vector3.Zero));
            else
                Warn($"Material '{m.Id}': 'eta' and 'k' need three components each, eta > 0; using 'color'.");
        }
        if (ior != null && (m.Color != null || m.Texture != null || m.EdgeColor != null))
            Info($"Material '{m.Id}': the complex IOR sets the metal's colour; 'color' and 'edge_color' are ignored.");
        ITexture? edge = ToVector3(m.EdgeColor) is Vector3 ec ? new SolidColor(ec) : null;
        return new Metal(albedo, m.Fuzz ?? m.Roughness ?? 0f, edge, ior, m.MultipleScattering);
    }

    private static bool IsDisney(MaterialData m) => m.Type?.ToLowerInvariant() is "disney" or "disney_bsdf" or "pbr";

    /// <summary>The <c>disney</c> type, translated onto OpenPBR (<see cref="DisneyMaterial"/>).</summary>
    private static IMaterial CreateDisney(MaterialData m, ITexture albedo, string sceneDir)
    {
        bool sss = m.SubsurfaceRadius is { } r && r.Any(v => v > 0f) && !m.ThinWalled;
        bool diffTrans = m.DiffTrans > 0f || m.DiffTransTexture != null;
        if (diffTrans && !m.ThinWalled)
            Info($"Material '{m.Id}': 'diff_trans' makes the surface thin-walled (a sheet with no interior).");
        return DisneyMaterial.Create(
            albedo,
            metallic:            DisneyParam(m.Metallic,            m.MetallicTexture,            sceneDir),
            roughness:           DisneyParam(m.Roughness ?? 0.5f,   m.RoughnessTexture,           sceneDir),
            specular:            DisneyParam(m.Specular,            m.SpecularTexture,            sceneDir),
            specularTint:        DisneyParam(m.SpecularTint,        m.SpecularTintTexture,        sceneDir),
            sheen:               DisneyParam(m.Sheen,               m.SheenTexture,               sceneDir),
            sheenTint:           DisneyParam(m.SheenTint,           m.SheenTintTexture,           sceneDir),
            sheenRoughness:      DisneyParam(m.SheenRoughness,      m.SheenRoughnessTexture,      sceneDir),
            clearcoat:           DisneyParam(m.Clearcoat,           m.ClearcoatTexture,           sceneDir),
            clearcoatGloss:      DisneyParam(m.ClearcoatGloss,      m.ClearcoatGlossTexture,      sceneDir),
            specTrans:           DisneyParam(m.SpecTrans ?? 0f,     m.SpecTransTexture,           sceneDir),
            ior:                 DisneyParam(m.DisneyIor,           m.IorTexture,                 sceneDir),
            anisotropic:         DisneyParam(m.Anisotropic,         m.AnisotropicTexture,         sceneDir),
            anisotropicRotation: DisneyParam(m.AnisotropicRotation, m.AnisotropicRotationTexture, sceneDir),
            transmissionColor:   DisneyColorParam(m.TransmissionColor, m.TransmissionColorTexture, sceneDir),
            transmissionDepth:   DisneyParam(m.TransmissionDepth,   m.TransmissionDepthTexture,   sceneDir),
            diffTrans:           diffTrans ? DisneyParam(m.DiffTrans, m.DiffTransTexture, sceneDir) : null,
            thinWalled:          m.ThinWalled,
            coatIor:             DisneyParam(m.CoatIor ?? 1.5f,     m.CoatIorTexture,             sceneDir),
            // coat_roughness only when written; otherwise clearcoat_gloss sets it.
            coatRoughness:       (m.CoatRoughness >= 0f || m.CoatRoughnessTexture != null)
                                    ? DisneyParam(MathF.Max(m.CoatRoughness, 0f), m.CoatRoughnessTexture, sceneDir)
                                    : null,
            thinFilmThickness:   DisneyParam(m.ThinFilmThickness ?? 0f, m.ThinFilmThicknessTexture, sceneDir),
            thinFilmIor:         DisneyParam(m.ThinFilmIor ?? 1.5f, m.ThinFilmIorTexture,         sceneDir),
            subsurfaceMedium:    sss,
            coatNormal:          m.CoatNormalMap != null ? LoadNormalMap(m.CoatNormalMap, sceneDir) : null);
    }

    // ── OpenPBR Surface (PIANO-REVIEW.md, Phase 5) ──────────────────────────

    private static bool IsOpenPbr(MaterialData m) =>
        string.Equals(m.Type, "openpbr", StringComparison.OrdinalIgnoreCase);

    private static FloatTexture PbrFloat(MaterialData m, MaterialParam? p, float def, string key, string sceneDir)
    {
        if (p == null) return def;
        if (p.Texture != null) return new FloatTexture(CreateTexture(p.Texture, sceneDir, TextureRole.Data));
        if (p.Values is { Count: > 0 } v)
        {
            if (v.Count > 1) Warn($"Material '{m.Id}': '{key}' takes one number; using the first.");
            return v[0];
        }
        return def;
    }

    private static ITexture PbrColor(MaterialData m, MaterialParam? p, Vector3 def, string key, string sceneDir)
    {
        if (p == null) return new SolidColor(def);
        if (p.Texture != null) return CreateTexture(p.Texture, sceneDir);
        return new SolidColor(PbrVector(m, p.Values, def, key));
    }

    /// <summary>A colour written as [r, g, b] or as one grey number.</summary>
    private static Vector3 PbrVector(MaterialData m, List<float>? v, Vector3 def, string key)
    {
        if (v == null || v.Count == 0) return def;
        if (v.Count == 1) return new Vector3(v[0]);
        if (v.Count == 3) return new Vector3(v[0], v[1], v[2]);
        Warn($"Material '{m.Id}': '{key}' needs one or three numbers; using the default.");
        return def;
    }

    /// <summary>The constant value of a scalar input, or null when textured.</summary>
    private static float? PbrConstant(MaterialParam? p, float def) =>
        p == null ? def : p.Texture != null ? null : p.Values is { Count: > 0 } v ? v[0] : def;

    private static IMaterial CreateOpenPbr(MaterialData m, string sceneDir)
    {
        // base_color falls back on the common 'color' / 'texture' keys.
        ITexture baseColor = m.BaseColor != null
            ? PbrColor(m, m.BaseColor, new Vector3(0.8f), "base_color", sceneDir)
            : m.Texture != null ? CreateTexture(m.Texture, sceneDir)
            : new SolidColor(PbrVector(m, m.Color, new Vector3(0.8f), "color"));

        // subsurface_radius: one number is the mean free path, three are a
        // per-channel mean free path (radius 1 times that scale).
        float radius = 1f;
        Vector3 radiusScale = new(1f, 0.5f, 0.25f);
        if (m.SubsurfaceRadius is { Count: 1 } r1) radius = r1[0];
        else if (m.SubsurfaceRadius is { Count: 3 } r3) radiusScale = new Vector3(r3[0], r3[1], r3[2]);
        if (m.SubsurfaceRadiusScale != null)
            radiusScale = PbrVector(m, m.SubsurfaceRadiusScale.Values, radiusScale, "subsurface_radius_scale");

        var p = new OpenPbrParameters
        {
            BaseWeight = PbrFloat(m, m.BaseWeight, 1f, "base_weight", sceneDir),
            BaseColor = baseColor,
            BaseMetalness = PbrFloat(m, m.BaseMetalness, 0f, "base_metalness", sceneDir),
            BaseDiffuseRoughness = PbrFloat(m, m.BaseDiffuseRoughness, 0f, "base_diffuse_roughness", sceneDir),
            SpecularWeight = PbrFloat(m, m.SpecularWeight, 1f, "specular_weight", sceneDir),
            SpecularColor = PbrColor(m, m.SpecularColor, Vector3.One, "specular_color", sceneDir),
            SpecularRoughness = PbrFloat(m, m.SpecularRoughness, 0.3f, "specular_roughness", sceneDir),
            SpecularRoughnessAnisotropy = PbrFloat(m, m.SpecularRoughnessAnisotropy, 0f, "specular_roughness_anisotropy", sceneDir),
            SpecularIor = PbrFloat(m, m.SpecularIor, 1.5f, "specular_ior", sceneDir),
            TransmissionWeight = PbrFloat(m, m.TransmissionWeight, 0f, "transmission_weight", sceneDir),
            TransmissionColor = m.TransmissionColorTexture != null ? CreateTexture(m.TransmissionColorTexture, sceneDir)
                              : new SolidColor(PbrVector(m, m.TransmissionColor, Vector3.One, "transmission_color")),
            TransmissionDepth = DisneyParam(m.TransmissionDepth, m.TransmissionDepthTexture, sceneDir),
            TransmissionScatter = PbrColor(m, m.TransmissionScatter, Vector3.Zero, "transmission_scatter", sceneDir),
            TransmissionScatterAnisotropy = PbrFloat(m, m.TransmissionScatterAnisotropy, 0f, "transmission_scatter_anisotropy", sceneDir),
            SubsurfaceWeight = PbrFloat(m, m.SubsurfaceWeight, 0f, "subsurface_weight", sceneDir),
            SubsurfaceColor = m.SubsurfaceColorTexture != null ? CreateTexture(m.SubsurfaceColorTexture, sceneDir)
                            : new SolidColor(PbrVector(m, m.SubsurfaceColor, new Vector3(0.8f), "subsurface_color")),
            SubsurfaceRadius = radius,
            SubsurfaceRadiusScale = new SolidColor(radiusScale),
            SubsurfaceScatterAnisotropy = PbrFloat(m, m.SubsurfaceScatterAnisotropy, 0f, "subsurface_scatter_anisotropy", sceneDir),
            CoatWeight = PbrFloat(m, m.CoatWeight, 0f, "coat_weight", sceneDir),
            CoatColor = PbrColor(m, m.CoatColor, Vector3.One, "coat_color", sceneDir),
            CoatRoughness = DisneyParam(MathF.Max(m.CoatRoughness, 0f), m.CoatRoughnessTexture, sceneDir),
            CoatRoughnessAnisotropy = PbrFloat(m, m.CoatRoughnessAnisotropy, 0f, "coat_roughness_anisotropy", sceneDir),
            CoatIor = DisneyParam(m.CoatIor ?? 1.6f, m.CoatIorTexture, sceneDir),
            CoatDarkening = PbrFloat(m, m.CoatDarkening, 1f, "coat_darkening", sceneDir),
            FuzzWeight = PbrFloat(m, m.FuzzWeight, 0f, "fuzz_weight", sceneDir),
            FuzzColor = PbrColor(m, m.FuzzColor, Vector3.One, "fuzz_color", sceneDir),
            FuzzRoughness = PbrFloat(m, m.FuzzRoughness, 0.5f, "fuzz_roughness", sceneDir),
            ThinFilmWeight = PbrFloat(m, m.ThinFilmWeight, 0f, "thin_film_weight", sceneDir),
            ThinFilmThickness = DisneyParam(m.ThinFilmThickness ?? 0.5f, m.ThinFilmThicknessTexture, sceneDir),
            ThinFilmIor = DisneyParam(m.ThinFilmIor ?? 1.4f, m.ThinFilmIorTexture, sceneDir),
            EmissionLuminance = PbrFloat(m, m.EmissionLuminance, 0f, "emission_luminance", sceneDir),
            EmissionColor = PbrColor(m, m.EmissionColor, Vector3.One, "emission_color", sceneDir),
            GeometryOpacity = PbrFloat(m, m.GeometryOpacity, 1f, "geometry_opacity", sceneDir),
            ThinWalled = m.GeometryThinWalled ?? m.ThinWalled,
            CoatNormal = m.CoatNormalMap != null ? LoadNormalMap(m.CoatNormalMap, sceneDir) : null,
        };
        return new OpenPbr(p) { InteriorIsMedium = BuildOpenPbrMedium(m) != null };
    }

    /// <summary>
    /// The participating medium inside an OpenPBR object, bound to every entity
    /// that uses the material (an explicit <c>interior_medium</c> wins):
    /// <list type="bullet">
    /// <item>subsurface: extinction 1 / (radius · radius_scale) and the
    /// single-scattering albedo that makes the multiple-scattering albedo
    /// <c>subsurface_color</c> (the OpenPBR inversion, with the phase
    /// function's g);</item>
    /// <item>scattering transmission (<c>transmission_scatter</c> &gt; 0 with a
    /// depth): μ_t = −ln(color) / depth, μ_s = scatter / depth, μ_a shifted to be
    /// non-negative.</item>
    /// </list>
    /// Textured colours give a homogeneous medium of their default value.
    /// </summary>
    private static IMedium? BuildOpenPbrMedium(MaterialData m)
    {
        if (m.GeometryThinWalled ?? m.ThinWalled) return null;
        float sw = PbrConstant(m.SubsurfaceWeight, 0f) ?? 1f;
        if (sw > 0f)
        {
            float radius = m.SubsurfaceRadius is { Count: 1 } r1 ? r1[0] : 1f;
            Vector3 scale = m.SubsurfaceRadius is { Count: 3 } r3 ? new Vector3(r3[0], r3[1], r3[2]) : new Vector3(1f, 0.5f, 0.25f);
            if (m.SubsurfaceRadiusScale?.Values is { } rs) scale = PbrVector(m, rs, scale, "subsurface_radius_scale");
            Vector3 mfp = Vector3.Max(radius * scale, new Vector3(1e-4f));
            Vector3 color = Vector3.Clamp(PbrVector(m, m.SubsurfaceColor, new Vector3(0.8f), "subsurface_color"),
                                          Vector3.Zero, Vector3.One);
            float g = Math.Clamp(PbrConstant(m.SubsurfaceScatterAnisotropy, 0f) ?? 0f, -0.999f, 0.999f);
            return SubsurfaceMedium(m.Id, color, mfp, g);
        }

        float tw = PbrConstant(m.TransmissionWeight, 0f) ?? 1f;
        float depth = m.TransmissionDepth;
        Vector3 scatter = m.TransmissionScatter?.Values is { } sv ? PbrVector(m, sv, Vector3.Zero, "transmission_scatter") : Vector3.Zero;
        if (tw <= 0f || depth <= 0f || scatter == Vector3.Zero) return null;
        Vector3 tc = Vector3.Clamp(PbrVector(m, m.TransmissionColor, Vector3.One, "transmission_color"), new Vector3(1e-4f), Vector3.One);
        Vector3 mt = new Vector3(-MathF.Log(tc.X), -MathF.Log(tc.Y), -MathF.Log(tc.Z)) / depth;
        Vector3 ms = Vector3.Max(scatter, Vector3.Zero) / depth;
        Vector3 ma = mt - ms;
        float shift = MathF.Min(0f, MathF.Min(ma.X, MathF.Min(ma.Y, ma.Z)));
        ma -= new Vector3(shift);
        float gt = Math.Clamp(PbrConstant(m.TransmissionScatterAnisotropy, 0f) ?? 0f, -0.999f, 0.999f);
        IPhaseFunction tphase = MathF.Abs(gt) < 1e-4f ? new IsotropicPhase() : new HenyeyGreensteinPhase(gt);
        return new HomogeneousMedium(ma, ms, tphase);
    }

    private static IMaterial CreateMaterial(MaterialData m, string sceneDir)
    {
        ITexture albedo = m.Texture != null
            ? CreateTexture(m.Texture, sceneDir)
            : new SolidColor(ToVector3(m.Color) ?? new Vector3(0.5f));

        IMaterial material = m.Type?.ToLowerInvariant() switch
        {
            "lambertian" => new Lambertian(albedo),
            "metal"      => CreateMetal(m, albedo),
            // Clear glass unless a colour is written (the shared default, grey
            // 0.5, halved the light at every crossing of the interface).
            "dielectric" => new Dielectric(m.RefractionIndex,
                                m.Color != null || m.Texture != null ? albedo : new SolidColor(Vector3.One),
                                m.Roughness ?? 0f),
            // White unless a colour is written: 'intensity' is the radiance.
            "emissive"   => CreateEmissive(m, albedo),
            "disney" or "disney_bsdf" or "pbr"
                         => CreateDisney(m, albedo, sceneDir),
            "openpbr"    => CreateOpenPbr(m, sceneDir),
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
                    case OpenPbr     opb: opb.NormalMap = normalMap; break;
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
                    case OpenPbr     opb: opb.BumpMap = bumpMap; break;
                    case MixMaterial mix: mix.BumpMap = bumpMap; break;
                }
            }
        }

        // ── Coat normal map (Disney only — exclusive to the clearcoat lobe) ─

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
        if (IsDisney(m)
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
                    case OpenPbr     opb: opb.Displacement = leafDisp; break;
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
        ITexture? mask = m.Mask != null ? CreateTexture(m.Mask, sceneDir, TextureRole.Data) : null;
 
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
            ITexture inner = CreateTexture(bm.Texture, sceneDir, TextureRole.Data);
            return new BumpMapTexture(inner, bm.Strength, bm.Scale);
        }
        catch (Exception ex)
        {
            Warn($"Failed to build bump map: {ex.Message}. Skipping.");
            return null;
        }
    }

    /// <summary>
    /// Applies the auto-defaults required to wire material-embedded SSS
    /// through the Disney BSDF transmission lobe. Triggered when the
    /// material declares <c>subsurface_radius</c> with at least one
    /// positive channel. Defaults only fill in values the user did NOT
    /// author explicitly (detected by comparing to the C# field default):
    /// <list type="bullet">
    ///   <item><c>spec_trans</c>: unauthored (null) → 1.0 (transmission lobe
    ///         must fire to emit MediumTransition.Enter). An explicit value,
    ///         including <c>0</c> for an opaque material, is left untouched.</item>
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
        if (IsOpenPbr(m)) return;
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

        // spec_trans unauthored (null). Promote to 1.0 when the user only
        // authored SSS knobs. An explicit value — including an explicit 0 for
        // an opaque material — passes through untouched.
        if (m.SpecTrans == null) m.SpecTrans = 1f;

        // transmission_color default = null → white (medium owns the colour).
        if (m.TransmissionColor == null)
            m.TransmissionColor = new List<float> { 1f, 1f, 1f };
    }

    /// <summary>
    /// The medium of a subsurface material from its artistic parameters, the
    /// OpenPBR parametrisation: <paramref name="color"/> is the albedo the
    /// surface shows (the multiple-scattering albedo of a thick slab), and
    /// <paramref name="mfp"/> the mean free path per channel:
    /// <code>
    ///   σ_t = 1 / mfp
    ///   s   = 4.09712 + 4.20863·A − √(9.59217 + 41.6808·A + 17.7126·A²)
    ///   α   = (1 − s²) / (1 − g·s²)          (single-scattering albedo)
    /// </code>
    /// the inversion of Chiang, Kutz and Burley 2016 with the anisotropy term
    /// of the OpenPBR specification. Taking the colour as the single-scattering
    /// albedo, as the Disney path did before Phase 8, rendered a colour of 0.8
    /// as 0.4 or so.
    /// </summary>
    internal static HomogeneousMedium SubsurfaceMedium(string? id, Vector3 color, Vector3 mfp, float g)
    {
        static float Single(float c, float g)
        {
            double s = 4.09712 + 4.20863 * c - Math.Sqrt(9.59217 + 41.6808 * c + 17.7126 * c * c);
            return (float)Math.Clamp((1 - s * s) / (1 - g * s * s), 0.0, 1.0);
        }
        color = Vector3.Clamp(color, Vector3.Zero, Vector3.One);
        mfp = Vector3.Max(mfp, new Vector3(1e-4f));
        g = Math.Clamp(g, -0.999f, 0.999f);
        Vector3 albedo = new(Single(color.X, g), Single(color.Y, g), Single(color.Z, g));
        Vector3 sigmaT = Vector3.One / mfp;
        IPhaseFunction phase = MathF.Abs(g) < 1e-4f ? new IsotropicPhase() : new HenyeyGreensteinPhase(g);
        Verbose($"Subsurface medium: material '{id}', mfp [{mfp.X:0.###},{mfp.Y:0.###},{mfp.Z:0.###}], " +
                $"single-scattering albedo [{albedo.X:0.###},{albedo.Y:0.###},{albedo.Z:0.###}]");
        return new HomogeneousMedium(sigmaT * (Vector3.One - albedo), sigmaT * albedo, phase);
    }

    /// <summary>
    /// The subsurface medium of a Disney material (<c>subsurface_radius</c>
    /// × <c>subsurface_scale</c>, <c>subsurface_color</c> or else the base
    /// colour, <c>subsurface_anisotropy</c>), with the OpenPBR parametrisation
    /// the Disney material is translated onto (<see cref="SubsurfaceMedium"/>).
    /// </summary>
    private static IMedium? BuildEmbeddedSssMedium(MaterialData m)
    {
        if (m.SubsurfaceRadius == null || m.SubsurfaceRadius.Count == 0)
            return null;
        List<float>? colorSrc = m.SubsurfaceColor ?? m.Color;
        Vector3 color = colorSrc is { Count: >= 3 } ? new Vector3(colorSrc[0], colorSrc[1], colorSrc[2]) : Vector3.One;
        var r = m.SubsurfaceRadius;
        float rR = r[0], rG = r.Count >= 2 ? r[1] : rR, rB = r.Count >= 3 ? r[2] : rG;
        float scale = m.SubsurfaceScale > 0f ? m.SubsurfaceScale : 1f;
        return SubsurfaceMedium(m.Id, color, new Vector3(rR, rG, rB) * scale, m.SubsurfaceAnisotropy);
    }
}
