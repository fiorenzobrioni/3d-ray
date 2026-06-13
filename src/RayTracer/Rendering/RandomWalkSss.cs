using System.Numerics;
using System.Runtime.CompilerServices;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Volumetrics;

namespace RayTracer.Rendering;

/// <summary>
/// Random-walk subsurface scattering integrator — partial of <see cref="Renderer"/>.
///
/// <para>Activated by <see cref="Renderer.ShadeSampleBounce"/> when an entry
/// refraction event lands on an entity bound to a <see cref="HomogeneousMedium"/>
/// with non-zero scattering coefficient. Replaces the regular volumetric path
/// for the duration of the interior traversal with a hero-wavelength
/// spectral-MIS estimator restricted to the bound entity (Cycles
/// <c>random_walk_v2</c>, Mitsuba <c>random_walk</c>).</para>
///
/// <para><b>Algorithm.</b> Free-flight distance sampling drives the walk:
/// at every step a hero channel <c>h</c> is selected with probability
/// <c>β[h] / sum(β)</c>, the next distance is drawn from
/// <c>p_h(t) = σ_t[h] · exp(-σ_t[h] · t)</c>, and the channel throughput is
/// updated through a balance-heuristic MIS combination of all three channels
/// — <c>β[c] *= σ_s[c] · exp(-σ_t[c] · t) / Σ_c q[c] · σ_t[c] · exp(-σ_t[c] · t)</c>
/// for scatter events and the analogous transmittance-only form for escape.
/// This is the spectrally unbiased counterpart of the single-channel
/// free-flight sampler (PBRT §15.2 / Wilkie-Weidlich 2014).</para>
///
/// <para><b>Restricted BVH.</b> The walk queries <see cref="HitRecord.EntityRoot"/>
/// (populated by <see cref="MediumBoundHittable"/>) instead of the global
/// world, so the boundary search cannot leak into adjacent geometry whose
/// surfaces happen to coincide with the bound entity's hull.</para>
///
/// <para><b>Exit handling.</b> When the free-flight sample crosses the
/// boundary, the medium is popped from the stack and the integrator hands
/// control back to the standard surface path via <see cref="Renderer.TraceRay"/>
/// from the exit point. No second Fresnel evaluation is applied at the exit
/// surface — the entry-side transmission factor (already in the caller's
/// throughput) is the only Fresnel coupling, matching the Cycles default. For
/// matched IORs (η = 1, the white-furnace configuration) the entry attenuation
/// degenerates to baseColor tint, giving the exact energy-preserving round-trip
/// the test asserts.</para>
///
/// <para><b>Russian Roulette.</b> Throughput-based termination kicks in after
/// <see cref="RandomWalkConfig.RrStartBounce"/> bounces using the walk's local
/// β (with a 5%-95% clamp on the survival probability). The max-bounces cap is
/// a hard ceiling on low-albedo paths where RR alone would not terminate in
/// reasonable time.</para>
/// </summary>
public partial class Renderer
{
    /// <summary>
    /// Demotes a refractive-entry medium to "absorption only" when
    /// <see cref="_sssMode"/> is <see cref="SssMode.Off"/>. The σ_a channel
    /// is preserved (so Beer-Lambert tint along the interior segment still
    /// applies, matching the look the artist set up in YAML), but σ_s is
    /// zeroed so the medium's <c>Sample()</c> path can't generate scattering
    /// events — the volumetric kernel degenerates to the legacy pure-
    /// absorption form. Pass-through for any non-scattering medium and for
    /// non-homogeneous media (whose semantics aren't tied to SSS dispatch).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IMedium? ResolvePushedMedium(IMedium? interior)
    {
        if (_sssMode != SssMode.Off) return interior;
        if (interior is HomogeneousMedium hm && IsScatteringMedium(hm))
            return new HomogeneousMedium(hm.SigmaA, Vector3.Zero, hm.Phase);
        return interior;
    }

