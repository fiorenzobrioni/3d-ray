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
/// <para><c>maxBounces</c> caps the number of transparent interfaces crossed.
/// A clear glass shell crosses 2 (entry + exit); nested glass-in-glass crosses
/// 4. Reaching the cap with non-trivial residual is treated as opaque to stay
/// conservative.</para>
/// </summary>
public static class ShadowRay
{
    public static Vector3 Transmittance(IHittable world, Ray ray, float tMin, float tMax, int maxBounces = 8)
    {
        Vector3 throughput = Vector3.One;
        Vector3 origin = ray.Origin;
        Vector3 dir = ray.Direction;
        float remaining = tMax;

        for (int i = 0; i < maxBounces; i++)
        {
            var rec = new HitRecord();
            var stepRay = new Ray(origin, dir);
            if (!world.Hit(stepRay, tMin, remaining, ref rec))
                return throughput;

            if (rec.Material == null)
                return Vector3.Zero;

            Vector3 t = rec.Material.ShadowTransmittance(dir, rec);
            if (MathUtils.NearZero(t))
                return Vector3.Zero;

            throughput *= t;
            if (MathUtils.NearZero(throughput))
                return Vector3.Zero;

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
