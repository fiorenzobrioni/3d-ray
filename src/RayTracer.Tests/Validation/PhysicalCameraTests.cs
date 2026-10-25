using System.Numerics;
using RayTracer.Core;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The photographic camera keys (PIANO-REVIEW.md, Phase 3): focal length and
/// sensor give the field of view of a camera focused at infinity, the f-number
/// gives the aperture diameter, and a bladed aperture is sampled uniformly.
/// </summary>
public class PhysicalCameraTests
{
    [Fact]
    public void FocalLengthAndSensor_GiveTheFieldOfView()
    {
        // 50 mm on full frame (36 x 24 mm at 3:2): vertical fov 2 atan(12/50).
        var cam = new CameraData { FocalLength = 50f, FStop = 2f };
        SceneLoader.ApplyPhysicalCamera(cam, 1.5f, metersPerUnit: 1f);
        Assert.Equal(2.0 * Math.Atan(12.0 / 50.0) * 180.0 / Math.PI, cam.Fov, 4);
        Assert.Equal(0.025f, cam.Aperture, 6);   // 50 mm / 2 = 25 mm, scene in metres
    }

    [Fact]
    public void FStop_WithoutFocalLength_UsesTheFocalLengthOfTheFov()
    {
        // fov chosen to be a 35 mm lens on a 36 mm sensor at 16:9.
        float sensorHeight = 36f / (16f / 9f);
        float fov = 2f * MathF.Atan(sensorHeight / 70f) * 180f / MathF.PI;
        var cam = new CameraData { Fov = fov, FStop = 1.4f };
        SceneLoader.ApplyPhysicalCamera(cam, 16f / 9f, metersPerUnit: 0.01f);   // scene in cm
        Assert.Equal(35f / 1.4f / 1000f / 0.01f, cam.Aperture, 3);   // 2.5 cm = 2.5 units
        Assert.Equal(fov, cam.Fov);
    }

    [Theory]
    [InlineData(5, 0f)]
    [InlineData(6, 17f)]
    [InlineData(9, 40f)]
    public void BladedAperture_IsUniformOverThePolygon(int blades, float rotation)
    {
        var from = new Vector3(0, 0, 5);
        var cam = new Camera.Camera(from, Vector3.Zero, Vector3.UnitY, 40f, 1f, 2f, 5f,
                                    apertureShape: new Camera.ApertureShape(blades, rotation, 1f));
        MathUtils.ReseedThread(blades);
        const int n = 200_000, radial = 10;
        var sector = new double[blades];
        var rho2 = new double[radial];
        double step = 2 * Math.PI / blades;
        double phase = rotation * Math.PI / 180 + Math.PI / 2;   // first vertex
        double apothem = Math.Cos(step / 2);
        for (int i = 0; i < n; i++)
        {
            // Camera frame: looking down -Z with up +Y, so U = +X and V = +Y.
            var o = cam.GetRay(0.5f, 0.5f).Origin - from;    // lens radius 1
            double ang = Math.Atan2(o.Y, o.X) - phase;
            ang -= 2 * Math.PI * Math.Floor(ang / (2 * Math.PI));
            int k = Math.Min((int)(ang / step), blades - 1);
            double mid = phase + (k + 0.5) * step;
            double rho = (o.X * Math.Cos(mid) + o.Y * Math.Sin(mid)) / apothem;
            Assert.True(rho <= 1 + 1e-5, $"sample outside the polygon (rho {rho})");
            sector[k]++;
            rho2[Math.Min((int)(rho * rho * radial), radial - 1)]++;
        }
        double ChiP(double[] c)
        {
            double e = (double)n / c.Length, chi2 = c.Sum(x => (x - e) * (x - e) / e);
            return BsdfTestKit.RegularizedGammaQ((c.Length - 1) / 2.0, chi2 / 2.0);
        }
        Assert.True(ChiP(sector) > 1e-4, "blade sectors are not equally likely");
        Assert.True(ChiP(rho2) > 1e-4, "points are not uniform inside the triangles");
    }

    [Fact]
    public void ApertureRatio_StretchesTheBokehHorizontally()
    {
        var from = new Vector3(0, 0, 5);
        var cam = new Camera.Camera(from, Vector3.Zero, Vector3.UnitY, 40f, 1f, 2f, 5f,
                                    apertureShape: new Camera.ApertureShape(0, 0f, 0.5f));
        MathUtils.ReseedThread(4);
        float maxX = 0, maxY = 0;
        for (int i = 0; i < 20000; i++)
        {
            var o = cam.GetRay(0.5f, 0.5f).Origin - from;
            maxX = MathF.Max(maxX, MathF.Abs(o.X));
            maxY = MathF.Max(maxY, MathF.Abs(o.Y));
        }
        Assert.InRange(maxX, 0.48f, 0.5001f);
        Assert.InRange(maxY, 0.98f, 1.0001f);
    }
}
