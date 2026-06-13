using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Geometry;
using RayTracer.Lights;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Motion-blur integration tests, three families:
///   (a) The project invariant: when nothing is animated the renderer draws no
///       time dimension, so the output is bit-identical whether
///       <see cref="MotionBlurSettings"/> is passed or not (mirror of the
///       capture invariant in <see cref="RenderCaptureTests"/>) — and an
///       animated scene actually produces a different image (the time
///       dimension has an effect).
///   (b) Camera: identical-key camera motion produces the same rays as a
///       static camera; mid-time poses are the lerp of the keys.
///   (c) SceneLoader: `motion:` and `shutter:` YAML parsing — activation flag,
///       shutter validation, instance motion, animated emissives registering
///       a snapshot GeometryLight, child motion warn-and-ignore.
/// </summary>
[Collection("SamplerExclusive")]
public class MotionBlurTests
{
    private const int Width = 32;
    private const int Height = 32;
    private const int Spp = 8;

    private static Renderer BuildRenderer(MotionBlurSettings motion = default, bool movingSphere = false)
    {
        var floorMat  = new Lambertian(new Vector3(0.7f, 0.3f, 0.2f));
        var sphereMat = new Lambertian(new Vector3(0.2f, 0.5f, 0.8f));

        IHittable sphere = movingSphere
            ? new AnimatedTransform(new Sphere(Vector3.Zero, 0.5f, sphereMat), new[]
              {
                  new MotionKey(0f, new Vector3(-0.7f, 0.5f, 0f), Quaternion.Identity, Vector3.One),
                  new MotionKey(1f, new Vector3(0.7f, 0.5f, 0f), Quaternion.Identity, Vector3.One),
              })
            : new Sphere(new Vector3(-0.7f, 0.5f, 0f), 0.5f, sphereMat);

        var world = new HittableList(new IHittable[]
        {
            new InfinitePlane(Vector3.Zero, Vector3.UnitY, floorMat),
            sphere,
        });

        var light = new SphereLight(
            center: new Vector3(0f, 3f, 2f),
            radius: 0.3f,
            color: Vector3.One,
            intensity: 10f,
            shadowSamples: 1);

        var camera = new RayTracer.Camera.Camera(
            lookFrom:    new Vector3(0f, 1.2f, 4f),
            lookAt:      new Vector3(0f, 0.5f, 0f),
            vUp:         Vector3.UnitY,
            vFovDeg:     45f,
            aspectRatio: (float)Width / Height,
            aperture:    0f,
            focusDist:   1f);

        return new Renderer(
            world, camera,
            new List<ILight> { light },
            new SkySettings(new Vector3(0.4f, 0.5f, 0.7f)),
            samplesPerPixel: Spp,
            maxDepth: 6,
            motionBlur: motion);
    }

    // ── (a) Renderer invariants ──────────────────────────────────────────────

    [Fact]
    public void InactiveMotionBlur_PixelsAreBitIdentical()
    {
        Sampler.SetKind(SamplerKind.Sobol);

        // A shutter declared on a static scene resolves to Active=false: the
        // renderer must draw no time dimension and stay bit-identical to the
        // default-constructed path.
        var baseline = BuildRenderer().Render(Width, Height);
        var declared = BuildRenderer(new MotionBlurSettings(0.2f, 0.8f, active: false))
            .Render(Width, Height);

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            Assert.Equal(baseline[y, x], declared[y, x]);
    }

    [Fact]
    public void ActiveMotionBlur_ChangesTheImage()
    {
        Sampler.SetKind(SamplerKind.Sobol);

        var frozen = BuildRenderer(default, movingSphere: true).Render(Width, Height);
        var blurred = BuildRenderer(new MotionBlurSettings(0f, 1f, active: true), movingSphere: true)
            .Render(Width, Height);

        int different = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            if (frozen[y, x] != blurred[y, x]) different++;

        // The sphere sweeps ~1.4 world units across the frame: a substantial
        // share of pixels must change once the shutter opens.
        Assert.True(different > Width * Height / 20,
            $"Motion blur had no visible effect ({different} pixels changed).");
    }

    // ── (b) Camera ───────────────────────────────────────────────────────────

