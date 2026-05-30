using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;

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
/// but focused refractive caustics are NOT reproduced (those need MNEE or
/// photon mapping — see DEVLOG roadmap).</para>
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
    /// Per-thread switch: when set, the walker treats any surface flagged
    /// <c>caustic_caster</c> (<see cref="HitRecord.CausticCaster"/>) as fully
    /// opaque, returning <see cref="Vector3.Zero"/>. The renderer raises this
    /// only around the straight NEE shadow rays of a <c>caustic_receiver</c>
    /// shading point, so the focused light those casters transmit is supplied
    /// solely by Manifold NEE (<see cref="Rendering.ManifoldWalker"/>) and is
    /// not double-counted by the straight transparent shadow ray. Thread-local
    /// because the renderer shades pixels in parallel; left false on every other
    /// thread / shading point, preserving the Phase-1 soft transmitted shadow.
    /// </summary>
    [ThreadStatic] public static bool BlockCausticCasters;

    public static Vector3 Transmittance(IHittable world, Ray ray, float tMin, float tMax, int maxBounces = 8)
    {
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

        for (int i = 0; i < maxBounces; i++)
        {
            var rec = new HitRecord();
            var stepRay = new Ray(origin, dir);
            if (!world.Hit(stepRay, tMin, remaining, ref rec))
                return throughput;

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
                remaining -= rec.T + 2f * MathUtils.Epsilon;
                if (remaining <= tMin) return throughput;
                continue;
            }

            if (rec.Material == null)
                return Vector3.Zero;

            // Caustic caster: opaque to a caustic_receiver's straight shadow ray
            // (MNEE supplies the refracted light instead — see BlockCausticCasters).
            if (BlockCausticCasters && rec.CausticCaster)
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
            remaining -= rec.T + 2f * MathUtils.Epsilon;
            if (remaining <= tMin) return throughput;
        }

        return Vector3.Zero;
    }
}
