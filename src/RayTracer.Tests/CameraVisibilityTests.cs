using System.Collections.Generic;
using System.Numerics;
using RayTracer.Camera;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Regression tests for the <c>visible_to_camera</c> light/entity flag
/// (Arnold's <c>camera</c> visibility, Cycles' "Ray Visibility → Camera").
///
/// <para>The flag must satisfy three independent invariants:</para>
/// <list type="number">
///   <item><description>A primary camera ray pointing straight at the
///   hidden proxy returns the background (sky), not the proxy's emission.</description></item>
///   <item><description>NEE direct illumination on a witness diffuse plane
///   is unchanged whether the light's proxy is visible to camera or not —
///   the lighting integral does not depend on a primary-ray visibility flag.</description></item>
///   <item><description>A perfect mirror still reflects the hidden light:
///   the mirror's primary-ray hit is at <c>depth == _maxDepth</c>, but the
///   reflected secondary ray runs at <c>depth - 1</c> and therefore falls
///   outside the camera-visibility filter, matching Arnold semantics.</description></item>
/// </list>
/// </summary>
public class CameraVisibilityTests
{
    private const int Spp   = 16;
    private const int Depth = 5;

    private static Camera.Camera MakeCamera(Vector3 from, Vector3 at, int w, int h, float fov = 30f) =>
        new(
            lookFrom:    from,
            lookAt:      at,
            vUp:         Vector3.UnitY,
            vFovDeg:     fov,
            aspectRatio: (float)w / h,
            aperture:    0f,
            focusDist:   (at - from).Length());

    private static Renderer MakeRenderer(IHittable world, Camera.Camera cam, List<ILight> lights) =>
        new(
            world:           world,
            camera:          cam,
            lights:          lights,
            sky:             new SkySettings(Vector3.Zero),
            samplesPerPixel: Spp,
            maxDepth:        Depth,
            verbose:         false);

    private static (IHittable World, SphereLight Light) BuildSceneWithLight(bool visibleToCamera)
    {
        // SphereLight at z = -3 directly in front of the camera at the origin,
        // looking down -Z. Identical to the SceneLoader's wiring:
        //   - proxy = Sphere with Emissive { IsLightProxy = true }
        //   - wrap in CameraInvisibleHittable when the flag is false
        //   - SphereLight constructor receives the proxy material for MIS
        var proxyMat = new Emissive(Vector3.One, intensity: 50f) { IsLightProxy = true };
        IHittable proxy = new Sphere(new Vector3(0f, 0f, -3f), 0.6f, proxyMat);
        if (!visibleToCamera) proxy = new CameraInvisibleHittable(proxy);

        var sphereLight = new SphereLight(
            center:        new Vector3(0f, 0f, -3f),
            radius:        0.6f,
            color:         Vector3.One,
            intensity:     50f,
            shadowSamples: 4,
            proxyMaterial: proxyMat);

        var world = new HittableList(new[] { proxy });
        return (world, sphereLight);
    }

    /// <summary>
    /// Primary camera ray straight at the proxy. With the flag off, the
    /// rendered pixel must equal the (black) sky — the proxy's emission must
    /// not contribute to the camera term.
    /// </summary>
    [Fact]
    public void HiddenLight_PrimaryRayDoesNotSeeProxy()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var (world, light) = BuildSceneWithLight(visibleToCamera: false);
        var camera = MakeCamera(Vector3.Zero, new Vector3(0f, 0f, -3f), 8, 8);
        var renderer = MakeRenderer(world, camera, new List<ILight> { light });

        var pixels = renderer.Render(8, 8);

