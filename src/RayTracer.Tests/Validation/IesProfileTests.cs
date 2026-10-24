using System.Globalization;
using System.Numerics;
using System.Text;
using RayTracer.Lights;
using RayTracer.Scene;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>IES LM-63 photometric profiles (PIANO-REVIEW.md Phase 6).</summary>
[Collection("SceneLoader")]
public class IesProfileTests
{
    /// <summary>A type C file: one vertical row per horizontal angle.</summary>
    private static string Ies(float[] vertical, float[] horizontal, Func<float, float, float> cd, float multiplier = 1f)
    {
        var sb = new StringBuilder("IESNA:LM-63-2002\n[TEST] synthetic\nTILT=NONE\n");
        var inv = CultureInfo.InvariantCulture;
        sb.Append($"1 -1 {multiplier.ToString(inv)} {vertical.Length} {horizontal.Length} 1 2 0 0 0\n1 1 50\n");
        sb.Append(string.Join(" ", vertical.Select(v => v.ToString(inv)))).Append('\n');
        // Horizontal angles and the first candela values share a line, as files allow.
        sb.Append(string.Join(" ", horizontal.Select(h => h.ToString(inv)))).Append(' ');
        foreach (float h in horizontal)
            sb.Append(string.Join(" ", vertical.Select(v => cd(v, h).ToString("R", inv)))).Append('\n');
        return sb.ToString();
    }

    private static readonly float[] V = Enumerable.Range(0, 181).Select(i => (float)i).ToArray();

    /// <summary>A cosine downlight, normalised: for the emission tests.</summary>
    internal static IesProfile ConeProfile()
        => IesProfile.Parse(Ies(V, new[] { 0f }, (v, _) => v <= 90f ? 1000f * MathF.Cos(v * MathF.PI / 180f) : 0f, 2f));

    [Fact]
    public void CosineDownlight_HasFluxPiTimesPeak()
    {
        var p = IesProfile.Parse(Ies(V, new[] { 0f }, (v, _) => v <= 90f ? 1000f * MathF.Cos(v * MathF.PI / 180f) : 0f, 2f));
        Assert.Equal(2000f, p.MaxCandela, 1);                         // multiplier applied
        Assert.Equal(2000f * MathF.PI, p.Flux, 0);
        Assert.Equal(2000f * MathF.Cos(MathF.PI / 4f), p.Candela(45f, 123f), 1);
        Assert.Equal(0f, p.Candela(120f, 0f));
        // As a direction: straight down is the nadir.
        Assert.Equal(2000f, p.Candela(-Vector3.UnitY, -Vector3.UnitY, Vector3.UnitX), 1);
    }

    [Fact]
    public void QuadrantSymmetry_MirrorsTheMissingPlanes()
    {
        var p = IesProfile.Parse(Ies(V, new[] { 0f, 90f }, (_, h) => h == 0f ? 100f : 300f));
        Assert.Equal(100f, p.Candela(30f, 0f), 2);
        Assert.Equal(300f, p.Candela(30f, 90f), 2);
        Assert.Equal(200f, p.Candela(30f, 45f), 2);
        Assert.Equal(100f, p.Candela(30f, 180f), 2);
        Assert.Equal(300f, p.Candela(30f, 270f), 2);
        Assert.Equal(200f, p.Candela(30f, 315f), 2);
    }

    [Fact]
    public void PointLightWithIes_EmitsTheFluxAsked()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"3dray-ies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "down.ies"),
            Ies(V, new[] { 0f }, (v, _) => v <= 90f ? 500f * MathF.Cos(v * MathF.PI / 180f) : 0f));
        string yaml = @"
camera: { position: [0, 0, 5], look_at: [0, 0, 0] }
lights:
  - { type: point, position: [0, 0, 0], ies: down.ies, power: 1200, power_unit: lumen }
  - { type: point, position: [0, 0, 0], ies: down.ies }
entities: []
";
        File.WriteAllText(Path.Combine(dir, "s.yaml"), yaml);
        try
        {
            var lights = SceneLoader.Load(Path.Combine(dir, "s.yaml"), 16, 16).Item3;
            Assert.InRange(LightPowerTests.MeasuredFlux(lights[0], Vector3.Zero), 1164f, 1236f);
            Assert.InRange(LightPowerTests.MeasuredFlux(lights[1], Vector3.Zero), 500f * MathF.PI * 0.97f, 500f * MathF.PI * 1.03f);
            Assert.Equal(1200f, lights[0].ApproximatePower(new RayTracer.Core.AABB(new Vector3(-1), new Vector3(1))), 0);
        }
        finally { Directory.Delete(dir, true); }
    }
}
