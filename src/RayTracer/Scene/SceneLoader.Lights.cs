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

        string? lightType = l.Type?.ToLowerInvariant();
        switch (lightType)
        {
            case "point":
            {
                var ies = LoadIes(l, sceneDir);
                var (c, i) = LightEmission(l, color, EmissionKind.Intensity, 0f, ies);
                return new PointLight(ToVector3(l.Position) ?? new Vector3(0, 10, 0), c, i, l.SoftRadius,
                                      ies, ToVector3(l.Direction));
            }
            case "directional" or "sun":
            {
                var (c, e) = LightEmission(l, color, EmissionKind.Irradiance, 0f);
                return new DirectionalLight(ToVector3(l.Direction) ?? new Vector3(-1, -1, -1),
                    c, e, l.AngularRadius,
                    shadowSamplesOverride ?? 0);   // 0 → auto (1 if delta, 16 if disc)
            }
            case "spot" or "spotlight":
            {
                var ies = LoadIes(l, sceneDir);
                var (c, i) = LightEmission(l, color, EmissionKind.Intensity, 0f, ies);
                return new SpotLight(
                    ToVector3(l.Position)  ?? new Vector3(0, 10, 0),
                    ToVector3(l.Direction) ?? new Vector3(0, -1, 0),
                    c, i, l.InnerAngle, l.OuterAngle, l.SoftRadius,
                    shadowSamplesOverride ?? l.ShadowSamples, ies);
            }
        }

        return lightType switch
        {

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

    /// <summary>The quantity a light type's amount is: what <c>intensity</c> means for it.</summary>
    private enum EmissionKind
    {
        /// <summary>Radiant intensity (per steradian): point, spot. Photometric: candela.</summary>
        Intensity,
        /// <summary>Radiance of the emitting surface: area, sphere. Photometric: nits.</summary>
        Radiance,
        /// <summary>Irradiance on a surface facing it: directional. Photometric: lux.</summary>
        Irradiance,
    }

    /// <summary>
    /// The colour and amount of a light (PIANO-REVIEW.md §3.14): the unitless
    /// <c>intensity</c> as before, or a photometric quantity (<c>power</c>,
    /// <c>candela</c>, <c>luminance</c>, <c>illuminance</c>) in nits-based
    /// scene units, the colour then reduced to a tint of luminance 1.
    /// <c>temperature</c> multiplies the colour by a black body, <c>normalize</c>
    /// spreads <c>intensity</c> over the emitting area, <c>exposure</c> scales
    /// by 2^stops. <paramref name="area"/> is the emitting area (radiance kinds).
    /// </summary>
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

    private static (Vector3 Color, float Amount) LightEmission(LightData l, Vector3 color, EmissionKind kind, float area,
                                                                IesProfile? ies = null)
    {
        string name = l.Type ?? "light";
        float? lumens = null;
        if (l.Power is float p)
        {
            string unit = (l.PowerUnit ?? "watt").Trim().ToLowerInvariant();
            if (unit is "lumen" or "lumens" or "lm") lumens = p;
            else
            {
                if (unit is not ("watt" or "watts" or "w"))
                    Warn($"Light '{name}': unknown power_unit '{l.PowerUnit}' (watt, lumen); using watts.");
                lumens = p * LightUnits.LumensPerWatt;
            }
        }

        // An IES profile is photometric on its own (absolute candela): the
        // light's amount scales it, to a flux or a peak when given.
        if (ies != null)
        {
            float scale = l.Candela is float peak ? peak / ies.MaxCandela
                        : lumens is float flux ? flux / ies.Flux
                        : l.Intensity;
            if (l.Exposure != 0f) scale *= MathF.Pow(2f, l.Exposure);
            return (LightUnits.UnitTint(color, l.Temperature), scale);
        }

        float? photometric = kind switch
        {
            // A spot's power is that of the point light it would be: the cone
            // cuts the light, it does not concentrate it.
            EmissionKind.Intensity => l.Candela ?? (lumens / (4f * MathF.PI)),
            EmissionKind.Radiance  => l.Luminance ?? (lumens is float lm && area > 0f ? lm / (MathF.PI * area) : (float?)null),
            _                      => l.Illuminance,
        };

        string?[] candidates = kind switch
        {
            EmissionKind.Intensity  => new[] { l.Luminance != null ? "luminance" : null, l.Illuminance != null ? "illuminance" : null },
            EmissionKind.Radiance   => new[] { l.Candela != null ? "candela" : null, l.Illuminance != null ? "illuminance" : null },
            _                       => new[] { l.Candela != null ? "candela" : null, l.Luminance != null ? "luminance" : null, l.Power != null ? "power" : null },
        };
        var unused = candidates.Where(k => k != null).ToArray();
        if (unused.Length > 0)
            Warn($"Light '{name}': {string.Join(", ", unused)} does not apply to this light type; ignored.");

        float amount;
        if (photometric is float q)
        {
            color = LightUnits.UnitTint(color, l.Temperature);
            amount = q;
        }
        else
        {
            if (l.Temperature is float k) color *= LightUnits.Blackbody(k);
            amount = l.Intensity;
            if (l.Normalize)
            {
                if (kind == EmissionKind.Radiance && area > 0f) amount /= area;
                else Warn($"Light '{name}': normalize applies to area and sphere lights only; ignored.");
            }
        }
        if (l.Exposure != 0f) amount *= MathF.Pow(2f, l.Exposure);
        return (color, amount);
    }

    /// <summary>
    /// <c>emissive</c>: white unless a colour is written; <c>intensity</c> is the
    /// radiance, or <c>luminance</c> in nits with the colour as a tint of
    /// luminance 1 (a texture keeps its values); <c>temperature</c> multiplies
    /// by a black body.
    /// </summary>
    private static Emissive CreateEmissive(MaterialData m, ITexture albedo)
    {
        bool textured = m.Texture != null;
        Vector3 color = ToVector3(m.Color) ?? Vector3.One;
        if (m.Luminance is float nits)
        {
            if (textured)
                return new Emissive(m.Temperature is float k ? new DerivedTexture(albedo, c => c * LightUnits.Blackbody(k)) : albedo, nits);
            return new Emissive(LightUnits.UnitTint(color, m.Temperature), nits);
        }
        if (textured)
            return new Emissive(m.Temperature is float k ? new DerivedTexture(albedo, c => c * LightUnits.Blackbody(k)) : albedo, m.Intensity);
        return new Emissive(m.Temperature is float t ? color * LightUnits.Blackbody(t) : color, m.Intensity);
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
        // visible_to_camera: false → wrap so primary rays skip past the proxy
        // (mirror reflections, refractions and NEE continue to see it; see
        // Renderer.FindVisibleHit for the skip-loop). Default true is a no-op so
        // existing scenes produce bit-identical BVH content.
        if (!l.VisibleToCamera)
            proxy = new CameraInvisibleHittable(proxy);
        objects.Add(proxy);

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
        // visible_to_camera: false → wrap so primary rays skip past the proxy
        // (mirror reflections, refractions and NEE continue to see it; see
        // Renderer.FindVisibleHit for the skip-loop). Default true is a no-op so
        // existing scenes produce bit-identical BVH content.
        if (!l.VisibleToCamera)
            proxy = new CameraInvisibleHittable(proxy);
        objects.Add(proxy);

        return new SphereLight(position.Value, l.Radius, color,
                               radiance, effectiveShadowSamples,
                               proxyMat);
    }
}
