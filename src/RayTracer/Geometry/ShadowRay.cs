using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Volumetrics;

namespace RayTracer.Geometry;

/// <summary>
/// Shadow-ray traversal helpers used by every <see cref="Lights.ILight"/> NEE
/// shadow test.
///
/// <para><b>Transparent shadow rays.</b> Walks a shadow ray through possibly
/// transmissive geometry, multiplying each crossed surface's
/// <see cref="IMaterial.ShadowTransmittance"/> into a throughput accumulator.
/// Returns <see cref="Vector3.One"/> for an unobstructed path,
/// <see cref="Vector3.Zero"/> when an opaque surface (or the bounce budget)
/// blocks it, and intermediate values when the ray crosses dielectric /
/// Disney <c>spec_trans</c> / mix surfaces.</para>
///
/// <para>The ray is NOT refracted — it continues straight, and each crossed
/// surface contributes its <see cref="IMaterial.ShadowTransmittance"/>
/// multiplicatively. This is the same approximation Arnold and Cycles use by
/// default: glass casts a Fresnel-tinted soft shadow instead of a hard one,
/// but focused refractive caustics are NOT reproduced (those need photon
/// mapping — see DEVLOG roadmap).</para>
///
/// <para><b>Beer-Lambert.</b> When the walker enters a surface that exposes a
/// non-zero <see cref="IMaterial.ShadowAbsorption"/> (Disney glass with
/// <c>transmission_color</c> + <c>transmission_depth &gt; 0</c>), the segment
/// inside the medium is attenuated by <c>exp(−σ_a · d)</c> until the next
/// surface hit (the back-face exit). This makes the shadow of a ruby /
/// emerald / amber sphere appear coloured, matching the volumetric
/// absorption that already tints the BSDF transmission lobe.</para>
///
/// <para><c>maxBounces</c> caps the number of transparent interfaces crossed.
/// A clear glass shell crosses 2 (entry + exit); nested glass-in-glass crosses
/// 4. Reaching the cap with non-trivial residual is treated as opaque to stay
/// conservative.</para>
/// </summary>
public static class ShadowRay
{
    /// <summary>
    /// Per-thread switch the renderer raises during the camera pass when photon
    /// caustics are active. While set, a <b>smooth specular transmissive</b>
    /// surface (<see cref="IMaterial.IsSpecularTransmissive"/>) is treated as
    /// OPAQUE to NEE shadow rays: the refracted, focused, Beer-Lambert-tinted
    /// light through clear glass is supplied by the photon map instead, so it is
    /// not also delivered as a straight-through soft fill (which would double the
    /// transmitted energy and wash out the coloured refractive shadow). Rough
    /// transmission and the caustics-off path are unaffected. Thread-static so it
    /// is correct under the parallel render and never leaks between renders.
    /// </summary>
    [ThreadStatic] private static bool _blockSpecularTransmission;
    public static bool BlockSpecularTransmission
    {
        get => _blockSpecularTransmission;
        set => _blockSpecularTransmission = value;
    }

    /// <summary>
    /// Per-thread switch raised by the renderer in reference mode: every surface
    /// with a material is opaque to NEE shadow rays, so no straight-through
    /// transmission approximation reaches the estimate. Light through glass is
    /// then carried only by BSDF-sampled paths. Thread-static for the same
    /// reason as <see cref="BlockSpecularTransmission"/>.
    /// </summary>
    [ThreadStatic] private static bool _receiverTransmits;
    /// <summary>
    /// Per-thread switch raised by the renderer around the direct lighting of
    /// a surface that transmits light (rough glass, diffuse transmission):
    /// lights then keep samples below the receiver's horizon, which they
    /// otherwise discard early, and the shadow ray leaves from the far side.
    /// Without it, NEE never reached a light through such a surface while MIS
    /// still gave BSDF sampling only its share: light was lost
    /// (PIANO-REVIEW.md §3.36).
    /// </summary>
    public static bool ReceiverTransmits
    {
        get => _receiverTransmits;
        set => _receiverTransmits = value;
    }

    [ThreadStatic] private static bool _blockAllTransmission;
    public static bool BlockAllTransmission
    {
        get => _blockAllTransmission;
        set => _blockAllTransmission = value;
    }