    /// <summary>
    /// True when the medium is a participating scatterer (at least one channel
    /// of σ_s is positive). The SSS dispatch in <see cref="ShadeSampleBounce"/>
    /// keys on this — pure-absorption media are routed through the existing
    /// Beer-Lambert path, which is cheaper and identical in outcome since the
    /// random walk degenerates to a single straight transmission when σ_s = 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsScatteringMedium(HomogeneousMedium m)
    {
        Vector3 s = m.SigmaS;
        return s.X > 0f || s.Y > 0f || s.Z > 0f;
    }

    /// <summary>
    /// Walks the interior of the bound entity from the entry refraction point
    /// to the exit, integrating in-scattering NEE at every event and handing
    /// the escape ray back to <see cref="TraceRay"/>. Returns the radiance
    /// arriving at the entry point from inside (the integrand the caller
    /// multiplies by the entry-surface attenuation, exactly like the
    /// <c>indirect</c> term from a regular <see cref="TraceRay"/> recursion).
    ///
    /// <para>Convention: the caller pushes the medium onto <paramref name="mediums"/>
    /// before calling. This routine pops on every return path (escape, RR
    /// kill, max-bounces) so the caller's stack copy reflects "back outside"
    /// at the return — defensive consistency, the caller is the local copy
    /// owner and discards it anyway.</para>
    /// </summary>
    /// <param name="entryRay">Ray starting at the entry refraction point,
    ///   pointing into the medium interior.</param>
    /// <param name="medium">The homogeneous medium currently on top of
    ///   <paramref name="mediums"/>.</param>
    /// <param name="entityRoot">Restricted hittable representing the bound
    ///   entity's geometry (from <see cref="HitRecord.EntityRoot"/>).</param>
    /// <param name="mediums">Medium stack with <paramref name="medium"/> on
    ///   top. Popped before any return.</param>
    /// <param name="depth">Remaining surface-bounce budget passed to
    ///   <see cref="TraceRay"/> at escape. Independent of the walk's volume-
    ///   bounce budget (<see cref="RandomWalkConfig.MaxVolumeBounces"/>).</param>
    /// <param name="pathThroughput">Cumulative β arriving at the entry point.
    ///   Threaded into the escape recursion's RR / dead-path detection so the
    ///   outer world sees the full path importance. The walk does NOT
    ///   multiply its return value by this — the caller already accounts
    ///   for the entry attenuation via the <c>attenuation * indirect</c> term.</param>
    private Vector3 RandomWalkSubsurface(
        Ray entryRay,
        HomogeneousMedium medium,
        IHittable entityRoot,
        ref MediumStack mediums,
        ref IorStack iors,
        int depth,
        Vector3 pathThroughput)
    {
        Vector3 sigmaA = medium.SigmaA;
        Vector3 sigmaS = medium.SigmaS;
        Vector3 sigmaT = medium.SigmaT;
        IPhaseFunction phase = medium.Phase;

        // Volume-internal throughput. Starts at 1 — the caller will multiply
        // the walk's return by the entry-surface attenuation, mirroring the
        // <c>attenuation * indirect</c> contract of every other TraceRay
        // recursion.
        Vector3 relBeta = Vector3.One;
        Vector3 L = Vector3.Zero;
        Ray ray = entryRay;
        // MIS bookkeeping for the escape→emitter hand-off (fix: align with the
        // standard volumetric path). Until the first scatter the walk is a pure
        // specular continuation of the entry refraction, so an escape must be
        // treated as a delta bounce. After the first phase scatter the escape
        // ray carries that scatter's (non-delta) direction, and its pdf must be
        // forwarded so TraceRay can MIS-weight any emitter / sky it hits against
        // the NEE already performed at the scatter vertex.
        bool scattered = false;
        float lastPhasePdf = 0f;

        for (int b = 0; b < _walkConfig.MaxVolumeBounces; b++)
        {
            // ── Hero channel pick (proportional to current throughput) ─────
            float sumB = relBeta.X + relBeta.Y + relBeta.Z;
            if (sumB <= 1e-30f)
                break;
            float qX = relBeta.X / sumB;
            float qY = relBeta.Y / sumB;
            float qZ = relBeta.Z / sumB;

            float xi = MathUtils.RandomFloat();
            float sigmaT_h;
            if (xi < qX)           sigmaT_h = sigmaT.X;
            else if (xi < qX + qY) sigmaT_h = sigmaT.Y;
            else                   sigmaT_h = sigmaT.Z;

            // ── Free-flight distance sample on the hero channel ────────────
            // σ_t[h] = 0 is a degenerate "vacuum on the hero" — proceed
            // directly to the boundary (no scatter event possible on this
            // channel; the other channels would still scatter but the
            // selected hero drives this iteration).
            float tDist = sigmaT_h > 0f
                ? -MathF.Log(1f - MathUtils.RandomFloat()) / sigmaT_h
                : float.PositiveInfinity;

            // ── Boundary intersection on the bound entity only ─────────────
            // Perf: clip the boundary query to tDist (perf fix). On a scatter
            // step (the common case in dense media) we only need to know
            // whether a boundary lies *closer* than the sampled free-flight
            // distance — bounding the ray lets the entity's BVH (mesh busts /
            // statues) prune everything past tDist. Hit==true ⇒ boundary at
            // brec.T ≤ tDist ⇒ escape; Hit==false ⇒ scatter at tDist.
            float tBound = float.IsPositiveInfinity(tDist)
                ? MathUtils.Infinity : tDist;
            var brec = new HitRecord();
            bool gotBoundary = entityRoot.Hit(ray, 1e-7f,
                                              tBound, ref brec);

            if (gotBoundary)
            {
                // ── Escape: cross the boundary ─────────────────────────────
                float tBoundary = brec.T;
                Vector3 tr = ExpVec(-sigmaT * tBoundary);
                // p_escape(t = tBoundary) = Σ_c q[c] · exp(-σ_t[c] · tBoundary)
                float pdf = qX * tr.X + qY * tr.Y + qZ * tr.Z;
                if (pdf <= 1e-30f)
                {
                    mediums.Pop();
                    return L;
                }
                relBeta *= tr / pdf;

                // Hand back to TraceRay from the exit point. ray.Direction is
                // outward (parallel to the boundary normal at exit), so
                // offsetting the origin along it nudges the new ray past the
                // boundary in a single ε step.
                Vector3 exitPoint = ray.Origin + ray.Direction * tBoundary;
                Ray escapeRay = new(MathUtils.OffsetOrigin(exitPoint, ray.Direction),
                                    ray.Direction, ray.Time);

                // Pop BEFORE the outer recursion so TraceRay sees the medium
                // we just left as inactive (back to the outer stack / global).
                // The dielectric IOR pushed alongside it on entry is popped in
                // lock-step so relative-IOR tracking stays balanced on escape.
                mediums.Pop();
                iors.Pop();

                // MIS hand-off: a post-scatter escape is the BSDF-sampling half
                // of the NEE already done at the last scatter vertex — forward
                // the phase pdf and mark it non-delta so emitter / sky hits are
                // MIS-weighted (no double counting). A b==0 escape never did
                // NEE and is a specular continuation → delta.
                Vector3 outer = TraceRay(escapeRay, depth,
                                         prevBsdfPdf: scattered ? lastPhasePdf : 0f,
                                         prevIsDelta: !scattered,
                                         ref mediums, ref iors,
                                         pathThroughput: pathThroughput * relBeta);
                L += relBeta * outer;
                return L;
            }

            // ── Volume scattering event at p = ray(tDist) ──────────────────
            Vector3 p = ray.Origin + ray.Direction * tDist;
            Vector3 trS = ExpVec(-sigmaT * tDist);
            // p_scatter(t = tDist) = Σ_c q[c] · σ_t[c] · exp(-σ_t[c] · tDist)
            float pdfScatter = qX * sigmaT.X * trS.X
                             + qY * sigmaT.Y * trS.Y
                             + qZ * sigmaT.Z * trS.Z;
            if (pdfScatter <= 1e-30f)
            {
                mediums.Pop();
                return L;
            }
            relBeta *= sigmaS * trS / pdfScatter;

            // ── In-scattering NEE (optional per quality preset) ────────────
            // Pass entityRoot as the medium-bound entity so that the shadow
            // ray's medium-transmittance segment is clipped to the in-medium
            // portion only (p → boundary), instead of being computed over the
            // full Euclidean distance to the light. Without this clip a dense
            // SSS medium (σ_t ~ 10 for skin / wax) collapses every NEE
            // contribution to ~exp(-20) over the boundary-to-light air gap
            // that should not attenuate the shadow ray at all.
            if (_walkConfig.NeeInsideWalk && b < _walkConfig.NeeMaxBounce)
            {
                Vector3 Lnee = ComputeDirectLightingMedium(p, ray.Direction, medium, entityRoot, ray.Time);
                Lnee = ClampWalkInScattering(Lnee, b);
                L += relBeta * Lnee;
            }

            // ── Phase sample (HG): sampler matches density → phase/pdf = 1 ─
            // Keep the pdf for the escape→emitter MIS hand-off above.
            (Vector3 wi, float phasePdf) = phase.Sample(ray.Direction);
            ray = new Ray(p, wi, ray.Time);
            scattered = true;
            lastPhasePdf = phasePdf;

            // ── Russian Roulette ───────────────────────────────────────────
            // Survival on the *full* path importance (entry attenuation already
            // accumulated in pathThroughput × the walk-local relBeta), so walks
            // through dark / strongly tinted media terminate earlier. RR stays
            // unbiased; max-channel keeps it conservative.
            if (b >= _walkConfig.RrStartBounce)
            {
                Vector3 full = pathThroughput * relBeta;
                float qRr = MathF.Max(full.X, MathF.Max(full.Y, full.Z));
                qRr = MathF.Min(0.95f, MathF.Max(qRr, 0.05f));
                if (MathUtils.RandomFloat() > qRr)
                {
                    mediums.Pop();
                    return L;
                }
                relBeta /= qRr;
            }
        }

        // Max walk bounces exhausted without escape — kill the path. Drops
        // the residual energy intentionally; the RR clamp keeps this bias
        // small (~ qRr_min^budget) and bounded against fireflies on dense /
        // dark albedo media.
        mediums.Pop();
        return L;
    }

