using System.Numerics;
using RayTracer.Core;
using RayTracer.Textures;

namespace RayTracer.Rendering.Sky;

/// <summary>
/// Image-based environment sky: wraps an <see cref="EnvironmentMap"/> as an
/// <see cref="ISkyModel"/>. Equirectangular HDR captures (.hdr / .exr) drive
/// IBL with importance-sampled NEE through the environment map's
/// luminance-weighted CDF.
///
/// <para>When the optional <c>extracted_sun</c> parameters are provided (set
/// by the <see cref="HdriSunExtractor"/>), the wrapper exposes them via
/// <see cref="AnalyticalSun"/> so the consumer can spawn a paired
/// <see cref="Lights.PhysicalSun"/> for clean shadows and lower variance —
/// the HDRI pixels covered by the sun have already been in-painted on the
/// environment map so they don't double-contribute.</para>
/// </summary>
public class HdriSky : ISkyModel
{
    public EnvironmentMap Map { get; }

    private readonly bool _hasExtractedSun;
    private readonly Vector3 _extractedSunDir;
    private readonly Vector3 _extractedSunRadiance;
    private readonly float _extractedCosHalfAngle;

    public HdriSky(EnvironmentMap map,
                   Vector3? extractedSunDir = null,
                   Vector3? extractedSunRadiance = null,
                   float extractedSunHalfAngleDeg = 0.265f)
    {
        Map = map;
        if (extractedSunDir.HasValue && extractedSunRadiance.HasValue)
        {
            _hasExtractedSun = true;
            _extractedSunDir = Vector3.Normalize(extractedSunDir.Value);
            _extractedSunRadiance = extractedSunRadiance.Value;
            _extractedCosHalfAngle = MathF.Cos(MathUtils.DegreesToRadians(MathF.Max(0.01f, extractedSunHalfAngleDeg)));
        }
    }

    public Vector3 EvaluateRadiance(Vector3 dirLocal) => Map.Sample(dirLocal);

    public float EstimatedAverageLuminance => Map.EstimatedAverageLuminance;

    public bool HasImportanceSampling => true;

    public (Vector3 Direction, Vector3 Radiance, float Pdf) ImportanceSample()
    {
        var (dir, pdf) = Map.SampleDirection();
        return (dir, Map.Sample(dir), pdf);
    }

    public float Pdf(Vector3 dirLocal) => Map.PdfDirection(dirLocal);

    public bool HasAnalyticalSun => _hasExtractedSun;

    public (Vector3 Direction, Vector3 Radiance, float CosHalfAngle, bool LimbDarkening) AnalyticalSun
        => (_extractedSunDir, _extractedSunRadiance, _extractedCosHalfAngle, /*limbDarkening*/ false);
}