    /// <summary>
    /// True when the last <see cref="Transmittance"/> walk on this thread let
    /// light through at least one surface (a transparent shadow). The renderer
    /// reads it right after a light sample: such a sample is then the only
    /// estimator of that transport (the BSDF paths through the same glass are
    /// suppressed), so it takes the full MIS weight.
    /// </summary>
    [ThreadStatic] private static bool _lastCrossedTransparent;

    /// <summary>
    /// Entity whose boundary shadow rays cross as if it were not there, set by
    /// the random-walk subsurface integrator around its in-walk light samples.
    /// The walk leaves its entity straight through the boundary with no
    /// refraction and no Fresnel, and MIS-pairs that escape with these light
    /// samples, so they must see the same null boundary. Every other surface
    /// follows the normal rule. Thread-static like the switches above.
    /// </summary>
    [ThreadStatic] private static IHittable? _passThroughEntity;
    public static IHittable? PassThroughEntity
    {
        get => _passThroughEntity;
        set => _passThroughEntity = value;
    }
    public static bool LastCrossedTransparent
    {
        get => _lastCrossedTransparent;
        set => _lastCrossedTransparent = value;
    }

    // ── Participating media along shadow rays (PIANO-REVIEW.md Phase 8) ────
    // The renderer opens a context around its light samples: the media the
    // shadow ray starts in (the path's stack and the global medium) and, at a
    // surface, the receiver's own boundary. The walk below then attenuates
    // every stretch by the medium it crosses and switches media at every
    // boundary it passes, as the path does: a fog volume between a surface
    // and a light now shadows it, and the global fog past the boundary of a
    // bound medium is counted (before, only the medium at the start was, and
    // only up to its own boundary).
    [ThreadStatic] private static bool t_mediaOn;
    [ThreadStatic] private static MediumStack t_media;
    [ThreadStatic] private static IMedium? t_globalMedium;
    [ThreadStatic] private static bool t_receiverCrosses, t_receiverFront;
    [ThreadStatic] private static Vector3 t_receiverSide;
    [ThreadStatic] private static IMedium? t_receiverInterior;
    [ThreadStatic] private static IHittable? t_receiverEntity;

    /// <summary>Opens a media context for the light samples that follow on this thread.</summary>
    public static void BeginMedia(in MediumStack media, IMedium? global)
    {
        t_mediaOn = true;
        t_media = media;
        t_globalMedium = global;
        t_receiverCrosses = false;
    }

    /// <summary>
    /// The receiver of the light samples is a boundary the path would cross
    /// on refraction: a shadow ray that leaves on its far side (against
    /// <paramref name="side"/>, the normal on the side the path arrived)
    /// crosses it first.
    /// </summary>
    public static void SetReceiver(Vector3 side, bool frontFace, IMedium? interior, IHittable? entity)
    {
        t_receiverCrosses = true;
        t_receiverSide = side;
        t_receiverFront = frontFace;
        t_receiverInterior = interior;
        t_receiverEntity = entity;
    }

    public static void EndMedia()
    {
        t_mediaOn = false;
        t_media = default;
        t_globalMedium = null;
        t_receiverCrosses = false;
        t_receiverInterior = null;
        t_receiverEntity = null;
    }

    /// <summary>
    /// A surface the media switch at: a refractive solid, or an entity bound
    /// to a medium (the path pushes the interior on entry and pops it on exit).
    /// </summary>
    private static bool IsMediumBoundary(in HitRecord rec)
        => rec.MediumIface.Interior != null || rec.MediumIface.Exterior != null
           || (rec.Material?.TryGetDielectricIor(in rec, out _) ?? false);

    private readonly record struct MediumSegment(IMedium Medium, Ray Ray, float Length);
    private const int MaxSegments = 16;
    [ThreadStatic] private static MediumSegment[]? t_segments;

    /// <summary>The transmittance of the noted medium stretches, once the ray is known to get through.</summary>
    private static Vector3 ThroughMedia(Vector3 throughput, int count)
    {
        var segs = t_segments;
        for (int k = 0; k < count && segs != null; k++)
        {
            throughput *= segs[k].Medium.Transmittance(segs[k].Ray, segs[k].Length);
            if (MathUtils.NearZero(throughput)) return Vector3.Zero;
        }
        return throughput;
    }

