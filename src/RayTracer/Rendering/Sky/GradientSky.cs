using System.Numerics;
using RayTracer.Core;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Three-color vertical gradient (zenith → horizon → ground). The legacy
/// stylised sky, kept for backward compatibility and as a fallback when no
/// physical model is needed. Optional analytical sun cap.
///
/// <para>Sky body sampling falls back to the parent's uniform-sphere strategy
/// (no luminance peaks worth importance-sampling). The sun cap is exposed
/// via <see cref="ISkyModel.HasAnalyticalSun"/> so <see cref="SkyEnvironment"/>
/// can register a <see cref="Lights.PhysicalSun"/> alongside it.</para>
/// </summary>
public class GradientSky : ISkyModel
{
    public Vector3 ZenithColor { get; }
    public Vector3 HorizonColor { get; }
    public Vector3 GroundColor { get; }

    private readonly bool _hasSun;
    private readonly Vector3 _sunDir;
    private readonly Vector3 _sunRadiance;
    private readonly float _cosSunHalfAngle;

    public GradientSky(Vector3 zenith, Vector3 horizon, Vector3 ground,
                       Vector3? sunDirToSun = null, Vector3? sunRadiance = null,
                       float sunHalfAngleDeg = 0.265f)
    {
        ZenithColor = zenith;
        HorizonColor = horizon;
        GroundColor = ground;
        if (sunDirToSun.HasValue)
        {
            _hasSun = true;
            _sunDir = Vector3.Normalize(sunDirToSun.Value);
            _sunRadiance = sunRadiance ?? new Vector3(20f);
            _cosSunHalfAngle = MathF.Cos(MathUtils.DegreesToRadians(MathF.Max(0.01f, sunHalfAngleDeg)));
        }
    }

    public Vector3 EvaluateRadiance(Vector3 dirLocal)
    {
        float y = dirLocal.Y;
        if (y >= 0f)
        {
            float t = MathF.Sqrt(MathF.Min(y, 1f));
            return Vector3.Lerp(HorizonColor, ZenithColor, t);
        }
        float tg = MathF.Min(-y * 4f, 1f);
        return Vector3.Lerp(HorizonColor, GroundColor, tg);
    }

    public float EstimatedAverageLuminance
    {
        get
        {
            float zL = MathUtils.Luminance(ZenithColor);
            float hL = MathUtils.Luminance(HorizonColor);
            float gL = MathUtils.Luminance(GroundColor);
            return (zL + 2f * hL + gL) * 0.25f;
        }
    }

    public bool HasImportanceSampling => false;

    public (Vector3 Direction, Vector3 Radiance, float Pdf) ImportanceSample()
        => (Vector3.UnitY, Vector3.Zero, 0f);

    public float Pdf(Vector3 dirLocal) => 0f;

    public bool HasAnalyticalSun => _hasSun;

    public (Vector3 Direction, Vector3 Radiance, float CosHalfAngle, bool LimbDarkening) AnalyticalSun
        => (_sunDir, _sunRadiance, _cosSunHalfAngle, /*limbDarkening*/ false);
}
