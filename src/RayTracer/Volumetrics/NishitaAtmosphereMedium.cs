using System.Numerics;
using RayTracer.Core;
using RayTracer.Core.Sampling;
using RayTracer.Rendering.Sky;

namespace RayTracer.Volumetrics;

/// <summary>
/// The Earth's atmosphere as a medium, for aerial perspective: the same air,
/// dust and ozone as the physical skies (<see cref="Atmosphere"/>), over a
/// flat ground (a scene spans kilometres, not the planet's curvature).
///
/// <list type="bullet">
/// <item>Air: Rayleigh scattering, density exp(−h / 8 km), Rayleigh phase.</item>
/// <item>Dust: Mie scattering and absorption, exp(−h / 1.2 km), Henyey-Greenstein
///   g = 0.76 (or the medium's <c>phase</c>).</item>
/// <item>Ozone: absorption only, a tent from 10 to 40 km peaking at 25 km.</item>
/// </list>
///
/// <para>Altitude h is <c>(y − sea level) × world scale</c> metres. Below sea
/// level the densities stay at their sea-level values; above the top of the
/// atmosphere (60 km) they are zero. Each density's integral over altitude
/// has a closed form, so the optical depth along any ray is exact and the
/// distance is sampled by inverting it (safeguarded Newton): no tracking,
/// no truncation. At a scattering event the species is picked in proportion
/// to its share of σ_s for the light the path carries, and its own phase
/// function takes over: air does not scatter forward like dust. (The old
/// medium tracked a single averaged channel and recoloured with
/// exp(τ̄ − τ_c), unbounded at sunset, and shared one phase function between
/// the species: §3.60.)</para>
///
/// <para><b>Aerial perspective, not a second sky.</b> The physical skies
/// already hold the whole atmosphere's light along an unobstructed ray, and
/// the sun's disc its transmittance. So the medium acts only on finite
/// stretches, between the eye (or a scattering point) and the surface it
/// sees: a ray, or a shadow ray, that runs to the sky crosses it untouched.
/// Applied to those too it counted the atmosphere twice: a washed-out sky
/// and a sun dimmed twice.</para>
/// </summary>
public sealed class NishitaAtmosphereMedium : AnalyticMedium
{
    private const double Top = 60_000.0;   // m, as the skies' atmosphere
    private const double OzoneLow = 10_000.0, OzonePeak = 25_000.0, OzoneHigh = 40_000.0;

    public float AirDensity { get; }
    public float DustDensity { get; }
    public float OzoneDensity { get; }
    public float SeaLevelY { get; }
    /// <summary>Metres per world unit. 1000 ≈ "1 world unit = 1 km".</summary>
    public float WorldScale { get; }

    private readonly IPhaseFunction _airPhase = new RayleighPhase();
    private readonly Vector3 _air, _dustS, _dustT, _ozone;   // per world unit, at the profile's peak

    public NishitaAtmosphereMedium(IPhaseFunction? dustPhase = null, float airDensity = 1f, float dustDensity = 1f,
                                   float ozoneDensity = 1f, float seaLevelY = 0f, float worldScale = 1000f)
        : base(dustPhase ?? new HenyeyGreensteinPhase(Atmosphere.MieAsymmetry))
    {
        AirDensity = MathF.Max(0f, airDensity);
        DustDensity = MathF.Max(0f, dustDensity);
        OzoneDensity = MathF.Max(0f, ozoneDensity);
        SeaLevelY = seaLevelY;
        WorldScale = MathF.Max(1e-3f, worldScale);
        _air = Atmosphere.RayleighScattering * (AirDensity * WorldScale);
        _dustS = Atmosphere.MieScattering * (DustDensity * WorldScale);
        _dustT = (Atmosphere.MieScattering + Atmosphere.MieAbsorption) * (DustDensity * WorldScale);
        _ozone = Atmosphere.OzoneAbsorption * (OzoneDensity * WorldScale);
    }

