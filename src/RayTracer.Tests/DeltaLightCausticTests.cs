using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Contract tests for the finite-virtual-emitter caustic support added to
/// <see cref="SphereLight"/>, <see cref="PointLight"/> and <see cref="SpotLight"/>.
/// These exercise <see cref="ILight.TrySampleEmissivePoint"/> (the area sample
/// the manifold walk consumes) and <see cref="ILight.DirectionalEmissionScale"/>
/// (the spot cone falloff) directly, in the analytic-oracle style of
/// <c>MnEeCausticTests</c>: the sampled point, normal, pdf and emitted radiance
/// all have closed-form values independent of the PRNG draw.
/// </summary>
public class DeltaLightCausticTests
{
    private const float Tol = 1e-4f;

    // ── SphereLight: true area emitter ───────────────────────────────────────

    [Fact]
    public void SphereLight_EmissivePoint_IsOnSurface_OutwardNormal_AreaPdf_Radiance()
    {
        var center = new Vector3(1f, 2f, -3f);
        const float R = 0.5f;
        var color = new Vector3(1f, 0.9f, 0.8f);
        const float intensity = 30f;
        var light = new SphereLight(center, R, color, intensity);

        float expectedPdf = 1f / (4f * MathF.PI * R * R);
        for (int i = 0; i < 64; i++)
        {
            Assert.True(light.TrySampleEmissivePoint(out var p, out var n, out var Le, out var pdf));

            // Point lies on the sphere of radius R, normal points outward.
            Assert.Equal(R, (p - center).Length(), 3);
            Assert.True(Vector3.Dot(n, Vector3.Normalize(p - center)) > 1f - Tol);
            Assert.True(MathF.Abs(n.Length() - 1f) < Tol);

            // Emitted radiance L_e = Color·Intensity; area pdf = 1/(4πR²).
            Assert.True((Le - color * intensity).Length() < Tol);
            Assert.Equal(expectedPdf, pdf, 3);
        }
    }

    /// <summary>
    /// Strong equivalence oracle: a <see cref="SphereLight"/> and a
    /// <see cref="GeometryLight"/> wrapping an emissive <see cref="Sphere"/> of the
    /// same radius and radiance must present the manifold walk the identical
    /// emissive-point distribution (same outward normal, same area pdf, same
    /// L_e) — so they produce the same caustic. SphereLight's <c>Color·Intensity</c>
    /// is exactly the emissive sphere's radiance.
    /// </summary>
    [Fact]
    public void SphereLight_EmissiveSampling_MatchesEmissiveSphereGeometryLight()
    {
        var center = new Vector3(0f, 1.4f, 0f);
        const float R = 1.0f;
        var radiance = new Vector3(30f, 30f, 30f);

        var sphereLight = new SphereLight(center, R, Vector3.One, intensity: 30f);

        var geom = new Sphere(center, R, new Lambertian(Vector3.One));
        var emissive = new Emissive(radiance, intensity: 1f);
        ILight geomLight = new GeometryLight(geom, emissive);

        float expectedPdf = 1f / (4f * MathF.PI * R * R);
        for (int i = 0; i < 64; i++)
        {
            Assert.True(sphereLight.TrySampleEmissivePoint(out _, out var nS, out var leS, out var pdfS));
            Assert.True(geomLight.TrySampleEmissivePoint(out var pG, out var nG, out var leG, out var pdfG));

            // Same area-measure pdf and radiance.
            Assert.Equal(expectedPdf, pdfS, 3);
            Assert.Equal(expectedPdf, pdfG, 3);
            Assert.True((leS - radiance).Length() < Tol);
            Assert.True((leG - radiance).Length() < Tol);

            // Both sample outward-facing surface points on the same sphere.
            Assert.Equal(R, (pG - center).Length(), 3);
            Assert.True(MathF.Abs(nS.Length() - 1f) < Tol);
            Assert.True(MathF.Abs(nG.Length() - 1f) < Tol);
        }
    }

    // ── PointLight: finite virtual bulb ──────────────────────────────────────

    [Theory]
    [InlineData(0f)]    // unset → default bulb radius
    [InlineData(0.2f)]  // explicit soft_radius
    public void PointLight_EmissivePoint_IsVirtualBulb_WithIntensityMatchedRadiance(float softRadius)
    {
        var pos = new Vector3(2f, 5f, 1f);
        var color = new Vector3(1f, 0.5f, 0.25f);
        const float intensity = 12f;
        var light = new PointLight(pos, color, intensity, softRadius);

        float r = softRadius > 0f ? softRadius : PointLight.DefaultBulbRadius;
        float expectedPdf = 1f / (4f * MathF.PI * r * r);
        var expectedLe = color * intensity / (MathF.PI * r * r);

        Assert.True(light.TrySampleEmissivePoint(out var p, out var n, out var Le, out var pdf));
        Assert.Equal(r, (p - pos).Length(), 3);                     // on the bulb surface
        Assert.True(Vector3.Dot(n, Vector3.Normalize(p - pos)) > 1f - Tol);  // outward normal
        Assert.Equal(expectedPdf, pdf, 2);
        Assert.True((Le - expectedLe).Length() < 1e-2f);
    }

