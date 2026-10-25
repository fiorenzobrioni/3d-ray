using System.Numerics;
using RayTracer.Rendering;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// Colour management (PIANO-REVIEW.md, Phase 3): the sRGB transfer function,
/// the ACES 1.x Output Transform against OpenColorIO, AgX against an
/// independent implementation of the same reference, and the legacy curve.
/// Reference values: OpenColorIO 2.6, config cg-config-v2.2.0_aces-v1.3,
/// "Linear Rec.709 (sRGB)" to display "sRGB - Display", view
/// "ACES 1.0 - SDR Video", clipped to [0, 1] as the ACES ODT does.
/// </summary>
public class ColorManagementTests
{
    public static TheoryData<Vector3, Vector3> AcesReference => new()
    {
        { new Vector3(0.18f, 0.18f, 0.18f), new Vector3(0.355952f, 0.355953f, 0.355953f) },
        { new Vector3(1.0f, 1.0f, 1.0f), new Vector3(0.811975f, 0.811975f, 0.811975f) },
        { new Vector3(0.005f, 0.005f, 0.005f), new Vector3(0.003547f, 0.003547f, 0.003547f) },
        { new Vector3(16.0f, 16.0f, 16.0f), new Vector3(0.999741f, 0.999741f, 0.999741f) },
        { new Vector3(0.8f, 0.1f, 0.05f), new Vector3(0.743985f, 0.25993f, 0.161965f) },
        { new Vector3(0.05f, 0.6f, 0.1f), new Vector3(0.200906f, 0.696467f, 0.283546f) },
        { new Vector3(0.1f, 0.2f, 0.9f), new Vector3(0.248197f, 0.388024f, 0.793545f) },
        { new Vector3(3.0f, 1.5f, 0.2f), new Vector3(0.953529f, 0.877434f, 0.5867f) },
        { new Vector3(0.02f, 0.03f, 0.2f), new Vector3(0.000142f, 0.071889f, 0.362998f) },
        { new Vector3(0.0346f, 0.0497f, 0.0429f), new Vector3(0.086476f, 0.123128f, 0.10769f) },
        { new Vector3(1.5396f, 4.4807f, 0.027f), new Vector3(0.917602f, 0.962794f, 0.700481f) },
        { new Vector3(0.0661f, 0.0388f, 0.0251f), new Vector3(0.15706f, 0.099987f, 0.064471f) },
        { new Vector3(0.1709f, 0.2985f, 0.3384f), new Vector3(0.356333f, 0.493654f, 0.527278f) },
        { new Vector3(18.2646f, 14.5431f, 11.4152f), new Vector3(1.0f, 0.998369f, 0.994379f) },
        { new Vector3(0.003f, 0.0022f, 0.0086f), new Vector3(0.000341f, 2.4e-05f, 0.009601f) },
        { new Vector3(0.3794f, 5.4748f, 4.9571f), new Vector3(0.889383f, 0.972356f, 0.965915f) },
        { new Vector3(0.0413f, 0.0337f, 0.0326f), new Vector3(0.10059f, 0.082888f, 0.080097f) },
        { new Vector3(0.0005f, 0.0088f, 0.0318f), new Vector3(0.0f, 0.010862f, 0.074732f) },
        { new Vector3(0.012f, 0.0001f, 0.0268f), new Vector3(0.012901f, 0.0f, 0.059214f) },
        { new Vector3(1.6714f, 5.4985f, 3.1841f), new Vector3(0.922888f, 0.971414f, 0.944757f) },
        { new Vector3(0.3906f, 0.4529f, 0.0559f), new Vector3(0.57648f, 0.615752f, 0.196801f) },
        { new Vector3(0.4783f, 0.8207f, 0.3403f), new Vector3(0.66111f, 0.773668f, 0.568288f) },
        { new Vector3(0.0019f, 0.0121f, 0.0101f), new Vector3(0.0f, 0.017477f, 0.013365f) },
        { new Vector3(0.7225f, 0.3358f, 0.8663f), new Vector3(0.752365f, 0.541901f, 0.782562f) },
    };

    public static TheoryData<Vector3, Vector3> AgxReference => new()
    {
        { new Vector3(0.18f, 0.18f, 0.18f), new Vector3(0.500516f, 0.500516f, 0.500516f) },
        { new Vector3(1.0f, 1.0f, 1.0f), new Vector3(0.791882f, 0.791882f, 0.791882f) },
        { new Vector3(0.005f, 0.005f, 0.005f), new Vector3(0.022098f, 0.022098f, 0.022098f) },
        { new Vector3(16.0f, 16.0f, 16.0f), new Vector3(0.997846f, 0.997846f, 0.997846f) },
        { new Vector3(0.8f, 0.1f, 0.05f), new Vector3(0.802222f, 0.457965f, 0.369597f) },
        { new Vector3(0.05f, 0.6f, 0.1f), new Vector3(0.461295f, 0.70653f, 0.447377f) },
        { new Vector3(0.1f, 0.2f, 0.9f), new Vector3(0.443869f, 0.576f, 0.809908f) },
        { new Vector3(3.0f, 1.5f, 0.2f), new Vector3(0.931766f, 0.842444f, 0.677872f) },
        { new Vector3(0.02f, 0.03f, 0.2f), new Vector3(0.146402f, 0.264697f, 0.548025f) },
        { new Vector3(0.0346f, 0.0497f, 0.0429f), new Vector3(0.222322f, 0.27088f, 0.250827f) },
        { new Vector3(1.5396f, 4.4807f, 0.027f), new Vector3(0.885478f, 0.941124f, 0.728825f) },
        { new Vector3(0.0661f, 0.0388f, 0.0251f), new Vector3(0.318415f, 0.232934f, 0.178204f) },
    };