    // ── Density profiles in altitude (metres) and their integrals ──────────

    private static double Exponential(double h, double scale)
        => h <= 0.0 ? 1.0 : h >= Top ? 0.0 : Math.Exp(-h / scale);

    /// <summary>∫₀ʰ ρ(x) dx for ρ = exp(−x / H), 1 below 0, 0 above the top.</summary>
    private static double ExponentialColumn(double h, double scale)
        => h <= 0.0 ? h : scale * -ExpM1(-Math.Min(h, Top) / scale);

    private static double Ozone(double h)
        => h <= OzoneLow || h >= OzoneHigh ? 0.0 : 1.0 - Math.Abs(h - OzonePeak) / (OzonePeak - OzoneLow);

    private static double OzoneColumn(double h)
    {
        const double w = OzonePeak - OzoneLow;
        if (h <= OzoneLow) return 0.0;
        if (h <= OzonePeak) return (h - OzoneLow) * (h - OzoneLow) / (2.0 * w);
        if (h < OzoneHigh) return w - (OzoneHigh - h) * (OzoneHigh - h) / (2.0 * w);
        return w;
    }

    private static double ExpM1(double x) => Math.Abs(x) < 1e-5 ? x + 0.5 * x * x : Math.Exp(x) - 1.0;

    private double Altitude(Ray ray, double t) => (ray.Origin.Y + t * ray.Direction.Y - SeaLevelY) * WorldScale;

    /// <summary>The three profiles integrated over [0, t] of the ray, in world units.</summary>
    private (double Air, double Dust, double Ozone) Columns(Ray ray, double t)
    {
        double h0 = Altitude(ray, 0.0);
        double dh = ray.Direction.Y * (double)WorldScale;   // metres of altitude per world unit
        if (Math.Abs(dh * t) < 1e-3)
        {
            // Level ray: the profiles at mid-segment (the column difference would cancel).
            double hm = h0 + 0.5 * dh * t;
            return (t * Exponential(hm, Atmosphere.RayleighScaleHeight),
                    t * Exponential(hm, Atmosphere.MieScaleHeight), t * Ozone(hm));
        }
        double h1 = h0 + dh * t;
        return ((ExponentialColumn(h1, Atmosphere.RayleighScaleHeight) - ExponentialColumn(h0, Atmosphere.RayleighScaleHeight)) / dh,
                (ExponentialColumn(h1, Atmosphere.MieScaleHeight) - ExponentialColumn(h0, Atmosphere.MieScaleHeight)) / dh,
                (OzoneColumn(h1) - OzoneColumn(h0)) / dh);
    }

    /// <summary>A stretch this long runs to the sky (see the class remarks).</summary>
    private const float SkyDistance = 1e29f;

    public override Vector3 OpticalDepth(Ray ray, float t)
    {
        if (t >= SkyDistance) return Vector3.Zero;
        var (a, d, o) = Columns(ray, t);
        return _air * (float)a + _dustT * (float)d + _ozone * (float)o;
    }

    private double ChannelDepth(Ray ray, int c, double t)
    {
        var (a, d, o) = Columns(ray, t);
        return MediumMath.Channel(_air, c) * a + MediumMath.Channel(_dustT, c) * d + MediumMath.Channel(_ozone, c) * o;
    }

    private double ChannelSigmaT(Ray ray, int c, double t)
    {
        double h = Altitude(ray, t);
        return MediumMath.Channel(_air, c) * Exponential(h, Atmosphere.RayleighScaleHeight)
             + MediumMath.Channel(_dustT, c) * Exponential(h, Atmosphere.MieScaleHeight)
             + MediumMath.Channel(_ozone, c) * Ozone(h);
    }

    public override Vector3 SigmaT(Ray ray, float t)
    {
        double h = Altitude(ray, t);
        return _air * (float)Exponential(h, Atmosphere.RayleighScaleHeight)
             + _dustT * (float)Exponential(h, Atmosphere.MieScaleHeight)
             + _ozone * (float)Ozone(h);
    }

