using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Textures;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

// Renderer, part: Next-event estimation: sky, lights, media.
public partial class Renderer
{
    /// <summary>
    /// Evaluates the sky for a missed ray and applies the MIS weight for the
    /// "BSDF-sample escaped into the environment" half of the estimator.
    ///
    /// <para>The sky's analytical sun disc (if any) is included only on delta
    /// bounces and camera rays — for non-delta bounces a paired
    /// <see cref="Lights.PhysicalSun"/> handles the sun via NEE, so adding
    /// it here would double-count. This matches Arnold / Cycles HDRI
    /// sun-extraction behaviour.</para>
    ///
    /// <para>For non-delta BSDF escapes onto HDRI environments, we additionally
    /// derive a mipmap LOD from the BSDF PDF: a wide lobe (low pdf) reads from
    /// a smoother HDRI level, killing the firefly spike from undersampled
    /// pixel peaks. The heuristic is
    /// <c>lod ≈ 0.5 · log₂(W·H / (4π · pdf_bsdf))</c>, derived from the
    /// solid-angle-to-texel-count of a uniform cone with the BSDF's effective
    /// width. Sharp specular (delta) bounces use LOD 0.</para>
    /// </summary>
    private Vector3 SampleSky(Ray ray, float prevBsdfPdf, bool prevIsDelta, int chainState, RayCategory category)
    {
        // A sky NEE-sampled through the same transparent surfaces already
        // delivered this transport (see SuppressChainEmission).
        bool skyIsNeeSampled = _envLight != null && _envLight.Sky.CanSampleDirectly;
        if (skyIsNeeSampled && SuppressChainEmission(chainState))
            return Vector3.Zero;

        // Camera rays + delta-mirror bounces see the full sky (sun included).
        // Non-delta indirect bounces see the sky body only. Visibility flags
        // and the background plate follow the ray's own category: a mirror
        // reflects the sky its glossy flag shows, not the camera's plate (a
        // delta bounce used to count as a camera ray here).
        bool showSun = prevIsDelta;
        var cat = category;

        // LOD heuristic for glossy HDRI lookups: a prefiltered (blurred) sky
        // tames the fireflies of a bright HDRI that only BSDF rays find. It
        // is not the sky the light samples see: when the environment is a
        // next-event light the MIS weights pair the two, and the blur spread
        // the bright features' light into directions weighted for the BSDF,
        // counting it twice (+26% on a diffuse floor under a bright patch,
        // PIANO-REVIEW.md Phase 6). There MIS already removes the fireflies,
        // so the prefilter is kept for a sky no light samples, never in the
        // reference estimator. The sky body handles the is-HDRI / not check
        // internally: a non-zero LOD on a gradient/Preetham sky is a no-op.
        float lod = 0f;
        if (!prevIsDelta && prevBsdfPdf > 1e-6f && !_mis.HasSkyLights && !_referenceMode
            && _sky.LightingModel is Sky.HdriSky hdriSky)
        {
            int W = hdriSky.Map.Width;
            int H = hdriSky.Map.Height;
            // Texels per steradian × cone solid angle (1/pdf) = texels covered.
            // lod = log₂(√texelsCovered) = 0.5 · log₂(texelsCovered).
            float texelsCovered = W * H / (4f * MathF.PI * prevBsdfPdf);
            if (texelsCovered > 1f)
                lod = 0.5f * MathF.Log2(texelsCovered);
            lod = MathF.Max(0f, MathF.Min(lod, hdriSky.Map.MaxMipLevel));
        }

        Vector3 sky = _sky.Sample(ray, cat, includeAnalyticalSun: showSun, mipLod: lod);

        // ── AOV: environment miss commits the sky colour ────────────────────
        // Background pixels get the (clamped) sky radiance as their albedo
        // guide and a zero normal; depth keeps its −1 "no hit" sentinel.
        if (_captureAovs && t_aovProbe is { Pending: true } aovProbe)
            aovProbe.CommitAs(Clamp01(sky), Vector3.Zero);

        // A delta bounce or a camera ray sees the sky (sun included) at full
        // weight: no light sampler produces its direction.
        if (prevIsDelta)
            return sky;

        // The sky body, weighted against the environment and every portal
        // (they all sample it), when any of them is a next-event light.
        Vector3 result = sky;
        if (_mis.HasSkyLights)
        {
            var (sum, sumSq) = _mis.SkyDensity(ray.Origin, ray.Direction);
            result = prevBsdfPdf + sum <= 1e-20f ? Vector3.Zero : sky * BsdfWeight(prevBsdfPdf, sum, sumSq);
        }

        // The analytic sun is left out of the sky body above: it is a light of
        // its own, sampled by its own NEE, and a BSDF ray inside its disc
        // collects it here with the matching weight (it used to collect
        // nothing while the sun's samples were still weighted, losing light
        // on glossy surfaces).
        foreach (var sun in _mis.Suns)
        {
            Vector3 Ls = sun.RadianceToward(ray.Direction);
            if (Ls == Vector3.Zero) continue;
            float pSun = _mis.Pick(sun, ray.Origin) * sun.PdfSolidAngle(ray.Origin, ray.Direction);
            result += Ls * BsdfWeight(prevBsdfPdf, pSun, pSun * pSun);
        }
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DIRECT LIGHTING (NEXT EVENT ESTIMATION)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes direct illumination from all lights at a surface hit point.
    ///
    /// 0. Ambient fill — applied unconditionally to all materials.
    /// 1. Per-light, per-shadow-sample:
    ///    - shadow test + stratified emitter sample
    ///    - material.Evaluate(toEye, toLight) · |N·L| for the BSDF shape
    ///    - balance-heuristic MIS weight w_nee = p_light / (p_light + p_bsdf)
    ///      using the material's own IMaterial.Pdf() in solid angle.
    ///
    /// For delta lights (point/directional/spot) no BSDF sampler can reach them,
    /// so w_nee = 1 unconditionally. A pure-delta material has Pdf 0 for every
    /// light direction and Evaluate 0 there too, so it gets no direct light.
    ///
    /// PBRT/Arnold direct-lighting convention:
    /// The integrand at the shadow-ray direction is
    /// <c>f(V, L) · |N·L| · visibility</c>, with
    /// every material colour factor (baseColor, metallic Fresnel, sheen tint,
    /// subsurface tint) baked in. The caller adds this contribution directly
    /// to the radiance estimator: <c>L = Le + L_direct + scatter_attn × L_indirect</c>.
    /// The scatter attenuation never multiplies the direct term — that would
    /// couple the indirect importance sample to the shadow estimator and
    /// bias the result for any direction-dependent BSDF.
    /// </summary>
    /// <summary>
    /// NEE at a surface. A surface that transmits (rough glass, diffuse
    /// transmission) also receives light from behind: the lights keep those
    /// samples and start the shadow ray on the far side while the switch is up
    /// (PIANO-REVIEW.md §3.36).
    /// </summary>
    private Vector3 DirectLightingAt(in HitRecord rec, Vector3 viewDir, IMaterial? material, in MediumStack media)
    {
        bool transmits = material?.HasTransmission(in rec) ?? false;
        ShadowRay.ReceiverTransmits = transmits;
        // The shadow rays start in the media the path is in, and cross this
        // surface's boundary when they leave from its far side.
        ShadowRay.BeginMedia(in media, _globalMedium);
        if (transmits && material!.TryGetDielectricIor(in rec, out _) || rec.MediumIface.Interior != null)
            ShadowRay.SetReceiver(Vector3.Dot(rec.Normal, viewDir) >= 0f ? rec.Normal : -rec.Normal,
                                  rec.FrontFace, rec.MediumIface.Interior, rec.EntityRoot);
        try
        {
            return ComputeDirectLighting(in rec, viewDir, material);
        }
        finally
        {
            ShadowRay.ReceiverTransmits = false;
            ShadowRay.EndMedia();
        }
    }

    /// <summary>
    /// The direct-lighting integrand f(V, L) |N·L| for one light direction:
    /// the material's own BSDF, the same the indirect bounce samples
    /// (PIANO-REVIEW.md §3.12). A surface with no material is Lambert white.
    /// </summary>
    private static Vector3 DirectIntegrand(IMaterial? material, in HitRecord rec, Vector3 viewDir, Vector3 dirToLight)
    {
        float cos = Vector3.Dot(rec.Normal, dirToLight);
        if (material == null) return new Vector3(MathF.Max(cos, 0f) / MathF.PI);
        return material.Evaluate(viewDir, dirToLight, in rec) * MathF.Abs(cos);
    }

    /// <summary>
    /// Shadow-terminator factor for the light direction, on the side of the
    /// surface the light is: for a light behind a transmissive surface the
    /// horizon is the back one (it used to read 0 there, so NEE never lit
    /// anything through rough glass, §3.36).
    /// </summary>
    private static float LightSideTerminator(in HitRecord rec, Vector3 dirToLight)
        => Vector3.Dot(rec.ShadingNg, dirToLight) >= 0f
            ? MathUtils.ShadowTerminatorTerm(rec.ShadingNg, rec.Normal, dirToLight)
            : MathUtils.ShadowTerminatorTerm(-rec.ShadingNg, -rec.Normal, dirToLight);

    private Vector3 ComputeDirectLighting(in HitRecord rec, Vector3 viewDir, IMaterial? material)
    {
        Vector3 result = Vector3.Zero;

        if (_mis.PicksOneLight)
        {
            // ── Single-light picking (power, uniform or light BVH) ───────────
            // Unbiased single-sample estimator (PBRT §16.3.2):
            //   contribution = lightAccum / pPick
            // MIS uses pdf_combined = pPick × pLightSample for non-delta lights.
            if (_lights.Count == 0)
            {
                return result;
            }
            float xi = MathUtils.RandomFloat();
            var (lightIdx, pPick) = _mis.Choose(rec.Point, xi);
            if (lightIdx < 0) return result;
            ILight light = _lights[lightIdx];

            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                var (inShadow, lightColor, dirToLight, distance) =
                    SampleLight(light, rec.Point, rec.Normal, s, rec.Time);

                if (inShadow) continue;

                Vector3 brdf = DirectIntegrand(material, in rec, viewDir, dirToLight);

                // Shadow-terminator softening on the light side (Chiang 2019):
                // cull the energy a perturbed shading normal would fabricate where
                // the light grazes below the true surface horizon (no-op where the
                // normal is unperturbed). The BSDF-sampled bounce carries it too.
                brdf *= LightSideTerminator(in rec, dirToLight);

                // The contribution is wNee · lightColor · brdf · Tr. When the
                // BRDF integrand is zero (light below the shading hemisphere,
                // delta lobe), the sample adds nothing — skip the medium
                // transmittance walk and the BSDF-pdf MIS evaluation entirely.
                if (brdf == Vector3.Zero) continue;

                // MIS: the NEE density carries the pick probability.
                float wNee = material != null && _mis.PairsWithBsdf(light)
                    ? LightSampleWeight(light, pPick, rec.Point, dirToLight, MisBsdfPdf(material, viewDir, dirToLight, in rec))
                    : 1f;

                if (LightSampleOwnsTransport()) wNee = 1f;
                lightAccum += wNee * lightColor * brdf;
            }

            // Divide by pPick to get the unbiased estimator (1 light sampled, not all).
            if (pPick > 0f)
                result += lightAccum / pPick;
        }
        else
        {
            // ── Sum over all lights (original behaviour) ─────────────────────
            foreach (var light in _lights)
            {
                int samples = light.ShadowSamples;
                Vector3 lightAccum = Vector3.Zero;

                for (int s = 0; s < samples; s++)
                {
                    var (inShadow, lightColor, dirToLight, distance) =
                        SampleLight(light, rec.Point, rec.Normal, s, rec.Time);

                    if (inShadow) continue;

                    Vector3 brdf = DirectIntegrand(material, in rec, viewDir, dirToLight);

                    // Shadow-terminator softening on the light side (Chiang 2019):
                    // cull the energy a perturbed shading normal would fabricate where
                    // the light grazes below the true surface horizon (no-op where the
                    // normal is unperturbed). The BSDF-sampled bounce carries it too.
                    brdf *= LightSideTerminator(in rec, dirToLight);

                    // Zero integrand (light below the hemisphere / delta lobe) →
                    // the sample contributes nothing; skip the medium walk and
                    // the BSDF-pdf MIS evaluation. See the single-light branch.
                    if (brdf == Vector3.Zero) continue;

                    // ── MIS weight (balance or power, configured per-renderer) ──
                    // Delta lights: always weight 1 (no BSDF sampler can hit them).
                    // Non-delta lights: if the material exposes Pdf() > 0, the
                    // weight reduces variance via Veach's combined estimator.
                    float wNee = material != null && _mis.PairsWithBsdf(light)
                        ? LightSampleWeight(light, 1f, rec.Point, dirToLight, MisBsdfPdf(material, viewDir, dirToLight, in rec))
                        : 1f;

                    if (LightSampleOwnsTransport()) wNee = 1f;
                lightAccum += wNee * lightColor * brdf;
                }

                result += lightAccum;
            }
        }

        return result;
    }

