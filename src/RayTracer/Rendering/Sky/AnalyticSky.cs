using System.Numerics;
using RayTracer.Core;
using RayTracer.Lights;

namespace RayTracer.Rendering.Sky;

/// <summary>Units of a physical sky (YAML <c>units:</c>).</summary>
public enum SkyUnits
{
    /// <summary>
    /// The scale the sky body always had: kcd/m² for Preetham and Hosek-Wilkie,
    /// a top-of-atmosphere illuminance of 20 for Nishita. The sun matches it.
    /// </summary>
    Relative,

    /// <summary>
    /// Luminance in nits, the scale of the photometric lights and of the
    /// camera's EV100 exposure (<see cref="LightUnits"/>): a clear noon sky is
    /// thousands of nits, the sun's disc about 10⁹.
    /// </summary>
    Photometric,
}

/// <summary>
/// What the physical skies share (PIANO-REVIEW.md Phase 6): the sun's disc,
/// derived from the same top-of-atmosphere illuminance as the sky body through
/// <see cref="Rendering.Sky.Atmosphere"/>; the ground below the horizon, a
/// Lambertian plane of <see cref="GroundAlbedo"/> lit by that sky and sun; and
/// the spherical mean luminance, integrated rather than guessed.
/// </summary>
public abstract class AnalyticSky : ISkyModel
{
    /// <summary>Direction TO the sun (sky-local, normalized).</summary>
    public Vector3 SunDirection { get; }

    /// <summary>Albedo of the ground below the horizon.</summary>
    public Vector3 GroundAlbedo { get; }

    /// <summary>Multiplier on the sky body and on the sun.</summary>
    public float Intensity { get; }

    public SkyUnits Units { get; }

    /// <summary>The atmosphere the sun's light crosses.</summary>
    public Atmosphere Atmosphere { get; }

    /// <summary>Illuminance on a horizontal plane at the ground, sky body and sun, before <see cref="Intensity"/>.</summary>
    public float SkyIlluminance { get; private set; }

    /// <summary>The sun's part of <see cref="SkyIlluminance"/>.</summary>
    public float SunIlluminance { get; private set; }

    private static readonly float SunCosHalfAngle = MathF.Cos(MathUtils.DegreesToRadians(Atmosphere.SunHalfAngleDeg));

    private Vector3 _sunRadiance;
    private Vector3 _ground;
    private float _averageLuminance;

    protected AnalyticSky(Vector3 sunDirToSun, Vector3 groundAlbedo, float intensity, SkyUnits units, Atmosphere atmosphere)
    {
        SunDirection = Vector3.Normalize(sunDirToSun);
        GroundAlbedo = Vector3.Clamp(groundAlbedo, Vector3.Zero, Vector3.One);
        Intensity = MathF.Max(0f, intensity);
        Units = units;
        Atmosphere = atmosphere;
    }

    /// <summary>Radiance of the sky body toward <paramref name="dir"/> (<c>dir.Y ≥ 0</c>), before <see cref="Intensity"/>.</summary>
    protected abstract Vector3 Above(Vector3 dir);

    /// <summary>
    /// Completes the model once the derived class is ready to evaluate
    /// <see cref="Above"/>: the sun's disc for a top-of-atmosphere illuminance
    /// of <paramref name="topIlluminance"/> (in the sky's units), the ground,
    /// and the mean luminance.
    /// </summary>
    protected void Finish(float topIlluminance)
    {
        _sunRadiance = Atmosphere.SunDiscRadiance(SunDirection, topIlluminance);

        // Upper hemisphere, midpoint rule in (θ, φ): the illuminance on the
        // ground and the mean luminance. Smooth bodies, so 48 × 96 is plenty.
        const int nTheta = 48, nPhi = 96;
        double irradiance = 0, luminance = 0;
        Vector3 e = Vector3.Zero;
        for (int i = 0; i < nTheta; i++)
        {
            double theta = (i + 0.5) * (Math.PI / 2) / nTheta;
            float sinT = (float)Math.Sin(theta), cosT = (float)Math.Cos(theta);
            double ringLum = 0;
            Vector3 ring = Vector3.Zero;
            for (int j = 0; j < nPhi; j++)
            {
                double phi = (j + 0.5) * 2 * Math.PI / nPhi;
                var dir = new Vector3(sinT * (float)Math.Cos(phi), cosT, sinT * (float)Math.Sin(phi));
                Vector3 l = Above(dir);
                ring += l;
                ringLum += MathUtils.Luminance(l);
            }
            double w = sinT * (Math.PI / 2 / nTheta) * (2 * Math.PI / nPhi);
            e += ring * (float)(w * cosT);
            luminance += ringLum * w;
        }
        Vector3 sunE = _sunRadiance * (Atmosphere.SunSolidAngle * MathF.Max(0f, SunDirection.Y));
        e += sunE;
        SunIlluminance = MathUtils.Luminance(sunE);
        SkyIlluminance = MathUtils.Luminance(e);
        _ground = GroundAlbedo * e / MathF.PI;
        float upperMean = (float)(luminance / (2 * Math.PI));
        _averageLuminance = 0.5f * (upperMean + MathUtils.Luminance(_ground));
    }

    public Vector3 EvaluateRadiance(Vector3 dirLocal)
    {
        if (dirLocal.Y >= 0f) return Above(dirLocal) * Intensity;

        // Below the horizon: the ground, reached over the first 3° so the
        // horizon has no seam.
        var flat = new Vector3(dirLocal.X, 0f, dirLocal.Z);
        float len = flat.Length();
        flat = len > 1e-6f ? flat / len : Vector3.UnitX;
        float t = MathF.Min(1f, -dirLocal.Y / 0.05f);
        t = t * t * (3f - 2f * t);
        return Vector3.Lerp(Above(flat), _ground, t) * Intensity;
    }

    public float EstimatedAverageLuminance => _averageLuminance * Intensity;

    public bool HasImportanceSampling => false;
    public (Vector3 Direction, Vector3 Radiance, float Pdf) ImportanceSample() => (Vector3.UnitY, Vector3.Zero, 0f);
    public float Pdf(Vector3 dirLocal) => 0f;

    public bool HasAnalyticalSun => true;

    public (Vector3 Direction, Vector3 Radiance, float CosHalfAngle, bool LimbDarkening) AnalyticalSun
        => (SunDirection, _sunRadiance * Intensity, SunCosHalfAngle, true);
}
