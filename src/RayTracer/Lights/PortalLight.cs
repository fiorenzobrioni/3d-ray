using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Rendering;

namespace RayTracer.Lights;

/// <summary>
/// Portal light — a virtual rectangle in the scene through which the
/// environment is observed. Critical for low-variance interior renders where
/// most of the IBL energy enters via small windows / skylights: sampling
/// arbitrary directions on the sky CDF wastes the &gt;99% of samples that hit
/// walls, while a portal restricts NEE to directions that actually reach the
/// outside.
///
/// <para><b>Algorithm</b> (Bitterli, Wyman, Pharr 2015 — "Portal-Masked
/// Environment Map Sampling"; also Mitsuba <c>emitters/portal.cpp</c>).
/// For each NEE sample:
/// <list type="number">
///   <item><description>Pick a uniform point <c>p</c> on the portal rectangle.</description></item>
///   <item><description>Direction <c>d = normalize(p − hitPoint)</c>.</description></item>
///   <item><description>Reject when the receiver lies on the back side of the portal (geometric horizon test).</description></item>
///   <item><description>Shadow-trace the ray (the portal is intangible — the trace continues past it to the world).</description></item>
///   <item><description>If the shadow ray escapes (no occluder), evaluate the sky in direction <c>d</c>.</description></item>
///   <item><description>PDF in solid angle: <c>p(d) = distance² / (area · cos(portalNormal, d))</c>.</description></item>
/// </list></para>
///
/// <para><b>YAML</b>
/// <code>
/// - type: portal
///   anchor:    [-2.0, 1.0,  0.0]   # one corner
///   u:         [ 1.0, 0.0,  0.0]   # edge along U
///   v:         [ 0.0, 2.0,  0.0]   # edge along V
///   shadow_samples: 8
/// </code>
/// The <c>anchor + U + V</c> parallelogram defines the portal. The portal
/// normal is <c>normalize(U × V)</c>; receivers on the wrong side return no
/// contribution. A portal carries no proxy geometry: it is invisible to camera
/// and to BSDF-sampled rays, contributing only via NEE.</para>
///
/// <para><b>MIS.</b> When combined with the environment light, the LightDistribution's
/// power-weighted picker will give the portal a disproportionately high pick
/// probability (it captures most of the indoor flux). The portal's PDF reaches
/// every direction it covers; outside that solid angle the PDF is 0 and BSDF
/// importance sampling carries the load.</para>
/// </summary>
public class PortalLight : ILight
{
    public Vector3 Anchor { get; }
    public Vector3 U { get; }
    public Vector3 V { get; }
    /// <summary>Outward portal normal (points TOWARDS the sky, away from the room).</summary>
    public Vector3 Normal { get; }
    public float Area { get; }

    /// <inheritdoc/>
    public int ShadowSamples { get; }

    /// <inheritdoc/>
    // Portal is an area-like emitter (PDF is the rectangle area density).
    public bool IsDelta => false;

    private readonly SkySettings _sky;
    private readonly int _sqrtSamples;
    private readonly float _invSqrtSamples;

    public PortalLight(SkySettings sky, Vector3 anchor, Vector3 u, Vector3 v, int shadowSamples = 8)
    {
        _sky = sky;
        Anchor = anchor;
        U = u;
        V = v;
        Vector3 cross = Vector3.Cross(u, v);
        Area = cross.Length();
        Normal = Area > 1e-8f ? cross / Area : Vector3.UnitY;

        ShadowSamples = Math.Max(1, shadowSamples);
        _sqrtSamples = (int)MathF.Ceiling(MathF.Sqrt(ShadowSamples));
        _invSqrtSamples = 1f / _sqrtSamples;
    }

    /// <summary>
    /// Approximate flux through the portal: hemispheric irradiance from the
    /// environment × portal area × π (Lambertian half-space integral). Used
    /// by the renderer's power-weighted light picker — high here so interior
    /// scenes route most NEE samples through the portal.
    /// </summary>
    public float ApproximatePower(AABB sceneBounds)
    {
        float L = _sky.EstimatedAverageLuminance;
        return L * Area * MathF.PI;
    }

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTest(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world)
        => IlluminateAndTestStratified(hitPoint, surfaceNormal, world, -1);