        // Center pixel sees the proxy along the optical axis. Sky is black, so
        // the proxy's emission (color=1, intensity=50 → 50 per channel pre-
        // tone-map) would dominate if it leaked through. The renderer's
        // ACES + clamp post-processing maps 50 close to 1.0; we test against
        // a generous threshold that any actual leak would exceed.
        Vector3 center = pixels[4, 4];
        Assert.True(center.X < 0.05f, $"center.X = {center.X} (proxy leaked into primary ray)");
        Assert.True(center.Y < 0.05f, $"center.Y = {center.Y}");
        Assert.True(center.Z < 0.05f, $"center.Z = {center.Z}");
    }

    /// <summary>
    /// Sanity baseline — same scene, default <c>visible_to_camera = true</c>:
    /// the center pixel must be bright (the proxy emits 50× white).
    /// </summary>
    [Fact]
    public void VisibleLight_PrimaryRaySeesProxy()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var (world, light) = BuildSceneWithLight(visibleToCamera: true);
        var camera = MakeCamera(Vector3.Zero, new Vector3(0f, 0f, -3f), 8, 8);
        var renderer = MakeRenderer(world, camera, new List<ILight> { light });

        var pixels = renderer.Render(8, 8);

        Vector3 center = pixels[4, 4];
        // Bright emission after ACES + gamma should be well above any noise
        // floor on all channels.
        Assert.True(center.X > 0.4f, $"center.X = {center.X} (proxy not visible to primary ray)");
        Assert.True(center.Y > 0.4f, $"center.Y = {center.Y}");
        Assert.True(center.Z > 0.4f, $"center.Z = {center.Z}");
    }

    /// <summary>
    /// A white Lambertian plane offset from the camera receives the same
    /// direct illumination whether the light is camera-visible or not — NEE
    /// samples the light directly and the proxy filter only triggers on
    /// primary rays.
    /// </summary>
    [Fact]
    public void HiddenLight_StillIlluminatesViaNEE()
    {
        Sampler.SetKind(SamplerKind.Prng);

        // Floor at y = -1, white Lambertian. Camera looks down at it, light is
        // a sphere off-axis so the floor receives direct light from above.
        // The camera does NOT see the light directly (it's behind/above the
        // camera) — so the primary-ray filter is irrelevant for the floor
        // pixel: NEE must yield the same radiance in both configurations.
        Vector3 lightPos = new(0f, 3f, 0f);

        IHittable Build(bool visible)
        {
            var proxyMat = new Emissive(Vector3.One, intensity: 30f) { IsLightProxy = true };
            IHittable proxy = new Sphere(lightPos, 0.3f, proxyMat);
            if (!visible) proxy = new CameraInvisibleHittable(proxy);
            var floor = new InfinitePlane(
                point:    new Vector3(0f, -1f, 0f),
                normal:   Vector3.UnitY,
                material: new Lambertian(Vector3.One));
            return new HittableList(new IHittable[] { proxy, floor });
        }

        SphereLight MakeLight() => new(
            center:        lightPos,
            radius:        0.3f,
            color:         Vector3.One,
            intensity:     30f,
            shadowSamples: 4);

        // Camera looks at the floor square in front, light directly above.
        var camera = MakeCamera(
            from: new Vector3(0f, 1f, 4f),
            at:   new Vector3(0f, -1f, 0f),
            w: 16, h: 16);

        Vector3 AverageFloorRadiance(bool visible)
        {
            var renderer = MakeRenderer(Build(visible), camera, new List<ILight> { MakeLight() });
            var px = renderer.Render(16, 16);
            // Bottom half of the image looks at the floor.
            Vector3 sum = Vector3.Zero;
            int count = 0;
            for (int y = 9; y < 16; y++)
            for (int x = 0; x < 16; x++)
            { sum += px[y, x]; count++; }
            return sum / count;
        }

        var visibleAvg = AverageFloorRadiance(visible: true);
        var hiddenAvg  = AverageFloorRadiance(visible: false);

        // The mean radiance on the floor should match up to MC noise. The PRNG
        // sampler at 16 spp on 16×16 pixels gives plenty of samples; the
        // visible/hidden means typically agree within ~10%. We use a generous
        // 20% tolerance to leave room for the global RNG (DEVLOG bug #2).
        float rel = (visibleAvg - hiddenAvg).Length() /
                    System.MathF.Max(visibleAvg.Length(), 1e-4f);
        Assert.True(rel < 0.20f,
            $"floor mean diverged: visible={visibleAvg}, hidden={hiddenAvg}, rel={rel:F3}");
        // And both must be non-trivially bright.
        Assert.True(hiddenAvg.X > 0.05f, $"hidden floor too dark: {hiddenAvg}");
        Assert.True(visibleAvg.X > 0.05f, $"visible floor too dark: {visibleAvg}");
    }

    // ── Mirror-reflection geometry (shared by hidden/visible tests) ─────────
    //
    // Horizontal mirror floor at y=0 facing +Y; camera straight overhead at
    // (0, 5, -3) looking down; light directly above the camera at (0, 10,
    // -3). The center camera ray (0, -1, 0) hits the mirror at (0, 0, -3)
    // and reflects to (0, 1, 0) — straight up to the light.
    //
    // Mirror geometry: q = (-5, 0, 0), u = (10, 0, 0), v = (0, 0, -10) so
    // n = (u × v)/|.| = (0, +1, 0); hit at (0, 0, -3) → α = 0.5, β = 0.3
    // (interior of the quad, no edge cases).
    private static readonly Vector3 MirrorLightPos = new(0f, 10f, -3f);
    private static readonly Vector3 MirrorHitPoint = new(0f, 0f, -3f);

    private static (IHittable World, SphereLight Light) BuildMirrorScene(bool visibleToCamera)
    {
        var proxyMat = new Emissive(Vector3.One, intensity: 200f) { IsLightProxy = true };
        IHittable proxy = new Sphere(MirrorLightPos, 0.4f, proxyMat);
        if (!visibleToCamera) proxy = new CameraInvisibleHittable(proxy);

        var mirror = new Quad(
            q: new Vector3(-5f, 0f,  0f),
            u: new Vector3(10f, 0f,  0f),
            v: new Vector3( 0f, 0f, -10f),
            material: new Metal(Vector3.One, fuzz: 0f));

        var world = new HittableList(new IHittable[] { proxy, mirror });
        var light = new SphereLight(
            center:        MirrorLightPos,
            radius:        0.4f,
            color:         Vector3.One,
            intensity:     200f,
            shadowSamples: 4,
            proxyMaterial: proxyMat);
        return (world, light);
    }

    /// <summary>
    /// Arnold/Cycles semantics: a hidden light still appears in mirrors. The
    /// camera ray hits the mirror at <c>depth == _maxDepth</c>; the reflected
    /// secondary ray runs at <c>depth - 1</c>, so the camera-visibility filter
    /// does NOT apply on the reflection and the proxy is hit normally.
    /// </summary>
    [Fact]
    public void HiddenLight_StillVisibleInMirror()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var (world, light) = BuildMirrorScene(visibleToCamera: false);
        // Camera straight down at the mirror; center pixel reflects up to
        // the hidden light.
        var camera = MakeCamera(
            from: new Vector3(0f, 5f, -3f),
            at:   MirrorHitPoint,
            w: 16, h: 16,
            fov: 20f);

        var renderer = MakeRenderer(world, camera, new List<ILight> { light });
        var pixels = renderer.Render(16, 16);

        // Best-luminance probe in the 3×3 center region — survives sub-pixel
        // jitter that may move the reflection slightly off (8, 8).
        float maxLum = 0f;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Vector3 p = pixels[8 + dy, 8 + dx];
            float lum = 0.2126f * p.X + 0.7152f * p.Y + 0.0722f * p.Z;
            if (lum > maxLum) maxLum = lum;
        }
        Assert.True(maxLum > 0.2f,
            $"mirror failed to reflect the hidden light (max center luminance = {maxLum:F4})");
    }

    /// <summary>
    /// Baseline twin: same geometry, light visible to camera. Confirms the
    /// mirror configuration produces a bright reflection in both cases — the
    /// camera-visibility wrapper is the only variable between the two tests.
    /// </summary>
    [Fact]
    public void VisibleLight_AlsoVisibleInMirror_Baseline()
    {
        Sampler.SetKind(SamplerKind.Prng);

        var (world, light) = BuildMirrorScene(visibleToCamera: true);
        var camera = MakeCamera(
            from: new Vector3(0f, 5f, -3f),
            at:   MirrorHitPoint,
            w: 16, h: 16,
            fov: 20f);

        var renderer = MakeRenderer(world, camera, new List<ILight> { light });
        var pixels = renderer.Render(16, 16);

        float maxLum = 0f;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Vector3 p = pixels[8 + dy, 8 + dx];
            float lum = 0.2126f * p.X + 0.7152f * p.Y + 0.0722f * p.Z;
            if (lum > maxLum) maxLum = lum;
        }
        Assert.True(maxLum > 0.2f,
            $"baseline mirror geometry failed (max center luminance = {maxLum:F4})");
    }
}
