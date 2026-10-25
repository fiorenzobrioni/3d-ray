using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Rendering;
using RayTracer.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The tangent frame and the height units of normal and bump maps
/// (PIANO-REVIEW.md Phase 4): a bump's height is in scene units for every
/// texture kind and every transform, mesh tangents are continuous across
/// faces (MikkTSpace), normal maps are texel-centred and wrap.
/// </summary>
public class NormalAndBumpFrameTests
{
    private static readonly IMaterial M = new Lambertian(new Vector3(0.5f));

    private sealed class LocalXRamp : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed) => new(p.X);
    }

    private sealed class URamp : ITexture
    {
        public Vector3 Value(float u, float v, Vector3 p, int seed) => new(u);
    }

    private static HitRecord HitDown(IHittable h, Vector3 from, Vector3 dir)
    {
        var rec = new HitRecord();
        Assert.True(h.Hit(new Ray(from, dir), 1e-4f, 1e4f, ref rec));
        return rec;
    }

    /// <summary>World normal after the bump, as the renderer composes it.</summary>
    private static Vector3 Bumped(in HitRecord rec, IBumpMap bump)
    {
        Vector3 n = rec.Normal;
        Vector3 t = Vector3.Normalize(rec.Tangent - Vector3.Dot(rec.Tangent, n) * n);
        Vector3 b = rec.Bitangent - Vector3.Dot(rec.Bitangent, n) * n - Vector3.Dot(rec.Bitangent, t) * t;
        b = Vector3.Normalize(b);
        Vector3 ts = bump.SampleTangentNormal(rec.U, rec.V, rec.LocalPoint, Renderer.BumpFrameAt(in rec, t, b), rec.ObjectSeed);
        return Vector3.Normalize(t * ts.X + b * ts.Y + n * ts.Z);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(37f)]
    public void ProceduralBump_FollowsTheObject_WhenItRotates(float degrees)
    {
        // A floor quad whose 3D texture rises along its own x axis, slope 1:
        // the bumped normal leans away from the object's x axis, however the
        // object is turned. Stepping the local point along the world tangent
        // read the slope along the wrong axis (zero at 90°).
        var quad = new Quad(new Vector3(-2, 0, -2), new Vector3(4, 0, 0), new Vector3(0, 0, 4), M);
        var rot = Matrix4x4.CreateRotationY(degrees * MathF.PI / 180f);
        var obj = new Transform(quad, rot);
        var rec = HitDown(obj, new Vector3(0.3f, 3f, 0.2f), -Vector3.UnitY);

        var bump = new BumpMapTexture(new LocalXRamp(), strength: 0.5f);
        Vector3 n = Bumped(in rec, bump);

        Vector3 objectX = Vector3.TransformNormal(Vector3.UnitX, rot);
        Vector3 expected = Vector3.Normalize(Vector3.UnitY - 0.5f * objectX);
        Assert.True(Vector3.Dot(n, expected) > 0.9999f, $"{degrees}°: got {n}, expected {expected}");
    }

    [Fact]
    public void ProceduralBump_SlopeIsInSceneUnits_UnderAScaledTransform()
    {
        // LocalPoint carries the transform's scale, so a ramp of slope 1 in
        // LocalPoint units is a slope of 1 per scene unit.
        var quad = new Quad(new Vector3(-1, 0, -1), new Vector3(2, 0, 0), new Vector3(0, 0, 2), M);
        var obj = new Transform(quad, Matrix4x4.CreateScale(3f, 1f, 3f));
        var rec = HitDown(obj, new Vector3(0.3f, 3f, 0.2f), -Vector3.UnitY);
        Vector3 n = Bumped(in rec, new BumpMapTexture(new LocalXRamp(), strength: 0.5f));
        Assert.Equal(0.5f, -n.X / n.Y, 3);
    }

    [Fact]
    public void ImageBump_HeightIsInSceneUnits()
    {
        // h = u over a 4-unit quad: slope 1/4 per scene unit, so strength 1
        // tilts the normal by atan(0.25). Measured per UV unit it was atan(1).
        var quad = new Quad(new Vector3(-2, 0, -2), new Vector3(4, 0, 0), new Vector3(0, 0, 4), M);
        var rec = HitDown(quad, new Vector3(0.3f, 3f, 0.2f), -Vector3.UnitY);
        Vector3 n = Bumped(in rec, new BumpMapTexture(new URamp(), strength: 1f));
        Assert.Equal(0.25f, -n.X / n.Y, 3);
    }

    [Fact]
    public void ImageBump_AgreesWithTheSameDisplacement()
    {
        // A sphere of radius 2: h = u runs once around the equator (length
        // 4π), slope 1/(4π) per scene unit; the displaced surface's normal
        // leans by that slope times the height scale against the direction
        // of growing u.
        var sphere = new Sphere(Vector3.Zero, 2f, M);
        var rec = HitDown(sphere, new Vector3(5f, 0.01f, 0.3f), -Vector3.UnitX);
        Vector3 n = Bumped(in rec, new BumpMapTexture(new URamp(), strength: 2f));
        Vector3 along = Vector3.Normalize(rec.DpDu);
        float tilt = -Vector3.Dot(n, along) / Vector3.Dot(n, rec.Normal);
        Assert.Equal(2f / (4f * MathF.PI), tilt, 3);
    }

    [Fact]
    public void MeshTangents_AreContinuousAcrossFaces()
    {
        // A faceted cylinder, 12 sides, u around and v up, smooth normals:
        // the face tangents jump by 30° at every edge, the vertex tangents
        // meet. Probe both sides of one edge.
        const int n = 12;
        var tris = new List<IHittable>();
        Vector3 P(int i, float y) { float a = 2f * MathF.PI * i / n; return new Vector3(MathF.Cos(a), y, -MathF.Sin(a)); }
        Vector3 N(int i) { float a = 2f * MathF.PI * i / n; return new Vector3(MathF.Cos(a), 0, -MathF.Sin(a)); }
        for (int i = 0; i < n; i++)
        {
            float u0 = (float)i / n, u1 = (float)(i + 1) / n;
            tris.Add(new SmoothTriangle(P(i, 0), P(i + 1, 0), P(i + 1, 1), N(i), N(i + 1), N(i + 1),
                                        new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), M));
            tris.Add(new SmoothTriangle(P(i, 0), P(i + 1, 1), P(i, 1), N(i), N(i + 1), N(i),
                                        new Vector2(u0, 0), new Vector2(u1, 1), new Vector2(u0, 1), M));
        }
        var before = new List<Vector3>();
        foreach (float da in new[] { -1e-3f, 1e-3f })
        {
            float a = 2f * MathF.PI / n + da;   // just either side of vertex column 1
            var dir = -new Vector3(MathF.Cos(a), 0, -MathF.Sin(a));
            HitRecord best = default; float bestT = float.MaxValue;
            foreach (var t in tris)
            {
                var r = new HitRecord();
                if (t.Hit(new Ray(-3f * dir + new Vector3(0, 0.5f, 0), dir), 1e-4f, bestT, ref r)) { best = r; bestT = r.T; }
            }
            before.Add(best.Tangent);
        }
        Assert.True(Vector3.Dot(before[0], before[1]) < 0.9f, "face tangents should differ by ~30°");

        MeshTangents.Generate(tris);
        var after = new List<Vector3>();
        foreach (float da in new[] { -1e-3f, 1e-3f })
        {
            float a = 2f * MathF.PI / n + da;
            var dir = -new Vector3(MathF.Cos(a), 0, -MathF.Sin(a));
            HitRecord best = default; float bestT = float.MaxValue;
            foreach (var t in tris)
            {
                var r = new HitRecord();
                if (t.Hit(new Ray(-3f * dir + new Vector3(0, 0.5f, 0), dir), 1e-4f, bestT, ref r)) { best = r; bestT = r.T; }
            }
            after.Add(best.Tangent);
            // Right-handed frame with B along +v (up).
            Assert.True(best.Bitangent.Y > 0.99f, $"bitangent {best.Bitangent}");
        }
        Assert.True(Vector3.Dot(after[0], after[1]) > 0.9999f, $"{after[0]} vs {after[1]}");
    }

    [Fact]
    public void MeshTangents_MirroredUvs_FlipTheBitangent()
    {
        // The same quad twice, the second with u running backwards: its
        // tangent points the other way and its bitangent stays along +v.
        var tris = new List<IHittable>
        {
            new SmoothTriangle(new(0,0,0), new(1,0,0), new(1,1,0), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                               new(0,0), new(1,0), new(1,1), M),
            new SmoothTriangle(new(2,0,0), new(3,0,0), new(3,1,0), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                               new(1,0), new(0,0), new(0,1), M),
        };
        MeshTangents.Generate(tris);
        var a = HitDown(tris[0], new Vector3(0.7f, 0.3f, 2f), -Vector3.UnitZ);
        var b = HitDown(tris[1], new Vector3(2.7f, 0.3f, 2f), -Vector3.UnitZ);
        Assert.True(a.Tangent.X > 0.99f && b.Tangent.X < -0.99f);
        Assert.True(a.Bitangent.Y > 0.99f && b.Bitangent.Y > 0.99f);
    }

    [Fact]
    public void NormalMap_IsTexelCentred_AndWraps()
    {
        // Two texels: the left leans +x, the right −x. At a texel's centre
        // its own normal; at the image's edge (u = 0) the two wrap into each
        // other and cancel. A 16-bit file keeps its precision.
        string path = Path.Combine(Path.GetTempPath(), $"3dray-nm-{Guid.NewGuid():N}.png");
        float lean = 0.6f;
        ushort Enc(float x) => (ushort)MathF.Round((x * 0.5f + 0.5f) * 65535f);
        using (var img = new Image<Rgba64>(2, 1))
        {
            float z = MathF.Sqrt(1 - lean * lean);
            img[0, 0] = new Rgba64(Enc(lean), Enc(0), Enc(z), 65535);
            img[1, 0] = new Rgba64(Enc(-lean), Enc(0), Enc(z), 65535);
            img.SaveAsPng(path);
        }
        try
        {
            var nm = new NormalMapTexture(path);
            Assert.Equal(lean, nm.SampleNormal(0.25f, 0.5f).X, 4);
            Assert.Equal(-lean, nm.SampleNormal(0.75f, 0.5f).X, 4);
            Assert.Equal(0f, nm.SampleNormal(0f, 0.5f).X, 4);
            Assert.Equal(0f, nm.SampleNormal(1f, 0.5f).X, 4);
        }
        finally { File.Delete(path); }
    }
}