    [Fact]
    public void CameraIdenticalKeys_MatchesStaticCameraRays()
    {
        var lookFrom = new Vector3(1f, 2f, 5f);
        var lookAt = new Vector3(0f, 0.5f, 0f);
        var keys = new[]
        {
            new RayTracer.Camera.CameraKey(0f, lookFrom, lookAt, Vector3.UnitY, 50f),
            new RayTracer.Camera.CameraKey(1f, lookFrom, lookAt, Vector3.UnitY, 50f),
        };
        var animated = new RayTracer.Camera.Camera(lookFrom, lookAt, Vector3.UnitY, 50f, 1.5f, 0f, 1f, keys);
        var statiq = new RayTracer.Camera.Camera(lookFrom, lookAt, Vector3.UnitY, 50f, 1.5f, 0f, 1f);

        for (float s = 0.1f; s < 1f; s += 0.2f)
        for (float t = 0.1f; t < 1f; t += 0.2f)
        {
            var ra = animated.GetRay(s, t, 0.37f);
            var rs = statiq.GetRay(s, t);
            Assert.True((ra.Origin - rs.Origin).Length() < 1e-5f);
            Assert.True((Vector3.Normalize(ra.Direction) - Vector3.Normalize(rs.Direction)).Length() < 1e-5f);
            Assert.Equal(0.37f, ra.Time);
        }
    }

    [Fact]
    public void CameraMotion_MidTimePoseIsTheLerp()
    {
        var posA = new Vector3(0f, 1f, 5f);
        var posB = new Vector3(4f, 1f, 5f);
        var lookAt = new Vector3(0f, 0f, 0f);
        var keys = new[]
        {
            new RayTracer.Camera.CameraKey(0f, posA, lookAt, Vector3.UnitY, 45f),
            new RayTracer.Camera.CameraKey(1f, posB, lookAt, Vector3.UnitY, 45f),
        };
        var camera = new RayTracer.Camera.Camera(posA, lookAt, Vector3.UnitY, 45f, 1f, 0f, 1f, keys);

        // Pinhole: the ray origin IS the (interpolated) camera position.
        Assert.True((camera.GetRay(0.5f, 0.5f, 0f).Origin - posA).Length() < 1e-5f);
        Assert.True((camera.GetRay(0.5f, 0.5f, 0.5f).Origin - new Vector3(2f, 1f, 5f)).Length() < 1e-5f);
        Assert.True((camera.GetRay(0.5f, 0.5f, 1f).Origin - posB).Length() < 1e-5f);
    }

    // ── (c) SceneLoader ──────────────────────────────────────────────────────

    private static (IHittable World, RayTracer.Camera.Camera Camera, List<ILight> Lights,
                    SkySettings Sky, Volumetrics.IMedium? GlobalMedium, MotionBlurSettings Motion)
        LoadScene(string yaml)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        try { return SceneLoader.Load(path, 64, 64); }
        finally { File.Delete(path); }
    }

    private const string SceneHeader = @"
camera:
  position: [0, 1, 5]
  look_at: [0, 0.5, 0]
lights:
  - { type: point, position: [0, 5, 0], color: [1, 1, 1], intensity: 50 }
materials:
  - { id: diffuse, type: lambertian, color: [0.5, 0.5, 0.5] }