    private static void Cross(ref MediumStack media, bool frontFace, IMedium? interior, IHittable? entity)
    {
        if (frontFace) media.Push(interior, entity);
        else media.Pop();
    }

    /// <summary>
    /// Shadow ray from a shading point to a point sampled ON a light's surface
    /// (area, sphere, emissive geometry), PBRT's "spawn ray to": both ends are
    /// pushed off their surfaces along their own normals, the receiver's toward
    /// its side and the light's toward the receiver, and the ray runs exactly
    /// between the two shifted points. The light's surface then lies strictly
    /// beyond the end of the ray, so the emitter can never shadow itself.
    /// The previous form (shifted origin, direction measured from the unshifted
    /// point, end at the light's surface) crossed the emitter whenever it was
    /// seen away from its normal: no direct light beyond ~52° from an area
    /// light's normal, and grazing samples of small spheres lost to the float
    /// precision of the tangent intersection (PIANO-REVIEW.md §3.19).
    /// </summary>
    public static Vector3 TransmittanceToPoint(IHittable world, Vector3 hitPoint, Vector3 surfaceNormal,
                                               Vector3 target, Vector3 targetNormal, float time)
    {
        Vector3 origin = MathUtils.OffsetToward(hitPoint, surfaceNormal, target - hitPoint);
        // Shift the target off the emitter on the receiver's side.
        if (Vector3.Dot(targetNormal, origin - target) < 0f) targetNormal = -targetNormal;
        Vector3 end = MathUtils.OffsetOrigin(target, targetNormal);
        Vector3 toEnd = end - origin;
        float distance = toEnd.Length();
        // Receiver touching the emitter: nothing can lie in between.
        if (distance <= 2f * MathUtils.OffsetDistance(origin))
            return Vector3.One;
        var ray = new Ray(origin, toEnd / distance, time);
        return Transmittance(world, ray, MathUtils.RayEpsilon, distance);
    }

