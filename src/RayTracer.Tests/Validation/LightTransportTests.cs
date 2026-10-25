using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Direct lighting from every light type against its integral
/// (PIANO-REVIEW.md Phase 6). A floor seen through a very narrow camera,
/// rendered at depth 2 (next-event estimation at the floor plus one BSDF
/// bounce that may reach the emitter, combined by MIS), must equal
/// ∫ L_e f cos dω over the light, computed here by quadrature with the
/// material's own BSDF. A diffuse and a glossy floor: MIS weights that do
/// not add up to one show most on the glossy one.
/// </summary>
[Collection("SamplerExclusive")]
public class LightTransportTests
{
    private readonly ITestOutputHelper _out;
    /// <summary>The integrator under test (the bidirectional ones rerun the whole class).</summary>
    protected virtual IntegratorKind Integrator => IntegratorKind.Path;

    public LightTransportTests(ITestOutputHelper output)
    {
        _out = output;
        // The tolerances assume the stratified sampler; other classes leave
        // the process-global kind at Prng.
        Core.Sampling.Sampler.SetKind(Core.Sampling.SamplerKind.Sobol);
    }

    private const int W = 4, H = 4;
    private static readonly Vector3 Cam = new(0f, 3f, 1.2f);

    private static IMaterial Floor(string kind) => kind is "glossy" or "mirror-ish"
        ? new Metal(new Vector3(0.9f), kind == "glossy" ? 0.12f : 0.04f)
        : new Lambertian(new Vector3(0.8f));

    /// <summary>The direction the floor reflects the camera into: where a glossy lobe peaks.</summary>
    private static readonly Vector3 Specular = Vector3.Normalize(new Vector3(-Cam.X, Cam.Y, -Cam.Z));

    private static Quad FloorQuad(IMaterial m) =>
        new(new Vector3(-50, 0, 50), new Vector3(100, 0, 0), new Vector3(0, 0, -100), m);

    /// <summary>The floor point the camera looks at, as the renderer's hit record has it.</summary>
    private static (HitRecord Rec, Vector3 V) FloorHit(IMaterial m)
    {
        var rec = new HitRecord();
        Vector3 d = Vector3.Normalize(-Cam);
        Assert.True(FloorQuad(m).Hit(new Ray(Cam, d), 1e-4f, 1e4f, ref rec));
        return (rec, -d);
    }

    private static Vector3 F(IMaterial m, in HitRecord rec, Vector3 v, Vector3 l)
        => m.Evaluate(v, l, in rec) * MathF.Max(0f, Vector3.Dot(rec.Normal, l));

