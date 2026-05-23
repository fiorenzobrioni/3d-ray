using System;
using System.IO;
using System.Numerics;
using RayTracer.Core;
using RayTracer.Geometry;
using RayTracer.Materials;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests;

/// <summary>
/// Coverage for the <c>world.ground</c> block — the dispatcher that builds the
/// floor geometry from a <see cref="GroundData"/> YAML record. Tests the
/// type dispatch, the legacy <c>y</c>/<c>material</c> shorthand, the inline
/// anonymous-material shortcut, the UV transform and per-ray-category
/// visibility flags introduced with the v2 pro upgrade.
///
/// <para>Tests load synthetic single-purpose YAML scenes from a temp file
/// through <see cref="SceneLoader.Load"/> and then ray-cast against the
/// returned world to introspect the produced primitive without coupling to
/// internal layout.</para>
/// </summary>
public class GroundTests
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers.
    // ─────────────────────────────────────────────────────────────────────────

    private static string WriteTempScene(string yaml)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        return path;
    }

    private static (IHittable World, HitRecord Hit) RayCast(string yaml,
                                                            Vector3 origin, Vector3 dir)
    {
        string path = WriteTempScene(yaml);
        try
        {
            var (world, _, _, _, _) = SceneLoader.Load(path, 64, 64);
            var rec = new HitRecord();
            var ray = new Ray(origin, Vector3.Normalize(dir));
            world.Hit(ray, MathUtils.Epsilon, MathUtils.Infinity, ref rec);
            return (world, rec);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  1. Legacy shorthand — `y` + `material` still works.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacyShorthand_YAndMaterial_ProducesInfinitePlaneAtY()
    {
        var (_, rec) = RayCast(
            """
            world:
              ground:
                material: "matte"
                y: 2.5
            materials:
              - id: "matte"
                type: "lambertian"
                color: [1, 1, 1]
            """,
            origin: new Vector3(0, 10, 0),
            dir:    new Vector3(0, -1, 0));

        Assert.True(rec.T > 0, "Expected the ray to hit the ground.");
        Assert.Equal(2.5f, rec.Point.Y, precision: 3);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  2. Type dispatch.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Type_Quad_ProducesFiniteSurface()
    {
        // Quad of half-size 5 → ray outside the patch must miss.
        var (_, recHit) = RayCast(
            """
            world:
              ground:
                type: "quad"
                size: 5
                color: [0.5, 0.5, 0.5]
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(recHit.T > 0);

        var (_, recMiss) = RayCast(
            """
            world:
              ground:
                type: "quad"
                size: 5
                color: [0.5, 0.5, 0.5]
            """,
            origin: new Vector3(20, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(recMiss.T == 0f, "Expected ray outside the finite quad to miss.");
    }

    [Fact]
    public void Type_Disk_BehavesAsRadiusBoundedDisc()
    {
        var (_, recHit) = RayCast(
            """
            world:
              ground:
                type: "disk"
                size: 3
                color: [0.5, 0.5, 0.5]
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(recHit.T > 0);

        var (_, recMiss) = RayCast(
            """
            world:
              ground:
                type: "disk"
                size: 3
                color: [0.5, 0.5, 0.5]
            """,
            origin: new Vector3(10, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(recMiss.T == 0f, "Expected ray outside the disk radius to miss.");
    }

    [Fact]
    public void Type_Unknown_FallsBackToInfinitePlaneWithWarning()
    {
        // 'foobar' is not a real ground type — must not throw, must fall back.
        var (_, rec) = RayCast(
            """
            world:
              ground:
                type: "foobar"
                y: 0
                color: [1, 1, 1]
            """,
            origin: new Vector3(100, 10, 100),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0, "Fallback infinite_plane should still receive the ray.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  3. Configurable normal / point.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NormalAndPoint_ConfigureTiltedGroundPlane()
    {
        // 45° tilted plane through the origin, normal pointing into +Y +Z.
        var (_, rec) = RayCast(
            """
            world:
              ground:
                point:  [0, 0, 0]
                normal: [0, 1, 1]
                color:  [1, 1, 1]
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0);
        // The surface normal at the hit must match the configured (0, 1, 1)
        // direction normalised, not the default (0, 1, 0).
        Vector3 expected = Vector3.Normalize(new Vector3(0, 1, 1));
        Assert.True(Vector3.Dot(rec.Normal, expected) > 0.99f,
            $"Expected hit normal ~{expected}, got {rec.Normal}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  4. Anonymous material shorthand.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InlineColor_NoMaterialBlock_BuildsAnonymousDisneyBsdf()
    {
        var (_, rec) = RayCast(
            """
            world:
              ground:
                color: [0.2, 0.4, 0.6]
                roughness: 0.7
                metallic: 0.1
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0);
        Assert.NotNull(rec.Material);
        // The anonymous material must be a DisneyBsdf since metallic/roughness
        // were specified.
        Assert.IsType<DisneyBsdf>(rec.Material);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  5. UV transform.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UvScale_RemapsUvCoordinates()
    {
        // Without UV scale the infinite-plane uvs frac to [0, 1); with a
        // uv_scale of 10 they multiply by 10 before the frac → still [0, 1)
        // but with a 10× higher spatial frequency. Compare the same world
        // point's reported (u, v) with and without the scale.
        var (_, recPlain) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
            """,
            origin: new Vector3(0.37f, 5, 0.91f),
            dir:    new Vector3(0, -1, 0));

        var (_, recScaled) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
                uv_scale: [10, 10]
            """,
            origin: new Vector3(0.37f, 5, 0.91f),
            dir:    new Vector3(0, -1, 0));

        // The scaled UVs must be different from the plain ones (10× scaling
        // wraps a non-integer u into a different fractional part).
        Assert.NotEqual(recPlain.U, recScaled.U);
    }

    [Fact]
    public void UvOffset_ShiftsUvCoordinates()
    {
        var (_, recPlain) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
            """,
            origin: new Vector3(0.25f, 5, 0.25f),
            dir:    new Vector3(0, -1, 0));

        var (_, recShifted) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
                uv_offset: [0.5, 0]
            """,
            origin: new Vector3(0.25f, 5, 0.25f),
            dir:    new Vector3(0, -1, 0));

        Assert.NotEqual(recPlain.U, recShifted.U);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  6. Visibility flags.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VisibilityCameraFalse_SetsCameraInvisibleFlag()
    {
        var (_, rec) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
                visibility:
                  camera: false
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0, "The world traversal still reports the hit.");
        Assert.True((rec.VisibilityMask & HitVisibilityMask.Camera) != 0,
            "Camera visibility bit should be set when visibility.camera=false.");
        // Bridge — legacy boolean API must reflect the same state.
        Assert.True(rec.CameraInvisible);
    }

    [Fact]
    public void VisibilityShadowFalse_SetsShadowBit()
    {
        var (_, rec) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
                visibility:
                  shadow: false
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0);
        Assert.True((rec.VisibilityMask & HitVisibilityMask.Shadow) != 0);
    }

    [Fact]
    public void VisibilityAllOn_ProducesEmptyMask()
    {
        // Default (no visibility block) means full visibility → mask must be None.
        var (_, rec) = RayCast(
            """
            world:
              ground:
                color: [1, 1, 1]
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0);
        Assert.Equal(HitVisibilityMask.None, rec.VisibilityMask);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  7. Auto-albedo sync with sky.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AutoAlbedo_MaterialMissing_UsesSkyGroundAlbedo()
    {
        var (_, rec) = RayCast(
            """
            world:
              sky:
                type: "gradient"
                ground_color: [0.9, 0.1, 0.05]
              ground: {}
            """,
            origin: new Vector3(0, 5, 0),
            dir:    new Vector3(0, -1, 0));
        Assert.True(rec.T > 0);
        Assert.NotNull(rec.Material);
        Assert.IsType<Lambertian>(rec.Material);
    }
}