    /// <summary>
    /// Unified light-sample dispatcher: calls the stratified variant for
    /// area/sphere/geometry/sun-disc/multi-sample-spot lights and falls back
    /// to the base <see cref="ILight.IlluminateAndTest"/> for all other lights.
    /// </summary>
    private (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        SampleLight(ILight light, Vector3 point, Vector3 normal, int sampleIndex, float time)
        // Single virtual dispatch — each light type self-selects stratified vs.
        // single-sample behaviour (directional sun-disc and multi-sample spots
        // guard on their own parameters internally). Replaces the former
        // per-sample type-ladder. See ILight.IlluminateAndTestStratified.
    {
        // Lights without a shadow walk (none today) must not inherit a stale flag.
        ShadowRay.LastCrossedTransparent = false;
        return light.IlluminateAndTestStratified(point, normal, _world, sampleIndex, time);
    }

    /// <summary>
    /// Next-event estimation at a scattering event in a medium, with the
    /// event's phase function in the BSDF's place. The shadow rays start in
    /// <paramref name="media"/> (the path's stack, the global medium when it
    /// is empty) and are attenuated by every medium they cross on the way
    /// (<see cref="ShadowRay.BeginMedia"/>).
    /// </summary>
    /// <param name="wo">Direction of the ray that produced the event (it points
    ///                  into the event, the phase functions' convention).</param>
    private Vector3 ComputeDirectLightingMedium(Vector3 p, Vector3 wo, IPhaseFunction phase,
                                                in MediumStack media, float time)
    {
        ShadowRay.BeginMedia(in media, _globalMedium);
        try
        {
            return DirectLightingInMedium(p, wo, phase, time);
        }
        finally
        {
            ShadowRay.EndMedia();
        }
    }

    private Vector3 DirectLightingInMedium(Vector3 p, Vector3 wo, IPhaseFunction phase, float time)
    {
        Vector3 result = Vector3.Zero;

        // Lights use `surfaceNormal` ONLY to offset the shadow-ray origin via
        // OffsetOrigin(p, n) = p + n × ε. For a volumetric scattering event
        // there is no surface to self-intersect with, so no offset is needed.
        // Passing Zero ensures OffsetOrigin returns the point unchanged.
        Vector3 dummyNormal = Vector3.Zero;

        if (_mis.PicksOneLight)
        {
            // ── Single-light picking in volumetric path ──────────────────────
            if (_lights.Count == 0) return result;
            float xi = MathUtils.RandomFloat();
            var (lightIdx, pPick) = _mis.Choose(p, xi);
            if (lightIdx < 0) return result;
            ILight light = _lights[lightIdx];

            int samples = light.ShadowSamples;
            Vector3 lightAccum = Vector3.Zero;

            for (int s = 0; s < samples; s++)
            {
                var (inShadow, lightColor, dirToLight, distance) =
                    SampleLight(light, p, dummyNormal, s, time);

                if (inShadow) continue;

                float phaseVal = phase.Evaluate(wo, dirToLight);

                float wNee = _mis.PairsWithBsdf(light)
                    ? LightSampleWeight(light, pPick, p, dirToLight, MisPhasePdf(phase, wo, dirToLight))
                    : 1f;

                if (LightSampleOwnsTransport()) wNee = 1f;
                wNee *= EquiangularNeeWeight(light, pPick);
                lightAccum += wNee * lightColor * phaseVal;
            }

            if (pPick > 0f)
                result += lightAccum / pPick;
        }
        else
        {
            // ── Sum over all lights ──────────────────────────────────────────
            foreach (var light in _lights)
            {
                int samples = light.ShadowSamples;
                Vector3 lightAccum = Vector3.Zero;

                for (int s = 0; s < samples; s++)
                {
                    // Mirror the dispatch used by ComputeDirectLighting so that
                    // AreaLight and SphereLight keep their stratified samples in
                    // the volumetric path too — this is where low-discrepancy
                    // shadow sampling matters most (large area lights seen through
                    // dense fog are the worst-case variance scenario).
                    var (inShadow, lightColor, dirToLight, distance) =
                        SampleLight(light, p, dummyNormal, s, time);

                    if (inShadow) continue;

                    // Phase function value for this in-scattering direction.
                    float phaseVal = phase.Evaluate(wo, dirToLight);

                    // ── MIS weight (phase-function vs light sampler) ────────────
                    // Delta lights cannot be reached by phase sampling; emit at
                    // full weight. Otherwise pair the analytic phase PDF against
                    // the light PDF using the configured heuristic.
                    float wNee = _mis.PairsWithBsdf(light)
                        ? LightSampleWeight(light, 1f, p, dirToLight, MisPhasePdf(phase, wo, dirToLight))
                        : 1f;

                    if (LightSampleOwnsTransport()) wNee = 1f;
                    wNee *= EquiangularNeeWeight(light, 1f);
                lightAccum += wNee * lightColor * phaseVal;
                }

                result += lightAccum;
            }
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SKY / ENVIRONMENT
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the sky/environment radiance for a ray that escaped the scene.
    /// Delegates to <see cref="SkySettings"/> which handles both legacy flat
    /// backgrounds and the new gradient sky with sun disk.
    /// </summary>
    private Vector3 CalculateSkyColor(Ray ray)
    {
        return _sky.Sample(ray);
    }
}