    /// <summary>
    /// Per-channel exp() helper, no allocations. Inlined into the walk's hot
    /// loop where we evaluate two transmittance vectors per iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 ExpVec(Vector3 v) => new(
        MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));

    /// <summary>
    /// Depth-aware NEE clamp inside the walk. Damps deep-volume fireflies
    /// where the per-channel throughput spike + a bright direct hit can spike
    /// the estimator. The ramp <c>1 / (1 + 0.1·b)</c> matches the heuristic
    /// in <see cref="RandomWalkConfig"/>'s rationale — at <c>b = 0</c> the
    /// clamp is identical to <see cref="ClampRadianceIndirect"/>; at
    /// <c>b = 30</c> it has tightened by ~4× — without flat-clamping early
    /// bounces that carry most of the legitimate radiance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ClampWalkInScattering(Vector3 color, int bounce)
    {
        if (float.IsNaN(color.X) || float.IsInfinity(color.X)) color.X = 0f;
        if (float.IsNaN(color.Y) || float.IsInfinity(color.Y)) color.Y = 0f;
        if (float.IsNaN(color.Z) || float.IsInfinity(color.Z)) color.Z = 0f;

        float limit = _indirectMaxSampleRadiance / (1f + 0.1f * bounce);
        float lum = MathUtils.Luminance(color);
        if (lum > limit && lum > 0f)
            color *= limit / lum;
        return Vector3.Max(color, Vector3.Zero);
    }
}
