using System.Globalization;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Rendering;
using RayTracer.Scene;
using Xunit;
using Xunit.Abstractions;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The render space (PIANO-REVIEW.md Phase 11): the loader builds the scene
/// with the camera at the origin, positions read in double. A scene far from
/// the world origin renders as the same scene at the origin: before, the ray
/// offsets (relative to the coordinates) grew with the distance and a 2-unit
/// scene 100 km away lost 6% of its light. Textures, emission textures,
/// procedural terrain and media read the world positions as written, so
/// nothing moves with the camera.
/// Two renders compared almost exactly: in the exclusive collection, so no
/// other class flips the process-wide sampler kind (or loads a scene) meanwhile.
/// </summary>
[Collection("SamplerExclusive")]
public class RenderSpaceTests
{
    private readonly ITestOutputHelper _out;
    public RenderSpaceTests(ITestOutputHelper output) => _out = output;

    private const int W = 48, H = 32;

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // Every kind of placement: own coordinates (sphere, quad, CSG, group
    // children, a mesh with no transform, torus centre, procedural heightfield
    // when the scene is moved across only: a terrain has no altitude of its own),
    // transforms (box, instance, transformed mesh), lights (point, area,
    // emissive sphere with a textured emission), a height fog.
    private static string Scene(double dx, double dy, double dz, string meshPath, string localMeshPath, bool fog,
                                (double X, double Y, double Z)? camera = null)
    {
        var c = camera ?? (0.6, 1.3, 4.0);
        string P(double x, double y, double z) => $"[{F(x + dx)}, {F(y + dy)}, {F(z + dz)}]";
        return $@"format_version: 2
world:
  sky: {{ type: flat, color: [0.05, 0.05, 0.06] }}
{(fog ? $"  medium: {{ type: height_fog, sigma_a: [0.02, 0.02, 0.02], sigma_s: [0.05, 0.05, 0.05], y0: {F(dy)}, scale_height: 1.5 }}" : "")}
cameras:
  - position: {P(c.X, c.Y, c.Z)}
    look_at: {P(0, 0.5, 0)}
    fov: 50
materials:
  - {{ id: floor, type: lambertian, color: [0.6, 0.6, 0.6] }}
  - {{ id: red, type: lambertian, color: [0.7, 0.2, 0.2] }}
  - id: noisy
    type: openpbr
    base_color: {{ type: noise, scale: 2, colors: [[0.1, 0.2, 0.6], [0.9, 0.8, 0.3]] }}
    specular_roughness: 0.5
  - id: lamp
    type: emissive
    color: {{ type: noise, scale: 3, colors: [[0.2, 0.2, 0.2], [1, 1, 1]] }}
    luminance: 20
templates:
  - name: post
    children:
      - {{ type: cylinder, center: [0, 0, 0], radius: 0.08, height: 0.6 }}
entities:
  - {{ type: quad, q: {P(-3, 0, 3)}, u: [6, 0, 0], v: [0, 0, -6], material: floor }}
  - {{ type: sphere, center: {P(-0.7, 0.45, 0)}, radius: 0.45, material: noisy }}
  - {{ type: box, translate: {P(0.8, 0.3, -0.3)}, rotate: [0, 30, 0], scale: [0.6, 0.6, 0.6], material: red }}
  - type: csg
    operation: subtraction
    material: noisy
    left: {{ type: sphere, center: {P(0.25, 0.3, 1.0)}, radius: 0.3 }}
    right: {{ type: sphere, center: {P(0.4, 0.45, 1.15)}, radius: 0.2 }}
  - type: group
    children:
      - {{ type: cylinder, center: {P(-1.5, 0, -1)}, radius: 0.15, height: 0.8, material: red }}
      - {{ type: sphere, center: {P(1.5, 0.2, -1)}, radius: 0.2, material: noisy }}
  - {{ type: instance, template: post, translate: {P(-1.2, 0, 1.2)}, material: red }}
  - {{ type: mesh, path: '{meshPath}', material: noisy }}
  - {{ type: mesh, path: '{localMeshPath}', translate: {P(0, 0, -3.5)}, material: noisy }}
  - {{ type: triangle, v0: {P(-2.5, 0.01, 2.5)}, v1: {P(-1.5, 0.01, 2.5)}, v2: {P(-2, 0.01, 1.5)}, material: noisy }}
  - {{ type: torus, center: {P(1.2, 0.12, 0.9)}, major_radius: 0.25, minor_radius: 0.08, material: red }}
  - type: heightfield
{(dy == 0 ? $"    bounds: [{F(-2.5 + dx)}, {F(-2.5 + dz)}, {F(-1.5 + dx)}, {F(-1.5 + dz)}]"
          : $"    bounds: [-2.5, -2.5, -1.5, -1.5]\n    translate: {P(0, 0, 0)}")}
    height_texture: {{ type: noise, scale: 2 }}
    height_scale: 0.4
    resolution: 32
    material: noisy
  - {{ type: sphere, center: {P(-1.6, 1.0, 0.6)}, radius: 0.15, material: lamp }}
lights:
  - {{ type: point, position: {P(1.5, 3, 2)}, intensity: 15 }}
  - {{ type: area, corner: {P(-0.5, 2.5, -0.5)}, u: [1, 0, 0], v: [0, 0, 1], luminance: 25 }}
";
    }

