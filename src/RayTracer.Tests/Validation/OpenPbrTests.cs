using System.Numerics;
using RayTracer.Core;
using RayTracer.Materials;
using RayTracer.Textures;
using Xunit;

namespace RayTracer.Tests.Validation;

/// <summary>
/// The OpenPBR material (PIANO-REVIEW.md, Phase 5) against the conditions its
/// specification sets: the white furnace in every configuration it lists,
/// sampling consistent with Evaluate and Pdf for every lobe, and exact
/// reductions to the conductor and the rough dielectric already validated
/// against Mitsuba.
/// </summary>
public class OpenPbrTests
{
    private static SolidColor C(float r, float g, float b) => new(new Vector3(r, g, b));
    private static SolidColor C(float v) => new(new Vector3(v));

    private static readonly OpenPbrParameters WhiteDiffuse = new() { BaseColor = C(1f) };

    public static TheoryData<string> FurnaceConfigs => new()
    {
        "diffuse", "diffuse-smooth-spec", "diffuse-rough", "metal", "metal-smooth", "coat", "coat-smooth",
        "fuzz", "thin-subsurface", "thin-subsurface-rough", "opacity", "everything",
    };

    private static OpenPbrParameters Furnace(string name) => name switch
    {
        "diffuse" => WhiteDiffuse,
        "diffuse-smooth-spec" => new() { BaseColor = C(1f), SpecularRoughness = 0f },
        "diffuse-rough" => new() { BaseColor = C(1f), BaseDiffuseRoughness = 0.8f, SpecularRoughness = 0.6f },
        "metal" => new() { BaseColor = C(1f), BaseMetalness = 1f, SpecularRoughness = 0.5f },
        "metal-smooth" => new() { BaseColor = C(1f), BaseMetalness = 1f, SpecularRoughness = 0f },
        "coat" => new() { BaseColor = C(1f), CoatWeight = 1f, CoatRoughness = 0.2f },
        "coat-smooth" => new() { BaseColor = C(1f), CoatWeight = 1f, CoatRoughness = 0f, CoatIor = 1.8f },
        "fuzz" => new() { BaseColor = C(1f), FuzzWeight = 1f, FuzzRoughness = 0.4f },
        "thin-subsurface" => new() { ThinWalled = true, SubsurfaceWeight = 1f, SubsurfaceColor = C(1f) },
        "thin-subsurface-rough" => new()
        {
            ThinWalled = true, SubsurfaceWeight = 1f, SubsurfaceColor = C(1f), BaseDiffuseRoughness = 0.7f,
            SubsurfaceScatterAnisotropy = 0.4f,
        },
        "opacity" => new() { BaseColor = C(1f), GeometryOpacity = 0.3f },
        "everything" => new()
        {
            BaseColor = C(1f), BaseMetalness = 0.4f, BaseDiffuseRoughness = 0.5f, SpecularRoughness = 0.35f,
            SpecularRoughnessAnisotropy = 0.5f, CoatWeight = 0.6f, CoatRoughness = 0.15f, FuzzWeight = 0.5f,
            ThinFilmWeight = 0.7f, ThinFilmThickness = 0.4f,
        },
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(FurnaceConfigs))]
    public void WhiteFurnace_ReflectsEverything(string config)
    {
        // The specification's list of energy-preserving configurations: a
        // white base, metallic or not, under any coat and fuzz, with any
        // roughness, anisotropy, thin film and opacity.
        var m = new OpenPbr(Furnace(config));
        foreach (float cosV in new[] { 0.95f, 0.6f, 0.25f })
        {
            var r = BsdfTestKit.Run(config, m, cosV, samples: 200_000);
            Assert.True(MathF.Abs(r.Albedo.X - 1f) < 0.012f && MathF.Abs(r.Albedo.Z - 1f) < 0.012f, r.ToString());
        }
    }

    public static TheoryData<string, float, bool> SamplingConfigs => new()
    {
        { "diffuse", 0.8f, true },
        { "diffuse-rough", 0.3f, true },
        { "metal", 0.5f, true },
        { "coat", 0.7f, true },
        { "fuzz", 0.4f, true },
        { "everything", 0.6f, true },
        { "everything", 0.2f, true },
        { "glass", 0.8f, true },
        { "glass", 0.4f, false },
        { "subsurface", 0.7f, true },
        { "thin-glass", 0.6f, true },
        { "thin-subsurface-rough", 0.5f, true },
    };

    private static OpenPbrParameters Sampling(string name) => name switch
    {
        "glass" => new() { TransmissionWeight = 1f, SpecularRoughness = 0.35f },
        "subsurface" => new() { SubsurfaceWeight = 0.6f, BaseColor = C(0.7f, 0.5f, 0.3f), SpecularRoughness = 0.4f },
        "thin-glass" => new() { ThinWalled = true, TransmissionWeight = 0.8f, SpecularRoughness = 0.3f, TransmissionColor = C(0.9f, 0.6f, 0.3f) },
        _ => Furnace(name),
    };

    [Theory]
    [MemberData(nameof(SamplingConfigs))]
    public void Sampling_DrawsThePdf_AndItsWeightIntegratesTheBsdf(string config, float cosV, bool frontFace)
    {
        var m = new OpenPbr(Sampling(config));
        var r = BsdfTestKit.Run(config, m, cosV, frontFace: frontFace, sub: 16);
        Assert.True(r.PValue > 1e-4, r.ToString());
        Assert.True(r.PdfIntegralError < 0.01, r.ToString());
        Assert.True(r.AlbedoCrossCheckError < 0.02, r.ToString());
        Assert.Equal(0, r.InconsistentSamples);
    }

    // ── Reductions to the validated materials ────────────────────────────────

    private static double WorstGap(IMaterial a, IMaterial b, bool frontFace, bool transmission)
    {
        var rec = BsdfTestKit.MakeRec(frontFace);
        var rng = new Random(17);
        double worst = 0;
        for (int i = 0; i < 3000; i++)
        {
            double t1 = Math.Acos(rng.NextDouble()), p1 = 2 * Math.PI * rng.NextDouble();
            double t2 = Math.Acos(rng.NextDouble()), p2 = 2 * Math.PI * rng.NextDouble();
            var v = new Vector3((float)(Math.Sin(t1) * Math.Cos(p1)), (float)Math.Cos(t1), (float)(Math.Sin(t1) * Math.Sin(p1)));
            float sign = transmission && (i & 1) == 1 ? -1f : 1f;
            var l = new Vector3((float)(Math.Sin(t2) * Math.Cos(p2)), sign * (float)Math.Cos(t2), (float)(Math.Sin(t2) * Math.Sin(p2)));
            Vector3 fa = a.Evaluate(v, l, rec), fb = b.Evaluate(v, l, rec);
            float pa = a.Pdf(v, l, rec), pb = b.Pdf(v, l, rec);
            for (int c = 0; c < 3; c++)
            {
                float x = c == 0 ? fa.X : c == 1 ? fa.Y : fa.Z, y = c == 0 ? fb.X : c == 1 ? fb.Y : fb.Z;
                worst = Math.Max(worst, Math.Abs(x - y) / Math.Max(Math.Max(x, y), 1e-2f));
            }
            if (pa > 0f || pb > 0f)
                worst = Math.Max(worst, Math.Abs(pa - pb) / Math.Max(Math.Max(pa, pb), 1e-2f));
        }
        return worst;
    }

    [Theory]
    [InlineData(0.4f, true)]
    [InlineData(0.4f, false)]
    [InlineData(0.15f, true)]
    public void FullTransmission_IsTheRoughDielectric(float roughness, bool frontFace)
    {
        var openpbr = new OpenPbr(new OpenPbrParameters { TransmissionWeight = 1f, SpecularRoughness = roughness });
        var glass = new Dielectric(1.5f, new SolidColor(Vector3.One), roughness);
        Assert.True(WorstGap(openpbr, glass, frontFace, transmission: true) < 2e-3);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.7f)]
    public void FullMetalness_IsTheConductor(float roughness)
    {
        var color = new Vector3(0.95f, 0.64f, 0.54f);
        var tint = new Vector3(0.9f, 0.8f, 0.85f);
        var openpbr = new OpenPbr(new OpenPbrParameters
        {
            BaseMetalness = 1f, BaseColor = new SolidColor(color), SpecularColor = new SolidColor(tint),
            SpecularRoughness = roughness,
        });
        var metal = new Metal(new SolidColor(color), roughness, new SolidColor(tint));
        Assert.True(WorstGap(openpbr, metal, true, transmission: false) < 2e-3);
    }

    [Fact]
    public void NoSpecular_IsLambert()
    {
        // specular_weight 0 makes the interface index-matched: what is left
        // is the diffuse base, Lambert at diffuse roughness 0.
        var rho = new Vector3(0.2f, 0.5f, 0.8f);
        var m = new OpenPbr(new OpenPbrParameters { BaseColor = new SolidColor(rho), SpecularWeight = 0f });
        var rec = BsdfTestKit.MakeRec();
        Vector3 f = m.Evaluate(BsdfTestKit.View(0.7f), Vector3.Normalize(new Vector3(-0.3f, 0.8f, 0.2f)), rec);
        Assert.Equal(rho.X / MathF.PI, f.X, 5);
        Assert.Equal(rho.Z / MathF.PI, f.Z, 5);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.5f)]
    [InlineData(2.0f)]
    public void SpecularWeight_ScalesTheNormalReflectance(float weight)
    {
        // A smooth interface over a black base: the albedo at normal
        // incidence is the reflectance, weight × F0(1.5) = weight × 0.04.
        var m = new OpenPbr(new OpenPbrParameters
        {
            BaseColor = C(0f), SpecularRoughness = 0f, SpecularWeight = weight,
        });
        var r = BsdfTestKit.Run("spec-weight", m, 1f, samples: 20_000);
        Assert.InRange(r.Albedo.X, weight * 0.04f - 2e-4f, weight * 0.04f + 2e-4f);
    }

    [Fact]
    public void CoatColor_IsTheTintAtNormalIncidence()
    {
        // A white smooth mirror under a smooth absorbing coat of index 1:
        // the reflection crosses the coat twice and comes out coat_color.
        var cc = new Vector3(0.8f, 0.5f, 0.2f);
        var m = new OpenPbr(new OpenPbrParameters
        {
            BaseColor = C(1f), BaseMetalness = 1f, SpecularRoughness = 0f,
            CoatWeight = 1f, CoatColor = new SolidColor(cc), CoatIor = 1.0001f, CoatDarkening = 0f,
        });
        var r = BsdfTestKit.Run("coat-tint", m, 1f, samples: 20_000);
        Assert.InRange(r.Albedo.X, cc.X - 2e-3f, cc.X + 2e-3f);
        Assert.InRange(r.Albedo.Z, cc.Z - 2e-3f, cc.Z + 2e-3f);
    }

    [Fact]
    public void CoatDarkening_DarkensAGreyBase()
    {
        // Internal reflections in the coat send light back to the base, which
        // absorbs part of it: a grey base under a clear coat is darker than the
        // undarkened albedo lerp(F0, 1, E_b) unless darkening is turned off.
        OpenPbrParameters P(float darkening) => new()
        {
            BaseColor = C(0.5f), SpecularWeight = 0f, CoatWeight = 1f, CoatRoughness = 0f, CoatDarkening = darkening,
        };
        var dark = BsdfTestKit.Run("dark", new OpenPbr(P(1f)), 1f, samples: 200_000).Albedo.X;
        var light = BsdfTestKit.Run("light", new OpenPbr(P(0f)), 1f, samples: 200_000).Albedo.X;
        float f0 = MathF.Pow(0.6f / 2.6f, 2f);
        Assert.InRange(light, f0 + (1 - f0) * 0.5f - 0.01f, f0 + (1 - f0) * 0.5f + 0.01f);
        Assert.True(dark < light - 0.03f, $"darkened {dark}, undarkened {light}");
    }

    [Fact]
    public void Emission_IsColourTimesLuminance_OnTheFrontFace()
    {
        var m = new OpenPbr(new OpenPbrParameters
        {
            EmissionLuminance = 3f, EmissionColor = new SolidColor(new Vector3(1f, 0.5f, 0.25f)),
        });
        Assert.Equal(new Vector3(3f, 1.5f, 0.75f), m.Emit(0.5f, 0.5f, Vector3.Zero, 0, frontFace: true));
        Assert.Equal(Vector3.Zero, m.Emit(0.5f, 0.5f, Vector3.Zero, 0, frontFace: false));
    }
}