    public static TheoryData<Vector3, Vector3> AgxPunchyReference => new()
    {
        { new Vector3(0.18f, 0.18f, 0.18f), new Vector3(0.343441f, 0.343441f, 0.343441f) },
        { new Vector3(1.0f, 1.0f, 1.0f), new Vector3(0.70399f, 0.70399f, 0.70399f) },
        { new Vector3(0.005f, 0.005f, 0.005f), new Vector3(0.000165f, 0.000165f, 0.000165f) },
        { new Vector3(16.0f, 16.0f, 16.0f), new Vector3(0.996793f, 0.996793f, 0.996793f) },
    };

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance)
    {
        float err = Vector3.Abs(expected - actual) is var d ? MathF.Max(d.X, MathF.Max(d.Y, d.Z)) : 0f;
        Assert.True(err <= tolerance, $"expected {expected}, got {actual} (error {err:E2})");
    }

    [Theory]
    [MemberData(nameof(AcesReference))]
    public void Aces_MatchesOpenColorIO(Vector3 linear, Vector3 display) =>
        // 1e-3 of a display value is a quarter of an 8-bit code.
        AssertClose(display, new DisplayTransform(ViewTransform.Aces).Apply(linear), 1e-3f);

    [Theory]
    [MemberData(nameof(AgxReference))]
    public void AgX_MatchesTheReferenceImplementation(Vector3 linear, Vector3 display) =>
        AssertClose(display, new DisplayTransform(ViewTransform.AgX).Apply(linear), 2e-4f);

    [Theory]
    [MemberData(nameof(AgxPunchyReference))]
    public void AgXPunchy_MatchesTheReferenceImplementation(Vector3 linear, Vector3 display) =>
        AssertClose(display, new DisplayTransform(ViewTransform.AgX, ViewLook.Punchy).Apply(linear), 2e-4f);

    [Fact]
    public void Srgb_IsThePiecewiseCurve()
    {
        Assert.Equal(0f, Srgb.Encode(0f));
        Assert.Equal(1f, Srgb.Encode(1f), 6);
        Assert.Equal(0.0031308f * 12.92f, Srgb.Encode(0.0031308f), 6);
        Assert.Equal(0.461356f, Srgb.Encode(0.18f), 5);    // not 0.18^(1/2.2) = 0.4587
        for (float x = 0f; x <= 1f; x += 0.001f)
            Assert.InRange(Srgb.Decode(Srgb.Encode(x)) - x, -1e-6f, 1e-6f);
        // The two pieces meet with matching value at the breakpoint.
        Assert.Equal(Srgb.Decode(0.04045f - 1e-6f), Srgb.Decode(0.04045f + 1e-6f), 5);
    }

    [Theory]
    [InlineData(ViewTransform.AgX)]
    [InlineData(ViewTransform.Aces)]
    [InlineData(ViewTransform.Standard)]
    [InlineData(ViewTransform.Legacy)]
    public void Greys_StayNeutral_AndMonotonic(ViewTransform view)
    {
        var t = new DisplayTransform(view);
        float prev = -1f;
        for (double ev = -12; ev <= 8; ev += 0.05)
        {
            float g = (float)(0.18 * Math.Pow(2, ev));
            var o = t.Apply(new Vector3(g));
            Assert.InRange(MathF.Abs(o.X - o.Y) + MathF.Abs(o.Y - o.Z), 0f, 2e-5f);
            Assert.InRange(o.X, 0f, 1f);
            Assert.True(o.X >= prev, $"{view}: not monotonic at {g}");
            prev = o.X;
        }
    }

    [Fact]
    public void Legacy_IsThePreReviewCurve()
    {
        var t = DisplayTransform.Legacy;
        foreach (float x in new[] { 0f, 0.05f, 0.18f, 1f, 4f, 40f })
        {
            float m = x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f);
            Assert.Equal(MathF.Pow(Math.Clamp(m, 0f, 1f), 1f / 2.2f), t.Apply(new Vector3(x)).X, 6);
        }
    }

    [Fact]
    public void Standard_ClipsAndEncodes()
    {
        var t = new DisplayTransform(ViewTransform.Standard);
        AssertClose(new Vector3(Srgb.Encode(0.18f), 1f, 0f), t.Apply(new Vector3(0.18f, 3f, -1f)), 1e-6f);
    }
}