    /// <summary>
    /// The caustic contribution is invariant to the bulb radius: the
    /// <c>L_e ∝ 1/(πr²)</c> radiance cancels the <c>1/pdf_A = 4πr²</c>. So
    /// <c>L_e / pdf_A</c> — the energy the estimator carries before geometry —
    /// must be independent of r. Guards against a future radius-scaling bug.
    /// </summary>
    [Fact]
    public void PointLight_CausticEnergy_IsRadiusInvariant()
    {
        var pos = new Vector3(0f, 4f, 0f);
        var color = Vector3.One;
        const float intensity = 10f;

        var small = new PointLight(pos, color, intensity, softRadius: 0.02f);
        var large = new PointLight(pos, color, intensity, softRadius: 0.5f);

        small.TrySampleEmissivePoint(out _, out _, out var leS, out var pdfS);
        large.TrySampleEmissivePoint(out _, out _, out var leL, out var pdfL);

        // L_e / pdf_A should match (≈ Color·Intensity·4) regardless of radius.
        var energyS = leS / pdfS;
        var energyL = leL / pdfL;
        Assert.True((energyS - energyL).Length() < 1e-2f,
            $"caustic energy depends on bulb radius: small={energyS} large={energyL}");
    }

    // ── SpotLight: virtual bulb + cone falloff ───────────────────────────────

    [Fact]
    public void SpotLight_EmissivePoint_MatchesPointLightBulbEnergy()
    {
        var pos = new Vector3(0f, 6f, 0f);
        var dir = new Vector3(0f, -1f, 0f);
        var color = Vector3.One;
        const float intensity = 20f;
        var spot = new SpotLight(pos, dir, color, intensity, innerAngleDeg: 15f, outerAngleDeg: 30f, softRadius: 0.1f);

        float r = 0.1f;
        Assert.True(spot.TrySampleEmissivePoint(out var p, out _, out var Le, out var pdf));
        Assert.Equal(r, (p - pos).Length(), 3);
        Assert.Equal(1f / (4f * MathF.PI * r * r), pdf, 2);
        Assert.True((Le - color * intensity / (MathF.PI * r * r)).Length() < 1e-1f);
    }

    [Fact]
    public void SpotLight_DirectionalEmissionScale_FollowsConeFalloff()
    {
        var dir = new Vector3(0f, -1f, 0f);
        ILight spot = new SpotLight(new Vector3(0f, 6f, 0f), dir, Vector3.One,
                                    intensity: 1f, innerAngleDeg: 15f, outerAngleDeg: 30f);

        // Along the beam axis (inside the inner cone) → full intensity.
        var onAxis = spot.DirectionalEmissionScale(dir);
        Assert.True((onAxis - Vector3.One).Length() < Tol);

        // Perpendicular to the axis (outside the outer cone) → zero.
        var perp = spot.DirectionalEmissionScale(new Vector3(1f, 0f, 0f));
        Assert.True(perp.Length() < Tol);

        // Between inner and outer cone (≈22.5°) → strictly within (0,1).
        float a = MathUtils.DegreesToRadians(22.5f);
        var mid = spot.DirectionalEmissionScale(
            new Vector3(MathF.Sin(a), -MathF.Cos(a), 0f));
        Assert.InRange(mid.X, 1e-3f, 1f - 1e-3f);
        Assert.Equal(mid.X, mid.Y, 5);
        Assert.Equal(mid.X, mid.Z, 5);
    }

    [Fact]
    public void DirectionalEmissionScale_IsOne_ForIsotropicLights()
    {
        var probe = Vector3.Normalize(new Vector3(0.3f, -1f, 0.2f));

        ILight sphere = new SphereLight(Vector3.Zero, 0.5f, Vector3.One, 20f);
        ILight point  = new PointLight(Vector3.Zero, Vector3.One, 10f);
        ILight area   = new AreaLight(new Vector3(-1, 5, -1), new Vector3(2, 0, 0),
                                      new Vector3(0, 0, 2), Vector3.One, 10f);
        ILight geom   = new GeometryLight(new Sphere(Vector3.Zero, 0.5f, new Lambertian(Vector3.One)),
                                          new Emissive(Vector3.One, 5f));

        Assert.True((sphere.DirectionalEmissionScale(probe) - Vector3.One).Length() < Tol);
        Assert.True((point.DirectionalEmissionScale(probe)  - Vector3.One).Length() < Tol);
        Assert.True((area.DirectionalEmissionScale(probe)   - Vector3.One).Length() < Tol);
        Assert.True((geom.DirectionalEmissionScale(probe)   - Vector3.One).Length() < Tol);
    }
}
