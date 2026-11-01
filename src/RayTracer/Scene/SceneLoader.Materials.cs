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
    // ── Parameters: a number, a colour or a texture block ───────────────────

    private static FloatTexture ParamFloat(MaterialData m, MaterialParam? p, float def, string key, string sceneDir)
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

    private static ITexture ParamColor(MaterialData m, MaterialParam? p, Vector3 def, string key, string sceneDir)
    {
        if (p == null) return new SolidColor(def);
        if (p.Texture != null) return CreateTexture(p.Texture, sceneDir);
        return new SolidColor(ParamVector(m, p.Values, def, key));
    }

    /// <summary>A colour written as [r, g, b] or as one grey number.</summary>
    private static Vector3 ParamVector(MaterialData m, List<float>? v, Vector3 def, string key)
    {
        if (v == null || v.Count == 0) return def;
        if (v.Count == 1) return new Vector3(v[0]);
        if (v.Count == 3) return new Vector3(v[0], v[1], v[2]);
        Warn($"Material '{m.Id}': '{key}' needs one or three numbers; using the default.");
        return def;
    }

    /// <summary>The constant value of a scalar input, or null when textured.</summary>
    private static float? ParamConstant(MaterialParam? p, float def) =>
        p == null ? def : p.Texture != null ? null : p.Values is { Count: > 0 } v ? v[0] : def;

    /// <summary>The constant colour of an input (its default when textured).</summary>
    private static Vector3 ParamConstantColor(MaterialData m, MaterialParam? p, Vector3 def, string key) =>
        p?.Values != null ? ParamVector(m, p.Values, def, key) : def;

    /// <summary>
    /// Metal: a measured <c>preset</c> or an explicit complex IOR
    /// (<c>eta</c> + <c>k</c>) gives the exact conductor Fresnel; otherwise
    /// the F82 model from <c>color</c> (normal incidence, default white as
    /// Mitsuba's conductor) and <c>edge_color</c> (grazing tint).
    /// </summary>
    private static Metal CreateMetal(MaterialData m, string sceneDir)
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
        if (ior != null && (m.Color != null || m.EdgeColor != null))
            Info($"Material '{m.Id}': the complex IOR sets the metal's colour; 'color' and 'edge_color' are ignored.");
        ITexture? edge = ToVector3(m.EdgeColor) is Vector3 ec ? new SolidColor(ec) : null;
        return new Metal(ParamColor(m, m.Color, Vector3.One, "color", sceneDir), m.Roughness ?? 0f, edge, ior, m.MultipleScattering);
    }

    /// <summary>
    /// <c>emissive</c>: <c>luminance</c> nits; a colour is a tint of luminance 1,
    /// a texture keeps its values; <c>temperature</c> multiplies by a black body.
    /// </summary>
    private static Emissive CreateEmissive(MaterialData m, string sceneDir)
    {
        if (m.Color?.Texture != null)
        {
            var tex = CreateTexture(m.Color.Texture, sceneDir);
            return new Emissive(m.Temperature is float k ? new DerivedTexture(tex, c => c * LightUnits.Blackbody(k)) : tex,
                                m.Luminance);
        }
        var color = ParamConstantColor(m, m.Color, Vector3.One, "color");
        return new Emissive(LightUnits.UnitTint(color, m.Temperature), m.Luminance);
    }

    // ── OpenPBR Surface (PIANO-REVIEW.md, Phase 5) ──────────────────────────

    private static IMaterial CreateOpenPbr(MaterialData m, string sceneDir)
    {
        var p = new OpenPbrParameters
        {
            BaseWeight = ParamFloat(m, m.BaseWeight, 1f, "base_weight", sceneDir),
            BaseColor = ParamColor(m, m.BaseColor, new Vector3(0.8f), "base_color", sceneDir),
            BaseMetalness = ParamFloat(m, m.BaseMetalness, 0f, "base_metalness", sceneDir),
            BaseDiffuseRoughness = ParamFloat(m, m.BaseDiffuseRoughness, 0f, "base_diffuse_roughness", sceneDir),
            SpecularWeight = ParamFloat(m, m.SpecularWeight, 1f, "specular_weight", sceneDir),
            SpecularColor = ParamColor(m, m.SpecularColor, Vector3.One, "specular_color", sceneDir),
            SpecularRoughness = ParamFloat(m, m.SpecularRoughness, 0.3f, "specular_roughness", sceneDir),
            SpecularRoughnessAnisotropy = ParamFloat(m, m.SpecularRoughnessAnisotropy, 0f, "specular_roughness_anisotropy", sceneDir),
            // Degrees in the file, a fraction of a turn in the material.
            SpecularRotation = Turns(ParamFloat(m, m.SpecularRotation, 0f, "specular_rotation", sceneDir)),
            SpecularIor = ParamFloat(m, m.SpecularIor, 1.5f, "specular_ior", sceneDir),
            TransmissionWeight = ParamFloat(m, m.TransmissionWeight, 0f, "transmission_weight", sceneDir),
            TransmissionColor = ParamColor(m, m.TransmissionColor, Vector3.One, "transmission_color", sceneDir),
            TransmissionDepth = ParamFloat(m, m.TransmissionDepth, 0f, "transmission_depth", sceneDir),
            TransmissionScatter = ParamColor(m, m.TransmissionScatter, Vector3.Zero, "transmission_scatter", sceneDir),
            TransmissionScatterAnisotropy = ParamFloat(m, m.TransmissionScatterAnisotropy, 0f, "transmission_scatter_anisotropy", sceneDir),
            SubsurfaceWeight = ParamFloat(m, m.SubsurfaceWeight, 0f, "subsurface_weight", sceneDir),
            SubsurfaceColor = ParamColor(m, m.SubsurfaceColor, new Vector3(0.8f), "subsurface_color", sceneDir),
            SubsurfaceRadius = m.SubsurfaceRadius ?? 1f,
            SubsurfaceRadiusScale = ParamColor(m, m.SubsurfaceRadiusScale, new Vector3(1f, 0.5f, 0.25f), "subsurface_radius_scale", sceneDir),
            SubsurfaceScatterAnisotropy = ParamFloat(m, m.SubsurfaceScatterAnisotropy, 0f, "subsurface_scatter_anisotropy", sceneDir),
            CoatWeight = ParamFloat(m, m.CoatWeight, 0f, "coat_weight", sceneDir),
            CoatColor = ParamColor(m, m.CoatColor, Vector3.One, "coat_color", sceneDir),
            CoatRoughness = ParamFloat(m, m.CoatRoughness, 0f, "coat_roughness", sceneDir),
            CoatRoughnessAnisotropy = ParamFloat(m, m.CoatRoughnessAnisotropy, 0f, "coat_roughness_anisotropy", sceneDir),
            CoatIor = ParamFloat(m, m.CoatIor, 1.6f, "coat_ior", sceneDir),
            CoatDarkening = ParamFloat(m, m.CoatDarkening, 1f, "coat_darkening", sceneDir),
            FuzzWeight = ParamFloat(m, m.FuzzWeight, 0f, "fuzz_weight", sceneDir),
            FuzzColor = ParamColor(m, m.FuzzColor, Vector3.One, "fuzz_color", sceneDir),
            FuzzRoughness = ParamFloat(m, m.FuzzRoughness, 0.5f, "fuzz_roughness", sceneDir),
            ThinFilmWeight = ParamFloat(m, m.ThinFilmWeight, 0f, "thin_film_weight", sceneDir),
            ThinFilmThickness = ParamFloat(m, m.ThinFilmThickness, 0.5f, "thin_film_thickness", sceneDir),
            ThinFilmIor = ParamFloat(m, m.ThinFilmIor, 1.4f, "thin_film_ior", sceneDir),
            EmissionLuminance = ParamFloat(m, m.EmissionLuminance, 0f, "emission_luminance", sceneDir),
            EmissionColor = ParamColor(m, m.EmissionColor, Vector3.One, "emission_color", sceneDir),
            GeometryOpacity = ParamFloat(m, m.GeometryOpacity, 1f, "geometry_opacity", sceneDir),
            ThinWalled = m.GeometryThinWalled,
            CoatNormal = m.CoatNormalMap != null ? LoadNormalMap(m.CoatNormalMap, sceneDir) : null,
        };
        return new OpenPbr(p) { InteriorIsMedium = BuildOpenPbrMedium(m) != null };
    }

    private static FloatTexture Turns(FloatTexture degrees) => degrees.IsConstant
        ? new FloatTexture(degrees.ConstantValue / 360f)
        : new FloatTexture(new DerivedTexture(degrees.ToTexture(), c => c / 360f));

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
    /// Textured inputs give a homogeneous medium of their default value.
    /// </summary>
    private static IMedium? BuildOpenPbrMedium(MaterialData m)
    {
        if (m.GeometryThinWalled) return null;
        float sw = ParamConstant(m.SubsurfaceWeight, 0f) ?? 1f;
        if (sw > 0f)
        {
            float radius = m.SubsurfaceRadius ?? 1f;
            Vector3 scale = ParamConstantColor(m, m.SubsurfaceRadiusScale, new Vector3(1f, 0.5f, 0.25f), "subsurface_radius_scale");
            Vector3 mfp = Vector3.Max(radius * scale, new Vector3(1e-4f));
            Vector3 color = Vector3.Clamp(ParamConstantColor(m, m.SubsurfaceColor, new Vector3(0.8f), "subsurface_color"),
                                          Vector3.Zero, Vector3.One);
            float g = Math.Clamp(ParamConstant(m.SubsurfaceScatterAnisotropy, 0f) ?? 0f, -0.999f, 0.999f);
            return SubsurfaceMedium(m.Id, color, mfp, g);
        }

        float tw = ParamConstant(m.TransmissionWeight, 0f) ?? 1f;
        float depth = ParamConstant(m.TransmissionDepth, 0f) ?? 0f;
        Vector3 scatter = ParamConstantColor(m, m.TransmissionScatter, Vector3.Zero, "transmission_scatter");
        if (tw <= 0f || depth <= 0f || scatter == Vector3.Zero) return null;
        Vector3 tc = Vector3.Clamp(ParamConstantColor(m, m.TransmissionColor, Vector3.One, "transmission_color"),
                                   new Vector3(1e-4f), Vector3.One);
        Vector3 mt = new Vector3(-MathF.Log(tc.X), -MathF.Log(tc.Y), -MathF.Log(tc.Z)) / depth;
        Vector3 ms = Vector3.Max(scatter, Vector3.Zero) / depth;
        Vector3 ma = mt - ms;
        float shift = MathF.Min(0f, MathF.Min(ma.X, MathF.Min(ma.Y, ma.Z)));
        ma -= new Vector3(shift);
        float gt = Math.Clamp(ParamConstant(m.TransmissionScatterAnisotropy, 0f) ?? 0f, -0.999f, 0.999f);
        IPhaseFunction tphase = MathF.Abs(gt) < 1e-4f ? new IsotropicPhase() : new HenyeyGreensteinPhase(gt);
        return new HomogeneousMedium(ma, ms, tphase);
    }

    private static IMaterial CreateMaterial(MaterialData m, string sceneDir)
    {
        IMaterial material = m.Type switch
        {
            // Default albedo 0.5, as Mitsuba's diffuse.
            "lambertian" => new Lambertian(ParamColor(m, m.Color, new Vector3(0.5f), "color", sceneDir)),
            "metal"      => CreateMetal(m, sceneDir),
            "dielectric" => new Dielectric(m.Ior, ParamColor(m, m.Color, Vector3.One, "color", sceneDir), m.Roughness ?? 0f),
            "emissive"   => CreateEmissive(m, sceneDir),
            "openpbr"    => CreateOpenPbr(m, sceneDir),
            _            => new Lambertian(new Vector3(0.5f)),
        };

        if (m.NormalMap != null && LoadNormalMap(m.NormalMap, sceneDir) is { } normalMap)
        {
            switch (material)
            {
                case Lambertian  lam: lam.NormalMap = normalMap; break;
                case Metal       met: met.NormalMap = normalMap; break;
                case Dielectric  die: die.NormalMap = normalMap; break;
                case Emissive    emi: emi.NormalMap = normalMap; break;
                case OpenPbr     opb: opb.NormalMap = normalMap; break;
            }
        }

        // Applied after the normal map at shading time.
        if (m.BumpMap != null && LoadBumpMap(m.BumpMap, sceneDir) is { } bumpMap)
        {
            switch (material)
            {
                case Lambertian  lam: lam.BumpMap = bumpMap; break;
                case Metal       met: met.BumpMap = bumpMap; break;
                case Dielectric  die: die.BumpMap = bumpMap; break;
                case Emissive    emi: emi.BumpMap = bumpMap; break;
                case OpenPbr     opb: opb.BumpMap = bumpMap; break;
            }
        }

        // Material-level displacement: every mesh entity that uses the
        // material is displaced.
        var leafDisp = BuildLeafDisplacement(m, sceneDir);
        if (leafDisp != null)
        {
            switch (material)
            {
                case Lambertian lam: lam.Displacement = leafDisp; break;
                case Metal      met: met.Displacement = leafDisp; break;
                case Dielectric die: die.Displacement = leafDisp; break;
                case Emissive   emi: emi.Displacement = leafDisp; break;
                case OpenPbr    opb: opb.Displacement = leafDisp; break;
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
 
        // blend: a number, or a texture mask in its place.
        ITexture? mask = m.Blend?.Texture != null ? CreateTexture(m.Blend.Texture, sceneDir, TextureRole.Data) : null;
        float blend = Math.Clamp(ParamConstant(m.Blend, 0.5f) ?? 0.5f, 0f, 1f);
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
}