    // A small mesh with no transform: its vertices are where it stands (the
    // offsets are multiples of 1/64, exact in float at 2^17).
    private static string WriteMesh(string dir, double dx, double dy, double dz)
    {
        string path = Path.Combine(dir, $"tetra-{F(dx)}-{F(dy)}-{F(dz)}.obj");
        string V(double x, double y, double z) => $"v {F(x + dx)} {F(y + dy)} {F(z + dz)}";
        File.WriteAllText(path, string.Join('\n',
            V(1.75, 0, 1.5), V(2.25, 0, 1.5), V(2.0, 0, 2.0), V(2.0, 0.5, 1.75),
            "f 1 3 2", "f 1 2 4", "f 2 3 4", "f 3 1 4") + "\n");
        return path;
    }

    private Vector3[,] Render(double dx, double dy, double dz, bool fog)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"3dray-rs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string mesh = WriteMesh(dir, dx, dy, dz);
            string local = WriteMesh(dir, 0, 0, 0);
            string path = Path.Combine(dir, "scene.yaml");
            File.WriteAllText(path, Scene(dx, dy, dz, mesh, local, fog));
            Sampler.SetKind(SamplerKind.Sobol);
            var (world, cam, lights, sky, medium, motion) = SceneLoader.Load(path, W, H);
            var renderer = new Renderer(world, cam, lights, sky, 32, 6, medium,
                                        motionBlur: motion, referenceMode: true);
            var result = renderer.Render(W, H, new RenderCaptureOptions { CaptureBeautyHalves = true });
            var img = new Vector3[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    img[y, x] = result.Buffers!.Beauty.GetRgb(x, y);
            return img;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static (double Ratio, double RelMse) Compare(Vector3[,] a, Vector3[,] b)
    {
        double sa = 0, sb = 0, rel = 0;
        for (int y = 0; y < a.GetLength(0); y++)
            for (int x = 0; x < a.GetLength(1); x++)
            {
                float la = MathUtils.Luminance(a[y, x]), lb = MathUtils.Luminance(b[y, x]);
                sa += la;
                sb += lb;
                rel += (la - lb) * (la - lb) / (lb * lb + 0.01);
            }
        return (sa / sb, rel / a.Length);
    }

    [Theory]
    [InlineData(131072.0, 0.0, -131072.0, false)]   // 2^17 across, on the ground
    [InlineData(131072.0, 8192.0, 131072.0, true)]  // and high up, in a height fog
    [InlineData(0.0, 8192.0, 0.0, false)]            // only up
    public void FarScene_RendersAsTheSameSceneAtTheOrigin(double dx, double dy, double dz, bool fog)
    {
        var near = Render(0, 0, 0, fog);
        var far = Render(dx, dy, dz, fog);
        var (ratio, relMse) = Compare(far, near);
        _out.WriteLine($"offset ({dx}, {dy}, {dz}), fog {fog}: mean ratio {ratio:F5}, relMSE {relMse:E2}");
        Assert.InRange(ratio, 0.999, 1.001);
        Assert.True(relMse < 1e-4, $"relMSE {relMse:E2}");
    }

    [Fact]
    public void Positions_AreReadInDouble_AndTheCameraSitsAtTheOrigin()
    {
        // A sphere 10.003 units ahead of a camera 100 km away: in float the
        // centre's 3 mm are lost (the spacing there is 7.8 mm).
        string path = Path.Combine(Path.GetTempPath(), $"3dray-rs-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, @"format_version: 2
cameras:
  - { position: [100000, 0, 100000], look_at: [100000, 0, 100010], fov: 30 }
materials:
  - { id: m, type: lambertian, color: [0.5, 0.5, 0.5] }
entities:
  - { type: sphere, center: [100000, 0, 100011.003], radius: 1, material: m }
lights: []
");
        try
        {
            var (world, cam, _, _, _, _) = SceneLoader.Load(path, 8, 8);
            Assert.Equal(new Vector3(100000, 0, 100000), SceneLoader.LastRenderOrigin);
            var ray = new Ray(Vector3.Zero, Vector3.UnitZ);
            var rec = new HitRecord();
            Assert.True(world.Hit(ray, 1e-4f, float.PositiveInfinity, ref rec));
            Assert.Equal(10.003f, rec.T, 5);
            // A sphere's texture point is relative to its centre.
            Assert.Equal(0f, rec.LocalPoint.X, 4);
            Assert.Equal(-1f, rec.LocalPoint.Z, 4);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TexturePoints_DoNotMoveWithTheCamera()
    {
        // The same world point seen with two cameras (so two render spaces):
        // every kind of entity must give the same local point and UV, or its
        // textures would slide with the camera. Vertical rays from above,
        // in a scene 2^17 units from the world origin.
        const double d = 131072;
        (double, double)[] targets =
        {
            (-0.7, 0.1), (0.0, 0.0), (0.8, -0.3), (0.3, 1.05), (-1.5, -1.0), (1.5, -1.0), (-1.2, 1.2),
            (2.0, 1.75), (2.0, -1.75), (-2.0, 2.2), (1.2 + 0.25, 0.9), (-2.0, -2.0), (-1.6, 0.6),
        };
        string dir = Path.Combine(Path.GetTempPath(), $"3dray-rs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string mesh = WriteMesh(dir, d, 0, d);
            string local = WriteMesh(dir, 0, 0, 0);
            var results = new List<(Vector3 Local, float U, float V)[]>();
            foreach (var cam in new[] { (0.6, 1.3, 4.0), (-3.0, 5.0, 7.5) })
            {
                string path = Path.Combine(dir, "scene.yaml");
                File.WriteAllText(path, Scene(d, 0, d, mesh, local, fog: false, cam));
                var (world, _, _, _, _, _) = SceneLoader.Load(path, 8, 8);
                // The render origin is the camera: a world point p is p − camera.
                var hits = new (Vector3, float, float)[targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    // Off the centres, where a cap's angular UV is undefined.
                    var (x, z) = (targets[i].Item1 + 0.013, targets[i].Item2 + 0.007);
                    var from = new Vector3((float)(x - cam.Item1), (float)(10.0 - cam.Item2), (float)(z - cam.Item3));
                    var rec = new HitRecord();
                    Assert.True(world.Hit(new Ray(from, -Vector3.UnitY), 1e-4f, float.PositiveInfinity, ref rec),
                                $"no hit at ({x}, {z})");
                    hits[i] = (rec.LocalPoint, rec.U, rec.V);
                }
                results.Add(hits);
            }
            for (int i = 0; i < targets.Length; i++)
            {
                var (la, ua, va) = results[0][i];
                var (lb, ub, vb) = results[1][i];
                _out.WriteLine($"({targets[i].Item1}, {targets[i].Item2}): local {la} / {lb}");
                // 2^17 has a float spacing of 1/64: an absolute (mesh) local
                // point is rounded there, a relative one is exact.
                Assert.True((la - lb).Length() < 0.02f, $"target {i}: local point {la} vs {lb}");
                Assert.InRange(ua - ub, -1e-4f, 1e-4f);
                Assert.InRange(va - vb, -1e-4f, 1e-4f);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
