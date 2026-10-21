using System.Numerics;

namespace RayTracer.Materials;

/// <summary>
/// Fresnel reflectance of a conductor (PIANO-REVIEW.md, Phase 5), in two
/// forms: the exact unpolarised Fresnel equations for a complex index of
/// refraction n + ik (measured metals), and the artist-friendly F82 model of
/// OpenPBR (Kutz et al. 2021): Schlick's curve from the normal-incidence colour
/// F0, pulled down near grazing so that at μ = 1/7 (82°) it reaches
/// <c>tint · Schlick(1/7)</c>, the dip real metals show before rising to 1.
/// Both come with their cosine-weighted hemispherical average
/// F_avg = 2 ∫ F(μ) μ dμ, which the multiple-scattering lobe needs.
/// </summary>
public static class ConductorFresnel
{
    /// <summary>Exact unpolarised reflectance for cos θ = <paramref name="cosTheta"/>
    /// and complex IOR <paramref name="eta"/> + i<paramref name="k"/>, one channel.</summary>
    public static double Complex(double cosTheta, double eta, double k)
    {
        cosTheta = Math.Clamp(cosTheta, 0.0, 1.0);
        var n = new System.Numerics.Complex(eta, k);
        double sin2 = 1.0 - cosTheta * cosTheta;
        var sin2t = sin2 / (n * n);
        var cost = System.Numerics.Complex.Sqrt(1.0 - sin2t);
        var rParl = (n * cosTheta - cost) / (n * cosTheta + cost);
        var rPerp = (cosTheta - n * cost) / (cosTheta + n * cost);
        return 0.5 * (rParl.Magnitude * rParl.Magnitude + rPerp.Magnitude * rPerp.Magnitude);
    }

    public static Vector3 Complex(float cosTheta, Vector3 eta, Vector3 k) => new(
        (float)Complex(cosTheta, eta.X, k.X),
        (float)Complex(cosTheta, eta.Y, k.Y),
        (float)Complex(cosTheta, eta.Z, k.Z));

    /// <summary>F_avg of the complex Fresnel, by midpoint quadrature on 256
    /// points (the integrand is smooth).</summary>
    public static Vector3 ComplexAverage(Vector3 eta, Vector3 k)
    {
        const int n = 256;
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < n; i++)
        {
            double mu = (i + 0.5) / n;
            r += Complex(mu, eta.X, k.X) * mu;
            g += Complex(mu, eta.Y, k.Y) * mu;
            b += Complex(mu, eta.Z, k.Z) * mu;
        }
        return new Vector3((float)(2 * r / n), (float)(2 * g / n), (float)(2 * b / n));
    }

    private const float MuBar = 1f / 7f;
    private static readonly float SchlickBar = MathF.Pow(1f - MuBar, 5f);
    private static readonly float DipBar = MuBar * MathF.Pow(1f - MuBar, 6f);

    /// <summary>The F82 coefficient a: F(μ) = Schlick(μ) − a μ (1 − μ)^6.</summary>
    private static Vector3 F82Coefficient(Vector3 f0, Vector3 tint)
    {
        Vector3 fBar = f0 + (Vector3.One - f0) * SchlickBar;
        return fBar * (Vector3.One - tint) / DipBar;
    }

    public static Vector3 F82(float cosTheta, Vector3 f0, Vector3 tint)
    {
        float mu = Math.Clamp(cosTheta, 0f, 1f);
        float m = 1f - mu, m2 = m * m, m5 = m2 * m2 * m;
        Vector3 schlick = f0 + (Vector3.One - f0) * m5;
        Vector3 f = schlick - F82Coefficient(f0, tint) * (mu * m5 * m);
        return Vector3.Clamp(f, Vector3.Zero, Vector3.One);
    }

    /// <summary>Closed form: 2∫(1−μ)^5 μ dμ = 1/21 and 2∫μ²(1−μ)^6 dμ = 1/126.</summary>
    public static Vector3 F82Average(Vector3 f0, Vector3 tint) =>
        Vector3.Clamp(f0 + (Vector3.One - f0) / 21f - F82Coefficient(f0, tint) / 126f, Vector3.Zero, Vector3.One);
}

/// <summary>Complex index of refraction n + ik per RGB channel.</summary>
public readonly record struct ConductorIor(Vector3 Eta, Vector3 K)
{
    /// <summary>
    /// Measured metals. Values are the RGB reduction of the spectral data
    /// that Mitsuba 3 ships (scalar_rgb variant), so a preset renders the same
    /// metal there and here.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ConductorIor> Presets =
        new Dictionary<string, ConductorIor>(StringComparer.OrdinalIgnoreCase)
        {
            ["gold"]       = new(new(0.143036f, 0.375307f, 1.44205f),  new(3.983f, 2.38556f, 1.60336f)),
            ["silver"]     = new(new(0.155276f, 0.116728f, 0.138388f), new(4.82835f, 3.12222f, 2.1469f)),
            ["copper"]     = new(new(0.201005f, 0.92375f, 1.10222f),   new(3.91326f, 2.45305f, 2.14209f)),
            ["aluminium"]  = new(new(1.6575f, 0.880405f, 0.521244f),   new(9.22381f, 6.2695f, 4.837f)),
            ["chromium"]   = new(new(4.36967f, 2.91651f, 1.6549f),     new(5.20655f, 4.23131f, 3.7547f)),
            ["tungsten"]   = new(new(4.37082f, 3.30025f, 2.99841f),    new(3.50064f, 2.60487f, 2.27323f)),
            ["nickel"]     = new(new(2.367305f, 1.663373f, 1.467078f), new(4.498812f, 3.050153f, 2.345439f)),
            ["iridium"]    = new(new(3.0863f, 2.082191f, 1.617915f),   new(5.592153f, 4.067088f, 3.267312f)),
            ["molybdenum"] = new(new(4.483974f, 3.525131f, 2.77631f),  new(4.111437f, 3.420827f, 3.150607f)),
            ["rhodium"]    = new(new(2.585793f, 1.860216f, 1.554263f), new(6.78226f, 4.702987f, 3.976073f)),
            ["tantalum"]   = new(new(2.062721f, 2.392939f, 2.627929f), new(2.40836f, 1.741486f, 1.947028f)),
            ["titanium_nitride"] = new(new(1.64845f, 1.150516f, 1.379823f), new(3.368328f, 1.943498f, 1.102068f)),
            ["vanadium"]   = new(new(4.277582f, 3.512949f, 2.761071f), new(3.491301f, 2.889455f, 3.111522f)),
            ["beryllium"]  = new(new(4.185012f, 3.185029f, 2.784061f), new(3.835466f, 3.010122f, 2.868994f)),
            ["mercury"]    = new(new(2.398939f, 1.440028f, 0.90957f),  new(6.327587f, 4.371925f, 3.421764f)),
        };
}