    /// <summary>
    /// ∫ over a cone of directions of Le(ω) f cos dω: midpoint rule uniform in
    /// the angle from the axis (dω = sin θ dθ dφ), fine near the axis, where a
    /// near-mirror lobe aimed at the light is a fraction of a degree wide.
    /// </summary>
    private static Vector3 Cone(IMaterial m, Vector3 axis, float cosMax, Func<Vector3, float, Vector3> le,
                                int nTheta = 1500, int nPhi = 360)
    {
        var (rec, v) = FloorHit(m);
        MathUtils.OrthonormalBasis(axis, out Vector3 t, out Vector3 b);
        double thetaMax = Math.Acos(cosMax), dTheta = thetaMax / nTheta, dPhi = 2 * Math.PI / nPhi;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < nTheta; i++)
        {
            double theta = (i + 0.5) * dTheta;
            float c = (float)Math.Cos(theta), s = (float)Math.Sin(theta);
            Vector3 ring = Vector3.Zero;
            for (int j = 0; j < nPhi; j++)
            {
                float phi = (float)((j + 0.5) * dPhi);
                Vector3 l = c * axis + s * (MathF.Cos(phi) * t + MathF.Sin(phi) * b);
                ring += le(l, c) * F(m, in rec, v, l);
            }
            sum += ring * (float)(Math.Sin(theta) * dTheta * dPhi);
        }
        return sum;
    }

    /// <summary>∫ over a one-sided rectangle (normal u × v) of Le f cos cos' / d² dA.</summary>
    private static Vector3 Rect(IMaterial m, Vector3 corner, Vector3 u, Vector3 v, Vector3 le, int n = 400)
    {
        var (rec, view) = FloorHit(m);
        Vector3 cross = Vector3.Cross(u, v);
        float area = cross.Length();
        Vector3 nl = cross / area;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                Vector3 p = corner + (i + 0.5f) / n * u + (j + 0.5f) / n * v;
                Vector3 d = p - rec.Point;
                float d2 = d.LengthSquared();
                Vector3 l = d / MathF.Sqrt(d2);
                float cosL = Vector3.Dot(-l, nl);
                if (cosL <= 0f) continue;
                sum += le * F(m, in rec, view, l) * cosL / d2;
            }
        return sum * (area / (n * n));
    }

    private (Vector3 Mean, Vector3 StdErr) Render(IMaterial floor, List<IHittable> extra, List<ILight> lights,
                                                 SkySettings sky, int spp,
                                                 LightSamplingStrategy strategy = LightSamplingStrategy.All)
    {
        var objects = new List<IHittable> { FloorQuad(floor) };
        objects.AddRange(extra);
        var world = new HittableList(objects);
        var cam = RenderHarness.LookAt(Cam, Vector3.Zero, 0.01f, W, H);
        var img = RenderHarness.RenderLinear(world, lights, sky, cam, W, H, spp, depth: 2,
                                             reference: true, lightSampling: strategy, integrator: Integrator);
        Vector3 mean = RenderHarness.Mean(img);
        Vector3 var = Vector3.Zero;
        foreach (var px in img) var += (px - mean) * (px - mean);
        var /= img.Length - 1;
        return (mean, Vector3.SquareRoot(var / img.Length));
    }

    private void Check(string what, Vector3 got, Vector3 se, Vector3 expected, float relTol = 0.01f)
    {
        float g = MathUtils.Luminance(got), e = MathUtils.Luminance(expected), s = MathUtils.Luminance(se);
        _out.WriteLine($"{what}: rendered {g:F5} (±{s:F5}), integral {e:F5}, ratio {g / e:F4}");
        Assert.True(MathF.Abs(g - e) <= MathF.Max(4f * s, relTol * e),
            $"{what}: rendered {g:F5} ±{s:F5}, integral {e:F5} (ratio {g / e:F4})");
    }

    private static SkySettings Black => new(Vector3.Zero);

    public static IEnumerable<object[]> Floors => new[] { new object[] { "diffuse" }, new object[] { "glossy" } };

    [Theory, MemberData(nameof(Floors))]
    public void PointLight(string floor)
    {
        var m = Floor(floor);
        var pos = new Vector3(0.5f, 2f, 0.3f);
        var (rec, v) = FloorHit(m);
        Vector3 d = pos - rec.Point;
        Vector3 expected = 10f / d.LengthSquared() * F(m, in rec, v, Vector3.Normalize(d));
        var (got, se) = Render(m, new(), new() { new Lights.PointLight(pos, Vector3.One, 10f) }, Black, 64);
        Check("point", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void SpotLight(string floor)
    {
        var m = Floor(floor);
        var pos = new Vector3(0.3f, 2f, -0.2f);
        var dir = Vector3.Normalize(new Vector3(0.05f, 0f, 0f) - pos);
        var spot = new Lights.SpotLight(pos, dir, Vector3.One, 10f, 2f, 6f);
        var (rec, v) = FloorHit(m);
        Vector3 d = pos - rec.Point;
        float cos = Vector3.Dot(-Vector3.Normalize(d), dir);
        float fall = Math.Clamp((cos - spot.CosOuterAngle) / (spot.CosInnerAngle - spot.CosOuterAngle), 0f, 1f);
        Vector3 expected = 10f * fall * fall / d.LengthSquared() * F(m, in rec, v, Vector3.Normalize(d));
        var (got, se) = Render(m, new(), new() { spot }, Black, 64);
        Check("spot", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void DirectionalLight_Delta(string floor)
    {
        var m = Floor(floor);
        var dir = Vector3.Normalize(new Vector3(-0.3f, -1f, -0.2f));
        var (rec, v) = FloorHit(m);
        Vector3 expected = 2f * F(m, in rec, v, -dir);
        var (got, se) = Render(m, new(), new() { new DirectionalLight(dir, Vector3.One, 2f) }, Black, 64);
        Check("directional", got, se, expected);
    }

    [Theory]
    [InlineData("diffuse", false)]
    [InlineData("glossy", false)]
    [InlineData("glossy", true)]
    [InlineData("mirror-ish", true)]
    public void DirectionalLight_Disc(string floor, bool inHighlight)
    {
        // `intensity` is the irradiance of the whole disc: radiance I / Ω.
        // No BSDF ray can reach the disc, so its samples keep their whole weight.
        var m = Floor(floor);
        var dir = inHighlight ? -Specular : Vector3.Normalize(new Vector3(-0.3f, -1f, -0.2f));
        float cosMax = MathF.Cos(4f * MathF.PI / 180f);
        float omega = 2f * MathF.PI * (1f - cosMax);
        Vector3 expected = Cone(m, -dir, cosMax, (_, _) => new Vector3(2f / omega));
        var (got, se) = Render(m, new(), new() { new DirectionalLight(dir, Vector3.One, 2f, 4f, 16) }, Black, 256);
        Check("directional disc", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void AreaLight(string floor)
    {
        var m = Floor(floor);
        Vector3 c = new(-0.6f, 1.5f, -0.5f), u = new(0.8f, 0, 0), vv = new(0, 0, 0.6f);
        var proxyMat = new Emissive(Vector3.One, 5f) { IsLightProxy = true };
        var proxy = new BackFaceCulledHittable(new Quad(c, u, vv, proxyMat));
        var light = new Lights.AreaLight(c, u, vv, Vector3.One, 5f, 16, 0f, proxyMat);
        Vector3 expected = Rect(m, c, u, vv, new Vector3(5f));
        var (got, se) = Render(m, new() { proxy }, new() { light }, Black, 256);
        Check("area", got, se, expected);
    }

    [Theory]
    [InlineData("diffuse", 8)]
    [InlineData("glossy", 8)]
    [InlineData("diffuse", 5)]
    [InlineData("diffuse", 12)]
    public void AreaLight_AnyShadowSampleCount(string floor, int samples)
    {
        // A count that is not a square used to leave the last cells of the
        // ⌈√n⌉ × ⌈√n⌉ grid unsampled: 8 samples never reached a ninth of the light.
        var m = Floor(floor);
        Vector3 c = new(-1f, 2f, -1f), u = new(2f, 0, 0), vv = new(0, 0, 2f);
        var proxyMat = new Emissive(Vector3.One, 0.5f) { IsLightProxy = true };
        var light = new Lights.AreaLight(c, u, vv, Vector3.One, 0.5f, samples, 0f, proxyMat);
        Vector3 expected = Rect(m, c, u, vv, new Vector3(0.5f));
        var (got, se) = Render(m, new() { new BackFaceCulledHittable(new Quad(c, u, vv, proxyMat)) }, new() { light }, Black, 2048);
        Check($"area, {samples} samples", got, se, expected, 0.004f);
    }

    [Theory, MemberData(nameof(Floors))]
    public void SphereLight(string floor)
    {
        var m = Floor(floor);
        var centre = new Vector3(0.4f, 1.2f, 0.2f);
        const float r = 0.3f;
        var proxyMat = new Emissive(Vector3.One, 5f) { IsLightProxy = true };
        var light = new Lights.SphereLight(centre, r, Vector3.One, 5f, 16, proxyMat);
        var (rec, _) = FloorHit(m);
        Vector3 toC = centre - rec.Point;
        float cosMax = MathF.Sqrt(1f - r * r / toC.LengthSquared());
        Vector3 expected = Cone(m, Vector3.Normalize(toC), cosMax, (_, _) => new Vector3(5f));
        var (got, se) = Render(m, new() { new Sphere(centre, r, proxyMat) }, new() { light }, Black, 256);
        Check("sphere", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void GeometryLight_Quad(string floor)
    {
        var m = Floor(floor);
        Vector3 c = new(-0.6f, 1.5f, -0.5f), u = new(0.8f, 0, 0), vv = new(0, 0, 0.6f);
        var em = new Emissive(Vector3.One, 5f);
        var quad = new Quad(c, u, vv, em);
        Vector3 expected = Rect(m, c, u, vv, new Vector3(5f));
        var (got, se) = Render(m, new() { quad }, new() { new GeometryLight(quad, em, 16) }, Black, 256);
        Check("emissive quad", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void GeometryLight_Sphere(string floor)
    {
        var m = Floor(floor);
        var centre = new Vector3(0.4f, 1.2f, 0.2f);
        const float r = 0.3f;
        var em = new Emissive(Vector3.One, 5f);
        var sphere = new Sphere(centre, r, em);
        var (rec, _) = FloorHit(m);
        Vector3 toC = centre - rec.Point;
        float cosMax = MathF.Sqrt(1f - r * r / toC.LengthSquared());
        Vector3 expected = Cone(m, Vector3.Normalize(toC), cosMax, (_, _) => new Vector3(5f));
        var (got, se) = Render(m, new() { sphere }, new() { new GeometryLight(sphere, em, 16) }, Black, 256);
        Check("emissive sphere", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void GeometryLight_NonUniformlyScaled(string floor)
    {
        // An emissive ellipsoid (a unit sphere scaled 0.15 × 0.5 × 0.3 and
        // turned): its area samples are not uniform in world space, so the MIS
        // weight of a BSDF ray that hits it must use the density at the point
        // hit (ISamplable.AreaPdf), not an average area.
        var m = Floor(floor);
        var em = new Emissive(Vector3.One, 5f);
        var matrix = Matrix4x4.CreateScale(0.15f, 0.5f, 0.3f) * Matrix4x4.CreateRotationZ(0.7f)
                   * Matrix4x4.CreateTranslation(0.3f, 1.1f, 0.2f);
        var ellipsoid = new Transform(new Sphere(Vector3.Zero, 1f, em), matrix);
        // Expected: ∫ Le f cos cos' / d² dA over the ellipsoid, the surface
        // sampled uniformly in object space and weighted by the Jacobian.
        var (rec, v) = FloorHit(m);
        Matrix4x4.Invert(matrix, out var inv);
        var normalMatrix = Matrix4x4.Transpose(inv);
        float det = MathF.Abs(matrix.GetDeterminant());
        var rng = new Random(3);
        double sum = 0;
        const int n = 2_000_000;
        for (int i = 0; i < n; i++)
        {
            float z = 2f * rng.NextSingle() - 1f, phi = 2f * MathF.PI * rng.NextSingle(), sr = MathF.Sqrt(1f - z * z);
            var nObj = new Vector3(sr * MathF.Cos(phi), sr * MathF.Sin(phi), z);
            Vector3 nW = Vector3.TransformNormal(nObj, normalMatrix);
            float jac = det * nW.Length();
            nW = Vector3.Normalize(nW);
            Vector3 p = Vector3.Transform(nObj, matrix);
            Vector3 d = p - rec.Point;
            float d2 = d.LengthSquared();
            Vector3 l = d / MathF.Sqrt(d2);
            float cosL = Vector3.Dot(-l, nW);
            if (cosL <= 0f) continue;   // the far side (convex: no other occlusion)
            sum += 5.0 * MathUtils.Luminance(F(m, in rec, v, l)) * cosL / d2 * (4 * Math.PI * jac);
        }
        float expected = (float)(sum / n);
        var (got, se) = Render(m, new() { ellipsoid }, new() { new GeometryLight(ellipsoid, em, 16) }, Black, 256);
        Check("emissive ellipsoid", got, se, new Vector3(expected));
    }

    [Theory, MemberData(nameof(Floors))]
    public void EnvironmentLight_UniformSky(string floor)
    {
        var m = Floor(floor);
        var sky = new SkySettings(new Vector3(0.5f));
        Vector3 expected = Cone(m, Vector3.UnitY, 0f, (_, _) => new Vector3(0.5f));
        var (got, se) = Render(m, new(), new() { new EnvironmentLight(sky, 4) }, sky, 512);
        Check("environment", got, se, expected);
    }

    private static EnvironmentMap BlobSky()
    {
        // An even sky of 0.3 with a bright patch (20, 15° wide) up and to the side.
        const int w = 64, h = 32;
        var px = new float[w * h * 3];
        Vector3 c = EnvironmentMap.DirectionOf(40f * MathF.PI / 180f, 35f * MathF.PI / 180f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var d = EnvironmentMap.DirectionOf(((x + 0.5f) / w - 0.5f) * 2f * MathF.PI, (0.5f - (y + 0.5f) / h) * MathF.PI);
                float L = Vector3.Dot(d, c) > MathF.Cos(15f * MathF.PI / 180f) ? 20f : 0.3f;
                px[(y * w + x) * 3] = L; px[(y * w + x) * 3 + 1] = 0.9f * L; px[(y * w + x) * 3 + 2] = 0.8f * L;
            }
        return new EnvironmentMap(px, w, h);
    }

    [Theory]
    [InlineData("diffuse", true)]
    [InlineData("diffuse", false)]
    [InlineData("glossy", true)]
    [InlineData("glossy", false)]
    public void EnvironmentLight_Hdri(string floor, bool compensation)
    {
        // With or without MIS compensation (Karlík 2019) the estimate is the
        // integral; compensation only moves which strategy carries what.
        var m = Floor(floor);
        var map = BlobSky();
        var sky = new SkySettings(map);
        Vector3 expected = Cone(m, Vector3.UnitY, 0f, (d, _) => map.Sample(d), 1500, 720);
        var lights = new List<ILight> { new EnvironmentLight(sky, 4) };
        map.UseMisCompensation = compensation;
        // The renderer turns compensation on for an HDRI sky unless shadows are transparent.
        var objects = new List<IHittable> { FloorQuad(m) };
        var cam = RenderHarness.LookAt(Cam, Vector3.Zero, 0.01f, W, H);
        var renderer = new Renderer(new HittableList(objects), cam, lights, sky, 1024, 2,
                                    lightSamplingStrategy: LightSamplingStrategy.All, referenceMode: true,
                                    transparentShadows: !compensation);
        renderer.Integrator = Integrator;
        var result = renderer.Render(W, H, new RenderCaptureOptions { CaptureBeautyHalves = true });
        var img = new Vector3[H, W];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) img[y, x] = result.Buffers!.Beauty.GetRgb(x, y);
        Vector3 mean = RenderHarness.Mean(img), var = Vector3.Zero;
        foreach (var px in img) var += (px - mean) * (px - mean);
        Check($"hdri {(compensation ? "compensated" : "plain")}", mean, Vector3.SquareRoot(var / (img.Length - 1) / img.Length), expected);
    }

    [Theory]
    [InlineData("diffuse", 0.265f, false)]
    [InlineData("glossy", 0.265f, false)]
    [InlineData("glossy", 3f, false)]
    [InlineData("glossy", 0.265f, true)]
    [InlineData("mirror-ish", 3f, true)]
    public void PhysicalSun(string floor, float halfDeg, bool inHighlight)
    {
        var m = Floor(floor);
        var toSun = inHighlight ? Specular : Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));
        float cosMax = MathF.Cos(halfDeg * MathF.PI / 180f);
        // Same irradiance whatever the disc: radiance scaled by 1/Ω.
        float L = 3f / (2f * MathF.PI * (1f - cosMax));
        Vector3 expected = Cone(m, toSun, cosMax, (_, c) => new Vector3(L * Lights.PhysicalSun.LimbFactor(c, cosMax)));
        var sun = new Lights.PhysicalSun(toSun, Vector3.One, L, halfDeg, limbDarkening: true, shadowSamples: 16);
        var (got, se) = Render(m, new(), new() { sun }, Black, 256);
        Check($"sun {halfDeg}° {(inHighlight ? "in the highlight" : "")}", got, se, expected);
    }

    [Theory, MemberData(nameof(Floors))]
    public void PortalLight_AddsNoLight(string floor)
    {
        // A portal only steers samples towards part of the sky: with the sky
        // also sampled as an environment light, the result is the sky's.
        var m = Floor(floor);
        var sky = new SkySettings(new Vector3(0.5f));
        var portal = new Lights.PortalLight(sky, new Vector3(-1, 2, -1), new Vector3(2, 0, 0), new Vector3(0, 0, 2), 8);
        Vector3 expected = Cone(m, Vector3.UnitY, 0f, (_, _) => new Vector3(0.5f));
        var (got, se) = Render(m, new(), new() { new EnvironmentLight(sky, 4), portal }, sky, 512);
        Check("portal + environment", got, se, expected);
    }

    [Theory]
    [InlineData("diffuse", LightSamplingStrategy.Power)]
    [InlineData("glossy", LightSamplingStrategy.Power)]
    [InlineData("glossy", LightSamplingStrategy.Uniform)]
    [InlineData("diffuse", LightSamplingStrategy.Bvh)]
    [InlineData("glossy", LightSamplingStrategy.Bvh)]
    public void OneLightPicked_IsUnbiased(string floor, LightSamplingStrategy strategy)
    {
        // Several non-delta lights, one picked per vertex: the MIS weight of a
        // BSDF ray that reaches a light must use the same pick probability as
        // the light sample.
        var m = Floor(floor);
        Vector3 c = new(-0.6f, 1.5f, -0.5f), u = new(0.8f, 0, 0), vv = new(0, 0, 0.6f);
        var aMat = new Emissive(Vector3.One, 5f) { IsLightProxy = true };
        var area = new Lights.AreaLight(c, u, vv, Vector3.One, 5f, 4, 0f, aMat);
        var centre = new Vector3(0.4f, 1.2f, 0.2f);
        var sMat = new Emissive(Vector3.One, 2f) { IsLightProxy = true };
        var sphere = new Lights.SphereLight(centre, 0.3f, Vector3.One, 2f, 4, sMat);
        var (rec, _) = FloorHit(m);
        Vector3 toC = centre - rec.Point;
        Vector3 expected = Rect(m, c, u, vv, new Vector3(5f))
                         + Cone(m, Vector3.Normalize(toC), MathF.Sqrt(1f - 0.09f / toC.LengthSquared()), (_, _) => new Vector3(2f));
        var (got, se) = Render(m, new() { new BackFaceCulledHittable(new Quad(c, u, vv, aMat)), new Sphere(centre, 0.3f, sMat) },
                               new() { area, sphere }, Black, 1024, strategy);
        Check($"{strategy} pick", got, se, expected);
    }
}