";

    [Fact]
    public void EntityMotion_ActivatesMotionBlur_AndAnimatesTheEntity()
    {
        var loaded = LoadScene(SceneHeader + @"
entities:
  - type: sphere
    center: [0, 0, 0]
    radius: 0.5
    material: diffuse
    translate: [0, 0.5, 0]
    motion:
      - { time: 1.0, translate: [3, 0.5, 0] }
");
        Assert.True(loaded.Motion.Active);
        Assert.Equal(0f, loaded.Motion.ShutterOpen);
        Assert.Equal(1f, loaded.Motion.ShutterClose);

        // The world hits the sphere at its base pose at t=0 and at the moved
        // pose at t=1 (proves the AnimatedTransform wrap + BVH routing).
        var rec = new HitRecord();
        Assert.True(loaded.World.Hit(new Ray(new Vector3(0, 0.5f, 5), -Vector3.UnitZ, 0f), 1e-3f, 1e30f, ref rec));
        rec = new HitRecord();
        Assert.True(loaded.World.Hit(new Ray(new Vector3(3, 0.5f, 5), -Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
        rec = new HitRecord();
        Assert.False(loaded.World.Hit(new Ray(new Vector3(3, 0.5f, 5), -Vector3.UnitZ, 0f), 1e-3f, 1e30f, ref rec));
    }

    [Fact]
    public void ShutterWithoutMotion_StaysInactive()
    {
        var loaded = LoadScene(@"
camera:
  position: [0, 1, 5]
  look_at: [0, 0.5, 0]
  shutter: [0.25, 0.75]
lights:
  - { type: point, position: [0, 5, 0], color: [1, 1, 1], intensity: 50 }
materials:
  - { id: diffuse, type: lambertian, color: [0.5, 0.5, 0.5] }
entities:
  - { type: sphere, center: [0, 0.5, 0], radius: 0.5, material: diffuse }
");
        Assert.False(loaded.Motion.Active);
    }

    [Fact]
    public void InvalidShutter_ResetsToFullInterval()
    {
        var loaded = LoadScene(@"
camera:
  position: [0, 1, 5]
  look_at: [0, 0.5, 0]
  shutter: [0.9, 0.1]
lights:
  - { type: point, position: [0, 5, 0], color: [1, 1, 1], intensity: 50 }
materials:
  - { id: diffuse, type: lambertian, color: [0.5, 0.5, 0.5] }
entities:
  - type: sphere
    center: [0, 0.5, 0]
    radius: 0.5
    material: diffuse
    motion:
      - { time: 1.0, translate: [1, 0, 0] }
");
        Assert.True(loaded.Motion.Active);
        Assert.Equal(0f, loaded.Motion.ShutterOpen);
        Assert.Equal(1f, loaded.Motion.ShutterClose);
    }

    [Fact]
    public void CameraMotionInYaml_ActivatesMotionBlur()
    {
        var loaded = LoadScene(@"
camera:
  position: [0, 1, 5]
  look_at: [0, 0.5, 0]
  motion:
    - { time: 1.0, position: [2, 1, 5] }
lights:
  - { type: point, position: [0, 5, 0], color: [1, 1, 1], intensity: 50 }
materials:
  - { id: diffuse, type: lambertian, color: [0.5, 0.5, 0.5] }
entities:
  - { type: sphere, center: [0, 0.5, 0], radius: 0.5, material: diffuse }
");
        Assert.True(loaded.Motion.Active);
        Assert.True(loaded.Camera.HasMotion);

        // Pinhole origin tracks the interpolated pose.
        Assert.True((loaded.Camera.GetRay(0.5f, 0.5f, 1f).Origin - new Vector3(2f, 1f, 5f)).Length() < 1e-4f);
    }

    [Fact]
    public void InstanceMotion_AnimatesTheInstance()
    {
        var loaded = LoadScene(SceneHeader + @"
templates:
  - name: ball
    children:
      - { type: sphere, center: [0, 0, 0], radius: 0.5, material: diffuse }
entities:
  - type: instance
    template: ball
    translate: [0, 0.5, 0]
    motion:
      - { time: 1.0, translate: [3, 0.5, 0] }
");
        Assert.True(loaded.Motion.Active);
        var rec = new HitRecord();
        Assert.True(loaded.World.Hit(new Ray(new Vector3(3, 0.5f, 5), -Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
        rec = new HitRecord();
        Assert.False(loaded.World.Hit(new Ray(new Vector3(3, 0.5f, 5), -Vector3.UnitZ, 0f), 1e-3f, 1e30f, ref rec));
    }

    [Fact]
    public void AnimatedEmissive_RegistersSnapshotGeometryLight()
    {
        var loaded = LoadScene(@"
camera:
  position: [0, 1, 5]
  look_at: [0, 0.5, 0]
lights: []
materials:
  - { id: glow, type: emissive, color: [1, 0.8, 0.6], intensity: 5 }
entities:
  - type: sphere
    center: [0, 0, 0]
    radius: 0.5
    material: glow
    translate: [0, 2, 0]
    motion:
      - { time: 1.0, translate: [2, 2, 0] }
");
        Assert.True(loaded.Motion.Active);
        // The animated emissive joins the NEE pool once, as a static snapshot
        // at the mid-animation pose.
        Assert.Single(loaded.Lights.OfType<GeometryLight>());
    }

    [Fact]
    public void ChildMotion_IsIgnored()
    {
        var loaded = LoadScene(SceneHeader + @"
entities:
  - type: group
    children:
      - type: sphere
        center: [0, 0.5, 0]
        radius: 0.5
        material: diffuse
        motion:
          - { time: 1.0, translate: [3, 0, 0] }
");
        // Child motion is warn-and-ignore: nothing animates, blur stays off.
        Assert.False(loaded.Motion.Active);
        var rec = new HitRecord();
        Assert.True(loaded.World.Hit(new Ray(new Vector3(0, 0.5f, 5), -Vector3.UnitZ, 1f), 1e-3f, 1e30f, ref rec));
    }
}