    /// <summary>Local σ_s (air + dust) at a world altitude, per world unit.</summary>
    public Vector3 SigmaS_AtY(float y)
    {
        double h = (y - SeaLevelY) * (double)WorldScale;
        return _air * (float)Exponential(h, Atmosphere.RayleighScaleHeight) + _dustS * (float)Exponential(h, Atmosphere.MieScaleHeight);
    }

    /// <summary>Local σ_t at a world altitude, per world unit.</summary>
    public Vector3 SigmaT_AtY(float y) => SigmaT(new Ray(new Vector3(0f, y, 0f), Vector3.UnitX), 0f);

    protected override float DistanceAtDepth(Ray ray, int c, float tau, float tMax)
    {
        if (tMax >= SkyDistance) return float.PositiveInfinity;
        // τ_c(t) is continuous and increasing (its derivative is σ_t,c ≥ 0):
        // Newton's method, kept inside a bracket that bisection shrinks
        // whenever a step would leave it.
        double hi = tMax;
        if (ray.Direction.Y > 0f)
        {
            // Nothing above the top of the atmosphere.
            double tTop = (Top / WorldScale + SeaLevelY - ray.Origin.Y) / ray.Direction.Y;
            hi = Math.Min(hi, Math.Max(tTop, 0.0));
        }
        if (ChannelDepth(ray, c, hi) < tau) return float.PositiveInfinity;
        double lo = 0.0;
        double sigma0 = ChannelSigmaT(ray, c, 0.0);
        double t = sigma0 > 0.0 ? Math.Min(tau / sigma0, hi) : 0.5 * hi;
        for (int i = 0; i < 64; i++)
        {
            double f = ChannelDepth(ray, c, t) - tau;
            if (Math.Abs(f) <= 1e-7 * tau) break;
            if (f > 0.0) hi = t; else lo = t;
            double d = ChannelSigmaT(ray, c, t);
            double next = d > 0.0 ? t - f / d : double.NaN;
            t = next > lo && next < hi ? next : 0.5 * (lo + hi);
            if (hi - lo <= 1e-7 * hi) break;
        }
        return t < tMax ? (float)t : float.PositiveInfinity;
    }

    public override bool ActsOn(float tMax) => tMax < SkyDistance;

    public override Vector3 InScattering(Ray ray, float t, Vector3 wo, Vector3 wi)
    {
        double h = Altitude(ray, t);
        return _air * ((float)Exponential(h, Atmosphere.RayleighScaleHeight) * _airPhase.Evaluate(wo, wi))
             + _dustS * ((float)Exponential(h, Atmosphere.MieScaleHeight) * Phase.Evaluate(wo, wi));
    }

    protected override (Vector3 SigmaS, IPhaseFunction Phase) Scattering(Ray ray, float t, Vector3 throughput)
    {
        double h = Altitude(ray, t);
        Vector3 air = _air * (float)Exponential(h, Atmosphere.RayleighScaleHeight);
        Vector3 dust = _dustS * (float)Exponential(h, Atmosphere.MieScaleHeight);
        // One species per event, in proportion to the light it scatters: the
        // weight σ_s,k / P_k averages to Σ σ_s,k p_k.
        Vector3 w = Vector3.Max(throughput, Vector3.Zero);
        float pa = MediumMath.Sum(air * w), pd = MediumMath.Sum(dust * w);
        if (!(pa + pd > 0f)) { pa = MediumMath.Sum(air); pd = MediumMath.Sum(dust); }
        if (!(pa + pd > 0f)) return (Vector3.Zero, Phase);
        float pAir = pa / (pa + pd);
        return Sampler.SampleLoose1D() < pAir ? (air / pAir, _airPhase) : (dust / (1f - pAir), Phase);
    }
}
