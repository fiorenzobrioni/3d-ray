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
            // ── Visibility: an emitter hidden from camera rays is still a light
            // (camera rays do not weigh their hits against NEE). Hidden from
            // other categories, its BSDF strategy would be cut while the light
            // sample still counted on it: left to the BSDF rays alone.
            case VisibilityFilteredHittable vis:
                if ((vis.HiddenFrom & ~HitVisibilityMask.Camera) == HitVisibilityMask.None)
                    ExtractGeometryLightsRecursive(vis.Inner, lights, shadowSamples, outerMatrix, hasOuterTransform);
                else if (ContainsEmissive(vis.Inner))
                    Warn("Emissive geometry hidden from diffuse, glossy, transmission or shadow rays is not " +
                         "sampled as a light (only its BSDF-hit emission counts).");
                break;

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

            // ── AnimatedTransform: NEE samples a static snapshot of the
            // emitter at the midpoint of its key range. Sample positions, pdf
            // probes and the power heuristic all share this one self-consistent
            // pose (MIS stays correct); shadow rays still run at the true ray
            // time and BSDF-hit emission sees the real motion. Fast-moving
            // emitters therefore get slightly biased direct-light positions —
            // a documented limitation.
            case AnimatedTransform at:
            {
                if (ContainsEmissive(at.Inner))
                    Warn("Animated emissive geometry: NEE samples it at the mid-animation " +
                         "pose; its motion remains fully visible to camera/BSDF rays.");
                Matrix4x4 composed = hasOuterTransform
                    ? at.NeeSnapshotMatrix * outerMatrix
                    : at.NeeSnapshotMatrix;
                ExtractGeometryLightsRecursive(at.Inner, lights, shadowSamples, composed, hasOuterTransform: true);
                break;
            }

            // ── CSG: currently not supported for NEE; warn once if it hides an emissive leaf
            case CsgObject csg:
                WarnIfCsgContainsEmissive(csg);
                break;

            // ── Leaf primitive: try to register it
            default:
            {
                if (!TryGetSamplableEmissive(obj, out ISamplable? samplable, out Emissive? emissive,
                                             out IMaterial? surfaceMaterial))
                    return;

                // Wrap in a Transform only when there is an accumulated outer transform.
                // When outerMatrix is identity we register the primitive directly to
                // avoid the minor Jacobian overhead of a no-op Transform.
                ISamplable finalSamplable = hasOuterTransform
                    ? new Transform(obj, outerMatrix)
                    : samplable;

                lights.Add(new GeometryLight(finalSamplable, emissive, shadowSamples,
                                             surfaceMaterial: surfaceMaterial));
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
        [NotNullWhen(true)] out Emissive? material,
        out IMaterial? surfaceMaterial)
    {
        samplable = null;
        material = null;
        surfaceMaterial = null;

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

        surfaceMaterial = mat;
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
            VisibilityFilteredHittable v => ContainsEmissive(v.Inner),
            AnimatedTransform at => ContainsEmissive(at.Inner),
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

    private static ILight? CreateLight(LightData l, int? shadowSamplesOverride,
                                        List<IHittable> objects, SkySettings sky, string sceneDir = ".")
    {
        var color = ToVector3(l.Color) ?? Vector3.One;

        switch (l.Type)
        {
            case "point":
            {
                var ies = LoadIes(l, sceneDir);
                var (c, i) = LightEmission(l, color, EmissionKind.Intensity, 0f, ies);
                return new PointLight(ToVector3(l.Position) ?? new Vector3(0, 10, 0), c, i, l.SoftRadius,
                                      ies, ToVector3(l.Direction));
            }
            case "directional":
            {
                var (c, e) = LightEmission(l, color, EmissionKind.Irradiance, 0f);
                return new DirectionalLight(ToVector3(l.Direction) ?? new Vector3(-1, -1, -1),
                    c, e, l.AngularRadius,
                    shadowSamplesOverride ?? 0);   // 0 → auto (1 if delta, 16 if disc)
            }
            case "spot":
            {
                var ies = LoadIes(l, sceneDir);
                var (c, i) = LightEmission(l, color, EmissionKind.Intensity, 0f, ies);
                return new SpotLight(
                    ToVector3(l.Position)  ?? new Vector3(0, 10, 0),
                    ToVector3(l.Direction) ?? new Vector3(0, -1, 0),
                    c, i, l.InnerAngle, l.OuterAngle, l.SoftRadius,
                    shadowSamplesOverride ?? l.ShadowSamples, ies);
            }
            // A window onto the environment: NEE restricted to the directions
            // the sky is seen through.
            case "portal":
                return CreatePortalLight(l, shadowSamplesOverride, sky);
            case "area":
                return CreateAreaLight(l, color, shadowSamplesOverride, objects);
            case "sphere":
                return CreateSphereLight(l, color, shadowSamplesOverride, objects);
            default:
                return null;
        }
    }

    /// <summary>The photometric quantity of a light type.</summary>
    private enum EmissionKind
    {
        /// <summary>Luminous intensity, candela: point, spot.</summary>
        Intensity,
        /// <summary>Luminance of the emitting surface, nits: area, sphere.</summary>
        Radiance,
        /// <summary>Illuminance on a surface facing the light, lux: directional.</summary>
        Irradiance,
    }

    /// <summary>Loads a point or spot light's IES file (relative to the scene); null on none or failure.</summary>
    private static IesProfile? LoadIes(LightData l, string sceneDir)
    {
        if (string.IsNullOrWhiteSpace(l.Ies)) return null;
        string path = Path.IsPathRooted(l.Ies) ? l.Ies : Path.Combine(sceneDir, l.Ies);
        try
        {
            var ies = IesProfile.Load(path);
            if (ies.TiltIgnored) Warn($"IES '{l.Ies}': TILT data ignored (the lamp is taken as mounted).");
            if (ies.Flux <= 0f) { Warn($"IES '{l.Ies}': the distribution emits nothing; ignored."); return null; }
            return ies;
        }
        catch (Exception ex)
        {
            Warn($"IES '{l.Ies}' could not be read ({ex.Message}); the light is isotropic.");
            return null;
        }
    }

    /// <summary>
    /// The colour and amount of a light (PIANO-REVIEW.md §3.14, Phase 10): the
    /// photometric quantity of the type (<c>intensity</c> cd, <c>luminance</c>
    /// nit, <c>illuminance</c> lux) or its <c>power</c>, the colour reduced to a
    /// tint of luminance 1 (times a black body for <c>temperature</c>), times
    /// 2^<c>exposure</c>. The scene is in nits; candela and power carry metres,
    /// converted with the scene's <c>units.length</c>. <paramref name="area"/>
    /// is the emitting area in scene units² (radiance kinds).
    /// </summary>
    private static (Vector3 Color, float Amount) LightEmission(LightData l, Vector3 color, EmissionKind kind, float area,
                                                                IesProfile? ies = null)
    {
        string name = l.Name ?? l.Type ?? "light";
        float? lumens = null;
        if (l.Power is float p)
            lumens = l.PowerUnit == "lumen" ? p : p * LightUnits.LumensPerWatt;

        float? quantity = kind switch
        {
            EmissionKind.Intensity => l.Intensity,
            EmissionKind.Radiance  => l.Luminance,
            _                      => l.Illuminance,
        };
        if (quantity != null && lumens != null)
            Warn($"Light '{name}': both '{(kind == EmissionKind.Intensity ? "intensity" : "luminance")}' and 'power' are set; using 'power'.");

        // An intensity of 1 cd at d metres is d / metres-per-unit scene units away.
        float perSquareMetre = 1f / (_metersPerUnit * _metersPerUnit);
        float amount;
        if (ies != null)
        {
            // An IES profile is absolute candela: power or intensity rescale it
            // to that flux or that peak.
            amount = lumens is float flux ? flux / ies.Flux
                   : quantity is float peak ? peak / ies.MaxCandela
                   : 1f;
            amount *= perSquareMetre;
        }
        else
        {
            amount = kind switch
            {
                // A spot's power is that of the point light it would be: the
                // cone cuts the light, it does not concentrate it.
                EmissionKind.Intensity => (lumens is float lm ? lm / (4f * MathF.PI) : quantity ?? 1f) * perSquareMetre,
                EmissionKind.Radiance  => lumens is float lm2 && area > 0f
                                              ? lm2 / (MathF.PI * area) * perSquareMetre
                                              : quantity ?? 1f,
                _                      => quantity ?? 1f,
            };
        }
        if (l.Exposure != 0f) amount *= MathF.Pow(2f, l.Exposure);
        return (LightUnits.UnitTint(color, l.Temperature), amount);
    }

    private static PortalLight? CreatePortalLight(LightData l, int? shadowSamplesOverride,
                                                    SkySettings sky)
    {
        var anchor = ToVector3(l.Corner);
        var u      = ToVector3(l.U);
        var v      = ToVector3(l.V);

        if (anchor == null || u == null || v == null)
        {
            Warn("Portal light requires 'corner', 'u', and 'v' vectors. Skipping.");
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
        (color, float radiance) = LightEmission(l, color, EmissionKind.Radiance,
                                                Vector3.Cross(u.Value, v.Value).Length());

        // Visible emissive proxy — Quad with the light's emission as Lambertian
        // radiance. The proxy's radiance equals the light's `intensity * color`
        // because the existing area-light estimator
        //   L_sample = Intensity × area × cosLight / (d² × N)
        // is the Lambertian-radiance integrand: NEE and the BSDF-sampled hit on
        // the proxy produce the same total energy, closing Veach's MIS estimator.
        // Wrap in BackFaceCulledHittable so the camera and specular rays see
        // through the dark (back) side of the panel — same pattern Arnold and
        // Cycles use for analytic single-sided quad lights.
        var proxyMat = new Emissive(color, radiance) { IsLightProxy = true };
        IHittable proxy = new BackFaceCulledHittable(
            new Quad(corner.Value, u.Value, v.Value, proxyMat));
        // visibility: the proxy hidden from some ray categories (the light
        // still illuminates through its own NEE).
        objects.Add(ApplyVisibility(proxy, l.Visibility));

        return new AreaLight(corner.Value, u.Value, v.Value, color,
                             radiance, effectiveShadowSamples, l.SoftRadius,
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
        (color, float radiance) = LightEmission(l, color, EmissionKind.Radiance,
                                                4f * MathF.PI * l.Radius * l.Radius);

        // Visible emissive proxy — Sphere with the light's emission as
        // Lambertian radiance. The existing solid-angle estimator
        //   L_sample = Intensity × Ω / N
        // already treats `intensity` as surface radiance (W/m²/sr) at
        // convergence: `Intensity × Ω` matches the irradiance at the receiver
        // from a uniform sphere of that radiance. Setting the proxy's
        // emission to `color × intensity` therefore gives BSDF-sampled hits
        // the same energy as NEE — required for MIS to be unbiased.
        var proxyMat = new Emissive(color, radiance) { IsLightProxy = true };
        IHittable proxy = new Sphere(position.Value, l.Radius, proxyMat);
        // visibility: the proxy hidden from some ray categories (the light
        // still illuminates through its own NEE).
        objects.Add(ApplyVisibility(proxy, l.Visibility));

        return new SphereLight(position.Value, l.Radius, color,
                               radiance, effectiveShadowSamples,
                               proxyMat);
    }
}