    public static Vector3 Transmittance(IHittable world, Ray ray, float tMin, float tMax, int maxBounces = 8)
    {
        _lastCrossedTransparent = false;
        Vector3 throughput = Vector3.One;
        Vector3 origin = ray.Origin;
        Vector3 dir = ray.Direction;
        float remaining = tMax;

        // Beer-Lambert state across consecutive interfaces. When a front-face
        // hit reports a non-zero absorption, we record the entry point and
        // the per-channel σ_a; on the next hit we apply exp(−σ_a · d) over
        // the interior segment before processing that hit's tint. Single-
        // medium tracking — nested glass-in-glass uses the latest medium and
        // overlapping volumes are not stacked, the same simplification the
        // surface BSDF medium-switch uses elsewhere.
        Vector3 currentSigma = Vector3.Zero;
        Vector3 entryPoint = default;
        bool inMedium = false;

        bool media = t_mediaOn;
        int segCount = 0;
        MediumStack stack = media ? t_media : default;
        IMedium? global = media ? t_globalMedium : null;
        // Leaving through the receiver's far side crosses its boundary.
        if (media && t_receiverCrosses && Vector3.Dot(dir, t_receiverSide) < 0f)
            Cross(ref stack, t_receiverFront, t_receiverInterior, t_receiverEntity);

        for (int i = 0; i < maxBounces; i++)
        {
            var rec = new HitRecord();
            // Inherit the caller's shutter time so transparent-shadow walking
            // tests occlusion against animated geometry at the path's instant.
            var stepRay = new Ray(origin, dir, ray.Time);
            bool hit = world.Hit(stepRay, tMin, remaining, ref rec);

            // The medium over this stretch, up to the hit or the end: noted,
            // and tracked only once no surface has blocked the ray.
            if (media && (stack.Top ?? global) is { } m)
            {
                var segs = t_segments ??= new MediumSegment[MaxSegments];
                if (segCount < MaxSegments) segs[segCount++] = new MediumSegment(m, stepRay, hit ? rec.T : remaining);
            }
            if (!hit)
                return ThroughMedia(throughput, segCount);

            // ── Shadow visibility (Arnold `visibility.shadow` / Cycles
            //    "Ray Visibility → Shadow") ───────────────────────────────
            // A surface flagged invisible to shadow rays does not cast or
            // receive a shadow contribution. Advance the ray past the hit
            // without touching the throughput so light continues unimpeded
            // — exactly the behaviour expected from a ground configured
            // with `visibility.shadow: false`.
            if ((rec.VisibilityMask & HitVisibilityMask.Shadow) != 0)
            {
                Vector3 advanceN = Vector3.Dot(dir, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
                origin = MathUtils.OffsetOrigin(rec.Point, advanceN);
                remaining -= rec.T + 2f * MathUtils.OffsetDistance(rec.Point);
                if (remaining <= tMin) return ThroughMedia(throughput, segCount);
                continue;
            }

            if (_passThroughEntity != null && ReferenceEquals(rec.EntityRoot, _passThroughEntity))
            {
                // The walk's own boundary: leaving it pops its medium.
                if (media)
                {
                    if (ReferenceEquals(stack.TopEntity, rec.EntityRoot)) stack.Pop();
                    else stack.Push(rec.MediumIface.Interior, rec.EntityRoot);
                }
                Vector3 passN = Vector3.Dot(dir, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
                origin = MathUtils.OffsetOrigin(rec.Point, passN);
                remaining -= rec.T + 2f * MathUtils.OffsetDistance(rec.Point);
                if (remaining <= tMin) return ThroughMedia(throughput, segCount);
                continue;
            }

            if (rec.Material == null)
                return Vector3.Zero;

            // Opaque mode still lets straight transmission through: crossing an
            // index-matched shell or a thin sheet is the exact transport.
            if (_blockAllTransmission && !rec.Material.TransmitsStraight(rec))
                return Vector3.Zero;

            // Photon caustics active: a smooth specular transmissive interface
            // (clear glass/water) is opaque to NEE here — the refracted, tinted
            // light it transmits is delivered by the photon map, so the glass
            // casts a proper (coloured) refractive shadow instead of a fake
            // straight-through fill. Rough/frosted transmission is untouched.
            if (_blockSpecularTransmission && rec.Material.IsSpecularTransmissive(rec))
                return Vector3.Zero;

            // Beer-Lambert over the segment we just traversed inside a medium.
            if (inMedium)
            {
                float d = (rec.Point - entryPoint).Length();
                throughput = new Vector3(
                    throughput.X * MathF.Exp(-currentSigma.X * d),
                    throughput.Y * MathF.Exp(-currentSigma.Y * d),
                    throughput.Z * MathF.Exp(-currentSigma.Z * d));
                inMedium = false;
                if (MathUtils.NearZero(throughput))
                    return Vector3.Zero;
            }

            Vector3 t = rec.Material.ShadowTransmittance(dir, rec);
            if (MathUtils.NearZero(t))
                return Vector3.Zero;

            throughput *= t;
            if (MathUtils.NearZero(throughput))
                return Vector3.Zero;
            _lastCrossedTransparent = true;

            if (media && IsMediumBoundary(in rec))
                Cross(ref stack, rec.FrontFace, rec.MediumIface.Interior, rec.EntityRoot);

            // Entering a Beer-Lambert medium on this front-face hit?
            if (rec.FrontFace)
            {
                Vector3 sigma = rec.Material.ShadowAbsorption(rec);
                if (!MathUtils.NearZero(sigma))
                {
                    inMedium = true;
                    entryPoint = rec.Point;
                    currentSigma = sigma;
                }
            }

            // Advance the origin just past the hit, offset along the geometric
            // normal on the outgoing side. OffsetOrigin shifts perpendicular
            // to `dir`, so the parametric distance consumed is rec.T plus a
            // bounded tangential slack — subtract 2·Epsilon to stay inside the
            // segment safely and avoid re-hitting the same surface.
            Vector3 offsetN = Vector3.Dot(dir, rec.Normal) >= 0f ? rec.Normal : -rec.Normal;
            origin = MathUtils.OffsetOrigin(rec.Point, offsetN);
            remaining -= rec.T + 2f * MathUtils.OffsetDistance(rec.Point);
            if (remaining <= tMin) return ThroughMedia(throughput, segCount);
        }

        return Vector3.Zero;
    }
}