    public (bool InShadow, Vector3 Color, Vector3 DirToLight, float Distance)
        IlluminateAndTestStratified(Vector3 hitPoint, Vector3 surfaceNormal, IHittable world, int sampleIndex)
    {
        // Stratified sample over the portal rectangle.
        float xi1, xi2;
        if (sampleIndex >= 0)
        {
            int su = sampleIndex % _sqrtSamples;
            int sv = sampleIndex / _sqrtSamples;
            xi1 = (su + MathUtils.RandomFloat()) * _invSqrtSamples;
            xi2 = (sv + MathUtils.RandomFloat()) * _invSqrtSamples;
        }
        else
        {
            xi1 = MathUtils.RandomFloat();
            xi2 = MathUtils.RandomFloat();
        }

        Vector3 portalPoint = Anchor + xi1 * U + xi2 * V;
        Vector3 toPortal = portalPoint - hitPoint;
        float dist = toPortal.Length();
        if (dist < MathUtils.Epsilon)
            return (true, Vector3.Zero, Normal, 0f);
        Vector3 dir = toPortal / dist;

        // Receiver geometric horizon (skip when surfaceNormal is degenerate —
        // volumetric scattering points pass Vector3.Zero).
        if (surfaceNormal.LengthSquared() > 0f && Vector3.Dot(surfaceNormal, dir) <= 0f)
            return (true, Vector3.Zero, dir, dist);

        // Portal orientation — only contribute when the receiver is on the
        // inside (the side the portal normal points away from).
        float cosPortal = Vector3.Dot(-dir, Normal);
        if (cosPortal <= 0f)
            return (true, Vector3.Zero, dir, dist);

        // Shadow-trace through the scene up to infinity. The portal itself is
        // intangible — it carries no geometry, so any hit between the receiver
        // and infinity is a real occluder.
        Vector3 shadowOrigin = MathUtils.OffsetOrigin(hitPoint, surfaceNormal);
        var shadowRay = new Ray(shadowOrigin, dir);
        Vector3 trans = ShadowRay.Transmittance(world, shadowRay, MathUtils.Epsilon, MathUtils.Infinity);
        if (MathUtils.NearZero(trans))
            return (true, Vector3.Zero, dir, dist);

        // Sample the sky in direction `dir`. Use Shadow category (not Camera)
        // so the analytical sun is excluded — if a PhysicalSun is present it
        // is already in the NEE light pool independently.
        Vector3 skyRadiance = _sky.Sample(new Ray(shadowOrigin, dir),
                                          RayCategory.Shadow,
                                          includeAnalyticalSun: false);

        // Solid-angle PDF of the portal sample, converted from area PDF:
        //   p_a = 1 / area  →  p_ω = p_a · dist² / cosPortal
        float pdf = (dist * dist) / (Area * cosPortal);
        if (pdf <= 0f) return (true, Vector3.Zero, dir, dist);

        // NEE contribution: L / (pdf × N), the N·L factor is applied by the
        // shading pipeline.
        Vector3 contribution = skyRadiance * trans / (pdf * ShadowSamples);
        return (false, contribution, dir, dist);
    }

    public float PdfSolidAngle(Vector3 hitPoint, Vector3 wi)
    {
        // Ray-plane intersection on the portal plane.
        Vector3 anchorToHit = hitPoint - Anchor;
        float dirDotN = Vector3.Dot(wi, Normal);
        if (MathF.Abs(dirDotN) < 1e-6f) return 0f;
        float t = -Vector3.Dot(anchorToHit, Normal) / dirDotN;
        if (t <= 0f) return 0f;
        Vector3 hitOnPlane = hitPoint + wi * t;
        Vector3 rel = hitOnPlane - Anchor;
        // Solve rel = u·U + v·V over the parallelogram basis (2×2 system).
        float uu = Vector3.Dot(U, U);
        float vv = Vector3.Dot(V, V);
        float uv = Vector3.Dot(U, V);
        float det = uu * vv - uv * uv;
        if (det <= 1e-8f) return 0f;
        float ru = Vector3.Dot(rel, U);
        float rv = Vector3.Dot(rel, V);
        float u = (vv * ru - uv * rv) / det;
        float v = (uu * rv - uv * ru) / det;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;

        float cosPortal = MathF.Abs(dirDotN);
        return (t * t) / (Area * cosPortal);
    }
}
