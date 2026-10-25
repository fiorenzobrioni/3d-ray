using System.Numerics;
using RayTracer.Core;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The camera's geometry (PIANO-REVIEW.md, Phase 3): a pinhole camera maps a
/// world point to the pixel the perspective projection predicts, and a thin
/// lens focuses exactly on the plane at the focus distance, with the blur of
/// every other depth given by the thin-lens formula.
/// </summary>
public class CameraModelTests
{
    private static readonly Vector3 From = new(1.3f, 2.1f, 4.2f);
    private static readonly Vector3 At = new(-0.4f, 0.5f, 0.2f);
    private const float Fov = 38f, Aspect = 16f / 9f;

    private static (Vector3 U, Vector3 V, Vector3 W) Frame()
    {
        var w = Vector3.Normalize(From - At);
        var u = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, w));
        return (u, Vector3.Cross(w, u), w);
    }

    /// <summary>Screen coordinates (s, t) of a world point, from the
    /// perspective projection written out independently of the camera.</summary>
    private static (float S, float T, float Depth) Project(Vector3 p)
    {
        var (u, v, w) = Frame();
        Vector3 d = p - From;
        float z = -Vector3.Dot(d, w);
        float h = MathF.Tan(Fov * MathF.PI / 360f);
        return (0.5f + Vector3.Dot(d, u) / (z * 2f * h * Aspect), 0.5f + Vector3.Dot(d, v) / (z * 2f * h), z);
    }

    private static Vector3 RandomPointInView(Random rng)
    {
        var (u, v, w) = Frame();
        float h = MathF.Tan(Fov * MathF.PI / 360f);
        float z = 0.5f + 20f * (float)rng.NextDouble();
        float x = ((float)rng.NextDouble() - 0.5f) * 2f * h * Aspect * z;
        float y = ((float)rng.NextDouble() - 0.5f) * 2f * h * z;
        return From - z * w + x * u + y * v;
    }

    [Fact]
    public void Pinhole_RayThroughProjectedPixel_HitsThePoint()
    {
        var cam = new Camera.Camera(From, At, Vector3.UnitY, Fov, Aspect, 0f, 1f);
        var rng = new Random(1);
        double worst = 0;
        for (int i = 0; i < 2000; i++)
        {
            var p = RandomPointInView(rng);
            var (s, t, _) = Project(p);
            var ray = cam.GetRay(s, t);
            var toP = Vector3.Normalize(p - ray.Origin);
            worst = Math.Max(worst, Vector3.Cross(Vector3.Normalize(ray.Direction), toP).Length());
        }
        Assert.True(worst < 2e-6, $"worst angular error {worst:E2} rad");
    }

    [Fact]
    public void ThinLens_RaysOfAPixel_ConvergeOnTheFocusPlane()
    {
        const float aperture = 0.4f, focus = 5.5f;
        var cam = new Camera.Camera(From, At, Vector3.UnitY, Fov, Aspect, aperture, focus);
        var (_, _, w) = Frame();
        MathUtils.ReseedThread(2);
        var rng = new Random(2);
        double worstSpread = 0, worstRadius = 0, meanR2 = 0;
        const int pixels = 50, lensSamples = 64;
        for (int i = 0; i < pixels; i++)
        {
            float s = (float)rng.NextDouble(), t = (float)rng.NextDouble();
            Vector3? first = null;
            for (int k = 0; k < lensSamples; k++)
            {
                var ray = cam.GetRay(s, t);
                Vector3 lens = ray.Origin - From;
                Assert.True(MathF.Abs(Vector3.Dot(lens, w)) < 1e-5f, "lens samples must lie on the lens plane");
                worstRadius = Math.Max(worstRadius, lens.Length());
                meanR2 += lens.LengthSquared();
                // Intersection with the plane at the focus distance.
                float tf = (focus + Vector3.Dot(ray.Origin - From, w)) / -Vector3.Dot(ray.Direction, w);
                Vector3 q = ray.Origin + tf * ray.Direction;
                first ??= q;
                worstSpread = Math.Max(worstSpread, (q - first.Value).Length());
            }
        }
        meanR2 /= pixels * lensSamples;
        float r = aperture / 2f;
        Assert.True(worstSpread < 1e-5 * focus, $"rays of one pixel spread {worstSpread:E2} on the focus plane");
        Assert.True(worstRadius <= r * 1.0001, $"lens sample at radius {worstRadius} > {r}");
        // Uniform over the disk: E[r²] = R²/2.
        Assert.InRange(meanR2 / (r * r), 0.47, 0.53);
    }

    [Theory]
    [InlineData(2.0f)]
    [InlineData(4.0f)]
    [InlineData(12.0f)]
    public void ThinLens_BlurOfAnotherDepth_FollowsTheThinLensFormula(float depth)
    {
        // A point at `depth` on the ray through the centre of a pixel: a ray
        // leaving the lens at offset o crosses that depth at |o| |d - f| / f
        // from the point. Projected back to the focus plane this is the circle
        // of confusion of the thin lens, A |d - f| / d (object space), which
        // a sensor at magnification m sees as A F |d - f| / (d (f - F)).
        const float aperture = 0.3f, focus = 5f;
        var cam = new Camera.Camera(From, At, Vector3.UnitY, Fov, Aspect, aperture, focus);
        var pinhole = new Camera.Camera(From, At, Vector3.UnitY, Fov, Aspect, 0f, focus);
        var (_, _, w) = Frame();
        MathUtils.ReseedThread(3);
        const float s = 0.37f, t = 0.61f;
        var axis = pinhole.GetRay(s, t);
        Vector3 dir = Vector3.Normalize(axis.Direction);
        Vector3 p = From + dir * (depth / -Vector3.Dot(dir, w));
        double worst = 0;
        for (int k = 0; k < 500; k++)
        {
            var ray = cam.GetRay(s, t);
            float lensOffset = (ray.Origin - From).Length();
            float tp = (depth + Vector3.Dot(ray.Origin - From, w)) / -Vector3.Dot(ray.Direction, w);
            float miss = (ray.Origin + tp * ray.Direction - p).Length();
            float expected = lensOffset * MathF.Abs(depth - focus) / focus;
            worst = Math.Max(worst, Math.Abs(miss - expected));
        }
        Assert.True(worst < 1e-5 * depth, $"blur deviates from the thin-lens formula by {worst:E2}");
    }
}
